namespace Budgeteer.Accounts.Import;

/// <summary>A break in the running-balance chain, i.e. a likely missing or duplicated transaction.
/// <see cref="RowIndex"/> is the 0-based index of the mutation in the parsed file.</summary>
public record ReconciliationGap(int RowIndex, decimal ExpectedBalance, decimal ActualBalance)
{
    public decimal Difference => ActualBalance - ExpectedBalance;
}

/// <summary>Outcome of reconciling a statement's running balances.</summary>
public record ReconciliationResult(bool Checked, bool Consistent, IReadOnlyList<ReconciliationGap> Gaps)
{
    public static readonly ReconciliationResult NotAvailable = new(false, true, Array.Empty<ReconciliationGap>());
}

/// <summary>
/// Verifies that an export's per-row running balances form a continuous chain:
/// for each consecutive pair, previousBalance + currentAmount must equal currentBalance.
/// A break means a transaction is missing from (or duplicated in) the export.
/// </summary>
public static class StatementReconciliation
{
    public static ReconciliationResult Check(IReadOnlyList<BankMutation> mutations)
    {
        // Running balances are per account: a combined multi-account export (e.g. Rabobank's
        // all-accounts download) restarts the chain at every account boundary, so each account's
        // rows must be chained separately or every boundary would show up as a false gap.
        // Original file indices are kept so reported row numbers refer to the actual file rows.
        var indexed = mutations
            .Select((m, i) => (Mutation: m, Index: i))
            .Where(x => x.Mutation.BalanceAfter is not null)
            .ToList();

        bool anyChecked = false;
        var gaps = new List<ReconciliationGap>();

        foreach (var group in indexed.GroupBy(x => x.Mutation.AccountIban))
        {
            // Only meaningful when the account has running balances on at least two rows.
            var rows = group.ToList();
            if (rows.Count < 2)
                continue;
            anyChecked = true;

            // The export lists rows in a consistent posting order, but it may be oldest-first or
            // newest-first. We can't recover the true order by sorting on the (date-only) booking
            // date, since same-day rows would be reordered arbitrarily and break the chain. Instead
            // we trust the file's own order and check the running-balance chain both as-is and
            // reversed, treating the account as consistent if either orientation holds.
            var forward = ChainGaps(rows);
            if (forward.Count == 0)
                continue;

            var backward = ChainGaps(Enumerable.Reverse(rows).ToList());
            if (backward.Count == 0)
                continue;

            // Neither orientation is fully consistent — surface the smaller set of breaks.
            gaps.AddRange(backward.Count < forward.Count ? backward : forward);
        }

        if (!anyChecked)
            return ReconciliationResult.NotAvailable;

        return new ReconciliationResult(
            Checked: true,
            Consistent: gaps.Count == 0,
            Gaps: gaps.OrderBy(g => g.RowIndex).ToList());
    }

    // Verifies prev.BalanceAfter + curr.Amount == curr.BalanceAfter down the list, in the given
    // order. Gaps carry the original file index of the row where the chain broke.
    private static List<ReconciliationGap> ChainGaps(IReadOnlyList<(BankMutation Mutation, int Index)> ordered)
    {
        var gaps = new List<ReconciliationGap>();
        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1].Mutation.BalanceAfter!.Value;
            var expected = prev + ordered[i].Mutation.Amount.Value;
            var actual = ordered[i].Mutation.BalanceAfter!.Value;
            if (expected != actual)
                gaps.Add(new ReconciliationGap(ordered[i].Index, expected, actual));
        }
        return gaps;
    }
}
