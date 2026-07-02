using Budgeteer.Accounts.Domain;
using Budgeteer.Accounts.ReadModels;
using Budgeteer.Budget.Categorization;
using Budgeteer.Budget.Domain;
using Budgeteer.Budget.EventHandlers;
using Budgeteer.Budget.ReadModels;
using Marten;
using Npgsql;
using Xunit;

namespace Budgeteer.Tests;

/// <summary>
/// Verifies the inline Marten projections (read models): appending events updates the
/// queryable Account / Expense / Income snapshots and the flat TransactionView, with no
/// manual event replay — including re-categorization flowing into the Expense snapshot.
/// </summary>
public class ProjectionTests
{
    private const string Conn =
        "Host=localhost;Port=5432;Database=budgeteer;Username=postgres;Password=postgres;Timeout=3;Command Timeout=10";

    private static bool Pg()
    {
        try { using var c = new NpgsqlConnection(Conn); c.Open(); return true; } catch { return false; }
    }

    // Mirrors the app's Marten projection registration.
    private static DocumentStore CreateStore() => DocumentStore.For(opts =>
    {
        opts.Connection(Conn);
        opts.DatabaseSchemaName = "proj_it";
        opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
        opts.Projections.Snapshot<AccountSummary>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<ExpenseView>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<Income>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Add(new TransactionViewProjection(),
            JasperFx.Events.Projections.ProjectionLifecycle.Inline);
    });

    [SkippableFact]
    public async Task Inline_projections_maintain_all_read_models()
    {
        Skip.IfNot(Pg(), "Local PostgreSQL not available.");

        await using var store = CreateStore();
        // Deterministic start so Single() assertions hold across runs.
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var categorizer = new TransactionCategorizer(store);
        await categorizer.SeedDefaultsAsync();
        var handler = new TransactionEventHandler(categorizer);

        var created = Account.Create("Checking", "Checking", 0m, "NL11RABO0123456789");
        var account = new Account();
        account.Apply(created); // sets Id so RecordTransaction stamps the right AccountId
        var expenseTxn = account.RecordTransaction("Boodschappen", -12.50m, new DateTime(2024, 1, 15), "Albert Heijn", "k1");
        var incomeTxn = account.RecordTransaction("Salaris januari", 1500m, new DateTime(2024, 1, 16), "Werkgever BV", "k2");

        // Create the account first (as the app does), then append transactions in a later session.
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Account>(created.AccountId, created);
            await session.SaveChangesAsync();
        }
        // Account event + derived budget event commit atomically, as production does.
        await using (var session = store.LightweightSession())
        {
            await handler.RecordAndProjectAsync(session, created.AccountId, expenseTxn);
            await handler.RecordAndProjectAsync(session, created.AccountId, incomeTxn);
            await session.SaveChangesAsync();
        }

        await using var q = store.QuerySession();

        // Account read model: balance and transaction count maintained inline.
        var acc = await q.Query<AccountSummary>().SingleAsync();
        Assert.Equal(1487.50m, acc.Balance);
        Assert.Equal(2, acc.TransactionCount);

        // Flat transaction read model: one document per recorded transaction.
        var views = await q.Query<TransactionView>().ToListAsync();
        Assert.Equal(2, views.Count);
        Assert.Contains(views, v => v.Amount == -12.50m && v.Payee == "Albert Heijn");
        Assert.Contains(views, v => v.Amount == 1500m);

        // Budget read models, auto-categorized.
        var expense = await q.Query<ExpenseView>().SingleAsync();
        Assert.Equal("Groceries", expense.Category);
        var income = await q.Query<Income>().SingleAsync();
        Assert.Equal("Salary", income.Category);

        // Re-categorization flows into the ExpenseView read model inline.
        await using (var session = store.LightweightSession())
        {
            var live = await session.Events.AggregateStreamAsync<Expense>(expense.Id);
            session.Events.Append(expense.Id, live!.Categorize("Dining"));
            await session.SaveChangesAsync();
        }
        var updated = await store.QuerySession().Query<ExpenseView>().SingleAsync();
        Assert.Equal("Dining", updated.Category);

        // Income can be recategorized too — it is not stuck with its import-time category.
        await using (var session = store.LightweightSession())
        {
            var liveIncome = await session.Events.AggregateStreamAsync<Income>(income.Id);
            session.Events.Append(income.Id, liveIncome!.Categorize("Bonus"));
            await session.SaveChangesAsync();
        }
        var updatedIncome = await store.QuerySession().Query<Income>().SingleAsync();
        Assert.Equal("Bonus", updatedIncome.Category);
    }
}
