using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// Analysis/dependencies.json (WP3b): for every finding and every Vault proposal, the evidence ids it rests on.
/// Built from SupportingEvidence/EvidenceRef (and the provenance), plus the findings a chain finding is built from, and expanded through
/// <see cref="EvidenceItem.ParentEvidenceId"/> so that evidence derived from another item also depends on its source.
/// </summary>
public sealed class DependencyIndex
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = SchemaVersions.Dependencies;
    public string CaseId { get; set; } = "";
    public SortedDictionary<string, List<string>> Findings { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, List<string>> VaultProposals { get; set; } = new(StringComparer.Ordinal);

    public static DependencyIndex Build(string caseId, IReadOnlyList<EvidenceItem> evidence, IReadOnlyList<Finding> findings, IReadOnlyList<VaultProposal> proposals)
    {
        var parent = evidence.Where(e => !string.IsNullOrEmpty(e.ParentEvidenceId)).GroupBy(e => e.EvidenceId, StringComparer.Ordinal)
                             .ToDictionary(g => g.Key, g => g.First().ParentEvidenceId!, StringComparer.Ordinal);
        var byId = findings.GroupBy(f => f.FindingId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var idx = new DependencyIndex { CaseId = caseId };

        HashSet<string> Direct(Finding f, HashSet<string> seen)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!seen.Add(f.FindingId)) return set;
            foreach (var r in f.SupportingEvidence) set.Add(r.EvidenceId);
            if (f.Provenance is { } p) foreach (var e in p.EvidenceIds) set.Add(e);
            foreach (var rel in f.RelatedFindingIds)
                if (byId.TryGetValue(rel, out var rf)) set.UnionWith(Direct(rf, seen));
            return set;
        }

        List<string> Expand(IEnumerable<string> ids)
        {
            var all = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids.Where(i => i.Length > 0))
            {
                var cur = id;
                while (cur is not null && all.Add(cur)) cur = parent.GetValueOrDefault(cur);
            }
            return all.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        foreach (var f in findings) idx.Findings[f.FindingId] = Expand(Direct(f, new HashSet<string>(StringComparer.Ordinal)));
        foreach (var p in proposals) idx.VaultProposals[VaultExport.IdOf(p)] = Expand(VaultExport.EvidenceIdsOf(p));
        return idx;
    }

    public void Write(string path) => Json.Write(path, this);

    public static DependencyIndex? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var idx = Json.Read<DependencyIndex>(path);
        idx.SchemaVersion = SchemaVersions.Accept(idx.SchemaVersion, Path.GetFileName(path));
        return idx;
    }

    public IEnumerable<string> AllEvidenceIds() => Findings.Values.Concat(VaultProposals.Values).SelectMany(x => x).Distinct(StringComparer.Ordinal);
}

/// <summary>One finding or Vault proposal marked INVALIDATED because evidence it rests on is MODIFIED or MISSING. findings.json is never rewritten.</summary>
public sealed record Invalidation(string Kind, string Id, string State, string Reason, IReadOnlyList<string> EvidenceIds)
{
    public const string Invalidated = "INVALIDATED";
}

/// <summary>Analysis/invalidations.json.</summary>
public sealed class InvalidationsFile
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = SchemaVersions.Invalidations;
    public string CaseId { get; set; } = "";
    public DateTimeOffset GeneratedUtc { get; set; }
    /// <summary>Where the dependencies came from: "dependencies.json", "derivat din findings.json (caz fără dependencies.json)" or "indisponibil".</summary>
    public string DependenciesSource { get; set; } = "";
    public List<Invalidation> Items { get; set; } = [];
}
