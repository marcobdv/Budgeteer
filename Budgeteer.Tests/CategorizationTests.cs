using Budgeteer.Budget.Categorization;
using Budgeteer.Budget.EventHandlers;
using Budgeteer.Shared.Events.Accounts;
using Budgeteer.Shared.Events.Budget;
using Marten;
using Npgsql;
using Xunit;

namespace Budgeteer.Tests;

public class CategorizationTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=budgeteer;Username=postgres;Password=postgres;Timeout=3;Command Timeout=10";

    private static bool PostgresAvailable()
    {
        try { using var c = new NpgsqlConnection(ConnectionString); c.Open(); return true; }
        catch { return false; }
    }

    private static DocumentStore CreateStore() => DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionString);
        opts.DatabaseSchemaName = "cat_it";
        opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
    });

    private static CategorizationRule Rule(string keyword, string category, int priority) =>
        new() { Keyword = keyword, Category = category, Priority = priority };

    [Fact]
    public void Match_picks_keyword_in_payee_or_description()
    {
        var rules = new[] { Rule("albert heijn", "Groceries", 100) };
        Assert.Equal("Groceries", TransactionCategorizer.Match(rules, "Albert Heijn 1234", "Boodschappen", -12.50m));
        Assert.Equal("Groceries", TransactionCategorizer.Match(rules, null, "Betaling ALBERT HEIJN", -5m));
    }

    [Fact]
    public void Match_prefers_higher_priority_then_longer_keyword()
    {
        var rules = new[]
        {
            Rule("shop", "Generic", 100),
            Rule("coffee shop", "Coffee", 100), // same priority, longer keyword wins
        };
        Assert.Equal("Coffee", TransactionCategorizer.Match(rules, "The Coffee Shop", "", -4m));

        var rules2 = new[]
        {
            Rule("shop", "Generic", 100),
            Rule("shop", "Override", 500), // higher priority wins regardless of length
        };
        Assert.Equal("Override", TransactionCategorizer.Match(rules2, "Some Shop", "", -4m));
    }

    [Fact]
    public void Match_defaults_income_when_no_rule_and_positive()
    {
        Assert.Equal(TransactionCategorizer.DefaultIncomeCategory,
            TransactionCategorizer.Match(System.Array.Empty<CategorizationRule>(), "Unknown", "x", 1000m));
    }

    [Fact]
    public void Match_returns_null_for_unmatched_expense()
    {
        Assert.Null(TransactionCategorizer.Match(System.Array.Empty<CategorizationRule>(), "Unknown", "x", -10m));
    }

    [SkippableFact]
    public async Task Seeded_defaults_categorize_known_dutch_merchants()
    {
        Skip.IfNot(PostgresAvailable(), "Local PostgreSQL not available.");

        await using var store = CreateStore();
        var categorizer = new TransactionCategorizer(store);
        await categorizer.SeedDefaultsAsync();

        Assert.Equal("Groceries", await categorizer.CategorizeAsync("Albert Heijn 1234", "Boodschappen", -12.50m));
        Assert.Equal("Subscriptions", await categorizer.CategorizeAsync("Spotify AB", "Spotify Premium", -9.99m));
        Assert.Equal("Salary", await categorizer.CategorizeAsync("Werkgever BV", "Salaris januari", 2500m));
    }

    [SkippableFact]
    public async Task Learning_makes_future_transactions_auto_categorize()
    {
        Skip.IfNot(PostgresAvailable(), "Local PostgreSQL not available.");

        await using var store = CreateStore();
        var categorizer = new TransactionCategorizer(store);

        // A payee unknown to the defaults starts uncategorized.
        // Unique per run so a learned rule persisted by a prior run can't pre-satisfy this.
        var payee = "Quirky Local Bookshop " + System.Guid.NewGuid().ToString("N");
        Assert.Null(await categorizer.CategorizeAsync(payee, "boeken", -20m));

        // ...after learning, the same payee is categorized automatically.
        await categorizer.LearnAsync(payee, "Hobbies");
        Assert.Equal("Hobbies", await categorizer.CategorizeAsync(payee, "boeken", -20m));
    }

    [SkippableFact]
    public async Task Importing_through_the_handler_auto_categorizes_the_expense()
    {
        Skip.IfNot(PostgresAvailable(), "Local PostgreSQL not available.");

        await using var store = CreateStore();
        var categorizer = new TransactionCategorizer(store);
        await categorizer.SeedDefaultsAsync();
        var handler = new TransactionEventHandler(categorizer);

        var txn = new TransactionRecorded(
            System.Guid.NewGuid().ToString(), "acct-x", new System.DateTime(2024, 2, 1),
            "Boodschappen", -33.10m, "Albert Heijn 99", System.DateTime.UtcNow);

        await using (var write = store.LightweightSession())
        {
            await handler.RecordAndProjectAsync(write, "acct-x", txn);
            await write.SaveChangesAsync();
        }

        await using var session = store.LightweightSession();
        var expense = (await session.Events.QueryAllRawEvents()
                .Where(e => e.DotNetTypeName.Contains("ExpenseRecorded")).ToListAsync())
            .Select(e => e.Data).OfType<ExpenseRecorded>()
            .First(e => e.TransactionId == txn.TransactionId);

        Assert.Equal("Groceries", expense.Category);
    }
}
