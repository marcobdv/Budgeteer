using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Budgeteer.Accounts.Import;

/// <summary>
/// Helpers shared by the bank-specific parsers: tolerant amount/date parsing
/// (Dutch banks use a comma as the decimal separator) and dedup-key generation.
/// </summary>
internal static class CsvParsingHelpers
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>
    /// Parses a monetary amount that may use either a comma or a dot as the decimal
    /// separator and may include thousands separators or a leading +/- sign.
    /// </summary>
    public static decimal ParseAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0m;

        var s = raw.Trim().Replace(" ", "").Replace(" ", "");

        // Normalize: if both separators occur, the last one is the decimal separator.
        int lastComma = s.LastIndexOf(',');
        int lastDot = s.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            if (lastComma > lastDot)
            {
                // 1.234,56 -> dot is thousands, comma is decimal
                s = s.Replace(".", "").Replace(',', '.');
            }
            else
            {
                // 1,234.56 -> comma is thousands, dot is decimal
                s = s.Replace(",", "");
            }
        }
        else if (lastComma >= 0)
        {
            // Only comma present -> treat as decimal separator
            s = s.Replace(',', '.');
        }
        // else: only dot or no separator -> already invariant-friendly

        return decimal.Parse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses an optional amount, returning null when blank or unparseable.</summary>
    public static decimal? ParseOptionalAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try { return ParseAmount(raw); }
        catch (Exception ex) when (ex is FormatException or OverflowException) { return null; }
    }

    /// <summary>
    /// Parses a date trying a set of known formats before falling back to culture-aware parsing.
    /// </summary>
    public static DateTime ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Empty date value.");

        var s = raw.Trim();
        string[] formats =
        {
            "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy/MM/dd",
            "dd-MM-yy", "yyyyMMdd", "d-M-yyyy", "M/d/yyyy"
        };

        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParse(s, Nl, DateTimeStyles.None, out dt))
            return dt;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;

        throw new FormatException($"Unrecognized date format: '{raw}'.");
    }

    /// <summary>
    /// Reads a CSV stream into a list of rows, each row a case-insensitive dictionary
    /// keyed by (trimmed) header name. Resilient to ragged rows and bad data.
    /// </summary>
    public static List<Dictionary<string, string>> ReadRows(Stream csv, string delimiter)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            DetectColumnCountChanges = false,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
        };

        using var csvReader = new CsvReader(reader, config);
        if (!csvReader.Read() || !csvReader.ReadHeader())
            return new List<Dictionary<string, string>>();

        var headers = (csvReader.HeaderRecord ?? Array.Empty<string>())
            .Select(h => (h ?? string.Empty).Trim())
            .ToArray();

        var rows = new List<Dictionary<string, string>>();
        while (csvReader.Read())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
            {
                if (string.IsNullOrEmpty(headers[i]))
                    continue;
                row[headers[i]] = csvReader.GetField(i) ?? string.Empty;
            }
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Returns the first non-empty value among the given candidate column names.</summary>
    public static string Field(this Dictionary<string, string> row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return string.Empty;
    }

    /// <summary>
    /// Produces a deterministic dedup key from the identifying fields of a mutation.
    /// Re-importing the same export will produce the same keys, so duplicates can be skipped.
    /// </summary>
    public static string MakeDedupKey(params string?[] parts)
    {
        var joined = string.Join("|", parts.Select(p => (p ?? string.Empty).Trim()));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
