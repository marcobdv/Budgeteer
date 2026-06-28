namespace Budgeteer.Budget.Budgeting;

/// <summary>
/// A user-defined monthly spending limit for a category. Plain Marten document
/// (configuration, not event-sourced — like categorization rules).
/// </summary>
public class BudgetAllocation
{
    /// <summary>Lower-cased category name; doubles as the document id for upserts.</summary>
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal MonthlyLimit { get; set; }
}
