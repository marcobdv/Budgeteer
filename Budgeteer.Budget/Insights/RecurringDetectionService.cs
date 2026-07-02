using Budgeteer.Budget.ReadModels;

namespace Budgeteer.Budget.Insights;

/// <summary>A detected recurring payment (e.g. a subscription).</summary>
public record RecurringPayment(
    string Payee,
    string Cadence,        // "Weekly" | "Monthly" | "Quarterly" | "Yearly"
    int IntervalDays,
    decimal TypicalAmount, // median magnitude
    decimal LastAmount,
    DateTime LastDate,
    DateTime NextExpected,
    int Occurrences,
    bool AmountChanged);   // last charge differs from the typical amount

/// <summary>
/// Finds expenses that recur on a regular cadence (subscriptions, rent, utilities…) by grouping
/// on payee and looking for a consistent interval across at least three occurrences.
/// Pure and deterministic so it is easy to test.
/// </summary>
public static class RecurringDetectionService
{
    private const int MinOccurrences = 3;

    // (label, target interval in days, tolerance)
    private static readonly (string Label, int Days, int Tol)[] Cadences =
    {
        ("Weekly", 7, 2),
        ("Biweekly", 14, 2),
        ("Monthly", 30, 5),
        ("Quarterly", 91, 12),
        ("Yearly", 365, 25),
    };

    /// <param name="expenses">Candidate expenses, grouped by payee.</param>
    /// <param name="excludeTransactionIds">
    /// Transactions to leave out — pass the transfer-leg ids, or a monthly transfer to your own
    /// savings account (stable payee, amount and cadence) is reported as a "subscription".
    /// </param>
    public static IReadOnlyList<RecurringPayment> Detect(
        IEnumerable<ExpenseView> expenses,
        IReadOnlyCollection<string>? excludeTransactionIds = null)
    {
        var results = new List<RecurringPayment>();

        var groups = expenses
            .Where(e => !string.IsNullOrWhiteSpace(e.Payee))
            .Where(e => excludeTransactionIds is null || !excludeTransactionIds.Contains(e.TransactionId))
            .GroupBy(e => e.Payee!.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups)
        {
            var items = g.OrderBy(e => e.Date).ToList();
            if (items.Count < MinOccurrences)
                continue;

            var intervals = new List<int>();
            for (int i = 1; i < items.Count; i++)
                intervals.Add((items[i].Date.Date - items[i - 1].Date.Date).Days);

            var medianInterval = Median(intervals);
            var cadence = Cadences.FirstOrDefault(c => Math.Abs(medianInterval - c.Days) <= c.Tol);
            if (cadence.Label is null)
                continue; // no regular cadence

            // Require the intervals to be reasonably consistent: most within tolerance of the
            // cadence, and at least two in-tolerance regardless. With only two intervals the
            // old "allow one irregular gap" rule needed just ONE match, so almost any pair of
            // gaps whose average landed near a cadence counted as recurring.
            var regular = intervals.Count(d => Math.Abs(d - cadence.Days) <= cadence.Tol);
            if (regular < Math.Max(2, intervals.Count - 1))
                continue;

            var amounts = items.Select(e => Math.Abs(e.Amount)).ToList();
            var typical = Median(amounts);

            // A weekly/biweekly window also matches habitual shopping (any 5–9 day gap at the
            // same supermarket). Short cadences must have stable amounts to count as recurring;
            // monthly and longer are meaningful from payee + interval alone.
            if (cadence.Days <= 14)
            {
                var stable = amounts.Count(a => typical > 0 && Math.Abs(a - typical) / typical <= 0.10m);
                if (stable * 2 <= amounts.Count)
                    continue;
            }

            var last = items[^1];
            var lastAmount = Math.Abs(last.Amount);

            results.Add(new RecurringPayment(
                Payee: g.Key,
                Cadence: cadence.Label,
                IntervalDays: medianInterval,
                TypicalAmount: typical,
                LastAmount: lastAmount,
                LastDate: last.Date,
                // Calendar-anchored for month-based cadences: a subscription charged on the
                // 15th is next expected on the 15th, not 30/91/365 days later.
                NextExpected: cadence.Label switch
                {
                    "Monthly" => last.Date.AddMonths(1),
                    "Quarterly" => last.Date.AddMonths(3),
                    "Yearly" => last.Date.AddYears(1),
                    _ => last.Date.AddDays(medianInterval),
                },
                Occurrences: items.Count,
                AmountChanged: typical > 0 && Math.Abs(lastAmount - typical) / typical > 0.05m));
        }

        return results.OrderByDescending(r => r.TypicalAmount).ToList();
    }

    private static int Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static decimal Median(List<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }
}
