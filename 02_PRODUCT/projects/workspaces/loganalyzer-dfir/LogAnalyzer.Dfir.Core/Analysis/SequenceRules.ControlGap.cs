using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class SequenceRules
{
    private static readonly Regex ControlTitleRx = new("„(?<t>[^”]+)”", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static string ControlName(Finding gap) => ControlTitleRx.Match(gap.Title) is { Success: true } m ? m.Groups["t"].Value : gap.Title;

    private static bool IsMediaControl(Ctx c, Finding gap)
    {
        var text = (gap.Title + " " + gap.Description).ToLowerInvariant();
        return c.Data.ControlGapMedia.ControlWords.Any(w => w.Length > 0 && text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// SEQ-CONTROL-GAP-MEDIA (lessons-learned row 63): a POLICY-CONTROL-GAP for a USB / device-install / audit / Defender control, followed - inside the gap or within the
    /// window after it - by a removable medium (MEDIA-*) and, where present, file activity on it (MEDIA-FILE-ACTIVITY). The gap finding carries no restoration time, so
    /// "control restabilit" is always reported as a step not observed; the end of the window is the last record collected, not a proven restoration.
    /// Minimum: the gap and at least one medium (data: minObservedSteps). High on a classified scope, Medium otherwise; Critical only with write evidence on an
    /// unregistered or unauthorized medium during the gap on a classified scope.
    /// </summary>
    private static void ControlGapMedia(Ctx c, List<Finding> o)
    {
        var d = c.Data.ControlGapMedia;
        var media = c.Existing.Where(IsMediaPresence).ToList();
        var acts = c.Existing.Where(f => f.RuleId == "MEDIA-FILE-ACTIVITY").ToList();
        foreach (var gap in c.Existing.Where(f => f.RuleId == "POLICY-CONTROL-GAP" && IsMediaControl(c, f)))
        {
            // Without the moment the control went off the gap cannot be placed on the timeline.
            if (gap.FirstSeenUtc is not { } start) continue;
            var end = gap.LastSeenUtc is { } e0 && e0 >= start ? e0 : start;
            var horizon = end + Min(d.MediaAfterGapMinutes);
            var inWindow = media.Where(m =>
            {
                var a = m.FirstSeenUtc ?? m.LastSeenUtc; var b = m.LastSeenUtc ?? m.FirstSeenUtc;
                return a is { } x && b is { } y && x <= horizon && y >= start;
            }).OrderBy(m => m.FirstSeenUtc ?? m.LastSeenUtc).ToList();
            if (inWindow.Count == 0) continue;

            var control = ControlName(gap);
            var steps = new List<SeqStep>
            {
                new("control dezactivat sau nesatisfăcut", "POLICY-CONTROL-GAP", start, "", control,
                    $"decalaj de control „{control}” (nivel rupt: vezi constatarea); fereastra se încheie la {Stamp(end)}, ultima înregistrare colectată în sursele politicii, nu o restabilire dovedită",
                    gap.SupportingEvidence.Take(5).ToList(), [gap.FindingId]),
            };
            var related = new List<string> { gap.FindingId };
            bool critical = false, anyWrite = false;
            foreach (var m in inWindow.Take(5))
            {
                var serial = SerialOf(m);
                var act = acts.FirstOrDefault(a => serial.Length > 0 && SerialOf(a) == serial);
                bool writeInGap = act is not null && IsWrite(act) && (act.FirstSeenUtc ?? act.LastSeenUtc) is { } af && (act.LastSeenUtc ?? af) >= start && af <= end;
                anyWrite |= writeInGap;
                if (writeInGap && m.RuleId is "MEDIA-UNREGISTERED" or "MEDIA-UNAUTHORIZED") critical = true;
                var seen = m.FirstSeenUtc is { } f1 && f1 >= start ? f1 : m.LastSeenUtc ?? m.FirstSeenUtc;
                var where = m.FirstSeenUtc is { } f2 && f2 < start ? " (mediul era observat și înainte de decalaj)" : seen > end ? " (după sfârșitul ferestrei, în intervalul de după decalaj)" : " (în interiorul decalajului)";
                var label = serial.Length > 0 ? serial : "fără serie";
                var evidence = m.SupportingEvidence.Take(5).Concat(act?.SupportingEvidence.Take(5) ?? []).ToList();
                var ids = new List<string> { m.FindingId };
                related.Add(m.FindingId);
                if (act is not null) { ids.Add(act.FindingId); related.Add(act.FindingId); }
                steps.Add(new($"mediu amovibil {label} observat", m.RuleId, seen, m.User, $"mediu {label}",
                    $"{m.RuleId}{where}" + (act is null ? "" : writeInGap ? $"; {act.RuleId}: urme de scriere pe volum în interiorul decalajului" : $"; {act.RuleId}: urme de activitate pe volum"), evidence, ids));
            }
            steps.Add(SequenceEngine.Missing("restabilirea controlului", "politică / GroupPolicy / Security 4719", c.Has(SecuritySource, "EventLog:Microsoft-Windows-GroupPolicy/Operational")));
            if (steps.Count(s => s.Observed) < d.MinObservedSteps) continue;

            var first = steps.Where(s => s.Observed && s.Source != "POLICY-CONTROL-GAP").OrderBy(s => s.TimeUtc).First();
            var more = inWindow.Count > 1 ? $" (+{inWindow.Count - 1} alte medii)" : "";
            var title = $"Secvență: control „{control}” dezactivat de la {Stamp(start)}; {first.Name} la {Stamp(first.TimeUtc)}{more}; restabilirea controlului: pas neobservat";
            var sev = critical && c.Classified ? Severity.Critical : c.Classified ? Severity.High : Severity.Medium;
            o.Add(Build(c, "SEQ-CONTROL-GAP-MEDIA", title, sev, anyWrite ? Confidence.Medium : Confidence.Low, steps,
                $"mediu observat între începutul decalajului și {d.MediaAfterGapMinutes} min după sfârșitul ferestrei", 
                "mediul este legat de decalaj doar prin timp (fereastra decalajului); nu există o relație dovedită între dezactivarea controlului și folosirea mediului.",
                "Combină un decalaj de control (POLICY-CONTROL-GAP) cu observațiile de medii amovibile (MEDIA-*, MEDIA-FILE-ACTIVITY).",
                related, gap.Host,
                ["Decalajul poate fi o întârziere de aplicare a politicii sau o eroare de configurare, fără legătură cu mediul.", "Mediul poate fi autorizat și folosit în activitate normală; starea lui vine din constatarea MEDIA-* componentă.",
                 "Sursele colectate nu arată cine a modificat controlul și nici dacă a fost restabilit."]));
        }
    }
}
