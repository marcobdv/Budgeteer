using System.Text;
using Budgeteer.Accounts.Domain;
using Budgeteer.Accounts.Import;
using Budgeteer.Shared.Events.Accounts;
using Marten;
using Npgsql;
using Xunit;

namespace Budgeteer.Tests;

/// <summary>
/// End-to-end tests that import parsed mutations into a real Marten/PostgreSQL event store,
/// reload the account aggregate from events, and assert balances and de-duplication.
///
/// These run only when a local PostgreSQL is reachable (the same one the app uses via Docker).
/// If none is available the tests are skipped so the unit suite stays green without Docker.
/// </summary>
public class ImportIntegrationTests
{
    private static DocumentStore CreateStore() => DocumentStore.For(opts =>
    {
        opts.Connection(TestPostgres.ConnectionString);
        // Isolate test data in its own schema so we never touch app data.
        opts.DatabaseSchemaName = "import_it";
        // Streams are keyed by string ids, matching the app configuration.
        opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
        opts.Events.AddEventTypes(new[]
        {
            typeof(AccountCreated), typeof(TransactionRecorded),
            typeof(Budgeteer.Shared.Events.Budget.ExpenseRecorded),
            typeof(Budgeteer.Shared.Events.Budget.IncomeRecorded)
        });
    });

    [SkippableFact]
    public async Task Importing_a_rabobank_file_records_events_and_rebuilds_balance()
    {
        TestPostgres.SkipUnlessAvailable();

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var importer = new BankStatementImporter();
        var mutations = importer.Parse(Encoding.UTF8.GetBytes(Samples.RabobankCsv));
        Assert.Equal(2, mutations.Count);

        // Create a fresh account and import the mutations as events.
        var created = Account.Create("Rabo test", "Checking", 0m, "NL11RABO0123456789");
        var accountId = created.AccountId;

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Account>(accountId, created);
            foreach (var m in mutations)
            {
                var evt = new Account().RecordTransaction(
                    m.Description, m.Amount, m.Date, m.CounterpartyName, m.DedupKey, m.CounterpartyIban);
                // RecordTransaction reads Id from the aggregate; set AccountId explicitly here.
                session.Events.Append(accountId, evt with { AccountId = accountId });
            }
            await session.SaveChangesAsync();
        }

        // Reload the aggregate from the event stream and verify state.
        await using (var session = store.LightweightSession())
        {
            var account = await session.Events.AggregateStreamAsync<Account>(accountId);
            Assert.NotNull(account);
            Assert.Equal(2, account!.TransactionIds.Count);
            // -12,50 + 1500,00 = 1487,50
            Assert.Equal(1487.50m, account.Balance);
            Assert.All(mutations, m => Assert.True(account.HasImported(m.DedupKey)));
        }
    }

    [SkippableFact]
    public async Task Reimporting_the_same_file_skips_duplicates()
    {
        TestPostgres.SkipUnlessAvailable();

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var importer = new BankStatementImporter();
        var mutations = importer.Parse(Encoding.UTF8.GetBytes(Samples.KnabCsv));

        var created = Account.Create("Knab test", "Checking", 0m, "NL12KNAB0123456789");
        var accountId = created.AccountId;

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Account>(accountId, created);
            await session.SaveChangesAsync();
        }

        // First import: all rows are new.
        var firstRun = await ImportSkippingDuplicates(store, accountId, mutations);
        Assert.Equal(mutations.Count, firstRun);

        // Second import of the same file: every row is a duplicate and must be skipped.
        var secondRun = await ImportSkippingDuplicates(store, accountId, mutations);
        Assert.Equal(0, secondRun);

        await using (var session = store.LightweightSession())
        {
            var account = await session.Events.AggregateStreamAsync<Account>(accountId);
            Assert.Equal(mutations.Count, account!.TransactionIds.Count); // not doubled
        }
    }

    [SkippableFact]
    public async Task Budget_handler_projects_expense_and_income_streams()
    {
        TestPostgres.SkipUnlessAvailable();

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var categorizer = new Budgeteer.Budget.Categorization.TransactionCategorizer(store);
        var handler = new Budgeteer.Budget.EventHandlers.TransactionEventHandler(categorizer);

        // An expense (negative) and an income (positive) transaction.
        var expenseTxn = new TransactionRecorded(
            Guid.NewGuid().ToString(), "acct-1", new DateTime(2024, 1, 15),
            "Boodschappen", -45.30m, "Albert Heijn", DateTime.UtcNow);
        var incomeTxn = new TransactionRecorded(
            Guid.NewGuid().ToString(), "acct-1", new DateTime(2024, 1, 16),
            "Salaris", 2250.00m, "Werkgever BV", DateTime.UtcNow);

        await using (var write = store.LightweightSession())
        {
            await handler.RecordAndProjectAsync(write, "acct-1", expenseTxn);
            await handler.RecordAndProjectAsync(write, "acct-1", incomeTxn);
            await write.SaveChangesAsync();
        }

        await using var session = store.LightweightSession();
        var expenses = await session.Events.QueryAllRawEvents()
            .Where(e => e.DotNetTypeName.Contains("ExpenseRecorded")).ToListAsync();
        var incomes = await session.Events.QueryAllRawEvents()
            .Where(e => e.DotNetTypeName.Contains("IncomeRecorded")).ToListAsync();

        Assert.Contains(expenses, e => ((Budgeteer.Shared.Events.Budget.ExpenseRecorded)e.Data).TransactionId == expenseTxn.TransactionId);
        Assert.Contains(incomes, e => ((Budgeteer.Shared.Events.Budget.IncomeRecorded)e.Data).TransactionId == incomeTxn.TransactionId);
    }

    // Mirrors the dedup logic in Budgeteer.Web BankImportService.ImportAsync.
    private static async Task<int> ImportSkippingDuplicates(
        IDocumentStore store, string accountId, IReadOnlyList<BankMutation> mutations)
    {
        await using var session = store.LightweightSession();
        var account = await session.Events.AggregateStreamAsync<Account>(accountId);
        int imported = 0;
        foreach (var m in mutations)
        {
            if (account!.HasImported(m.DedupKey))
                continue;
            var evt = new Account().RecordTransaction(
                m.Description, m.Amount, m.Date, m.CounterpartyName, m.DedupKey, m.CounterpartyIban);
            session.Events.Append(accountId, evt with { AccountId = accountId });
            imported++;
        }
        if (imported > 0)
            await session.SaveChangesAsync();
        return imported;
    }
}
