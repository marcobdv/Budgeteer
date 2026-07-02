using Budgeteer.Shared.Events.Budget;

namespace Budgeteer.Budget.Domain;

/// <summary>
/// Income aggregate - the read model for an income transaction in the Budget domain.
/// Maintained by Marten as an inline snapshot from the income event stream.
/// </summary>
public class Income
{
    public string Id { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public string? Source { get; set; }
    public DateTime RecordedAt { get; set; }

    public void Apply(IncomeRecorded evt)
    {
        Id = evt.IncomeId;
        TransactionId = evt.TransactionId;
        Date = evt.Date;
        Description = evt.Description;
        Amount = evt.Amount;
        Category = evt.Category;
        Source = evt.Source;
        RecordedAt = evt.RecordedAt;
    }

    public void Apply(IncomeCategorized evt)
    {
        Category = evt.Category;
    }

    // Business logic: recategorize an income (mirrors Expense.Categorize, so a miscategorized
    // salary or refund isn't stuck with its import-time category forever).
    public IncomeCategorized Categorize(string category)
    {
        var evt = new IncomeCategorized(
            IncomeId: Id,
            Category: category,
            CategorizedAt: DateTime.UtcNow
        );

        Apply(evt);
        return evt;
    }
}
