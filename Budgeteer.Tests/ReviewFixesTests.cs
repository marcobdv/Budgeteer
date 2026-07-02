using System.Text;
using Budgeteer.Accounts.Import;
using Budgeteer.Budget.Categorization;
using Xunit;

namespace Budgeteer.Tests;

/// <summary>
/// Regression tests for the verified code-review findings (pure-function fixes only;
/// the edit-preserves-import-key fix is covered in <see cref="LedgerIntegrationTests"/>).
/// </summary>
public class ReviewFixesTests
{
    // #3 — two genuinely identical same-day KNAB rows must not collapse to one dedup key.
    [Fact]
    public void Knab_identical_same_day_rows_get_distinct_dedup_keys()
    {
        const string csv =
            "\"Rekeningnummer\";\"Transactiedatum\";\"Valutacode\";\"CreditDebet\";\"Bedrag\";\"Tegenrekeningnummer\";\"Tegenrekeninghouder\";\"Omschrijving\"\n" +
            "\"NL12KNAB0123456789\";\"15-01-2024\";\"EUR\";\"D\";\"4,50\";\"\";\"Coffee Co\";\"Latte\"\n" +
            "\"NL12KNAB0123456789\";\"15-01-2024\";\"EUR\";\"D\";\"4,50\";\"\";\"Coffee Co\";\"Latte\"\n";

        var rows = new KnabCsvParser().Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].DedupKey, rows[1].DedupKey);

        // Re-parsing the same file yields the same two keys, so re-imports still dedup correctly.
        var again = new KnabCsvParser().Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.Equal(rows[0].DedupKey, again[0].DedupKey);
        Assert.Equal(rows[1].DedupKey, again[1].DedupKey);
    }

    // #5 — one out-of-range amount must skip only that row, not abort the whole import.
    [Fact]
    public void An_overflowing_amount_skips_only_its_row()
    {
        // The middle row's amount has far more digits than decimal can hold.
        const string csv =
            "\"IBAN/BBAN\",\"Munt\",\"Volgnr\",\"Datum\",\"Bedrag\",\"Saldo na trn\",\"Naam tegenpartij\",\"Omschrijving-1\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"1\",\"2024-01-15\",\"-12,50\",\"100,00\",\"Shop\",\"ok\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"2\",\"2024-01-16\",\"-999999999999999999999999999999999,00\",\"50,00\",\"Bad\",\"overflow\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"3\",\"2024-01-17\",\"-5,00\",\"45,00\",\"Shop\",\"ok2\"\n";

        var rows = new RabobankCsvParser().Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

        Assert.Equal(2, rows.Count); // the two valid rows survive; the overflow row is skipped
        Assert.All(rows, r => Assert.NotEqual(0m, r.Amount));
    }

    // #6 — a newest-first statement with a valid running-balance chain must not be flagged.
    [Fact]
    public void Reconciliation_accepts_a_newest_first_consistent_statement()
    {
        BankMutation M(int day, decimal amount, decimal balance) =>
            new() { Date = new DateTime(2024, 1, day), Amount = amount, BalanceAfter = balance };

        // Listed newest-first: 105 <- (+20) 85 <- (-5) 90 <- (-10) [start 100].
        var result = StatementReconciliation.Check(new[]
        {
            M(3, 20m, 105m),
            M(2, -5m, 85m),
            M(1, -10m, 90m),
        });

        Assert.True(result.Checked);
        Assert.True(result.Consistent);
        Assert.Empty(result.Gaps);
    }

    // #7 — an expense seed keyword must not categorize an incoming payment.
    [Fact]
    public void Income_is_not_mislabeled_by_an_expense_seed_keyword()
    {
        var rules = new[]
        {
            new CategorizationRule { Keyword = "google", Category = "Subscriptions", Source = RuleSource.Seed },
        };

        // Positive amount (income) whose text contains an expense keyword.
        Assert.Equal(TransactionCategorizer.DefaultIncomeCategory,
            TransactionCategorizer.Match(rules, "Google Pay", "Refund", 25m));

        // The same keyword still categorizes an expense.
        Assert.Equal("Subscriptions",
            TransactionCategorizer.Match(rules, "Google", "Google One", -1.99m));
    }

    // #10 — an explicit manual rule outranks an auto-learned one.
    [Fact]
    public void Manual_rule_outranks_a_learned_rule()
    {
        var rules = new[]
        {
            new CategorizationRule
            {
                Keyword = "bakery", Category = "Learned Cat", Source = RuleSource.Learned,
                Priority = (int)CategorizationRule.PriorityTier.Learned
            },
            new CategorizationRule
            {
                Keyword = "bakery", Category = "Manual Cat", Source = RuleSource.Manual,
                Priority = (int)CategorizationRule.PriorityTier.Manual
            },
        };

        Assert.Equal("Manual Cat", TransactionCategorizer.Match(rules, "Corner Bakery", "", -8m));
    }

    // A duplicate TransactionDeleted in a stream (raced in before the concurrency guard
    // existed) must not reverse the balance twice when the aggregate is rebuilt.
    [Fact]
    public void Replaying_a_duplicate_delete_reverses_the_balance_only_once()
    {
        var account = new Budgeteer.Accounts.Domain.Account();
        account.Apply(new Budgeteer.Shared.Events.Accounts.AccountCreated(
            "a1", "Checking", "Checking", 100m, DateTime.UtcNow, null));

        var recorded = new Budgeteer.Shared.Events.Accounts.TransactionRecorded(
            "t1", "a1", new DateTime(2024, 1, 15), "Groceries", -25m, "AH", DateTime.UtcNow, "key-1", null);
        account.Apply(recorded);
        Assert.Equal(75m, account.Balance.Value);

        var deleted = new Budgeteer.Shared.Events.Accounts.TransactionDeleted(
            "t1", "a1", -25m, "key-1", DateTime.UtcNow);
        account.Apply(deleted);
        account.Apply(deleted); // duplicate — must be a no-op

        Assert.Equal(100m, account.Balance.Value);
        Assert.Empty(account.TransactionIds);
    }

    // The aggregate defends the import-dedup invariant itself rather than trusting callers.
    [Fact]
    public void Recording_an_already_imported_key_throws()
    {
        var account = new Budgeteer.Accounts.Domain.Account();
        account.Apply(new Budgeteer.Shared.Events.Accounts.AccountCreated(
            "a1", "Checking", "Checking", 0m, DateTime.UtcNow, null));
        account.Apply(new Budgeteer.Shared.Events.Accounts.TransactionRecorded(
            "t1", "a1", new DateTime(2024, 1, 15), "Groceries", -25m, "AH", DateTime.UtcNow, "key-1", null));

        Assert.Throws<InvalidOperationException>(() =>
            account.RecordTransaction("Groceries again", -25m, new DateTime(2024, 1, 15), importKey: "key-1"));
    }
}
