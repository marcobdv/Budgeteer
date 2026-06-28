using Budgeteer.Shared.Events.Budget;

namespace Budgeteer.Budget.ReadModels;

/// <summary>
/// Read model for an expense, maintained as an inline Marten snapshot from the expense's
/// event stream. Kept separate from the <see cref="Domain.Expense"/> aggregate (which carries
/// command/factory methods) so it is a clean projection target: only <c>Apply</c> methods.
/// Re-categorizations (<see cref="ExpenseCategorized"/>) flow in automatically.
/// </summary>
public class ExpenseView
{
    public string Id { get; set; } = string.Empty; // = expense stream key
    public string TransactionId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public string? Payee { get; set; }

    public void Apply(ExpenseRecorded e)
    {
        Id = e.ExpenseId;
        TransactionId = e.TransactionId;
        Date = e.Date;
        Description = e.Description;
        Amount = e.Amount;
        Category = e.Category;
        Payee = e.Payee;
    }

    public void Apply(ExpenseCategorized e) => Category = e.Category;
}
