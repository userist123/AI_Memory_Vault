using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>A complete case scope for tests that are not about the scope itself (CaseWorkspace.Create refuses an incomplete one).</summary>
internal static class TestScopes
{
    public static CaseScope Valid() => new()
    {
        Purpose = "test", PeriodFromUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), PeriodToUtc = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        SystemsInScope = ["TESTHOST"], Approver = "approver-1", LegalBasis = LegalBasis.Incident,
        Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
    };
}
