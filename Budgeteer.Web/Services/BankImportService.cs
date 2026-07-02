using Budgeteer.Accounts;
using Budgeteer.Accounts.Domain;
using Budgeteer.Accounts.Import;
using Budgeteer.Accounts.ReadModels;
using Budgeteer.Budget.Categorization;
using Budgeteer.Budget.EventHandlers;
using Marten;

namespace Budgeteer.Web.Services;

/// <summary>
/// Orchestrates importing parsed bank mutations into the event store:
/// matches/creates the target account, appends <c>TransactionRecorded</c> events
/// (skipping duplicates) and the corresponding Budget-domain events in one transaction.
/// </summary>
public sealed class BankImportService
{
    private readonly IDocumentStore _store;
    private readonly TransactionEventHandler _budgetHandler;
    private readonly TransactionCategorizer _categorizer;
    private readonly TransferDetectionService _transfers;

    public BankImportService(IDocumentStore store, TransactionEventHandler budgetHandler,
        TransactionCategorizer categorizer, TransferDetectionService transfers)
    {
        _store = store;
        _budgetHandler = budgetHandler;
        _categorizer = categorizer;
        _transfers = transfers;
    }

    /// <summary>Loads all accounts from the inline <c>AccountSummary</c> read model.</summary>
    public async Task<List<AccountSummary>> LoadAccountsAsync()
    {
        await using var session = _store.QuerySession();
        return (await session.Query<AccountSummary>().ToListAsync()).ToList();
    }

    /// <summary>
    /// Finds an existing account by IBAN (case/space-insensitive) or creates a new one.
    /// Returns the account id (stream key).
    /// </summary>
    public async Task<string> FindOrCreateAccountByIbanAsync(string iban, string name, string accountType)
    {
        var target = Iban.From(iban);
        var accounts = await LoadAccountsAsync();
        var existing = accounts.FirstOrDefault(a => !target.IsEmpty && Iban.From(a.Iban) == target);
        if (existing != null)
            return existing.Id;

        await using var session = _store.LightweightSession();
        var evt = Account.Create(name, accountType, initialBalance: 0m, iban: iban);
        session.Events.StartStream<Account>(evt.AccountId, evt);
        await session.SaveChangesAsync();
        return evt.AccountId;
    }

    /// <summary>
    /// Appends the given mutations to the account stream, skipping any whose dedup key
    /// was already imported. Each newly recorded transaction is forwarded to the Budget domain.
    /// </summary>
    public async Task<ImportResult> ImportAsync(string accountId, IReadOnlyList<BankMutation> mutations)
    {
        // Load categorization rules once, not once per transaction.
        var rules = await _categorizer.GetRulesAsync();

        await using var session = _store.LightweightSession();
        var account = await session.Events.AggregateStreamAsync<Account>(accountId)
            ?? throw new InvalidOperationException($"Account '{accountId}' was not found.");

        // A combined export (e.g. Rabobank's all-accounts download) carries rows for several
        // IBANs; only the target account's rows may land on its stream, or another account's
        // transactions silently corrupt its balance. Without an IBAN on the target account
        // there is nothing to match rows on, so a multi-account file must be refused.
        var fileIbans = mutations.Select(m => m.AccountIban).Where(i => !i.IsEmpty).Distinct().ToList();
        if (account.Iban.IsEmpty && fileIbans.Count > 1)
            throw new InvalidOperationException(
                $"This file contains rows for {fileIbans.Count} accounts ({string.Join(", ", fileIbans)}), " +
                "but the selected account has no IBAN to match rows against. " +
                "Import into an account with a matching IBAN, one account at a time.");

        var alreadyImported = account.ImportKeys;
        int skipped = 0;
        int skippedOtherAccount = 0;
        var importedIds = new List<string>();

        // Guard against duplicates within the same file as well as across imports.
        var seenInThisBatch = new HashSet<string>();

        foreach (var m in mutations)
        {
            if (!m.AccountIban.IsEmpty && !account.Iban.IsEmpty && m.AccountIban != account.Iban)
            {
                skippedOtherAccount++;
                continue;
            }

            if (alreadyImported.Contains(m.DedupKey) || !seenInThisBatch.Add(m.DedupKey))
            {
                skipped++;
                continue;
            }

            var evt = account.RecordTransaction(
                description: m.Description,
                amount: m.Amount,
                transactionDate: m.Date,
                payee: m.CounterpartyName,
                importKey: m.DedupKey,
                counterpartyIban: m.CounterpartyIban);

            // Append the account event AND the budget event in the same session so they commit
            // atomically — a failure can't leave a transaction without its expense/income.
            session.Events.Append(accountId, evt);
            var category = TransactionCategorizer.Match(rules, evt.Payee, evt.Description, evt.Amount);
            _budgetHandler.Project(session, evt, category);
            importedIds.Add(evt.TransactionId);
        }

        int imported = importedIds.Count;
        if (imported > 0)
        {
            // Record the batch so the import can be undone as a unit.
            session.Store(new ImportBatch
            {
                AccountId = accountId,
                AccountName = account.Name,
                ImportedAt = DateTime.UtcNow,
                TransactionIds = importedIds
            });
            await session.SaveChangesAsync();
        }

        // Detect transfers between own accounts now that the new transactions are in the read model.
        if (imported > 0)
            await _transfers.DetectAsync();

        return new ImportResult(Imported: imported, Skipped: skipped, SkippedOtherAccount: skippedOtherAccount);
    }
}

/// <summary>Outcome of an import run.</summary>
public record ImportResult(int Imported, int Skipped, int SkippedOtherAccount = 0);
