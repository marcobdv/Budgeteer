using Budgeteer.Shared.Events.Accounts;
using Marten;
using Marten.Events.Projections;

namespace Budgeteer.Accounts.ReadModels;

/// <summary>
/// Flat, queryable read model: one document per recorded transaction.
/// Maintained inline by <see cref="TransactionViewProjection"/> so the transaction list and
/// dashboard can query documents directly instead of replaying the whole event store.
/// The current budget category is joined in at query time from the Expense/Income snapshots,
/// keeping this projection a pure, single-event write.
/// </summary>
public class TransactionView
{
    public string Id { get; set; } = string.Empty; // = TransactionId
    public string AccountId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Payee { get; set; }
    public decimal Amount { get; set; }
    public string? CounterpartyIban { get; set; }
    public string? ImportKey { get; set; }
}

/// <summary>
/// Projects each <see cref="TransactionRecorded"/> event into a <see cref="TransactionView"/> document.
/// </summary>
public class TransactionViewProjection : EventProjection
{
    public TransactionView Create(TransactionRecorded e) => new()
    {
        Id = e.TransactionId,
        AccountId = e.AccountId,
        Date = e.TransactionDate,
        Description = e.Description,
        Payee = e.Payee,
        Amount = e.Amount,
        CounterpartyIban = e.CounterpartyIban,
        ImportKey = e.ImportKey
    };

    // Remove the row when its transaction is deleted.
    public void Project(TransactionDeleted e, IDocumentOperations ops) =>
        ops.Delete<TransactionView>(e.TransactionId);
}
