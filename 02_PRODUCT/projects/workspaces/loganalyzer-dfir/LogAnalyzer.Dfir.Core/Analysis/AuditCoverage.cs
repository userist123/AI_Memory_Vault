using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// What the Security log could not show. A category with no event at all was not audited (NOT_AVAILABLE); a category whose
/// events cover only a small part of the hours in which the log recorded anything was audited intermittently (PARTIAL) —
/// on the reference station 4688 appeared only during boot, before the "No Auditing" policy was applied. Neither ever
/// means the activity did not happen.
/// </summary>
public static class AuditCoverage
{
    private sealed record Category(string Artifact, int[] Ids, string Impact, string Alternative);

    private static readonly Category[] Categories =
    [
        new("Security 4688 (crearea proceselor)", [4688], "Nu se știe din jurnal ce procese au pornit, cu ce linie de comandă și din ce părinte",
            "Prefetch, Amcache, BAM, SRUM, Sysmon 1"),
        new("Security 5156/5157 (conexiuni WFP per proces)", [5156, 5157], "Nu se știe din jurnal ce program a deschis ce conexiune",
            "SRUM (octeți per aplicație), captură PCAP, Sysmon 3, fotografia live a conexiunilor"),
        new("Security 4663 (acces la obiecte / fișiere)", [4663], "Nu se știe din jurnal ce fișiere au fost citite sau șterse",
            "USN Journal, LNK / Jump Lists, ShellBags"),
    ];

    /// <summary>Below this share of active hours, a category counts as intermittently audited.</summary>
    public const double IntermittentShare = 0.10;

    public static List<EvidenceGap> Gaps(IReadOnlyCollection<TimelineEvent> events)
    {
        var security = events.Where(e => e.Source.Equals("EventLog:Security", StringComparison.OrdinalIgnoreCase) && e.Time.Utc is not null).ToList();
        if (security.Count == 0) return [];
        static DateTime Hour(TimelineEvent e) { var t = e.Time.Utc!.Value.UtcDateTime; return new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc); }
        var activeHours = security.Select(Hour).ToHashSet();
        var first = security.Min(e => e.Time.Utc);
        var last = security.Max(e => e.Time.Utc);
        var span = $"{first:yyyy-MM-dd} – {last:yyyy-MM-dd}, {security.Count} evenimente în {activeHours.Count} ore cu activitate";
        var gaps = new List<EvidenceGap>();
        foreach (var c in Categories)
        {
            var ids = c.Ids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToHashSet();
            var hits = security.Where(e => ids.Contains(e.EventId)).ToList();
            if (hits.Count == 0)
            {
                gaps.Add(new EvidenceGap(c.Artifact, EvidenceStatus.NotAvailable,
                    $"jurnalul Security ({span}) nu conține niciun eveniment {string.Join("/", c.Ids)}: subcategoria de audit era, cel mai probabil, dezactivată",
                    c.Impact, c.Alternative, "Nu (activați auditul pentru viitor)"));
                continue;
            }
            var hours = hits.Select(Hour).ToHashSet();
            double share = (double)hours.Count / activeHours.Count;
            if (share < IntermittentShare)
                gaps.Add(new EvidenceGap(c.Artifact, EvidenceStatus.Partial,
                    $"{hits.Count} evenimente {string.Join("/", c.Ids)} în doar {hours.Count} din {activeHours.Count} ore cu activitate ({share:P1}; {span}): auditul nu a fost continuu" +
                    $" (cele mai frecvente: {string.Join(", ", hits.Select(e => e.Fields.GetValueOrDefault("NewProcessName") ?? "").Where(n => n.Length > 0).Select(Path.GetFileName).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} ×{g.Count()}"))})",
                    c.Impact + " în afara acestor ore", c.Alternative, "Nu (activați auditul pentru viitor)"));
        }
        return gaps;
    }
}
