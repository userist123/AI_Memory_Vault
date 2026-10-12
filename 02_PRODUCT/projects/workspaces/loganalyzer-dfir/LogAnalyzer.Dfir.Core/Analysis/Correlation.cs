using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Correlation rules over the unified timeline. Each rule yields findings that point to the exact evidence rows
/// behind them (EvidenceId + locator). Classification follows the spec: DIRECT = one record shows it; CORRELATED =
/// independent sources agree; CANDIDATE = worth an analyst's look, not proven.
/// </summary>
public static class Correlation
{
    /// <summary>
    /// A program in a user-writable folder is linked to a grave finding about the same folder only when both lie within this
    /// interval of each other (inclusive). User folders such as %TEMP% collect unrelated files for months; without a time limit
    /// any program there would be "linked" to any script there.
    /// </summary>
    public static readonly TimeSpan SharedFolderCorrelationWindow = TimeSpan.FromHours(24);

    private static readonly string[] UserWritableMarkers =
        [@"\programdata\", @"\appdata\", @"\temp\", @"\downloads\", @"\users\public\", @"\windows\temp\", @"\$recycle.bin\"];

    /// <summary>Signed Microsoft binaries frequently used to run attacker code or move data ("living off the land").</summary>
    internal static readonly HashSet<string> Lolbins = new(StringComparer.OrdinalIgnoreCase)
    {
        "msbuild.exe", "regsvr32.exe", "rundll32.exe", "mshta.exe", "installutil.exe", "regasm.exe", "regsvcs.exe", "cmstp.exe",
        "wscript.exe", "cscript.exe", "certutil.exe", "bitsadmin.exe", "msiexec.exe", "powershell.exe", "pwsh.exe", "wmic.exe",
        "forfiles.exe", "odbcconf.exe", "csc.exe", "vbc.exe", "jsc.exe", "msxsl.exe", "curl.exe",
    };

    private static readonly string[] ScriptExtensions = [".cmd", ".bat", ".vbs", ".js", ".ps1", ".hta", ".wsf"];

    /// <summary>Writable by design but owned by Microsoft components (Defender platform, installer caches, PowerShell's own policy probes).</summary>
    private static readonly string[] KnownBenignMarkers =
        [@"\programdata\microsoft\", @"\programdata\package cache\", "__psscriptpolicytest_", @"\appdata\local\microsoft\windowsapps\",
         // Defender's platform and ATP folders are ACL-protected (not user-writable) even though they sit under ProgramData.
         @"\programdata\microsoft\windows defender\platform\", @"\programdata\microsoft\windows defender advanced threat protection\"];

    /// <summary>Environment variables in task/service commands that resolve to user-writable folders (not expanded on this machine).</summary>
    private static readonly (string Var, string Folder)[] WritableEnvVars =
        // No trailing separator: the variable is followed by its own "\" in the path ("%ProgramData%\Microsoft").
        [("%localappdata%", @"\appdata\local"), ("%appdata%", @"\appdata\roaming"), ("%temp%", @"\temp"), ("%tmp%", @"\temp"),
         ("%programdata%", @"\programdata"), ("%public%", @"\users\public"), ("%allusersprofile%", @"\programdata")];

    /// <summary>Failed-logon burst rules: at least this many failures (brute force) or distinct accounts (spray) ...</summary>
    internal const int CredentialThreshold = 10;
    /// <summary>... inside this window.</summary>
    internal static readonly TimeSpan CredentialWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The densest window of at most <paramref name="window"/> over time-sorted items whose count (or distinct key count) reaches
    /// <paramref name="n"/>; returns the item index range, or null.
    /// </summary>
    internal static (int Start, int End)? FindBurst(IReadOnlyList<(DateTimeOffset Time, string Key)> items, int n, TimeSpan window, bool distinct)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int l = 0, best = 0; (int, int)? range = null;
        for (int r = 0; r < items.Count; r++)
        {
            counts[items[r].Key] = counts.GetValueOrDefault(items[r].Key) + 1;
            while (items[r].Time - items[l].Time > window)
            {
                if (--counts[items[l].Key] == 0) counts.Remove(items[l].Key);
                l++;
            }
            int metric = distinct ? counts.Count : r - l + 1;
            if (metric > best) { best = metric; range = (l, r); }
        }
        return best >= n ? range : null;
    }

    private static readonly System.Text.RegularExpressions.Regex ScriptPathToken = new(@"[^\s""'<>|;,()]+", System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    /// <summary>True when a path in the text ENDS with a script extension (".js" must not match ".json" or ".jsx").</summary>
    public static bool HasScriptPath(string text)
    {
        foreach (System.Text.RegularExpressions.Match m in ScriptPathToken.Matches(text))
            if (ScriptExtensions.Any(x => m.Value.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex DefenderCommand = new(
        @"(?:set|add)-mppreference\b[^\r\n]*?-(?:exclusion(?:path|extension|process|ipaddress)\b|disable(?:realtimemonitoring|behaviormonitoring|ioavprotection|scriptscanning|blockatfirstseen|intrusionpreventionsystem|archivescanning)\s*[: ]\s*(?:\$true|1)\b)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static readonly System.Text.RegularExpressions.Regex DefenderWeakeningValue = new(
        @"exclusions|\bDisable(?:RealtimeMonitoring|BehaviorMonitoring|IOAVProtection|ScriptScanning|OnAccessProtection|AntiSpyware|AntiVirus|BlockAtFirstSeen|ArchiveScanning)\s*=\s*(?:0x)?0*1\b|TamperProtection\s*=\s*(?:0x)?0*4\b|SpynetReporting\s*=\s*(?:0x)?0+\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static readonly System.Text.RegularExpressions.Regex WmiTypedName = new(
        @"^\s*(?:(?<type>[A-Za-z_]+)\.)?Name\s*=\s*""(?<name>[^""]*)""\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// True only for the subscription Windows registers itself: the consumer / filter NAME field is exactly "SCM Event Log Consumer" / "SCM Event Log Filter"
    /// (an NTEventLogEventConsumer / __EventFilter). A command line or script that merely mentions the phrase is not built-in.
    /// </summary>
    private static bool IsBuiltInWmiSubscription(string consumer)
    {
        var m = WmiTypedName.Match(consumer);
        var name = (m.Success ? m.Groups["name"].Value : consumer).Trim();
        var type = m.Success ? m.Groups["type"].Value : "";
        if (name.Equals("SCM Event Log Consumer", StringComparison.OrdinalIgnoreCase))
            return type.Length == 0 || type.Equals("NTEventLogEventConsumer", StringComparison.OrdinalIgnoreCase);
        if (name.Equals("SCM Event Log Filter", StringComparison.OrdinalIgnoreCase))
            return type.Length == 0 || type.Equals("__EventFilter", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>A Defender 5007 "new value" that weakens protection: an exclusion, a protection-disabling switch (explicit list) set to 1, Tamper Protection off, cloud reporting off.</summary>
    private static bool WeakensDefender(string newValue) => newValue.Length > 0 && DefenderWeakeningValue.IsMatch(newValue);

    private static readonly System.Text.RegularExpressions.Regex CommandExecutableRx = new(
        @"^\s*(?:""(?<q>[^""]*)""|(?<p>.+?\.(?:exe|sys|dll)(?=\s|$)|\S+))",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// The executable of a service ImagePath / task command: the quoted first token, or the unquoted path up to the first .exe/.sys/.dll followed
    /// by a space or the end. Arguments (log paths, config folders) are not part of the location being classified.
    /// </summary>
    public static string CommandExecutable(string command)
    {
        var m = CommandExecutableRx.Match(command ?? "");
        return !m.Success ? "" : (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["p"].Value).Trim();
    }

    /// <summary>True when the executable of a command line (not its arguments) is in a user-writable location.</summary>
    public static bool IsUserWritableCommand(string command) => IsUserWritable(CommandExecutable(command));

    public static bool IsUserWritable(string path)
    {
        var p = path.Replace('/', '\\').ToLowerInvariant();
        foreach (var (v, folder) in WritableEnvVars) p = p.Replace(v, folder);
        return UserWritableMarkers.Any(p.Contains) && !KnownBenignMarkers.Any(p.Contains);
    }

    /// <param name="maintenancePolicy">Approved log-clearing accounts and maintenance windows from the procedure profile; <c>null</c> = not defined yet.</param>
    /// <param name="profile">The procedure profile (approved software, ...) for the WP11 rules; <c>null</c> = none, nothing is approved.</param>
    public static List<Finding> Run(IReadOnlyList<TimelineEvent> events, LogMaintenancePolicy? maintenancePolicy = null, ProcedureProfile? profile = null)
    {
        var f = new List<Finding>();
        int n = 0;
        string Id() => $"F-{++n:D4}";
        EvidenceRef Ref(TimelineEvent e, string d) => new(e.EvidenceId, e.Locator, d);
        string F(TimelineEvent e, params string[] keys) => Wp11Rules.F(e, keys);
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
                Description = $"{LogAnalyzer.Dfir.Language.RoText.Count(g.Count(), "eveniment", "evenimente")} Defender pentru {g.Key}: {string.Join(", ", threats.Take(15))}. Acțiuni: {string.Join(", ", g.Select(e => F(e, "Action Name")).Where(a => a.Length > 0).Distinct())}.",
                ClassificationReason = "Înregistrare directă în jurnalul Defender.",
                SupportingEvidence = g.Take(20).Select(e => Ref(e, $"Defender {e.EventId}")).ToList(),
                RecommendedNextSteps = ["Verificați dacă fișierul mai există și dacă rulează.", "Căutați același hash pe alte stații."],
            });
        }
        // 5001/5010/5012 = protection turned off; 5013 = Tamper Protection blocked a change; 5007 = a setting changed (only weakening values count);
        // PowerShell / command lines that add exclusions or switch protection off are the same act seen from the actor's side.
        const string DefenderChannel = "Microsoft-Windows-Windows Defender/Operational";
        var tamper = events.Where(e => Ev(e, DefenderChannel, 5001, 5010, 5012, 5013) ||
                                       Ev(e, DefenderChannel, 5007) && WeakensDefender(F(e, "New Value", "NewValue"))).ToList();
        var tamperCommands = Wp11Rules.ProcessStarts(events).Where(p => DefenderCommand.IsMatch(p.CommandLine)).Select(p => p.Event)
            .Concat(events.Where(e => Ev(e, "Microsoft-Windows-PowerShell/Operational", 4104) && DefenderCommand.IsMatch(F(e, "ScriptBlockText")))).ToList();
        if (tamper.Count + tamperCommands.Count > 0)
        {
            var all = tamper.Concat(tamperCommands).Where(e => e.Time.Utc is not null).ToList();
            string Describe(TimelineEvent e) => e.EventId switch
            {
                "5007" => $"{(F(e, "New Value", "NewValue").Contains("Exclusions", StringComparison.OrdinalIgnoreCase) ? "excludere" : "setare slăbită")}: {F(e, "New Value", "NewValue")}",
                "5013" => "eveniment 5013 (Tamper Protection a blocat o modificare)",
                "4688" or "1" or "4104" => $"comandă: {Trunc(F(e, "CommandLine", "ScriptBlockText"), 160)}",
                _ => $"eveniment {e.EventId}",
            };
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "DEF-TAMPER", Title = "Microsoft Defender dezactivat, slăbit sau cu excluderi adăugate",
                Severity = Severity.High, Category = "Defense Evasion", Classification = Classification.Direct, Confidence = Confidence.High,
                FirstSeenUtc = all.Count > 0 ? all.Min(e => e.Time.Utc) : null, LastSeenUtc = all.Count > 0 ? all.Max(e => e.Time.Utc) : null, MitreTechniqueId = "T1562.001",
                Description = string.Join("; ", tamper.Concat(tamperCommands).Take(10).Select(Describe)) +
                              ". Ce s-a întâmplat: protecția antivirus a fost oprită sau slăbită, ori s-a încercat. De ce contează: atacatorii o fac înainte să ruleze programe dăunătoare, ca să nu fie detectați.",
                ClassificationReason = "Înregistrări directe: jurnalul Defender (5001 protecție dezactivată, 5007 configurație modificată, 5013 modificare blocată de Tamper Protection) sau o linie de comandă / script PowerShell care apelează comenzile Defender de modificare a preferințelor (din lista de date a regulii).",
                SupportingEvidence = tamper.Concat(tamperCommands).Take(20).Select(e => Ref(e, $"Defender {e.EventId}")).ToList(),
                AlternativeExplanations = ["Excludere sau modificare făcută intenționat de un administrator pentru un program legitim."],
                RecommendedNextSteps = ["Stabiliți cine a modificat setarea (Security 4688/4657, contul din eveniment) și dacă există o justificare aprobată.", "Verificați starea actuală a protecției și a listei de excluderi în Windows Security."],
            });
        }

        // 2. Log tampering: cleared logs and record-id gaps (ported from the 2026-08-08 prototype's LogIntegrityService).
        var clears = events.Where(e => Ev(e, "Security", 1102) || Ev(e, "System", 104) || Ev(e, "Security", 1100) || Ev(e, "Security", 4719)).ToList();
        // The severity of a cleared log depends on other findings (a clear next to a High finding is worse), so the finding is
        // built after sections 1-7 have run (see below); its id is reserved here to keep the numbering stable.
        var logTamperId = clears.Count > 0 ? Id() : "";

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
        foreach (var e in services.Where(e => IsUserWritableCommand(F(e, "ImagePath") + F(e, "ServiceFileName"))))
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-SERVICE-USERPATH", Title = $"Serviciu instalat dintr-o locație în care utilizatorii pot scrie: {F(e, "ServiceName")}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1543.003",
                FirstSeenUtc = e.Time.Utc, File = F(e, "ImagePath") + F(e, "ServiceFileName"), User = F(e, "AccountName"),
                Description = $"Serviciul {F(e, "ServiceName")} rulează {F(e, "ImagePath")}{F(e, "ServiceFileName")} ca {F(e, "AccountName")}{F(e, "ServiceAccount")}.",
                ClassificationReason = "Eveniment de instalare a serviciului cu cale în ProgramData/AppData/Temp.",
                SupportingEvidence = [Ref(e, "instalare serviciu")],
            });
        // TaskScheduler 106 (registered) carries no action: the command comes from 4698 (TaskContent XML) or from 200 (ActionName) / 129 (Path),
        // which are matched to the registration by (host, task name). 200 / 129 occur at EVERY task start, so they never create a finding on
        // their own: they only supply the action text and the "ran at" note of a task whose creation (106 / 4698) is in the log.
        const string TaskChannel = "Microsoft-Windows-TaskScheduler/Operational";
        var taskEvents = events.Where(e => Ev(e, "Security", 4698) || Ev(e, TaskChannel, 106, 200, 129)).ToList();
        foreach (var g in taskEvents.GroupBy(e => (e.Host.ToLowerInvariant(), Task: F(e, "TaskName").ToLowerInvariant())).Where(g => g.Key.Task.Length > 0))
        {
            var creations = g.Where(e => Ev(e, "Security", 4698) || Ev(e, TaskChannel, 106)).OrderBy(e => e.Time.Utc).ToList();
            if (creations.Count == 0) continue;
            var withAction = g.Where(e => !Ev(e, TaskChannel, 106)).ToList();
            var actionText = string.Join(" ", withAction.Select(e => F(e, "TaskContent") + " " + F(e, "ActionName") + " " + F(e, "Path")));
            if (!(IsUserWritable(actionText) || HasScriptPath(actionText))) continue;
            var created = creations[0];
            var taskName = F(created, "TaskName");
            var user = (F(created, "SubjectUserName") + F(created, "UserContext")).Trim();
            var ran = g.Where(e => Ev(e, TaskChannel, 200, 129)).OrderBy(e => e.Time.Utc).FirstOrDefault();
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-TASK", Title = $"Activitate planificată care rulează un script sau pornește dintr-o locație în care utilizatorii pot scrie: {taskName}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1053.005",
                FirstSeenUtc = created.Time.Utc, User = user,
                Description = $"Activitatea planificată {taskName} a fost creată {(user.Length > 0 ? "de " + user : "(creatorul nu apare în jurnal)")}" +
                              (ran?.Time.Utc is { } at ? $"; a rulat la {at:yyyy-MM-dd HH:mm} UTC" : "") +
                              ". Acțiunea indică un script sau o cale în care utilizatorii pot scrie. Ce verificați: ce rulează activitatea planificată, cine a creat-o și dacă a pornit (TaskScheduler 200).",
                ClassificationReason = "Eveniment de creare a activității planificate (106 / 4698) și acțiunea lui (4698 TaskContent, 200, 129): indică un script sau o cale în care utilizatorii pot scrie.",
                SupportingEvidence = g.Take(6).Select(e => Ref(e, $"eveniment {e.EventId} al activității planificate")).ToList(),
            });
        }

        // Task definitions (System32\Tasks XML): configuration that runs a program from a user-writable location.
        foreach (var e in events.Where(e => e.Source == "ScheduledTask" && e.Path.Length > 0 &&
                                            (IsUserWritable(e.Path) || HasScriptPath(F(e, "Arguments")) && IsUserWritable(F(e, "Arguments")))))
        {
            bool hidden = F(e, "Hidden") == "true";
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-TASK-CONFIG", Title = $"Activitate planificată{(hidden ? " ascunsă" : "")} care rulează dintr-o locație în care utilizatorii pot scrie: {e.Task}",
                Severity = hidden ? Severity.High : Severity.Medium, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1053.005", FirstSeenUtc = e.Time.Utc, File = e.Path, Process = e.Process, User = e.User,
                Description = $"{e.Task} rulează {F(e, "Command")} {F(e, "Arguments")} ca {e.User} ({F(e, "RunLevel")}); declanșatori: {F(e, "Triggers")}; autor {F(e, "Author")}.",
                ClassificationReason = "Definiția activității planificate (XML din System32\\Tasks) indică o cale în care utilizatorii pot scrie. Arată configurația, nu o rulare.",
                SupportingEvidence = [Ref(e, "definiția activității planificate")],
                AlternativeExplanations = ["Actualizatoare legitime instalate per utilizator (AppData) creează astfel de activități planificate."],
                MissingEvidence = ["Rulări ale activității planificate: TaskScheduler/Operational (200/201) sau Prefetch pentru executabil."],
                ContradictingEvidence = F(e, "Enabled") == "false" ? ["Activitatea planificată este dezactivată (Settings/Enabled=false): în această stare nu pornește singură."] : [],
            });
        }

        // Services and drivers configured in the SYSTEM hive whose binary or ServiceDll is in a user-writable location.
        foreach (var e in events.Where(e => e.Source == "Service" && (IsUserWritable(e.Path) || IsUserWritable(F(e, "ServiceDll")))))
        {
            var file = IsUserWritable(F(e, "ServiceDll")) ? F(e, "ServiceDll") : e.Path;
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-SERVICE-CONFIG", Title = $"Serviciu configurat dintr-o locație în care utilizatorii pot scrie: {e.Service}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1543.003", FirstSeenUtc = e.Time.Utc, File = file, User = e.User,
                Description = $"{e.Service} ({F(e, "ServiceType")}, {F(e, "StartMode")}) rulează {F(e, "ImagePath")}" +
                              (F(e, "ServiceDll").Length > 0 ? $" cu ServiceDll {F(e, "ServiceDll")}" : "") + $" ca {e.User}.",
                ClassificationReason = "Configurația serviciului din hive-ul SYSTEM indică o cale în care utilizatorii pot scrie. Arată configurația, nu o rulare.",
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
                Title = $"Regulă de firewall care permite {(inbound ? "intrarea" : "ieșirea")} pentru un program dintr-o locație în care utilizatorii pot scrie: {WinPath.GetFileName(F(e, "ApplicationPath"))}",
                Severity = inbound ? Severity.High : Severity.Medium, Category = "DefenseEvasion", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1562.004", FirstSeenUtc = e.Time.Utc, File = F(e, "ApplicationPath"), User = F(e, "ModifyingUser"),
                Description = $"Regula „{F(e, "RuleName")}” ({(inbound ? "Inbound" : "Outbound")}, Allow) pentru {F(e, "ApplicationPath")}, adăugată/modificată de {F(e, "ModifyingApplication")}.",
                ClassificationReason = "Evenimentul de firewall arată o regulă de tip Allow pentru o cale în care utilizatorii pot scrie.",
                SupportingEvidence = [Ref(e, "regulă firewall")],
                AlternativeExplanations = ["Aplicații per utilizator (de ex. jocuri, clienți de chat) își adaugă reguli la instalare."],
            });
        }

        // WMI event subscriptions: WMI-Activity 5861 (permanent event consumer registered) and Sysmon 19/20/21 (filter / consumer / binding).
        // Built-in consumers that Windows registers itself are left out.
        var wmiSubs = events.Where(e => (Ev(e, "Microsoft-Windows-WMI-Activity/Operational", 5861) || Ev(e, Wp11Rules.Sysmon, 19, 20, 21)) &&
                                        !IsBuiltInWmiSubscription(F(e, "CONSUMER", "Consumer", "Name", "ESS"))).ToList();
        foreach (var g in wmiSubs.GroupBy(e => F(e, "CONSUMER", "Consumer", "Name", "ESS") is { Length: > 0 } k ? k : "(fără nume) " + e.EventId, StringComparer.OrdinalIgnoreCase))
        {
            var first = g.OrderBy(e => e.Time.Utc).First();
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-WMI", Title = $"Abonament WMI permanent înregistrat: {Trunc(g.Key, 80)}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1546.003",
                FirstSeenUtc = g.Min(e => e.Time.Utc), LastSeenUtc = g.Max(e => e.Time.Utc), User = first.User,
                Description = $"Ce s-a întâmplat: Windows a înregistrat un abonament WMI permanent (evenimente {string.Join(", ", g.Select(e => e.EventId).Distinct())}): {Trunc(Wp11Rules.AllText(first), 240)}. " +
                              "De ce contează: un abonament WMI rulează o comandă sau un script la un eveniment (pornire, oră), fără fișier vizibil, și supraviețuiește repornirii. Ce verificați: consumatorul (comanda sau scriptul) și cine l-a creat.",
                ClassificationReason = "Înregistrare directă a unui consumator / filtru / legături WMI permanente (WMI-Activity 5861, Sysmon 19-21).",
                SupportingEvidence = g.Take(10).Select(e => Ref(e, $"WMI {e.EventId}")).ToList(),
                AlternativeExplanations = ["Software de administrare sau de monitorizare legitim înregistrează abonamente WMI (de exemplu agenți de inventar)."],
                RecommendedNextSteps = ["Listați abonamentele cu Get-CimInstance -Namespace root/subscription -ClassName __EventConsumer și verificați comanda.", "Stabiliți contul și procesul care l-au creat."],
            });
        }

        // Registry autostarts (Run/RunOnce, Winlogon, IFEO) from saved hives.
        foreach (var e in events.Where(e => e.Source == "RunKey" && IsUserWritable(e.Path)))
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PERSIST-RUNKEY-USERPATH", Title = $"Pornire automată dintr-o locație în care utilizatorii pot scrie: {F(e, "ValueName")}",
                Severity = Severity.Medium, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                MitreTechniqueId = "T1547.001", FirstSeenUtc = e.Time.Utc, File = e.Path, Process = e.Process,
                Description = $"{F(e, "Hive")}\\{F(e, "Key")}: {F(e, "ValueName")} = {F(e, "Command")}",
                ClassificationReason = "Valoarea din cheia Run/RunOnce indică o cale în care utilizatorii pot scrie. Ora este LastWriteTime al cheii.",
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
                    FindingId = "", RuleId = "EXEC-USERPATH", Title = $"Program rulat dintr-o locație în care utilizatorii pot scrie: {e.Process}",
                    Severity = Severity.Medium, Category = "Execution", Classification = Classification.Direct, Confidence = Confidence.High,
                    LastSeenUtc = e.Time.Utc, File = exePath, Process = e.Process,
                    Description = $"{exePath} a rulat de {F(e, "RunCount")} ori; ultima rulare {e.Time.Utc:yyyy-MM-dd HH:mm} UTC.",
                    ClassificationReason = "Fișierul Prefetch al programului (execuție dovedită).", SemanticType = SemanticType.Execution,
                    SupportingEvidence = [Ref(e, "Prefetch")],
                    AlternativeExplanations = ["Instalator sau aplicație legitimă instalată per utilizator."],
                });
            // No exclusion by folder name: a script run from a user-writable folder is always reported, even when it looks like one of our own
            // (the LADFIR_REMOTE_ folders seen on a developer PC come from the test suite, and any name can be imitated by an attacker).
            var scripts = refsFiles.Where(r => ScriptExtensions.Any(x => r.EndsWith(x, StringComparison.OrdinalIgnoreCase)) && IsUserWritable(r)).ToList();
            if (scripts.Count > 0 && Lolbins.Contains(e.Process))
                f.Add(new Finding
                {
                    FindingId = Id(), RuleId = "EXEC-SCRIPT-VIA-LOLBIN", Title = $"{e.Process} a lucrat cu scripturi din locații în care utilizatorii pot scrie",
                    Severity = Severity.High, Category = "Execution", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1218",
                    LastSeenUtc = e.Time.Utc, Process = e.Process, File = string.Join("; ", scripts.Take(5)),
                    Description = $"Prefetch-ul lui {e.Process} face referire la: {string.Join(", ", scripts.Take(10))}.",
                    ClassificationReason = "Fișiere referite în Prefetch în timpul execuției programului.",
                    SupportingEvidence = [Ref(e, "Prefetch ReferencedFiles")],
                });
        }

        // 4b. BAM (execution) and Amcache (presence) from user-writable locations, one entry per path.
        foreach (var e in events.Where(e => e.Source is "BAM" or "Amcache" && IsUserWritable(e.Path))
                                .GroupBy(e => (e.Source, e.Path.ToLowerInvariant())).Select(g => g.OrderByDescending(x => x.Time.Utc).First()))
            userPathExec.Add(new Finding
            {
                FindingId = "", RuleId = "EXEC-USERPATH", Title = $"{(e.Source == "BAM" ? "Program rulat" : "Program prezent")} dintr-o locație în care utilizatorii pot scrie: {e.Process}",
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
                    Title = Lolbins.Contains(exe) ? $"{exe} (unealtă Windows abuzată frecvent) a transferat date în rețea" : $"{exe} (dintr-o locație în care utilizatorii pot scrie) a trimis {sent / 1_000_000.0:0.#} MB",
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
        // Brute force: the same user failing from the same address CredentialThreshold times inside CredentialWindow.
        var failures = events.Where(e => Ev(e, "Security", 4625) && e.Time.Utc is not null).ToList();
        foreach (var g in failures.GroupBy(e => (F(e, "IpAddress"), F(e, "TargetUserName"))))
        {
            var ordered = g.OrderBy(e => e.Time.Utc).ToList();
            if (FindBurst(ordered.Select(e => (e.Time.Utc!.Value, "")).ToList(), CredentialThreshold, CredentialWindow, distinct: false) is not { } b) continue;
            var inWindow = ordered.Skip(b.Start).Take(b.End - b.Start + 1).ToList();
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "CRED-BRUTEFORCE", Title = $"{inWindow.Count} autentificări eșuate pentru {g.Key.Item2} de la {g.Key.Item1}",
                Severity = Severity.Medium, Category = "Credential Access", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1110",
                FirstSeenUtc = inWindow[0].Time.Utc, LastSeenUtc = inWindow[^1].Time.Utc, Ip = g.Key.Item1, User = g.Key.Item2,
                Description = $"Între {inWindow[0].Time.Utc:yyyy-MM-dd HH:mm} și {inWindow[^1].Time.Utc:yyyy-MM-dd HH:mm} UTC (cel puțin {CredentialThreshold} încercări în {CredentialWindow.TotalMinutes:0} minute; în total {g.Count()} în probe).",
                ClassificationReason = "Evenimente 4625 repetate pentru același cont și aceeași adresă, într-o fereastră scurtă.",
                SupportingEvidence = inWindow.Take(15).Select(e => Ref(e, "4625")).ToList(),
            });
        }
        // Password spraying: one address tries many different accounts (few passwords each), which per-account counting never sees.
        foreach (var g in failures.Where(e => F(e, "TargetUserName").Length > 0 && F(e, "TargetUserName") != "-" && Wp11Rules.IsRemoteAddress(F(e, "IpAddress")))
                                  .GroupBy(e => F(e, "IpAddress")))
        {
            var ordered = g.OrderBy(e => e.Time.Utc).ToList();
            if (FindBurst(ordered.Select(e => (e.Time.Utc!.Value, F(e, "TargetUserName").ToLowerInvariant())).ToList(), CredentialThreshold, CredentialWindow, distinct: true) is not { } b) continue;
            var inWindow = ordered.Skip(b.Start).Take(b.End - b.Start + 1).ToList();
            var users = inWindow.Select(e => F(e, "TargetUserName")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "CRED-SPRAY", Title = $"Posibil password spraying: {users.Count} conturi diferite încercate de la {g.Key}",
                Severity = Severity.High, Category = "Credential Access", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1110.003",
                FirstSeenUtc = inWindow[0].Time.Utc, LastSeenUtc = inWindow[^1].Time.Utc, Ip = g.Key,
                Description = $"Între {inWindow[0].Time.Utc:yyyy-MM-dd HH:mm} și {inWindow[^1].Time.Utc:yyyy-MM-dd HH:mm} UTC, de la {g.Key} au eșuat autentificări pentru {users.Count} conturi diferite ({string.Join(", ", users.Take(10))}{(users.Count > 10 ? ", …" : "")}). " +
                              "Ce s-a întâmplat: aceeași adresă a încercat puține parole pe multe conturi. De ce contează: așa se evită blocarea contului și se găsesc conturi cu parole slabe. Ce verificați: dacă după serie există o autentificare reușită (4624) de la aceeași adresă.",
                ClassificationReason = $"Cel puțin {CredentialThreshold} conturi distincte cu 4625 de la aceeași adresă în {CredentialWindow.TotalMinutes:0} minute.",
                SupportingEvidence = inWindow.Take(15).Select(e => Ref(e, "4625")).ToList(),
                AlternativeExplanations = ["Un server sau o aplicație care validează conturi cu parole expirate (serviciu, scaner de vulnerabilități autorizat)."],
                RecommendedNextSteps = ["Căutați 4624 de la aceeași adresă după serie.", "Blocați sau monitorizați adresa dacă este externă; resetați parolele conturilor reușite."],
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
        // One finding per script block (blocks split over several 4104 records share a ScriptBlockId). The keyword list and weights are data
        // (Data/rule_lists.json); Windows marks blocks with suspicious APIs as Warning (Level 3), which counts on its own.
        var psCfg = Wp11Data.Default.Lists.Powershell;
        var psBlocks = events.Where(e => Ev(e, "Microsoft-Windows-PowerShell/Operational", 4104))
            .GroupBy(e => F(e, "ScriptBlockId") is { Length: > 0 } sid ? "id:" + sid : "ev:" + e.EvidenceId + e.Locator).ToList();
        var psFindings = new List<(int Score, Finding Finding)>();
        foreach (var blk in psBlocks)
        {
            var parts = blk.OrderBy(e => int.TryParse(F(e, "MessageNumber"), out var mn) ? mn : 0).ToList();
            var text = string.Concat(parts.Select(e => F(e, "ScriptBlockText")));
            var hitKw = psCfg.Keywords.Where(k => k.Text.Length > 0 && text.Contains(k.Text, StringComparison.OrdinalIgnoreCase)).ToList();
            int score = hitKw.Sum(k => k.Weight);
            bool warned = parts.Any(e => F(e, "_xLevel") == "3");
            // Two distinct indicators, or one heavy one, or Windows' own Warning flag; a single weight-2 keyword (an installer's iex) is not enough.
            if (hitKw.Count < 2 && score < psCfg.MinScore && !warned) continue;
            bool amsi = hitKw.Any(k => k.High);
            var first = parts[0];
            var mitre = hitKw.OrderByDescending(k => k.Weight).Select(k => k.Mitre).FirstOrDefault(m => m.Length > 0) ?? "T1059.001";
            psFindings.Add((score, new Finding
            {
                FindingId = Id(), RuleId = "PS-SUSPICIOUS",
                Title = hitKw.Count > 0 ? $"Bloc PowerShell suspect: {string.Join(", ", hitKw.OrderByDescending(k => k.Weight).Take(4).Select(k => k.Text.Trim()))}" : "Bloc PowerShell marcat de Windows ca suspect (nivel Avertisment)",
                Severity = amsi || score >= psCfg.HighScore ? Severity.High : Severity.Medium, Category = "Execution", Classification = Classification.Candidate, Confidence = Confidence.Medium, MitreTechniqueId = mitre,
                FirstSeenUtc = first.Time.Utc, LastSeenUtc = parts[^1].Time.Utc, User = first.User,
                Description = $"Ce s-a întâmplat: un script PowerShell conține {(hitKw.Count > 0 ? "tehnici folosite des la descărcare, ofuscare sau dezactivarea protecției (" + string.Join(", ", hitKw.Select(k => k.Text.Trim())) + ")" : "apeluri pe care Windows însuși le marchează ca suspecte (nivel Warning)")}. " +
                              $"De ce contează: aceste tehnici apar în atacuri fără fișiere, dar și în scripturi de administrare. Ce verificați: contul, procesul părinte și scopul scriptului. Începutul: {Trunc(text, 200)}",
                ClassificationReason = hitKw.Count > 0 ? $"Cuvinte-cheie ponderate în textul scriptului (scor {score}); Script Block Logging nu arată rezultatul execuției." : "Evenimentul 4104 are nivelul Warning, setat de Windows pentru conținut suspect.",
                SupportingEvidence = parts.Take(5).Select(e => Ref(e, "4104")).ToList(),
                AlternativeExplanations = ["Scripturi de administrare sau de instalare legitime."],
                RecommendedNextSteps = ["Citiți scriptul complet și identificați contul și procesul care l-au lansat."],
            }));
        }
        foreach (var (_, pf) in psFindings.OrderByDescending(x => x.Score).ThenBy(x => x.Finding.FirstSeenUtc).Take(100)) f.Add(pf);
        if (psFindings.Count > 100)
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "PS-SUSPICIOUS", Title = $"Alte {psFindings.Count - 100} blocuri PowerShell suspecte nu au fost listate individual",
                Severity = Severity.Low, Category = "Execution", Classification = Classification.Candidate, Confidence = Confidence.Low, MitreTechniqueId = "T1059.001",
                Description = "Raportul listează primele 100 de blocuri după scor; restul sunt în cronologie (evenimente 4104).",
                ClassificationReason = "Limită de afișare, nu o concluzie.",
            });

        // 7b. WP11 Tier 1: SMB shares, account lifecycle, remote administration, security agents, snapshot deletion, DNS, remote-access tools.
        f.AddRange(Wp11Rules.Run(events, f.ToList(), Id, profile));

        if (clears.Count > 0)
        {
            // System 1074 / 6006 / 6005 pair a Security 1100 with an ordinary shutdown or restart (owner decision 27).
            var systemEvents = events.Where(e => e.Time.Utc is not null && e.EventId is "1074" or "6006" or "6005" && e.Source.Equals("EventLog:System", StringComparison.OrdinalIgnoreCase))
                                     .Select(e => new SystemLifecycleEvent(e.Time.Utc!.Value, "System", int.Parse(e.EventId))).ToList();
            f.Add(BuildLogTamper(logTamperId, clears, f, maintenancePolicy, systemEvents, (e, k) => F(e, k)));
        }

        // 8. Executions from user-writable locations: individual findings only when another signal touches them.
        static DateTimeOffset? T(Finding x) => x.FirstSeenUtc ?? x.LastSeenUtc;
        var strong = f.Where(x => x.Severity >= Severity.High && T(x) is not null).ToList();
        var correlated = new HashSet<Finding>();
        // The artifact behind each entry: Prefetch and BAM record a run, Amcache only that the program was present.
        static string ExecSource(Finding x) => x.SupportingEvidence.FirstOrDefault()?.Description is { Length: > 0 } d ? d : "?";
        static bool Ran(Finding x) => x.SemanticType == SemanticType.Execution;
        foreach (var x in userPathExec)
        {
            var dir = WinPath.GetDirectoryName(Normalize(x.File)) ?? "";
            var touching = strong.Where(s =>
                // A shared folder links two findings only when they are also close in time: %TEMP% holds unrelated files for months.
                (s.File.Length > 0 && dir.Length > 3 && Normalize(s.File).StartsWith(dir, StringComparison.OrdinalIgnoreCase)
                 && T(s) is { } sf && T(x) is { } xf && (sf - xf).Duration() <= SharedFolderCorrelationWindow) ||
                (s.Process.Length > 0 && s.Process.Equals(x.Process, StringComparison.OrdinalIgnoreCase)) ||
                (s.RuleId is "DEF-DETECTION" or "EXEC-SCRIPT-VIA-LOLBIN" or "NET-LOLBIN-TRAFFIC" or "PERSIST-SERVICE-USERPATH" or "PERSIST-TASK" &&
                 T(s) is { } st && T(x) is { } xt && (st - xt).Duration() <= TimeSpan.FromMinutes(10))).ToList();
            if (touching.Count == 0) continue;
            // The window covers the partners too: their evidence is part of this finding and must lie inside the window it claims.
            var span = touching.Select(s => (s.FirstSeenUtc ?? s.LastSeenUtc, s.LastSeenUtc ?? s.FirstSeenUtc)).Append((T(x), x.LastSeenUtc ?? T(x)))
                               .SelectMany(p => new[] { p.Item1, p.Item2 }).Where(t => t is not null).Select(t => t!.Value).ToList();
            var src = ExecSource(x);
            f.Add(new Finding
            {
                FindingId = Id(), RuleId = "EXEC-USERPATH-CORRELATED",
                Title = Ran(x) ? $"Program rulat dintr-o locație în care utilizatorii pot scrie, legat de alte constatări: {x.Process}"
                               : $"Program prezent ({src}) în locație în care utilizatorii pot scrie, legat de alte constatări: {x.Process}",
                Severity = Severity.High, Category = "Execution", Classification = Classification.Correlated, Confidence = Confidence.Medium,
                FirstSeenUtc = span.Count > 0 ? span.Min() : null, LastSeenUtc = span.Count > 0 ? span.Max() : x.LastSeenUtc, File = x.File, Process = x.Process,
                Description = x.Description + " Legături: " + string.Join("; ", touching.Take(5).Select(s => s.Title)),
                ClassificationReason = Ran(x)
                    ? $"Execuție dovedită ({src}) apropiată în timp sau în cale de alte constatări grave."
                    : $"Prezența programului ({src}, fără dovadă de execuție) apropiată în timp sau în cale de alte constatări grave.",
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
                SemanticType = rest.All(Ran) ? SemanticType.Execution : SemanticType.Presence,
                Title = rest.All(Ran) ? $"{LogAnalyzer.Dfir.Language.RoText.Count(rest.Count, "program", "programe")} au rulat din locații în care utilizatorii pot scrie (fără alte semnale)"
                      : rest.Any(Ran) ? $"{LogAnalyzer.Dfir.Language.RoText.Count(rest.Count, "program", "programe")} au rulat sau sunt doar prezente în locații în care utilizatorii pot scrie (fără alte semnale)"
                      : $"{LogAnalyzer.Dfir.Language.RoText.Count(rest.Count, "program", "programe")} prezente în locații în care utilizatorii pot scrie, fără dovadă de execuție (fără alte semnale)",
                Severity = Severity.Low, Category = "Execution", Classification = Classification.Candidate, Confidence = Confidence.Low,
                FirstSeenUtc = rest.Min(T), LastSeenUtc = rest.Max(T),
                Description = string.Join("; ", rest.GroupBy(x => x.Process, StringComparer.OrdinalIgnoreCase).Select(g => (Last: g.OrderByDescending(T).First(), N: g.Count())).OrderByDescending(g => T(g.Last)).Take(60)
                    .Select(g => $"{g.Last.Process} ({T(g.Last):yyyy-MM-dd HH:mm}{(Ran(g.Last) ? "" : $", doar prezent ({ExecSource(g.Last)})")}{(g.N > 1 ? $", {g.N} apariții" : "")})")),
                ClassificationReason = string.Join("; ", new[]
                {
                    rest.Count(Ran) is var ran and > 0 ? $"execuție dovedită pentru {ran} ({string.Join("/", rest.Where(Ran).Select(ExecSource).Distinct())})" : "",
                    rest.Count(x => !Ran(x)) is var only and > 0 ? $"doar prezență, fără dovadă de execuție, pentru {only} ({string.Join("/", rest.Where(x => !Ran(x)).Select(ExecSource).Distinct())})" : "",
                }.Where(s => s.Length > 0)) is var basis && basis.Length > 0
                    ? char.ToUpperInvariant(basis[0]) + basis[1..] + "; în sine frecvente pentru instalatoare și aplicații per utilizator."
                    : "În sine frecvente pentru instalatoare și aplicații per utilizator.",
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
                    Title = $"Lanț de incident {T(cluster[0]):yyyy-MM-dd HH:mm} – {T(cluster[^1]):HH:mm} UTC ({LogAnalyzer.Dfir.Language.RoText.Count(cluster.Count, "constatare", "constatări")} legate)",
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

    /// <summary>
    /// LOG-TAMPER. Clears (Security 1102 / System 104) are scored by <see cref="LogClearAssessment"/>: planned maintenance is Info,
    /// an unexplained clear without a procedure profile is Medium, an unexpected or corroborated one is High, never Critical.
    /// Service stop (1100) and audit-policy change (4719) follow the same lifecycle (owner decision 27): a stop during a shutdown/restart
    /// or a Group Policy audit change is Info, a removal of success/failure auditing that is not routine is High.
    /// </summary>
    private static Finding BuildLogTamper(string id, List<TimelineEvent> clears, List<Finding> found, LogMaintenancePolicy? policy, IReadOnlyList<SystemLifecycleEvent> systemEvents, Func<TimelineEvent, string, string> F)
    {
        var logClears = clears.Where(e => e.EventId is "1102" or "104").Where(e => e.Time.Utc is not null).OrderBy(e => e.Time.Utc).ToList();
        var others = found.Where(x => x.Severity >= Severity.High && (x.FirstSeenUtc ?? x.LastSeenUtc) is not null).Select(x => (x.FirstSeenUtc ?? x.LastSeenUtc)!.Value).ToList();
        var assessed = LogClearAssessment.Assess(
            logClears.Select(e => new LogClearEvent(e.EventId == "1102" ? "Security" : F(e, "Channel") is { Length: > 0 } ch ? ch : "System",
                e.Time.Utc!.Value, F(e, "SubjectUserName"), F(e, "SubjectDomainName"))).ToList(), policy, others).ToList();
        foreach (var e in clears.Where(e => e.EventId == "1100" && e.Time.Utc is not null).OrderBy(e => e.Time.Utc))
            assessed.Add(LogClearAssessment.AssessServiceStop(new ServiceStopEvent(e.Time.Utc!.Value, F(e, "SubjectUserName"), F(e, "SubjectDomainName")), policy, systemEvents));
        foreach (var e in clears.Where(e => e.EventId == "4719" && e.Time.Utc is not null).OrderBy(e => e.Time.Utc))
            assessed.Add(LogClearAssessment.AssessAuditPolicyChange(new AuditPolicyChangeEvent(e.Time.Utc!.Value, F(e, "SubjectUserName"), F(e, "SubjectDomainName"), F(e, "SubjectUserSid"), F(e, "AuditPolicyChanges")), policy));
        var severity = assessed.Count > 0 ? LogClearAssessment.Overall(assessed) : Severity.Medium;
        var lines = clears.OrderBy(e => e.Time.Utc).Take(15).Select(e =>
            $"{e.Time.Utc:yyyy-MM-dd HH:mm} {e.Source[9..]} {e.EventId} {(e.EventId == "1102" ? "de " + F(e, "SubjectUserName") : F(e, "Channel"))}").ToList();
        var verdict = assessed.Select(a => a.Reason).Distinct().ToList();
        // Only Security 1100 records, each next to a System 1074/6006/6005: the logging service stopped because Windows shut down.
        // Nothing was cleared, so the finding must not say so (no "logs cleared" title, no T1070.001, no "activity before the clear").
        var stops = clears.Where(e => e.EventId == "1100").ToList();
        if (stops.Count == clears.Count && stops.All(e => e.Time.Utc is { } t && LogClearAssessment.ShutdownPair(t, systemEvents) is not null))
            return new Finding
            {
                FindingId = id, RuleId = "LOG-TAMPER", Title = "Serviciul de jurnalizare s-a oprit la oprirea sistemului (normal)",
                Severity = Severity.Info, Category = "Defense Evasion",
                Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "",
                FirstSeenUtc = clears.Min(e => e.Time.Utc), LastSeenUtc = clears.Max(e => e.Time.Utc),
                Description = string.Join("; ", lines) + (verdict.Count > 0 ? ". Evaluare: " + string.Join(" ", verdict.Take(5)) : "") +
                              ". Niciun jurnal nu a fost golit (nu există Security 1102 / System 104).",
                ClassificationReason = "Doar Security 1100, fiecare lângă o oprire/repornire a sistemului (System 1074/6006/6005 în ±10 min): Windows oprește serviciul de jurnalizare la fiecare oprire.",
                SupportingEvidence = clears.Take(30).Select(e => new EvidenceRef(e.EvidenceId, e.Locator, $"{e.Source} {e.EventId}")).ToList(),
                MissingEvidence = ["Nimic din jurnal nu lipsește din cauza acestei opriri: jurnalizarea reia la pornirea următoare."],
                RecommendedNextSteps = ["Nicio acțiune, dacă opririle corespund unor reporniri cunoscute ale stației."],
                AlternativeExplanations = [],
            };
        return new Finding
        {
            FindingId = id, RuleId = "LOG-TAMPER", Title = "Jurnale șterse, serviciul de jurnalizare oprit sau politica de audit modificată",
            Severity = severity, Category = "Defense Evasion",
            Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1070.001",
            FirstSeenUtc = clears.Min(e => e.Time.Utc), LastSeenUtc = clears.Max(e => e.Time.Utc),
            Description = string.Join("; ", lines) + (verdict.Count > 0 ? ". Evaluare: " + string.Join(" ", verdict.Take(5)) : ""),
            ClassificationReason = "Evenimente 1102/104/1100/4719 înregistrate de Windows. Severitatea ține de ciclul de viață al golirii (planificată / neașteptată / neevaluată) și de factori de corelare, nu de ora din zi.",
            SupportingEvidence = clears.Take(30).Select(e => new EvidenceRef(e.EvidenceId, e.Locator, $"{e.Source} {e.EventId}")).ToList(),
            MissingEvidence = ["Activitatea anterioară ștergerii nu mai este în jurnal; surse alternative: SRUM, Prefetch, Amcache, copii VSS."],
            AlternativeExplanations = ["Mentenanță planificată (rotirea jurnalelor, curățare după instalare) făcută de un administrator."],
        };
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
