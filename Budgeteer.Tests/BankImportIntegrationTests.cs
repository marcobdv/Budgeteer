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
/// Exercises the real BankImportService end-to-end: atomic import (account + budget events in one
/// transaction), in-import categorization, inline read models, and dedup on re-import.
/// </summary>
public class BankImportIntegrationTests
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
        opts.DatabaseSchemaName = "import_svc_it";
        opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
        opts.Projections.Snapshot<AccountSummary>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<ExpenseView>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<Income>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Add(new TransactionViewProjection(),
            JasperFx.Events.Projections.ProjectionLifecycle.Inline);
    });

    [SkippableFact]
    public async Task Import_commits_account_and_budget_atomically_and_categorizes()
    {
        Skip.IfNot(Pg(), "Local PostgreSQL not available.");

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var categorizer = new TransactionCategorizer(store);
        await categorizer.SeedDefaultsAsync();
        var handler = new TransactionEventHandler(store, categorizer);
        var transfers = new TransferDetectionService(store);
        var importer = new BankImportService(store, handler, categorizer, transfers);

        var mutations = new BankStatementImporter().Parse(Encoding.UTF8.GetBytes(Samples.RabobankCsv));
        var accountId = await importer.FindOrCreateAccountByIbanAsync("NL11RABO0123456789", "Rabo", "Checking");

        var result = await importer.ImportAsync(accountId, mutations);
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);

        await using (var q = store.QuerySession())
        {
            // Account read model balance: -12,50 + 1500,00 = 1487,50
            var acc = await q.Query<AccountSummary>().Where(a => a.Id == accountId).SingleAsync();
            Assert.Equal(1487.50m, acc.Balance);
            Assert.Equal(2, acc.TransactionCount);

            // Budget read models were written in the same import and auto-categorized.
            var expense = await q.Query<ExpenseView>().SingleAsync();
            Assert.Equal("Groceries", expense.Category); // Albert Heijn
            var income = await q.Query<Income>().SingleAsync();
            Assert.Equal("Salary", income.Category);       // Salaris
            Assert.Equal(2, (await q.Query<TransactionView>().ToListAsync()).Count);
        }

        // Re-importing the same file is fully deduplicated.
        var again = await importer.ImportAsync(accountId, mutations);
        Assert.Equal(0, again.Imported);
        Assert.Equal(2, again.Skipped);
    }
}
