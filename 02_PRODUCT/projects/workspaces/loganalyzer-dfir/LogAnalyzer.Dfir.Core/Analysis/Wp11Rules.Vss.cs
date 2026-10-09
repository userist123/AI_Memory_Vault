using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    private sealed record VssHit(TimelineEvent Event, VssCommandPattern Pattern, string Command, bool ProcessStart);

    /// <summary>
    /// VSS-SNAPSHOT-DELETED. A command that deletes shadow copies (vssadmin / wmic / WMI-CIM / diskshadow in 4688, Sysmon 1 or a PowerShell
    /// script block) is Medium on its own: it shows the command was started, not that it succeeded. It is High only when the case already
    /// holds mass-modification / encryption evidence (an Impact finding, T1486/T1485, or a Defender ransomware detection) within the impact
    /// window; no new detector is built here. Resizing the shadow storage is only Low. volsnap 25/33 are listed when they stand alone
    /// (Info/Low: a size limit is a common benign cause) and attach to a command finding when one exists; VSS 8193/8194 are errors and are
    /// only supporting evidence, never a finding by themselves.
    /// </summary>
    private static void Vss(Ctx c, List<Finding> o)
    {
        var lists = c.Data.Lists.Vss;
        var patterns = lists.CommandPatterns.Select(p => (P: p, Rx: new Regex(p.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))).ToList();
        var hits = new List<VssHit>();
        foreach (var e in c.Events)
        {
            string cmd; bool start;
            if (Ev(e, "Security", 4688)) { cmd = F(e, "CommandLine"); start = true; }
            else if (Ev(e, Sysmon, 1)) { cmd = F(e, "CommandLine"); start = true; }
            else if (Ev(e, "Microsoft-Windows-PowerShell/Operational", 4104)) { cmd = F(e, "ScriptBlockText"); start = false; }
            else continue;
            if (cmd.Length == 0) continue;
            foreach (var (p, rx) in patterns)
            {
                bool m;
                try { m = rx.IsMatch(cmd); } catch (RegexMatchTimeoutException) { m = false; }
                if (m) { hits.Add(new(e, p, cmd, start)); break; }
            }
        }
        var sysEvents = c.Events.Where(e => (Ev(e, "System", 25, 33) && e.Provider.Equals("volsnap", StringComparison.OrdinalIgnoreCase)) ||
                                            (Ev(e, "Application", 8193, 8194) && e.Provider.Equals("VSS", StringComparison.OrdinalIgnoreCase))).ToList();

        if (hits.Count > 0)
        {
            var ordered = hits.OrderBy(h => T(h.Event)).ToList();
            var first = T(ordered[0].Event); var last = T(ordered[^1].Event);
            bool anyDelete = hits.Any(h => h.Pattern.Kind == "delete");
            var near = sysEvents.Where(e => first is { } f && T(e) is { } t && t >= f - c.Options.ImpactWindow && t <= (last ?? f) + c.Options.ImpactWindow).ToList();
            var impact = (first is null ? [] : c.Existing.Concat(o).Where(f => IsImpactFinding(f, lists) && (f.FirstSeenUtc ?? f.LastSeenUtc) is { } ft && (ft - first.Value).Duration() <= c.Options.ImpactWindow)).ToList();
            var sev = !anyDelete ? Severity.Low : impact.Count > 0 ? Severity.High : Severity.Medium;
            bool processStart = hits.Any(h => h.ProcessStart);
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "VSS-SNAPSHOT-DELETED",
                Title = anyDelete ? "Comandă de ștergere a copiilor shadow (VSS) înregistrată" : "Comandă de reducere a spațiului copiilor shadow (VSS) înregistrată",
                Severity = sev, Category = "Impact", Classification = impact.Count > 0 ? Classification.Correlated : Classification.Direct,
                Confidence = impact.Count > 0 ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1490",
                FirstSeenUtc = first, LastSeenUtc = last, User = F(ordered[0].Event, "SubjectUserName", "User"), Process = ordered[0].Event.Process,
                Description = $"{hits.Count} comenzi ({string.Join(", ", hits.Select(h => $"{h.Pattern.Tool} {h.Pattern.Kind}").Distinct())}): {Join(hits.Select(h => h.Command.ReplaceLineEndings(" ").Length > 160 ? h.Command.ReplaceLineEndings(" ")[..160] + "…" : h.Command.ReplaceLineEndings(" ")), 3)}. " +
                              "Înregistrarea arată că a fost lansată comanda; nu arată că ștergerea a reușit sau ce copii existau." +
                              (near.Count > 0 ? $" Evenimente de sistem VSS/volsnap în ±{c.Options.ImpactWindow.TotalHours:0} h: {Join(near.Select(e => $"{e.Provider} {e.EventId}"), 6)} (volsnap 33 = cea mai veche copie ștearsă pentru limita de spațiu; 8193/8194 = erori VSS)." : "") +
                              (impact.Count > 0 ? $" Dovezi de impact deja în caz: {Join(impact.Select(f => f.Title), 3)}." : ""),
                ClassificationReason = impact.Count > 0 ? "Comandă de ștergere a copiilor shadow și, în ±24 h, o constatare de impact existentă (nu un detector nou)." : "Linie de comandă de ștergere a copiilor shadow în 4688 / Sysmon 1 / PowerShell 4104.",
                SemanticType = processStart ? SemanticType.Execution : SemanticType.Observation,
                SupportingEvidence = ordered.Take(12).Select(h => Ref(h.Event, $"{h.Event.EventId} {h.Pattern.Tool} {h.Pattern.Kind}")).Concat(near.Take(8).Select(e => Ref(e, $"{e.Provider} {e.EventId}"))).ToList(),
                RelatedFindingIds = impact.Select(f => f.FindingId).ToList(),
                AlternativeExplanations = ["Administratorii șterg sau redimensionează copiile shadow pentru a elibera spațiu sau înainte de o reconfigurare.", "Software de backup poate folosi aceste comenzi."],
            });
            return;
        }

        var vol = sysEvents.Where(e => e.Provider.Equals("volsnap", StringComparison.OrdinalIgnoreCase)).OrderBy(T).ToList();
        if (vol.Count == 0) return;
        bool aborted = vol.Any(e => e.EventId == "25");
        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = "VSS-SNAPSHOT-DELETED",
            Title = aborted ? "Copii shadow (VSS) abandonate de sistem: spațiul de stocare nu a putut crește" : "Cea mai veche copie shadow (VSS) ștearsă de sistem pentru limita de spațiu",
            Severity = aborted ? Severity.Low : Severity.Info, Category = "Impact", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1490",
            FirstSeenUtc = T(vol[0]), LastSeenUtc = T(vol[^1]),
            Description = $"volsnap: {Join(vol.Select(e => e.EventId))} ({vol.Count} evenimente). 33 = cea mai veche copie a fost ștearsă pentru a respecta limita de spațiu setată; 25 = copii abandonate pentru că spațiul nu a putut crește peste limită. " +
                          "Sunt efecte ale limitei de spațiu, nu dovada unei ștergeri făcute de un utilizator; nu există în caz o comandă de ștergere.",
            ClassificationReason = "Evenimente volsnap 25/33 în jurnalul System; nicio linie de comandă de ștergere a copiilor shadow în caz.",
            SemanticType = SemanticType.Observation,
            SupportingEvidence = vol.Take(12).Select(e => Ref(e, $"volsnap {e.EventId}")).ToList(),
            AlternativeExplanations = ["Limita de spațiu pentru copiile shadow a fost atinsă (cauza obișnuită).", "O eroare de I/O pe volum."],
        });
    }

    private static bool IsImpactFinding(Finding f, VssLists lists) =>
        lists.ImpactMitreIds.Contains(f.MitreTechniqueId, StringComparer.OrdinalIgnoreCase) ||
        lists.ImpactCategories.Contains(f.Category, StringComparer.OrdinalIgnoreCase) && f.RuleId != "VSS-SNAPSHOT-DELETED" ||
        f.RuleId == "DEF-DETECTION" && lists.ImpactDetectionKeywords.Any(k => (f.Title + " " + f.Description).Contains(k, StringComparison.OrdinalIgnoreCase));
}
