using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.AI;

/// <summary>
/// One record of THIS case that an AI statement may cite: an evidence item, a finding, a timeline event, an anti-forensics check or an evidence gap.
/// Everything the validator compares against is taken from the case data (never from the AI text): the free-text blob, the times, the hashes and the
/// sets used to recompute counts.
/// </summary>
public sealed class AiCaseRecord
{
    public required string Id { get; init; }
    /// <summary>evidence | finding | event | anti-forensics | gap</summary>
    public required string Kind { get; init; }
    public string CaseId { get; init; } = "";
    public IReadOnlyList<DateTimeOffset> Times { get; init; } = [];
    public IReadOnlyList<string> Hashes { get; init; } = [];
    public IReadOnlyCollection<string> EventKeys { get; init; } = [];
    public IReadOnlyCollection<string> EvidenceIds { get; init; } = [];
    public IReadOnlyCollection<string> FindingIds { get; init; } = [];
    /// <summary>Number of evidence pointers (a finding citing three locators of one file has three).</summary>
    public int EvidenceRefCount { get; init; }
    public required Func<string> BlobFactory { get; init; }

    private string? _blob, _norm;
    /// <summary>All text of the record, one field per line.</summary>
    public string Blob => _blob ??= BlobFactory();
    /// <summary>The blob with '/' read as '\' (paths compare independent of slash direction).</summary>
    public string BlobNormalized => _norm ??= Blob.Replace('/', '\\');
}

/// <summary>What the validator needs from a case. Implemented over the real case by <see cref="AiCaseFacts"/>; tests may supply their own.</summary>
public interface IAiCaseFacts
{
    string CaseId { get; }
    /// <summary>SHA-256 over the case id and every evidence item's id, SHA-256 and size: the AI text and its validation are bound to it.</summary>
    string EvidenceBindingSha256 { get; }
    /// <summary>The record with that id in THIS case, or null.</summary>
    AiCaseRecord? Find(string id);
}

/// <summary>The case's records, built from the case data. Catalog aliases (the model cites E3, the case knows EV-000001#rec 5) are explicit and refuse a clash.</summary>
public sealed class AiCaseFacts : IAiCaseFacts
{
    private readonly Dictionary<string, AiCaseRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    public string CaseId { get; }
    public string EvidenceBindingSha256 { get; }
    public int Count => _records.Count;
    public IEnumerable<string> Ids => _records.Keys;

    public AiCaseFacts(string caseId, string evidenceBindingSha256) { CaseId = caseId; EvidenceBindingSha256 = evidenceBindingSha256; }

    public AiCaseRecord? Find(string id)
    {
        if (_records.TryGetValue(id, out var r)) return r;
        return _aliases.TryGetValue(id, out var target) && _records.TryGetValue(target, out var t) ? t : null;
    }

    public void Add(AiCaseRecord r) { _records.TryAdd(r.Id, r); }

    public void Alias(string alias, string targetId)
    {
        if (_records.ContainsKey(alias)) throw new InvalidOperationException($"Aliasul {alias} există deja ca element al cazului.");
        if (!_records.ContainsKey(targetId)) throw new InvalidOperationException($"Aliasul {alias} arată spre un element inexistent: {targetId}.");
        _aliases[alias] = targetId;
    }

    public static string EventKey(string evidenceId, string locator) => evidenceId + "#" + locator;

    public static string Binding(string caseId, IEnumerable<EvidenceItem> evidence)
    {
        var sb = new StringBuilder("case=").Append(caseId).Append('\n');
        foreach (var e in evidence.OrderBy(e => e.EvidenceId, StringComparer.Ordinal))
            sb.Append(e.EvidenceId).Append('|').Append(e.Sha256).Append('|').Append(e.Size).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static AiCaseFacts Build(string caseId, IReadOnlyList<EvidenceItem> evidence, IReadOnlyList<Finding> findings, IReadOnlyList<TimelineEvent> timeline,
                                    IReadOnlyList<AntiForensicCheck> antiForensics, IReadOnlyList<EvidenceGap> gaps)
    {
        var facts = new AiCaseFacts(caseId, Binding(caseId, evidence));
        static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);
        static IEnumerable<string> IsoForms(DateTimeOffset t)
        {
            var u = t.UtcDateTime;
            yield return u.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
            yield return u.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            yield return u.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            yield return Iso(t);
        }

        var eventsByEvidence = timeline.GroupBy(e => e.EvidenceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(e => EventKey(e.EvidenceId, e.Locator)).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var findingsByEvidence = findings.SelectMany(f => f.SupportingEvidence.Select(r => (r.EvidenceId, f.FindingId)))
            .GroupBy(x => x.EvidenceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(x => x.FindingId).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var eventsByKey = timeline.GroupBy(e => EventKey(e.EvidenceId, e.Locator), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var e in evidence)
        {
            var events = eventsByEvidence.TryGetValue(e.EvidenceId, out var ek) ? ek : [];
            var evEvents = timeline.Where(x => x.EvidenceId == e.EvidenceId);
            facts.Add(new AiCaseRecord
            {
                Id = e.EvidenceId, Kind = "evidence", CaseId = e.CaseId,
                Times = new[] { e.AcquiredAtUtc }.Concat(evEvents.Where(x => x.Time.Utc is not null).Select(x => x.Time.Utc!.Value)).ToList(),
                Hashes = new[] { e.Sha256 }.Concat(evEvents.Select(x => x.Hash)).Concat(evEvents.Select(x => x.SourceSha256)).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                EventKeys = events, EvidenceIds = [e.EvidenceId], FindingIds = findingsByEvidence.TryGetValue(e.EvidenceId, out var fe) ? fe : [], EvidenceRefCount = 1,
                BlobFactory = () => string.Join("\n", new[]
                {
                    e.EvidenceId, e.Source, e.SourceType, e.OriginalPath, e.StoredPath, e.OriginalName, e.Size.ToString(), e.Sha256, e.Host, e.User, e.Collector, e.Notes,
                    string.Join(" ", IsoForms(e.AcquiredAtUtc)),
                }.Concat(evEvents.Select(EventText))),
            });
        }

        foreach (var f in findings)
        {
            var refs = f.SupportingEvidence;
            var keys = refs.Select(r => EventKey(r.EvidenceId, r.Locator)).Where(eventsByKey.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
            var times = new List<DateTimeOffset>();
            if (f.FirstSeenUtc is { } a) times.Add(a);
            if (f.LastSeenUtc is { } b) times.Add(b);
            facts.Add(new AiCaseRecord
            {
                Id = f.FindingId, Kind = "finding", CaseId = caseId, Times = times,
                Hashes = refs.Select(r => r.Sha256).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                EventKeys = keys, EvidenceIds = refs.Select(r => r.EvidenceId).Distinct(StringComparer.Ordinal).ToList(),
                FindingIds = new[] { f.FindingId }.Concat(f.RelatedFindingIds).Distinct(StringComparer.Ordinal).ToList(), EvidenceRefCount = refs.Count,
                BlobFactory = () => string.Join("\n", new[]
                {
                    f.FindingId, f.RuleId, f.Title, f.Description, f.Category, f.Host, f.User, f.Process, f.Pid?.ToString() ?? "", f.File, f.Ip, f.Domain, f.MitreTechniqueId,
                    string.Join(" ", times.SelectMany(IsoForms)),
                }.Concat(refs.Select(r => $"{r.EvidenceId} {r.Locator} {r.Description} {r.Sha256}")).Concat(f.RelatedFindingIds)),
            });
        }

        foreach (var ev in eventsByKey.Values)
        {
            var key = EventKey(ev.EvidenceId, ev.Locator);
            facts.Add(new AiCaseRecord
            {
                Id = key, Kind = "event", CaseId = caseId,
                Times = ev.Time.Utc is { } t ? [t] : [],
                Hashes = new[] { ev.Hash, ev.SourceSha256 }.Where(h => h.Length > 0).ToList(),
                EventKeys = [key], EvidenceIds = [ev.EvidenceId], FindingIds = [], EvidenceRefCount = 1,
                BlobFactory = () => EventText(ev) + (ev.Time.Utc is { } tt ? "\n" + string.Join(" ", IsoForms(tt)) : ""),
            });
        }

        foreach (var a in antiForensics)
            facts.Add(new AiCaseRecord
            {
                Id = a.Id, Kind = "anti-forensics", CaseId = caseId,
                Hashes = a.Evidence.Select(r => r.Sha256).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                EventKeys = a.Evidence.Select(r => EventKey(r.EvidenceId, r.Locator)).Where(eventsByKey.ContainsKey).Distinct(StringComparer.Ordinal).ToList(),
                EvidenceIds = a.Evidence.Select(r => r.EvidenceId).Distinct(StringComparer.Ordinal).ToList(), EvidenceRefCount = a.Evidence.Count,
                BlobFactory = () => string.Join("\n", new[] { a.Id, a.Technique, a.Attack, a.ResultText, a.Reason, a.Detail }.Concat(a.Evidence.Select(r => $"{r.EvidenceId} {r.Locator} {r.Description} {r.Sha256}"))),
            });

        int g = 0;
        foreach (var gap in gaps)
            facts.Add(new AiCaseRecord
            {
                Id = "G" + ++g, Kind = "gap", CaseId = caseId,
                BlobFactory = () => string.Join("\n", gap.Artifact, gap.Status.ToSpec(), gap.Reason, gap.Impact, gap.AlternativeSource, gap.Recoverability, gap.Notes),
            });
        return facts;
    }

    private static string EventText(TimelineEvent e) =>
        string.Join("\n", new[]
        {
            e.EvidenceId, e.Source, e.EventId, e.Provider, e.Host, e.User, e.Process, e.Pid?.ToString() ?? "", e.Path, e.RemoteIp, e.RemotePort?.ToString() ?? "", e.Dns,
            e.Task, e.Service, e.Hash, e.Summary, e.TimeSemantics, e.Locator, e.SourceSha256, e.Time.UtcIso, e.Time.Raw,
        }.Concat(e.Fields.Select(kv => $"{kv.Key}={kv.Value}")));
}
