using System.Collections;

namespace Budgeteer.Accounts.Import;

/// <summary>
/// The mutations parsed from a bank CSV export, plus diagnostics about rows that could not be
/// parsed. Implements <see cref="IReadOnlyList{BankMutation}"/> so callers that only care about
/// the mutations can treat the result as the list itself — but the Import UI surfaces
/// <see cref="SkippedRows"/> so a format change at the bank can't silently swallow data.
/// </summary>
public sealed class BankParseResult : IReadOnlyList<BankMutation>
{
    private readonly IReadOnlyList<BankMutation> _mutations;

    public BankParseResult(IReadOnlyList<BankMutation> mutations, int skippedRows, IReadOnlyList<string> skipSamples)
    {
        _mutations = mutations;
        SkippedRows = skippedRows;
        SkipSamples = skipSamples;
    }

    public IReadOnlyList<BankMutation> Mutations => _mutations;

    /// <summary>Number of data rows that were skipped because they could not be parsed.</summary>
    public int SkippedRows { get; }

    /// <summary>Up to a few human-readable examples of why rows were skipped.</summary>
    public IReadOnlyList<string> SkipSamples { get; }

    public BankMutation this[int index] => _mutations[index];
    public int Count => _mutations.Count;
    public IEnumerator<BankMutation> GetEnumerator() => _mutations.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
