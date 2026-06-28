namespace Budgeteer.Accounts.ReadModels;

/// <summary>
/// Records the transactions created by a single CSV import, so an import can be undone as a unit.
/// Plain Marten document written by the import service.
/// </summary>
public class ImportBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
    public List<string> TransactionIds { get; set; } = new();
}
