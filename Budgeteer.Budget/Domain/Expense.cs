using Budgeteer.Shared.Events.Budget;

namespace Budgeteer.Budget.Domain;

/// <summary>
/// Expense aggregate - represents a categorized expense
/// Created in response to Account.TransactionRecorded events
/// </summary>
public class Expense
{
    public string Id { get; set; } = string.Empty;
    public string TransactionId { get; private set; } = string.Empty;
    public DateTime Date { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string? Category { get; private set; }
    public string? Payee { get; private set; }
    public DateTime RecordedAt { get; private set; }

    // For Marten event sourcing
    public void Apply(ExpenseRecorded evt)
    {
        Id = evt.ExpenseId;
        TransactionId = evt.TransactionId;
        Date = evt.Date;
        Description = evt.Description;
        Amount = evt.Amount;
        Category = evt.Category;
        Payee = evt.Payee;
        RecordedAt = evt.RecordedAt;
    }

    public void Apply(ExpenseCategorized evt)
    {
        Category = evt.Category;
    }

    // Factory method - creates expense from account transaction
    public static ExpenseRecorded CreateFromTransaction(
        string transactionId,
        DateTime date,
        string description,
        decimal amount,
        string? payee = null,
        string? category = null)
    {
        return new ExpenseRecorded(
            ExpenseId: Guid.NewGuid().ToString(),
            TransactionId: transactionId,
            Date: date,
            Description: description,
            Amount: Math.Abs(amount), // Expenses are always positive
            Category: category,
            Payee: payee,
            RecordedAt: DateTime.UtcNow
        );
    }

    // Business logic: Categorize expense
    public ExpenseCategorized Categorize(string category)
    {
        var evt = new ExpenseCategorized(
            ExpenseId: Id,
            Category: category,
            CategorizedAt: DateTime.UtcNow
        );

        Apply(evt);
        return evt;
    }
}
