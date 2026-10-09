namespace LogAnalyzer.Verification;

/// <summary>Knobs of a verification run. The defaults are the documented ones (docs/dfir/VERIFICATION.md).</summary>
public sealed class VerificationOptions
{
    /// <summary>Clock for <c>GeneratedUtc</c> (tests replace it; nothing else in the report depends on the time of the run).</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Clock skew accepted between the audited system's times and the collector's acquisition time, and between two sources, before a time
    /// comparison counts as a contradiction. Times from different machines are never exact.
    /// </summary>
    public TimeSpan ClockSkewTolerance { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Slack on the claimed window [FirstSeen, LastSeen]: both ends come from the same rows, so it is small.</summary>
    public TimeSpan WindowTolerance { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Re-hash the evidence the findings rest on (independently of any re-check). Switch off only in tests that do not need it.</summary>
    public bool RehashEvidence { get; init; } = true;

    /// <summary>Contradiction rules added to <see cref="ContradictionRules.Default"/> (the set is extensible; each rule needs its own test).</summary>
    public IReadOnlyList<IContradictionRule> ExtraContradictionRules { get; init; } = [];
}
