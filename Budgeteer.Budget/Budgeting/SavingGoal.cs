namespace Budgeteer.Budget.Budgeting;

/// <summary>
/// A financial / saving goal: a target amount to reach, optionally by a date, optionally backed
/// by a linked account (progress = that account's balance) or a manually tracked amount.
/// Plain Marten document.
/// </summary>
public class SavingGoal
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }

    /// <summary>When set, progress tracks this account's balance; otherwise <see cref="ManualAmount"/>.</summary>
    public string? LinkedAccountId { get; set; }
    public decimal ManualAmount { get; set; }

    public DateTime CreatedAt { get; set; }
}
