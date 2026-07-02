using Budgeteer.Accounts;
using Budgeteer.Accounts.Import;
using Budgeteer.Budget.Insights;
using Budgeteer.Budget.ReadModels;
using Budgeteer.Web.Services;
using Xunit;

namespace Budgeteer.Tests;

public class StatementReconciliationTests
{
    private static BankMutation M(int day, decimal amount, decimal? balance, string iban = "") =>
        new() { Date = new DateTime(2024, 1, day), Amount = amount, BalanceAfter = balance, AccountIban = Iban.From(iban) };

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

    [Fact]
    public void Multi_account_export_is_chained_per_account_without_false_gaps()
    {
        // A combined Rabobank export interleaves accounts; each account's own chain is
        // consistent, but the naive whole-file chain would break at every boundary.
        var result = StatementReconciliation.Check(new[]
        {
            M(1, -10m, 90m, "NL11RABO0000000001"),
            M(1, 50m, 1050m, "NL22RABO0000000002"),
            M(2, -5m, 85m, "NL11RABO0000000001"),
            M(2, -50m, 1000m, "NL22RABO0000000002"),
        });

        Assert.True(result.Checked);
        Assert.True(result.Consistent);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void Gap_row_index_refers_to_the_original_file_row()
    {
        // Row 1 (index 1) has no balance and is skipped by the check; the break surfaces
        // at file row index 3, not at the index within the filtered/grouped list.
        var result = StatementReconciliation.Check(new[]
        {
            M(1, -10m, 90m),
            M(2, -1m, null),
            M(3, -5m, 85m),
            M(4, 20m, 200m), // 85 + 20 = 105, statement says 200
        });

        Assert.False(result.Consistent);
        var gap = Assert.Single(result.Gaps);
        Assert.Equal(3, gap.RowIndex);
        Assert.Equal(105m, gap.ExpectedBalance);
        Assert.Equal(200m, gap.ActualBalance);
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
        // Calendar-anchored: charged on the 15th -> next expected on the 15th.
        Assert.Equal(new DateTime(2024, 5, 15), sub.NextExpected);
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

    [Fact]
    public void Two_random_gaps_cannot_synthesize_a_cadence()
    {
        // Gaps of 75 and 100 days: neither is quarterly, but their median (87) is within the
        // quarterly tolerance. The old "allow one irregular gap" rule accepted this.
        var detected = RecurringDetectionService.Detect(new[]
        {
            E("Random Shop", 2024, 1, 1, 20m),
            E("Random Shop", 2024, 3, 16, 20m), // +75 days
            E("Random Shop", 2024, 6, 24, 20m), // +100 days
        });
        Assert.Empty(detected);
    }

    [Fact]
    public void Weekly_grocery_runs_with_varying_amounts_are_not_recurring()
    {
        var detected = RecurringDetectionService.Detect(new[]
        {
            E("Albert Heijn", 2024, 1, 6, 47.13m),
            E("Albert Heijn", 2024, 1, 13, 82.50m),
            E("Albert Heijn", 2024, 1, 20, 31.05m),
            E("Albert Heijn", 2024, 1, 27, 64.20m),
        });
        Assert.Empty(detected);
    }

    [Fact]
    public void Weekly_fixed_charge_is_recurring()
    {
        var detected = RecurringDetectionService.Detect(new[]
        {
            E("Storage Box BV", 2024, 1, 6, 12.50m),
            E("Storage Box BV", 2024, 1, 13, 12.50m),
            E("Storage Box BV", 2024, 1, 20, 12.50m),
        });
        var sub = Assert.Single(detected);
        Assert.Equal("Weekly", sub.Cadence);
    }

    [Fact]
    public void Biweekly_charges_are_detected()
    {
        var detected = RecurringDetectionService.Detect(new[]
        {
            E("Schoonmaak Service", 2024, 1, 5, 45m),
            E("Schoonmaak Service", 2024, 1, 19, 45m),
            E("Schoonmaak Service", 2024, 2, 2, 45m),
        });
        var sub = Assert.Single(detected);
        Assert.Equal("Biweekly", sub.Cadence);
    }

    [Fact]
    public void Excluded_transaction_ids_are_ignored()
    {
        // A monthly transfer to your own savings looks exactly like a subscription; the
        // caller passes the transfer-leg ids so it is not reported as one.
        static ExpenseView T(string id, int month) => new()
        {
            TransactionId = id, Payee = "Eigen Spaarrekening",
            Date = new DateTime(2024, month, 1), Amount = 500m
        };
        var transfers = new[] { T("tr1", 1), T("tr2", 2), T("tr3", 3) };

        Assert.NotEmpty(RecurringDetectionService.Detect(transfers));
        Assert.Empty(RecurringDetectionService.Detect(transfers,
            excludeTransactionIds: new[] { "tr1", "tr2", "tr3" }));
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
