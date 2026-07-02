using Marten;

namespace Budgeteer.Budget.Categorization;

/// <summary>
/// Assigns categories to transactions using keyword rules stored in Marten.
/// The core <see cref="Match"/> is a pure function (easily unit-tested); the async
/// members load/persist rules and implement learning from manual corrections.
/// </summary>
public class TransactionCategorizer
{
    /// <summary>Category used for income that no rule matched.</summary>
    public const string DefaultIncomeCategory = "Income";

    /// <summary>
    /// Categories that describe money coming in. Shipped (seed) rules for any other category are
    /// treated as spending rules and are not applied to positive (income) amounts, so an income
    /// deposit can't be mislabeled by an expense keyword that happens to appear in its text.
    /// </summary>
    private static readonly HashSet<string> IncomeCategories =
        new(StringComparer.OrdinalIgnoreCase) { DefaultIncomeCategory, "Salary" };

    private readonly IDocumentStore _store;

    public TransactionCategorizer(IDocumentStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Pure matching: returns the category of the best-matching rule, or a sensible default.
    /// Highest priority wins; ties are broken by the longest (most specific) keyword.
    /// Falls back to <see cref="DefaultIncomeCategory"/> for unmatched positive amounts,
    /// and null (uncategorized) for unmatched expenses.
    /// </summary>
    public static string? Match(IEnumerable<CategorizationRule> rules, string? payee, string? description, decimal amount)
    {
        // The trailing space lets keywords that end in a space ("bp ", "vve ") match at the
        // end of the text too — end-of-text is a word boundary.
        var text = ((payee ?? string.Empty) + " " + (description ?? string.Empty)).ToLowerInvariant() + " ";

        var best = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Keyword) && KeywordMatches(text, r.Keyword))
            // For income, only apply income-category seed rules or explicit user (manual/learned)
            // rules; don't let an expense seed keyword categorize an incoming payment.
            .Where(r => amount <= 0 || r.Source != RuleSource.Seed || IncomeCategories.Contains(r.Category))
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Keyword.Length)
            .FirstOrDefault();

        if (best != null)
            return best.Category;

        return amount > 0 ? DefaultIncomeCategory : null;
    }

    // A keyword only matches at the start of a word: "bp " must not match inside "abp" and
    // "ret " must not match inside "internet". The end stays substring-based so "mcdonald"
    // still matches "mcdonalds" — seeds that need an end boundary encode it with a trailing
    // space, which is why the keyword must NOT be trimmed here.
    private static bool KeywordMatches(string text, string keyword)
    {
        var kw = keyword.ToLowerInvariant();
        int idx = 0;
        while ((idx = text.IndexOf(kw, idx, StringComparison.Ordinal)) >= 0)
        {
            if (idx == 0 || !char.IsLetterOrDigit(text[idx - 1]))
                return true;
            idx++;
        }
        return false;
    }

    /// <summary>Categorizes a single transaction against the rules currently in the store.</summary>
    public async Task<string?> CategorizeAsync(string? payee, string? description, decimal amount)
    {
        await using var session = _store.QuerySession();
        var rules = await session.Query<CategorizationRule>().ToListAsync();
        return Match(rules, payee, description, amount);
    }

    public async Task<IReadOnlyList<CategorizationRule>> GetRulesAsync()
    {
        await using var session = _store.QuerySession();
        return await session.Query<CategorizationRule>().ToListAsync();
    }

    /// <summary>Adds (or updates by keyword) a rule. Used by the Categories page.</summary>
    public async Task AddOrUpdateRuleAsync(string keyword, string category,
        RuleSource source = RuleSource.Manual, int? priority = null)
    {
        keyword = (keyword ?? string.Empty).Trim();
        if (keyword.Length == 0 || string.IsNullOrWhiteSpace(category))
            return;

        await using var session = _store.LightweightSession();
        var existing = await session.Query<CategorizationRule>()
            .Where(r => r.Keyword == keyword.ToLowerInvariant())
            .FirstOrDefaultAsync();

        var rule = existing ?? new CategorizationRule { CreatedAt = DateTime.UtcNow };
        rule.Keyword = keyword.ToLowerInvariant();
        rule.Category = category.Trim();
        rule.Source = source;
        rule.Priority = priority ?? (source == RuleSource.Learned
            ? (int)CategorizationRule.PriorityTier.Learned
            : (int)CategorizationRule.PriorityTier.Manual);

        session.Store(rule);
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Learns from a manual correction: future transactions from the same payee will be
    /// auto-categorized the same way. This is what makes categorization "smart".
    /// </summary>
    public Task LearnAsync(string? payee, string category)
    {
        if (string.IsNullOrWhiteSpace(payee) || string.IsNullOrWhiteSpace(category))
            return Task.CompletedTask;
        return AddOrUpdateRuleAsync(payee!, category, RuleSource.Learned);
    }

    public async Task DeleteRuleAsync(string id)
    {
        await using var session = _store.LightweightSession();
        session.Delete<CategorizationRule>(id);
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the default rule set if no seed rules exist yet. Idempotent and safe to call
    /// on every startup. Learned/manual rules are never touched.
    /// </summary>
    public async Task SeedDefaultsAsync()
    {
        await using var session = _store.LightweightSession();
        var hasSeed = await session.Query<CategorizationRule>()
            .AnyAsync(r => r.Source == RuleSource.Seed);
        if (hasSeed)
            return;

        foreach (var (keyword, category) in DefaultRules.Entries)
        {
            session.Store(new CategorizationRule
            {
                // Deterministic id keeps seeding idempotent.
                Id = "seed:" + keyword,
                Keyword = keyword.ToLowerInvariant(),
                Category = category,
                Priority = (int)CategorizationRule.PriorityTier.Seed,
                Source = RuleSource.Seed,
                CreatedAt = DateTime.UtcNow
            });
        }
        await session.SaveChangesAsync();
    }
}
