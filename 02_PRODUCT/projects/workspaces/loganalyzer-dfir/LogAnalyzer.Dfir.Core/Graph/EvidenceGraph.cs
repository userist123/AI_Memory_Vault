using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Graph;

/// <summary>Relationship types of master spec §7, plus three explicit extensions (DOWNLOADED, OPENED, DETECTED).</summary>
public enum RelationType
{
    Executed, Spawned, ConnectedTo, Resolved, Authenticated, Created, Modified, Deleted, Persisted, Loaded, Installed, LoggedOn,
    MemberOf, AuthorizedBy, Violates, Satisfies, Supports, Contradicts, DerivedFrom, PartOf, DependsOn,
    Downloaded, Opened, Detected,
}

public sealed record Entity(string Id, string Type, string Key, string Label);

/// <summary>
/// One edge (spec §7). It carries either evidence (EvidenceId + Locator of the record that shows it) or an explicit
/// derivation (the finding/rule that inferred it) — never neither.
/// </summary>
public sealed record Relationship
{
    public string RelationshipId { get; }
    public string SourceEntity { get; }
    public string TargetEntity { get; }
    public RelationType Type { get; }
    public Timestamp Timestamp { get; }
    public string EvidenceId { get; }
    public string Locator { get; }
    public string Derivation { get; }
    public Classification Classification { get; }
    public Confidence Confidence { get; }
    public string Reason { get; }

    public Relationship(string id, string source, string target, RelationType type, Timestamp time, string evidenceId, string locator,
                        string derivation, Classification classification, Confidence confidence, string reason)
    {
        bool evidenced = evidenceId.Length > 0 && locator.Length > 0;
        if (!evidenced && derivation.Length == 0)
            throw new ArgumentException($"Relația {type} {source} → {target} nu are nici probă (EvidenceId + Locator), nici derivare explicită.");
        if (source.Length == 0 || target.Length == 0) throw new ArgumentException("Relație fără capăt.");
        (RelationshipId, SourceEntity, TargetEntity, Type, Timestamp, EvidenceId, Locator, Derivation, Classification, Confidence, Reason) =
            (id, source, target, type, time, evidenceId, locator, derivation, classification, confidence, reason);
    }

    public string TypeName => string.Concat(Type.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant();
}

/// <summary>
/// Entities and typed relationships built only from timeline events (direct, with their evidence) and findings (derived,
/// with the finding as derivation). A link the evidence does not show is not drawn.
/// </summary>
public sealed class EvidenceGraph
{
    private readonly Dictionary<string, Entity> _entities = new(StringComparer.Ordinal);
    private readonly List<Relationship> _relationships = [];
    private readonly Dictionary<string, List<Relationship>> _byEntity = new(StringComparer.Ordinal);

    public IReadOnlyCollection<Entity> Entities => _entities.Values;
    public IReadOnlyList<Relationship> Relationships => _relationships;

    public static string FileId(string path) => "File:" + Correlation.Normalize(path.Trim().Trim('"'));
    public static string DomainId(string name) => "Domain:" + name.Trim().TrimEnd('.').ToLowerInvariant();

    public Entity Add(string type, string key, string label = "")
    {
        var id = type switch
        {
            "File" => FileId(key),
            "Domain" => DomainId(key),
            _ => $"{type}:{key.Trim().ToLowerInvariant()}",
        };
        if (!_entities.TryGetValue(id, out var e)) _entities[id] = e = new Entity(id, type, key, label.Length > 0 ? label : key);
        return e;
    }

    public Relationship Link(Entity from, RelationType type, Entity to, TimelineEvent? evidence, string derivation = "",
                             Classification classification = Classification.Direct, Confidence confidence = Confidence.High, string reason = "")
    {
        var r = new Relationship($"R-{_relationships.Count + 1:D6}", from.Id, to.Id, type, evidence?.Time ?? Timestamp.Unknown(),
            evidence?.EvidenceId ?? "", evidence?.Locator ?? "", derivation, classification, confidence, reason);
        _relationships.Add(r);
        foreach (var id in new[] { r.SourceEntity, r.TargetEntity })
        {
            if (!_byEntity.TryGetValue(id, out var l)) _byEntity[id] = l = [];
            l.Add(r);
        }
        return r;
    }

    // ------------------------------------------------------------------ build

    private static readonly string[] CodeExtensions = [".exe", ".dll", ".cmd", ".bat", ".ps1", ".vbs", ".js", ".hta", ".msi", ".scr"];

    public static EvidenceGraph Build(IReadOnlyList<TimelineEvent> events, IReadOnlyList<Finding> findings, string hostName)
    {
        var g = new EvidenceGraph();
        var host = g.Add("Host", hostName);
        // Entities an event produced, so findings can be attached to what their evidence shows.
        var produced = new Dictionary<(string, string), (TimelineEvent Event, List<Entity> Entities)>();
        void Note(TimelineEvent e, Entity x)
        {
            var k = (e.EvidenceId, e.Locator);
            if (!produced.TryGetValue(k, out var v)) produced[k] = v = (e, []);
            if (!v.Entities.Contains(x)) v.Entities.Add(x);
        }
        string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";

        foreach (var e in events)
        {
            switch (e.Source)
            {
                case "Prefetch":
                {
                    var refs = F(e, "ReferencedFiles").Split('|', StringSplitOptions.RemoveEmptyEntries);
                    var exePath = refs.FirstOrDefault(r => r.EndsWith("\\" + e.Process, StringComparison.OrdinalIgnoreCase)) ?? e.Path;
                    if (exePath.Length == 0) break;
                    var exe = g.Add("File", exePath);
                    g.Link(host, RelationType.Executed, exe, e, reason: "Prefetch: rulare înregistrată");
                    Note(e, exe);
                    foreach (var r in refs.Where(r => !r.Equals(exePath, StringComparison.OrdinalIgnoreCase) && Correlation.IsUserWritable(r)
                                                      && CodeExtensions.Any(x => r.EndsWith(x, StringComparison.OrdinalIgnoreCase))))
                    {
                        var file = g.Add("File", r);
                        g.Link(exe, RelationType.Loaded, file, e, reason: "fișier referit în Prefetch-ul programului (accesat în primele secunde)");
                        Note(e, file);
                    }
                    break;
                }
                case "BAM":
                case "UserAssist":
                {
                    if (e.Path.Length == 0) break;
                    var actor = e.User.Length > 0 ? g.Add("User", e.User) : host;
                    var file = g.Add("File", e.Path);
                    g.Link(actor, RelationType.Executed, file, e, reason: e.Source == "BAM" ? "BAM: ultima rulare" : "UserAssist: pornit din interfață");
                    Note(e, file);
                    break;
                }
                case "BrowserDownload":
                {
                    if (e.Path.Length == 0) break;
                    var file = g.Add("File", e.Path);
                    // The file came from the last URL of the chain (e.Dns); the page the user was on (tab) may be another site.
                    var origin = e.Dns.Length > 0 ? g.Add("Domain", e.Dns) : host;
                    g.Link(origin, RelationType.Downloaded, file, e, reason: "domeniul URL-ului final al descărcării");
                    if (Uri.TryCreate(F(e, "TabUrl"), UriKind.Absolute, out var tab) && tab.Host.Length > 0 && DomainId(tab.Host) != origin.Id)
                        g.Link(g.Add("Domain", tab.Host), RelationType.Downloaded, file, e, reason: "pagina (tab-ul) din care a pornit descărcarea");
                    Note(e, file);
                    break;
                }
                case "Service":
                {
                    var svc = g.Add("Service", e.Service.Length > 0 ? e.Service : e.Locator[(e.Locator.LastIndexOf('\\') + 1)..]);
                    foreach (var p in new[] { e.Path, F(e, "ServiceDll") }.Where(p => p.Length > 0))
                    {
                        var file = g.Add("File", p);
                        g.Link(file, RelationType.Persisted, svc, e, reason: "binarul / DLL-ul unui serviciu configurat");
                        Note(e, file);
                    }
                    Note(e, svc);
                    break;
                }
                case "ScheduledTask":
                {
                    var task = g.Add("Task", e.Task);
                    if (e.Path.Length > 0)
                    {
                        var file = g.Add("File", e.Path);
                        g.Link(file, RelationType.Persisted, task, e, reason: "comanda unui task programat");
                        Note(e, file);
                    }
                    Note(e, task);
                    break;
                }
                case "RunKey":
                case "Winlogon":
                case "IFEO":
                {
                    if (e.Path.Length == 0) break;
                    var key = g.Add("RegistryKey", e.Locator);
                    var file = g.Add("File", e.Path);
                    g.Link(file, RelationType.Persisted, key, e, reason: $"pornire automată ({e.Source})");
                    Note(e, file);
                    Note(e, key);
                    break;
                }
                case "USB":
                {
                    var dev = g.Add("Device", F(e, "Serial"), $"{F(e, "Vendor")} {F(e, "Product")} {F(e, "Serial")}".Trim());
                    if (e.TimeSemantics.Contains("first install")) g.Link(dev, RelationType.Installed, host, e, reason: "prima instalare a dispozitivului");
                    else if (e.TimeSemantics.Contains("arrival")) g.Link(dev, RelationType.ConnectedTo, host, e, reason: "ultima conectare");
                    Note(e, dev);
                    break;
                }
                case "LNK":
                case "JumpList":
                {
                    if (e.Path.Length == 0 || !e.Path.Contains('\\')) break;
                    var file = g.Add("File", e.Path);
                    g.Link(host, RelationType.Opened, file, e, reason: e.Source == "LNK" ? "shortcut din Recent" : "element din Jump List");
                    Note(e, file);
                    break;
                }
                case "PCAP:dns" when F(e, "Kind") == "response" && e.Dns.Length > 0:
                {
                    var domain = g.Add("Domain", e.Dns);
                    foreach (var a in F(e, "Answers").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(a => IPAddress.TryParse(a, out _)))
                    {
                        var ip = g.Add("IpAddress", a);
                        g.Link(domain, RelationType.Resolved, ip, e, reason: "răspuns DNS în captură");
                        Note(e, ip);
                    }
                    Note(e, domain);
                    break;
                }
                case "PCAP:flow":
                {
                    var client = g.Add("IpAddress", F(e, "ClientIP"));
                    var server = g.Add("IpAddress", F(e, "ServerIP"));
                    g.Link(client, RelationType.ConnectedTo, server, e, reason: $"flux {F(e, "Protocol")} spre portul {F(e, "ServerPort")}");
                    Note(e, server);
                    break;
                }
                default:
                    if (e.Source == "EventLog:Microsoft-Windows-Windows Defender/Operational" && e.EventId is "1116" or "1117" && F(e, "Threat Name").Length > 0)
                    {
                        var threat = g.Add("Threat", F(e, "Threat Name"));
                        var target = Correlation.DefenderContainer(F(e, "Path"));
                        if (target.Length > 0)
                        {
                            var file = g.Add("File", target);
                            g.Link(threat, RelationType.Detected, file, e, reason: $"Defender {e.EventId}");
                            Note(e, file);
                        }
                        Note(e, threat);
                    }
                    break;
            }
        }

        // Findings: what their evidence produced SUPPORTS them; chains contain their steps; a download tied to a run derives it.
        var findingEntity = findings.ToDictionary(f => f.FindingId, f => g.Add("Finding", f.FindingId, $"{f.RuleId}: {f.Title}"));
        foreach (var f in findings)
        {
            var fe = findingEntity[f.FindingId];
            foreach (var r in f.SupportingEvidence)
                if (produced.TryGetValue((r.EvidenceId, r.Locator), out var p))
                    foreach (var x in p.Entities)
                        g.Link(x, RelationType.Supports, fe, p.Event, reason: r.Description);
            foreach (var part in f.RelatedFindingIds.Where(findingEntity.ContainsKey))
                g.Link(findingEntity[part], RelationType.PartOf, fe, null, derivation: $"{f.RuleId} {f.FindingId}",
                       classification: Classification.Correlated, confidence: f.Confidence, reason: "pas al lanțului de incident (apropiere în timp)");
            if (f.RuleId == "DOWNLOAD-THEN-EXEC" && f.File.Length > 0)
            {
                var download = g.Add("File", f.File);
                // The program that ran (first file its evidence produced), not the files it loaded.
                foreach (var r in f.SupportingEvidence.Skip(1))
                    if (produced.TryGetValue((r.EvidenceId, r.Locator), out var p) && p.Entities.FirstOrDefault(x => x.Type == "File") is { } program && program.Id != download.Id)
                        g.Link(program, RelationType.DerivedFrom, download, null, derivation: $"{f.RuleId} {f.FindingId}",
                               classification: Classification.Correlated, confidence: f.Confidence, reason: f.ClassificationReason);
            }
        }
        return g;
    }

    // ------------------------------------------------------------------ query

    public IReadOnlyList<Relationship> Edges(string entityId) => _byEntity.GetValueOrDefault(entityId) ?? [];

    public IReadOnlyList<Entity> Neighbors(string entityId) =>
        Edges(entityId).Select(r => r.SourceEntity == entityId ? r.TargetEntity : r.SourceEntity).Distinct().Select(id => _entities[id]).ToList();

    /// <summary>Shortest path ignoring direction (breadth-first); empty when unconnected.</summary>
    public IReadOnlyList<Relationship> Path(string from, string to)
    {
        if (!_entities.ContainsKey(from) || !_entities.ContainsKey(to)) return [];
        var prev = new Dictionary<string, Relationship?>(StringComparer.Ordinal) { [from] = null };
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == to) break;
            foreach (var r in Edges(cur))
            {
                var next = r.SourceEntity == cur ? r.TargetEntity : r.SourceEntity;
                if (prev.ContainsKey(next)) continue;
                prev[next] = r;
                queue.Enqueue(next);
            }
        }
        if (!prev.ContainsKey(to)) return [];
        var path = new List<Relationship>();
        for (var at = to; prev[at] is { } r; at = r.SourceEntity == at ? r.TargetEntity : r.SourceEntity) path.Add(r);
        path.Reverse();
        return path;
    }

    public Entity? Find(string type, string contains) =>
        _entities.Values.FirstOrDefault(e => e.Type == type && e.Key.Contains(contains, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ snapshot

    /// <summary>JSON snapshot (entities, relationships with evidence or derivation) and its SHA-256.</summary>
    public (string Json, string Sha256) Snapshot(string caseId)
    {
        var json = JsonSerializer.Serialize(new
        {
            CaseId = caseId,
            Entities = _entities.Values.OrderBy(e => e.Id, StringComparer.Ordinal),
            Relationships = _relationships.Select(r => new
            {
                r.RelationshipId, r.SourceEntity, r.TargetEntity, Type = r.TypeName, TimeUtc = r.Timestamp.UtcIso, r.EvidenceId, r.Locator,
                r.Derivation, Classification = r.Classification.ToSpec(), Confidence = r.Confidence.ToSpec(), r.Reason,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
        return (json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
    }
}
