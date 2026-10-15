using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Classified information on the case timeline (owner request 2026-10-15; HG 585 art. 291 access records, art. 294 media accounting): who accessed the
/// files of the procedure profile's classified folders and whether any left the station without an entry in the transfer register. The analysis itself
/// is <see cref="ClassifiedAccess"/>; this file turns the timeline into its records and its result into findings.
/// </summary>
public static partial class Wp14Rules
{
    internal const string ClassifiedAccessRuleId = "CLASSIFIED-ACCESS";
    internal const string ClassifiedUnjustifiedRuleId = "CLASSIFIED-EXPORT-UNJUSTIFIED";
    internal const string ClassifiedToVerifyRuleId = "CLASSIFIED-EXPORT-TO-VERIFY";
    internal const string ClassifiedApprovedRuleId = "CLASSIFIED-EXPORT-APPROVED";
    internal const string ClassifiedDeletedRuleId = "CLASSIFIED-DELETED";
    internal const string ClassifiedAuditChangedRuleId = "CLASSIFIED-AUDIT-CHANGED";

    /// <summary>The timeline rows the classified analysis reads, as neutral records (the row itself is kept as the origin for the evidence references).</summary>
    internal static List<AccessRecord> ClassifiedRecords(IReadOnlyList<TimelineEvent> events)
    {
        var l = new List<AccessRecord>();
        foreach (var e in events)
        {
            if (e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) && int.TryParse(e.EventId, out var id))
            {
                var ch = e.Source["EventLog:".Length..];
                bool sec = ch.Equals("Security", StringComparison.OrdinalIgnoreCase) && id is 4656 or 4658 or 4660 or 4663 or 4670 or 4907 or 4688 or 5145;
                bool print = ch.Contains("PrintService", StringComparison.OrdinalIgnoreCase) && id == 307;
                if (sec || print) l.Add(new AccessRecord("event", sec ? "Security" : ch, id, T(e), $"{ch} {id} {e.Locator}".Trim(), e.Fields, e.Path, e));
            }
            else if (e.Source is "LNK" or "JumpList" or "USN")
                l.Add(new AccessRecord(e.Source, "", 0, e.Source == "LNK" && ParseIso(F(e, "LnkFileModifiedUtc")) is { } lt ? lt : T(e), $"{e.Source} {e.Locator}".Trim(), e.Fields,
                                       e.Path.Length > 0 ? e.Path : F(e, "LnkTarget"), e));
        }
        return l;
    }

    internal static void ClassifiedFindings(Ctx c, List<Finding> o)
    {
        var folders = c.Profile?.ClassifiedFolders.Where(f => f.Path.Trim().Length > 0).Select(f => new ClassifiedRoot(f.Path.Trim(), null, f.Note)).ToList() ?? [];
        if (folders.Count == 0) return;   // no classified folders: the station control's A10 says so; the timeline has nothing to compare with
        var observed = ObservedUsb(c.Events);
        var input = new ClassifiedInput
        {
            Folders = folders, Records = ClassifiedRecords(c.Events), Transfers = c.Input.Transfers,
            RemovableDrives = observed.Select(m => m.Letter).Where(x => x.Length > 0).Select(x => x + ":").ToHashSet(StringComparer.OrdinalIgnoreCase),
            Media = observed.SelectMany(m => m.Times.Select(t => new MediumSeen(t, m.Serial, Kind(m), m.Letter))).ToList(),
            NetworkApplies = c.Scope.Network != NetworkCategory.StandalonePc,
            RegisterZone = c.Input.StationZone,   // the station's declared zone, never the analysis machine's
        };
        var rep = ClassifiedAccess.Analyze(input);
        static List<EvidenceRef> Refs(IEnumerable<object?> origins, string what) =>
            origins.OfType<TimelineEvent>().Distinct().Take(20).Select(e => Ref(e, what + ": " + e.Summary)).ToList();

        foreach (var f in rep.Ordered)
        {
            foreach (var x in f.Exits)
            {
                var (rule, sev, cls) = x.Verdict switch
                {
                    ExitVerdict.Unjustified => (ClassifiedUnjustifiedRuleId, Severity.High, Classification.Correlated),
                    ExitVerdict.Approved => (ClassifiedApprovedRuleId, Severity.Info, Classification.Correlated),
                    _ => (ClassifiedToVerifyRuleId, Severity.Medium, Classification.Candidate),
                };
                o.Add(new Finding
                {
                    FindingId = c.NextId(), RuleId = rule,
                    Title = $"Informație clasificată, ieșire {x.VerdictText}: {ClassifiedAccess.FileName(f.Path)} — {x.ChannelText}",
                    Severity = sev, Category = c.FindingCategory, Classification = cls,
                    Confidence = x.Channel == ExitChannel.TemporalCoincidence || x.Weak ? Confidence.Low : Confidence.Medium,
                    FirstSeenUtc = x.TimeUtc, LastSeenUtc = x.TimeUtc, User = x.Who, File = f.Path,
                    Description = $"{x.Detail} Destinație: {x.Target}. Lanțul probelor: {string.Join(" → ", x.Chain)}",
                    ClassificationReason = x.Verdict == ExitVerdict.Unjustified
                        ? "O înregistrare arată fișierul clasificat (sau unul cu același nume) mergând în afara folderului, iar registrul transferurilor nu are o intrare care să acopere ieșirea."
                        : x.Verdict == ExitVerdict.Approved ? "Ieșirea corespunde unei intrări din registrul transferurilor (fișier, suport, oră)."
                        : x.Channel == ExitChannel.TemporalCoincidence ? ClassifiedAccess.TemporalNote + ": apropierea în timp nu arată copierea."
                        : "Urma arată o mișcare a fișierului, dar nu destinația sau nu fișierul însuși; trebuie verificată.",
                    SemanticType = x.Channel == ExitChannel.TemporalCoincidence ? SemanticType.Inference : SemanticType.Correlation,
                    SupportingEvidence = Refs(x.Origins, x.ChannelText),
                    MissingEvidence = x.Verdict == ExitVerdict.Approved ? [] : ["Hash-ul fișierului de pe destinație", "Registrul transferurilor completat pentru această ieșire"],
                });
            }
            if (f.Deleted)
                o.Add(new Finding
                {
                    FindingId = c.NextId(), RuleId = ClassifiedDeletedRuleId, Title = $"Fișier clasificat șters: {ClassifiedAccess.FileName(f.Path)}",
                    Severity = Severity.Medium, Category = c.FindingCategory, Classification = Classification.Direct, Confidence = Confidence.High,
                    FirstSeenUtc = f.Accesses.First(a => a.Access.StartsWith("șters", StringComparison.Ordinal)).TimeUtc, File = f.Path,
                    User = string.Join(", ", f.Accesses.Where(a => a.Access.StartsWith("șters", StringComparison.Ordinal)).Select(a => a.Who).Distinct()),
                    Description = $"Windows a înregistrat ștergerea fișierului {f.Path} (4663 cu DELETE urmat de 4660). Distrugerea unui document clasificat se face pe bază de proces-verbal.",
                    ClassificationReason = "Ștergerea este înregistrată direct de Windows; dacă era aprobată se vede doar din documentele organizației.",
                    SemanticType = SemanticType.Observation,
                    SupportingEvidence = Refs(f.Accesses.Where(a => a.Access.StartsWith("șters", StringComparison.Ordinal)).Select(a => a.Origin), "ștergere"),
                });
            var strong = f.Accesses.Where(a => !a.Weak).ToList();
            if (f.Accesses.Count > 0)
                o.Add(new Finding
                {
                    FindingId = c.NextId(), RuleId = ClassifiedAccessRuleId, Title = $"Accese la fișierul clasificat {ClassifiedAccess.FileName(f.Path)} ({f.Accesses.Count})",
                    Severity = Severity.Info, Category = c.FindingCategory, Classification = strong.Count > 0 ? Classification.Direct : Classification.Candidate,
                    Confidence = strong.Count > 0 ? Confidence.High : Confidence.Low, File = f.Path, User = f.WhoText,
                    FirstSeenUtc = f.Accesses.Min(a => a.TimeUtc), LastSeenUtc = f.Accesses.Max(a => a.TimeUtc),
                    Description = $"{f.Path}: {strong.Count} accese înregistrate de auditul Windows, {f.Accesses.Count - strong.Count} urme ale stației (dovadă mai slabă). " +
                                  string.Join("; ", f.Accesses.OrderBy(a => a.TimeUtc).Take(15).Select(a => $"{(a.TimeUtc is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" : "ora necunoscută")} {a.Who} {a.Process} {a.Access}")),
                    ClassificationReason = "Cronologia acceselor din auditul fișierelor (SACL + „File System”) și din urmele stației.",
                    SemanticType = SemanticType.Observation,
                    SupportingEvidence = Refs(f.Accesses.Select(a => a.Origin), "acces"),
                });
        }
        foreach (var s in rep.SecurityChanges)
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = ClassifiedAuditChangedRuleId,
                Title = (s.AuditRules ? "Regulile de audit (SACL) ale unui obiect clasificat au fost schimbate: " : "Permisiunile unui obiect clasificat au fost schimbate: ") + s.Object,
                Severity = s.AuditRules ? Severity.High : Severity.Medium, Category = c.FindingCategory, Classification = Classification.Direct, Confidence = Confidence.High,
                FirstSeenUtc = s.TimeUtc, LastSeenUtc = s.TimeUtc, User = s.Who, Process = s.Process, File = s.Object,
                Description = $"{s.Who} a schimbat {(s.AuditRules ? "regulile de audit" : "permisiunile")} pentru {s.Object} cu {s.Process}. " +
                              (s.AuditRules ? "Cine schimbă regulile de audit poate opri înregistrarea accesului la documente." : "Permisiunile decid cine poate deschide documentele."),
                ClassificationReason = "Schimbarea este înregistrată direct de Windows (Security 4670 / 4907).",
                SemanticType = SemanticType.Configuration,
                SupportingEvidence = Refs([s.Origin], s.AuditRules ? "4907" : "4670"),
            });
    }
}
