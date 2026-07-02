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
/// Published when a category is assigned to an expense
/// </summary>
public record ExpenseCategorized(
    string ExpenseId,
    string Category,
    DateTime CategorizedAt
);

/// <summary>
/// Published when a category is assigned to an income
/// </summary>
public record IncomeCategorized(
    string IncomeId,
    string Category,
    DateTime CategorizedAt
);
