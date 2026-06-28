using Budgeteer.Accounts.ReadModels;
using Budgeteer.Budget.Budgeting;
using Marten;

namespace Budgeteer.Web.Services;

/// <summary>Computed progress toward a saving goal.</summary>
public record GoalProgress(SavingGoal Goal, decimal Current)
{
    public decimal Target => Goal.TargetAmount;
    public decimal Remaining => Math.Max(0m, Target - Current);
    public bool Reached => Target > 0 && Current >= Target;
    // Clamped to [0,100] so an overdrawn linked account can't produce a negative bar width.
    public double Percent => Target > 0 ? Math.Clamp((double)(Current / Target) * 100, 0, 100) : 0;

    /// <summary>
    /// Amount to set aside per month to reach the target by <see cref="SavingGoal.TargetDate"/>.
    /// Null when there is no date, the goal is reached, or the date is already in the past.
    /// A target due in the current month counts as one month (set aside the remainder now).
    /// </summary>
    public decimal? MonthlyNeeded(DateTime asOf)
    {
        if (Goal.TargetDate is not { } date || Reached)
            return null;
        if (date.Date < asOf.Date)
            return null; // genuinely past due
        var months = Math.Max(1, (date.Year - asOf.Year) * 12 + (date.Month - asOf.Month));
        return Math.Round(Remaining / months, 2);
    }
}

/// <summary>CRUD over saving goals plus progress computation against account balances.</summary>
public sealed class SavingGoalService
{
    private readonly IDocumentStore _store;

    public SavingGoalService(IDocumentStore store) => _store = store;

    public async Task<List<SavingGoal>> GetAllAsync()
    {
        await using var session = _store.QuerySession();
        return (await session.Query<SavingGoal>().ToListAsync()).ToList();
    }

    public async Task SaveAsync(SavingGoal goal)
    {
        if (string.IsNullOrWhiteSpace(goal.Name) || goal.TargetAmount <= 0)
            return;
        if (string.IsNullOrWhiteSpace(goal.Id))
            goal.Id = Guid.NewGuid().ToString();

        await using var session = _store.LightweightSession();
        session.Store(goal);
        await session.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        await using var session = _store.LightweightSession();
        session.Delete<SavingGoal>(id);
        await session.SaveChangesAsync();
    }

    /// <summary>Resolves a goal's current saved amount: linked-account balance, or the manual amount.</summary>
    public static decimal CurrentFor(SavingGoal goal, IReadOnlyList<AccountSummary> accounts)
    {
        if (!string.IsNullOrWhiteSpace(goal.LinkedAccountId))
            return accounts.FirstOrDefault(a => a.Id == goal.LinkedAccountId)?.Balance ?? 0m;
        return goal.ManualAmount;
    }

    public static GoalProgress Progress(SavingGoal goal, IReadOnlyList<AccountSummary> accounts) =>
        new(goal, CurrentFor(goal, accounts));
}
