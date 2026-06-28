using System.Text;

namespace Budgeteer.Accounts.Import;

/// <summary>
/// Detects the bank format of a CSV export and parses it into normalized mutations.
/// This type is pure (no database access) so it is trivially testable; persistence
/// of the resulting mutations as events is handled by the caller.
/// </summary>
public sealed class BankStatementImporter
{
    private readonly IReadOnlyList<IBankStatementParser> _parsers;

    public BankStatementImporter()
        : this(new IBankStatementParser[] { new KnabCsvParser(), new RabobankCsvParser() })
    {
    }

    public BankStatementImporter(IEnumerable<IBankStatementParser> parsers)
    {
        _parsers = parsers.ToList();
    }

    /// <summary>
    /// Inspects the first (header) line of the CSV to determine which bank produced it.
    /// Returns <see cref="BankFormat.Unknown"/> if no parser recognizes it.
    /// </summary>
    public BankFormat DetectFormat(string headerLine)
    {
        foreach (var parser in _parsers)
        {
            if (parser.CanParse(headerLine))
                return parser.Format;
        }
        return BankFormat.Unknown;
    }

    /// <summary>
    /// Parses the CSV. When <paramref name="format"/> is <see cref="BankFormat.Unknown"/>,
    /// the format is auto-detected from the header line.
    /// </summary>
    public IReadOnlyList<BankMutation> Parse(byte[] content, BankFormat format = BankFormat.Unknown)
    {
        if (format == BankFormat.Unknown)
            format = DetectFormat(ReadFirstLine(content));

        var parser = _parsers.FirstOrDefault(p => p.Format == format)
            ?? throw new NotSupportedException(
                "Could not recognize the CSV format. Supported exports: KNAB and Rabobank.");

        using var stream = new MemoryStream(content);
        return parser.Parse(stream);
    }

    private static string ReadFirstLine(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        // Skip any leading blank lines (some exports start with one).
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }
        return string.Empty;
    }
}
