using Budgeteer.Accounts.ReadModels;
using Budgeteer.Budget.Budgeting;
using Budgeteer.Web.Services;
using Xunit;

namespace Budgeteer.Tests;

public class BudgetAllocationLogicTests
{
    private static (string, decimal, int) Spend(string cat, decimal total) => (cat, total, 1);

    [Fact]
    public void BuildBudgetRows_joins_spending_with_limits_and_flags_status()
    {
        var spending = new[] { Spend("Groceries", 100m), Spend("Dining", 30m) };
        var allocations = new[]
        {
            new BudgetAllocation { Category = "Groceries", MonthlyLimit = 80m },  // over
            new BudgetAllocation { Category = "Rent", MonthlyLimit = 500m },      // limit, no spend
        };

        var rows = BudgetAllocationService.BuildBudgetRows(spending, allocations, periodMonths: 1);

        var groceries = rows.Single(r => r.Category == "Groceries");
        Assert.True(groceries.OverBudget);
        Assert.Equal(-20m, groceries.Remaining);

        var dining = rows.Single(r => r.Category == "Dining");
        Assert.False(dining.HasLimit); // no allocation

        var rent = rows.Single(r => r.Category == "Rent");
        Assert.Equal(0m, rent.Spent);
        Assert.Equal(500m, rent.Remaining);
    }

    [Fact]
    public void Limit_is_scaled_to_the_number_of_months_in_the_period()
    {
        var rows = BudgetAllocationService.BuildBudgetRows(
            new[] { Spend("Groceries", 4800m) },
            new[] { new BudgetAllocation { Category = "Groceries", MonthlyLimit = 400m } },
            periodMonths: 12);

        var g = rows.Single();
        Assert.Equal(4800m, g.Limit);   // 400 * 12 months
        Assert.False(g.OverBudget);     // 4800 spent over 12 months is exactly on budget
        Assert.Equal(0m, g.Remaining);
    }

    [Fact]
    public void Case_variant_categories_do_not_throw()
    {
        // 'Groceries' and 'groceries' must not cause a duplicate-key crash.
        var rows = BudgetAllocationService.BuildBudgetRows(
            new[] { ("Groceries", 30m, 1), ("groceries", 20m, 1) },
            new[] { new BudgetAllocation { Category = "Groceries", MonthlyLimit = 100m } },
            periodMonths: 1);

        var g = rows.Single();
        Assert.Equal(50m, g.Spent); // collapsed case-insensitively
    }

    [Fact]
    public void NearLimit_triggers_at_80_percent_but_not_over()
    {
        var rows = BudgetAllocationService.BuildBudgetRows(
            new[] { Spend("Fun", 85m) },
            new[] { new BudgetAllocation { Category = "Fun", MonthlyLimit = 100m } },
            periodMonths: 1);

        var fun = rows.Single();
        Assert.True(fun.NearLimit);
        Assert.False(fun.OverBudget);
    }
}

public class TransferDetectionLogicTests
{
    private static TransactionView Tx(string id, string acc, decimal amount, DateTime date, string? cpIban) =>
        new() { Id = id, AccountId = acc, Amount = amount, Date = date, CounterpartyIban = cpIban };

    private static readonly List<AccountSummary> Accounts = new()
    {
        new AccountSummary { Id = "accA", Iban = "NL11AAAA0000000001" },
        new AccountSummary { Id = "accB", Iban = "NL22BBBB0000000002" },
    };

    [Fact]
    public void Detects_paired_transfer_between_own_accounts()
    {
        var d = new DateTime(2024, 3, 1);
        var txns = new List<TransactionView>
        {
            Tx("t1", "accA", -100m, d, "NL22BBBB0000000002"),       // out of A, to B
            Tx("t2", "accB", 100m, d.AddDays(1), "NL11AAAA0000000001"), // into B, from A
            Tx("t3", "accA", -50m, d, null),                         // unrelated expense
            Tx("t4", "accB", 999m, d, "NL99XXXX0000000009"),         // unrelated income
        };

        var links = TransferDetectionService.Detect(txns, Accounts);

        var link = Assert.Single(links);
        Assert.Equal("t1", link.FromTransactionId);
        Assert.Equal("t2", link.ToTransactionId);
        Assert.Equal(100m, link.Amount);
    }

    [Fact]
    public void Does_not_pair_equal_amounts_without_iban_linkage()
    {
        var d = new DateTime(2024, 3, 1);
        var txns = new List<TransactionView>
        {
            Tx("t1", "accA", -100m, d, null),  // no counterparty IBAN
            Tx("t2", "accB", 100m, d, null),   // no counterparty IBAN -> not a confirmed transfer
        };

        Assert.Empty(TransferDetectionService.Detect(txns, Accounts));
    }

    [Fact]
    public void Does_not_pair_when_only_one_leg_references_the_other_account()
    {
        var d = new DateTime(2024, 3, 1);
        var txns = new List<TransactionView>
        {
            Tx("t1", "accA", -100m, d, "NL22BBBB0000000002"), // references B
            Tx("t2", "accB", 100m, d, null),                  // no counterparty -> not mutual
        };

        Assert.Empty(TransferDetectionService.Detect(txns, Accounts));
    }

    [Fact]
    public void Does_not_pair_within_the_same_account()
    {
        var d = new DateTime(2024, 3, 1);
        var txns = new List<TransactionView>
        {
            Tx("t1", "accA", -100m, d, "NL11AAAA0000000001"),
            Tx("t2", "accA", 100m, d, "NL11AAAA0000000001"),
        };

        Assert.Empty(TransferDetectionService.Detect(txns, Accounts));
    }

    [Fact]
    public void Already_linked_transactions_are_not_paired_again()
    {
        // t1<->t2 were linked in an earlier run (2 days apart). A later import adds t5, a
        // same-day — i.e. "better" — candidate for t1. Recomputing must not re-pair t1,
        // or t1 ends up in two links and both t2 and t5 get excluded from income.
        var d = new DateTime(2024, 3, 1);
        var txns = new List<TransactionView>
        {
            Tx("t1", "accA", -100m, d, "NL22BBBB0000000002"),
            Tx("t2", "accB", 100m, d.AddDays(2), "NL11AAAA0000000001"),
            Tx("t5", "accB", 100m, d, "NL11AAAA0000000001"),
        };

        var links = TransferDetectionService.Detect(txns, Accounts,
            alreadyLinkedTransactionIds: new[] { "t1", "t2" });

        Assert.Empty(links);

        // Sanity: without the seed, the closer-date candidate t5 would indeed win.
        var unseeded = TransferDetectionService.Detect(txns, Accounts);
        var link = Assert.Single(unseeded);
        Assert.Equal("t5", link.ToTransactionId);
    }
}

public class SavingGoalLogicTests
{
    private static readonly List<AccountSummary> Accounts = new()
    {
        new AccountSummary { Id = "savings", Name = "Savings", Balance = 750m },
    };

    [Fact]
    public void CurrentFor_uses_linked_account_balance_or_manual_amount()
    {
        var linked = new SavingGoal { TargetAmount = 1000m, LinkedAccountId = "savings" };
        Assert.Equal(750m, SavingGoalService.CurrentFor(linked, Accounts));

        var manual = new SavingGoal { TargetAmount = 1000m, ManualAmount = 250m };
        Assert.Equal(250m, SavingGoalService.CurrentFor(manual, Accounts));
    }

    [Fact]
    public void Progress_computes_percent_remaining_and_reached()
    {
        var p = SavingGoalService.Progress(
            new SavingGoal { TargetAmount = 1000m, LinkedAccountId = "savings" }, Accounts);
        Assert.Equal(75, p.Percent, 0);
        Assert.Equal(250m, p.Remaining);
        Assert.False(p.Reached);

        var done = SavingGoalService.Progress(
            new SavingGoal { TargetAmount = 500m, LinkedAccountId = "savings" }, Accounts);
        Assert.True(done.Reached);
        Assert.Equal(0m, done.Remaining);
    }

    [Fact]
    public void MonthlyNeeded_splits_remaining_over_months_until_target_date()
    {
        var asOf = new DateTime(2024, 1, 1);
        var goal = new SavingGoal { TargetAmount = 1200m, ManualAmount = 200m, TargetDate = new DateTime(2024, 11, 1) };
        var p = SavingGoalService.Progress(goal, Accounts); // not linked -> manual 200

        // remaining 1000 over 10 months = 100/month
        Assert.Equal(100m, p.MonthlyNeeded(asOf));

        // No date -> null; past date -> null
        var noDate = SavingGoalService.Progress(new SavingGoal { TargetAmount = 1000m, ManualAmount = 0m }, Accounts);
        Assert.Null(noDate.MonthlyNeeded(asOf));
        var past = SavingGoalService.Progress(
            new SavingGoal { TargetAmount = 1000m, ManualAmount = 0m, TargetDate = new DateTime(2023, 1, 1) }, Accounts);
        Assert.Null(past.MonthlyNeeded(asOf));
    }

    [Fact]
    public void MonthlyNeeded_in_the_target_month_returns_the_full_remainder()
    {
        var asOf = new DateTime(2026, 6, 17);
        var goal = new SavingGoal { TargetAmount = 1000m, ManualAmount = 600m, TargetDate = new DateTime(2026, 6, 30) };
        var p = SavingGoalService.Progress(goal, Accounts); // manual 600

        Assert.Equal(400m, p.MonthlyNeeded(asOf)); // due this month -> set aside the remainder now
    }

    [Fact]
    public void Percent_is_clamped_to_zero_for_an_overdrawn_linked_account()
    {
        var overdrawn = new List<AccountSummary> { new() { Id = "x", Balance = -200m } };
        var p = SavingGoalService.Progress(
            new SavingGoal { TargetAmount = 1000m, LinkedAccountId = "x" }, overdrawn);
        Assert.Equal(0, p.Percent);
    }
}
