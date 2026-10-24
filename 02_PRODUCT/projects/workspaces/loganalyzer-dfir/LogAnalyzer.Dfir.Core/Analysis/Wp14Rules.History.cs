using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>One dated trace of a medium from any source, with where the source came from (live log, archived log, Windows.old, shadow copy).</summary>
public sealed record MediumTrace(DateTimeOffset WhenUtc, string Source, string Meaning, string Origin, TimelineEvent Event)
{
    public string Text => $"{WhenUtc:yyyy-MM-dd HH:mm} UTC · {Source}" + (Meaning.Length > 0 ? $" ({Meaning})" : "") + (Origin.Length > 0 ? $" · {Origin}" : "");
}

/// <summary>
/// E1: where an evidence item came from, as a short Romanian label for the connection history: an archived event log (Archive-*.evtx), the
/// Windows.old folder of a previous installation, or a Volume Shadow Copy. "" = the current system.
/// </summary>
public static class EvidenceOrigin
{
    public static string Label(EvidenceItem e) => Label(e.OriginalPath + " " + e.Notes);

    public static string Label(string pathOrNotes)
    {
        var p = pathOrNotes.Replace('/', '\\');
        string shadow = "";
        var i = p.IndexOf("HarddiskVolumeShadowCopy", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            var digits = new string(p[(i + "HarddiskVolumeShadowCopy".Length)..].TakeWhile(char.IsDigit).ToArray());
            shadow = "copie de umbră (VSS) " + digits;
        }
        var parts = new List<string>();
        if (shadow.Length > 0) parts.Add(shadow);
        if (p.Contains(@"\Windows.old\", StringComparison.OrdinalIgnoreCase)) parts.Add("Windows.old (instalare anterioară)");
        if (System.Text.RegularExpressions.Regex.IsMatch(p, @"(^|\\|\s)Archive-[^\\]+\.evtx", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)))
            parts.Add("jurnal arhivat");
        return string.Join(", ", parts);
    }

    /// <summary>evidence id → origin label, only for items that are not from the current system.</summary>
    public static Dictionary<string, string> Map(IEnumerable<EvidenceItem> evidence) =>
        evidence.Select(e => (e.EvidenceId, L: Label(e))).Where(x => x.L.Length > 0).ToDictionary(x => x.EvidenceId, x => x.L, StringComparer.Ordinal);
}

public static partial class Wp14Rules
{
    internal const string HistoryRuleId = "MEDIA-HISTORY";

    /// <summary>Every dated trace of a medium from every source, oldest first.</summary>
    public static IReadOnlyList<MediumTrace> History(IReadOnlyList<TimelineEvent> events, string serial, IReadOnlyDictionary<string, string>? origins = null)
    {
        var norm = LogAnalyzer.Dfir.Registers.MediaRegister.NormalizeSerial(serial);
        var m = ObservedUsb(events).FirstOrDefault(o => o.Serial.Equals(norm, StringComparison.OrdinalIgnoreCase));
        return m is null ? [] : Traces(m, origins);
    }

    private static List<MediumTrace> Traces(Observed m, IReadOnlyDictionary<string, string>? origins) =>
        m.Events.Where(e => T(e) is not null)
            .Select(e => new MediumTrace(T(e)!.Value, TraceSource(e), e.TimeSemantics is "recorded" ? "" : e.TimeSemantics, origins?.GetValueOrDefault(e.EvidenceId) ?? "", e))
            .GroupBy(t => (t.WhenUtc, t.Source)).Select(g => g.First())
            .OrderBy(t => t.WhenUtc).ToList();

    private static string TraceSource(TimelineEvent e) =>
        e.Source == "USB" ? "registrul SYSTEM (USBSTOR / proprietăți dispozitiv)"
        : e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) ? $"{e.Source["EventLog:".Length..]} {e.EventId}"
        : e.Source;

    /// <summary>One MEDIA-HISTORY finding per medium: the full connection history from every source, earliest evidence first and named.</summary>
    internal static void HistoryFindings(Ctx c, List<Finding> o)
    {
        var origins = c.Input.EvidenceOrigins;
        foreach (var m in ObservedUsb(c.Events).Where(m => m.Serial.Length > 0))
        {
            var traces = Traces(m, origins);
            if (traces.Count == 0) continue;
            var first = traces[0];
            var fromOld = traces.Count(t => t.Origin.Length > 0);
            var sources = traces.Select(t => t.Source).Distinct().ToList();
            var desc = $"Istoricul conectărilor suportului {m.Serial} din toate sursele ({sources.Count}: {string.Join(", ", sources)}): {traces.Count} urme datate. " +
                       $"Cea mai veche dovadă: {first.Text}. " +
                       (fromOld > 0 ? $"{fromOld} urme provin din surse istorice (jurnale arhivate, Windows.old sau copii de umbră). " : "Nicio urmă din jurnale arhivate, Windows.old sau copii de umbră (nu au fost aduse sau nu conțin suportul). ") +
                       "Urme: " + string.Join("; ", traces.Take(20).Select(t => t.Text)) + (traces.Count > 20 ? $"; și încă {traces.Count - 20}." : ".");
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = HistoryRuleId,
                Title = $"Istoricul conectărilor suportului {m.Serial}: prima dovadă {first.WhenUtc:yyyy-MM-dd HH:mm} UTC",
                Severity = Severity.Info, Category = c.FindingCategory, Classification = Classification.Direct, Confidence = Confidence.Medium,
                FirstSeenUtc = first.WhenUtc, LastSeenUtc = traces[^1].WhenUtc, Description = desc,
                ClassificationReason = "Fiecare urmă vine direct dintr-o sursă (jurnal, registru); cea mai veche nu este neapărat prima conectare: sursele mai vechi pot lipsi.",
                SemanticType = SemanticType.Presence,
                SupportingEvidence = traces.Take(15).Select(t => Ref(t.Event, t.Text)).ToList(),
                AlternativeExplanations =
                [
                    "Cea mai veche urmă găsită nu este neapărat prima conectare: jurnalele mai vechi pot fi suprascrise, șterse sau neaduse.",
                    "Unele urme (instalarea driverului, proprietățile dispozitivului) se reînnoiesc la actualizări sau reinstalări, fără o conectare nouă.",
                    "Istoricul arată prezența suportului pe stație, nu cine l-a folosit și nici ce s-a copiat.",
                ],
                MissingEvidence = fromOld > 0 ? [] : ["Jurnalele arhivate (Archive-*.evtx), Windows.old\\Windows\\System32\\winevt și config sau copiile de umbră pot conține conectări mai vechi."],
            });
        }
    }
}
