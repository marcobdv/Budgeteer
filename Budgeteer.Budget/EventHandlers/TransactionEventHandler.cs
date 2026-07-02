using Budgeteer.Shared.Events.Accounts;
using Budgeteer.Shared.Events.Budget;
using Budgeteer.Budget.Domain;
using Budgeteer.Budget.Categorization;
using Marten;

namespace Budgeteer.Budget.EventHandlers;

/// <summary>
/// Listens to Account domain TransactionRecorded events
/// and creates corresponding Budget domain events (Expense/Income/Transfer)
/// This is the integration point between the two domains
/// </summary>
public class TransactionEventHandler
{
    private readonly TransactionCategorizer _categorizer;

    public TransactionEventHandler(TransactionCategorizer categorizer)
    {
        _categorizer = categorizer;
    }

    /// <summary>
    /// Appends the account event and its derived Budget-domain event to the given session WITHOUT
    /// saving, after resolving the category. Lets a caller (e.g. manual add) commit the account
    /// stream and the budget projection atomically in one SaveChanges, the same guarantee bulk
    /// import relies on, so a transaction can never appear in the ledger without its budget view.
    /// </summary>
    public async Task RecordAndProjectAsync(IDocumentSession session, string accountId, TransactionRecorded accountEvent)
    {
        session.Events.Append(accountId, accountEvent);
        await ProjectAsync(session, accountEvent);
    }

    /// <summary>
    /// Resolves the category and appends only the derived Budget-domain event to the session,
    /// WITHOUT saving. For callers that append the account event themselves — e.g. via a
    /// FetchForWriting stream, so the account append carries an expected version.
    /// </summary>
    public async Task ProjectAsync(IDocumentSession session, TransactionRecorded accountEvent)
    {
        var rules = await _categorizer.GetRulesAsync();
        var category = TransactionCategorizer.Match(
            rules, accountEvent.Payee, accountEvent.Description, accountEvent.Amount);
        Project(session, accountEvent, category);
    }

    /// <summary>
    /// Appends the Budget-domain event for a transaction to the given session WITHOUT saving, so the
    /// caller can commit it atomically alongside the account stream (and avoid a per-transaction
    /// round trip during bulk import). The <paramref name="category"/> is resolved by the caller.
    /// Zero-amount transactions are recorded as a (zero) expense so the budget reconciles with the
    /// transaction list rather than silently dropping them.
    /// </summary>
    public void Project(IDocumentSession session, TransactionRecorded accountEvent, string? category)
    {
        // Negative or zero amount = expense; positive = income.
        if (accountEvent.Amount <= 0)
        {
            var expenseEvent = Expense.CreateFromTransaction(
                transactionId: accountEvent.TransactionId,
                date: accountEvent.TransactionDate,
                description: accountEvent.Description,
                amount: accountEvent.Amount,
                payee: accountEvent.Payee,
                category: category);

            session.Events.StartStream<Expense>(expenseEvent.ExpenseId, expenseEvent);
        }
        else
        {
            var incomeEvent = new IncomeRecorded(
                IncomeId: Guid.NewGuid().ToString(),
                TransactionId: accountEvent.TransactionId,
                Date: accountEvent.TransactionDate,
                Description: accountEvent.Description,
                Amount: accountEvent.Amount,
                Category: category,
                Source: accountEvent.Payee,
                RecordedAt: DateTime.UtcNow);

            session.Events.StartStream<Income>(incomeEvent.IncomeId, incomeEvent);
        }
    }
}
