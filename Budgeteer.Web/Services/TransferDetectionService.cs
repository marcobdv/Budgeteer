using Budgeteer.Accounts;
using Budgeteer.Accounts.ReadModels;
using Marten;

namespace Budgeteer.Web.Services;

/// <summary>
/// Detects transfers between the user's own accounts by pairing opposite, equal-amount
/// transactions whose counterparty IBAN references another owned account. Detected pairs are
/// stored as <see cref="TransferLink"/> documents and excluded from income/expense/spending.
/// </summary>
public sealed class TransferDetectionService
{
    private const int DayWindow = 4;

    private readonly IDocumentStore _store;

    public TransferDetectionService(IDocumentStore store) => _store = store;

    /// <summary>Runs detection over the current read models, persisting any newly found pairs.</summary>
    public async Task<int> DetectAsync()
    {
        await using var session = _store.LightweightSession();
        var txns = await session.Query<TransactionView>().ToListAsync();
        var accounts = await session.Query<AccountSummary>().ToListAsync();
        var existing = (await session.Query<TransferLink>().ToListAsync())
            .Select(l => l.Id).ToHashSet();

        var links = Detect(txns, accounts);
        int added = 0;
        foreach (var link in links)
        {
            if (existing.Add(link.Id))
            {
                session.Store(link);
                added++;
            }
        }
        if (added > 0)
            await session.SaveChangesAsync();
        return added;
    }

    /// <summary>Transaction ids that are part of a detected transfer (both legs).</summary>
    public async Task<HashSet<string>> GetTransferTransactionIdsAsync()
    {
        await using var session = _store.QuerySession();
        var links = await session.Query<TransferLink>().ToListAsync();
        var ids = new HashSet<string>();
        foreach (var l in links)
        {
            ids.Add(l.FromTransactionId);
            ids.Add(l.ToTransactionId);
        }
        return ids;
    }

    /// <summary>
    /// Pure detection: pairs each outgoing transaction with a matching incoming transaction in a
    /// different owned account. A pair must have equal magnitude, fall within a few days, and have
    /// BOTH legs' counterparty IBANs reference each other's account (mutual reference) — this avoids
    /// mis-pairing unrelated equal-amount transactions. Candidates are grouped by amount (no O(n^2)
    /// full scan) and iterated in a deterministic order so pairing is stable across runs.
    /// </summary>
    public static IReadOnlyList<TransferLink> Detect(
        IReadOnlyList<TransactionView> txns, IReadOnlyList<AccountSummary> accounts)
    {
        var ibanByAccount = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Iban))
            .ToDictionary(a => a.Id, a => Iban.From(a.Iban));

        // Incoming candidates indexed by amount, each list deterministically ordered.
        var incomingByAmount = txns
            .Where(t => t.Amount > 0)
            .OrderBy(t => t.Date).ThenBy(t => t.Id, StringComparer.Ordinal)
            .GroupBy(t => t.Amount)
            .ToDictionary(g => g.Key, g => g.ToList());

        var used = new HashSet<string>();
        var links = new List<TransferLink>();

        foreach (var o in txns.Where(t => t.Amount < 0)
                               .OrderBy(t => t.Date).ThenBy(t => t.Id, StringComparer.Ordinal))
        {
            if (used.Contains(o.Id)) continue;
            if (!incomingByAmount.TryGetValue(-o.Amount, out var candidates)) continue;

            TransactionView? best = null;
            int bestDays = int.MaxValue;
            foreach (var i in candidates) // already deterministically ordered
            {
                if (used.Contains(i.Id) || i.AccountId == o.AccountId) continue;
                var days = Math.Abs((i.Date.Date - o.Date.Date).Days);
                if (days > DayWindow) continue;
                if (!MutuallyReference(o, i, ibanByAccount)) continue;
                if (days < bestDays) { best = i; bestDays = days; }
            }

            if (best is not null)
            {
                used.Add(o.Id);
                used.Add(best.Id);
                links.Add(new TransferLink
                {
                    Id = MakeId(o.Id, best.Id),
                    FromTransactionId = o.Id,
                    ToTransactionId = best.Id,
                    FromAccountId = o.AccountId,
                    ToAccountId = best.AccountId,
                    Amount = Math.Abs(o.Amount),
                    Date = o.Date
                });
            }
        }

        return links;
    }

    // Both legs must name the other's own account by IBAN. Requiring a mutual reference (rather than
    // either side) prevents an unrelated equal-amount payment/refund from being mistaken for a transfer.
    private static bool MutuallyReference(
        TransactionView outgoing, TransactionView incoming, IReadOnlyDictionary<string, Iban> ibanByAccount)
    {
        var outCp = Iban.From(outgoing.CounterpartyIban);
        var inCp = Iban.From(incoming.CounterpartyIban);
        if (outCp.IsEmpty || inCp.IsEmpty)
            return false;
        var outOwn = ibanByAccount.TryGetValue(outgoing.AccountId, out var a) ? a : Iban.Empty;
        var inOwn = ibanByAccount.TryGetValue(incoming.AccountId, out var b) ? b : Iban.Empty;
        return !inOwn.IsEmpty && !outOwn.IsEmpty && outCp == inOwn && inCp == outOwn;
    }

    private static string MakeId(string a, string b)
    {
        var (x, y) = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        return $"transfer:{x}:{y}";
    }
}
