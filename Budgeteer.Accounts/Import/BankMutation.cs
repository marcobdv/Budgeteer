namespace Budgeteer.Accounts.Import;

/// <summary>
/// A normalized bank mutation parsed from a bank CSV export.
/// All bank-specific formats are mapped onto this shape before being
/// turned into <see cref="Budgeteer.Shared.Events.Accounts.TransactionRecorded"/> events.
/// </summary>
public record BankMutation
{
    /// <summary>IBAN of the account the mutation belongs to (the "own" account).</summary>
    public string AccountIban { get; init; } = string.Empty;

    /// <summary>Booking date of the transaction.</summary>
    public DateTime Date { get; init; }

    /// <summary>Signed amount. Positive = money in (income), negative = money out (expense).</summary>
    public decimal Amount { get; init; }

    /// <summary>Currency code, e.g. "EUR".</summary>
    public string Currency { get; init; } = "EUR";

    /// <summary>Human readable description / payment reference.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Name of the counterparty (payee for expenses, source for income).</summary>
    public string? CounterpartyName { get; init; }

    /// <summary>IBAN/account number of the counterparty, when present.</summary>
    public string? CounterpartyIban { get; init; }

    /// <summary>
    /// A stable key derived from the source row, used to detect and skip duplicate imports.
    /// </summary>
    public string DedupKey { get; init; } = string.Empty;

    /// <summary>
    /// Running account balance immediately after this transaction, when the export provides it
    /// (e.g. Rabobank's "Saldo na trn"). Used to reconcile for missing/duplicate rows.
    /// </summary>
    public decimal? BalanceAfter { get; init; }
}
