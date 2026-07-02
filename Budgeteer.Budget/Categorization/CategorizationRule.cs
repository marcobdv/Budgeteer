namespace Budgeteer.Budget.Categorization;

/// <summary>Where a categorization rule came from.</summary>
public enum RuleSource
{
    /// <summary>Shipped default rule.</summary>
    Seed = 0,
    /// <summary>Created automatically from a manual correction (learned).</summary>
    Learned = 1,
    /// <summary>Created by hand on the Categories page.</summary>
    Manual = 2
}

/// <summary>
/// Marker document persisted once the default rules have been seeded, so seeding never
/// repeats — a user who deletes every seed rule must not get them all back on next startup
/// (which is what inferring "already seeded" from surviving seed rules did).
/// </summary>
public class CategorizationSeedMarker
{
    public const string DefaultId = "default";
    public string Id { get; set; } = DefaultId;
    public DateTime SeededAt { get; set; }
}

/// <summary>
/// A rule that assigns a category to a transaction when <see cref="Keyword"/> occurs
/// (case-insensitively) in the transaction's payee or description.
/// Stored as a Marten document. Higher <see cref="Priority"/> wins; learned/manual rules
/// outrank seed rules so user intent overrides the defaults.
/// </summary>
public class CategorizationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Lower-cased substring matched against "payee + description".</summary>
    public string Keyword { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int Priority { get; set; } = (int)PriorityTier.Seed;

    public RuleSource Source { get; set; } = RuleSource.Seed;

    public DateTime CreatedAt { get; set; }

    public enum PriorityTier
    {
        Seed = 100,
        // An explicit rule the user typed on the Categories page outranks one auto-learned from a
        // single correction, so deliberate configuration wins over inference.
        Learned = 300,
        Manual = 500
    }
}
