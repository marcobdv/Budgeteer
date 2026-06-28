using Budgeteer.Accounts.ReadModels;
using Marten;

namespace Budgeteer.Web.Services;

/// <summary>A transaction enriched with its account name and current budget category.</summary>
public record TransactionRow(
    string TransactionId,
    string AccountId,
    string AccountName,
    DateTime Date,
    string Description,
    string? Payee,
    decimal Amount,            // signed: + income, - expense
    string? Category,
    bool IsTransfer = false);  // a leg of a transfer between own accounts

/// <summary>Which direction of money flow to include.</summary>
public enum FlowFilter { All, IncomeOnly, ExpenseOnly }

/// <summary>Criteria for filtering transaction rows.</summary>
public record TransactionFilter(
    string? AccountId = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Category = null,   // null = any; "Uncategorized" matches null/empty
    string? Search = null,
    FlowFilter Flow = FlowFilter.All);

/// <summary>
/// Builds enriched transaction rows by joining the account-domain transactions with the
/// current categories from the budget domain, and applies filtering. Backs both the
/// dashboard and the Transactions page.
/// </summary>
public sealed class TransactionQueryService
{
    public const string Uncategorized = "Uncategorized";

    private readonly IDocumentStore _store;

    public TransactionQueryService(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<List<TransactionRow>> LoadAllAsync()
    {
        // One session, all read models are inline projections — straight document queries, no replay.
        await using var session = _store.QuerySession();

        var accountNames = (await session.Query<AccountSummary>().ToListAsync())
            .ToDictionary(a => a.Id, a => a.Name);
        var txns = await session.Query<TransactionView>().ToListAsync();
        var expenses = await session.Query<Budgeteer.Budget.ReadModels.ExpenseView>().ToListAsync();
        var incomes = await session.Query<Budgeteer.Budget.Domain.Income>().ToListAsync();
        var transferLinks = await session.Query<TransferLink>().ToListAsync();

        // Current category per transaction (expenses reflect re-categorizations; income is as recorded).
        var categoryByTxn = new Dictionary<string, string?>();
        foreach (var e in expenses)
            categoryByTxn[e.TransactionId] = e.Category;
        foreach (var i in incomes)
            categoryByTxn[i.TransactionId] = i.Category;

        var transferIds = new HashSet<string>();
        foreach (var l in transferLinks)
        {
            transferIds.Add(l.FromTransactionId);
            transferIds.Add(l.ToTransactionId);
        }

        return txns
            .Select(t => new TransactionRow(
                t.Id,
                t.AccountId,
                accountNames.TryGetValue(t.AccountId, out var n) ? n : "Unknown",
                t.Date,
                t.Description,
                t.Payee,
                t.Amount,
                categoryByTxn.TryGetValue(t.Id, out var c) ? c : null,
                transferIds.Contains(t.Id)))
            .OrderByDescending(r => r.Date)
            .ToList();
    }

    /// <summary>Applies a filter to already-loaded rows (pure, so it's easy to test).</summary>
    public static IReadOnlyList<TransactionRow> Apply(IEnumerable<TransactionRow> rows, TransactionFilter f)
    {
        IEnumerable<TransactionRow> q = rows;

        if (!string.IsNullOrWhiteSpace(f.AccountId))
            q = q.Where(r => r.AccountId == f.AccountId);
        if (f.From is { } from)
            q = q.Where(r => r.Date.Date >= from.Date);
        if (f.To is { } to)
            q = q.Where(r => r.Date.Date <= to.Date);
        // Transfers between own accounts are neither income nor expense.
        if (f.Flow == FlowFilter.IncomeOnly)
            q = q.Where(r => r.Amount > 0 && !r.IsTransfer);
        else if (f.Flow == FlowFilter.ExpenseOnly)
            q = q.Where(r => r.Amount < 0 && !r.IsTransfer);

        if (!string.IsNullOrWhiteSpace(f.Category))
        {
            if (string.Equals(f.Category, Uncategorized, StringComparison.OrdinalIgnoreCase))
                q = q.Where(r => string.IsNullOrWhiteSpace(r.Category));
            else
                q = q.Where(r => string.Equals(r.Category, f.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(r =>
                (r.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Payee?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return q.ToList();
    }

    /// <summary>Distinct category labels present in the rows (uncategorized normalized).</summary>
    public static IReadOnlyList<string> DistinctCategories(IEnumerable<TransactionRow> rows) =>
        rows.Select(r => string.IsNullOrWhiteSpace(r.Category) ? Uncategorized : r.Category!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();
}
