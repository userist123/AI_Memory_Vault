using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// For every path exclusion listed by a CFG-DEFENDER-EXCL finding: the Defender Operational 5007 event whose "New Value" added that path (the latest such event,
/// because it is the one the exclusion seen at collection comes from) is attached as evidence of the CFG finding, with its time and EventRecordID; every
/// DEF-DETECTION finding whose detection events name a file under that folder is put into <see cref="Finding.RelatedFindingIds"/>. When a detection under the
/// folder is older than that 5007, the CFG finding says so (<see cref="DetectionBeforeExclusion"/>) and what this does not prove (<see cref="NotProven"/>).
/// Runs once, after every finding exists, next to <see cref="DefenderExclusionHistoryMerger"/>. Deterministic (findings in their current order, paths in their
/// listed order, events ordered by time then locator); it only adds to findings and never removes one.
/// </summary>
public static class DefenderExclusionDetectionMerger
{
    public const string DetectionBeforeExclusion = "După o detecție în acest folder, folderul a fost exclus de la scanare";
    public const string NotProven = "Ce NU dovedește: intenția sau cine a făcut-o";
    public const string DefenderChannel = "EventLog:Microsoft-Windows-Windows Defender/Operational";

    public sealed record Result(List<Finding> Findings, int Linked);

    private static readonly HashSet<string> DetectionIds = ["1116", "1006", "1015", "1117", "1007", "1118", "1119"];
    private static readonly Regex RecordId = new(@"EventRecordID=(?<n>\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    public static Result Merge(IReadOnlyList<Finding> all, IReadOnlyCollection<TimelineEvent> timeline)
    {
        var cfg = all.Where(f => f.RuleId == "CFG-DEFENDER-EXCL").ToList();
        if (cfg.Count == 0) return new Result([.. all], 0);
        var defender = timeline.Where(e => e.Source.Equals(DefenderChannel, StringComparison.OrdinalIgnoreCase)).ToList();
        var adds = defender.Where(e => e.EventId == "5007")
            .Select(e => (Path: Norm(ExcludedPathOf(F(e, "New Value") + F(e, "NewValue"))), Event: e)).Where(x => x.Path.Length > 0)
            .OrderBy(x => x.Event.Time.Utc ?? DateTimeOffset.MinValue).ThenBy(x => x.Event.Locator, StringComparer.Ordinal).ToList();
        var rows = EvidenceQuote.Index(timeline);
        // per DEF-DETECTION finding: the paths and times of its detection events (from the timeline rows its evidence points to) plus its File
        var detections = all.Where(f => f.RuleId == "DEF-DETECTION").Select(f => (Finding: f, Hits: f.SupportingEvidence
                .Select(r => rows.GetValueOrDefault((r.EvidenceId, r.Locator))).Where(e => e is not null && DetectionIds.Contains(e.EventId))
                .Select(e => (Path: Norm(F(e!, "Path").Length > 0 ? F(e!, "Path") : e!.Path), Time: e!.Time.Utc)).ToList())).ToList();
        int linked = 0;
        foreach (var c in cfg)
        {
            foreach (var path in ListedPaths(c))
            {
                var add = adds.LastOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && SameHost(x.Event.Host, c.Host));
                var under = detections.Where(d => SameHost(d.Finding.Host, c.Host) && (Under(Norm(d.Finding.File), path) || d.Hits.Any(h => Under(h.Path, path)))).ToList();
                if (add.Event is null && under.Count == 0) continue;
                bool changed = false;
                foreach (var d in under)
                    if (!c.RelatedFindingIds.Contains(d.Finding.FindingId)) { c.RelatedFindingIds.Add(d.Finding.FindingId); changed = true; }
                if (add.Event is { } ev)
                {
                    var at = ev.Time.Utc;
                    var firstDet = under.SelectMany(d => d.Hits.Where(h => Under(h.Path, path)).Select(h => (d.Finding.FindingId, h.Time)))
                        .Concat(under.Where(d => !d.Hits.Any(h => Under(h.Path, path))).Select(d => (d.Finding.FindingId, Time: d.Finding.FirstSeenUtc)))
                        .Where(x => x.Time is not null).OrderBy(x => x.Time).ThenBy(x => x.FindingId, StringComparer.Ordinal).FirstOrDefault();
                    bool before = at is not null && firstDet.Time is not null && firstDet.Time < at;
                    var rid = RecordId.Match(ev.Locator);
                    var (quote, ts, channel) = EvidenceQuote.From(ev);
                    var desc = $"Defender 5007: excluderea „{path}” adăugată la {(at is null ? "oră necunoscută" : at.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC")}" +
                               (rid.Success ? $" (EventRecordID {rid.Groups["n"].Value})" : "") +
                               (before ? $". {DetectionBeforeExclusion} (prima detecție: {firstDet.FindingId}, {firstDet.Time!.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC)" : "");
                    var r = new EvidenceRef(ev.EvidenceId, ev.Locator, desc, ev.SourceSha256, quote, ts, channel);
                    if (!c.SupportingEvidence.Any(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator)) { c.SupportingEvidence.Add(r); changed = true; }
                    if (before)
                    {
                        var sentence = $"{DetectionBeforeExclusion}: „{path}” (detecția {firstDet.FindingId} precede evenimentul 5007 {ev.Locator}). {NotProven}.";
                        if (!c.Limitations.Contains(sentence)) { c.Limitations = [sentence, .. c.Limitations]; changed = true; }
                    }
                }
                if (!changed) continue;
                linked++;
                c.AuditTrail = [.. c.AuditTrail, new FindingAuditEntry("finding.exclusion_detection_linked", "LogAnalyzer",
                    $"{c.RuleId} {c.FindingId} {path}: 5007 {(add.Event?.Locator ?? "absent")}; DEF-DETECTION {(under.Count == 0 ? "niciuna" : string.Join(", ", under.Select(d => d.Finding.FindingId)))}", DateTimeOffset.UtcNow)];
            }
        }
        return new Result([.. all], linked);
    }

    /// <summary>The exclusion paths the CFG evidence quote lists ("N excluderi: a; b; … încă K"), normalised, in their listed order.</summary>
    internal static List<string> ListedPaths(Finding cfg)
    {
        var list = new List<string>();
        foreach (var e in cfg.SupportingEvidence)
        {
            if (!e.Locator.EndsWith("Paths", StringComparison.OrdinalIgnoreCase)) continue;
            var q = e.Quote ?? "";
            var i = q.IndexOf(" excluderi: ", StringComparison.Ordinal);
            if (i < 0) continue;
            foreach (var item in q[(i + " excluderi: ".Length)..].Split("; ", StringSplitOptions.RemoveEmptyEntries))
                if (!item.StartsWith("… ", StringComparison.Ordinal) && Norm(item) is { Length: > 0 } n && !list.Contains(n, StringComparer.OrdinalIgnoreCase)) list.Add(n);
        }
        return list;
    }

    /// <summary>The folder a 5007 "New Value" adds ("...\Exclusions\Paths\C:\x = 0" or TemporaryPaths); "" when it is not a path exclusion (same reading as Correlation).</summary>
    internal static string ExcludedPathOf(string newValue)
    {
        foreach (var marker in new[] { @"\Exclusions\TemporaryPaths\", @"\Exclusions\Paths\" })
        {
            int i = (newValue ?? "").IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var p = newValue![(i + marker.Length)..];
            int eq = p.IndexOf(" = ", StringComparison.Ordinal);
            if (eq >= 0) p = p[..eq];
            p = p.Trim();
            while (p.StartsWith('\\') || p.StartsWith('?')) p = p[1..];
            return p.TrimEnd('\\');
        }
        return "";
    }

    /// <summary>A Defender path ("file:_C:\x\a.exe", "containerfile:_...", several joined by ";") is under the folder when one of its parts is the folder or inside it.</summary>
    private static bool Under(string defenderPath, string folder)
    {
        if (defenderPath.Length == 0 || folder.Length == 0) return false;
        foreach (var raw in defenderPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.Trim();
            int u = p.IndexOf(":_", StringComparison.Ordinal);
            if (u > 0 && u < 20) p = p[(u + 2)..];
            int arrow = p.IndexOf("->", StringComparison.Ordinal);
            if (arrow >= 0) p = p[..arrow];
            p = Norm(p);
            if (p.Equals(folder, StringComparison.OrdinalIgnoreCase) || p.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
    private static string Norm(string p) => (p ?? "").Trim().Trim('"', '\'').Replace('/', '\\').TrimEnd('\\');
    private static bool SameHost(string a, string b) => a.Length == 0 || b.Length == 0 || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
