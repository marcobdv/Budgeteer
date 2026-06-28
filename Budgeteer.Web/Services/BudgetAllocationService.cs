using Budgeteer.Budget.Budgeting;
using Marten;

namespace Budgeteer.Web.Services;

/// <summary>
/// Budget-vs-actual for one category. The configured limit is monthly; <see cref="Limit"/> scales
/// it to the number of months in the inspected period so the comparison against <see cref="Spent"/>
/// (also summed over that period) is apples-to-apples.
/// </summary>
public record CategoryBudget(string Category, decimal MonthlyLimit, decimal Spent, int Months)
{
    /// <summary>The monthly limit scaled to the inspected period.</summary>
    public decimal Limit => MonthlyLimit * Math.Max(1, Months);
    public decimal Remaining => Limit - Spent;
    public bool HasLimit => MonthlyLimit > 0;
    /// <summary>Spend as a percentage of the (period-scaled) limit, capped for display at 999.</summary>
    public double Percent => Limit > 0 ? Math.Min(999, (double)(Spent / Limit) * 100) : 0;
    public bool OverBudget => Limit > 0 && Spent > Limit;
    public bool NearLimit => Limit > 0 && !OverBudget && Spent >= Limit * 0.8m;
}

/// <summary>Manages per-category monthly budget limits and computes budget-vs-actual rows.</summary>
public sealed class BudgetAllocationService
{
    private readonly IDocumentStore _store;

    public BudgetAllocationService(IDocumentStore store) => _store = store;

    public async Task<List<BudgetAllocation>> GetAllAsync()
    {
        await using var session = _store.QuerySession();
        return (await session.Query<BudgetAllocation>().ToListAsync()).ToList();
    }

    /// <summary>Upserts a monthly limit for a category. A limit of 0 or less removes it.</summary>
    public async Task SetAsync(string category, decimal monthlyLimit)
    {
        if (string.IsNullOrWhiteSpace(category))
            return;
        var id = category.Trim().ToLowerInvariant();

        await using var session = _store.LightweightSession();
        if (monthlyLimit <= 0)
        {
            session.Delete<BudgetAllocation>(id);
        }
        else
        {
            session.Store(new BudgetAllocation { Id = id, Category = category.Trim(), MonthlyLimit = monthlyLimit });
        }
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Joins per-category spending with the configured limits. Categories appear if they have
    /// either spending or a limit; ordered by spend descending. <paramref name="periodMonths"/> is
    /// the number of months covered by the inspected period, used to scale the monthly limits.
    /// All keys are matched case-insensitively (and deduped) so case-variant categories can't clash.
    /// </summary>
    public static IReadOnlyList<CategoryBudget> BuildBudgetRows(
        IEnumerable<(string Category, decimal Total, int Count)> spending,
        IEnumerable<BudgetAllocation> allocations,
        int periodMonths = 1)
    {
        var limits = allocations
            .GroupBy(a => a.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().MonthlyLimit, StringComparer.OrdinalIgnoreCase);

        // GroupBy (not ToDictionary) so duplicate case-variant categories can't throw.
        var spendByCat = spending
            .GroupBy(s => s.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Total), StringComparer.OrdinalIgnoreCase);

        var categories = spendByCat.Keys
            .Concat(limits.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var months = Math.Max(1, periodMonths);
        return categories
            .Select(c => new CategoryBudget(
                c,
                limits.TryGetValue(c, out var lim) ? lim : 0m,
                spendByCat.TryGetValue(c, out var sp) ? sp : 0m,
                months))
            .OrderByDescending(r => r.Spent)
            .ThenByDescending(r => r.MonthlyLimit)
            .ToList();
    }
}
