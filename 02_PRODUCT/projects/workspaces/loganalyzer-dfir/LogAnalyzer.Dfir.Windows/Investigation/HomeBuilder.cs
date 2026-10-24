using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Home;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>Feeds <see cref="HomeAggregator"/> from an investigation result and its case (findings, verification, re-check, coverage).</summary>
public static class HomeBuilder
{
    public static HomeSummary Build(InvestigationResult r, CaseWorkspace ws, CoverageMatrix? coverage = null, IReadOnlyList<string>? readOnlyReasons = null)
    {
        coverage ??= CaseLoader.CoverageOf(r, ws);
        var bySeverity = r.Findings.GroupBy(f => f.Severity).ToDictionary(g => g.Key, g => g.Count());
        var top = r.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.FindingId, StringComparer.Ordinal).Take(3).Select(f => $"{f.Title} ({f.Severity.ToSpec()})").ToList();
        return HomeAggregator.Build(new HomeInputs
        {
            CaseOpen = true, CaseName = ws.Info.Name, AnalysisState = r.State, AnalysisStateReason = r.StateReason,
            FindingsBySeverity = bySeverity, TopFindings = top, GapCount = r.Gaps.Count,
            VerificationCounts = r.Verification?.Counts,
            Integrity = ws.LastRecheck?.Verdict, IntegritySummary = ws.LastRecheck?.Summary ?? "",
            Coverage = coverage, ScopeNote = ws.ScopeNote, ReadOnly = readOnlyReasons is { Count: > 0 }, ReadOnlyReasons = readOnlyReasons ?? [],
        });
    }

    /// <summary>The Home summary when no case is open.</summary>
    public static HomeSummary NoCase() => HomeAggregator.Build(new HomeInputs { CaseOpen = false, Coverage = CoverageMatrix.Unknown(Windows.Parsers.WindowsParsers.Registry.Descriptors) });
}
