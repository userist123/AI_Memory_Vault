using System.Security.Principal;
using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Graph;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Parsers;

namespace LogAnalyzer.Dfir.Windows.Investigation;

public sealed record CollectionRow(string Collector, EvidenceStatus Status, int EvidenceCount, string Errors, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public sealed class InvestigationResult
{
    public required CaseWorkspace Case { get; init; }
    public List<CollectionRow> Collection { get; } = [];
    public List<ParseResult> Parsing { get; } = [];
    public List<TimelineEvent> Timeline { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<EvidenceGap> Gaps { get; } = [];
    /// <summary>Findings refused because they did not point to evidence in the case (kept for review, never reported as findings).</summary>
    public List<RejectedFinding> RejectedFindings { get; } = [];
    /// <summary>Entities and relationships built from the timeline and findings (Analysis/graph.json).</summary>
    public EvidenceGraph? Graph { get; set; }
    public string TimelineCsv { get; set; } = "";
    public string FindingsJson { get; set; } = "";
}

/// <summary>
/// The full investigation, as done by hand on LAB-PC: acquire (or import) evidence into a case, parse it with the
/// real parsers, build one timeline, correlate, record findings and evidence gaps. Every step leaves an audit trail.
/// </summary>
public sealed class InvestigationPipeline
{
    private readonly ParserRegistry _registry;

    public InvestigationPipeline(ParserRegistry? registry = null) => _registry = registry ?? WindowsParsers.Registry;

    public static IReadOnlyList<ICollector> AllCollectors { get; } =
        [new LiveStateCollector(), new EventLogCollector(), new PrefetchCollector(), new ExecutionArtifactsCollector(), new SrumCollector()];

    public static CaseWorkspace NewCase(string casesRoot, string name)
    {
        var id = $"CASE-{Environment.MachineName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        return CaseWorkspace.Create(Path.Combine(casesRoot, id), new CaseInfo
        {
            CaseId = id, Name = name, Host = Environment.MachineName, User = Environment.UserName, CreatedAtUtc = DateTimeOffset.UtcNow,
            Investigator = $"{Environment.UserDomainName}\\{Environment.UserName}", Os = Environment.OSVersion.VersionString,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(), Timezone = TimeZoneInfo.Local.Id,
            CollectionMode = "investigation",
        });
    }

    /// <summary>Imports existing evidence files (EVTX, .pf, SRUDB.dat, .pcapng) — copies, never moves.</summary>
    public static List<EvidenceItem> Import(CaseWorkspace ws, IEnumerable<string> files)
    {
        var list = new List<EvidenceItem>();
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            var type = ext switch
            {
                ".evtx" => "evtx", ".pf" => "prefetch", ".pcapng" => "pcapng", ".lnk" => "lnk",
                _ when f.EndsWith(".automaticDestinations-ms", StringComparison.OrdinalIgnoreCase) => "jumplist_auto",
                _ when Path.GetFileName(f).Equals("SRUDB.dat", StringComparison.OrdinalIgnoreCase) => "srum",
                _ when Path.GetFileName(f).Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
                       || Path.GetFileName(f).EndsWith("SYSTEM.hiv", StringComparison.OrdinalIgnoreCase) => "system_hive",
                _ when Path.GetFileName(f).Equals("Amcache.hve", StringComparison.OrdinalIgnoreCase) => "amcache",
                _ when EvidenceFingerprint.Detect(f) == "task_xml" => "task_xml",
                _ when Path.GetFileName(f).Equals("History", StringComparison.OrdinalIgnoreCase) && EvidenceFingerprint.Detect(f) == "sqlite" => "chromium_history",
                _ when Path.GetFileName(f).Equals("places.sqlite", StringComparison.OrdinalIgnoreCase) && EvidenceFingerprint.Detect(f) == "sqlite" => "firefox_places",
                _ when RegistryHiveType(Path.GetFileName(f)) is { } hiveType && EvidenceFingerprint.Detect(f) == "regf" => hiveType,
                _ => null,
            };
            if (type is null) continue;
            var source = type == "evtx" ? Path.GetFileNameWithoutExtension(f).Replace('%', '/') : type;
            list.Add(ws.ImportFile(f, "Import:" + source, type == "evtx" ? "EventLog:" + source : type, TemporalType.Historical, "Import", DfirInfo.ApplicationVersion,
                notes: "Importat de operator din: " + f));
        }
        return list;
    }

    /// <summary>Hive kind from the usual file names of saved or copied hives.</summary>
    public static string? RegistryHiveType(string name)
    {
        var n = name.ToUpperInvariant();
        if (n is "NTUSER.DAT" or "HKCU.HIV" || n.StartsWith("NTUSER", StringComparison.Ordinal) && n.EndsWith(".HIV", StringComparison.Ordinal)) return "ntuser_hive";
        if (n is "SOFTWARE" or "SOFTWARE.HIV" || n.EndsWith("_SOFTWARE.HIV", StringComparison.Ordinal)) return "software_hive";
        return null;
    }

    public InvestigationResult Run(CaseWorkspace ws, CollectionProfile profile, bool collect, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var r = new InvestigationResult { Case = ws };
        using var id = WindowsIdentity.GetCurrent();
        bool elevated = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        ws.Audit("investigation.start", $"profile={profile} collect={collect} elevated={elevated}");

        // 1. Acquisition.
        if (collect)
            foreach (var c in AllCollectors.Where(c => (c.Profiles & profile) != 0))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Colectare: {c.Name}");
                var start = DateTimeOffset.UtcNow;
                CollectorOutcome o;
                try { o = c.Collect(new CollectorContext { Case = ws, IsElevated = elevated }, ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                { o = new CollectorOutcome { Status = EvidenceStatus.Failed, Errors = { ex.Message } }; }
                var end = DateTimeOffset.UtcNow;
                r.Collection.Add(new CollectionRow(c.Name, o.Status, o.Evidence.Count, string.Join("; ", o.Errors), start, end));
                ws.RecordCollection(new CollectionAuditRecord(c.Name, start, end, o.Status, o.ExitCode, o.Source, ws.Root, o.Evidence.Sum(e => e.Size),
                    "", o.Tool, o.ToolVersion, string.Join(" && ", o.Commands), string.Join("; ", o.Errors)));
                if (o.Status is EvidenceStatus.NotAvailable or EvidenceStatus.Failed)
                    r.Gaps.Add(new EvidenceGap(c.Name, o.Status, string.Join("; ", o.Errors), "Sursa lipsește din analiză",
                        c.RequiresElevation ? "Rulare ca administrator" : "Import manual al probei", elevated ? "Posibil" : "Cu drepturi de administrator"));
                else if (o.Errors.Count > 0)
                    r.Gaps.Add(new EvidenceGap(c.Name, EvidenceStatus.Partial, string.Join("; ", o.Errors.Take(8)), "Unele surse lipsesc", "Rulare ca administrator / import", "Posibil"));
            }

        // 2. Parsing into one timeline.
        var analysisDir = Path.Combine(ws.Root, "Analysis");
        Directory.CreateDirectory(analysisDir);
        var sink = new ListSink();
        File.WriteAllText(Path.Combine(analysisDir, "parsers.json"), JsonSerializer.Serialize(_registry.Descriptors, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var ev in ws.LoadEvidence())
        {
            ct.ThrowIfCancellationRequested();
            var full = ws.FullPath(ev.StoredPath);
            var sel = _registry.Select(ev, full);
            if (!sel.HasCandidate)
            {
                // Live snapshots are read by LiveStateAnalyzer below; anything else is listed, never silently ignored.
                if (ev.SourceType != "live_snapshot")
                    r.Parsing.Add(new ParseResult
                    {
                        EvidenceId = ev.EvidenceId, Parser = "-", ParserVersion = "-", Status = EvidenceStatus.SkippedByDesign,
                        Error = sel.Problem, ExpectedSha256 = ev.Sha256,
                    });
                continue;
            }
            if (sel.Parsers.Count == 0)
            {
                var pre0 = sel.Preflight!;
                var refused = new ParseResult
                {
                    EvidenceId = ev.EvidenceId, Parser = string.Join("|", _registry.Candidates(ev).Select(p => p.Descriptor.ParserId)), ParserVersion = "-",
                    Status = EvidenceStatus.Failed, Error = sel.Problem, ExpectedSha256 = ev.Sha256, SourceSha256Before = pre0.Sha256, SourceFingerprint = pre0.Fingerprint,
                };
                r.Parsing.Add(refused);
                r.Gaps.Add(new EvidenceGap(Path.GetFileName(ev.StoredPath), EvidenceStatus.Failed, refused.Error,
                    "Proba nu a fost parsată: rezultatele ar putea să nu provină din sursa achiziționată", "Reachiziție din sursa originală", "Doar prin reachiziție"));
                ws.Audit(pre0.Code == "EVIDENCE_MUTATED" ? "evidence.mutated" : "evidence.preflight_failed", $"{ev.EvidenceId} {sel.Problem}");
                continue;
            }
            var pre = sel.Preflight!;
            foreach (var parser in sel.Parsers)
            {
                progress?.Report($"Parsare: {Path.GetFileName(ev.StoredPath)} ({parser.Descriptor.ParserId})");
                var mark = sink.Events.Count;
                var pr = parser.Parse(ev, full, sink, ct);
                pr.ExpectedSha256 = ev.Sha256;
                pr.SourceSha256Before = pre.Sha256;
                pr.SourceFingerprint = pre.Fingerprint;
                var post = parser.Preflight(ev, full);
                pr.SourceSha256After = post.Sha256;
                if (!post.CanParse)
                {
                    // The source changed while it was being read: nothing extracted from it can be attributed to the acquired original.
                    sink.Events.RemoveRange(mark, sink.Events.Count - mark);
                    pr.Status = EvidenceStatus.Failed;
                    pr.Error = $"{post.Code} în timpul parsării: {post.Detail}" + (pr.Error.Length > 0 ? $" | {pr.Error}" : "");
                    ws.Audit("evidence.mutated", $"{ev.EvidenceId} during parse {post.Detail}");
                }
                else
                    ProvenanceBinder.BindEvents(sink.Events.Skip(mark), ev, parser.Descriptor.ParserId, parser.Descriptor.Version);
                r.Parsing.Add(pr);
                ws.RecordTransformation(ev.EvidenceId, parser.Descriptor.ParserId, parser.Descriptor.Version, "Analysis/timeline.csv", $"{pr.Records} înregistrări, status {pr.Status.ToSpec()}");
                r.Gaps.AddRange(pr.Gaps);
                if (pr.Status is EvidenceStatus.Failed or EvidenceStatus.Partial)
                    r.Gaps.Add(new EvidenceGap(Path.GetFileName(ev.StoredPath), pr.Status, pr.Error.Length > 0 ? pr.Error : $"{pr.MalformedRecords} înregistrări corupte",
                        "Evenimente lipsă din cronologie", "Alt parser / copie a probei", "Posibil"));
            }
        }
        r.Timeline.AddRange(sink.Events.OrderBy(e => e.Time.Utc ?? DateTimeOffset.MaxValue));
        r.Gaps.AddRange(AuditCoverage.Gaps(r.Timeline));

        // 3. Correlation (timeline) + live state.
        progress?.Report("Corelare");
        var found = new List<Finding>(Correlation.Run(r.Timeline));
        foreach (var live in ws.LoadEvidence().Where(e => e.SourceType == "live_snapshot"))
        {
            var pre = EvidencePreflight.Check(live, ws.FullPath(live.StoredPath));
            if (!pre.CanParse)
            {
                r.Gaps.Add(new EvidenceGap(Path.GetFileName(live.StoredPath), EvidenceStatus.Failed, $"{pre.Code}: {pre.Detail}",
                    "Constatările din starea live lipsesc", "O nouă fotografie a stării live", "Doar prin reachiziție"));
                ws.Audit(pre.Code == "EVIDENCE_MUTATED" ? "evidence.mutated" : "evidence.preflight_failed", $"{live.EvidenceId} {pre.Code} {pre.Detail}");
                continue;
            }
            found.AddRange(LiveStateAnalyzer.Analyze(live, ws.FullPath(live.StoredPath), found.Count));
        }
        var (kept, rejected) = ProvenanceBinder.BindFindings(found, ws.LoadEvidence().ToDictionary(e => e.EvidenceId, StringComparer.Ordinal));
        r.Findings.AddRange(kept);
        r.RejectedFindings.AddRange(rejected);
        foreach (var x in rejected) ws.Audit("finding.rejected", $"{x.Finding.FindingId} {x.Finding.RuleId}: {x.Reason}");

        // 4. Outputs, kept in the case and hashed into custody.
        progress?.Report("Scriere rezultate");
        r.TimelineCsv = Path.Combine(analysisDir, "timeline.csv");
        using (var w = new CsvWriter(r.TimelineCsv, ["TimeUtc", "TimeSemantics", "Source", "EventId", "Provider", "Host", "User", "Process", "Pid", "Path", "RemoteIp", "RemotePort", "Dns", "Summary", "Classification", "EvidenceId", "Locator", "SourceSha256", "Parser", "ParserVersion"]))
            foreach (var e in r.Timeline)
                w.WriteRow(new object?[] { e.Time.Utc?.ToString("o") ?? "", e.TimeSemantics, e.Source, e.EventId, e.Provider, e.Host, e.User, e.Process, e.Pid, e.Path, e.RemoteIp, e.RemotePort, e.Dns, e.Summary, e.Classification.ToSpec(), e.EvidenceId, e.Locator, e.SourceSha256, e.ParserId, e.ParserVersion });
        r.FindingsJson = Path.Combine(analysisDir, "findings.json");
        File.WriteAllText(r.FindingsJson, JsonSerializer.Serialize(new { r.Findings, r.Gaps, r.Collection, RejectedFindings = r.RejectedFindings.Select(x => new { x.Finding.FindingId, x.Finding.RuleId, x.Finding.Title, x.Reason }) }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(analysisDir, "parsing.json"), JsonSerializer.Serialize(r.Parsing, new JsonSerializerOptions { WriteIndented = true }));
        progress?.Report("Graf de probe");
        r.Graph = EvidenceGraph.Build(r.Timeline, r.Findings, ws.Info.Host);
        var (graphJson, graphSha) = r.Graph.Snapshot(ws.Info.CaseId);
        File.WriteAllText(Path.Combine(analysisDir, "graph.json"), graphJson);
        ws.RecordTransformation("CASE", "EvidenceGraph", "1.0", "Analysis/graph.json", $"{r.Graph.Entities.Count} entități, {r.Graph.Relationships.Count} relații, SHA-256 {graphSha}");
        ws.Audit("investigation.end", $"{r.Timeline.Count} events, {r.Findings.Count} findings, {r.Gaps.Count} gaps");
        return r;
    }
}

/// <summary>Findings from the live-state snapshot: what is running or set to start from user-writable locations, and unsigned.</summary>
public static class LiveStateAnalyzer
{
    public static IEnumerable<Finding> Analyze(EvidenceItem item, string path, int offset)
    {
        var snap = JsonSerializer.Deserialize<LiveStateCollector.Snapshot>(File.ReadAllText(path));
        if (snap is null) yield break;
        int n = offset;
        string Id() => $"F-{++n:D4}";
        var internet = snap.Connections.Where(c => c.State != "LISTEN" && IpClassifier.IsExternal(c.Remote.Address.ToString())).ToLookup(c => c.Pid);
        foreach (var p in snap.Processes.Where(p => p.UserWritable && p.Signature.StartsWith("NESEMNAT", StringComparison.Ordinal)))
        {
            var conns = internet[p.Pid].Select(c => $"{c.Remote.Address}:{c.Remote.Port}").Distinct().ToList();
            yield return new Finding
            {
                FindingId = Id(), RuleId = conns.Count > 0 ? "LIVE-UNSIGNED-USERPATH-NET" : "LIVE-UNSIGNED-USERPATH",
                Title = $"Proces nesemnat din locație scriabilă{(conns.Count > 0 ? ", cu conexiuni în Internet" : "")}: {p.Name}",
                Severity = conns.Count > 0 ? Severity.High : Severity.Medium, Category = "Execution", Classification = Classification.Direct,
                Confidence = conns.Count > 0 ? Confidence.High : Confidence.Medium, Process = p.Name, Pid = p.Pid, File = p.Path,
                Ip = string.Join(", ", conns.Take(10)), FirstSeenUtc = snap.TakenUtc,
                Description = $"PID {p.Pid}, părinte {p.Ppid}; linie de comandă: {p.CommandLine}" + (conns.Count > 0 ? $"; conexiuni: {string.Join(", ", conns.Take(10))}" : ""),
                ClassificationReason = "Observat în fotografia stării live (semnătură verificată, tabela TCP).",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Processes[pid={p.Pid}]", "fotografie live")],
                AlternativeExplanations = ["Aplicație legitimă instalată per utilizator, fără semnătură."],
                RecommendedNextSteps = ["Izolați programul din fila „Izolare procese suspecte” pentru scanare completă."],
            };
        }
        foreach (var s in snap.Services.Where(s => s.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-SERVICE-USERPATH", Title = $"Serviciu configurat dintr-o locație scriabilă: {s.Name}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1543.003",
                File = s.Path, User = s.Account, FirstSeenUtc = snap.TakenUtc,
                Description = $"{s.DisplayName}: {s.Path} ({s.StartMode}, {s.State}, cont {s.Account}).",
                ClassificationReason = "Configurația curentă a serviciilor.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Services[{s.Name}]", "fotografie live")],
            };
        foreach (var t in snap.Tasks.Where(t => t.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-TASK-USERPATH", Title = $"Task programat care rulează din locație scriabilă: {Path.GetFileName(t.Path)}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1053.005",
                File = t.Command, FirstSeenUtc = snap.TakenUtc, Description = $"{t.Path}: {t.Command}",
                ClassificationReason = "Definiția curentă a taskului.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Tasks[{t.Path}]", "fotografie live")],
            };
        foreach (var a in snap.Autoruns.Where(a => a.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-AUTORUN-USERPATH", Title = $"Pornire automată din locație scriabilă: {a.Name}",
                Severity = Severity.Medium, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1547.001",
                File = a.Value, FirstSeenUtc = snap.TakenUtc, Description = $"{a.Location}\\{a.Name} = {a.Value}",
                ClassificationReason = "Cheile Run curente.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Autoruns[{a.Name}]", "fotografie live")],
                AlternativeExplanations = ["Multe aplicații per utilizator (actualizatoare, sincronizare) pornesc legitim din AppData."],
            };
    }
}
