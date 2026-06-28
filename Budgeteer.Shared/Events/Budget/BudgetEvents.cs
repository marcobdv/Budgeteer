namespace Budgeteer.Shared.Events.Budget;

/// <summary>
/// Published when Budget domain processes an expense transaction
/// </summary>
public record ExpenseRecorded(
    string ExpenseId,
    string TransactionId, // Reference to Account domain transaction
    DateTime Date,
    string Description,
    decimal Amount,
    string? Category,
    string? Payee,
    DateTime RecordedAt
);

/// <summary>
/// Published when Budget domain processes an income transaction
/// </summary>
public record IncomeRecorded(
    string IncomeId,
    string TransactionId, // Reference to Account domain transaction
    DateTime Date,
    string Description,
    decimal Amount,
    string? Category,
    string? Source,
    DateTime RecordedAt
);

/// <summary>
/// Published when Budget domain processes a transfer between accounts
/// </summary>
public record TransferRecorded(
    string TransferId,
    string FromTransactionId,
    string ToTransactionId,
    DateTime Date,
    decimal Amount,
    string FromAccount,
    string ToAccount,
    DateTime RecordedAt
);

/// <summary>
/// Published when a category is assigned to an expense
/// </summary>
public record ExpenseCategorized(
    string ExpenseId,
    string Category,
    DateTime CategorizedAt
);

/// <summary>
/// Published when a budget category is created
/// </summary>
public record CategoryCreated(
    string CategoryId,
    string Name,
    string? ParentCategoryId,
    DateTime CreatedAt
);
