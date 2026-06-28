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
    public void ParseAmount_handles_dutch_and_invariant_formats(string raw, double expected)
    {
        Assert.Equal((decimal)expected, CsvParsingHelpers.ParseAmount(raw));
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
