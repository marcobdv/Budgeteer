using Budgeteer.Shared.Events.Accounts;

namespace Budgeteer.Accounts.Domain;

/// <summary>
/// Account aggregate - manages bank accounts and their transactions
/// Uses Marten's event sourcing capabilities
/// </summary>
public class Account
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string AccountType { get; private set; } = string.Empty;
    public string? Iban { get; private set; }
    public decimal Balance { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public List<string> TransactionIds { get; private set; } = new();

    /// <summary>Dedup keys of transactions already imported from CSV, to prevent duplicates.</summary>
    public HashSet<string> ImportKeys { get; private set; } = new();

    // For Marten event sourcing - apply events to rebuild state
    public void Apply(AccountCreated evt)
    {
        Id = evt.AccountId;
        Name = evt.Name;
        AccountType = evt.AccountType;
        Iban = evt.Iban;
        Balance = evt.InitialBalance;
        CreatedAt = evt.CreatedAt;
    }

    public void Apply(TransactionRecorded evt)
    {
        Balance += evt.Amount;
        TransactionIds.Add(evt.TransactionId);
        if (!string.IsNullOrEmpty(evt.ImportKey))
            ImportKeys.Add(evt.ImportKey);
    }

    public void Apply(AccountBalanceChanged evt)
    {
        Balance = evt.NewBalance;
    }

    public void Apply(TransactionDeleted evt)
    {
        Balance -= evt.Amount;
        TransactionIds.Remove(evt.TransactionId);
        if (!string.IsNullOrEmpty(evt.ImportKey))
            ImportKeys.Remove(evt.ImportKey); // allow the row to be re-imported
    }

    // Factory method to create a new account
    public static AccountCreated Create(string name, string accountType, decimal initialBalance, string? iban = null)
    {
        return new AccountCreated(
            AccountId: Guid.NewGuid().ToString(),
            Name: name,
            AccountType: accountType,
            InitialBalance: initialBalance,
            CreatedAt: DateTime.UtcNow,
            Iban: string.IsNullOrWhiteSpace(iban) ? null : iban.Trim()
        );
    }

    // Business logic: Record a transaction
    public TransactionRecorded RecordTransaction(
        string description,
        decimal amount,
        DateTime transactionDate,
        string? payee = null,
        string? importKey = null,
        string? counterpartyIban = null)
    {
        return new TransactionRecorded(
            TransactionId: Guid.NewGuid().ToString(),
            AccountId: Id,
            TransactionDate: transactionDate,
            Description: description,
            Amount: amount,
            Payee: payee,
            RecordedAt: DateTime.UtcNow,
            ImportKey: importKey,
            CounterpartyIban: counterpartyIban
        );
    }

    /// <summary>
    /// Returns true if a mutation with the given import dedup key has already been recorded.
    /// </summary>
    public bool HasImported(string dedupKey) => ImportKeys.Contains(dedupKey);
}
