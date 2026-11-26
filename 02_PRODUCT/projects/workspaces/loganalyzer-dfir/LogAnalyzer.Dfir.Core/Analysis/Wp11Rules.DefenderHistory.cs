using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    // ---------------------------------------------------------------- DEF-HISTORY-ONLY
    // Defender writes one file per detection in ...\Scans\History\Service\DetectionHistory and keeps it after the Microsoft-Windows-Windows Defender/Operational
    // log is cleared or rotated. A detection whose Detection ID is in a DetectionHistory file and in no 1116-1119 event of the collected log is a detection
    // the log no longer shows. The history keeps detections for weeks while the small default log rotates within days, so the severity follows the window:
    // High (T1070.001) only when the log was cleared (System 104 for the channel) at or after the detection, or the detection falls inside the time span the
    // collected log covers; Low when the detection is outside that span (rotation, or a log exported before it); Medium without a technique when the time is
    // unknown or the channel is absent from the case. A detection that IS in the log produces nothing here: DEF-DETECTION covers it.

    internal const string DefenderChannelName = "Microsoft-Windows-Windows Defender/Operational";

    /// <summary>"{2169D50A-…}" and "2169d50a-…" are the same id: no braces, upper case, trimmed.</summary>
    private static string DetectionKey(string id) => id.Trim().Trim('{', '}').Trim().ToUpperInvariant();

    internal enum DefenderHistoryCase { NotInCase, UnknownTime, ClearedAfter, ClearedBeforeOnly, InsideLog, BeforeLog, AfterLog }

    private sealed record DefenderLogView(bool InCase, DateTimeOffset? Oldest, DateTimeOffset? Newest, List<DateTimeOffset?> Clears);

    private static DefenderHistoryCase Assess(DefenderLogView v, DateTimeOffset? when)
    {
        if (!v.InCase) return DefenderHistoryCase.NotInCase;
        if (when is not { } w) return DefenderHistoryCase.UnknownTime;
        if (v.Clears.Any(t => t is null || t >= w)) return DefenderHistoryCase.ClearedAfter;
        if (v.Oldest is { } o && w < o) return DefenderHistoryCase.BeforeLog;
        if (v.Newest is { } n && w > n) return DefenderHistoryCase.AfterLog;
        // only clears before the detection and no event of the channel in the case: the log after the clear is not here
        return v.Oldest is null ? DefenderHistoryCase.ClearedBeforeOnly : DefenderHistoryCase.InsideLog;
    }

    private static bool Strong(DefenderHistoryCase k) => k is DefenderHistoryCase.ClearedAfter or DefenderHistoryCase.InsideLog;

    private static Severity SeverityOf(DefenderHistoryCase k) =>
        Strong(k) ? Severity.High : k is DefenderHistoryCase.BeforeLog or DefenderHistoryCase.AfterLog ? Severity.Low : Severity.Medium;

    private static void DefenderHistory(Ctx c, List<Finding> o)
    {
        var history = c.Events.Where(e => e.Source.Equals(DefenderDetectionHistoryParser.EventSource, StringComparison.OrdinalIgnoreCase) && DetectionKey(F(e, "Detection ID")).Length > 0).ToList();
        if (history.Count == 0) return;
        var channel = c.Events.Where(e => e.Source.Equals("EventLog:" + DefenderChannelName, StringComparison.OrdinalIgnoreCase)).ToList();
        // A cleared log can be collected empty: System 104 naming the channel proves the log was there and was emptied.
        var clears = c.Events.Where(e => Ev(e, "System", 104) && F(e, "Channel", "LogName").Equals(DefenderChannelName, StringComparison.OrdinalIgnoreCase)).Select(T).ToList();
        var times = channel.Select(T).Where(t => t is not null).ToList();
        var view = new DefenderLogView(channel.Count > 0 || clears.Count > 0, times.Count > 0 ? times.Min() : null, times.Count > 0 ? times.Max() : null, clears);
        var logged = c.Events.Where(e => Ev(e, DefenderChannelName, 1116, 1117, 1118, 1119)).Select(e => DetectionKey(F(e, "Detection ID"))).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);

        var items = new List<AdItem>();
        foreach (var g in history.GroupBy(e => DetectionKey(F(e, "Detection ID")), StringComparer.Ordinal))
        {
            if (logged.Contains(g.Key)) continue;
            var list = g.OrderBy(e => T(e) ?? DateTimeOffset.MaxValue).ToList();
            var first = list[0];
            var kind = Assess(view, T(first));
            var name = DefenderDetectionHistoryParser.Safe(F(first, "Threat Name"));
            items.Add(new AdItem(SeverityOf(kind), T(first), T(list[^1]), name.Length > 0 ? name : g.Key,
                Ref(first, "înregistrare DetectionHistory " + g.Key), () => MakeDefenderHistory(c, list, kind, view)));
        }
        // the Low summary of the detections not listed one by one carries no technique: T1070.001 only on the High findings themselves
        EmitCapped(c, o, items, "DEF-HISTORY-ONLY", "Defense Evasion", "", "detecție doar în istoric", "detecții doar în istoric");
    }

    private static Finding MakeDefenderHistory(Ctx c, List<TimelineEvent> list, DefenderHistoryCase kind, DefenderLogView view)
    {
        var e = list[0];
        string S(string key) => DefenderDetectionHistoryParser.Safe(F(e, key));
        string threat = S("Threat Name"), user = S("Detection User"), proc = S("Process Name"), sha = S("SHA256");
        string path = DefenderDetectionHistoryParser.Safe(F(e, "Path"));
        string file = DefenderDetectionHistoryParser.Safe(F(e, "Path").Split("; ")[0]);
        int us = file.IndexOf(":_", StringComparison.Ordinal);
        if (us >= 0) file = file[(us + 2)..];   // "file:_C:\x" -> "C:\x"
        var when = T(e);
        bool collected = kind != DefenderHistoryCase.NotInCase, strong = Strong(kind);

        var alt = new List<string>
        {
            "Rotirea normală a jurnalului Defender: dimensiunea maximă implicită este mică și evenimentele vechi se pierd fără nicio intervenție.",
            "Colectare parțială: evenimentele 1116 / 1117 ale acestei detecții pot exista în jurnal și să nu fi fost colectate.",
            "Detecția este mai veche decât fereastra de timp pe care o acoperă jurnalul colectat.",
        };
        var missing = new List<string> { "Evenimentul 1116 / 1117 cu același Detection ID (nu există în jurnalul colectat).", "Cine și când a golit sau a rotit jurnalul (Security 1102, System 104), dacă a fost golit." };
        if (!collected) missing.Insert(0, "Jurnalul Defender/Operational nu apare în caz (nu a fost colectat sau a fost colectat gol, fără nicio golire înregistrată): nu se poate spune dacă detecția lipsește din el.");
        else if (kind == DefenderHistoryCase.BeforeLog) missing.Add($"Jurnalul colectat începe la {Time(view.Oldest)}, după detecție: lipsa poate fi rotire normală.");
        else if (kind == DefenderHistoryCase.AfterLog) missing.Add($"Jurnalul colectat se termină la {Time(view.Newest)}, înainte de detecție: a fost exportat înaintea ei.");
        else if (kind == DefenderHistoryCase.ClearedBeforeOnly) missing.Add("Jurnalul Defender a fost golit înainte de detecție (System 104), iar evenimentele lui de după golire nu sunt în caz: colectați jurnalul ca să vedeți dacă detecția apare în el.");
        else if (kind == DefenderHistoryCase.UnknownTime) missing.Add("Ora detecției nu a putut fi citită din fișierul de istoric, deci nu se poate compara cu intervalul acoperit de jurnal.");

        string what = kind switch
        {
            DefenderHistoryCase.ClearedAfter => "Defender a înregistrat această detecție, iar jurnalul Defender a fost golit după ea (System 104); detecția nu mai apare în jurnal.",
            DefenderHistoryCase.InsideLog => "Defender a înregistrat această detecție, dar ea nu apare în jurnalul Defender colectat, deși jurnalul acoperă momentul detecției; înregistrarea a fost ștearsă sau jurnalul a fost alterat.",
            DefenderHistoryCase.BeforeLog => "Defender a înregistrat această detecție, dar jurnalul colectat începe după ea; lipsa ei din jurnal poate fi rotire normală.",
            DefenderHistoryCase.AfterLog => "Defender a înregistrat această detecție după momentul la care se termină jurnalul colectat; jurnalul a fost exportat înaintea ei.",
            DefenderHistoryCase.ClearedBeforeOnly => "Defender a înregistrat această detecție; jurnalul Defender a fost golit înainte de ea, iar evenimentele lui de după golire nu sunt în caz, deci nu se poate spune dacă detecția lipsește din el.",
            DefenderHistoryCase.UnknownTime =>"Defender a înregistrat această detecție, dar ea nu apare în jurnalul Defender colectat; ora ei nu se cunoaște, deci nu se poate spune dacă jurnalul ar fi trebuit s-o conțină.",
            _ => "Defender a înregistrat această detecție; jurnalul Defender nu apare în caz, deci nu se poate spune dacă detecția lipsește din el.",
        };
        return new Finding
        {
            FindingId = c.NextId(), RuleId = "DEF-HISTORY-ONLY",
            Title = threat.Length > 0 ? $"Detecție Defender {PlainPhrases.Q(threat)} în istoricul de detecții, fără eveniment în jurnalul Defender"
                                    : "Detecție Defender în istoricul de detecții, fără eveniment în jurnalul Defender",
            Severity = SeverityOf(kind), Category = "Defense Evasion",
            Classification = Classification.Candidate, Confidence = strong ? Confidence.Medium : Confidence.Low,
            MitreTechniqueId = strong ? "T1070.001" : "", SemanticType = SemanticType.Observation,
            FirstSeenUtc = when, LastSeenUtc = T(list[^1]), User = user, Process = proc.Length > 0 ? WinPath.GetFileName(proc) : "", File = file,
            PlainWhat = $"Defender (antivirusul Windows) a păstrat în istoricul lui o detecție{(threat.Length > 0 ? " " + PlainPhrases.Q(threat) : "")}{(file.Length > 0 ? " în " + PlainPhrases.Q(PlainPhrases.Leaf(file)) : "")}, dar jurnalul Defender nu o arată",
            Description = $"Fișierul DetectionHistory {PlainPhrases.Q(F(e, "Detection ID"))}: amenințarea {(threat.Length > 0 ? threat : "(fără nume)")}" +
                          (file.Length > 0 ? $", fișierul {file}" : "") + $", la {Time(when)}" + (user.Length > 0 ? $", utilizator {user}" : "") + (proc.Length > 0 ? $", proces {proc}" : "") +
                          (sha.Length > 0 ? $", SHA-256 {sha}" : "") + (path.Length > 0 && path != file ? $". Resurse: {path}" : "") + ". " +
                          "Ce s-a întâmplat: " + what + " " +
                          (collected ? "De ce contează: ștergerea sau golirea jurnalului Defender ascunde detecțiile; istoricul de detecții este un al doilea loc în care ele rămân. "
                                     : "De ce contează: detecția există în istoric și trebuie comparată cu jurnalul, care lipsește din caz. ") +
                          "Ce verificați: colectați jurnalul Defender/Operational, căutați golirea jurnalelor (Security 1102, System 104) și dacă fișierul mai există.",
            ClassificationReason = collected && kind != DefenderHistoryCase.ClearedBeforeOnly ? "Detection ID din DetectionHistory fără 1116-1119 cu același ID în jurnalul Defender colectat; severitatea urmează intervalul acoperit de jurnal și golirile lui."
                                             : "Detection ID din DetectionHistory; evenimentele jurnalului Defender din jurul detecției nu sunt în caz, deci nu există comparație.",
            SupportingEvidence = list.Take(10).Select(x => Ref(x, "înregistrare DetectionHistory")).ToList(),
            AlternativeExplanations = alt,
            MissingEvidence = missing,
            RecommendedNextSteps = ["Colectați și citiți jurnalul Defender/Operational (inclusiv arhivele) și căutați același Detection ID.", "Verificați golirea jurnalelor (Security 1102, System 104) în jurul orei detecției.", "Verificați dacă fișierul mai există și dacă rulează; căutați același hash pe alte stații."],
        };
    }
}
