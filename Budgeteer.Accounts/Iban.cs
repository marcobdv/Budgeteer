namespace Budgeteer.Accounts;

/// <summary>
/// An IBAN / bank account number as a value type. Equality is on the normalized form (spacing and
/// case stripped), so two IBANs written differently compare equal — which is exactly what the
/// account-matching and transfer-detection logic needs. Replaces passing raw <see cref="string"/>s
/// around and calling a normalize helper at every comparison site.
/// </summary>
public readonly record struct Iban
{
    private readonly string? _normalized;

    // The parameter is named after the Value property (and marked as the JSON constructor) so
    // System.Text.Json round-trips the struct: without it, deserializing {"Value":"NL91..."}
    // would silently produce Iban.Empty — a data wipe if this type is ever stored in a document.
    [System.Text.Json.Serialization.JsonConstructor]
    public Iban(string? value) => _normalized = Normalize(value);

    /// <summary>An absent/blank IBAN.</summary>
    public static readonly Iban Empty = default;

    /// <summary>Builds an <see cref="Iban"/> from a raw string (null/blank becomes <see cref="Empty"/>).
    /// Deliberately tolerant — no checksum — so it can be used for matching bank-CSV data;
    /// validate user-typed IBANs with <see cref="TryParse"/> instead.</summary>
    public static Iban From(string? raw) => new(raw);

    /// <summary>
    /// Parses a user-entered IBAN with real validation (length, structure and the ISO 13616
    /// mod-97 checksum), so a typo'd IBAN is rejected instead of silently never matching imports.
    /// </summary>
    public static bool TryParse(string? raw, out Iban iban)
    {
        iban = From(raw);
        if (iban.IsEmpty || !PassesChecksum(iban.Value))
        {
            iban = Empty;
            return false;
        }
        return true;
    }

    // ISO 13616: two-letter country code, two check digits, mod-97 of the rearranged
    // alphanumerics equals 1. Length is checked loosely (15–34) rather than per country.
    private static bool PassesChecksum(string normalized)
    {
        if (normalized.Length is < 15 or > 34)
            return false;
        if (!char.IsAsciiLetter(normalized[0]) || !char.IsAsciiLetter(normalized[1]) ||
            !char.IsAsciiDigit(normalized[2]) || !char.IsAsciiDigit(normalized[3]))
            return false;

        var rearranged = string.Concat(normalized.AsSpan(4), normalized.AsSpan(0, 4));
        int remainder = 0;
        foreach (var c in rearranged)
        {
            int v = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = v < 10 ? (remainder * 10 + v) % 97 : (remainder * 100 + v) % 97;
        }
        return remainder == 1;
    }

    /// <summary>True when no IBAN is present.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => string.IsNullOrEmpty(_normalized);

    /// <summary>The normalized value (upper-cased, alphanumerics only); empty string when absent.</summary>
    public string Value => _normalized ?? string.Empty;

    /// <summary>The normalized value, or null when absent — for storing in nullable string columns/events.</summary>
    public string? ToNullableString() => IsEmpty ? null : Value;

    public override string ToString() => Value;

    public bool Equals(Iban other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>Strips non-alphanumeric characters and upper-cases, for tolerant equality.</summary>
    public static string Normalize(string? iban) =>
        new string((iban ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
