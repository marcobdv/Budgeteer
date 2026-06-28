namespace Budgeteer.Accounts;

/// <summary>Helpers for comparing IBANs/account numbers regardless of spacing or case.</summary>
public static class Iban
{
    /// <summary>Strips non-alphanumeric characters and upper-cases, for tolerant equality checks.</summary>
    public static string Normalize(string? iban) =>
        new string((iban ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
