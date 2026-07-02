using Budgeteer.Budget.Categorization;
using Budgeteer.Budget.Domain;
using Budgeteer.Budget.ReadModels;
using Marten;

namespace Budgeteer.Web.Services;

/// <summary>
/// Read/queries over the Budget domain plus the "recategorize and learn" workflow.
/// </summary>
public sealed class BudgetService
{
    private readonly IDocumentStore _store;
    private readonly TransactionCategorizer _categorizer;

    public BudgetService(IDocumentStore store, TransactionCategorizer categorizer)
    {
        _store = store;
        _categorizer = categorizer;
    }

    /// <summary>All expenses, from the inline <c>ExpenseView</c> read model.</summary>
    public async Task<List<ExpenseView>> LoadExpensesAsync()
    {
        await using var session = _store.QuerySession();
        return (await session.Query<ExpenseView>().ToListAsync()).ToList();
    }

    /// <summary>All income, from the inline <c>Income</c> snapshot read model.</summary>
    public async Task<List<Income>> LoadIncomeAsync()
    {
        await using var session = _store.QuerySession();
        return (await session.Query<Income>().ToListAsync()).ToList();
    }

    /// <summary>
    /// Re-categorizes an expense and learns from it, so future imports from the same payee
    /// are auto-categorized the same way.
    /// </summary>
    public async Task RecategorizeAsync(string expenseId, string category)
    {
        await using var session = _store.LightweightSession();
        var expense = await session.Events.AggregateStreamAsync<Expense>(expenseId);
        if (expense is null)
            return;

        var evt = expense.Categorize(category);
        session.Events.Append(expenseId, evt);
        await session.SaveChangesAsync();

        // The smart bit: remember this choice for the payee.
        await _categorizer.LearnAsync(expense.Payee, category);
    }

    /// <summary>
    /// Re-categorizes an income and learns from it, mirroring <see cref="RecategorizeAsync"/> —
    /// incomes are not stuck with the category assigned at import time.
    /// </summary>
    public async Task RecategorizeIncomeAsync(string incomeId, string category)
    {
        await using var session = _store.LightweightSession();
        var income = await session.Events.AggregateStreamAsync<Income>(incomeId);
        if (income is null)
            return;

        var evt = income.Categorize(category);
        session.Events.Append(incomeId, evt);
        await session.SaveChangesAsync();

        await _categorizer.LearnAsync(income.Source, category);
    }

    /// <summary>Spending totals per category (expenses only), highest first.</summary>
    public static IReadOnlyList<(string Category, decimal Total, int Count)> SpendingByCategory(
        IEnumerable<ExpenseView> expenses)
    {
        // Group case-insensitively so 'Groceries' and 'groceries' collapse into one category
        // (downstream BudgetAllocationService keys categories case-insensitively).
        return expenses
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "Uncategorized" : e.Category!.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => (Category: g.Key, Total: g.Sum(e => e.Amount), Count: g.Count()))
            .OrderByDescending(x => x.Total)
            .ToList();
    }
}
