using Budgeteer.Web.Services;
using Xunit;

namespace Budgeteer.Tests;

public class TransactionFilterTests
{
    private static readonly List<TransactionRow> Rows = new()
    {
        new("t1", "acc-checking", "Checking", new DateTime(2024, 1, 5), "Boodschappen", "Albert Heijn", -12.50m, "Groceries"),
        new("t2", "acc-checking", "Checking", new DateTime(2024, 1, 16), "Salaris", "Werkgever BV", 2000m, "Salary"),
        new("t3", "acc-savings",  "Savings",  new DateTime(2024, 2, 10), "Spotify", "Spotify AB", -9.99m, "Subscriptions"),
        new("t4", "acc-checking", "Checking", new DateTime(2024, 2, 20), "Onbekend", "Mystery Shop", -30m, null),
    };

    [Fact]
    public void Filters_by_account()
    {
        var r = TransactionQueryService.Apply(Rows, new TransactionFilter(AccountId: "acc-savings"));
        Assert.Single(r);
        Assert.Equal("t3", r[0].TransactionId);
    }

    [Fact]
    public void Filters_by_date_range_inclusive()
    {
        var r = TransactionQueryService.Apply(Rows, new TransactionFilter(
            From: new DateTime(2024, 1, 16), To: new DateTime(2024, 2, 10)));
        Assert.Equal(new[] { "t2", "t3" }, r.Select(x => x.TransactionId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Filters_by_flow()
    {
        var income = TransactionQueryService.Apply(Rows, new TransactionFilter(Flow: FlowFilter.IncomeOnly));
        Assert.Equal(new[] { "t2" }, income.Select(x => x.TransactionId).ToArray());

        var expenses = TransactionQueryService.Apply(Rows, new TransactionFilter(Flow: FlowFilter.ExpenseOnly));
        Assert.Equal(3, expenses.Count);
        Assert.DoesNotContain(expenses, x => x.TransactionId == "t2");
    }

    [Fact]
    public void Filters_by_category_including_uncategorized()
    {
        var groceries = TransactionQueryService.Apply(Rows, new TransactionFilter(Category: "Groceries"));
        Assert.Equal(new[] { "t1" }, groceries.Select(x => x.TransactionId).ToArray());

        var uncategorized = TransactionQueryService.Apply(Rows,
            new TransactionFilter(Category: TransactionQueryService.Uncategorized));
        Assert.Equal(new[] { "t4" }, uncategorized.Select(x => x.TransactionId).ToArray());
    }

    [Fact]
    public void Search_matches_description_or_payee_case_insensitively()
    {
        var byPayee = TransactionQueryService.Apply(Rows, new TransactionFilter(Search: "spotify"));
        Assert.Equal(new[] { "t3" }, byPayee.Select(x => x.TransactionId).ToArray());

        var byDescription = TransactionQueryService.Apply(Rows, new TransactionFilter(Search: "BOODSCHAP"));
        Assert.Equal(new[] { "t1" }, byDescription.Select(x => x.TransactionId).ToArray());
    }

    [Fact]
    public void Combined_filters_are_anded()
    {
        var r = TransactionQueryService.Apply(Rows, new TransactionFilter(
            AccountId: "acc-checking", Flow: FlowFilter.ExpenseOnly, From: new DateTime(2024, 2, 1)));
        Assert.Equal(new[] { "t4" }, r.Select(x => x.TransactionId).ToArray());
    }

    [Fact]
    public void DistinctCategories_normalizes_uncategorized_and_sorts()
    {
        var cats = TransactionQueryService.DistinctCategories(Rows);
        Assert.Equal(new[] { "Groceries", "Salary", "Subscriptions", "Uncategorized" }, cats.ToArray());
    }
}
