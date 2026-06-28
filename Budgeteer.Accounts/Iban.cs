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

    public Iban(string? raw) => _normalized = Normalize(raw);

    /// <summary>An absent/blank IBAN.</summary>
    public static readonly Iban Empty = default;

    /// <summary>Builds an <see cref="Iban"/> from a raw string (null/blank becomes <see cref="Empty"/>).</summary>
    public static Iban From(string? raw) => new(raw);

    /// <summary>True when no IBAN is present.</summary>
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
