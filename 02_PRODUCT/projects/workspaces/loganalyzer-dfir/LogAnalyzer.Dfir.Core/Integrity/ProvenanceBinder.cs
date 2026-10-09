using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Integrity;

public sealed record RejectedFinding(Finding Finding, string Reason);

/// <summary>
/// Binds the source hash (and parser identity) to everything derived from a source, and refuses findings that do not
/// point to evidence in the case. A finding is emitted only if every reference resolves to an acquired item.
/// </summary>
public static class ProvenanceBinder
{
    /// <summary>Stamps events produced by one parser run over one evidence item.</summary>
    public static void BindEvents(IEnumerable<TimelineEvent> events, EvidenceItem source, string parserId, string parserVersion)
    {
        foreach (var e in events)
        {
            if (!e.EvidenceId.Equals(source.EvidenceId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Parserul {parserId} a produs un eveniment pentru {e.EvidenceId} în timp ce citea {source.EvidenceId}.");
            e.SourceSha256 = source.Sha256;
            e.ParserId = parserId;
            e.ParserVersion = parserVersion;
            TimeFacts.Annotate(e);
        }
    }

    public static (List<Finding> Kept, List<RejectedFinding> Rejected) BindFindings(
        IEnumerable<Finding> findings, IReadOnlyDictionary<string, EvidenceItem> evidence)
    {
        var kept = new List<Finding>();
        var rejected = new List<RejectedFinding>();
        foreach (var f in findings)
        {
            if (f.SupportingEvidence.Count == 0)
            {
                rejected.Add(new(f, "constatare fără probă de susținere"));
                continue;
            }
            var unknown = f.SupportingEvidence.Select(r => r.EvidenceId).Where(id => !evidence.ContainsKey(id)).Distinct().ToList();
            if (unknown.Count > 0)
            {
                rejected.Add(new(f, $"trimite la probe care nu există în caz: {string.Join(", ", unknown)}"));
                continue;
            }
            for (int i = 0; i < f.SupportingEvidence.Count; i++)
            {
                var r = f.SupportingEvidence[i];
                f.SupportingEvidence[i] = r with { Sha256 = evidence[r.EvidenceId].Sha256 };
            }
            kept.Add(f);
        }
        return (kept, rejected);
    }
}
