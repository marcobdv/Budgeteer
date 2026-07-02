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
    private static DocumentStore CreateStore() => DocumentStore.For(opts =>
    {
        opts.Connection(TestPostgres.ConnectionString);
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
        TestPostgres.SkipUnlessAvailable();

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var categorizer = new TransactionCategorizer(store);
        await categorizer.SeedDefaultsAsync();
        var handler = new TransactionEventHandler(categorizer);
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

    [SkippableFact]
    public async Task Multi_account_export_only_imports_rows_matching_the_target_accounts_iban()
    {
        TestPostgres.SkipUnlessAvailable();

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var categorizer = new TransactionCategorizer(store);
        var handler = new TransactionEventHandler(categorizer);
        var transfers = new TransferDetectionService(store);
        var importer = new BankImportService(store, handler, categorizer, transfers);

        // Rabobank's combined download carries several accounts in one file.
        var mutations = new BankStatementImporter().Parse(Encoding.UTF8.GetBytes(Samples.RabobankMultiAccountCsv));
        Assert.Equal(3, mutations.Count);

        var checkingId = await importer.FindOrCreateAccountByIbanAsync("NL11RABO0123456789", "Checking", "Checking");
        var savingsId = await importer.FindOrCreateAccountByIbanAsync("NL44RABO0999999999", "Savings", "Savings");

        // Importing the whole file into each account must only land that account's rows.
        var checkingResult = await importer.ImportAsync(checkingId, mutations);
        Assert.Equal(2, checkingResult.Imported);
        Assert.Equal(1, checkingResult.SkippedOtherAccount);

        var savingsResult = await importer.ImportAsync(savingsId, mutations);
        Assert.Equal(1, savingsResult.Imported);
        Assert.Equal(2, savingsResult.SkippedOtherAccount);

        await using var q = store.QuerySession();
        var checking = await q.Query<AccountSummary>().Where(a => a.Id == checkingId).SingleAsync();
        Assert.Equal(2, checking.TransactionCount);
        Assert.Equal(1487.50m, checking.Balance); // -12,50 + 1500,00
        var savings = await q.Query<AccountSummary>().Where(a => a.Id == savingsId).SingleAsync();
        Assert.Equal(1, savings.TransactionCount);
        Assert.Equal(250.00m, savings.Balance);
    }

    [SkippableFact]
    public async Task Multi_account_export_into_an_ibanless_account_is_refused()
    {
        TestPostgres.SkipUnlessAvailable();

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var categorizer = new TransactionCategorizer(store);
        var handler = new TransactionEventHandler(categorizer);
        var transfers = new TransferDetectionService(store);
        var importer = new BankImportService(store, handler, categorizer, transfers);

        string accountId;
        await using (var session = store.LightweightSession())
        {
            var evt = Budgeteer.Accounts.Domain.Account.Create("Manual", "Checking", 0m);
            session.Events.StartStream<Budgeteer.Accounts.Domain.Account>(evt.AccountId, evt);
            await session.SaveChangesAsync();
            accountId = evt.AccountId;
        }

        var mutations = new BankStatementImporter().Parse(Encoding.UTF8.GetBytes(Samples.RabobankMultiAccountCsv));
        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportAsync(accountId, mutations));
    }
}
