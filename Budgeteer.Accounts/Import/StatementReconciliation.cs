namespace Budgeteer.Accounts.Import;

/// <summary>A break in the running-balance chain, i.e. a likely missing or duplicated transaction.</summary>
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
        // Only meaningful if the export carries running balances on at least two rows.
        var withBalance = mutations.Where(m => m.BalanceAfter is not null).ToList();
        if (withBalance.Count < 2)
            return ReconciliationResult.NotAvailable;

        // The export lists rows in a consistent posting order, but it may be oldest-first or
        // newest-first. We can't recover the true order by sorting on the (date-only) booking date,
        // since same-day rows would be reordered arbitrarily and break the chain. Instead we trust
        // the file's own order and check the running-balance chain both as-is and reversed, treating
        // the statement as consistent if either orientation holds.
        var forward = ChainGaps(withBalance);
        if (forward.Count == 0)
            return new ReconciliationResult(Checked: true, Consistent: true, Gaps: forward);

        var backward = ChainGaps(Enumerable.Reverse(withBalance).ToList());
        if (backward.Count == 0)
            return new ReconciliationResult(Checked: true, Consistent: true, Gaps: backward);

        // Neither orientation is fully consistent — surface the smaller set of breaks.
        var gaps = backward.Count < forward.Count ? backward : forward;
        return new ReconciliationResult(Checked: true, Consistent: false, Gaps: gaps);
    }

    // Verifies prev.BalanceAfter + curr.Amount == curr.BalanceAfter down the list, in the given order.
    private static List<ReconciliationGap> ChainGaps(IReadOnlyList<BankMutation> ordered)
    {
        var gaps = new List<ReconciliationGap>();
        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1].BalanceAfter!.Value;
            var expected = prev + ordered[i].Amount;
            var actual = ordered[i].BalanceAfter!.Value;
            if (expected != actual)
                gaps.Add(new ReconciliationGap(i, expected, actual));
        }
        return gaps;
    }
}
