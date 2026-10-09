using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Correlation rules over the unified timeline. Each rule yields findings that point to the exact evidence rows
/// behind them (EvidenceId + locator). Classification follows the spec: DIRECT = one record shows it; CORRELATED =
/// independent sources agree; CANDIDATE = worth an analyst's look, not proven.
/// </summary>
public static class Correlation
{
    private static readonly string[] UserWritableMarkers =
        [@"\programdata\", @"\appdata\", @"\temp\", @"\downloads\", @"\users\public\", @"\windows\temp\", @"\$recycle.bin\"];

    /// <summary>Signed Microsoft binaries frequently used to run attacker code or move data ("living off the land").</summary>
    private static readonly HashSet<string> Lolbins = new(StringComparer.OrdinalIgnoreCase)
    {
        "msbuild.exe", "regsvr32.exe", "rundll32.exe", "mshta.exe", "installutil.exe", "regasm.exe", "regsvcs.exe", "cmstp.exe",
        "wscript.exe", "cscript.exe", "certutil.exe", "bitsadmin.exe", "msiexec.exe", "powershell.exe", "pwsh.exe", "wmic.exe",
        "forfiles.exe", "odbcconf.exe", "csc.exe", "vbc.exe", "jsc.exe", "msxsl.exe", "curl.exe",
    };

    private static readonly string[] ScriptExtensions = [".cmd", ".bat", ".vbs", ".js", ".ps1", ".hta", ".wsf"];

    /// <summary>Writable by design but owned by Microsoft components (Defender platform, installer caches, PowerShell's own policy probes).</summary>
    private static readonly string[] KnownBenignMarkers =
        [@"\programdata\microsoft\", @"\programdata\package cache\", "__psscriptpolicytest_", @"\appdata\local\microsoft\windowsapps\"];

    /// <summary>Environment variables in task/service commands that resolve to user-writable folders (not expanded on this machine).</summary>
    private static readonly (string Var, string Folder)[] WritableEnvVars =
        // No trailing separator: the variable is followed by its own "\" in the path ("%ProgramData%\Microsoft").
        [("%localappdata%", @"\appdata\local"), ("%appdata%", @"\appdata\roaming"), ("%temp%", @"\temp"), ("%tmp%", @"\temp"),
         ("%programdata%", @"\programdata"), ("%public%", @"\users\public"), ("%allusersprofile%", @"\programdata")];

    public static bool IsUserWritable(string path)
    {
        var p = path.Replace('/', '\\').ToLowerInvariant();
        foreach (var (v, folder) in WritableEnvVars) p = p.Replace(v, folder);
        return UserWritableMarkers.Any(p.Contains) && !KnownBenignMarkers.Any(p.Contains);
    }

    public static List<Finding> Run(IReadOnlyList<TimelineEvent> events)
    {
        var f = new List<Finding>();
        int n = 0;
        string Id() => $"F-{++n:D4}";
        EvidenceRef Ref(TimelineEvent e, string d) => new(e.EvidenceId, e.Locator, d);
        string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
        bool Ev(TimelineEvent e, string channel, params int[] ids) =>
            e.Source.Equals("EventLog:" + channel, StringComparison.OrdinalIgnoreCase) && int.TryParse(e.EventId, out var id) && ids.Contains(id);

        // 1. Defender detections and tampering with Defender.
        foreach (var g in events.Where(e => Ev(e, "Microsoft-Windows-Windows Defender/Operational", 1116, 1117, 1006, 1007, 1015))
                                .GroupBy(e => DefenderContainer(F(e, "Path")), StringComparer.OrdinalIgnoreCase))
        {
            var first = g.OrderBy(e => e.Time.Utc).First();
            var threats = g.Select(e => F(e, "Threat Name")).Where(x => x.Length > 0).Distinct().ToList();
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "DEF-DETECTION",
                Title = $"Microsoft Defender a detectat {(threats.Count == 1 ? threats[0] : $"{threats.Count} amenințări")} în {WinPath.GetFileName(g.Key.TrimEnd('\\'))}",
                Severity = Severity.High, Category = "Execution", Classification = Classification.Direct, Confidence = Confidence.High,
                FirstSeenUtc = g.Min(e => e.Time.Utc), LastSeenUtc = g.Max(e => e.Time.Utc), File = g.Key, User = F(first, "Detection User"),
                Description = $"{g.Count()} evenimente Defender pentru {g.Key}: {string.Join(", ", threats.Take(15))}. Acțiuni: {string.Join(", ", g.Select(e => F(e, "Action Name")).Where(a => a.Length > 0).Distinct())}.",
                ClassificationReason = "Înregistrare directă în jurnalul Defender.",
                SupportingEvidence = g.Take(20).Select(e => Ref(e, $"Defender {e.EventId}")).ToList(),
                RecommendedNextSteps = ["Verificați dacă fișierul mai există și dacă rulează.", "Căutați același hash pe alte stații."],
            });
        }
        var tamper = events.Where(e => Ev(e, "Microsoft-Windows-Windows Defender/Operational", 5001, 5010, 5012) ||
                                       Ev(e, "Microsoft-Windows-Windows Defender/Operational", 5007) && F(e, "New Value").Contains("Exclusions", StringComparison.OrdinalIgnoreCase)).ToList();
        if (tamper.Count > 0)
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "DEF-TAMPER", Title = "Microsoft Defender dezactivat sau cu excluderi adăugate",
                Severity = Severity.High, Category = "Defense Evasion", Classification = Classification.Direct, Confidence = Confidence.High,
                FirstSeenUtc = tamper.Min(e => e.Time.Utc), LastSeenUtc = tamper.Max(e => e.Time.Utc), MitreTechniqueId = "T1562.001",
                Description = string.Join("; ", tamper.Take(10).Select(e => e.EventId == "5007" ? $"excludere: {F(e, "New Value")}" : $"eveniment {e.EventId}")),
                ClassificationReason = "Înregistrări directe în jurnalul Defender (5001 protecție dezactivată, 5007 configurație modificată).",
                SupportingEvidence = tamper.Take(20).Select(e => Ref(e, $"Defender {e.EventId}")).ToList(),
                AlternativeExplanations = ["Excludere adăugată intenționat de un administrator pentru un program legitim."],
            });

        // 2. Log tampering: cleared logs and record-id gaps (ported from the 2026-08-08 prototype's LogIntegrityService).
        var clears = events.Where(e => Ev(e, "Security", 1102) || Ev(e, "System", 104) || Ev(e, "Security", 1100) || Ev(e, "Security", 4719)).ToList();
        if (clears.Count > 0)
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "LOG-TAMPER", Title = "Jurnale șterse, serviciul de jurnalizare oprit sau politica de audit modificată",
                Severity = clears.Any(e => e.EventId is "1102" or "104") ? Severity.High : Severity.Medium, Category = "Defense Evasion",
                Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1070.001",
                FirstSeenUtc = clears.Min(e => e.Time.Utc), LastSeenUtc = clears.Max(e => e.Time.Utc),
                Description = string.Join("; ", clears.OrderBy(e => e.Time.Utc).Take(15).Select(e =>
                    $"{e.Time.Utc:yyyy-MM-dd HH:mm} {e.Source[9..]} {e.EventId} {(e.EventId == "1102" ? "de " + F(e, "SubjectUserName") : F(e, "Channel"))}")),
                ClassificationReason = "Evenimente 1102/104/1100/4719 înregistrate de Windows.",
                SupportingEvidence = clears.Take(30).Select(e => Ref(e, $"{e.Source} {e.EventId}")).ToList(),
                MissingEvidence = ["Activitatea anterioară ștergerii nu mai este în jurnal; surse alternative: SRUM, Prefetch, Amcache, copii VSS."],
            });
        foreach (var g in events.Where(e => e.Source.StartsWith("EventLog:", StringComparison.Ordinal) && e.Locator.StartsWith("EventRecordID=", StringComparison.Ordinal))
                                .GroupBy(e => e.EvidenceId))
        {
            var ordered = g.Select(e => (e, id: long.TryParse(e.Locator[14..], out var r) ? r : -1)).Where(x => x.id > 0).OrderBy(x => x.id).ToList();
            var gaps = new List<string>();
            var refs = new List<EvidenceRef>();
            for (int i = 1; i < ordered.Count; i++)
            {
                long diff = ordered[i].id - ordered[i - 1].id;
                if (diff <= 1) continue;
                // A gap with a time going backwards or a large hole inside an exported log means records were removed.
                if (diff > 50)
                {
                    gaps.Add($"RecordID {ordered[i - 1].id} ({ordered[i - 1].e.Time.Utc:yyyy-MM-dd HH:mm}) → {ordered[i].id} ({ordered[i].e.Time.Utc:yyyy-MM-dd HH:mm}): lipsesc {diff - 1}");
                    refs.Add(Ref(ordered[i].e, $"după golul de {diff - 1} înregistrări"));
                }
            }
            if (gaps.Count > 0)
                f.Add(new Finding
                {
                    FindingId = Id(), RuleId = "LOG-GAP", Title = $"Goluri în numerotarea înregistrărilor din {g.First().Source[9..]}",
                    Severity = Severity.Medium, Category = "Defense Evasion", Classification = Classification.Candidate, Confidence = Confidence.Medium,
                    MitreTechniqueId = "T1070.001", Description = string.Join("; ", gaps.Take(10)),
                    ClassificationReason = "Într-un jurnal exportat, numerele de înregistrare sunt consecutive; golurile apar la ștergere selectivă sau la corupere.",
                    SupportingEvidence = refs.Take(20).ToList(),
                    AlternativeExplanations = ["Corupere a fișierului jurnal.", "Export parțial."],
                });
        }

        // 3. Persistence: new services and scheduled tasks, especially from user-writable locations.
        var services = events.Where(e => Ev(e, "System", 7045) || Ev(e, "Security", 4697)).ToList();
        foreach (var e in services.Where(e => IsUserWritable(F(e, "ImagePath") + F(e, "ServiceFileName"))))
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-SERVICE-USERPATH", Title = $"Serviciu instalat dintr-o locație scriabilă de utilizatori: {F(e, "ServiceName")}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1543.003",
                FirstSeenUtc = e.Time.Utc, File = F(e, "ImagePath") + F(e, "ServiceFileName"), User = F(e, "AccountName"),
                Description = $"Serviciul {F(e, "ServiceName")} rulează {F(e, "ImagePath")}{F(e, "ServiceFileName")} ca {F(e, "AccountName")}{F(e, "ServiceAccount")}.",
                ClassificationReason = "Eveniment de instalare a serviciului cu cale în ProgramData/AppData/Temp.",
                SupportingEvidence = [Ref(e, "instalare serviciu")],
            });
        var tasks = events.Where(e => Ev(e, "Security", 4698) || Ev(e, "Microsoft-Windows-TaskScheduler/Operational", 106)).ToList();
        foreach (var e in tasks.Where(e => IsUserWritable(F(e, "TaskContent")) || ScriptExtensions.Any(x => F(e, "TaskContent").Contains(x, StringComparison.OrdinalIgnoreCase))))
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-TASK", Title = $"Task programat care rulează din locație scriabilă sau un script: {F(e, "TaskName")}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1053.005",
                FirstSeenUtc = e.Time.Utc, User = F(e, "SubjectUserName") + F(e, "UserContext"),
                Description = $"Task {F(e, "TaskName")} creat de {F(e, "SubjectUserName")}{F(e, "UserContext")}.",
                ClassificationReason = "Eveniment de creare a taskului; acțiunea indică o cale scriabilă sau un script.",
                SupportingEvidence = [Ref(e, "creare task")],
            });

        // Task definitions (System32\Tasks XML): configuration that runs a program from a user-writable location.
        foreach (var e in events.Where(e => e.Source == "ScheduledTask" && e.Path.Length > 0 &&
                                            (IsUserWritable(e.Path) || ScriptExtensions.Any(x => F(e, "Arguments").Contains(x, StringComparison.OrdinalIgnoreCase) && IsUserWritable(F(e, "Arguments"))))))
        {
            bool hidden = F(e, "Hidden") == "true";
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-TASK-CONFIG", Title = $"Task programat{(hidden ? " ascuns" : "")} care rulează din locație scriabilă: {e.Task}",
                Severity = hidden ? Severity.High : Severity.Medium, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1053.005", FirstSeenUtc = e.Time.Utc, File = e.Path, Process = e.Process, User = e.User,
                Description = $"{e.Task} rulează {F(e, "Command")} {F(e, "Arguments")} ca {e.User} ({F(e, "RunLevel")}); declanșatori: {F(e, "Triggers")}; autor {F(e, "Author")}.",
                ClassificationReason = "Definiția taskului (XML din System32\\Tasks) indică o cale scriabilă de utilizatori. Arată configurația, nu o rulare.",
                SupportingEvidence = [Ref(e, "definiție task")],
                AlternativeExplanations = ["Actualizatoare legitime instalate per utilizator (AppData) creează astfel de taskuri."],
                MissingEvidence = ["Rulări ale taskului: TaskScheduler/Operational (200/201) sau Prefetch pentru executabil."],
                ContradictingEvidence = F(e, "Enabled") == "false" ? ["Taskul este dezactivat (Settings/Enabled=false): în această stare nu pornește singur."] : [],
            });
        }

        // Services and drivers configured in the SYSTEM hive whose binary or ServiceDll is in a user-writable location.
        foreach (var e in events.Where(e => e.Source == "Service" && (IsUserWritable(e.Path) || IsUserWritable(F(e, "ServiceDll")))))
        {
            var file = IsUserWritable(F(e, "ServiceDll")) ? F(e, "ServiceDll") : e.Path;
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-SERVICE-CONFIG", Title = $"Serviciu configurat din locație scriabilă: {e.Service}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1543.003", FirstSeenUtc = e.Time.Utc, File = file, User = e.User,
                Description = $"{e.Service} ({F(e, "ServiceType")}, {F(e, "StartMode")}) rulează {F(e, "ImagePath")}" +
                              (F(e, "ServiceDll").Length > 0 ? $" cu ServiceDll {F(e, "ServiceDll")}" : "") + $" ca {e.User}.",
                ClassificationReason = "Configurația serviciului din hive-ul SYSTEM indică o cale scriabilă de utilizatori. Arată configurația, nu o rulare.",
                SupportingEvidence = [Ref(e, "configurație serviciu")],
                AlternativeExplanations = ["Unele produse legitime își instalează serviciul în ProgramData."],
                MissingEvidence = ["Instalarea (System 7045) și pornirile (7036), semnătura binarului."],
                ContradictingEvidence = F(e, "StartMode").Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                    ? ["Serviciul este dezactivat (Start=4): în această stare nu pornește singur."] : [],
            });
        }

        // Firewall rules that allow a program from a user-writable folder (Firewall.evtx 2004/2005 older, 2097/2099 Windows 11).
        // Codes checked on real events: Action 3 = Allow, 2 = Block; Direction 1 = Inbound, 2 = Outbound.
        foreach (var e in events.Where(e => Ev(e, "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall", 2004, 2005, 2097, 2099)
                                            && F(e, "Action") == "3" && IsUserWritable(F(e, "ApplicationPath"))))
        {
            bool inbound = F(e, "Direction") == "1";
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "FIREWALL-RULE-USERPATH",
                Title = $"Regulă de firewall care permite {(inbound ? "intrarea" : "ieșirea")} pentru un program din locație scriabilă: {WinPath.GetFileName(F(e, "ApplicationPath"))}",
                Severity = inbound ? Severity.High : Severity.Medium, Category = "DefenseEvasion", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1562.004", FirstSeenUtc = e.Time.Utc, File = F(e, "ApplicationPath"), User = F(e, "ModifyingUser"),
                Description = $"Regula „{F(e, "RuleName")}” ({(inbound ? "Inbound" : "Outbound")}, Allow) pentru {F(e, "ApplicationPath")}, adăugată/modificată de {F(e, "ModifyingApplication")}.",
                ClassificationReason = "Evenimentul de firewall arată o regulă de tip Allow pentru o cale scriabilă de utilizatori.",
                SupportingEvidence = [Ref(e, "regulă firewall")],
                AlternativeExplanations = ["Aplicații per utilizator (de ex. jocuri, clienți de chat) își adaugă reguli la instalare."],
            });
        }

        // Registry autostarts (Run/RunOnce, Winlogon, IFEO) from saved hives.
        foreach (var e in events.Where(e => e.Source == "RunKey" && IsUserWritable(e.Path)))
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-RUNKEY-USERPATH", Title = $"Pornire automată din locație scriabilă: {F(e, "ValueName")}",
                Severity = Severity.Medium, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1547.001", FirstSeenUtc = e.Time.Utc, File = e.Path, Process = e.Process,
                Description = $"{F(e, "Hive")}\\{F(e, "Key")}: {F(e, "ValueName")} = {F(e, "Command")}",
                ClassificationReason = "Valoarea din cheia Run/RunOnce indică o cale scriabilă de utilizatori. Ora este LastWriteTime al cheii.",
                SupportingEvidence = [Ref(e, "valoare Run")],
                AlternativeExplanations = ["Multe aplicații per utilizator (actualizatoare, sincronizare) pornesc legitim din AppData."],
            });
        foreach (var e in events.Where(e => e.Source == "IFEO"))
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-IFEO-DEBUGGER", Title = $"Image File Execution Options: {F(e, "Target")} este înlocuit de {e.Process}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1546.012", FirstSeenUtc = e.Time.Utc, File = e.Path, Process = e.Process,
                Description = $"La pornirea {F(e, "Target")} Windows rulează {F(e, "Command")}.",
                ClassificationReason = "Valoarea Debugger din IFEO redirecționează pornirea programului țintă.",
                SupportingEvidence = [Ref(e, "IFEO Debugger")],
                AlternativeExplanations = ["Depanatoare instalate intenționat de dezvoltatori (de ex. vsjitdebugger.exe)."],
            });
        foreach (var e in events.Where(e => e.Source == "Winlogon" && F(e, "NonDefault") == "true"))
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-WINLOGON", Title = $"Winlogon {F(e, "ValueName")} diferit de valoarea implicită",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1547.004", FirstSeenUtc = e.Time.Utc, File = e.Path,
                Description = $"{F(e, "ValueName")} = {F(e, "Command")}",
                ClassificationReason = "Shell diferit de explorer.exe sau Userinit cu alte programe decât userinit.exe.",
                SupportingEvidence = [Ref(e, "Winlogon")],
                AlternativeExplanations = ["Medii kiosk sau shell-uri înlocuite intenționat de administrator."],
            });

        // 4. Execution from user-writable locations (Prefetch references), scripts launched by installers.
        var userPathExec = new List<Finding>();
        foreach (var e in events.Where(e => e.Source == "Prefetch").GroupBy(e => e.Fields.GetValueOrDefault("PrefetchHash") + e.Process).Select(g => g.OrderByDescending(x => x.Time.Utc).First()))
        {
            var refsFiles = F(e, "ReferencedFiles").Split('|');
            var exePath = refsFiles.FirstOrDefault(r => r.EndsWith("\\" + e.Process, StringComparison.OrdinalIgnoreCase)) ?? e.Path;
            if (IsUserWritable(exePath))
                userPathExec.Add(new Finding
                {
                    FindingId = "", RuleId = "EXEC-USERPATH", Title = $"Program rulat dintr-o locație scriabilă: {e.Process}",
                    Severity = Severity.Medium, Category = "Execution", Classification = Classification.Direct, Confidence = Confidence.High,
                    LastSeenUtc = e.Time.Utc, File = exePath, Process = e.Process,
                    Description = $"{exePath} a rulat de {F(e, "RunCount")} ori; ultima rulare {e.Time.Utc:yyyy-MM-dd HH:mm} UTC.",
                    ClassificationReason = "Fișierul Prefetch al programului (execuție dovedită).", SemanticType = SemanticType.Execution,
                    SupportingEvidence = [Ref(e, "Prefetch")],
                    AlternativeExplanations = ["Instalator sau aplicație legitimă instalată per utilizator."],
                });
            var scripts = refsFiles.Where(r => ScriptExtensions.Any(x => r.EndsWith(x, StringComparison.OrdinalIgnoreCase)) && IsUserWritable(r)).ToList();
            if (scripts.Count > 0 && Lolbins.Contains(e.Process))
                f.Add(new Finding
                {
                    FindingId = Id(), RuleId = "EXEC-SCRIPT-VIA-LOLBIN", Title = $"{e.Process} a lucrat cu scripturi din locații scriabile",
                    Severity = Severity.High, Category = "Execution", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1218",
                    LastSeenUtc = e.Time.Utc, Process = e.Process, File = string.Join("; ", scripts.Take(5)),
                    Description = $"Prefetch-ul lui {e.Process} referă: {string.Join(", ", scripts.Take(10))}.",
                    ClassificationReason = "Fișiere referite în Prefetch în timpul execuției programului.",
                    SupportingEvidence = [Ref(e, "Prefetch ReferencedFiles")],
                });
        }

        // 4b. BAM (execution) and Amcache (presence) from user-writable locations, one entry per path.
        foreach (var e in events.Where(e => e.Source is "BAM" or "Amcache" && IsUserWritable(e.Path))
                                .GroupBy(e => (e.Source, e.Path.ToLowerInvariant())).Select(g => g.OrderByDescending(x => x.Time.Utc).First()))
            userPathExec.Add(new Finding
            {
                FindingId = "", RuleId = "EXEC-USERPATH", Title = $"{(e.Source == "BAM" ? "Program rulat" : "Program prezent")} dintr-o locație scriabilă: {e.Process}",
                Severity = Severity.Medium, Category = "Execution", Classification = Classification.Direct,
                Confidence = e.Source == "BAM" ? Confidence.High : Confidence.Medium, LastSeenUtc = e.Time.Utc, File = e.Path, Process = e.Process, User = e.User,
                Description = e.Source == "BAM"
                    ? $"{e.Path}: ultima rulare {e.Time.Utc:yyyy-MM-dd HH:mm} UTC (BAM, utilizator {e.User})."
                    : $"{e.Path}: prezent în Amcache (SHA-1 {e.Hash}, {F(e, "Publisher")} {F(e, "Version")}), intrare scrisă {e.Time.Utc:yyyy-MM-dd HH:mm} UTC.",
                ClassificationReason = e.Source == "BAM" ? "BAM înregistrează ultima execuție per utilizator." : "Amcache înregistrează prezența/instalarea programului.",
                SemanticType = AntiOverclaim.SemanticForArtifact(e.Source),
                SupportingEvidence = [Ref(e, e.Source)],
                AlternativeExplanations = ["Instalator sau aplicație legitimă instalată per utilizator."],
            });

        // 5. Network: LOLBins with real traffic (SRUM), upload-heavy applications.
        var srum = events.Where(e => e.Source == "SRUM").ToList();
        foreach (var g in srum.GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            long sent = g.Sum(e => long.TryParse(F(e, "BytesSent"), out var s) ? s : 0), recv = g.Sum(e => long.TryParse(F(e, "BytesRecvd"), out var r) ? r : 0);
            var exe = WinPath.GetFileName(g.Key.TrimEnd('\\'));
            if (Lolbins.Contains(exe) && sent + recv > 1_000_000 && !exe.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) || IsUserWritable(g.Key) && sent > 5_000_000)
            {
                var prefetch = events.Where(e => e.Source == "Prefetch" && e.Process.Equals(exe, StringComparison.OrdinalIgnoreCase)).ToList();
                f.Add(new Finding
                {
                    FindingId = Id(), RuleId = Lolbins.Contains(exe) ? "NET-LOLBIN-TRAFFIC" : "NET-USERPATH-UPLOAD",
                    Title = Lolbins.Contains(exe) ? $"{exe} (unealtă Windows abuzată frecvent) a transferat date în rețea" : $"{exe} (din locație scriabilă) a trimis {sent / 1_000_000.0:0.#} MB",
                    Severity = Severity.High, Category = "Command and Control / Exfiltration",
                    Classification = prefetch.Count > 0 ? Classification.Correlated : Classification.Candidate, Confidence = prefetch.Count > 0 ? Confidence.High : Confidence.Medium,
                    FirstSeenUtc = g.Min(e => e.Time.Utc), LastSeenUtc = g.Max(e => e.Time.Utc), Process = exe, File = g.Key,
                    Description = $"SRUM: {sent:N0} B trimiși, {recv:N0} B primiți, în {g.Count()} intervale orare." +
                                  (prefetch.Count > 0 ? $" Prefetch confirmă execuția (ultima {prefetch.Max(p => p.Time.Utc):yyyy-MM-dd HH:mm} UTC)." : ""),
                    ClassificationReason = prefetch.Count > 0 ? "Două surse independente: SRUM (trafic) și Prefetch (execuție)." : "O singură sursă (SRUM).",
                    SupportingEvidence = g.OrderByDescending(e => long.TryParse(F(e, "BytesSent"), out var s) ? s : 0).Take(10).Select(e => Ref(e, $"SRUM {F(e, "BytesSent")} B trimiși"))
                                          .Concat(prefetch.Take(3).Select(p => Ref(p, "Prefetch"))).ToList(),
                    AlternativeExplanations = Lolbins.Contains(exe) ? ["Build sau instalare legitimă care descarcă pachete."] : ["Aplicație legitimă cu sincronizare în cloud."],
                    MissingEvidence = ["SRUM nu arată destinațiile; folosiți captura de rețea sau jurnalele firewall-ului pentru adrese."],
                    MitreTechniqueId = Lolbins.Contains(exe) ? "T1127.001" : "T1041",
                });
            }
        }

        // 6. Credential attacks and remote access.
        foreach (var g in events.Where(e => Ev(e, "Security", 4625)).GroupBy(e => (F(e, "IpAddress"), F(e, "TargetUserName")))
                                .Where(g => g.Count() >= 10))
        {
            var ordered = g.OrderBy(e => e.Time.Utc).ToList();
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "CRED-BRUTEFORCE", Title = $"{g.Count()} autentificări eșuate pentru {g.Key.Item2} de la {g.Key.Item1}",
                Severity = Severity.Medium, Category = "Credential Access", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1110",
                FirstSeenUtc = ordered[0].Time.Utc, LastSeenUtc = ordered[^1].Time.Utc, Ip = g.Key.Item1, User = g.Key.Item2,
                Description = $"Între {ordered[0].Time.Utc:yyyy-MM-dd HH:mm} și {ordered[^1].Time.Utc:yyyy-MM-dd HH:mm} UTC.",
                ClassificationReason = "Evenimente 4625 repetate.",
                SupportingEvidence = ordered.Take(15).Select(e => Ref(e, "4625")).ToList(),
            });
        }
        var rdp = events.Where(e => Ev(e, "Security", 4624) && F(e, "LogonType") == "10" && IpClassifier.IsExternal(F(e, "IpAddress"))).ToList();
        if (rdp.Count > 0)
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "REMOTE-RDP-PUBLIC", Title = "Autentificări RDP de pe adrese din Internet",
                Severity = Severity.High, Category = "Initial Access", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1133",
                FirstSeenUtc = rdp.Min(e => e.Time.Utc), LastSeenUtc = rdp.Max(e => e.Time.Utc),
                Description = string.Join("; ", rdp.GroupBy(e => (F(e, "TargetUserName"), F(e, "IpAddress"))).Select(g => $"{g.Key.Item1} de la {g.Key.Item2} ({g.Count()})")),
                ClassificationReason = "Evenimente 4624 cu tip 10 și adresă publică.",
                SupportingEvidence = rdp.Take(20).Select(e => Ref(e, "4624 RDP")).ToList(),
            });

        // 7. Suspicious PowerShell.
        var ps = events.Where(e => Ev(e, "Microsoft-Windows-PowerShell/Operational", 4104) &&
                                   new[] { "DownloadString", "FromBase64String", "Invoke-Expression", "IEX(", "Net.WebClient", "-EncodedCommand", "Add-MpPreference", "Set-MpPreference -Disable" }
                                       .Any(k => F(e, "ScriptBlockText").Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
        if (ps.Count > 0)
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PS-SUSPICIOUS", Title = "Blocuri PowerShell cu tehnici de descărcare/obfuscare/dezactivare a protecției",
                Severity = Severity.Medium, Category = "Execution", Classification = Classification.Candidate, Confidence = Confidence.Medium, MitreTechniqueId = "T1059.001",
                FirstSeenUtc = ps.Min(e => e.Time.Utc), LastSeenUtc = ps.Max(e => e.Time.Utc),
                Description = $"{ps.Count} blocuri de script. Primul: {Trunc(F(ps[0], "ScriptBlockText"), 200)}",
                ClassificationReason = "Cuvinte-cheie în textul scriptului (Script Block Logging); multe instrumente administrative legitime le folosesc.",
                SupportingEvidence = ps.Take(20).Select(e => Ref(e, "4104")).ToList(),
                AlternativeExplanations = ["Scripturi de administrare sau de instalare legitime."],
            });

        // 8. Executions from user-writable locations: individual findings only when another signal touches them.
        static DateTimeOffset? T(Finding x) => x.FirstSeenUtc ?? x.LastSeenUtc;
        var strong = f.Where(x => x.Severity >= Severity.High && T(x) is not null).ToList();
        var correlated = new HashSet<Finding>();
        foreach (var x in userPathExec)
        {
            var dir = WinPath.GetDirectoryName(Normalize(x.File)) ?? "";
            var touching = strong.Where(s =>
                (s.File.Length > 0 && dir.Length > 3 && Normalize(s.File).StartsWith(dir, StringComparison.OrdinalIgnoreCase)) ||
                (s.Process.Length > 0 && s.Process.Equals(x.Process, StringComparison.OrdinalIgnoreCase)) ||
                (s.RuleId is "DEF-DETECTION" or "EXEC-SCRIPT-VIA-LOLBIN" or "NET-LOLBIN-TRAFFIC" or "PERSIST-SERVICE-USERPATH" or "PERSIST-TASK" &&
                 T(s) is { } st && T(x) is { } xt && (st - xt).Duration() <= TimeSpan.FromMinutes(10))).ToList();
            if (touching.Count == 0) continue;
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "EXEC-USERPATH-CORRELATED", Title = $"Program rulat din locație scriabilă, legat de alte constatări: {x.Process}",
                Severity = Severity.High, Category = "Execution", Classification = Classification.Correlated, Confidence = Confidence.Medium,
                LastSeenUtc = x.LastSeenUtc, File = x.File, Process = x.Process,
                Description = x.Description + " Legături: " + string.Join("; ", touching.Take(5).Select(s => s.Title)),
                ClassificationReason = "Execuție dovedită (Prefetch) apropiată în timp sau în cale de alte constatări grave.",
                SupportingEvidence = x.SupportingEvidence.Concat(touching.SelectMany(s => s.SupportingEvidence.Take(2))).ToList(),
                AlternativeExplanations = x.AlternativeExplanations,
            });
            correlated.Add(x);
        }
        var rest = userPathExec.Where(x => !correlated.Contains(x)).ToList();
        if (rest.Count > 0)
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "EXEC-USERPATH",
                SemanticType = rest.All(x => x.SemanticType == SemanticType.Execution) ? SemanticType.Execution : SemanticType.Presence,
                Title = rest.All(x => x.SemanticType == SemanticType.Execution)
                    ? $"{rest.Count} programe au rulat din locații scriabile (fără alte semnale)"
                    : $"{rest.Count} programe au rulat sau sunt doar prezente în locații scriabile (fără alte semnale)",
                Severity = Severity.Low, Category = "Execution", Classification = Classification.Candidate, Confidence = Confidence.Low,
                FirstSeenUtc = rest.Min(T), LastSeenUtc = rest.Max(T),
                Description = string.Join("; ", rest.OrderByDescending(T).Take(60).Select(x => $"{x.Process} ({T(x):yyyy-MM-dd HH:mm})")),
                ClassificationReason = "Execuții dovedite de Prefetch; în sine frecvente pentru instalatoare și aplicații per utilizator.",
                SupportingEvidence = rest.SelectMany(x => x.SupportingEvidence).Take(60).ToList(),
            });

        // Initial access: a browser download followed, within 6 hours, by a program run from the same folder (or below it).
        var execs = events.Where(e => e.Source is "Prefetch" or "BAM" or "UserAssist" && e.Time.Utc is not null).Select(e =>
        {
            var path = e.Source == "Prefetch"
                ? F(e, "ReferencedFiles").Split('|').FirstOrDefault(r => r.EndsWith("\\" + e.Process, StringComparison.OrdinalIgnoreCase)) ?? e.Path
                : e.Path;
            return (Event: e, Path: Normalize(path));
        }).Where(x => x.Path.Length > 0).ToList();
        foreach (var d in events.Where(e => e.Source == "BrowserDownload" && e.Path.Length > 0 && e.Time.Utc is not null))
        {
            var dir = Normalize(WinPath.GetDirectoryName(d.Path) ?? "");
            if (dir.Length < 4) continue;
            var start = d.Time.Utc!.Value;
            var hits = execs.Where(x => x.Path.StartsWith(dir + "\\", StringComparison.Ordinal) && x.Event.Time.Utc >= start && x.Event.Time.Utc <= start.AddHours(6))
                            .OrderBy(x => x.Event.Time.Utc).ToList();
            if (hits.Count == 0) continue;
            // A distinctive number from the downloaded file's name (5+ digits) found in the program's path ties them closely.
            var tokens = System.Text.RegularExpressions.Regex.Matches(WinPath.GetFileNameWithoutExtension(d.Path), @"\d{5,}").Select(m => m.Value).ToList();
            bool tied = hits.Any(h => tokens.Any(t => h.Path.Contains(t, StringComparison.Ordinal)));
            var first = hits[0].Event;
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "DOWNLOAD-THEN-EXEC",
                Title = $"Descărcare urmată de rularea unui program din același folder: {WinPath.GetFileName(d.Path)} → {first.Process}",
                Severity = tied ? Severity.High : Severity.Medium, Category = "InitialAccess", Classification = Classification.Correlated,
                Confidence = tied ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1204.002",
                FirstSeenUtc = start, LastSeenUtc = first.Time.Utc, File = d.Path, Process = first.Process, Domain = d.Dns,
                Description = $"{d.Path} descărcat la {start:yyyy-MM-dd HH:mm:ss} UTC din {F(d, "TabUrl")} (lanț: {Trunc(F(d, "UrlChain"), 200)}); " +
                              $"apoi {string.Join(", ", hits.Take(5).Select(h => $"{h.Event.Process} ({h.Event.Source}, {h.Event.Time.Utc:HH:mm:ss})"))}.",
                ClassificationReason = tied
                    ? "Programul rulează din folderul descărcării, în următoarele 6 ore, iar calea lui conține numărul din numele fișierului descărcat."
                    : "Programul rulează din folderul descărcării, în următoarele 6 ore.",
                SupportingEvidence = [Ref(d, "descărcare"), .. hits.Take(3).Select(h => Ref(h.Event, h.Event.Source))],
                AlternativeExplanations = ["Utilizatorul a rulat alt program din folderul Downloads, fără legătură cu descărcarea."],
                MissingEvidence = ["Zone.Identifier (Mark-of-the-Web) al fișierului extras; jurnalul de extragere al arhivei."],
            });
        }

        // 9. Incident chain: serious findings close in time are presented as one ordered story.
        var timed = f.Where(x => x.Severity >= Severity.High && T(x) is not null && x.RuleId != "DEF-TAMPER").OrderBy(T).ToList();
        var cluster = new List<Finding>();
        void Flush()
        {
            if (cluster.Select(c => c.RuleId).Distinct().Count() >= 3)
                f.Add(new Finding
                {
                    FindingId = Id(), RuleId = "INCIDENT-CHAIN",
                    Title = $"Lanț de incident {T(cluster[0]):yyyy-MM-dd HH:mm} – {T(cluster[^1]):HH:mm} UTC ({cluster.Count} constatări legate)",
                    Severity = cluster.Any(c => c.RuleId == "DEF-DETECTION") && cluster.Any(c => c.RuleId is "EXEC-SCRIPT-VIA-LOLBIN" or "NET-LOLBIN-TRAFFIC" or "PERSIST-TASK" or "LOG-TAMPER")
                        ? Severity.Critical : Severity.High,
                    Category = "Incident", Classification = Classification.Correlated, Confidence = Confidence.Medium,
                    FirstSeenUtc = T(cluster[0]), LastSeenUtc = T(cluster[^1]),
                    Description = string.Join(" → ", cluster.Select(c => $"[{T(c):HH:mm}] {c.Title}")),
                    ClassificationReason = "Constatări din surse diferite (Prefetch, SRUM, Defender, jurnale) concentrate în aceeași fereastră de timp.",
                    SupportingEvidence = cluster.SelectMany(c => c.SupportingEvidence.Take(3)).ToList(),
                    RelatedFindingIds = cluster.Select(c => c.FindingId).ToList(),
                    AlternativeExplanations = ["Coincidență temporală a unor activități fără legătură; verificați fiecare pas."],
                    RecommendedNextSteps = ["Reconstituiți fiecare pas din probele indicate.", "Stabiliți ce date au părăsit stația în fereastra lanțului."],
                });
            cluster.Clear();
        }
        foreach (var x in timed)
        {
            if (cluster.Count > 0 && T(x) - T(cluster[^1]) > TimeSpan.FromHours(3)) Flush();
            cluster.Add(x);
        }
        Flush();

        return f.OrderByDescending(x => x.Severity).ThenBy(x => x.FirstSeenUtc ?? x.LastSeenUtc).ToList();
    }

    /// <summary>"containerfile:_C:\x.zip; file:_C:\x.zip->inner" → "C:\x.zip".</summary>
    public static string DefenderContainer(string path)
    {
        // Behaviour detections list "process:_pid:…" before the file: prefer the container, then the file segment.
        var parts = path.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var first = parts.FirstOrDefault(s => s.StartsWith("containerfile:_", StringComparison.OrdinalIgnoreCase))
                    ?? parts.FirstOrDefault(s => s.StartsWith("file:_", StringComparison.OrdinalIgnoreCase))
                    ?? parts.FirstOrDefault() ?? "";
        foreach (var prefix in new[] { "containerfile:_", "file:_", "process:_" })
            if (first.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) first = first[prefix.Length..];
        var arrow = first.IndexOf("->", StringComparison.Ordinal);
        return arrow > 0 ? first[..arrow] : first;
    }

    /// <summary>Prefetch paths start with \VOLUME{guid}\; compare on the part after the volume.</summary>
    public static string Normalize(string path)
    {
        var p = path.Replace('/', '\\');
        if (p.StartsWith("\\VOLUME{", StringComparison.OrdinalIgnoreCase)) { var i = p.IndexOf('}'); if (i > 0) p = p[(i + 1)..]; }
        else if (p.Length > 2 && p[1] == ':') p = p[2..];
        return p.ToUpperInvariant();
    }

    private static string Trunc(string s, int n) { s = s.ReplaceLineEndings(" "); return s.Length <= n ? s : s[..n] + "…"; }
}
