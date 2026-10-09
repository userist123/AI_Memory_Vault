using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Home;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using LogAnalyzer.Verification;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>An existing case, rebuilt from the files the pipeline wrote (R9.1).</summary>
public sealed class LoadedCase
{
    public required CaseWorkspace Workspace { get; init; }
    public required InvestigationResult Result { get; init; }
    public CaseLifecycle Lifecycle { get; init; }
    /// <summary>True when the case may not be changed: sealed, archived, invalidated or the integrity re-check failed. The workspace itself refuses writes.</summary>
    public bool ReadOnly { get; init; }
    public IReadOnlyList<string> ReadOnlyReasons { get; init; } = [];
    /// <summary>What could not be restored, in words (e.g. files absent, gaps not stored). Never empty when something was lost.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>Verdict of the re-check run on open (null only when the open was asked to skip it).</summary>
    public RecheckResult? Recheck => Workspace.LastRecheck;
    public required CoverageMatrix Coverage { get; init; }
    public required HomeSummary Home { get; init; }
}

/// <summary>Outcome of opening a case folder: either a <see cref="LoadedCase"/> or the reason it was refused.</summary>
public sealed record CaseLoadResult(LoadedCase? Case, string? RefusedReason)
{
    public bool Opened => Case is not null;
}

/// <summary>
/// WP5 / R9.1: opens an existing case folder. It reads back findings, gaps, collection rows, parse results, timeline, scope and verification from
/// the files the pipeline wrote, runs the integrity re-check (WP3b), audits the open as the signed-in account and decides whether the case is
/// shown read-only. Nothing is repaired or rewritten; a file of an unknown version refuses the whole open with the reason.
/// </summary>
public static class CaseLoader
{
    public static Task<CaseLoadResult> OpenAsync(string root, RecentCases? recent = null, CancellationToken ct = default) =>
        Task.Run(() => Open(root, recent, ct), ct);

    public static CaseLoadResult Open(string root, RecentCases? recent = null, CancellationToken ct = default, bool recheck = true)
    {
        string full;
        try { full = Path.GetFullPath(root); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Refuse("calea cazului nu este validă: " + ex.Message); }
        if (!Directory.Exists(full)) return Refuse($"folderul cazului nu există: {full}");
        var caseJson = Path.Combine(full, "case.json");
        if (!File.Exists(caseJson)) return Refuse("folderul nu conține case.json: nu este un caz LogAnalyzer");

        // Everything that can refuse is read BEFORE the workspace is opened, so a refused folder is not written to (no audit entry in a folder that is not ours).
        var notes = new List<string>();
        CaseInfo info;
        FindingsFile? ff = null;
        List<TimelineEvent> timeline = [];
        List<ParseResult> parsing = [];
        List<CollectionRow> collection = [];
        VerificationReport? verification = null;
        RunState? run = null;
        var analysis = Path.Combine(full, "Analysis");
        try
        {
            info = Json.Read<CaseInfo>(caseJson);
            if (SchemaVersions.Major(info.SchemaVersion) is < 1 or > 1)
                return Refuse($"case.json: versiunea de caz '{info.SchemaVersion}' nu este cunoscută (această aplicație citește 1.x); cazul nu a fost deschis.");

            var manifest = SchemaManifest.Read(analysis);
            var findingsPath = Path.Combine(analysis, "findings.json");
            if (File.Exists(findingsPath))
            {
                ff = FindingsFile.Read(findingsPath);
                if (SchemaVersions.Major(ff.SchemaVersion) < 2) notes.Add($"findings.json este în formatul vechi {ff.SchemaVersion}: câmpurile contractului de constatare lipsesc și au valori implicite (NOT_ASSESSED).");
                if (ff.Collection is { ValueKind: JsonValueKind.Array } c) collection = JsonSerializer.Deserialize<List<CollectionRow>>(c.GetRawText()) ?? [];
            }
            else notes.Add("Analysis/findings.json lipsește: analiza nu a fost rulată pentru acest caz sau fișierul a fost șters; nu există constatări de arătat.");

            var timelinePath = Path.Combine(analysis, "timeline.csv");
            if (File.Exists(timelinePath))
            {
                SchemaVersions.Accept(manifest.VersionOf("timeline.csv"), "timeline.csv");
                timeline = TimelineCsv.Read(timelinePath);
                notes.Add("Cronologia a fost citită din timeline.csv: conține doar coloanele acelui fișier (câmpurile specifice parserelor nu sunt păstrate în el).");
            }
            else if (ff is not null) notes.Add("Analysis/timeline.csv lipsește: cronologia nu poate fi arătată.");

            var parsingPath = Path.Combine(analysis, "parsing.json");
            if (File.Exists(parsingPath)) parsing = JsonSerializer.Deserialize<List<ParseResult>>(File.ReadAllText(parsingPath)) ?? [];
            else if (ff is not null) notes.Add("Analysis/parsing.json lipsește: starea parserelor nu este cunoscută.");

            verification = VerificationReport.Read(Path.Combine(analysis, VerificationReport.FileName));
            if (verification is null && ff is not null) notes.Add("Analysis/verification.json lipsește: verificarea automată este nedeterminată pentru acest caz.");

            var runPath = Path.Combine(analysis, "run_state.json");
            if (File.Exists(runPath)) run = JsonSerializer.Deserialize<RunState>(File.ReadAllText(runPath));
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or FormatException)
        {
            return Refuse("cazul nu poate fi citit: " + ex.Message);
        }
        if (ff is not null) notes.Add("Golurile de probă găsite după scrierea findings.json (reguli neîncărcate, fișiere nescanate de YARA) nu sunt păstrate în fișier; detecțiile, regulile și verificările anti-forensics nu sunt reîncărcate.");

        // Open: audited as the signed-in account (OperatorIdentity), then re-checked.
        CaseWorkspace ws;
        try { ws = CaseWorkspace.Open(full, recheck: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Refuse("cazul nu poate fi deschis: " + ex.Message); }
        if (recheck) ws.Recheck(ct);

        var (lifecycle, reasons) = Classify(ws);
        if (reasons.Count > 0)
        {
            ws.MarkReadOnly(string.Join("; ", reasons));
            ws.Audit("case.opened_readonly", $"{CaseLifecycleNames.Spec(lifecycle)}: {string.Join("; ", reasons)}");
        }

        var result = new InvestigationResult { Case = ws };
        if (ff is not null)
        {
            result.Findings.AddRange(ff.Findings);
            result.Gaps.AddRange(ff.Gaps);
            result.FindingsJson = Path.Combine(analysis, "findings.json");
        }
        result.Collection.AddRange(collection);
        result.Parsing.AddRange(parsing);
        result.Timeline.AddRange(timeline);
        if (File.Exists(Path.Combine(analysis, "timeline.csv"))) result.TimelineCsv = Path.Combine(analysis, "timeline.csv");
        result.Verification = verification;
        if (verification is not null)
        {
            var verdicts = verification.ToContract();
            foreach (var f in result.Findings)
                if (verdicts.TryGetValue(f.FindingId, out var v)) f.Verification = v;
        }
        // The state is the one the run recorded; a case with no findings.json has not been analysed, whatever else is in the folder.
        result.State = ff is null ? OperationState.NotStarted : run?.State ?? OperationState.NotStarted;
        result.StateReason = ff is null ? "Analiza nu a fost rulată pentru acest caz." : run?.Reason ?? "run_state.json lipsește: starea analizei este necunoscută.";
        if (ff is not null && run is null) notes.Add("Analysis/run_state.json lipsește: starea analizei este nedeterminată.");

        var loaded = Describe(result, ws, lifecycle, reasons, notes);
        recent?.Record(ws.Root, ws.Info.Name, lifecycle);
        return new CaseLoadResult(loaded, null);
    }

    /// <summary>Coverage matrix and Home summary for a result (a loaded case or a run that just finished).</summary>
    public static LoadedCase Describe(InvestigationResult r, CaseWorkspace ws, CaseLifecycle lifecycle, IReadOnlyList<string> readOnlyReasons, IReadOnlyList<string> notes)
    {
        var coverage = CoverageOf(r, ws);
        var home = HomeBuilder.Build(r, ws, coverage, readOnlyReasons);
        return new LoadedCase
        {
            Workspace = ws, Result = r, Lifecycle = lifecycle, ReadOnly = readOnlyReasons.Count > 0, ReadOnlyReasons = readOnlyReasons, Notes = notes,
            Coverage = coverage, Home = home,
        };
    }

    public static CoverageMatrix CoverageOf(InvestigationResult r, CaseWorkspace ws) =>
        CoverageMatrix.Build(new CoverageInputs(ws.LoadEvidence(), r.Parsing, r.Collection.Select(c => new CollectorRun(c.Collector, c.Status, c.EvidenceCount, c.Errors)).ToList(),
                                                r.Gaps, WindowsParsers.Registry.Descriptors));

    private static CaseLoadResult Refuse(string reason) => new(null, reason);

    /// <summary>The lifecycle of an opened case and the reasons it is read-only. An active case with a sound re-check has no reasons.</summary>
    private static (CaseLifecycle, List<string>) Classify(CaseWorkspace ws)
    {
        var reasons = new List<string>();
        var life = CaseLifecycle.Active;
        if (IsClosed(ws)) { life = CaseLifecycle.Sealed; reasons.Add("cazul a fost închis (sigilat) de un operator; ancora de închidere este în lanțul de audit"); }
        var evidence = ws.LoadEvidence();
        if (evidence.Count > 0 && evidence.All(e => e.State is EvidenceState.Archived or EvidenceState.Disposed))
        {
            if (life == CaseLifecycle.Active) life = CaseLifecycle.Archived;
            reasons.Add("toate probele sunt ARCHIVED sau DISPOSED");
        }
        if (ws.LastRecheck is { } rc)
        {
            if (rc.InvalidatedCount > 0)
            {
                life = CaseLifecycle.Invalidated;
                reasons.Add($"{rc.InvalidatedCount} rezultate sunt INVALIDATED de reverificare (probe modificate sau lipsă)");
            }
            if (rc.Verdict is RecheckVerdict.ChainBroken or RecheckVerdict.Modified or RecheckVerdict.Missing)
            {
                if (life != CaseLifecycle.Invalidated) life = CaseLifecycle.IntegrityFailed;
                reasons.Add("reverificarea integrității a eșuat: " + rc.Summary);
            }
        }
        return (life, reasons);
    }

    /// <summary>True when the audit chain holds a <c>case.closed</c> entry (written by <see cref="CaseClosure.Close"/>). Read from the chain file, not from the plain log.</summary>
    private static bool IsClosed(CaseWorkspace ws)
    {
        try
        {
            if (!File.Exists(ws.AuditChainPath)) return false;
            foreach (var line in File.ReadLines(ws.AuditChainPath))
                if (line.Contains("\"action\":\"case.closed\"", StringComparison.Ordinal)) return true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }
}
