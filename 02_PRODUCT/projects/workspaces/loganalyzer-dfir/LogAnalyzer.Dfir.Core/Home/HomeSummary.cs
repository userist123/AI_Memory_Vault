using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
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
    public const string NothingDetected = "Nimic detectat în sursele analizate";
    public const string NoCase = "Niciun caz deschis";

    public static HomeSummary Build(HomeInputs i)
    {
        var cov = i.Coverage;
        int total = i.FindingsBySeverity.Values.Sum();
        int N(Severity s) => i.FindingsBySeverity.TryGetValue(s, out var n) ? n : 0;
        var top = Enum.GetValues<Severity>().Where(s => N(s) > 0).DefaultIfEmpty(Severity.Info).Max();
        string coverageLine = $"Acoperire: {cov.OverallLabel} — {cov.OverallReason}";

        bool analysed = i.AnalysisState is OperationState.Completed or OperationState.Partial;
        var (trustState, trust) = TrustOf(i);

        AttentionLevel level; string label;
        if (!i.CaseOpen) { level = AttentionLevel.Undetermined; label = "Nedeterminat: " + NoCase.ToLowerInvariant(); }
        else if (!analysed && total == 0)
        {
            level = AttentionLevel.Undetermined;
            label = i.AnalysisState == OperationState.NotStarted
                ? "Nedeterminat: analiza nu a fost rulată pentru acest caz"
                : $"Nedeterminat: analiza nu a fost finalizată ({StateWord(i.AnalysisState)}){(i.AnalysisStateReason.Length > 0 ? " — " + i.AnalysisStateReason : "")}";
        }
        else if (total > 0)
        {
            (level, label) = top switch
            {
                Severity.Critical => (AttentionLevel.Critical, "Atenție critică"),
                Severity.High => (AttentionLevel.High, "Atenție ridicată"),
                Severity.Medium => (AttentionLevel.Medium, "Atenție medie"),
                _ => (AttentionLevel.Low, "Observații minore"),
            };
            if (i.AnalysisState == OperationState.Partial) label += " (analiză parțială)";
        }
        else if (cov.Overall is OverallCoverage.Minimal or OverallCoverage.Unknown)
        {
            level = AttentionLevel.Undetermined;
            label = $"Nedeterminat: {NothingDetected.ToLowerInvariant()}, dar acoperirea este {cov.OverallLabel.ToLowerInvariant()}";
        }
        else { level = AttentionLevel.NothingDetected; label = $"{NothingDetected} (acoperire {cov.OverallLabel.ToLowerInvariant()})"; }

        string problem;
        if (!i.CaseOpen) problem = "Nedeterminat: nu este deschis niciun caz.";
        else if (level == AttentionLevel.Undetermined && total == 0) problem = label + ".";
        else if (total == 0) problem = $"{NothingDetected}. Aceasta nu înseamnă că sistemul este curat: acoperire {cov.OverallLabel.ToLowerInvariant()}, {i.GapCount} goluri de probă.";
        else problem = level >= AttentionLevel.High ? $"Da: {N(Severity.Critical) + N(Severity.High)} constatări de severitate ridicată sau critică."
                     : level == AttentionLevel.Medium ? $"Posibil: {N(Severity.Medium)} constatări de severitate medie de revizuit."
                     : "Doar observații de severitate scăzută; verificați acoperirea înainte de a le considera neînsemnate.";
        if (trustState == TrustState.Compromised) problem += " În plus, integritatea probelor este compromisă.";

        string seriousness = total == 0
            ? (i.CaseOpen ? "Nu se poate stabili: nu există constatări, iar lipsa lor nu dovedește lipsa activității." : "Nedeterminat.")
            : $"Severitate maximă: {SeverityWord(top)}.";

        string found;
        if (!i.CaseOpen) found = "Nimic de arătat: nu este deschis niciun caz.";
        else
        {
            var bySev = Enum.GetValues<Severity>().Reverse().Where(s => N(s) > 0).Select(s => $"{N(s)} {SeverityWord(s).ToLowerInvariant()}").ToList();
            found = total == 0 ? $"{NothingDetected}." : $"{total} constatări ({string.Join(", ", bySev)}).";
            found += i.VerificationCounts is { } vc ? " " + LogAnalyzer.Dfir.Home.HomeAggregator.VerificationLine(vc) : " Verificare automată: nerulată sau indisponibilă (nedeterminat).";
            if (i.TopFindings.Count > 0) found += " Primele: " + string.Join("; ", i.TopFindings.Take(3)) + ".";
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
        return $"Verificare automată (nu externă): {solid} susținute, {weak} nedovedite sau neevaluate, {bad} contrazise sau respinse.";
    }

    private static (TrustState, string) TrustOf(HomeInputs i)
    {
        if (!i.CaseOpen) return (TrustState.Undetermined, "Nedeterminat: nu este deschis niciun caz.");
        var detail = i.IntegritySummary.Length > 0 ? " " + i.IntegritySummary : "";
        return i.Integrity switch
        {
            null => (TrustState.Undetermined, "Nedeterminat: reverificarea de integritate nu a rulat (sau încă rulează)."),
            RecheckVerdict.Valid => (TrustState.Intact, "Probele și lanțurile de custodie și audit sunt intacte la reverificare." + detail),
            RecheckVerdict.Legacy => (TrustState.Limited, "Caz creat înainte de lanțurile criptografice: integritatea nu poate fi dovedită complet." + detail),
            RecheckVerdict.Unverified => (TrustState.Limited, "Unele probe nu au putut fi verificate; integritatea nu este dovedită complet." + detail),
            RecheckVerdict.Missing => (TrustState.Compromised, "Probe sau rezultate lipsesc din caz; rezultatele care depind de ele sunt invalidate." + detail),
            RecheckVerdict.Modified => (TrustState.Compromised, "Probe sau rezultate au fost modificate față de achiziție; rezultatele care depind de ele sunt invalidate." + detail),
            _ => (TrustState.Compromised, "Lanțul de custodie sau de audit este rupt; nu folosiți rezultatele ca probă." + detail),
        };
    }

    private static List<string> NextSteps(HomeInputs i, int total, AttentionLevel level, TrustState trust)
    {
        var steps = new List<string>();
        if (!i.CaseOpen) { steps.Add("Alegeți: verificați acest calculator, analizați probe sau deschideți un caz existent."); return steps; }
        if (trust == TrustState.Compromised) steps.Add("Nu folosiți rezultatele ca probă: investigați probele modificate sau lipsă și reachiziționați-le din sursa originală.");
        if (i.AnalysisState == OperationState.NotStarted) steps.Add("Rulați analiza pe probele cazului.");
        else if (i.AnalysisState is OperationState.Failed or OperationState.Cancelled or OperationState.Blocked) steps.Add("Analiza nu s-a finalizat: reluați-o după ce remediați cauza.");
        if (level >= AttentionLevel.High) steps.Add($"Revizuiți constatările de severitate ridicată sau critică ({i.FindingsBySeverity.GetValueOrDefault(Severity.Critical) + i.FindingsBySeverity.GetValueOrDefault(Severity.High)}).");
        else if (level is AttentionLevel.Medium or AttentionLevel.Low) steps.Add($"Revizuiți cele {total} constatări.");
        if (i.VerificationCounts is { } vc)
        {
            int bad = vc.GetValueOrDefault(StandardState.Contradicted.ToSpec()) + vc.GetValueOrDefault(StandardState.Rejected.ToSpec());
            int weak = vc.GetValueOrDefault(StandardState.Unproven.ToSpec());
            if (bad > 0) steps.Add($"{bad} constatări sunt contrazise sau respinse: nu le prezentați ca fapte.");
            else if (weak > 0) steps.Add($"{weak} constatări sunt nedovedite: căutați probe suplimentare.");
        }
        var holes = i.Coverage.Gaps;
        if (holes.Count > 0) steps.Add("Completați sursele lipsă sau parțiale: " + string.Join(", ", holes.Take(4).Select(h => h.Family)) + (holes.Count > 4 ? $" și încă {holes.Count - 4}" : "") + ".");
        if (i.ScopeNote is { Length: > 0 }) steps.Add("Confirmați scopul cazului (acum: " + i.ScopeNote + ").");
        if (i.ReadOnly) steps.Add("Cazul este deschis doar pentru citire; nu se pot adăuga probe sau rula analize.");
        if (steps.Count == 0) steps.Add("Nicio acțiune urgentă; păstrați cazul și lanțul de custodie.");
        return steps.Take(MaxNextSteps).ToList();
    }

    private static string SeverityWord(Severity s) => s switch
    {
        Severity.Critical => "Critică", Severity.High => "Ridicată", Severity.Medium => "Medie", Severity.Low => "Scăzută", _ => "Informativă",
    };

    private static string StateWord(OperationState s) => s switch
    {
        OperationState.Running => "în desfășurare", OperationState.Failed => "eșuată", OperationState.Cancelled => "oprită", OperationState.Blocked => "blocată", _ => "nepornită",
    };
}
