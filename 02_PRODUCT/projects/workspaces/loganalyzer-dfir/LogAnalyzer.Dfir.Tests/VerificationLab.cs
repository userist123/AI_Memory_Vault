using System.Text.Json;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Graph;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Verification;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// A small case on disk for the verification tests: evidence items, timeline.csv, findings.json, graph.json, dependencies.json, parsing.json
/// written the way the pipeline writes them, so the verifier reads real files (it never gets objects handed over).
/// </summary>
internal sealed class VerificationLab : IDisposable
{
    public static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddDays(-2);
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ladfir_wp4_" + Guid.NewGuid().ToString("N"));
    public CaseWorkspace Ws { get; }
    public List<TimelineEvent> Events { get; } = [];
    public List<Finding> Findings { get; } = [];
    public Dictionary<string, EvidenceItem> Ev { get; } = [];

    public VerificationLab()
    {
        Ws = CaseWorkspace.Create(Path.Combine(Dir, "case"), new CaseInfo
        { CaseId = "CASE-V", Name = "v", CreatedAtUtc = DateTimeOffset.UtcNow, Host = "AUDITED", Scope = TestScopes.Valid() });
        Directory.CreateDirectory(Path.Combine(Ws.Root, "Analysis"));
    }

    public void Dispose()
    {
        if (!Directory.Exists(Dir)) return;
        foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Dir, true);
    }

    public string Analysis(string file) => Path.Combine(Ws.Root, "Analysis", file);

    public EvidenceItem Evidence(string key, string sourceType = "evtx")
    {
        var p = Path.Combine(Ws.RawDir("s"), key + ".bin");
        File.WriteAllText(p, "content-" + key);
        return Ev[key] = Ws.RegisterStored(p, key + ".bin", "s", sourceType, TemporalType.Historical, "unit", "1");
    }

    public void Tamper(string key)
    {
        var full = Ws.FullPath(Ev[key].StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        File.AppendAllText(full, "TAMPERED");
    }

    public void Remove(string key)
    {
        var full = Ws.FullPath(Ev[key].StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        File.Delete(full);
    }

    public TimelineEvent Event(string source, string evidenceKey, string locator, DateTimeOffset? time, string eventId = "", string path = "", string summary = "x",
                               string semantics = "recorded", string process = "")
    {
        var e = new TimelineEvent
        {
            Time = time is { } t ? Timestamp.FromUtc(t.UtcDateTime, "raw", "test") : Timestamp.Unknown("garbage"),
            Source = source, EvidenceId = Ev[evidenceKey].EvidenceId, Locator = locator, EventId = eventId, Path = path, Summary = summary, TimeSemantics = semantics, Process = process,
        };
        Events.Add(e);
        return e;
    }

    public EvidenceRef Ref(string evidenceKey, string locator, bool withHash = true) =>
        new(Ev[evidenceKey].EvidenceId, locator, "d", withHash ? Ev[evidenceKey].Sha256 : "");

    public Finding Add(string id, string rule, SemanticType sem, DateTimeOffset? first, DateTimeOffset? last, params EvidenceRef[] refs)
    {
        var f = new Finding
        {
            FindingId = id, RuleId = rule, Title = "t " + id, Description = "d", Classification = Classification.Direct, Confidence = Confidence.Medium,
            FirstSeenUtc = first, LastSeenUtc = last, SemanticType = sem, SupportingEvidence = refs.ToList(),
        };
        Findings.Add(f);
        return f;
    }

    public void WriteTimeline()
    {
        string[] header = ["TimeUtc", "TimeSemantics", "Source", "EventId", "Provider", "Host", "User", "Process", "Pid", "Path", "RemoteIp", "RemotePort", "Dns", "Summary", "Classification", "EvidenceId", "Locator", "SourceSha256", "Parser", "ParserVersion",
                           "TimeRaw", "TimeConversion", "TimeZoneBasis", "TimeUncertainty", "SemanticType"];
        using var w = new CsvWriter(Analysis("timeline.csv"), header);
        foreach (var e in Events)
            w.WriteRow(new object?[] { e.Time.Utc?.ToString("o") ?? "", e.TimeSemantics, e.Source, e.EventId, e.Provider, e.Host, e.User, e.Process, e.Pid, e.Path, e.RemoteIp, e.RemotePort, e.Dns, e.Summary, "DIRECT", e.EvidenceId, e.Locator, "", "p", "1",
                                   e.Time.Raw, e.Time.ConversionMethod, "", "", "OBSERVATION" });
    }

    public void WriteFindings(Action<JsonObject>? mutate = null)
    {
        var json = SchemaVersions.WithVersion(new { Findings, Gaps = new List<EvidenceGap>() }, SchemaVersions.Findings);
        if (mutate is not null)
        {
            var node = JsonNode.Parse(json)!.AsObject();
            mutate(node);
            json = node.ToJsonString();
        }
        File.WriteAllText(Analysis("findings.json"), json);
    }

    /// <summary>Removes SemanticType from every finding in findings.json, like a file written before the finding contract.</summary>
    public void WriteLegacyFindings() => WriteFindings(root =>
    {
        foreach (var f in root["Findings"]!.AsArray()) f!.AsObject().Remove("SemanticType");
    });

    public void WriteGraph()
    {
        var (json, _) = EvidenceGraph.Build(Events, Findings, "AUDITED").Snapshot("CASE-V");
        File.WriteAllText(Analysis("graph.json"), json);
    }

    public void WriteDependencies() => Ws.WriteDependencies(Findings, []);

    /// <summary>parsing.json as the pipeline writes it (default serializer: enum as number; 0 = SUCCESS).</summary>
    public void WriteParsing(params string[] evidenceKeys)
    {
        var rows = evidenceKeys.Select(k => new ParseResult { EvidenceId = Ev[k].EvidenceId, Parser = "p-" + k, ParserVersion = "1", Status = EvidenceStatus.Success, Records = 1 }).ToList();
        File.WriteAllText(Analysis("parsing.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Everything a normal run leaves behind (timeline, findings, graph, dependencies, parsing for every evidence key).</summary>
    public void WriteAll()
    {
        WriteTimeline(); WriteFindings(); WriteGraph(); WriteDependencies(); WriteParsing([.. Ev.Keys]);
    }

    public VerificationReport Verify(VerificationOptions? o = null) => CaseVerifier.Verify(Ws, o ?? new VerificationOptions { Clock = () => T0 });

    /// <summary>A typical execution claim backed by Prefetch and BAM.</summary>
    public Finding ExecutionWithTwoKinds(string id = "F-0001")
    {
        if (!Ev.ContainsKey("pf")) Evidence("pf", "prefetch");
        if (!Ev.ContainsKey("bam")) Evidence("bam", "file");
        Event("Prefetch", "pf", "pf1", T0, path: @"C:\Users\u\AppData\x.exe", process: "X.EXE");
        Event("BAM", "bam", "bam1", T0.AddSeconds(1), path: @"C:\Users\u\AppData\x.exe");
        return Add(id, "EXEC-USERPATH", SemanticType.Execution, null, T0.AddSeconds(1), Ref("pf", "pf1"), Ref("bam", "bam1"));
    }

    public static CheckResult Check(FindingVerdict v, string id) => v.Checks.Single(c => c.CheckId == id);
}
