using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>One input of a run: an evidence item with the hash it had in the evidence index when the run ended.</summary>
public sealed record RunInput(string EvidenceId, string OriginalName, string Sha256, string State);

/// <summary>A parser as it ran on one evidence item (name, version, maturity, records read, status).</summary>
public sealed record RunParser(string EvidenceId, string Parser, string Version, string Maturity, string Status, int Records);

/// <summary>A detection rule loaded for the run, identified by its content hash.</summary>
public sealed record RunRule(string RuleId, string Version, string Kind, string Sha256);

/// <summary>An output of the run with its SHA-256 at the moment the run was recorded.</summary>
public sealed record RunOutput(string Path, string Sha256);

/// <summary>
/// A finding reduced to what a comparison between runs needs. <see cref="Key"/> does not depend on the F-nnnn number, which a new run may
/// give differently: rule id + the sorted evidence locators it rests on (or, without evidence, rule id + title + first time).
/// </summary>
public sealed record FindingSnapshot(string Key, string FindingId, string RuleId, string Title, Severity Severity, Confidence Confidence, string Classification,
    DateTimeOffset? FirstSeenUtc, DateTimeOffset? LastSeenUtc, string Host, string User, int EvidenceCount, string DescriptionSha256)
{
    public static FindingSnapshot Of(Finding f)
    {
        var locators = f.SupportingEvidence.Select(e => e.EvidenceId + "@" + e.Locator).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var basis = locators.Count > 0 ? string.Join("\n", locators) : f.Title + "\n" + (f.FirstSeenUtc ?? f.LastSeenUtc)?.ToString("O");
        return new FindingSnapshot(f.RuleId + ":" + AnalysisRuns.Sha(basis)[..16], f.FindingId, f.RuleId, f.Title, f.Severity, f.Confidence, f.Classification.ToString(),
            f.FirstSeenUtc, f.LastSeenUtc, f.Host, f.User, f.SupportingEvidence.Count, AnalysisRuns.Sha(f.Description));
    }
}

/// <summary>
/// The immutable manifest of one analysis run (master plan 6.16, annex B): toolchain (application, finding contract and rule versions, parsers,
/// detection rules), inputs (evidence with hashes), configuration (time zone, scope period), outputs with hashes and the finding snapshot.
/// </summary>
public sealed class AnalysisRunManifest
{
    public const string Schema = "loganalyzer.analysis_run/1";
    public string SchemaVersion { get; set; } = Schema;
    public string RunId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }
    public string Operator { get; set; } = "";
    public string ApplicationVersion { get; set; } = "";
    public string RuleContractVersion { get; set; } = "";
    public string FindingContractVersion { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public DateTimeOffset? ScopeFromUtc { get; set; }
    public DateTimeOffset? ScopeToUtc { get; set; }
    public string State { get; set; } = "";
    public int TimelineEvents { get; set; }
    public List<RunInput> Inputs { get; set; } = [];
    public List<RunParser> Parsers { get; set; } = [];
    public List<RunRule> Rules { get; set; } = [];
    public List<RunOutput> Outputs { get; set; } = [];
    public List<FindingSnapshot> Findings { get; set; } = [];
    /// <summary>How the run was asked for: collection profile, registers and hash sets used, procedure profile (review 2026-11-08, P6: without them a re-run with other settings looked unexplained).</summary>
    public SortedDictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
}

public enum ChangeSource { Inputs, Toolchain, Unexplained }

/// <summary>One field of one finding that differs between two runs.</summary>
public sealed record FieldChange(string Field, string Before, string After);

public sealed record FindingChange(string Key, string RuleId, string Title, string BeforeId, string AfterId, IReadOnlyList<FieldChange> Fields);

/// <summary>
/// The difference between two runs of the same case. Each change carries the most likely source: different inputs, a different toolchain, or
/// neither — then it is unexplained and goes to review (a run is not an oracle: the same inputs and tools should give the same result).
/// </summary>
public sealed class RunDiff
{
    public string BeforeRunId { get; init; } = "";
    public string AfterRunId { get; init; } = "";
    public List<string> InputChanges { get; } = [];
    public List<string> ToolchainChanges { get; } = [];
    public List<FindingSnapshot> Added { get; } = [];
    public List<FindingSnapshot> Removed { get; } = [];
    public List<FindingChange> Changed { get; } = [];
    public int Unchanged { get; set; }
    public ChangeSource Source => InputChanges.Count > 0 ? ChangeSource.Inputs : ToolchainChanges.Count > 0 ? ChangeSource.Toolchain : ChangeSource.Unexplained;
    public bool HasFindingChanges => Added.Count + Removed.Count + Changed.Count > 0;
    /// <summary>Finding changes no input or toolchain change explains: they need a reviewer's look.</summary>
    public bool NeedsReview => HasFindingChanges && Source == ChangeSource.Unexplained;

    public string Summary => !HasFindingChanges
        ? $"Aceleași constatări ({Unchanged})" + (InputChanges.Count + ToolchainChanges.Count > 0 ? ", deși intrările sau uneltele diferă" : "")
        : $"{Added.Count} noi, {Removed.Count} dispărute, {Changed.Count} modificate, {Unchanged} neschimbate; cauza probabilă: " + Source switch
        {
            ChangeSource.Inputs => "probe diferite",
            ChangeSource.Toolchain => "altă versiune a aplicației, a regulilor sau a parserelor",
            _ => "NEEXPLICATĂ (aceleași probe, aceleași unelte): de verificat",
        };
}

public sealed record RunListItem(string RunId, DateTimeOffset FinishedUtc, string ApplicationVersion, int Findings, int Inputs, string Integrity, bool IntegrityOk);

/// <summary>
/// Immutable analysis runs (master plan 6.16): after every run the manifest, the finding snapshot and the diff against the previous run are
/// written once under <c>Analysis/Runs/&lt;run id&gt;/</c>, marked read-only and registered in custody (so the open-time re-check sees any change),
/// and the manifest hash goes to the case audit. A new run never rewrites an earlier one: an existing run folder is a refusal, not an overwrite.
/// </summary>
public static class AnalysisRuns
{
    public const string RunsDir = "Analysis/Runs";
    public const string ManifestName = "run_manifest.json";
    public const string DiffName = "diff_previous.json";

    internal static string Sha(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s ?? "")));

    /// <summary>The outputs of the pipeline whose hashes go into the manifest (those that exist).</summary>
    public static readonly string[] TrackedOutputs =
        ["Analysis/findings.json", "Analysis/timeline.csv", "Analysis/graph.json", "Analysis/detections.json", "Analysis/rules.json", "Analysis/parsing.json", "Analysis/anti_forensics.json"];

    public static AnalysisRunManifest Build(CaseWorkspace ws, DateTimeOffset startedUtc, IReadOnlyList<Finding> findings, IReadOnlyList<ParseResult> parsing,
        IReadOnlyList<DetectionRule> rules, int timelineEvents, string state, string findingContractVersion, IReadOnlyDictionary<string, string>? settings = null)
    {
        var m = new AnalysisRunManifest
        {
            CaseId = ws.Info.CaseId, StartedUtc = startedUtc, FinishedUtc = DateTimeOffset.UtcNow, Operator = OperatorIdentity.Who,
            ApplicationVersion = DfirInfo.ApplicationVersion, RuleContractVersion = LogAnalyzer.Dfir.Analysis.RuleContract.RuleVersion,
            FindingContractVersion = findingContractVersion, TimeZone = ws.Info.Timezone, ScopeFromUtc = ws.Info.Scope.PeriodFromUtc, ScopeToUtc = ws.Info.Scope.PeriodToUtc,
            State = state, TimelineEvents = timelineEvents,
        };
        m.Inputs = ws.LoadEvidence().OrderBy(e => e.EvidenceId, StringComparer.Ordinal).Select(e => new RunInput(e.EvidenceId, e.OriginalName, e.Sha256, e.State.ToString())).ToList();
        m.Parsers = parsing.OrderBy(p => p.EvidenceId, StringComparer.Ordinal).ThenBy(p => p.Parser, StringComparer.Ordinal)
            .Select(p => new RunParser(p.EvidenceId, p.Parser, p.ParserVersion, p.ParserStatus, p.Status.ToSpec(), p.Records)).ToList();
        m.Rules = rules.OrderBy(r => r.RuleId, StringComparer.Ordinal).Select(r => new RunRule(r.RuleId, r.Version, r.Kind.ToString(), r.Sha256)).ToList();
        foreach (var rel in TrackedOutputs)
            if (ws.TryFullPath(rel, out var full) && File.Exists(full)) m.Outputs.Add(new RunOutput(rel, Hashing.Sha256File(full)));
        if (settings is not null) foreach (var (k, v) in settings) m.Settings[k] = v;
        // Findings that share a key (same rule and evidence, e.g. one per user) are numbered within the key (always, so a key stays stable when the count changes) in a content order that does not depend
        // on the F-number, so none hides another in a diff (review 2026-11-08, P1).
        m.Findings = findings.Select(FindingSnapshot.Of)
            .GroupBy(f => f.Key, StringComparer.Ordinal)
            .SelectMany(g => g.OrderBy(f => f.Host, StringComparer.Ordinal).ThenBy(f => f.User, StringComparer.Ordinal)
                                                  .ThenBy(f => f.FirstSeenUtc).ThenBy(f => f.Title, StringComparer.Ordinal).ThenBy(f => f.DescriptionSha256, StringComparer.Ordinal)
                                                  .Select((f, i) => f with { Key = $"{f.Key}#{i + 1}" }))
            .OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        return m;
    }

    private static string RunsRoot(CaseWorkspace ws) => Path.Combine(ws.Root, "Analysis", "Runs");

    /// <summary>
    /// Records the run: a new folder (RUN-yyyyMMdd-HHmmss, suffixed when two runs end in the same second), the manifest, the diff against the latest
    /// earlier run, both read-only and registered in custody; the manifest SHA-256 goes to the audit as <c>analysis.run_recorded</c>.
    /// </summary>
    public static (AnalysisRunManifest Manifest, RunDiff? Diff) Record(CaseWorkspace ws, AnalysisRunManifest m)
    {
        var previous = List(ws).Where(r => r.IntegrityOk).Select(r => r.RunId).LastOrDefault();
        var root = RunsRoot(ws);
        Directory.CreateDirectory(root);
        var id = $"RUN-{m.FinishedUtc:yyyyMMdd-HHmmss}";
        for (int i = 2; Directory.Exists(Path.Combine(root, id)); i++) id = $"RUN-{m.FinishedUtc:yyyyMMdd-HHmmss}-{i}";
        m.RunId = id;
        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(dir);

        var manifestPath = Path.Combine(dir, ManifestName);
        var json = JsonSerializer.Serialize(m, Json.Options);
        WriteOnce(manifestPath, json);
        ws.RecordOutput($"{RunsDir}/{id}/{ManifestName}", "AnalysisRuns", "1", m.Inputs.Select(i => i.EvidenceId));
        ws.Audit("analysis.run_recorded", $"{id} manifest_sha256={Sha(json)} findings={m.Findings.Count} inputs={m.Inputs.Count} app={m.ApplicationVersion}");

        RunDiff? diff = null;
        if (previous is not null && Load(ws, previous) is { } before)
        {
            diff = Diff(before, m);
            WriteOnce(Path.Combine(dir, DiffName), JsonSerializer.Serialize(new
            {
                diff.BeforeRunId, diff.AfterRunId, Source = diff.Source.ToString(), diff.NeedsReview, diff.Summary, diff.InputChanges, diff.ToolchainChanges,
                diff.Added, diff.Removed, diff.Changed, diff.Unchanged,
            }, Json.Options));
            ws.RecordOutput($"{RunsDir}/{id}/{DiffName}", "AnalysisRuns", "1");
            ws.Audit(diff.NeedsReview ? "analysis.run_diff_unexplained" : "analysis.run_diff", $"{previous} -> {id}: {diff.Summary}");
        }
        return (m, diff);
    }

    private static void WriteOnce(string path, string text)
    {
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))   // CreateNew: an earlier run is never overwritten
        using (var w = new StreamWriter(fs, new UTF8Encoding(false))) w.Write(text);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
    }

    /// <summary>Reads a recorded run, or null when its manifest is missing or unreadable.</summary>
    public static AnalysisRunManifest? Load(CaseWorkspace ws, string runId)
    {
        if (!IsRunId(runId)) return null;
        var p = Path.Combine(RunsRoot(ws), runId, ManifestName);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<AnalysisRunManifest>(File.ReadAllText(p), Json.Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>Recorded in the manifest but never compared: they say how evidence was collected, not how it was analysed.</summary>
    public static readonly HashSet<string> CollectionOnlySettings = new(StringComparer.Ordinal) { "colectare", "profil_colectare" };

    /// <summary>"RUN-" + date, time and an optional counter; nothing that could leave the Runs folder (review P8: "C:foo" escaped through Path.Combine).</summary>
    public static bool IsRunId(string id) => System.Text.RegularExpressions.Regex.IsMatch(id ?? "", @"^RUN-\d{8}-\d{6}(-\d{1,4})?$");

    /// <summary>
    /// The recorded runs, oldest first, each with an integrity verdict: its manifest hash must equal the one the audit recorded when the run ended.
    /// A run the audit recorded whose folder is gone is listed too, as missing (review P5).
    /// </summary>
    public static IReadOnlyList<RunListItem> List(CaseWorkspace ws)
    {
        var root = RunsRoot(ws);
        var audited = AuditedManifestHashes(ws);
        var list = new List<RunListItem>();
        var onDisk = Directory.Exists(root) ? Directory.EnumerateDirectories(root, "RUN-*").Select(Path.GetFileName).OfType<string>() : [];
        foreach (var id in onDisk.Union(audited.Keys, StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal))
        {
            var dir = Path.Combine(root, id);
            if (!Directory.Exists(dir)) { list.Add(new RunListItem(id, default, "", 0, 0, "folderul rulării LIPSEȘTE (înregistrată în audit)", false)); continue; }
            var p = Path.Combine(dir, ManifestName);
            if (!File.Exists(p)) { list.Add(new RunListItem(id, default, "", 0, 0, "manifest lipsă", false)); continue; }
            var text = File.ReadAllText(p);
            var m = Load(ws, id);
            string verdict; bool ok;
            if (m is null) (verdict, ok) = ("manifest ilizibil", false);
            else if (!audited.TryGetValue(id, out var sha)) (verdict, ok) = ("neînregistrat în auditul cazului", false);
            else if (!string.Equals(sha, Sha(text), StringComparison.Ordinal)) (verdict, ok) = ("MODIFICAT după înregistrare", false);
            else (verdict, ok) = ("intact (amprenta din audit)", true);
            list.Add(new RunListItem(id, m?.FinishedUtc ?? default, m?.ApplicationVersion ?? "", m?.Findings.Count ?? 0, m?.Inputs.Count ?? 0, verdict, ok));
        }
        return list;
    }

    private static Dictionary<string, string> AuditedManifestHashes(CaseWorkspace ws)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(ws.AuditChainPath)) return d;
        // Only the verified part of the chain counts (review P4: a line inserted before the real one was trusted while the chain was broken).
        var chain = ws.VerifyChains().Audit;
        long upTo = chain.Status == ChainStatus.Broken ? (chain.BrokenAtSeq ?? 1) - 1 : long.MaxValue;
        // The position decides, not the seq a line claims (review round 2: a forged line claiming seq 1 was accepted): walk the chained lines in
        // file order and stop at the first one that does not continue the previous (seq + 1, prevHash = previous hash) or lies past the verified part.
        long lastSeq = 0; string? lastHash = null;
        foreach (var line in File.ReadLines(ws.AuditChainPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            System.Text.Json.Nodes.JsonObject? node;
            try { node = System.Text.Json.Nodes.JsonNode.Parse(line)?.AsObject(); }
            catch (JsonException) { break; }
            if (node?["seq"] is null) { if (lastHash is null) continue; break; }   // legacy lines before the chain are skipped; after it, the chain ended
            long seq; string prev, hash;
            try { seq = node["seq"]!.GetValue<long>(); prev = node["prevHash"]?.GetValue<string>() ?? ""; hash = node["hash"]?.GetValue<string>() ?? ""; }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException) { break; }
            if (seq > upTo || (lastHash is not null && (seq != lastSeq + 1 || !string.Equals(prev, lastHash, StringComparison.Ordinal)))) break;
            lastSeq = seq; lastHash = hash;
            if (!line.Contains("\"analysis.run_recorded\"", StringComparison.Ordinal)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<AuditEntry>(line, Json.Options);
                if (e is null || e.Action != "analysis.run_recorded") continue;
                var parts = e.Detail.Split(' ');
                var sha = parts.FirstOrDefault(p => p.StartsWith("manifest_sha256=", StringComparison.Ordinal))?["manifest_sha256=".Length..];
                if (parts.Length > 0 && IsRunId(parts[0]) && sha is not null) d.TryAdd(parts[0], sha);   // first verified record wins
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return d;
    }

    /// <summary>Compares two runs field by field. Pure: nothing is written.</summary>
    public static RunDiff Diff(AnalysisRunManifest before, AnalysisRunManifest after)
    {
        var d = new RunDiff { BeforeRunId = before.RunId, AfterRunId = after.RunId };

        // grouped, never ToDictionary: a duplicate id (the same IOC file in the application and in the case Rules folder) must not throw (review P2)
        var bi = before.Inputs.GroupBy(i => i.EvidenceId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var ai = after.Inputs.GroupBy(i => i.EvidenceId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var i in after.Inputs.Where(i => !bi.ContainsKey(i.EvidenceId))) d.InputChanges.Add($"probă nouă {i.EvidenceId} ({i.OriginalName})");
        foreach (var i in before.Inputs.Where(i => !ai.ContainsKey(i.EvidenceId))) d.InputChanges.Add($"probă lipsă {i.EvidenceId} ({i.OriginalName})");
        foreach (var i in after.Inputs.Where(i => bi.TryGetValue(i.EvidenceId, out var b) && !string.Equals(b.Sha256, i.Sha256, StringComparison.OrdinalIgnoreCase)))
            d.InputChanges.Add($"amprentă diferită {i.EvidenceId} ({i.OriginalName})");
        if (before.ScopeFromUtc != after.ScopeFromUtc || before.ScopeToUtc != after.ScopeToUtc) d.InputChanges.Add("perioada scopului diferă");
        if (!string.Equals(before.TimeZone, after.TimeZone, StringComparison.Ordinal)) d.InputChanges.Add($"fusul orar {before.TimeZone} -> {after.TimeZone}");

        void Tool(string what, string b, string a) { if (!string.Equals(b, a, StringComparison.Ordinal)) d.ToolchainChanges.Add($"{what} {b} -> {a}"); }
        Tool("aplicația", before.ApplicationVersion, after.ApplicationVersion);
        Tool("contractul regulilor", before.RuleContractVersion, after.RuleContractVersion);
        Tool("contractul constatărilor", before.FindingContractVersion, after.FindingContractVersion);
        var bp = before.Parsers.Select(p => $"{p.Parser} {p.Version}").ToHashSet(StringComparer.Ordinal);
        var ap = after.Parsers.Select(p => $"{p.Parser} {p.Version}").ToHashSet(StringComparer.Ordinal);
        foreach (var p in ap.Except(bp).Order(StringComparer.Ordinal)) d.ToolchainChanges.Add("parser nou sau altă versiune: " + p);
        foreach (var p in bp.Except(ap).Order(StringComparer.Ordinal)) d.ToolchainChanges.Add("parser absent: " + p);
        static string RuleSet(IEnumerable<RunRule> g) => string.Join(",", g.Select(r => r.Sha256.ToLowerInvariant()).Order(StringComparer.Ordinal));
        var br = before.Rules.GroupBy(r => r.RuleId, StringComparer.Ordinal).ToDictionary(g => g.Key, RuleSet, StringComparer.Ordinal);
        var ar = after.Rules.GroupBy(r => r.RuleId, StringComparer.Ordinal).ToDictionary(g => g.Key, RuleSet, StringComparer.Ordinal);
        foreach (var id in ar.Keys.Where(k => !br.ContainsKey(k)).Order(StringComparer.Ordinal)) d.ToolchainChanges.Add("regulă nouă " + id);
        foreach (var id in br.Keys.Where(k => !ar.ContainsKey(k)).Order(StringComparer.Ordinal)) d.ToolchainChanges.Add("regulă scoasă " + id);
        foreach (var id in ar.Keys.Where(k => br.TryGetValue(k, out var b) && b != ar[k]).Order(StringComparer.Ordinal)) d.ToolchainChanges.Add("regulă modificată " + id);
        // collection settings describe how evidence came in, not how it was analysed: a reanalysis (no collection) must not pass them off as
        // the explanation of a finding change (review round 2)
        foreach (var k in before.Settings.Keys.Union(after.Settings.Keys).Where(k => !CollectionOnlySettings.Contains(k)).Order(StringComparer.Ordinal))
            Tool("setarea " + k, before.Settings.GetValueOrDefault(k, "(lipsă)"), after.Settings.GetValueOrDefault(k, "(lipsă)"));

        // Keys are unique within a run built by Build; a hand-made or older manifest with repeated keys is paired in order and the surplus is
        // reported as added or removed, so no finding disappears silently (review P1).
        var bg = before.Findings.GroupBy(f => f.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var ag = after.Findings.GroupBy(f => f.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var pairs = new List<(FindingSnapshot B, FindingSnapshot A)>();
        foreach (var (key, al) in ag)
        {
            var bl = bg.GetValueOrDefault(key) ?? [];
            for (int i = 0; i < al.Count; i++) { if (i < bl.Count) pairs.Add((bl[i], al[i])); else d.Added.Add(al[i]); }
        }
        foreach (var (key, bl) in bg)
        {
            var n = ag.GetValueOrDefault(key)?.Count ?? 0;
            d.Removed.AddRange(bl.Skip(n));
        }
        foreach (var (b, a) in pairs)
        {
            var fields = new List<FieldChange>();
            void F(string name, string x, string y) { if (!string.Equals(x, y, StringComparison.Ordinal)) fields.Add(new FieldChange(name, x, y)); }
            F("severitate", b.Severity.ToString(), a.Severity.ToString());
            F("încredere", b.Confidence.ToString(), a.Confidence.ToString());
            F("clasificare", b.Classification, a.Classification);
            F("titlu", b.Title, a.Title);
            F("prima apariție", b.FirstSeenUtc?.ToString("O") ?? "", a.FirstSeenUtc?.ToString("O") ?? "");
            F("ultima apariție", b.LastSeenUtc?.ToString("O") ?? "", a.LastSeenUtc?.ToString("O") ?? "");
            F("utilizator", b.User, a.User);
            F("stație", b.Host, a.Host);
            F("probe", b.EvidenceCount.ToString(), a.EvidenceCount.ToString());
            F("descriere", b.DescriptionSha256[..12], a.DescriptionSha256[..12]);
            if (fields.Count > 0) d.Changed.Add(new FindingChange(a.Key, a.RuleId, a.Title, b.FindingId, a.FindingId, fields));
            else d.Unchanged++;
        }
        return d;
    }
}
