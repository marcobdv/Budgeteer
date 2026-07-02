using System.Text;
using Budgeteer.Accounts.Import;
using Xunit;

namespace Budgeteer.Tests;

public class BankStatementParserTests
{
    private const string RabobankCsv = Samples.RabobankCsv;
    private const string KnabCsv = Samples.KnabCsv;

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Rabobank_parses_signed_amounts_dates_and_descriptions()
    {
        var result = new RabobankCsvParser().Parse(new MemoryStream(Bytes(RabobankCsv)));

        Assert.Equal(2, result.Count);

        var expense = result[0];
        Assert.Equal("NL11RABO0123456789", expense.AccountIban.ToString());
        Assert.Equal(new DateTime(2024, 1, 15), expense.Date);
        Assert.Equal(-12.50m, expense.Amount);
        Assert.Equal("Albert Heijn 1234", expense.CounterpartyName);
        Assert.Equal("NL22INGB0009876543", expense.CounterpartyIban.ToString());
        Assert.Contains("Boodschappen", expense.Description);
        Assert.Contains("AH Filiaal 1234", expense.Description); // Omschrijving-1 + -2 joined

        var income = result[1];
        Assert.Equal(1500.00m, income.Amount); // "+1.500,00" -> dot thousands, comma decimal
        Assert.Equal(new DateTime(2024, 1, 16), income.Date);
        Assert.Equal("Werkgever BV", income.CounterpartyName);
    }

    [Fact]
    public void Knab_derives_sign_from_credit_debet_column()
    {
        var result = new KnabCsvParser().Parse(new MemoryStream(Bytes(KnabCsv)));

        Assert.Equal(2, result.Count);

        var expense = result[0];
        Assert.Equal("NL12KNAB0123456789", expense.AccountIban.ToString());
        Assert.Equal(new DateTime(2024, 1, 15), expense.Date); // dd-MM-yyyy
        Assert.Equal(-45.30m, expense.Amount); // "D" -> negative
        Assert.Equal("Albert Heijn", expense.CounterpartyName);
        Assert.Equal("Boodschappen AH Amsterdam", expense.Description);

        var income = result[1];
        Assert.Equal(2250.00m, income.Amount); // "C" + "2.250,00" -> positive
    }

    [Theory]
    [InlineData(RabobankCsv, BankFormat.Rabobank)]
    [InlineData(KnabCsv, BankFormat.Knab)]
    public void Importer_autodetects_format(string csv, BankFormat expected)
    {
        var importer = new BankStatementImporter();
        var mutations = importer.Parse(Bytes(csv)); // format Unknown -> auto-detect

        Assert.NotEmpty(mutations);
        var firstLine = csv.Split('\n')[0];
        Assert.Equal(expected, importer.DetectFormat(firstLine));
    }

    [Fact]
    public void Unrecognized_format_throws_clear_error()
    {
        var importer = new BankStatementImporter();
        var ex = Assert.Throws<NotSupportedException>(
            () => importer.Parse(Bytes("foo,bar,baz\n1,2,3\n")));
        Assert.Contains("KNAB", ex.Message);
        Assert.Contains("Rabobank", ex.Message);
    }

    [Theory]
    [InlineData("-12,50", -12.50)]
    [InlineData("+1.500,00", 1500.00)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("99.99", 99.99)]
    [InlineData("0,00", 0.00)]
    [InlineData("1.500", 1500.00)]     // Dutch thousands-only form, not 1.5
    [InlineData("1.500.000", 1500000.00)]
    [InlineData("-1.500", -1500.00)]
    [InlineData("1.50", 1.50)]         // two decimals -> a genuine decimal separator
    [InlineData("0.500", 0.50)]        // leading zero -> decimal, not thousands
    public void ParseAmount_handles_dutch_and_invariant_formats(string raw, double expected)
    {
        Assert.Equal((decimal)expected, CsvParsingHelpers.ParseAmount(raw));
    }

    [Theory]
    [InlineData("2024-01-15", 2024, 1, 15)]
    [InlineData("15-01-2024", 2024, 1, 15)]
    [InlineData("03/04/2024", 2024, 4, 3)] // day-first, never April 3rd read as March 4th
    [InlineData("20240115", 2024, 1, 15)]
    public void ParseDate_prefers_day_first_formats(string raw, int y, int m, int d)
    {
        Assert.Equal(new DateTime(y, m, d), CsvParsingHelpers.ParseDate(raw));
    }

    [Fact]
    public void Malformed_rows_are_counted_and_reported()
    {
        const string csv =
            "\"IBAN/BBAN\",\"Munt\",\"Volgnr\",\"Datum\",\"Bedrag\",\"Saldo na trn\",\"Naam tegenpartij\",\"Omschrijving-1\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"1\",\"2024-01-15\",\"-12,50\",\"987,50\",\"AH\",\"ok\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"2\",\"not-a-date\",\"-1,00\",\"986,50\",\"AH\",\"bad date\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"3\",\"2024-01-16\",\"-2,00\",\"984,50\",\"AH\",\"ok\"\n";

        var result = new RabobankCsvParser().Parse(new MemoryStream(Bytes(csv)));

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result.SkippedRows);
        var sample = Assert.Single(result.SkipSamples);
        Assert.Contains("row 2", sample);
    }

    [Fact]
    public void Bomless_windows1252_export_decodes_diacritics()
    {
        const string csv =
            "\"IBAN/BBAN\",\"Munt\",\"Volgnr\",\"Datum\",\"Bedrag\",\"Naam tegenpartij\",\"Omschrijving-1\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"1\",\"2024-01-15\",\"-4,50\",\"Café de Zon\",\"Koffie\"\n";

        // Encode as Windows-1252 (é = 0xE9), the historical ANSI default of Dutch bank exports.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252).GetBytes(csv);

        var result = new RabobankCsvParser().Parse(new MemoryStream(bytes));

        var row = Assert.Single(result.Mutations);
        Assert.Equal("Café de Zon", row.CounterpartyName);
    }

    [Fact]
    public void Rabobank_rows_without_volgnr_get_distinct_dedup_keys()
    {
        // Older export variants lack the sequence column; two identical rows must still import.
        const string csv =
            "\"IBAN/BBAN\",\"Munt\",\"Datum\",\"Bedrag\",\"Naam tegenpartij\",\"Omschrijving-1\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"2024-01-15\",\"-4,50\",\"Coffee Co\",\"Latte\"\n" +
            "\"NL11RABO0123456789\",\"EUR\",\"2024-01-15\",\"-4,50\",\"Coffee Co\",\"Latte\"\n";

        var rows = new RabobankCsvParser().Parse(new MemoryStream(Bytes(csv)));

        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].DedupKey, rows[1].DedupKey);

        // Stable across re-parses so re-imports still dedup.
        var again = new RabobankCsvParser().Parse(new MemoryStream(Bytes(csv)));
        Assert.Equal(rows[0].DedupKey, again[0].DedupKey);
        Assert.Equal(rows[1].DedupKey, again[1].DedupKey);
    }

    [Fact]
    public void DedupKey_is_stable_and_distinct()
    {
        var a = new KnabCsvParser().Parse(new MemoryStream(Bytes(KnabCsv)));
        var b = new KnabCsvParser().Parse(new MemoryStream(Bytes(KnabCsv)));

        // Same input -> same keys (so re-imports can be skipped)
        Assert.Equal(a[0].DedupKey, b[0].DedupKey);
        // Different rows -> different keys
        Assert.NotEqual(a[0].DedupKey, a[1].DedupKey);
    }
}
