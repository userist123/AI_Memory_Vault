using System.Globalization;
using LogAnalyzer.Dfir.FileSystem;
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
    /// <param name="AuditpolGuid">The audit subcategory that writes these events (PolicyNative: Filtering Platform Connection 0x26, File System 0x1D); "" = not checked.</param>
    /// <param name="AlsoRequired">What the subcategory alone does not switch on (4663 also needs a SACL on the objects); "" = nothing.</param>
    private sealed record Category(string Artifact, int[] Ids, string Impact, string Alternative, string AuditpolGuid = "", string AlsoRequired = "");

    public const string GuidFilteringPlatformConnection = "0CCE9226-69AE-11D9-BED3-505054503030", GuidFileSystem = "0CCE921D-69AE-11D9-BED3-505054503030";

    private static readonly Category[] Categories =
    [
        new("Security 4688 (crearea proceselor)", [4688], "Nu se știe din jurnal ce procese au pornit, cu ce linie de comandă și din ce părinte",
            "Prefetch, Amcache, BAM, SRUM, Sysmon 1"),
        new("Security 5156/5157 (conexiuni WFP per proces)", [5156, 5157], "Nu se știe din jurnal ce program a deschis ce conexiune",
            "SRUM (octeți per aplicație), captură PCAP, Sysmon 3, instantaneul stării live a conexiunilor", GuidFilteringPlatformConnection),
        new("Security 4663 (acces la obiecte / fișiere)", [4663], "Nu se știe din jurnal ce fișiere au fost citite sau șterse",
            "USN Journal, LNK / Jump Lists, ShellBags", GuidFileSystem,
            "pentru 4663 mai este necesară și o listă SACL de auditare pe fișierele sau folderele urmărite; fără ea, nici subcategoria pornită nu scrie evenimentul"),
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
        var span = $"{first:yyyy-MM-dd} – {last:yyyy-MM-dd}, {LogAnalyzer.Dfir.Language.RoText.Count(security.Count, "eveniment", "evenimente")} în {activeHours.Count} ore cu activitate";
        var gaps = new List<EvidenceGap>();
        var auditpol = NewestAuditpol(events);
        foreach (var c in Categories)
        {
            var ids = c.Ids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToHashSet();
            var hits = security.Where(e => ids.Contains(e.EventId)).ToList();
            if (hits.Count == 0)
            {
                var row = c.AuditpolGuid.Length == 0 ? null : auditpol.FirstOrDefault(r => F(r, "SubcategoryGuid").Equals(c.AuditpolGuid, StringComparison.OrdinalIgnoreCase));
                bool noAuditing = row is not null && F(row, "AuditSuccess") == "nu" && F(row, "AuditFailure") == "nu";
                gaps.Add(new EvidenceGap(c.Artifact, EvidenceStatus.NotAvailable,
                    noAuditing
                        ? $"jurnalul Security ({span}) nu conține niciun eveniment {string.Join("/", c.Ids)}; politica de audit colectată arată subcategoria „{F(row!, "Subcategory")}” cu setarea „{F(row!, "Setting")}” " +
                          $"(auditpol:{c.AuditpolGuid}: „{LogAnalyzer.Dfir.Integrity.EvidenceQuote.Truncate(F(row!, "RawLine"), LogAnalyzer.Dfir.Integrity.EvidenceQuote.MaxQuoteLength)}”)" +
                          (c.AlsoRequired.Length > 0 ? "; " + c.AlsoRequired : "")
                        : $"jurnalul Security ({span}) nu conține niciun eveniment {string.Join("/", c.Ids)}: subcategoria de audit era, cel mai probabil, dezactivată",
                    c.Impact, c.Alternative, "Nu (activați auditul pentru viitor)"));
                continue;
            }
            var hours = hits.Select(Hour).ToHashSet();
            double share = (double)hours.Count / activeHours.Count;
            if (share < IntermittentShare)
                gaps.Add(new EvidenceGap(c.Artifact, EvidenceStatus.Partial,
                    $"{LogAnalyzer.Dfir.Language.RoText.Count(hits.Count, "eveniment", "evenimente")} {string.Join("/", c.Ids)} în doar {hours.Count} din {activeHours.Count} ore cu activitate ({share:P1}; {span}): auditul nu a fost continuu" +
                    $" (cele mai frecvente: {string.Join(", ", hits.Select(e => e.Fields.GetValueOrDefault("NewProcessName") ?? "").Where(n => n.Length > 0).Select(WinPath.GetFileName).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} ×{g.Count()}"))})",
                    c.Impact + " în afara acestor ore", c.Alternative, "Nu (activați auditul pentru viitor)"));
        }
        return gaps;
    }

    private static string F(TimelineEvent r, string k) => r.Fields.TryGetValue(k, out var v) ? v : "";

    /// <summary>The auditpol rows (parser AuditPolicyCsvParser, locator "auditpol:&lt;GUID&gt;") of the newest auditpol evidence item, chosen as LocalSecurityAudit does.</summary>
    private static List<TimelineEvent> NewestAuditpol(IReadOnlyCollection<TimelineEvent> events)
    {
        var mine = events.Where(e => e.Source == "auditpol").ToList();
        if (mine.Count == 0) return mine;
        var id = mine.GroupBy(r => r.EvidenceId).OrderByDescending(g => g.Max(r => r.Time.Utc ?? DateTimeOffset.MinValue)).ThenByDescending(g => g.Key, StringComparer.Ordinal).First().Key;
        return mine.Where(r => r.EvidenceId == id).ToList();
    }
}
