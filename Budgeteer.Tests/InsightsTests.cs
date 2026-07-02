using Budgeteer.Accounts.Import;
using Budgeteer.Budget.Insights;
using Budgeteer.Budget.ReadModels;
using Budgeteer.Web.Services;
using Xunit;

namespace Budgeteer.Tests;

public class StatementReconciliationTests
{
    private static BankMutation M(int day, decimal amount, decimal? balance) =>
        new() { Date = new DateTime(2024, 1, day), Amount = amount, BalanceAfter = balance };

    [Fact]
    public void Consistent_running_balances_report_no_gaps()
    {
        var result = StatementReconciliation.Check(new[]
        {
            M(1, -10m, 90m),
            M(2, -5m, 85m),
            M(3, 20m, 105m),
        });

        Assert.True(result.Checked);
        Assert.True(result.Consistent);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void A_break_in_the_chain_is_flagged_as_a_gap()
    {
        // 85 + 20 should be 105, but the statement jumps to 200 -> a transaction is missing.
        var result = StatementReconciliation.Check(new[]
        {
            M(1, -10m, 90m),
            M(2, -5m, 85m),
            M(3, 20m, 200m),
        });

        Assert.True(result.Checked);
        Assert.False(result.Consistent);
        var gap = Assert.Single(result.Gaps);
        Assert.Equal(105m, gap.ExpectedBalance);
        Assert.Equal(200m, gap.ActualBalance);
    }

    [Fact]
    public void Not_available_when_export_lacks_running_balances()
    {
        var result = StatementReconciliation.Check(new[] { M(1, -10m, null), M(2, -5m, null) });
        Assert.False(result.Checked);
        Assert.True(result.Consistent); // nothing to contradict
    }
}

public class RecurringDetectionTests
{
    private static ExpenseView E(string payee, int year, int month, int day, decimal amount) =>
        new() { Payee = payee, Date = new DateTime(year, month, day), Amount = amount };

    [Fact]
    public void Detects_a_monthly_subscription_and_flags_amount_change()
    {
        var detected = RecurringDetectionService.Detect(new[]
        {
            E("Spotify", 2024, 1, 15, 9.99m),
            E("Spotify", 2024, 2, 15, 9.99m),
            E("Spotify", 2024, 3, 15, 9.99m),
            E("Spotify", 2024, 4, 15, 11.99m), // price hike
        });

        var sub = Assert.Single(detected);
        Assert.Equal("Monthly", sub.Cadence);
        Assert.Equal(4, sub.Occurrences);
        Assert.True(sub.AmountChanged);
        Assert.Equal(new DateTime(2024, 4, 15).AddDays(sub.IntervalDays), sub.NextExpected);
    }

    [Fact]
    public void Fewer_than_three_occurrences_is_not_recurring()
    {
        var detected = RecurringDetectionService.Detect(new[]
        {
            E("Gym", 2024, 1, 1, 30m),
            E("Gym", 2024, 2, 1, 30m),
        });
        Assert.Empty(detected);
    }

    [Fact]
    public void Irregular_intervals_are_not_recurring()
    {
        var detected = RecurringDetectionService.Detect(new[]
        {
            E("Random Shop", 2024, 1, 1, 20m),
            E("Random Shop", 2024, 1, 5, 20m),
            E("Random Shop", 2024, 3, 20, 20m),
        });
        Assert.Empty(detected);
    }
}

public class CsvExportTests
{
    [Fact]
    public void Csv_has_header_and_rfc4180_escaping_and_type_column()
    {
        var rows = new[]
        {
            new TransactionRow("t1", "a", "Checking", new DateTime(2024, 1, 15),
                "Groceries, weekly", "Albert \"AH\" Heijn", -12.50m, "Groceries", false),
            new TransactionRow("t2", "a", "Checking", new DateTime(2024, 1, 16),
                "Salary", "Employer", 2000m, "Salary", false),
        };

        var csv = TransactionCsvExporter.ToCsv(rows);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Date,Account,Description,Payee,Category,Amount,Type", lines[0]);
        // Fields with a comma or quote are quoted; embedded quotes are doubled.
        Assert.Contains("\"Groceries, weekly\"", csv);
        Assert.Contains("\"Albert \"\"AH\"\" Heijn\"", csv);
        Assert.Contains("-12.50,Expense", csv);
        Assert.Contains("2000.00,Income", csv);
    }

    [Fact]
    public void Formula_prefixes_in_text_fields_are_neutralized()
    {
        // Description/payee text is chosen by the counterparty on a bank statement,
        // so a formula must come out inert when the export is opened in a spreadsheet.
        var rows = new[]
        {
            new TransactionRow("t1", "a", "Checking", new DateTime(2024, 1, 15),
                "=HYPERLINK(\"http://evil\",\"open\")", "@payee", -1m, "+cat", false),
        };

        var csv = TransactionCsvExporter.ToCsv(rows);

        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\",\"\"open\"\")\"", csv);
        Assert.Contains("'@payee", csv);
        Assert.Contains("'+cat", csv);
        // Numeric amounts must stay numeric — no apostrophe on the leading minus.
        Assert.Contains(",-1.00,", csv);
    }

    [Fact]
    public void Newlines_inside_fields_stay_quoted_and_rows_stay_intact()
    {
        var rows = new[]
        {
            new TransactionRow("t1", "a", "Checking", new DateTime(2024, 1, 15),
                "line one\nline two", null, -1m, null, false),
        };

        var csv = TransactionCsvExporter.ToCsv(rows);

        Assert.Contains("\"line one\nline two\"", csv);
        // Header + one record: exactly two CRLF-terminated lines outside the quoted field.
        Assert.Equal(2, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
