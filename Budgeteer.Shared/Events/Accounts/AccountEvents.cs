namespace Budgeteer.Shared.Events.Accounts;

/// <summary>
/// Published when a new account is created in the system
/// </summary>
public record AccountCreated(
    string AccountId,
    string Name,
    string AccountType, // e.g., "Checking", "Savings", "Credit Card"
    decimal InitialBalance,
    DateTime CreatedAt,
    string? Iban = null // bank account number, used to match imported mutations
);

/// <summary>
/// Published when a transaction is recorded in an account
/// This is the source event that Budget domain will react to
/// </summary>
public record TransactionRecorded(
    string TransactionId,
    string AccountId,
    DateTime TransactionDate,
    string Description,
    decimal Amount, // Positive = income, Negative = expense
    string? Payee,
    DateTime RecordedAt,
    string? ImportKey = null, // stable key from a CSV import row, used to skip duplicates
    string? CounterpartyIban = null
);

/// <summary>
/// Published when a previously recorded transaction is removed from an account.
/// Carries the original amount (to reverse the balance) and import key (to allow re-import).
/// </summary>
public record TransactionDeleted(
    string TransactionId,
    string AccountId,
    decimal Amount,
    string? ImportKey,
    DateTime DeletedAt
);
