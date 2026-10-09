using System.Globalization;
using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Verification;

/// <summary>One row of Analysis/timeline.csv, read back from disk (never produced by calling a parser).</summary>
public sealed record TimelineRow(DateTimeOffset? Time, string TimeSemantics, string Source, string EventId, string Path, string Process,
                                 string Summary, string EvidenceId, string Locator, string TimeRaw);

/// <summary>The graph's entities and relationships as read from Analysis/graph.json.</summary>
public sealed class GraphFacts
{
    public HashSet<string> EntityIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GraphEdge> Edges { get; } = [];
}

public sealed record GraphEdge(string Id, string Source, string Target, string Type, string EvidenceId, string Derivation);

/// <summary>A window of unreliable time (clock jump, zone change, record-order inversion) read from Analysis/policy_timeline.json.</summary>
public sealed record ManipulatedWindowFact(DateTimeOffset Start, DateTimeOffset End, string Kind, string Description);

/// <summary>Everything the checks read, loaded once per run. Every input is optional: a missing or unreadable one is a note, never a crash.</summary>
public sealed class CaseFacts
{
    public required CaseWorkspace Case { get; init; }
    public Dictionary<string, EvidenceItem> Evidence { get; } = new(StringComparer.Ordinal);
    public FindingsFile? FindingsFile { get; set; }
    public List<TimelineRow>? Timeline { get; set; }
    public Dictionary<(string, string), List<TimelineRow>> RowsByRef { get; } = [];
    public ILookup<string, TimelineRow> RowsByEvidence { get; set; } = Array.Empty<TimelineRow>().ToLookup(r => r.EvidenceId);
    public GraphFacts? Graph { get; set; }
    public DependencyIndex? Dependencies { get; set; }
    /// <summary>Evidence id → parser ids that produced rows (from Analysis/parsing.json); null when the file is absent.</summary>
    public Dictionary<string, List<string>>? ParsersByEvidence { get; set; }
    /// <summary>Evidence id → why it is not intact (MODIFIED / MISSING), from this run's own hash check and from the re-check files.</summary>
    public Dictionary<string, string> BrokenEvidence { get; } = new(StringComparer.Ordinal);
    /// <summary>Finding id → invalidation reason, from Analysis/invalidations.json.</summary>
    public Dictionary<string, string> InvalidatedFindings { get; } = new(StringComparer.Ordinal);
    public bool InvalidationsRead { get; set; }
    public DateTimeOffset? LatestAcquisitionUtc { get; set; }
    /// <summary>WP15b: intervals in which the clock was moved, the zone changed or record order contradicts time (Analysis/policy_timeline.json, TimeWindows). Empty when the file is absent.</summary>
    public List<ManipulatedWindowFact> ManipulatedWindows { get; } = [];
    public JsonDocument? AiReasoning { get; set; }
    public Dictionary<string, string> Inputs { get; } = new(StringComparer.Ordinal);
    public List<string> Notes { get; } = [];

    public string AnalysisPath(string file) => System.IO.Path.Combine(Case.Root, "Analysis", file);

    public static CaseFacts Load(CaseWorkspace ws, VerificationOptions o, IEnumerable<string> evidenceToHash, CancellationToken ct)
    {
        var f = new CaseFacts { Case = ws };
        void Input(string name, bool present) => f.Inputs[name] = present ? "prezent" : "lipsește";

        foreach (var e in ws.LoadEvidence()) f.Evidence[e.EvidenceId] = e;
        f.LatestAcquisitionUtc = f.Evidence.Count == 0 ? null : f.Evidence.Values.Max(e => e.AcquiredAtUtc);
        Input("Evidence/evidence_index.jsonl", f.Evidence.Count > 0);

        // findings.json (read only)
        var fp = f.AnalysisPath("findings.json");
        Input("Analysis/findings.json", File.Exists(fp));
        if (File.Exists(fp))
            try { f.FindingsFile = FindingsFile.Read(fp); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            { f.Notes.Add("findings.json nu a putut fi citit: " + ex.Message); }

        // timeline.csv
        var tp = f.AnalysisPath("timeline.csv");
        Input("Analysis/timeline.csv", File.Exists(tp));
        if (File.Exists(tp))
            try { f.Timeline = ReadTimeline(tp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { f.Notes.Add("timeline.csv nu a putut fi citit: " + ex.Message); }
        if (f.Timeline is not null)
        {
            foreach (var r in f.Timeline)
            {
                if (!f.RowsByRef.TryGetValue((r.EvidenceId, r.Locator), out var l)) f.RowsByRef[(r.EvidenceId, r.Locator)] = l = [];
                l.Add(r);
            }
            f.RowsByEvidence = f.Timeline.ToLookup(r => r.EvidenceId, StringComparer.Ordinal);
        }

        // graph.json
        var gp = f.AnalysisPath("graph.json");
        Input("Analysis/graph.json", File.Exists(gp));
        if (File.Exists(gp))
            try { f.Graph = ReadGraph(gp); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            { f.Notes.Add("graph.json nu a putut fi citit: " + ex.Message); }

        // dependencies.json
        var dp = f.AnalysisPath("dependencies.json");
        Input("Analysis/dependencies.json", File.Exists(dp));
        if (File.Exists(dp))
            try { f.Dependencies = DependencyIndex.Read(dp); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            { f.Notes.Add("dependencies.json nu a putut fi citit: " + ex.Message); }

        // parsing.json: which parser produced rows for which evidence
        var pp = f.AnalysisPath("parsing.json");
        Input("Analysis/parsing.json", File.Exists(pp));
        if (File.Exists(pp))
            try { f.ParsersByEvidence = ReadParsers(pp); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { f.Notes.Add("parsing.json nu a putut fi citit: " + ex.Message); }

        // invalidations.json (written by the re-check) and integrity_recheck.json
        var ip = f.AnalysisPath("invalidations.json");
        Input("Analysis/invalidations.json", File.Exists(ip));
        try
        {
            var inv = ws.LoadInvalidations();
            f.InvalidationsRead = File.Exists(ip);
            foreach (var i in inv.Items)
            {
                if (i.Kind == "finding") f.InvalidatedFindings[i.Id] = i.Reason;
                foreach (var ev in i.EvidenceIds) f.BrokenEvidence.TryAdd(ev, "invalidată de reverificare: " + i.Reason);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        { f.Notes.Add("invalidations.json nu a putut fi citit: " + ex.Message); }
        var rp = f.AnalysisPath("integrity_recheck.json");
        Input("Analysis/integrity_recheck.json", File.Exists(rp));
        if (File.Exists(rp))
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(rp));
                if (doc.RootElement.TryGetProperty("evidence", out var evs) || doc.RootElement.TryGetProperty("Evidence", out evs))
                    foreach (var c in evs.EnumerateArray())
                    {
                        var id = Str(c, "evidenceId"); var status = Str(c, "status");
                        if (id.Length > 0 && status.ToUpperInvariant() is "MODIFIED" or "MISSING") f.BrokenEvidence.TryAdd(id, $"reverificarea a găsit proba {status.ToUpperInvariant()}");
                    }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { f.Notes.Add("integrity_recheck.json nu a putut fi citit: " + ex.Message); }

        // This run's own hash check of the evidence the findings rest on (independent of whether a re-check ran).
        if (o.RehashEvidence)
            foreach (var id in evidenceToHash.Distinct(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!f.Evidence.TryGetValue(id, out var e)) continue;
                var full = ws.FullPath(e.StoredPath);
                if (!File.Exists(full)) { f.BrokenEvidence.TryAdd(id, "fișierul probei LIPSEȘTE din caz"); continue; }
                if (e.Sha256.Length != 64) continue;   // no baseline: reported by PROVENANCE
                try
                {
                    var actual = Hashing.Sha256File(full, ct);
                    if (!string.Equals(actual, e.Sha256, StringComparison.OrdinalIgnoreCase)) f.BrokenEvidence.TryAdd(id, "proba este MODIFICATĂ (SHA-256 diferit de cel de la achiziție)");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { f.Notes.Add($"proba {id} nu a putut fi recitită pentru verificarea hash-ului: {ex.Message}"); }
            }

        // policy_timeline.json (WP15b): only its manipulated-time windows are used here
        var ptp = f.AnalysisPath("policy_timeline.json");
        Input("Analysis/policy_timeline.json", File.Exists(ptp));
        if (File.Exists(ptp))
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ptp));
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("TimeWindows", out var tw) && tw.ValueKind == JsonValueKind.Array)
                    foreach (var w in tw.EnumerateArray())
                        if (DateTimeOffset.TryParse(Str(w, "Start"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var a)
                            && DateTimeOffset.TryParse(Str(w, "End"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var b))
                            f.ManipulatedWindows.Add(new ManipulatedWindowFact(a, b, Str(w, "Kind"), Str(w, "Description")));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { f.Notes.Add("policy_timeline.json nu a putut fi citit: " + ex.Message); }

        // AI analysis, when one was run
        var ap = f.AnalysisPath("ai_reasoning.json");
        Input("Analysis/ai_reasoning.json", File.Exists(ap));
        if (File.Exists(ap))
            try { f.AiReasoning = JsonDocument.Parse(File.ReadAllText(ap)); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { f.Notes.Add("ai_reasoning.json nu a putut fi citit: " + ex.Message); }
        return f;
    }

    private static string Str(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
        return "";
    }

    private static List<TimelineRow> ReadTimeline(string path)
    {
        var rows = new List<TimelineRow>();
        foreach (var d in CsvReader.ReadDicts(path))
        {
            string G(string k) => d.TryGetValue(k, out var v) ? v : "";
            DateTimeOffset? t = DateTimeOffset.TryParse(G("TimeUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
            rows.Add(new TimelineRow(t, G("TimeSemantics"), G("Source"), G("EventId"), G("Path"), G("Process"), G("Summary"), G("EvidenceId"), G("Locator"), G("TimeRaw")));
        }
        return rows;
    }

    private static GraphFacts ReadGraph(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("graph.json nu este un obiect JSON.");
        SchemaVersions.Accept(root.TryGetProperty("schema_version", out var v) ? v.GetString() : null, "graph.json");
        var g = new GraphFacts();
        foreach (var p in root.EnumerateObject())
        {
            if (p.Name.Equals("Entities", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
                foreach (var e in p.Value.EnumerateArray()) { var id = Str(e, "id"); if (id.Length > 0) g.EntityIds.Add(id); }
            else if (p.Name.Equals("Relationships", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
                foreach (var r in p.Value.EnumerateArray())
                    g.Edges.Add(new GraphEdge(Str(r, "relationshipId"), Str(r, "sourceEntity"), Str(r, "targetEntity"), Str(r, "type"), Str(r, "evidenceId"), Str(r, "derivation")));
        }
        return g;
    }

    private static Dictionary<string, List<string>> ReadParsers(string path)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var id = Str(e, "evidenceId"); var parser = Str(e, "parser"); var status = Str(e, "status").ToUpperInvariant();
            if (id.Length == 0 || parser.Length == 0 || parser == "-") continue;
            // status is the spec name (SUCCESS...) or the legacy number; only a run that produced rows counts as "a parser recorded".
            if (status is "SUCCESS" or "PARTIAL" or "EMPTY" or "0" or "1" or "4")
            {
                if (!result.TryGetValue(id, out var l)) result[id] = l = [];
                if (!l.Contains(parser)) l.Add(parser);
            }
        }
        return result;
    }
}
