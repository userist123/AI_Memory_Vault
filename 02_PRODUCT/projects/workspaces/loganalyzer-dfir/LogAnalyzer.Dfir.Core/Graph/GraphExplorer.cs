using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Graph;

/// <summary>One edge seen from a chosen entity: which way it points, what it links to, and what supports it.</summary>
public sealed record GraphEdgeRow(
    string Direction, string Relation, string OtherId, string OtherType, string OtherLabel, string TimeUtc, string Classification,
    string Confidence, string Support, string Reason);

/// <summary>
/// Read-only queries the application shows over an Evidence Graph: find entities by text, list an entity's edges with their
/// evidence (EvidenceId + locator) or derivation, and the shortest path between two entities. The classification of an edge is shown with the Romanian
/// state label of <see cref="StateLabels.ForClassification"/> (Observat, Corelat, Deducție, Nedemonstrat): one mapping for the whole application.
/// </summary>
public static class GraphExplorer
{
    /// <summary>Entities whose key or label contains the text (case-insensitive), the most connected first.</summary>
    public static IReadOnlyList<Entity> Search(EvidenceGraph g, string text, int limit = 200)
    {
        var t = text.Trim();
        return g.Entities.Where(e => t.Length == 0 || e.Key.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Label.Contains(t, StringComparison.OrdinalIgnoreCase)
                                      || e.Type.Equals(t, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(e => g.Edges(e.Id).Count).ThenBy(e => e.Id, StringComparer.Ordinal).Take(limit).ToList();
    }

    public static IReadOnlyList<GraphEdgeRow> EdgesOf(EvidenceGraph g, string entityId)
    {
        var byId = g.Entities.ToDictionary(e => e.Id, StringComparer.Ordinal);
        return g.Edges(entityId).Select(r =>
        {
            bool outgoing = r.SourceEntity == entityId;
            var other = byId[outgoing ? r.TargetEntity : r.SourceEntity];
            var support = r.EvidenceId.Length > 0 ? $"{r.EvidenceId} · {r.Locator}" : "";
            if (r.Derivation.Length > 0) support += (support.Length > 0 ? " · " : "") + "derivat: " + r.Derivation;
            return new GraphEdgeRow(outgoing ? "→" : "←", r.TypeName, other.Id, other.Type, other.Label, r.Timestamp.UtcIso,
                StateLabels.ForClassification(r.Classification), r.Confidence.ToSpec(), support, r.Reason);
        }).OrderBy(x => x.TimeUtc.Length == 0).ThenBy(x => x.TimeUtc, StringComparer.Ordinal).ToList();
    }

    /// <summary>The shortest path between two entities, one row per edge in order; empty when they are not connected.</summary>
    public static IReadOnlyList<GraphEdgeRow> PathBetween(EvidenceGraph g, string fromId, string toId)
    {
        var rows = new List<GraphEdgeRow>();
        var at = fromId;
        foreach (var r in g.Path(fromId, toId))
        {
            var row = EdgesOf(g, at).First(x => x.Relation == r.TypeName && x.OtherId == (r.SourceEntity == at ? r.TargetEntity : r.SourceEntity)
                                                && x.Support.StartsWith(r.EvidenceId.Length > 0 ? r.EvidenceId : "derivat", StringComparison.Ordinal));
            rows.Add(row);
            at = row.OtherId;
        }
        return rows;
    }
}
