using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Home;

/// <summary>
/// How much attention the case needs. There is deliberately no SAFE / NORMAL / OK level: when nothing fired, the level is
/// <see cref="NothingDetected"/> (a statement about the sources that were analysed) or <see cref="Undetermined"/> (when those sources are too few to say even that).
/// </summary>
public enum AttentionLevel { Undetermined, NothingDetected, Low, Medium, High, Critical }

/// <summary>Whether the evidence of the open case can be relied on, from the integrity re-check (WP3b).</summary>
public enum TrustState { Undetermined, Intact, Limited, Compromised }

/// <summary>What the Home page is computed from. Everything is read from the case (findings, verification, re-check, coverage); nothing is typed in.</summary>
public sealed record HomeInputs
{
    public bool CaseOpen { get; init; }
    public string CaseName { get; init; } = "";
    /// <summary>How far the analysis got; <see cref="OperationState.NotStarted"/> when the case has no findings.json.</summary>
    public OperationState AnalysisState { get; init; } = OperationState.NotStarted;
    public string AnalysisStateReason { get; init; } = "";
    public IReadOnlyDictionary<Severity, int> FindingsBySeverity { get; init; } = new Dictionary<Severity, int>();
    public IReadOnlyList<string> TopFindings { get; init; } = [];
    public int GapCount { get; init; }
    /// <summary>Verdict counts by spec name (VERIFIED, SUPPORTED, UNPROVEN, ...). Null = the verification did not run for this case.</summary>
    public IReadOnlyDictionary<string, int>? VerificationCounts { get; init; }
    /// <summary>Verdict of the integrity re-check. Null = it did not run (yet).</summary>
    public RecheckVerdict? Integrity { get; init; }
    public string IntegritySummary { get; init; } = "";
    public CoverageMatrix Coverage { get; init; } = CoverageMatrix.Unknown([]);
    public string? ScopeNote { get; init; }
    public bool ReadOnly { get; init; }
    public IReadOnlyList<string> ReadOnlyReasons { get; init; } = [];
}

/// <summary>The five answers of the Home page (U3) and the data behind them.</summary>
public sealed record HomeSummary
{
    public AttentionLevel Attention { get; init; }
    public string AttentionLabel { get; init; } = "";
    /// <summary>Is there a problem?</summary>
    public string Problem { get; init; } = "";
    /// <summary>How serious?</summary>
    public string Seriousness { get; init; } = "";
    /// <summary>Is the evidence trustworthy?</summary>
    public string Trust { get; init; } = "";
    public TrustState TrustState { get; init; }
    /// <summary>What was found?</summary>
    public string Found { get; init; } = "";
    /// <summary>What next? Short and data-driven; at most <see cref="HomeAggregator.MaxNextSteps"/>.</summary>
    public IReadOnlyList<string> NextSteps { get; init; } = [];
    public string CoverageLine { get; init; } = "";
    public CoverageMatrix Coverage { get; init; } = CoverageMatrix.Unknown([]);
    public int TotalFindings { get; init; }
}

public static class HomeAggregator
{
    public const int MaxNextSteps = 5;
    public static string NothingDetected => Loc.T("home.nothing_detected");
    public static string NoCase => Loc.T("home.no_case");

    public static HomeSummary Build(HomeInputs i)
    {
        var cov = i.Coverage;
        int total = i.FindingsBySeverity.Values.Sum();
        int N(Severity s) => i.FindingsBySeverity.TryGetValue(s, out var n) ? n : 0;
        var top = Enum.GetValues<Severity>().Where(s => N(s) > 0).DefaultIfEmpty(Severity.Info).Max();
        string coverageLine = Loc.Format("home.coverage_line", cov.OverallLabel, cov.OverallReason);

        bool analysed = i.AnalysisState is OperationState.Completed or OperationState.Partial;
        var (trustState, trust) = TrustOf(i);

        AttentionLevel level; string label;
        if (!i.CaseOpen) { level = AttentionLevel.Undetermined; label = Loc.Format("home.undetermined_prefix", NoCase.ToLowerInvariant()); }
        else if (!analysed && total == 0)
        {
            level = AttentionLevel.Undetermined;
            label = i.AnalysisState == OperationState.NotStarted
                ? Loc.T("home.undetermined_not_run")
                : Loc.Format("home.undetermined_unfinished", StateWord(i.AnalysisState), i.AnalysisStateReason.Length > 0 ? " — " + i.AnalysisStateReason : "");
        }
        else if (total > 0)
        {
            (level, label) = top switch
            {
                Severity.Critical => (AttentionLevel.Critical, Loc.T("home.attention_critical")),
                Severity.High => (AttentionLevel.High, Loc.T("home.attention_high")),
                Severity.Medium => (AttentionLevel.Medium, Loc.T("home.attention_medium")),
                _ => (AttentionLevel.Low, Loc.T("home.attention_low")),
            };
            if (i.AnalysisState == OperationState.Partial) label += Loc.T("home.partial_suffix");
        }
        else if (cov.Overall is OverallCoverage.Minimal or OverallCoverage.Unknown)
        {
            level = AttentionLevel.Undetermined;
            label = Loc.Format("home.undetermined_coverage", NothingDetected.ToLowerInvariant(), cov.OverallLabel.ToLowerInvariant());
        }
        else { level = AttentionLevel.NothingDetected; label = Loc.Format("home.nothing_with_coverage", NothingDetected, cov.OverallLabel.ToLowerInvariant()); }

        string problem;
        if (!i.CaseOpen) problem = Loc.T("home.problem.undetermined_no_case");
        else if (level == AttentionLevel.Undetermined && total == 0) problem = label + ".";
        else if (total == 0) problem = Loc.Format("home.problem.none_not_clean", NothingDetected, cov.OverallLabel.ToLowerInvariant(), i.GapCount);
        else problem = level >= AttentionLevel.High ? Loc.Format("home.problem.high", N(Severity.Critical) + N(Severity.High))
                     : level == AttentionLevel.Medium ? Loc.Format("home.problem.medium", N(Severity.Medium))
                     : Loc.T("home.problem.low");
        if (trustState == TrustState.Compromised) problem += Loc.T("home.problem.compromised");

        string seriousness = total == 0
            ? (i.CaseOpen ? Loc.T("home.seriousness.none") : Loc.T("home.seriousness.undetermined"))
            : Loc.Format("home.seriousness.max", SeverityWord(top));

        string found;
        if (!i.CaseOpen) found = Loc.T("home.found.nothing_to_show");
        else
        {
            var bySev = Enum.GetValues<Severity>().Reverse().Where(s => N(s) > 0).Select(s => $"{N(s)} {SeverityWord(s).ToLowerInvariant()}").ToList();
            found = total == 0 ? $"{NothingDetected}." : Loc.Format("home.found.count", total, string.Join(", ", bySev));
            found += i.VerificationCounts is { } vc ? " " + LogAnalyzer.Dfir.Home.HomeAggregator.VerificationLine(vc) : Loc.T("home.found.no_verification");
            if (i.TopFindings.Count > 0) found += Loc.Format("home.found.first", string.Join("; ", i.TopFindings.Take(3)));
        }

        return new HomeSummary
        {
            Attention = level, AttentionLabel = label, Problem = problem, Seriousness = seriousness, Trust = trust, TrustState = trustState, Found = found,
            NextSteps = NextSteps(i, total, level, trustState), CoverageLine = coverageLine, Coverage = cov, TotalFindings = total,
        };
    }

    public static string VerificationLine(IReadOnlyDictionary<string, int> counts)
    {
        int Get(StandardState s) => counts.TryGetValue(s.ToSpec(), out var n) ? n : 0;
        int solid = Get(StandardState.Verified) + Get(StandardState.Supported);
        int weak = Get(StandardState.Unproven) + Get(StandardState.Unknown) + Get(StandardState.NotAssessed);
        int bad = Get(StandardState.Contradicted) + Get(StandardState.Rejected);
        return Loc.Format("home.verification_line", solid, weak, bad);
    }

    private static (TrustState, string) TrustOf(HomeInputs i)
    {
        if (!i.CaseOpen) return (TrustState.Undetermined, Loc.T("home.trust.no_case"));
        var detail = i.IntegritySummary.Length > 0 ? " " + i.IntegritySummary : "";
        return i.Integrity switch
        {
            null => (TrustState.Undetermined, Loc.T("home.trust.not_run")),
            RecheckVerdict.Valid => (TrustState.Intact, Loc.T("home.trust.valid") + detail),
            RecheckVerdict.Legacy => (TrustState.Limited, Loc.T("home.trust.legacy") + detail),
            RecheckVerdict.Unverified => (TrustState.Limited, Loc.T("home.trust.unverified") + detail),
            RecheckVerdict.Missing => (TrustState.Compromised, Loc.T("home.trust.missing") + detail),
            RecheckVerdict.Modified => (TrustState.Compromised, Loc.T("home.trust.modified") + detail),
            _ => (TrustState.Compromised, Loc.T("home.trust.broken") + detail),
        };
    }

    private static List<string> NextSteps(HomeInputs i, int total, AttentionLevel level, TrustState trust)
    {
        var steps = new List<string>();
        if (!i.CaseOpen) { steps.Add(Loc.T("home.next.choose")); return steps; }
        if (trust == TrustState.Compromised) steps.Add(Loc.T("home.next.compromised"));
        if (i.AnalysisState == OperationState.NotStarted) steps.Add(Loc.T("home.next.run"));
        else if (i.AnalysisState is OperationState.Failed or OperationState.Cancelled or OperationState.Blocked) steps.Add(Loc.T("home.next.rerun"));
        if (level >= AttentionLevel.High) steps.Add(Loc.Format("home.next.review_high", i.FindingsBySeverity.GetValueOrDefault(Severity.Critical) + i.FindingsBySeverity.GetValueOrDefault(Severity.High)));
        else if (level is AttentionLevel.Medium or AttentionLevel.Low) steps.Add(Loc.Format("home.next.review_all", total));
        if (i.VerificationCounts is { } vc)
        {
            int bad = vc.GetValueOrDefault(StandardState.Contradicted.ToSpec()) + vc.GetValueOrDefault(StandardState.Rejected.ToSpec());
            int weak = vc.GetValueOrDefault(StandardState.Unproven.ToSpec());
            if (bad > 0) steps.Add(Loc.Format("home.next.bad", bad));
            else if (weak > 0) steps.Add(Loc.Format("home.next.weak", weak));
        }
        var holes = i.Coverage.Gaps;
        if (holes.Count > 0) steps.Add(Loc.Format("home.next.sources", string.Join(", ", holes.Take(4).Select(h => h.Family)), holes.Count > 4 ? Loc.Format("home.next.sources_more", holes.Count - 4) : ""));
        if (i.ScopeNote is { Length: > 0 }) steps.Add(Loc.Format("home.next.scope", i.ScopeNote));
        if (i.ReadOnly) steps.Add(Loc.T("home.next.read_only"));
        if (steps.Count == 0) steps.Add(Loc.T("home.next.none"));
        return steps.Take(MaxNextSteps).ToList();
    }

    private static string SeverityWord(Severity s) => s switch
    {
        Severity.Critical => Loc.T("sev.critical"), Severity.High => Loc.T("sev.high"), Severity.Medium => Loc.T("sev.medium"), Severity.Low => Loc.T("sev.low"), _ => Loc.T("sev.info"),
    };

    private static string StateWord(OperationState s) => s switch
    {
        OperationState.Running => Loc.T("home.state.running"), OperationState.Failed => Loc.T("home.state.failed"), OperationState.Cancelled => Loc.T("home.state.cancelled"), OperationState.Blocked => Loc.T("home.state.blocked"), _ => Loc.T("home.state.not_started"),
    };
}
