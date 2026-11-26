using System.Text.RegularExpressions;
using System.Globalization;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>DETECTED: a trace was observed. NOT_DETECTED: the source that would show it was analysed and shows none. UNDETERMINED: it was not.</summary>
public enum AntiForensicResult { Detected, NotDetected, Undetermined }

public sealed record AntiForensicCheck(string Id, string Technique, string Attack, AntiForensicResult Result, string Reason, IReadOnlyList<EvidenceRef> Evidence, string Detail = "")
{
    /// <summary>What happened, in plain Romanian without codes (owner 2026-11-06); empty when the check has no sentence of its own yet.</summary>
    public string Plain { get; init; } = "";

    /// <summary>Reader-facing result (DETECTAT / NEDETECTAT / NEDETERMINAT); <see cref="ResultText"/> keeps the spec code for data and the legend.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string ResultRomanian => LogAnalyzer.Dfir.Language.RoLabels.AntiForensic(Result);
    public string ResultText => Result switch { AntiForensicResult.Detected => "DETECTED", AntiForensicResult.NotDetected => "NOT_DETECTED", _ => "UNDETERMINED" };
}

/// <summary>
/// Anti-forensics lab (master spec §23). Each technique is checked only against what the parsed evidence can show. A trace is
/// reported as observed, not as malicious intent: deleting a service or a task, changing the clock or an audit setting can be
/// legitimate; the evidence says what happened, the analyst decides why. Nothing here ever says "clean".
/// Event semantics used below were checked against the providers' own message texts on Windows 11:
/// Kernel-General 1 "Change Reason" 1 = an application or system component changed the time, 2 = synchronized with the
/// hardware clock, 3 = adjusted to a new time zone; Security 4719 AuditPolicyChanges %%8448 = Success removed,
/// %%8449 = Success added, %%8450 = Failure removed, %%8451 = Failure added; Firewall 2059 = all rules deleted,
/// 2097/2099 = rule added/modified.
/// </summary>
public static class AntiForensics
{
    /// <summary>Text the EVTX parser puts in the gap of a file whose chunks failed their checksums.</summary>
    public const string CorruptChunksMarker = "chunk-uri corupte eliminate";
    /// <summary>Text the EVTX parser puts in the gap of a file shorter than its header says.</summary>
    public const string TruncatedMarker = "EVTX trunchiat";
    /// <summary>Text the EVTX parser puts in the gap of a file whose RecordID sequence goes back (IDs used twice).</summary>
    public const string RecordIdReuseMarker = "RecordID reutilizat";

    private const string Security = "EventLog:Security", System = "EventLog:System";
    private const string Firewall = "EventLog:Microsoft-Windows-Windows Firewall With Advanced Security/Firewall";
    private const string TaskOps = "EventLog:Microsoft-Windows-TaskScheduler/Operational";

    /// <summary>Utilities whose renaming ATT&amp;CK lists under T1036.003 (Rename System Utilities), by their PE OriginalFileName.</summary>
    /// <summary>
    /// The USN sentence of an AF08 "undetermined" text: its own sentence, capital letter, full stop. When there is no journal and the reason already says so
    /// (<paramref name="why"/> names the journal), nothing is added, so the same cause is never stated twice.
    /// </summary>
    internal static string UsnSentence(int usnCount, string why)
    {
        if (usnCount > 0) return $"Jurnalul USN a fost analizat ({LogAnalyzer.Dfir.Language.RoText.Count(usnCount, "înregistrare", "înregistrări")}).";
        if (why.Contains("jurnalul USN", StringComparison.OrdinalIgnoreCase)) return "";
        return "Jurnalul USN nu a fost analizat: lipsește din caz (nu a fost colectat sau importat), deci o modificare urmată de redenumire sau mutare nu se poate verifica.";
    }

    private static readonly HashSet<string> SystemUtilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.dll", "rundll32.exe", "regsvr32.exe", "mshta.exe", "wscript.exe", "cscript.exe", "certutil.exe",
        "bitsadmin.exe", "msbuild.exe", "installutil.exe", "regasm.exe", "regsvcs.exe", "schtasks.exe", "wmic.exe", "net.exe", "net1.exe",
        "sc.exe", "reg.exe", "vssadmin.exe", "wevtutil.exe", "nltest.exe", "whoami.exe", "msiexec.exe", "bcdedit.exe", "psexec.c",
    };

    /// <summary>
    /// Where Windows keeps its own core processes (the "Hunt Evil" locations). A process with one of these names elsewhere
    /// matches T1036.005. Paths are compared after removing the volume and drive.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExpectedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["svchost.exe"] = [@"\WINDOWS\SYSTEM32", @"\WINDOWS\SYSWOW64"],
        ["lsass.exe"] = [@"\WINDOWS\SYSTEM32"], ["csrss.exe"] = [@"\WINDOWS\SYSTEM32"], ["smss.exe"] = [@"\WINDOWS\SYSTEM32"],
        ["wininit.exe"] = [@"\WINDOWS\SYSTEM32"], ["services.exe"] = [@"\WINDOWS\SYSTEM32"], ["winlogon.exe"] = [@"\WINDOWS\SYSTEM32"],
        ["lsaiso.exe"] = [@"\WINDOWS\SYSTEM32"], ["spoolsv.exe"] = [@"\WINDOWS\SYSTEM32"],
        ["taskhostw.exe"] = [@"\WINDOWS\SYSTEM32"], ["conhost.exe"] = [@"\WINDOWS\SYSTEM32"],
        ["dllhost.exe"] = [@"\WINDOWS\SYSTEM32", @"\WINDOWS\SYSWOW64"], ["rundll32.exe"] = [@"\WINDOWS\SYSTEM32", @"\WINDOWS\SYSWOW64"],
        ["cmd.exe"] = [@"\WINDOWS\SYSTEM32", @"\WINDOWS\SYSWOW64"],
        ["powershell.exe"] = [@"\WINDOWS\SYSTEM32\WINDOWSPOWERSHELL\V1.0", @"\WINDOWS\SYSWOW64\WINDOWSPOWERSHELL\V1.0"],
        ["explorer.exe"] = [@"\WINDOWS", @"\WINDOWS\SYSWOW64"],
    };

    /// <summary>Component-store copies of the same binaries are not masquerading.</summary>
    private static readonly string[] ComponentStores = [@"\WINDOWS\WINSXS\", @"\WINDOWS\SERVICING\", @"\WINDOWS\SOFTWAREDISTRIBUTION\"];

    /// <param name="maintenancePolicy">Approved log-clearing accounts and windows (procedure profile); <c>null</c> = not defined yet.</param>
    /// <param name="otherHighFindingTimesUtc">Times of other High/Critical findings in the case, used to corroborate a log clear.</param>
    public static List<AntiForensicCheck> Evaluate(IReadOnlyList<TimelineEvent> events, IReadOnlyList<EvidenceGap> gaps,
        LogMaintenancePolicy? maintenancePolicy = null, IReadOnlyList<DateTimeOffset>? otherHighFindingTimesUtc = null)
    {
        var sources = events.Select(e => e.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Has(string s) => sources.Contains(s);
        bool Ev(TimelineEvent e, string source, params string[] ids) => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && ids.Contains(e.EventId);
        string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
        EvidenceRef Ref(TimelineEvent e, string d) => new(e.EvidenceId, e.Locator, d, e.SourceSha256);
        var evtxSources = events.Where(e => e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase)).ToList();
        var checks = new List<AntiForensicCheck>();
        void Add(string id, string technique, string attack, AntiForensicResult r, string reason, IEnumerable<EvidenceRef>? ev = null, string detail = "") =>
            checks.Add(new AntiForensicCheck(id, technique, attack, r, reason, (ev ?? []).Take(200).ToList(), detail));
        string Missing(params string[] s) => "lipsește " + string.Join(" și ", s.Where(x => !Has(x)).Select(x => x["EventLog:".Length..]));

        // AF01 — EVTX cleared: Security 1102 / System 104, written by the Eventlog service itself.
        {
            var hits = events.Where(e => e.Provider.Equals("Microsoft-Windows-Eventlog", StringComparison.OrdinalIgnoreCase)
                                         && (Ev(e, Security, "1102") || Ev(e, System, "104"))).ToList();
            if (hits.Count > 0)
            {
                // Detected stays: the clear was observed. Whether it was planned is the lifecycle, reported next to it, never assumed.
                var assessed = LogClearAssessment.Assess(
                    hits.Where(e => e.Time.Utc is not null).Select(e => new LogClearEvent(e.EventId == "1102" ? "Security" : F(e, "Channel"), e.Time.Utc!.Value, F(e, "SubjectUserName"), F(e, "SubjectDomainName"))).ToList(),
                    maintenancePolicy, otherHighFindingTimesUtc ?? []);
                string lifecycle = assessed.Count == 0 ? "" : " Ciclu de viață: " + string.Join(" ", assessed.Select(a => a.Reason).Distinct().Take(5));
                Add("AF01", "Jurnal EVTX golit", "T1070.001", AntiForensicResult.Detected,
                    $"{hits.Count} goliri: " + string.Join("; ", hits.Take(10).Select(e => $"{e.Time.UtcIso} {(e.EventId == "1102" ? "Security" : F(e, "Channel"))} de {F(e, "SubjectDomainName")}\\{F(e, "SubjectUserName")}")) + "." + lifecycle,
                    hits.Select(e => Ref(e, "eveniment de golire a jurnalului")));
            }
            else if (Has(Security) && Has(System))
                Add("AF01", "Jurnal EVTX golit", "T1070.001", AntiForensicResult.NotDetected, "Security și System analizate: niciun 1102 / 104.");
            else Add("AF01", "Jurnal EVTX golit", "T1070.001", AntiForensicResult.Undetermined, Missing(Security, System) + ": golirea nu poate fi verificată.");
        }

        // AF02 / AF03 — EVTX corrupted / truncated, as found by the EVTX parser's checks.
        foreach (var (id, technique, marker) in new[] { ("AF02", "Jurnal EVTX corupt", CorruptChunksMarker), ("AF03", "Jurnal EVTX trunchiat", TruncatedMarker) })
        {
            var hits = gaps.Where(g => g.Reason.Contains(marker, StringComparison.Ordinal) || g.Artifact.Contains(marker, StringComparison.Ordinal)).ToList();
            if (hits.Count > 0) Add(id, technique, "T1070.001", AntiForensicResult.Detected, string.Join("; ", hits.Select(g => $"{g.Artifact}: {g.Reason}")));
            else if (evtxSources.Count > 0) Add(id, technique, "T1070.001", AntiForensicResult.NotDetected, "Toate fișierele EVTX parsate au trecut verificarea structurii (antet, chunk-uri, CRC).");
            else Add(id, technique, "T1070.001", AntiForensicResult.Undetermined, "Niciun fișier EVTX analizat.");
        }

        // AF04 — RecordID gaps inside one EVTX file (records removed from the middle of a log) and RecordIDs used twice.
        {
            var holes = new List<string>();
            var refs = new List<EvidenceRef>();
            var unclean = events.Where(e => Ev(e, System, "6008") || e.Source.Equals(System, StringComparison.OrdinalIgnoreCase) && e.EventId == "41"
                                            && e.Provider.Equals("Microsoft-Windows-Kernel-Power", StringComparison.OrdinalIgnoreCase)).ToList();
            static long RecordId(TimelineEvent e) =>
                long.TryParse(e.Locator.Replace("EventRecordID=", "").Split(';')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
            foreach (var g in evtxSources.GroupBy(e => (e.EvidenceId, e.Source)))
            {
                // In file order: a step back means IDs were issued twice.
                var inOrder = g.Where(e => RecordId(e) >= 0).ToList();
                for (int i = 1; i < inOrder.Count; i++)
                    if (RecordId(inOrder[i]) <= RecordId(inOrder[i - 1]))
                    {
                        var at = inOrder[i].Time.Utc;
                        var crash = unclean.Where(u => u.Time.Utc is { } t && at is { } a && t >= a.AddMinutes(-10) && t <= a.AddMinutes(2)).ToList();
                        holes.Add($"{g.Key.Source["EventLog:".Length..]}: RecordID reutilizat — după {RecordId(inOrder[i - 1])} ({inOrder[i - 1].Time.UtcIso}) urmează {RecordId(inOrder[i])} ({inOrder[i].Time.UtcIso})" +
                                  (crash.Count > 0 ? $"; coincide cu o oprire necurată ({string.Join(", ", crash.Select(c => $"System {c.EventId} {c.Time.UtcIso}"))})" : "; nicio oprire necurată înregistrată în System în jurul acestei ore"));
                        refs.Add(Ref(inOrder[i - 1], "ultima înregistrare înainte de reluarea numerotării"));
                        refs.Add(Ref(inOrder[i], "prima înregistrare cu un ID deja folosit"));
                        refs.AddRange(crash.Select(c => Ref(c, "oprire necurată")));
                    }
                var ids = inOrder.Select(e => (Id: RecordId(e), Event: e)).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToList();
                for (int i = 1; i < ids.Count; i++)
                    if (ids[i].Id > ids[i - 1].Id + 1)
                    {
                        holes.Add($"{g.Key.Source["EventLog:".Length..]}: lipsesc RecordID {ids[i - 1].Id + 1}–{ids[i].Id - 1} ({ids[i - 1].Event.Time.UtcIso} → {ids[i].Event.Time.UtcIso})");
                        refs.Add(Ref(ids[i - 1].Event, "ultima înregistrare înainte de gol"));
                        refs.Add(Ref(ids[i].Event, "prima înregistrare după gol"));
                    }
            }
            if (holes.Count > 0)
                Add("AF04", "Goluri în RecordID", "T1070.001", AntiForensicResult.Detected,
                    $"{LogAnalyzer.Dfir.Language.RoText.Count(holes.Count, "anomalie", "anomalii")}" + (holes.Count > 3 ? $"; primele 3: " : ": ") + string.Join("; ", holes.Take(3)) +
                    (holes.Count > 3 ? $"; lista completă ({holes.Count}) este în Analysis/anti_forensics.json, câmpul Detail" : "") +
                    (gaps.Any(x => x.Reason.Contains(CorruptChunksMarker, StringComparison.Ordinal)) ? ". Unele pot proveni din chunk-urile corupte eliminate (AF02)." : ""), refs, string.Join("\n", holes));
            else if (evtxSources.Count > 0) Add("AF04", "Goluri în RecordID", "T1070.001", AntiForensicResult.NotDetected, "RecordID continuu și fără repetări în fiecare fișier EVTX analizat.");
            else Add("AF04", "Goluri în RecordID", "T1070.001", AntiForensicResult.Undetermined, "Niciun fișier EVTX analizat.");
        }

        // AF05 — system clock changed by something other than time synchronization (W32Time runs in svchost.exe). WP15b: magnitude and direction of every
        // jump, time-zone changes and record-order inversions (RecordID grows while the time goes back) come from TimeManipulation, the same source the
        // TEMPORAL check of the verification layer uses for its unreliable-time windows.
        {
            var tm = TimeManipulation.Analyze(events);
            // One filter for AF05 and for TimeReliability, so the report never says the clock was changed while AF05 says it was not.
            var manual = events.Where(TimeReliability.IsClockChange).ToList();
            string Mag(TimelineEvent e) => tm.Jumps.FirstOrDefault(j => j.Evidence.EvidenceId == e.EvidenceId && j.Evidence.Locator == e.Locator && j.EventId == e.EventId) is { Magnitude.Length: > 0 } j ? $" [{j.Magnitude}]" : "";
            string zones = tm.ZoneChanges.Count == 0 ? "" : $" Schimbări de fus orar: {tm.ZoneChanges.Count} ({string.Join("; ", tm.ZoneChanges.Take(5).Select(z => $"{z.TimeUtc:yyyy-MM-dd HH:mm} UTC {z.Detail}"))}); nu sunt, singure, manipulare.";
            string inversions = tm.Inversions.Count == 0 ? "" : $" Ordinea înregistrărilor contrazice ora: {tm.Inversions.Count} ({string.Join("; ", tm.Inversions.Take(5).Select(v => $"{v.Source["EventLog:".Length..]} RecordID {v.PreviousRecordId} → {v.RecordId}, ora înapoi cu {TimeManipulation.Human(v.Back)}"))}).";
            var refs = manual.Select(e => Ref(e, "schimbare a orei sistemului")).Concat(tm.Inversions.Select(v => v.Evidence)).ToList();
            if (manual.Count > 0 || tm.Inversions.Count > 0)
            {
                Add("AF05", "Manipularea orei sistemului", "", AntiForensicResult.Detected,
                    (manual.Count > 0
                        ? $"{manual.Count(e => e.EventId == "4616")} în Security 4616 și {manual.Count(e => e.EventId == "1")} în Kernel-General 1 (aceeași schimbare apare în ambele jurnale), făcute de alt proces decât sincronizarea (svchost): " +
                          string.Join("; ", manual.Take(10).Select(e => e.EventId == "4616"
                              ? $"4616 {F(e, "PreviousTime")} → {F(e, "NewTime")}{Mag(e)} de {F(e, "SubjectUserName")} ({WinPath.GetFileName(F(e, "ProcessName"))})"
                              : $"Kernel-General {F(e, "OldTime")} → {F(e, "NewTime")}{Mag(e)} ({WinPath.GetFileName(F(e, "ProcessName"))})")) + "."
                        : "Nicio schimbare manuală a ceasului în Security 4616 / Kernel-General 1.") + inversions + zones,
                    refs);
                var jumps = manual.Select(e => tm.Jumps.FirstOrDefault(j => j.Evidence.EvidenceId == e.EvidenceId && j.Evidence.Locator == e.Locator && j.EventId == e.EventId)).OfType<ClockJump>().ToList();
                checks[^1] = checks[^1] with { Plain = ClockPlain(jumps, tm.Inversions.Count, TimeZoneInfo.Local) };
            }
            else if (Has(System))
                Add("AF05", "Manipularea orei sistemului", "", AntiForensicResult.NotDetected,
                    "System analizat: schimbările de oră (Kernel-General 1) sunt doar sincronizări, ceasul hardware sau fusul orar." + zones);
            else Add("AF05", "Manipularea orei sistemului", "", AntiForensicResult.Undetermined, "Lipsește jurnalul System (Kernel-General 1).");
        }

        // AF06 — Prefetch disabled (registry) or .pf files deleted (change journal).
        var usn = events.Where(e => e.Source == "USN").ToList();
        string Window() => usn.Count == 0 ? "" : $"{usn.Min(e => e.Time.Utc):yyyy-MM-dd HH:mm} – {usn.Max(e => e.Time.Utc):yyyy-MM-dd HH:mm} UTC";
        {
            var cfg = events.Where(e => e.Source == "SystemConfig" && e.Fields.ContainsKey("EnablePrefetcher")).ToList();
            var off = cfg.Where(e => F(e, "EnablePrefetcher") == "0").ToList();
            var pfDeleted = usn.Where(e => F(e, "FileName").EndsWith(".pf", StringComparison.OrdinalIgnoreCase) && F(e, "Reason").Contains("File delete", StringComparison.Ordinal)).ToList();
            var parts = new List<string>();
            if (off.Count > 0) parts.Add("EnablePrefetcher = 0: Windows nu mai creează fișiere Prefetch");
            if (pfDeleted.Count > 0)
            {
                // Recreated later under the same name = rewritten by Windows; a burst of distinct names in one second is a bulk deletion.
                var creates = usn.Where(c => F(c, "Reason").Contains("File create", StringComparison.Ordinal) && c.Time.Utc is not null)
                                 .GroupBy(c => F(c, "FileName"), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Max(c => c.Time.Utc!.Value), StringComparer.OrdinalIgnoreCase);
                var recreated = pfDeleted.Count(d => creates.TryGetValue(F(d, "FileName"), out var last) && d.Time.Utc is { } dt && last > dt);
                var burst = pfDeleted.GroupBy(e => e.Time.Utc).OrderByDescending(g => g.Count()).First();
                parts.Add($"{LogAnalyzer.Dfir.Language.RoText.Count(pfDeleted.Count, "fișier", "fișiere")} .pf șterse (jurnal USN), din care {recreated} recreate ulterior cu același nume; " +
                          $"cel mai mare grup: {burst.Count()} în aceeași secundă, {burst.Key:yyyy-MM-ddTHH:mm:ssZ}. Jurnalul USN nu arată procesul care a șters.");
            }
            if (parts.Count > 0)
                Add("AF06", "Prefetch dezactivat / șters", "T1070.004", AntiForensicResult.Detected, string.Join(". ", parts),
                    off.Select(e => Ref(e, "EnablePrefetcher")).Concat(pfDeleted.Select(e => Ref(e, "fișier Prefetch șters"))));
            else if (usn.Count > 0)
                Add("AF06", "Prefetch dezactivat / șters", "T1070.004", AntiForensicResult.NotDetected,
                    $"Jurnalul USN ({Window()}) nu conține ștergeri de fișiere .pf" + (cfg.Count > 0 ? $"; Prefetch activ (EnablePrefetcher = {F(cfg[0], "EnablePrefetcher")})." : ". Ce s-a întâmplat înaintea jurnalului nu se vede."),
                    cfg.Select(e => Ref(e, "EnablePrefetcher")));
            else
                Add("AF06", "Prefetch dezactivat / șters", "T1070.004", AntiForensicResult.Undetermined,
                    (cfg.Count > 0 ? $"Prefetch activ (EnablePrefetcher = {F(cfg[0], "EnablePrefetcher")}). " : "Configurația Prefetch (hive SYSTEM) nu a fost analizată. ") +
                    "Ștergerea fișierelor .pf se vede doar în jurnalul USN, care nu a fost analizat.", cfg.Select(e => Ref(e, "EnablePrefetcher")));
        }

        // AF07 — the change journal absent on the system volume, or deleted and recreated: its ID (a FILETIME) is the creation time.
        // Three different answers: the volume has no journal (Windows creates one on the system volume by default), the journal was created long after the
        // oldest event-log record (recreated, or a major repair), and nothing wrong in an analysed journal. No journal analysed and none reported absent = undetermined.
        {
            const string Af07Title = "Anomalii în jurnalul USN";
            var ids = usn.Select(e => F(e, "JournalId")).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var created = ids.Select(i => (Id: i, At: LogAnalyzer.Dfir.FileSystem.UsnJournalParser.JournalCreatedUtc(i))).ToList();
            // Anchor = the 1st percentile of the event-log times, not the minimum: one record written with a wrong clock must not move it
            // (with 3 or more records at least the earliest one is set aside). A wrong clock or rotated logs still shift the anchor, and the text says so.
            var evtxTimes = evtxSources.Where(e => e.Time.Utc is not null).Select(e => e.Time.Utc!.Value).OrderBy(t => t).ToList();
            var earliestLog = evtxTimes.Count == 0 ? default : evtxTimes[evtxTimes.Count < 3 ? 0 : Math.Max(1, evtxTimes.Count / 100)];
            // The collector's text when the volume has no active journal (Win32 error 1179): "jurnalul USN C: nu a putut fi citit (volumul nu are jurnal USN activ ...)".
            var absent = gaps.Select(g => g.Artifact + " " + g.Reason).Where(t => t.Contains("nu are jurnal USN activ", StringComparison.OrdinalIgnoreCase)).ToList();
            string absentLetter = absent.Select(t => Regex.Match(t, @"jurnalul USN ([A-Za-z]):")).Where(m => m.Success).Select(m => m.Groups[1].Value.ToUpperInvariant()).FirstOrDefault() ?? "";
            // It is the system volume when the text says so, or when the case shows processes or files running from <letter>:\Windows.
            bool IsSystemVolume() => absent.Any(t => t.Contains("volumul de sistem", StringComparison.OrdinalIgnoreCase)) ||
                absentLetter.Length > 0 && events.Any(e => new[] { e.Path, e.Process, F(e, "NewProcessName"), F(e, "Image"), F(e, "ProcessName"), F(e, "CommandLine") }
                    .Any(p => p.TrimStart('"', ' ').StartsWith(absentLetter + @":\Windows\", StringComparison.OrdinalIgnoreCase)));
            var tolerance = TimeSpan.FromHours(24);
            if (absent.Count > 0 && IsSystemVolume())
                Add("AF07", Af07Title, "T1070.004", AntiForensicResult.Detected,
                    $"Gravitate Medie: volumul de sistem nu are jurnal USN activ ({absentLetter}:); Windows îl creează implicit pe volumul de sistem, deci jurnalul a fost oprit sau șters (fsutil usn deletejournal) sau volumul a fost configurat altfel. De verificat: " +
                    "un volum de sistem fără jurnal poate fi și o imagine pregătită special; citirea jurnalului a returnat eroarea „volumul nu are jurnal USN activ”.")
                    ;
            else if (ids.Count == 0)
                Add("AF07", Af07Title, "T1070.004", AntiForensicResult.Undetermined,
                    absent.Count > 0
                        ? $"Jurnalul USN{(absentLetter.Length > 0 ? " " + absentLetter + ":" : "")} nu este activ, dar nu se știe dacă este volumul de sistem (nu există în probe niciun proces sau fișier din {(absentLetter.Length > 0 ? absentLetter + ":" : "acel volum")}\\Windows): fără acest lucru, lipsa jurnalului nu se poate judeca."
                        : "Jurnalul USN nu a fost analizat (export fsutil usn readjournal).");
            else if (ids.Count > 1)
                Add("AF07", Af07Title, "T1070.004", AntiForensicResult.Detected,
                    "Exporturi cu ID-uri de jurnal diferite (jurnal șters și recreat între ele): " + string.Join("; ", created.Select(c => $"{c.Id} creat {c.At:yyyy-MM-ddTHH:mm:ssZ}")),
                    usn.GroupBy(e => F(e, "JournalId")).Select(g => Ref(g.First(), "ID jurnal")));
            else if (created[0].At is { } at && earliestLog != default && at - earliestLog > tolerance)
                Add("AF07", Af07Title, "T1070.004", AntiForensicResult.Detected,
                    $"Gravitate Medie, De verificat: jurnalul USN {ids[0]} a fost creat la {at:yyyy-MM-ddTHH:mm:ssZ}, la peste {tolerance.TotalHours:0} de ore după începutul jurnalelor de evenimente ({earliestLog:yyyy-MM-ddTHH:mm:ssZ}, percentila 1 a orelor înregistrărilor): " +
                    "a fost șters și recreat (sau volumul este nou). Ancora depinde de jurnalele de evenimente: un ceas greșit sau jurnale EVTX rotite (cele vechi șterse de Windows) o mută. Toleranță: un jurnal poate fi recreat și de Windows după un chkdsk sau o actualizare majoră, fără nicio intenție de ascundere.",
                    [Ref(usn[0], "ID jurnal")]);
            else
                Add("AF07", Af07Title, "T1070.004", AntiForensicResult.NotDetected,
                    $"Un singur jurnal, {ids[0]}, creat la {created[0].At:yyyy-MM-ddTHH:mm:ssZ}" +
                    (earliestLog != default ? $", fără mai mult de {tolerance.TotalHours:0} de ore după începutul jurnalelor de evenimente ({earliestLog:yyyy-MM-ddTHH:mm:ssZ}, percentila 1; un ceas greșit sau jurnale EVTX rotite mută această ancoră)" : "") +
                    $". Acoperire: {Window()}. Limită: ora formatării sau a instalării volumului nu este în probe, deci un jurnal recreat imediat după instalare nu se vede.", [Ref(usn[0], "ID jurnal")]);
        }

        // AF08 — timestomp: the NTFS-TIMESTOMP test over $MFT (+ USN), the same one the finding uses (NtfsTimestomp, calibrated on Corpus/NtfsCalib).
        {
            const string Af08Title = "Modificarea marcajelor de timp ale fișierelor (timestomp)";
            const string Limit = "Limite: uneltele care schimbă și $FILE_NAME sau și ora de schimbare a înregistrării nu se văd; fără jurnal USN, o schimbare urmată de redenumire sau mutare nu se vede; " +
                                 "tunelarea NTFS (cu nume scurte 8.3 pe volum, un fișier nou cu numele unuia șters sau redenumit cu puțin înainte moștenește ora lui de creare) produce același tipar și se exclude doar când jurnalul USN arată numele eliberat de alt fișier.";
            var mftEvents = events.Where(e => e.Source == "MFT").ToList();
            var outcome = NtfsTimestomp.Analyze(events);
            var hits = outcome.Hits;
            int possibleTunnel = hits.Count(h => h.Level == NtfsTimestomp.Level.Low);   // only the $MFT and a short name: possible tunnelling, still worth a look
            string tunnelNote = outcome.TunnelExplained > 0 || possibleTunnel > 0
                ? $" Tunelare NTFS: {outcome.TunnelExplained} fișiere explicate prin tunelare (jurnalul USN arată numele eliberat de alt fișier chiar înainte), {possibleTunnel} doar din $MFT cu nume scurt 8.3 (posibil tunelare, neconfirmat)."
                : "";
            long sysTrees = NtfsTimestomp.Sum(mftEvents, "MismatchSystemTrees"), dirsOnly = NtfsTimestomp.Sum(mftEvents, "MismatchDirectories");
            string countedNote = sysTrees + dirsOnly > 0
                ? $"{sysTrees:N0} nepotriviri din arborii gestionați de sistem și {dirsOnly:N0} din directoare au fost doar numărate (tiparul normal al instalării și actualizărilor); acolo doar calea cu jurnalul USN poate detecta; "
                : "";
            if (hits.Count == 0 && !NtfsTimestomp.HasMft(mftEvents))
                Add("AF08", Af08Title, "T1070.006", AntiForensicResult.Undetermined,
                    "$MFT nu este parsat: comparația $STANDARD_INFORMATION / $FILE_NAME nu se poate face." +
                    (usn.Count > 0 ? " Motivul USN „Basic info change” apare și la schimbarea atributelor, deci nu dovedește singur o modificare a orelor." : ""));
            else
            {
                long records = NtfsTimestomp.Sum(mftEvents, "TotalRecords");
                string usnPart = usn.Count > 0 ? $"jurnalul USN a fost analizat ({LogAnalyzer.Dfir.Language.RoText.Count(usn.Count, "înregistrare", "înregistrări")})" : "fără jurnal USN";
                if (hits.Count > 0)
                {
                    int high = hits.Count(h => h.Level == NtfsTimestomp.Level.High), medium = hits.Count - high - possibleTunnel;
                    Add("AF08", Af08Title, "T1070.006", AntiForensicResult.Detected,
                        $"{hits.Count} fișiere cu orele de creare care par schimbate după crearea fișierului ({high} confirmate de jurnalul USN, {medium} doar din $MFT, {possibleTunnel} doar din $MFT cu nume scurt 8.3 = posibil tunelare; {records:N0} înregistrări $MFT, {usnPart}{(countedNote.Length > 0 ? "; " + countedNote.TrimEnd(' ', ';') : "")}): " +
                        string.Join("; ", hits.Take(5).Select(h => $"{LogAnalyzer.Dfir.FileSystem.MftParser.SafeText(h.Path)} ({(h.Level == NtfsTimestomp.Level.High ? "USN" : h.Level == NtfsTimestomp.Level.Low ? "doar $MFT, posibil tunelare" : "doar $MFT")})")) + (hits.Count > 5 ? $" și încă {hits.Count - 5}" : "") + "." + tunnelNote + " " + Limit,
                        hits.SelectMany(h => new[] { h.Mft, h.UsnCreate, h.UsnChange }.Where(e => e is not null).Select(e => Ref(e!, e!.Source == "MFT" ? "înregistrare $MFT: orele $SI / $FN" : "jurnal USN"))));
                }
                else if (NtfsTimestomp.Undetermined(mftEvents, usn) is { } why)
                    Add("AF08", Af08Title, "T1070.006", AntiForensicResult.Undetermined, $"$MFT analizat ({records:N0} înregistrări), dar {why}.{(UsnSentence(usn.Count, why) is { Length: > 0 } us ? " " + us : "")}{tunnelNote} " + (countedNote.Length > 0 ? countedNote.TrimEnd(' ', ';') + ". " : "") + Limit);
                else
                    Add("AF08", Af08Title, "T1070.006", AntiForensicResult.NotDetected,
                        $"Verificat: {records:N0} înregistrări $MFT ({NtfsTimestomp.Sum(mftEvents, "MismatchTotal"):N0} cu nepotrivire $SI/$FN, {NtfsTimestomp.Sum(mftEvents, "RecentTotal"):N0} recente), {usnPart}; " + countedNote +
                        "niciun fișier cu orele de creare mai vechi decât crearea fără tiparul unei copieri." + tunnelNote + " " + Limit);
            }
        }

        // AF09 — registry changes to logging and audit configuration, visible only with registry auditing (Sysmon 12–14, Security 4657).
        {
            string[] keys = [@"\SERVICES\EVENTLOG", @"\WINEVT\CHANNELS", @"\CONTROL\LSA\AUDIT", @"\POLICIES\MICROSOFT\WINDOWS\EVENTLOG"];
            var regAudit = events.Where(e => Ev(e, Security, "4657") || e.Source.Equals("EventLog:Microsoft-Windows-Sysmon/Operational", StringComparison.OrdinalIgnoreCase) && e.EventId is "12" or "13" or "14").ToList();
            var hits = regAudit.Where(e => keys.Any(k => (F(e, "TargetObject") + F(e, "ObjectName")).ToUpperInvariant().Contains(k))).ToList();
            if (hits.Count > 0)
                Add("AF09", "Modificări în registru ale jurnalizării", "T1112", AntiForensicResult.Detected,
                    string.Join("; ", hits.Take(10).Select(e => $"{e.Time.UtcIso} {F(e, "TargetObject")}{F(e, "ObjectName")}")), hits.Select(e => Ref(e, "modificare de registru")));
            else if (regAudit.Count > 0)
                Add("AF09", "Modificări în registru ale jurnalizării", "T1112", AntiForensicResult.NotDetected, $"{LogAnalyzer.Dfir.Language.RoText.Count(regAudit.Count, "eveniment", "evenimente")} de audit al registrului, niciunul pe cheile de jurnalizare.");
            else Add("AF09", "Modificări în registru ale jurnalizării", "T1112", AntiForensicResult.Undetermined, "Nu există audit al registrului (Sysmon 12–14 sau Security 4657).");
        }

        // AF10 — logging policy weakened: audit removed (4719 %%8448/%%8450), Eventlog service start type changed or disabled.
        {
            var removed = events.Where(e => Ev(e, Security, "4719") && (F(e, "AuditPolicyChanges").Contains("%%8448") || F(e, "AuditPolicyChanges").Contains("%%8450"))).ToList();
            var svc = events.Where(e => Ev(e, System, "7040") && F(e, "param4").Equals("EventLog", StringComparison.OrdinalIgnoreCase)).ToList();
            var disabled = events.Where(e => e.Source == "Service" && e.Service.Equals("EventLog", StringComparison.OrdinalIgnoreCase) && F(e, "StartValue") == "4").ToList();
            var all = removed.Concat(svc).Concat(disabled).ToList();
            if (all.Count > 0)
            {
                string Sub(TimelineEvent e) => Guid.TryParse(F(e, "SubcategoryGuid").Trim('{', '}'), out var g) && AuditSubcategories.ByName.FirstOrDefault(x => x.Value == g).Key is { } n ? n : F(e, "SubcategoryGuid");
                string Change(string c) => c.Replace("%%8448", "Success removed").Replace("%%8449", "Success added").Replace("%%8450", "Failure removed").Replace("%%8451", "Failure added");
                var parts = new List<string>();
                if (removed.Count > 0)
                    parts.Add($"{removed.Count} eliminări din politica de audit (4719): " + string.Join("; ", removed.GroupBy(Sub).Take(15)
                        .Select(g => $"{g.Key} ×{g.Count()} ({Change(F(g.First(), "AuditPolicyChanges"))}, de {F(g.First(), "SubjectUserName")}, prima {g.Min(x => x.Time.UtcIso)})")));
                if (svc.Count > 0) parts.Add($"serviciul EventLog: modul de pornire schimbat de {LogAnalyzer.Dfir.Language.RoText.Times(svc.Count)} ({string.Join("; ", svc.Take(5).Select(e => $"{F(e, "param2")} → {F(e, "param3")}"))})");
                if (disabled.Count > 0) parts.Add("serviciul EventLog este dezactivat (Start = 4) în hive-ul SYSTEM");
                Add("AF10", "Slăbirea politicii de jurnalizare", "T1562.002", AntiForensicResult.Detected, string.Join(". ", parts), all.Select(e => Ref(e, "schimbare a jurnalizării")));
            }
            else
                Add("AF10", "Slăbirea politicii de jurnalizare", "T1562.002", AntiForensicResult.Undetermined,
                    "Nicio eliminare de audit observată, dar 4719 apare doar dacă subcategoria Audit Policy Change este auditată; absența lui nu dovedește nimic.");
        }

        // AF11 — services installed (System 7045) that are no longer configured in the SYSTEM hive.
        {
            var installed = events.Where(e => Ev(e, System, "7045")).ToList();
            var configured = events.Where(e => e.Source == "Service").ToList();
            if (installed.Count > 0 && configured.Count > 0)
            {
                var names = configured.Select(e => F(e, "DisplayName")).Concat(configured.Select(e => e.Service)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var images = configured.Select(e => F(e, "ImagePath").Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var gone = installed.Where(e => !names.Contains(F(e, "ServiceName")) && !images.Contains(F(e, "ImagePath").Trim())).ToList();
                Add("AF11", "Ștergerea unui serviciu", "T1070.009", gone.Count > 0 ? AntiForensicResult.Detected : AntiForensicResult.NotDetected,
                    gone.Count > 0
                        ? $"{gone.Count} din {LogAnalyzer.Dfir.Language.RoText.Count(installed.Count, "serviciu", "servicii")} instalate (7045) nu mai există în configurația curentă (dezinstalare legitimă sau ștergere): " +
                          string.Join("; ", gone.Take(15).Select(e => $"{e.Time.UtcIso} {F(e, "ServiceName")} ({F(e, "ImagePath")})"))
                        : $"Toate cele {LogAnalyzer.Dfir.Language.RoText.Count(installed.Count, "serviciu", "servicii")} instalate (7045) există în hive-ul SYSTEM.",
                    gone.Select(e => Ref(e, "serviciu instalat, absent din configurație")));
            }
            else Add("AF11", "Ștergerea unui serviciu", "T1070.009", AntiForensicResult.Undetermined,
                     installed.Count == 0 ? "Nicio instalare de serviciu (System 7045) disponibilă pentru comparație." : "Configurația serviciilor (hive SYSTEM) nu a fost analizată.");
        }

        // AF12 — scheduled task deletion: Security 4699, TaskScheduler/Operational 141.
        {
            var hits = events.Where(e => Ev(e, Security, "4699") || Ev(e, TaskOps, "141")).ToList();
            if (hits.Count > 0)
                Add("AF12", "Ștergerea unei activități planificate", "T1070.009", AntiForensicResult.Detected,
                    $"{hits.Count} ștergeri: " + string.Join("; ", hits.Take(15).Select(e => $"{e.Time.UtcIso} {F(e, "TaskName")} de {F(e, "SubjectUserName")}{F(e, "UserName")}")),
                    hits.Select(e => Ref(e, "activitate planificată ștearsă")));
            else if (Has(TaskOps))
                Add("AF12", "Ștergerea unei activități planificate", "T1070.009", AntiForensicResult.NotDetected, "TaskScheduler/Operational analizat: niciun eveniment 141.");
            else Add("AF12", "Ștergerea unei activități planificate", "T1070.009", AntiForensicResult.Undetermined,
                     "Lipsește TaskScheduler/Operational; 4699 apare doar dacă Other Object Access Events este auditat.");
        }

        // AF13 — core Windows process names running from elsewhere (paths from Prefetch, BAM, Amcache, 4688, Sysmon 1).
        {
            var withPath = events.Where(e => e.Source is "Prefetch" or "BAM" or "Amcache" or "ShimCache" || Ev(e, Security, "4688")
                                             || e.Source.Equals("EventLog:Microsoft-Windows-Sysmon/Operational", StringComparison.OrdinalIgnoreCase) && e.EventId == "1")
                                 .Where(e => e.Path.Contains('\\')).ToList();
            var hits = withPath.Where(e => IsMasquerading(e.Path)).ToList();
            if (hits.Count > 0)
                Add("AF13", "Deghizare de proces (nume de sistem în altă locație)", "T1036.005", AntiForensicResult.Detected,
                    string.Join("; ", hits.GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Take(15).Select(g => $"{g.Key} ({string.Join(", ", g.Select(x => x.Source).Distinct())})")),
                    hits.Select(e => Ref(e, "binar de sistem în afara locației lui")));
            else if (withPath.Count > 0)
                Add("AF13", "Deghizare de proces (nume de sistem în altă locație)", "T1036.005", AntiForensicResult.NotDetected,
                    $"{withPath.Count} căi de execuție verificate: niciun proces de sistem în afara locației lui.");
            else Add("AF13", "Deghizare de proces (nume de sistem în altă locație)", "T1036.005", AntiForensicResult.Undetermined, "Nicio sursă cu căi de execuție analizată.");
        }

        Add("AF14", "Fluxuri de date alternative (ADS)", "T1564.004", AntiForensicResult.Undetermined,
            "Fluxurile alternative NTFS nu sunt colectate; nicio sursă parsată nu le enumeră.");

        // AF15 — renamed system utility: Amcache OriginalFileName of a system utility under another name.
        {
            var amcache = events.Where(e => e.Source == "Amcache").ToList();
            var hits = amcache.Where(e => SystemUtilities.Contains(F(e, "OriginalFileName"))
                                          && !F(e, "OriginalFileName").Equals(WinPath.GetFileName(e.Path), StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count > 0)
                Add("AF15", "Executabil de sistem redenumit", "T1036.003", AntiForensicResult.Detected,
                    string.Join("; ", hits.Take(15).Select(e => $"{e.Path} (OriginalFileName {F(e, "OriginalFileName")})")), hits.Select(e => Ref(e, "utilitar redenumit")));
            else if (amcache.Any(e => F(e, "OriginalFileName").Length > 0))
                Add("AF15", "Executabil de sistem redenumit", "T1036.003", AntiForensicResult.NotDetected,
                    $"{amcache.Count(e => F(e, "OriginalFileName").Length > 0)} intrări Amcache cu OriginalFileName: niciun utilitar de sistem sub alt nume.");
            else Add("AF15", "Executabil de sistem redenumit", "T1036.003", AntiForensicResult.Undetermined, "Amcache (cu OriginalFileName) nu a fost analizat.");
        }

        // AF16 — firewall manipulation: all rules deleted (2059), or an allow rule for a program in a user-writable folder.
        {
            var allDeleted = events.Where(e => Ev(e, Firewall, "2059")).ToList();
            var allowUser = events.Where(e => Ev(e, Firewall, "2004", "2005", "2097", "2099") && F(e, "Action") == "3"
                                              && F(e, "ApplicationPath").Length > 0 && Correlation.IsUserWritable(F(e, "ApplicationPath"))).ToList();
            var hits = allDeleted.Concat(allowUser).ToList();
            if (hits.Count > 0)
                Add("AF16", "Manipularea firewall-ului", "T1562.004", AntiForensicResult.Detected,
                    string.Join("; ", allDeleted.Take(5).Select(e => $"{e.Time.UtcIso} toate regulile șterse (Store Type {F(e, "Store Type")}) de {F(e, "ModifyingApplication")}")
                        .Concat(allowUser.Take(10).Select(e => $"{e.Time.UtcIso} regulă Allow pentru {F(e, "ApplicationPath")}"))),
                    hits.Select(e => Ref(e, "modificare a firewall-ului")));
            else if (Has(Firewall))
                Add("AF16", "Manipularea firewall-ului", "T1562.004", AntiForensicResult.NotDetected,
                    "Jurnalul firewall analizat: nicio ștergere a tuturor regulilor și nicio regulă Allow pentru programe din foldere în care utilizatorii pot scrie.");
            else Add("AF16", "Manipularea firewall-ului", "T1562.004", AntiForensicResult.Undetermined, "Lipsește jurnalul Windows Firewall With Advanced Security/Firewall.");
        }
        return checks;
    }

    /// <summary>
    /// AF05 in plain words: who moved the clock, which way, by how much, with what program and when. The Security and Kernel-General records of one
    /// change are told once (Security names the account); records whose order contradicts their time are told as such.
    /// </summary>
    internal static string ClockPlain(IReadOnlyList<ClockJump> jumps, int inversions, TimeZoneInfo zone)
    {
        var security = jumps.Where(j => j.EventId == "4616").ToList();
        // both records of one change are written at the same moment; a 4616 without its new time is still matched by when it was written
        static bool SameProcess(ClockJump s, ClockJump k) => s.Process.Length == 0 || k.Process.Length == 0 || s.Process.Equals(k.Process, StringComparison.OrdinalIgnoreCase);
        bool Same(ClockJump s, ClockJump k) => (s.TimeUtc - k.TimeUtc).Duration() <= TimeSpan.FromSeconds(5) && SameProcess(s, k) || s.New is { } n && k.New is { } m && (n - m).Duration() <= TimeSpan.FromSeconds(2);
        // a 4616 without its times takes the size and direction from its Kernel-General twin; the account stays the 4616's
        var kernel = jumps.Where(j => j.EventId != "4616").ToList();
        security = security.Select(s => s.Delta is null && kernel.FirstOrDefault(k => Same(s, k) && k.Delta is not null) is { } twin
            ? s with { Delta = twin.Delta, Direction = twin.Direction, Previous = twin.Previous, New = twin.New } : s).ToList();
        var told = security.Concat(kernel.Where(j => !security.Any(s => Same(s, j))))
                           .OrderBy(j => j.TimeUtc).ToList();
        var sentences = told.Take(3).Select(j =>
        {
            var who = LogAnalyzer.Dfir.Language.PlainStory.Who(j.Actor, null);
            var how = j.Delta is { } d && d.Duration() >= TimeSpan.FromSeconds(1)
                ? $"a mutat ceasul calculatorului {j.Direction} cu {PlainPhrases.Duration(d)}"
                : "a schimbat ora calculatorului";
            var tool = j.Process.Length > 0 ? $", cu programul {PlainPhrases.Q(WinPath.GetFileName(j.Process))}" : "";
            return $"{LogAnalyzer.Dfir.Language.PlainStory.When(j.TimeUtc, j.TimeUtc, zone)}, {who} {how}{tool}.";
        }).ToList();
        if (told.Count > 3) sentences.Add($"Ceasul a mai fost schimbat {PlainPhrases.Times(told.Count - 3)}.");
        if (inversions > 0)
            sentences.Add($"În jurnale, {(inversions == 1 ? "o înregistrare are" : LogAnalyzer.Dfir.Language.RoText.Count(inversions, "înregistrare are", "înregistrări au"))} o oră mai veche decât cele scrise înaintea lor: ceasul a fost dat înapoi.");
        if (sentences.Count == 0) return "";
        sentences.Add(told.Count + inversions == 1 ? "Orele evenimentelor din jurul acestui moment nu sunt sigure." : "Orele evenimentelor din jurul acestor momente nu sunt sigure.");
        return string.Join(" ", sentences);
    }

    /// <summary>True when the file name is a core Windows process and the folder is not where Windows keeps it.</summary>
    public static bool IsMasquerading(string path)
    {
        var raw = path.Trim().Trim('"').Replace('/', '\\');
        // Win32 extended-length / device prefix (\\?\C:\...) and NT object prefix (\??\C:\...) do not change which file it is.
        if (raw.StartsWith(@"\\?\", StringComparison.Ordinal) || raw.StartsWith(@"\\.\", StringComparison.Ordinal)) raw = raw[4..];
        var p = Correlation.Normalize(raw);
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = Correlation.Normalize(p[4..]);
        if (p.StartsWith(@"\DEVICE\HARDDISKVOLUME", StringComparison.Ordinal)) { int i = p.IndexOf('\\', 8); if (i > 0) p = p[i..]; }
        var name = WinPath.GetFileName(p);
        if (!ExpectedDirs.TryGetValue(name, out var dirs)) return false;
        if (ComponentStores.Any(s => p.StartsWith(s, StringComparison.Ordinal))) return false;
        var dir = p[..^(name.Length + 1)];
        return !dirs.Contains(dir, StringComparer.Ordinal);
    }
}
