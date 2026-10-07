using System.Globalization;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>DETECTED: a trace was observed. NOT_DETECTED: the source that would show it was analysed and shows none. UNDETERMINED: it was not.</summary>
public enum AntiForensicResult { Detected, NotDetected, Undetermined }

public sealed record AntiForensicCheck(string Id, string Technique, string Attack, AntiForensicResult Result, string Reason, IReadOnlyList<EvidenceRef> Evidence)
{
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

    public static List<AntiForensicCheck> Evaluate(IReadOnlyList<TimelineEvent> events, IReadOnlyList<EvidenceGap> gaps)
    {
        var sources = events.Select(e => e.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Has(string s) => sources.Contains(s);
        bool Ev(TimelineEvent e, string source, params string[] ids) => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && ids.Contains(e.EventId);
        string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
        EvidenceRef Ref(TimelineEvent e, string d) => new(e.EvidenceId, e.Locator, d, e.SourceSha256);
        var evtxSources = events.Where(e => e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase)).ToList();
        var checks = new List<AntiForensicCheck>();
        void Add(string id, string technique, string attack, AntiForensicResult r, string reason, IEnumerable<EvidenceRef>? ev = null) =>
            checks.Add(new AntiForensicCheck(id, technique, attack, r, reason, (ev ?? []).Take(200).ToList()));
        string Missing(params string[] s) => "lipsește " + string.Join(" și ", s.Where(x => !Has(x)).Select(x => x["EventLog:".Length..]));

        // AF01 — EVTX cleared: Security 1102 / System 104, written by the Eventlog service itself.
        {
            var hits = events.Where(e => e.Provider.Equals("Microsoft-Windows-Eventlog", StringComparison.OrdinalIgnoreCase)
                                         && (Ev(e, Security, "1102") || Ev(e, System, "104"))).ToList();
            if (hits.Count > 0)
                Add("AF01", "Jurnal EVTX golit", "T1070.001", AntiForensicResult.Detected,
                    $"{hits.Count} goliri: " + string.Join("; ", hits.Take(10).Select(e => $"{e.Time.UtcIso} {(e.EventId == "1102" ? "Security" : F(e, "Channel"))} de {F(e, "SubjectDomainName")}\\{F(e, "SubjectUserName")}")),
                    hits.Select(e => Ref(e, "eveniment de golire a jurnalului")));
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
                    $"{holes.Count} anomalii: " + string.Join("; ", holes.Take(20)) +
                    (gaps.Any(x => x.Reason.Contains(CorruptChunksMarker, StringComparison.Ordinal)) ? ". Unele pot proveni din chunk-urile corupte eliminate (AF02)." : ""), refs);
            else if (evtxSources.Count > 0) Add("AF04", "Goluri în RecordID", "T1070.001", AntiForensicResult.NotDetected, "RecordID continuu și fără repetări în fiecare fișier EVTX analizat.");
            else Add("AF04", "Goluri în RecordID", "T1070.001", AntiForensicResult.Undetermined, "Niciun fișier EVTX analizat.");
        }

        // AF05 — system clock changed by something other than time synchronization (W32Time runs in svchost.exe).
        {
            var manual = events.Where(e => Ev(e, Security, "4616") && !F(e, "ProcessName").EndsWith(@"\svchost.exe", StringComparison.OrdinalIgnoreCase)
                                        || e.Source.Equals(System, StringComparison.OrdinalIgnoreCase) && e.EventId == "1"
                                           && e.Provider.Equals("Microsoft-Windows-Kernel-General", StringComparison.OrdinalIgnoreCase)
                                           && F(e, "Reason") == "1" && !F(e, "ProcessName").EndsWith(@"\svchost.exe", StringComparison.OrdinalIgnoreCase)).ToList();
            if (manual.Count > 0)
                Add("AF05", "Manipularea orei sistemului", "", AntiForensicResult.Detected,
                    $"{manual.Count(e => e.EventId == "4616")} în Security 4616 și {manual.Count(e => e.EventId == "1")} în Kernel-General 1 (aceeași schimbare apare în ambele jurnale), făcute de alt proces decât sincronizarea (svchost): " +
                    string.Join("; ", manual.Take(10).Select(e => e.EventId == "4616"
                        ? $"4616 {F(e, "PreviousTime")} → {F(e, "NewTime")} de {F(e, "SubjectUserName")} ({Path.GetFileName(F(e, "ProcessName"))})"
                        : $"Kernel-General {F(e, "OldTime")} → {F(e, "NewTime")} ({Path.GetFileName(F(e, "ProcessName"))})")),
                    manual.Select(e => Ref(e, "schimbare a orei sistemului")));
            else if (Has(System))
                Add("AF05", "Manipularea orei sistemului", "", AntiForensicResult.NotDetected,
                    "System analizat: schimbările de oră (Kernel-General 1) sunt doar sincronizări, ceasul hardware sau fusul orar.");
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
                parts.Add($"{pfDeleted.Count} fișiere .pf șterse (jurnal USN), din care {recreated} recreate ulterior cu același nume; " +
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

        // AF07 — the change journal deleted and recreated: its ID (a FILETIME) is the creation time.
        {
            var ids = usn.Select(e => F(e, "JournalId")).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var created = ids.Select(i => (Id: i, At: LogAnalyzer.Dfir.FileSystem.UsnJournalParser.JournalCreatedUtc(i))).ToList();
            var earliestLog = evtxSources.Where(e => e.Time.Utc is not null).Select(e => e.Time.Utc!.Value).DefaultIfEmpty().Min();
            if (ids.Count == 0)
                Add("AF07", "Anomalii în jurnalul USN", "T1070.004", AntiForensicResult.Undetermined, "Jurnalul USN nu a fost analizat (export fsutil usn readjournal).");
            else if (ids.Count > 1)
                Add("AF07", "Anomalii în jurnalul USN", "T1070.004", AntiForensicResult.Detected,
                    "Exporturi cu ID-uri de jurnal diferite (jurnal șters și recreat între ele): " + string.Join("; ", created.Select(c => $"{c.Id} creat {c.At:yyyy-MM-ddTHH:mm:ssZ}")),
                    usn.GroupBy(e => F(e, "JournalId")).Select(g => Ref(g.First(), "ID jurnal")));
            else if (created[0].At is { } at && earliestLog != default && at > earliestLog)
                Add("AF07", "Anomalii în jurnalul USN", "T1070.004", AntiForensicResult.Detected,
                    $"Jurnalul USN {ids[0]} a fost creat la {at:yyyy-MM-ddTHH:mm:ssZ}, după cea mai veche înregistrare din jurnalele de evenimente ({earliestLog:yyyy-MM-ddTHH:mm:ssZ}): " +
                    "a fost șters și recreat (sau volumul este nou).", [Ref(usn[0], "ID jurnal")]);
            else
                Add("AF07", "Anomalii în jurnalul USN", "T1070.004", AntiForensicResult.NotDetected,
                    $"Un singur jurnal, {ids[0]}, creat la {created[0].At:yyyy-MM-ddTHH:mm:ssZ}" +
                    (earliestLog != default ? $", înaintea celei mai vechi înregistrări din jurnalele de evenimente ({earliestLog:yyyy-MM-ddTHH:mm:ssZ})" : "") +
                    $". Acoperire: {Window()}.", [Ref(usn[0], "ID jurnal")]);
        }

        Add("AF08", "Modificarea marcajelor de timp ale fișierelor (timestomp)", "T1070.006", AntiForensicResult.Undetermined,
            "$MFT nu este parsat: comparația $STANDARD_INFORMATION / $FILE_NAME nu se poate face." +
            (usn.Count > 0 ? " Motivul USN „Basic info change” apare și la schimbarea atributelor, deci nu dovedește singur o modificare a orelor." : ""));

        // AF09 — registry changes to logging and audit configuration, visible only with registry auditing (Sysmon 12–14, Security 4657).
        {
            string[] keys = [@"\SERVICES\EVENTLOG", @"\WINEVT\CHANNELS", @"\CONTROL\LSA\AUDIT", @"\POLICIES\MICROSOFT\WINDOWS\EVENTLOG"];
            var regAudit = events.Where(e => Ev(e, Security, "4657") || e.Source.Equals("EventLog:Microsoft-Windows-Sysmon/Operational", StringComparison.OrdinalIgnoreCase) && e.EventId is "12" or "13" or "14").ToList();
            var hits = regAudit.Where(e => keys.Any(k => (F(e, "TargetObject") + F(e, "ObjectName")).ToUpperInvariant().Contains(k))).ToList();
            if (hits.Count > 0)
                Add("AF09", "Modificări în registru ale jurnalizării", "T1112", AntiForensicResult.Detected,
                    string.Join("; ", hits.Take(10).Select(e => $"{e.Time.UtcIso} {F(e, "TargetObject")}{F(e, "ObjectName")}")), hits.Select(e => Ref(e, "modificare de registru")));
            else if (regAudit.Count > 0)
                Add("AF09", "Modificări în registru ale jurnalizării", "T1112", AntiForensicResult.NotDetected, $"{regAudit.Count} evenimente de audit al registrului, niciunul pe cheile de jurnalizare.");
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
                if (svc.Count > 0) parts.Add($"serviciul EventLog: modul de pornire schimbat de {svc.Count} ori ({string.Join("; ", svc.Take(5).Select(e => $"{F(e, "param2")} → {F(e, "param3")}"))})");
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
                        ? $"{gone.Count} din {installed.Count} servicii instalate (7045) nu mai există în configurația curentă (dezinstalare legitimă sau ștergere): " +
                          string.Join("; ", gone.Take(15).Select(e => $"{e.Time.UtcIso} {F(e, "ServiceName")} ({F(e, "ImagePath")})"))
                        : $"Toate cele {installed.Count} servicii instalate (7045) există în hive-ul SYSTEM.",
                    gone.Select(e => Ref(e, "serviciu instalat, absent din configurație")));
            }
            else Add("AF11", "Ștergerea unui serviciu", "T1070.009", AntiForensicResult.Undetermined,
                     installed.Count == 0 ? "Nicio instalare de serviciu (System 7045) disponibilă pentru comparație." : "Configurația serviciilor (hive SYSTEM) nu a fost analizată.");
        }

        // AF12 — scheduled task deletion: Security 4699, TaskScheduler/Operational 141.
        {
            var hits = events.Where(e => Ev(e, Security, "4699") || Ev(e, TaskOps, "141")).ToList();
            if (hits.Count > 0)
                Add("AF12", "Ștergerea unui task programat", "T1070.009", AntiForensicResult.Detected,
                    $"{hits.Count} ștergeri: " + string.Join("; ", hits.Take(15).Select(e => $"{e.Time.UtcIso} {F(e, "TaskName")} de {F(e, "SubjectUserName")}{F(e, "UserName")}")),
                    hits.Select(e => Ref(e, "task șters")));
            else if (Has(TaskOps))
                Add("AF12", "Ștergerea unui task programat", "T1070.009", AntiForensicResult.NotDetected, "TaskScheduler/Operational analizat: niciun eveniment 141.");
            else Add("AF12", "Ștergerea unui task programat", "T1070.009", AntiForensicResult.Undetermined,
                     "Lipsește TaskScheduler/Operational; 4699 apare doar dacă Other Object Access Events este auditat.");
        }

        // AF13 — core Windows process names running from elsewhere (paths from Prefetch, BAM, Amcache, 4688, Sysmon 1).
        {
            var withPath = events.Where(e => e.Source is "Prefetch" or "BAM" or "Amcache" or "ShimCache" || Ev(e, Security, "4688")
                                             || e.Source.Equals("EventLog:Microsoft-Windows-Sysmon/Operational", StringComparison.OrdinalIgnoreCase) && e.EventId == "1")
                                 .Where(e => e.Path.Contains('\\')).ToList();
            var hits = withPath.Where(e => IsMasquerading(e.Path)).ToList();
            if (hits.Count > 0)
                Add("AF13", "Mascarare de proces (nume de sistem în altă locație)", "T1036.005", AntiForensicResult.Detected,
                    string.Join("; ", hits.GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Take(15).Select(g => $"{g.Key} ({string.Join(", ", g.Select(x => x.Source).Distinct())})")),
                    hits.Select(e => Ref(e, "binar de sistem în afara locației lui")));
            else if (withPath.Count > 0)
                Add("AF13", "Mascarare de proces (nume de sistem în altă locație)", "T1036.005", AntiForensicResult.NotDetected,
                    $"{withPath.Count} căi de execuție verificate: niciun proces de sistem în afara locației lui.");
            else Add("AF13", "Mascarare de proces (nume de sistem în altă locație)", "T1036.005", AntiForensicResult.Undetermined, "Nicio sursă cu căi de execuție analizată.");
        }

        Add("AF14", "Fluxuri de date alternative (ADS)", "T1564.004", AntiForensicResult.Undetermined,
            "Fluxurile alternative NTFS nu sunt colectate; nicio sursă parsată nu le enumeră.");

        // AF15 — renamed system utility: Amcache OriginalFileName of a system utility under another name.
        {
            var amcache = events.Where(e => e.Source == "Amcache").ToList();
            var hits = amcache.Where(e => SystemUtilities.Contains(F(e, "OriginalFileName"))
                                          && !F(e, "OriginalFileName").Equals(Path.GetFileName(e.Path), StringComparison.OrdinalIgnoreCase)).ToList();
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
                    "Jurnalul firewall analizat: nicio ștergere a tuturor regulilor și nicio regulă Allow pentru programe din foldere scriabile de utilizator.");
            else Add("AF16", "Manipularea firewall-ului", "T1562.004", AntiForensicResult.Undetermined, "Lipsește jurnalul Windows Firewall With Advanced Security/Firewall.");
        }
        return checks;
    }

    /// <summary>True when the file name is a core Windows process and the folder is not where Windows keeps it.</summary>
    public static bool IsMasquerading(string path)
    {
        var p = Correlation.Normalize(path.Trim().Trim('"'));
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = Correlation.Normalize(p[4..]);
        if (p.StartsWith(@"\DEVICE\HARDDISKVOLUME", StringComparison.Ordinal)) { int i = p.IndexOf('\\', 8); if (i > 0) p = p[i..]; }
        var name = Path.GetFileName(p);
        if (!ExpectedDirs.TryGetValue(name, out var dirs)) return false;
        if (ComponentStores.Any(s => p.StartsWith(s, StringComparison.Ordinal))) return false;
        var dir = p[..^(name.Length + 1)];
        return !dirs.Contains(dir, StringComparer.Ordinal);
    }
}
