using Budgeteer.Shared.Events.Accounts;

namespace Budgeteer.Accounts.ReadModels;

/// <summary>
/// Read model for an account, maintained as an inline Marten snapshot from the account's
/// event stream. Kept separate from the <see cref="Domain.Account"/> aggregate (which carries
/// command/factory methods) so it is a clean projection target: only <c>Apply</c> methods.
/// </summary>
public class AccountSummary
{
    public string Id { get; set; } = string.Empty; // = account stream key
    public string Name { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string? Iban { get; set; }
    public decimal Balance { get; set; }
    public int TransactionCount { get; set; }
    public DateTime CreatedAt { get; set; }

    public void Apply(AccountCreated e)
    {
        Id = e.AccountId;
        Name = e.Name;
        AccountType = e.AccountType;
        Iban = e.Iban;
        Balance = e.InitialBalance;
        CreatedAt = e.CreatedAt;
    }

    public void Apply(TransactionRecorded e)
    {
        Balance += e.Amount;
        TransactionCount++;
    }

    public void Apply(AccountBalanceChanged e) => Balance = e.NewBalance;

    public void Apply(TransactionDeleted e)
    {
        Balance -= e.Amount;
        TransactionCount--;
    }
}
