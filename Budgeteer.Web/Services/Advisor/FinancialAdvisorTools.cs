using System.ComponentModel;
using System.Globalization;
using System.Text;
using Budgeteer.Accounts.ReadModels;
using Budgeteer.Budget.Insights;
using Marten;

namespace Budgeteer.Web.Services.Advisor;

/// <summary>
/// The read-only "tools" the AI financial advisor agent can call to ground its answers in the
/// user's actual data. Every method queries the existing read models / services and returns a
/// compact, human-readable text summary for the model to reason over. Nothing here mutates state —
/// the advisor can look, not touch.
/// </summary>
public sealed class FinancialAdvisorTools
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly IDocumentStore _store;
    private readonly TransactionQueryService _transactions;
    private readonly BudgetService _budget;
    private readonly BudgetAllocationService _allocations;
    private readonly SavingGoalService _goals;
    private readonly TransferDetectionService _transfers;

    public FinancialAdvisorTools(
        IDocumentStore store,
        TransactionQueryService transactions,
        BudgetService budget,
        BudgetAllocationService allocations,
        SavingGoalService goals,
        TransferDetectionService transfers)
    {
        _store = store;
        _transactions = transactions;
        _budget = budget;
        _allocations = allocations;
        _goals = goals;
        _transfers = transfers;
    }

    private static string Money(decimal amount) => "€" + amount.ToString("N2", Inv);

    // Payee and description text comes from imported bank statements — i.e. from whoever sent or
    // billed a payment — so it is attacker-influenced and can contain instruction-like text.
    // Flatten line breaks (so it can't imitate extra tool-output lines) and quote it; the system
    // prompt tells the model that quoted statement text is data, never instructions.
    private static string Untrusted(string? s)
    {
        var flat = (s ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return "\"" + flat.Replace('"', '\'') + "\"";
    }

    private async Task<List<AccountSummary>> LoadAccountsAsync()
    {
        await using var session = _store.QuerySession();
        return (await session.Query<AccountSummary>().ToListAsync()).ToList();
    }

    [Description("Lists all of the user's accounts with their current balances and the total net worth. Use this for questions about balances, how much money the user has, or their overall financial position.")]
    public async Task<string> GetAccountsAndBalances()
    {
        var accounts = await LoadAccountsAsync();
        if (accounts.Count == 0)
            return "No accounts have been set up yet.";

        var sb = new StringBuilder();
        sb.AppendLine("Accounts:");
        foreach (var a in accounts.OrderByDescending(a => a.Balance))
            sb.AppendLine($"- {a.Name} ({a.AccountType}): {Money(a.Balance)} across {a.TransactionCount} transaction(s).");
        sb.AppendLine($"Total net worth: {Money(accounts.Sum(a => a.Balance))}.");
        return sb.ToString();
    }

    [Description("Summarizes spending grouped by category over the last N months (expenses only). Use this to understand where the user's money goes.")]
    public async Task<string> GetSpendingByCategory(
        [Description("How many months back to include. Defaults to 3.")] int months = 3)
    {
        months = Math.Clamp(months, 1, 120);
        var cutoff = DateTime.UtcNow.Date.AddMonths(-months);
        var expenses = (await _budget.LoadExpensesAsync())
            .Where(e => e.Date.Date >= cutoff)
            .ToList();
        if (expenses.Count == 0)
            return $"No expenses recorded in the last {months} month(s).";

        var byCategory = BudgetService.SpendingByCategory(expenses);
        var total = byCategory.Sum(c => c.Total);

        var sb = new StringBuilder();
        sb.AppendLine($"Spending by category over the last {months} month(s) (total {Money(total)}):");
        foreach (var c in byCategory)
        {
            var share = total > 0 ? (double)(c.Total / total) * 100 : 0;
            sb.AppendLine($"- {c.Category}: {Money(c.Total)} ({share.ToString("N0", Inv)}%) across {c.Count} transaction(s).");
        }
        return sb.ToString();
    }

    [Description("Compares spending against the configured monthly budget limits for each category over the last N months, flagging categories that are over or near their limit.")]
    public async Task<string> GetBudgetStatus(
        [Description("How many months back to include. Defaults to 1 (the current month's pace).")] int months = 1)
    {
        months = Math.Clamp(months, 1, 120);
        var cutoff = DateTime.UtcNow.Date.AddMonths(-months);
        var expenses = (await _budget.LoadExpensesAsync())
            .Where(e => e.Date.Date >= cutoff)
            .ToList();
        var spending = BudgetService.SpendingByCategory(expenses);
        var allocations = await _allocations.GetAllAsync();
        var rows = BudgetAllocationService.BuildBudgetRows(spending, allocations, months);

        var withLimits = rows.Where(r => r.HasLimit).ToList();
        if (withLimits.Count == 0)
            return "No budget limits have been configured yet, so there is nothing to compare spending against.";

        var sb = new StringBuilder();
        sb.AppendLine($"Budget vs. actual over the last {months} month(s):");
        foreach (var r in withLimits)
        {
            var state = r.OverBudget ? "OVER BUDGET" : r.NearLimit ? "near limit" : "ok";
            sb.AppendLine($"- {r.Category}: spent {Money(r.Spent)} of {Money(r.Limit)} " +
                          $"({r.Percent.ToString("N0", Inv)}%, {Money(r.Remaining)} remaining) — {state}.");
        }
        return sb.ToString();
    }

    [Description("Reports progress toward each of the user's saving goals, including how much more is needed and the monthly amount required to hit any target date.")]
    public async Task<string> GetSavingGoals()
    {
        var goals = await _goals.GetAllAsync();
        if (goals.Count == 0)
            return "No saving goals have been set up yet.";

        var accounts = await LoadAccountsAsync();
        var now = DateTime.UtcNow;

        var sb = new StringBuilder();
        sb.AppendLine("Saving goals:");
        foreach (var g in goals)
        {
            var p = SavingGoalService.Progress(g, accounts);
            var line = $"- {g.Name}: {Money(p.Current)} of {Money(p.Target)} " +
                       $"({p.Percent.ToString("N0", Inv)}%), {Money(p.Remaining)} remaining";
            if (g.TargetDate is { } date)
            {
                line += $", target {date:yyyy-MM-dd}";
                var monthly = p.MonthlyNeeded(now);
                if (monthly is { } m)
                    line += $" ({Money(m)}/month needed)";
            }
            if (p.Reached) line += " — REACHED";
            sb.AppendLine(line + ".");
        }
        return sb.ToString();
    }

    [Description("Detects recurring payments and subscriptions (regular charges from the same payee), including the typical amount, cadence, and when the next charge is expected.")]
    public async Task<string> GetRecurringPayments()
    {
        var expenses = await _budget.LoadExpensesAsync();
        // Exclude transfer legs so a monthly savings transfer isn't reported as a subscription.
        var transferIds = await _transfers.GetTransferTransactionIdsAsync();
        var recurring = RecurringDetectionService.Detect(expenses, transferIds);
        if (recurring.Count == 0)
            return "No recurring payments or subscriptions were detected.";

        var monthlyEquivalent = recurring.Sum(r => r.Cadence switch
        {
            "Weekly" => r.TypicalAmount * 52m / 12m,
            "Biweekly" => r.TypicalAmount * 26m / 12m,
            "Monthly" => r.TypicalAmount,
            "Quarterly" => r.TypicalAmount / 3m,
            "Yearly" => r.TypicalAmount / 12m,
            _ => 0m
        });

        var sb = new StringBuilder();
        sb.AppendLine("Recurring payments / subscriptions:");
        foreach (var r in recurring)
        {
            var changed = r.AmountChanged ? $" (last charge {Money(r.LastAmount)} differs from typical)" : "";
            sb.AppendLine($"- {Untrusted(r.Payee)}: {Money(r.TypicalAmount)} {r.Cadence.ToLowerInvariant()}, " +
                          $"next expected {r.NextExpected:yyyy-MM-dd}{changed}.");
        }
        sb.AppendLine($"Estimated total recurring cost: {Money(monthlyEquivalent)}/month.");
        return sb.ToString();
    }

    [Description("Summarizes total income, total expenses, and net cash flow over the last N months. Transfers between the user's own accounts are excluded.")]
    public async Task<string> GetIncomeVsExpenseSummary(
        [Description("How many months back to include. Defaults to 3.")] int months = 3)
    {
        months = Math.Clamp(months, 1, 120);
        var cutoff = DateTime.UtcNow.Date.AddMonths(-months);
        var rows = (await _transactions.LoadAllAsync())
            .Where(r => !r.IsTransfer && r.Date.Date >= cutoff)
            .ToList();
        if (rows.Count == 0)
            return $"No transactions recorded in the last {months} month(s).";

        var income = rows.Where(r => r.Amount > 0).Sum(r => r.Amount);
        var expenses = rows.Where(r => r.Amount < 0).Sum(r => r.Amount); // negative
        var net = income + expenses;

        var sb = new StringBuilder();
        sb.AppendLine($"Cash flow over the last {months} month(s):");
        sb.AppendLine($"- Income: {Money(income)}");
        sb.AppendLine($"- Expenses: {Money(Math.Abs(expenses))}");
        sb.AppendLine($"- Net: {Money(net)} ({(net >= 0 ? "surplus" : "deficit")})");
        sb.AppendLine($"- Average net per month: {Money(net / months)}");
        return sb.ToString();
    }

    [Description("Lists the most recent transactions (newest first), with date, payee/description, amount, and category. Use this to look at specific recent activity.")]
    public async Task<string> GetRecentTransactions(
        [Description("How many transactions to return. Defaults to 20, capped at 100.")] int count = 20)
    {
        count = Math.Clamp(count, 1, 100);
        var rows = (await _transactions.LoadAllAsync()).Take(count).ToList();
        if (rows.Count == 0)
            return "No transactions have been recorded yet.";

        var sb = new StringBuilder();
        sb.AppendLine($"Most recent {rows.Count} transaction(s):");
        foreach (var r in rows)
        {
            var who = string.IsNullOrWhiteSpace(r.Payee) ? r.Description : r.Payee;
            var cat = string.IsNullOrWhiteSpace(r.Category) ? "Uncategorized" : r.Category;
            var transfer = r.IsTransfer ? " [transfer]" : "";
            sb.AppendLine($"- {r.Date:yyyy-MM-dd} {Untrusted(who)} {Money(r.Amount)} [{cat}] ({r.AccountName}){transfer}");
        }
        return sb.ToString();
    }

    [Description("Shows month-by-month income, expenses, and net cash flow over the last N months to reveal trends (e.g. whether spending is creeping up). Transfers between the user's own accounts are excluded.")]
    public async Task<string> GetSpendingTrend(
        [Description("How many months back to include (2-36). Defaults to 6.")] int months = 6)
    {
        months = Math.Clamp(months, 2, 36);
        var cutoff = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-(months - 1));
        var rows = (await _transactions.LoadAllAsync())
            .Where(r => !r.IsTransfer && r.Date.Date >= cutoff)
            .ToList();
        if (rows.Count == 0)
            return $"No transactions in the last {months} month(s).";

        var byMonth = rows
            .GroupBy(r => new DateTime(r.Date.Year, r.Date.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Month = g.Key,
                Income = g.Where(r => r.Amount > 0).Sum(r => r.Amount),
                Expense = g.Where(r => r.Amount < 0).Sum(r => -r.Amount),
            })
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Monthly cash flow (last {months} month(s)):");
        foreach (var m in byMonth)
            sb.AppendLine($"- {m.Month:yyyy-MM}: income {Money(m.Income)}, expenses {Money(m.Expense)}, net {Money(m.Income - m.Expense)}");

        if (byMonth.Count >= 2)
        {
            var earlier = byMonth.Take(byMonth.Count / 2).Average(m => (double)m.Expense);
            var recent = byMonth.Skip(byMonth.Count / 2).Average(m => (double)m.Expense);
            if (earlier > 0)
            {
                var change = (recent - earlier) / earlier * 100;
                var dir = change > 5 ? "rising" : change < -5 ? "falling" : "roughly flat";
                sb.AppendLine($"Expense trend: {dir} ({change.ToString("N0", Inv)}% recent half vs earlier half).");
            }
        }
        return sb.ToString();
    }

    [Description("Lists the largest individual expenses over the last N months (biggest first). Useful for spotting one-off big spends.")]
    public async Task<string> GetLargestTransactions(
        [Description("How many to return (1-50). Defaults to 10.")] int count = 10,
        [Description("How many months back to include. Defaults to 12.")] int months = 12)
    {
        count = Math.Clamp(count, 1, 50);
        months = Math.Clamp(months, 1, 120);
        var cutoff = DateTime.UtcNow.Date.AddMonths(-months);
        var rows = (await _transactions.LoadAllAsync())
            .Where(r => !r.IsTransfer && r.Amount < 0 && r.Date.Date >= cutoff)
            .OrderBy(r => r.Amount) // most negative (largest expense) first
            .Take(count)
            .ToList();
        if (rows.Count == 0)
            return $"No expenses in the last {months} month(s).";

        var sb = new StringBuilder();
        sb.AppendLine($"Largest expenses (last {months} month(s)):");
        foreach (var r in rows)
        {
            var who = string.IsNullOrWhiteSpace(r.Payee) ? r.Description : r.Payee;
            var cat = string.IsNullOrWhiteSpace(r.Category) ? "Uncategorized" : r.Category;
            sb.AppendLine($"- {r.Date:yyyy-MM-dd} {Untrusted(who)} {Money(Math.Abs(r.Amount))} [{cat}]");
        }
        return sb.ToString();
    }

    [Description("Totals how much the user has spent at a specific payee/merchant over the last N months (partial, case-insensitive match), with transaction count and monthly average.")]
    public async Task<string> GetSpendingForPayee(
        [Description("The payee or merchant name to search for (partial match, case-insensitive).")] string payee,
        [Description("How many months back to include. Defaults to 12.")] int months = 12)
    {
        if (string.IsNullOrWhiteSpace(payee))
            return "Please specify a payee or merchant name.";
        months = Math.Clamp(months, 1, 120);
        var cutoff = DateTime.UtcNow.Date.AddMonths(-months);
        var rows = (await _transactions.LoadAllAsync())
            .Where(r => !r.IsTransfer && r.Amount < 0 && r.Date.Date >= cutoff)
            .Where(r => (r.Payee?.Contains(payee, StringComparison.OrdinalIgnoreCase) ?? false)
                     || (r.Description?.Contains(payee, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        if (rows.Count == 0)
            return $"No spending matching '{payee}' in the last {months} month(s).";

        var total = rows.Sum(r => -r.Amount);
        var sb = new StringBuilder();
        sb.AppendLine($"Spending matching '{payee}' over the last {months} month(s):");
        sb.AppendLine($"- Total: {Money(total)} across {rows.Count} transaction(s)");
        sb.AppendLine($"- Average per month: {Money(total / months)}");
        sb.AppendLine($"- Most recent: {rows.Max(r => r.Date):yyyy-MM-dd}");
        return sb.ToString();
    }

    [Description("Lists the individual transactions in a given category over the last N months — the detail behind a category total. Pass 'Uncategorized' to see uncategorized expenses.")]
    public async Task<string> GetTransactionsInCategory(
        [Description("The category name (e.g. 'Groceries'). Use 'Uncategorized' for uncategorized expenses.")] string category,
        [Description("How many months back to include. Defaults to 3.")] int months = 3,
        [Description("Max transactions to list (1-100). Defaults to 30.")] int count = 30)
    {
        if (string.IsNullOrWhiteSpace(category))
            return "Please specify a category.";
        months = Math.Clamp(months, 1, 120);
        count = Math.Clamp(count, 1, 100);
        var cutoff = DateTime.UtcNow.Date.AddMonths(-months);
        var all = await _transactions.LoadAllAsync();
        var filtered = TransactionQueryService.Apply(all,
            new TransactionFilter(Category: category, From: cutoff, Flow: FlowFilter.ExpenseOnly));
        if (filtered.Count == 0)
            return $"No '{category}' transactions in the last {months} month(s).";

        var total = filtered.Sum(r => -r.Amount);
        var rows = filtered.Take(count).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"'{category}' transactions (last {months} month(s), {Money(total)} total across {filtered.Count}):");
        foreach (var r in rows)
        {
            var who = string.IsNullOrWhiteSpace(r.Payee) ? r.Description : r.Payee;
            sb.AppendLine($"- {r.Date:yyyy-MM-dd} {Untrusted(who)} {Money(Math.Abs(r.Amount))}");
        }
        if (filtered.Count > rows.Count)
            sb.AppendLine($"… and {filtered.Count - rows.Count} more.");
        return sb.ToString();
    }

    [Description("Flags unusually large charges: transactions well above the typical amount for that same payee. Useful for catching price hikes, duplicate charges, or unexpected costs.")]
    public async Task<string> GetUnusualTransactions(
        [Description("How many months back to include. Defaults to 6.")] int months = 6)
    {
        months = Math.Clamp(months, 1, 120);
        var cutoff = DateTime.UtcNow.Date.AddMonths(-months);
        var expenses = (await _budget.LoadExpensesAsync())
            .Where(e => e.Date.Date >= cutoff && !string.IsNullOrWhiteSpace(e.Payee))
            .ToList();

        var flagged = new List<(DateTime Date, string Payee, decimal Amount, decimal Typical)>();
        foreach (var g in expenses.GroupBy(e => e.Payee!.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var items = g.ToList();
            if (items.Count < 3)
                continue; // need a baseline to judge "unusual"
            var magnitudes = items.Select(e => Math.Abs(e.Amount)).OrderBy(x => x).ToList();
            var median = magnitudes[magnitudes.Count / 2];
            if (median <= 0)
                continue;
            foreach (var e in items)
            {
                var mag = Math.Abs(e.Amount);
                if (mag > median * 1.75m && mag - median >= 5m)
                    flagged.Add((e.Date, g.Key, mag, median));
            }
        }

        if (flagged.Count == 0)
            return $"No unusually large charges detected in the last {months} month(s).";

        var sb = new StringBuilder();
        sb.AppendLine($"Unusually large charges vs each payee's typical amount (last {months} month(s)):");
        foreach (var f in flagged.OrderByDescending(f => f.Amount - f.Typical).Take(20))
            sb.AppendLine($"- {f.Date:yyyy-MM-dd} {f.Payee}: {Money(f.Amount)} (typical {Money(f.Typical)})");
        return sb.ToString();
    }

    [Description("Projects this calendar month's spending per budgeted category to month-end based on the pace so far, and flags categories on track to exceed their monthly limit.")]
    public async Task<string> GetBudgetProjection()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var dayOfMonth = Math.Min(now.Day, daysInMonth);
        var elapsed = (decimal)dayOfMonth / daysInMonth;

        var expenses = (await _budget.LoadExpensesAsync())
            .Where(e => e.Date.Date >= monthStart)
            .ToList();
        var spending = BudgetService.SpendingByCategory(expenses);
        var allocations = await _allocations.GetAllAsync();
        var rows = BudgetAllocationService.BuildBudgetRows(spending, allocations, periodMonths: 1)
            .Where(r => r.HasLimit)
            .ToList();
        if (rows.Count == 0)
            return "No budget limits are configured, so there is nothing to project.";

        var sb = new StringBuilder();
        sb.AppendLine($"Budget projection for {monthStart:yyyy-MM} ({(elapsed * 100).ToString("N0", Inv)}% of the month elapsed):");
        foreach (var r in rows)
        {
            var projected = elapsed > 0 ? r.Spent / elapsed : r.Spent;
            var verdict = projected > r.MonthlyLimit
                ? $"PROJECTED OVER by {Money(projected - r.MonthlyLimit)}"
                : "on track";
            sb.AppendLine($"- {r.Category}: spent {Money(r.Spent)} of {Money(r.MonthlyLimit)}, " +
                          $"projected {Money(projected)} — {verdict}");
        }
        return sb.ToString();
    }

    [Description("Lists expenses that have no category yet, so the user can categorize them for more accurate budgets and reports. Newest first.")]
    public async Task<string> GetUncategorizedTransactions(
        [Description("Max transactions to list (1-100). Defaults to 30.")] int count = 30)
    {
        count = Math.Clamp(count, 1, 100);
        var rows = (await _transactions.LoadAllAsync())
            .Where(r => !r.IsTransfer && r.Amount < 0 && string.IsNullOrWhiteSpace(r.Category))
            .ToList();
        if (rows.Count == 0)
            return "All expenses are categorized.";

        var total = rows.Sum(r => -r.Amount);
        var sb = new StringBuilder();
        sb.AppendLine($"Uncategorized expenses: {rows.Count} transaction(s) totaling {Money(total)}.");
        foreach (var r in rows.Take(count))
        {
            var who = string.IsNullOrWhiteSpace(r.Payee) ? r.Description : r.Payee;
            sb.AppendLine($"- {r.Date:yyyy-MM-dd} {Untrusted(who)} {Money(Math.Abs(r.Amount))} ({r.AccountName})");
        }
        if (rows.Count > count)
            sb.AppendLine($"… and {rows.Count - count} more.");
        return sb.ToString();
    }
}
