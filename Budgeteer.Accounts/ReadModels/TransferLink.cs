namespace Budgeteer.Accounts.ReadModels;

/// <summary>
/// Records that two transactions form a transfer between the user's own accounts.
/// Plain Marten document produced by transfer detection; used to exclude both legs
/// from income/expense totals and spending breakdowns.
/// </summary>
public class TransferLink
{
    /// <summary>Deterministic id from the (sorted) transaction id pair, so detection is idempotent.</summary>
    public string Id { get; set; } = string.Empty;
    public string FromTransactionId { get; set; } = string.Empty; // the outgoing (negative) leg
    public string ToTransactionId { get; set; } = string.Empty;   // the incoming (positive) leg
    public string FromAccountId { get; set; } = string.Empty;
    public string ToAccountId { get; set; } = string.Empty;
    public decimal Amount { get; set; } // positive magnitude
    public DateTime Date { get; set; }
}
