using System.Text;
using Budgeteer.Accounts.ReadModels;
using Budgeteer.Budget.Categorization;
using Budgeteer.Budget.Domain;
using Budgeteer.Budget.EventHandlers;
using Budgeteer.Budget.ReadModels;
using Budgeteer.Accounts.Import;
using Budgeteer.Web.Services;
using Marten;
using Npgsql;
using Xunit;

namespace Budgeteer.Tests;

/// <summary>
/// End-to-end edit/undo/delete: deleting a transaction reverses the balance and removes its budget
/// read models; undoing an import removes its transactions and frees their import keys for re-import.
/// </summary>
public class LedgerIntegrationTests
{
    private const string Conn =
        "Host=localhost;Port=5432;Database=budgeteer;Username=postgres;Password=postgres;Timeout=3;Command Timeout=10";

    private static bool Pg()
    {
        try { using var c = new NpgsqlConnection(Conn); c.Open(); return true; } catch { return false; }
    }

    private static DocumentStore CreateStore() => DocumentStore.For(opts =>
    {
        opts.Connection(Conn);
        opts.DatabaseSchemaName = "ledger_it";
        opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
        opts.Projections.Snapshot<AccountSummary>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<ExpenseView>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<Income>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Add(new TransactionViewProjection(),
            JasperFx.Events.Projections.ProjectionLifecycle.Inline);
    });

    private static (BankImportService import, LedgerService ledger) Build(DocumentStore store)
    {
        var categorizer = new TransactionCategorizer(store);
        var handler = new TransactionEventHandler(categorizer);
        var transfers = new TransferDetectionService(store);
        return (new BankImportService(store, handler, categorizer, transfers),
                new LedgerService(store, categorizer, handler, transfers));
    }

    [SkippableFact]
    public async Task Deleting_a_transaction_reverses_balance_and_removes_its_budget_view()
    {
        Skip.IfNot(Pg(), "Local PostgreSQL not available.");

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
        await new TransactionCategorizer(store).SeedDefaultsAsync();
        var (import, ledger) = Build(store);

        var mutations = new BankStatementImporter().Parse(Encoding.UTF8.GetBytes(Samples.RabobankCsv));
        var accountId = await import.FindOrCreateAccountByIbanAsync("NL11RABO0123456789", "Rabo", "Checking");
        await import.ImportAsync(accountId, mutations); // -12.50 expense + 1500 income => 1487.50

        string expenseTxnId;
        await using (var q = store.QuerySession())
        {
            expenseTxnId = (await q.Query<TransactionView>().Where(v => v.Amount < 0).ToListAsync()).Single().Id;
        }

        await ledger.DeleteTransactionAsync(expenseTxnId);

        await using (var q = store.QuerySession())
        {
            var acc = await q.Query<AccountSummary>().Where(a => a.Id == accountId).SingleAsync();
            Assert.Equal(1500m, acc.Balance);          // -12.50 reversed
            Assert.Equal(1, acc.TransactionCount);
            Assert.Empty(await q.Query<ExpenseView>().ToListAsync()); // budget view removed
            Assert.Single(await q.Query<TransactionView>().ToListAsync());
        }
    }

    [SkippableFact]
    public async Task Undoing_an_import_removes_its_transactions_and_frees_keys_for_reimport()
    {
        Skip.IfNot(Pg(), "Local PostgreSQL not available.");

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
        await new TransactionCategorizer(store).SeedDefaultsAsync();
        var (import, ledger) = Build(store);

        var mutations = new BankStatementImporter().Parse(Encoding.UTF8.GetBytes(Samples.RabobankCsv));
        var accountId = await import.FindOrCreateAccountByIbanAsync("NL11RABO0123456789", "Rabo", "Checking");
        await import.ImportAsync(accountId, mutations);

        var batch = await ledger.GetLastImportAsync();
        Assert.NotNull(batch);
        var removed = await ledger.UndoImportAsync(batch!.Id);
        Assert.Equal(2, removed);

        await using (var q = store.QuerySession())
        {
            var acc = await q.Query<AccountSummary>().Where(a => a.Id == accountId).SingleAsync();
            Assert.Equal(0m, acc.Balance);
            Assert.Empty(await q.Query<TransactionView>().ToListAsync());
        }

        // Keys were freed, so the same file imports cleanly again (not skipped as duplicates).
        var again = await import.ImportAsync(accountId, mutations);
        Assert.Equal(2, again.Imported);
        Assert.Equal(0, again.Skipped);
    }

    [SkippableFact]
    public async Task Editing_an_imported_transaction_keeps_its_dedup_key_so_reimport_skips_it()
    {
        Skip.IfNot(Pg(), "Local PostgreSQL not available.");

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
        await new TransactionCategorizer(store).SeedDefaultsAsync();
        var (import, ledger) = Build(store);

        var mutations = new BankStatementImporter().Parse(Encoding.UTF8.GetBytes(Samples.RabobankCsv));
        var accountId = await import.FindOrCreateAccountByIbanAsync("NL11RABO0123456789", "Rabo", "Checking");
        await import.ImportAsync(accountId, mutations); // 2 rows imported

        string expenseTxnId;
        await using (var q = store.QuerySession())
        {
            expenseTxnId = (await q.Query<TransactionView>().Where(v => v.Amount < 0).ToListAsync()).Single().Id;
        }

        // Edit the imported expense (e.g. fix the description).
        await ledger.ReplaceTransactionAsync(expenseTxnId, "Groceries (corrected)", -12.50m,
            new DateTime(2024, 1, 15), "Albert Heijn 1234");

        // Re-importing the same export must skip both rows — the edit preserved the import key.
        var again = await import.ImportAsync(accountId, mutations);
        Assert.Equal(0, again.Imported);
        Assert.Equal(2, again.Skipped);

        await using (var q = store.QuerySession())
        {
            // No duplicate row crept in; the balance is unchanged at -12.50 + 1500.
            Assert.Equal(2, await q.Query<TransactionView>().CountAsync());
            var acc = await q.Query<AccountSummary>().Where(a => a.Id == accountId).SingleAsync();
            Assert.Equal(1487.50m, acc.Balance);
        }
    }
}
