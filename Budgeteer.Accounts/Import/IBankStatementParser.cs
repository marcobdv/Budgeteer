namespace Budgeteer.Accounts.Import;

/// <summary>
/// Identifies the supported bank export formats.
/// </summary>
public enum BankFormat
{
    Unknown = 0,
    Knab = 1,
    Rabobank = 2
}

/// <summary>
/// Parses a single bank's CSV export into a stream of normalized <see cref="BankMutation"/>s.
/// </summary>
public interface IBankStatementParser
{
    /// <summary>The bank format this parser handles.</summary>
    BankFormat Format { get; }

    /// <summary>
    /// Returns true if the given CSV header line looks like it belongs to this bank's format.
    /// Used for auto-detection.
    /// </summary>
    bool CanParse(string headerLine);

    /// <summary>Parses the full CSV content into normalized mutations.</summary>
    IReadOnlyList<BankMutation> Parse(Stream csv);
}
