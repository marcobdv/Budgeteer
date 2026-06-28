using Budgeteer.Accounts.Domain;
using Budgeteer.Accounts.ReadModels;
using Budgeteer.Budget.Categorization;
using Budgeteer.Budget.Domain;
using Budgeteer.Budget.EventHandlers;
using Budgeteer.Budget.ReadModels;
using Budgeteer.Shared.Events.Accounts;
using Marten;

namespace Budgeteer.Web.Services;

/// <summary>
/// Edit/undo/delete operations over the ledger. Deleting a transaction appends a
/// <see cref="TransactionDeleted"/> event (reverses the balance, frees the import key) and removes
/// the derived budget read models + any transfer link, all in one transaction.
/// </summary>
public sealed class LedgerService
{
    private readonly IDocumentStore _store;
    private readonly TransactionCategorizer _categorizer;
    private readonly TransactionEventHandler _budgetHandler;
    private readonly TransferDetectionService _transfers;

    public LedgerService(IDocumentStore store, TransactionCategorizer categorizer,
        TransactionEventHandler budgetHandler, TransferDetectionService transfers)
    {
        _store = store;
        _categorizer = categorizer;
        _budgetHandler = budgetHandler;
        _transfers = transfers;
    }

    public async Task DeleteTransactionAsync(string transactionId)
    {
        await using var session = _store.LightweightSession();
        var view = await session.LoadAsync<TransactionView>(transactionId);
        if (view is null) return;

        await RemoveTransactionsAsync(session, new[] { view });
        await session.SaveChangesAsync();
    }

    public async Task DeleteAccountAsync(string accountId)
    {
        await using var session = _store.LightweightSession();
        var views = await session.Query<TransactionView>().Where(v => v.AccountId == accountId).ToListAsync();
        var ids = views.Select(v => v.Id).ToList();

        // Drop the account stream and its read model wholesale, plus all derived budget/transfer docs.
        session.Events.ArchiveStream(accountId);
        session.Delete<AccountSummary>(accountId);
        foreach (var v in views)
            session.Delete<TransactionView>(v.Id);
        await RemoveBudgetAndTransfersAsync(session, ids);

        // Drop the account's import batches too, otherwise the Import page's "undo last import"
        // affordance keeps pointing at a deleted account and undoes nothing.
        var batches = await session.Query<ImportBatch>().Where(b => b.AccountId == accountId).ToListAsync();
        foreach (var b in batches)
            session.Delete<ImportBatch>(b.Id);

        await session.SaveChangesAsync();
    }

    /// <summary>The most recent import batch (for the "undo last import" affordance).</summary>
    public async Task<ImportBatch?> GetLastImportAsync()
    {
        await using var session = _store.QuerySession();
        return (await session.Query<ImportBatch>().OrderByDescending(b => b.ImportedAt).Take(1).ToListAsync())
            .FirstOrDefault();
    }

    /// <summary>Undoes an import batch, deleting exactly the transactions it created.</summary>
    public async Task<int> UndoImportAsync(string batchId)
    {
        await using var session = _store.LightweightSession();
        var batch = await session.LoadAsync<ImportBatch>(batchId);
        if (batch is null) return 0;

        var views = await session.Query<TransactionView>()
            .Where(v => batch.TransactionIds.Contains(v.Id)).ToListAsync();
        await RemoveTransactionsAsync(session, views);
        session.Delete<ImportBatch>(batchId);
        await session.SaveChangesAsync();
        return views.Count;
    }

    /// <summary>
    /// Edits a transaction by deleting the old one and recording a replacement (which is
    /// re-categorized from scratch). Done in one transaction so the balance stays correct.
    /// </summary>
    public async Task ReplaceTransactionAsync(
        string transactionId, string description, decimal amount, DateTime date, string? payee)
    {
        await using var session = _store.LightweightSession();
        var view = await session.LoadAsync<TransactionView>(transactionId);
        if (view is null) return;

        await RemoveTransactionsAsync(session, new[] { view });

        var rules = await _categorizer.GetRulesAsync();
        var account = await session.Events.AggregateStreamAsync<Account>(view.AccountId);
        if (account is null) return;

        // Preserve the import dedup key and counterparty IBAN from the original transaction, so an
        // edit doesn't (a) free the key and let a re-import duplicate the row, or (b) drop the IBAN
        // that transfer detection needs to re-pair the two legs of an internal transfer.
        var evt = account.RecordTransaction(
            description, amount, date, payee,
            importKey: view.ImportKey, counterpartyIban: view.CounterpartyIban);
        session.Events.Append(view.AccountId, evt);
        var category = TransactionCategorizer.Match(rules, payee, description, amount);
        _budgetHandler.Project(session, evt, category);
        await session.SaveChangesAsync();

        // The replacement may complete (or break) a transfer pairing.
        await _transfers.DetectAsync();
    }

    // Appends a TransactionDeleted to each transaction's account stream and removes derived docs.
    private async Task RemoveTransactionsAsync(IDocumentSession session, IReadOnlyCollection<TransactionView> views)
    {
        foreach (var v in views)
        {
            session.Events.Append(v.AccountId,
                new TransactionDeleted(v.Id, v.AccountId, v.Amount, v.ImportKey, DateTime.UtcNow));
        }
        await RemoveBudgetAndTransfersAsync(session, views.Select(v => v.Id).ToList());
    }

    // Archives the budget streams for the given transactions and deletes their read models + transfer links.
    private static async Task RemoveBudgetAndTransfersAsync(IDocumentSession session, List<string> transactionIds)
    {
        if (transactionIds.Count == 0) return;

        var expenses = await session.Query<ExpenseView>()
            .Where(e => transactionIds.Contains(e.TransactionId)).ToListAsync();
        foreach (var e in expenses)
        {
            session.Events.ArchiveStream(e.Id); // so a projection rebuild won't resurrect it
            session.Delete<ExpenseView>(e.Id);
        }

        var incomes = await session.Query<Income>()
            .Where(i => transactionIds.Contains(i.TransactionId)).ToListAsync();
        foreach (var i in incomes)
        {
            session.Events.ArchiveStream(i.Id);
            session.Delete<Income>(i.Id);
        }

        var links = await session.Query<TransferLink>()
            .Where(l => transactionIds.Contains(l.FromTransactionId) || transactionIds.Contains(l.ToTransactionId))
            .ToListAsync();
        foreach (var l in links)
            session.Delete<TransferLink>(l.Id);
    }
}
