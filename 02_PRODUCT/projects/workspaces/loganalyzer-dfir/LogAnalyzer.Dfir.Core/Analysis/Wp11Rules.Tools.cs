using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    private sealed record ToolHit(TimelineEvent Event, string Kind, string Path, string Hash);

    /// <summary>The executable path a row is about: the Prefetch entry's own image (from its referenced files), else the row's path.</summary>
    private static string ExePath(TimelineEvent e)
    {
        if (e.Source == "Prefetch")
        {
            var refs = F(e, "ReferencedFiles").Split('|');
            return refs.FirstOrDefault(r => r.EndsWith("\\" + e.Process, StringComparison.OrdinalIgnoreCase)) ?? e.Path;
        }
        return e.Path;
    }

    private static IEnumerable<string> Sha256sOf(TimelineEvent e)
    {
        if (e.Hash.Length == 64) yield return e.Hash;
        foreach (var part in F(e, "Hashes").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            if (part.StartsWith("SHA256=", StringComparison.OrdinalIgnoreCase) && part.Length == 71) yield return part[7..];
    }

    // ---- user-writable folders, two-tools window, DNS suffix: what raises or limits the severity of a tool finding ----

    internal static readonly TimeSpan ToolWindow = TimeSpan.FromHours(24);
    internal const int ToolMaxEvidence = 14;

    private const System.Text.RegularExpressions.RegexOptions ToolRx = System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase;
    private static readonly TimeSpan ToolRxTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>An .exe path token of a command line: a quoted path (may contain spaces) or an unquoted one (no spaces).</summary>
    private static readonly System.Text.RegularExpressions.Regex ToolExeToken = new(@"""[^""\r\n]{1,1024}?\.exe""|[^\s""',;]{1,1024}\.exe\b", ToolRx, ToolRxTimeout);

    /// <summary>A launcher in front of the real program ("cmd /c x.exe", "rundll32 a.dll,f", "powershell -c …"), with or without .exe.</summary>
    private static readonly System.Text.RegularExpressions.Regex ToolWrapper = new(@"^(?:[^\s""]*\\)?(?:cmd|rundll32|powershell|pwsh|wscript|cscript|mshta|conhost)(?:\.exe)?(?:\s|$)", ToolRx, ToolRxTimeout);

    /// <summary>Cap for one path or name quoted in a finding (a hostile ImagePath can be 32 K characters).</summary>
    private const int ToolTextCap = 400;

    private static readonly System.Text.RegularExpressions.Regex[] ToolWritableFolders =
    [
        new(@"\\users\\[^\\]+\\downloads\\", ToolRx, ToolRxTimeout),
        new(@"\\users\\[^\\]+\\desktop\\", ToolRx, ToolRxTimeout),
        new(@"\\appdata\\local\\temp\\", ToolRx, ToolRxTimeout),
        new(@"\\users\\public\\", ToolRx, ToolRxTimeout),
        new(@"\\windows\\temp\\", ToolRx, ToolRxTimeout),
        new(@"\\\$recycle\.bin\\", ToolRx, ToolRxTimeout),
        new(@"\\appdata\\roaming\\", ToolRx, ToolRxTimeout),
        new(@"\\users\\[^\\]+\\documents\\", ToolRx, ToolRxTimeout),
        new(@"^(?:[a-z]:)?\\perflogs\\", ToolRx, ToolRxTimeout),
        // folders under Windows that a standard user can write to
        new(@"\\windows\\(?:tasks|tracing|registration\\crmlog|system32\\(?:tasks|com\\dmp|spool\\(?:drivers\\color|printers|servers))|syswow64\\(?:tasks|com\\dmp))\\", ToolRx, ToolRxTimeout),
        // a file directly in ProgramData, not in a vendor sub-folder
        new(@"^(?:[a-z]:)?\\programdata\\[^\\]+$", ToolRx, ToolRxTimeout),
    ];

    /// <summary>Where an installed program normally lives: Program Files, Program Files (x86), Windows on the system drive C: (or a path without a drive: kernel
    /// and Prefetch forms) and their known-folder GUIDs as UserAssist writes them. The user-writable folders inside them are excluded by the caller.</summary>
    private static readonly System.Text.RegularExpressions.Regex ToolStandardFolder =
        new(@"^(?:(?:c:)?\\(?:program files(?: \(x86\))?|windows)\\|\{(?:6D809377-6AF0-444B-8957-A3773F02200E|7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E|1AC14E77-02E7-4E5D-B744-2EB1AE5198B7|F38BF404-1D43-42F2-9305-67DE0B28FC23|D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27)\}\\)", ToolRx, ToolRxTimeout);

    // \Device\HarddiskVolumeN (kernel paths) and \VOLUME{guid} (Prefetch ReferencedFiles) stand for the drive letter
    private static readonly System.Text.RegularExpressions.Regex ToolDevicePrefix = new(@"^\\(?:device\\harddiskvolume\d+|volume\{[^}\\]*\})", ToolRx, ToolRxTimeout);

    private static string ToolNormPath(string path)
    {
        var p = path.Trim().Trim('"').Replace('/', '\\');
        if (p.StartsWith(@"\??\", StringComparison.Ordinal) || p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p[4..];
        try { p = ToolDevicePrefix.Replace(p, ""); } catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { }
        foreach (var (v, folder) in ToolEnv) p = p.Replace(v, folder, StringComparison.OrdinalIgnoreCase);
        return ToolResolveDots(p);
    }

    /// <summary>"C:\Windows\..\Users\Public\x.exe" -> "C:\Users\Public\x.exe": "." and ".." segments resolved, never above the root.</summary>
    private static string ToolResolveDots(string p)
    {
        if (!p.Contains(@"\.", StringComparison.Ordinal)) return p;
        var parts = p.Split('\\');
        var kept = new List<string>(parts.Length);
        foreach (var s in parts)
        {
            if (s == ".") continue;
            if (s == "..") { if (kept.Count > 1) kept.RemoveAt(kept.Count - 1); continue; }
            kept.Add(s);
        }
        return string.Join('\\', kept);
    }

    /// <summary>True only for Program Files / Windows on the system drive and outside their user-writable sub-folders (an approval by name covers nothing
    /// else: Windows\Temp, AppData, ProgramData sub-folders, UNC shares, 8.3 names, other drives, unknown places).</summary>
    internal static bool ToolStandardLocation(string path)
    {
        try { return ToolStandardFolder.IsMatch(ToolNormPath(path)) && !ToolUserWritable(path); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }

    /// <summary>The station a piece of evidence comes from: lower case, the NetBIOS part of an FQDN (an IP address is kept whole); "" when the parser did not record it.</summary>
    private static string ToolHost(TimelineEvent e)
    {
        var h = e.Host.Trim().ToLowerInvariant();
        if (System.Net.IPAddress.TryParse(h, out _)) return h;
        int dot = h.IndexOf('.');
        return dot > 0 ? h[..dot] : h;
    }

    private static readonly (string Var, string Folder)[] ToolEnv =
        [("%temp%", @"\appdata\local\temp"), ("%tmp%", @"\appdata\local\temp"), ("%public%", @"\users\public"), ("%programdata%", @"\programdata"), ("%allusersprofile%", @"\programdata"),
         ("%systemroot%", @"c:\windows"), ("%windir%", @"c:\windows"), ("%programfiles(x86)%", @"c:\program files (x86)"), ("%programfiles%", @"c:\program files"), ("%systemdrive%", "c:")];

    /// <summary>True when the path is in a folder a standard user can write to (Downloads, Desktop, Temp, Public, Recycle Bin, the root of ProgramData).</summary>
    internal static bool ToolUserWritable(string path)
    {
        var p = ToolNormPath(path);
        try { return ToolWritableFolders.Any(r => r.IsMatch(p)); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }

    /// <summary>The executable of a service ImagePath: without the \??\ prefix, the quotes and the arguments.</summary>
    private static string ToolServiceExe(string imagePath)
    {
        var s = imagePath.Trim();
        if (s.StartsWith(@"\??\", StringComparison.Ordinal) || s.StartsWith(@"\\?\", StringComparison.Ordinal)) s = s[4..];
        var exe = Correlation.CommandExecutable(s).Trim().Trim('"');
        return exe.Length > 0 ? exe : ServiceExe(s).Trim().Trim('"');
    }

    private static string? ToolInnerExe(string commandLine, Func<string, bool> isTool)
    {
        try { return ToolExeToken.Matches(commandLine).Select(m => m.Value.Trim('"')).FirstOrDefault(isTool); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return null; }
    }

    /// <summary>The name is the domain or a subdomain of it, at a label boundary.</summary>
    private static bool ToolDomainMatches(RemoteToolRow tool, string queryName)
    {
        var d = queryName.Trim().TrimEnd('.').ToLowerInvariant();
        return d.Length > 0 && tool.Domains.Any(s => s.Length > 0 && (d == s.ToLowerInvariant() || d.EndsWith("." + s.ToLowerInvariant(), StringComparison.Ordinal)));
    }

    /// <summary>Text built from evidence: line breaks and hidden characters removed (ReportText.Clean), double quotes made single.</summary>
    private static string ToolSafe(string s)
    {
        var t = DefenderDetectionHistoryParser.Safe(s).Replace('"', '\'');
        if (t.Length <= ToolTextCap) return t;
        int cut = char.IsHighSurrogate(t[ToolTextCap - 1]) ? ToolTextCap - 1 : ToolTextCap;
        return t[..cut] + "…";
    }

    private static bool ToolIsWrapper(string exe)
    {
        try { return ToolWrapper.IsMatch(exe.Trim().Trim('"')); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }

    private static bool ToolNear(List<DateTimeOffset> a, List<DateTimeOffset> b)
    {
        foreach (var t in a)
        {
            int i = b.BinarySearch(t);
            if (i < 0) i = ~i;
            if (i < b.Count && b[i] - t <= ToolWindow) return true;
            if (i > 0 && t - b[i - 1] <= ToolWindow) return true;
        }
        return false;
    }

    private sealed record ToolResult(RemoteToolRow Tool, List<ToolHit> Exec, List<ToolHit> Present);

    /// <summary>
    /// REMOTE-TOOL-EXECUTED (Prefetch / BAM / UserAssist / 4688 / Sysmon 1 for a known remote-access tool) and REMOTE-TOOL-PRESENT (Amcache /
    /// ShimCache, 7045/4697 service, MsiInstaller 11707 install, the tool's own log files, DNS queries for its domains): one finding per tool,
    /// execution wins. Present is not executed. Software approved in the procedure profile is Info with "aprobat în profil". The tool list is data
    /// (Data/remote_access_tools.json). All tools are collected first, because two distinct tools on one station within 24 h raise each other.
    /// PRESENT becomes Medium when the tool runs or is a service from a folder a user can write to, or when two or more distinct tools have evidence on one
    /// station within 24 h (Quick Assist, approved tools, DNS-only hits and unnamed evidence in a multi-station case do not count); EXECUTED is already High.
    /// Quick Assist (common) is Low when present and Medium when run from its own place. An approval by name covers only runs and services in Program Files /
    /// Windows on the system drive outside their user-writable sub-folders: any other copy is Medium.
    /// </summary>
    private static void Tools(Ctx c, List<Finding> o)
    {
        var starts = ProcessStarts(c.Events).ToList();
        var results = new List<ToolResult>();
        foreach (var tool in c.Data.Tools.Tools)
        {
            bool IsExe(string path)
            {
                if (path.Length == 0) return false;
                var file = WinPath.GetFileName(path.Trim().Trim('"').TrimEnd('\\'));
                if (tool.Executables.Contains(file, StringComparer.OrdinalIgnoreCase)) return true;
                // A generic file name is the tool only together with a path hint (vendor folder).
                var norm = path.Replace('/', '\\');
                return tool.ConditionalExecutables.Contains(file, StringComparer.OrdinalIgnoreCase) && tool.PathHints.Any(h => norm.Contains(h, StringComparison.OrdinalIgnoreCase));
            }
            var exec = new List<ToolHit>(); var present = new List<ToolHit>();
            foreach (var e in c.Events)
            {
                if (e.Source is "Prefetch" or "BAM" or "UserAssist")
                {
                    if (IsExe(e.Process) || IsExe(e.Path)) exec.Add(new(e, e.Source, ExePath(e), ""));
                }
                else if (e.Source is "Amcache" or "ShimCache")
                {
                    if (IsExe(e.Path) || IsExe(e.Process)) present.Add(new(e, e.Source, e.Path, e.Hash.Length == 64 ? e.Hash : ""));
                }
                else if (Ev(e, "System", 7045) || Ev(e, "Security", 4697))
                {
                    var name = F(e, "ServiceName"); var image = F(e, "ImagePath", "ServiceFileName"); var exe = ToolServiceExe(image);
                    // a wrapper (cmd /c, rundll32 …) in front of the tool: the tool is a later token of the command line
                    if (ToolInnerExe(image, IsExe) is { } inner && (!IsExe(exe) || ToolIsWrapper(exe))) exe = inner;
                    if (tool.ServiceNames.Contains(name, StringComparer.OrdinalIgnoreCase) || tool.DisplayContains.Any(d => name.Contains(d, StringComparison.OrdinalIgnoreCase)) || IsExe(exe))
                        present.Add(new(e, "serviciu " + e.EventId, exe, ""));
                }
                else if (Ev(e, "Application", 11707, 1033) && e.Provider.Contains("MsiInstaller", StringComparison.OrdinalIgnoreCase))
                {
                    if (tool.ProductContains.Any(p => AllText(e).Contains(p, StringComparison.OrdinalIgnoreCase))) present.Add(new(e, "instalare MSI", "", ""));
                }
                else if (tool.Domains.Count > 0 && (Ev(e, Sysmon, 22) || Ev(e, DnsClientChannel, 3006, 3008)))
                {
                    // A DNS query shows that the station looked the domain up; it is neither an install nor an execution.
                    if (ToolDomainMatches(tool, F(e, "QueryName"))) present.Add(new(e, "interogare DNS", "", ""));
                }
                // The tool's own log files, seen as a path in any other collected source (file system, LNK, Jump Lists…).
                bool artifactAlreadyRead = e.Source is "Prefetch" or "BAM" or "UserAssist" or "Amcache" or "ShimCache" || e.Source.StartsWith("EventLog:", StringComparison.Ordinal);
                if (!artifactAlreadyRead && tool.LogMarkers.Any(m => e.Path.Replace('/', '\\').Contains(m, StringComparison.OrdinalIgnoreCase)))
                    present.Add(new(e, e.Source.Contains("Registry", StringComparison.OrdinalIgnoreCase) || e.Path.StartsWith("HK", StringComparison.OrdinalIgnoreCase) ? "cheie din registrul Windows (registry)" : "fișier de log al aplicației", e.Path, ""));
            }
            foreach (var p in starts.Where(p => IsExe(p.Image)))
                exec.Add(new(p.Event, p.Event.EventId == "4688" ? "4688" : "Sysmon 1", p.Image.Trim().Trim('"'), Sha256sOf(p.Event).FirstOrDefault() ?? ""));
            if (exec.Count == 0 && present.Count == 0) continue;
            results.Add(new(tool, exec, present));
        }

        // Approval first: an approved tool neither raises nor is raised by the others.
        var assessed = results.Select(r =>
        {
            var hits = (r.Exec.Count > 0 ? r.Exec.Concat(r.Present) : r.Present).ToList();
            var paths = hits.Select(h => h.Path).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var aliases = r.Tool.Aliases.Concat([r.Tool.Name]).Concat(r.Tool.Executables.Select(x => WinPath.GetFileNameWithoutExtension(x)));
            var hashes = hits.SelectMany(h => h.Hash.Length > 0 ? [h.Hash] : Sha256sOf(h.Event)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var approved = ApprovedSoftwareMatcher.Match(c.Profile, aliases, paths, hashes);
            // a profile row with no path pattern and no hash approves the name only: it covers only copies in Program Files / Windows
            bool nameOnly = approved is not null && approved.PathPattern.Trim().Length == 0 && approved.Sha256.Trim().Length == 0;
            var outside = nameOnly
                ? hits.Where(h => (r.Exec.Contains(h) || h.Kind.StartsWith("serviciu", StringComparison.Ordinal)) && h.Path.Length > 0 && !ToolStandardLocation(h.Path))
                      .Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : [];
            return (R: r, Hits: hits, Paths: paths, Approved: outside.Count > 0 ? null : approved, ApprovedOutside: outside);
        }).ToList();

        // Two distinct tools (the common ones and approved ones excluded) with evidence on one station within 24 h: each names the others.
        // A DNS query alone does not count (visiting a vendor's site is not having its tool). Prefetch / Amcache / ShimCache carry no station name:
        // in a case with at most one named station they belong to it; with several stations they are not correlated.
        var caseHosts = c.Events.Select(ToolHost).Where(h => h.Length > 0).Distinct(StringComparer.Ordinal).Take(2).ToList();
        string StationOf(TimelineEvent e) => ToolHost(e) is { Length: > 0 } h ? h : caseHosts.Count <= 1 ? caseHosts.FirstOrDefault() ?? "*" : "";
        var byHost = assessed.Where(a => !a.R.Tool.Common && a.Approved is null).ToDictionary(a => a.R, a => a.Hits
            .Where(h => h.Kind != "interogare DNS" && T(h.Event) is not null && StationOf(h.Event).Length > 0).GroupBy(h => StationOf(h.Event))
            .ToDictionary(g => g.Key, g => g.Select(h => T(h.Event)!.Value).OrderBy(t => t).ToList()));
        List<RemoteToolRow> Others(ToolResult r) => !byHost.TryGetValue(r, out var mine) ? [] :
            byHost.Where(kv => kv.Key != r && kv.Value.Any(h => mine.TryGetValue(h.Key, out var m) && ToolNear(m, h.Value))).Select(kv => kv.Key.Tool).ToList();

        foreach (var a in assessed)
        {
            var r = a.R;
            var tool = r.Tool; var exec = r.Exec; var present = r.Present;
            bool executed = exec.Count > 0;
            var hits = a.Hits; var paths = a.Paths; var approved = a.Approved;
            // runs, or is a service, from a folder a user can write to (an Amcache / ShimCache entry shows presence, not a run)
            var writable = hits.Where(h => (exec.Contains(h) || h.Kind.StartsWith("serviciu", StringComparison.Ordinal)) && h.Path.Length > 0 && ToolUserWritable(h.Path)).Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var others = Others(r);
            bool approvedOutside = a.ApprovedOutside.Count > 0;

            var severity = executed ? Severity.High : Severity.Low;
            string why = "";
            if (!executed && (writable.Count > 0 || others.Count > 0)) severity = Severity.Medium;
            // Quick Assist is part of Windows: run from its own place it is Medium (help-desk scams use exactly that copy), present it is Low
            if (tool.Common && writable.Count == 0) { severity = executed ? Severity.Medium : Severity.Low; why = $" {tool.Name} face parte din Windows; rulat din locul lui normal, severitatea este cel mult Medium (escrocheriile de tip „suport tehnic” folosesc exact această copie)."; }
            if (approved is not null) severity = Severity.Info;
            if (approvedOutside) { severity = Severity.Medium; why += $" Unealta figurează în profil doar după nume, iar aprobarea pe nume acoperă numai Program Files și Windows, nu și: {Join(a.ApprovedOutside.Select(ToolSafe), 3)}."; }
            // "raised" only where it changed the severity: an executed tool is High anyway, an approved one stays Info
            string raised = !executed && approved is null ? "; severitatea este crescută" : "";
            if (writable.Count > 0) why += $" Rulează sau este serviciu dintr-un folder în care utilizatorul poate scrie ({Join(writable.Select(ToolSafe), 3)}){raised}.";
            if (others.Count > 0 && !tool.Common) why += $" Pe aceeași stație, în cel mult 24 de ore, apar și: {Join(others.Select(t => t.Name))}{raised}.";

            var times = hits.Select(h => T(h.Event)).Where(t => t is not null).OrderBy(t => t).ToList();
            var runCounts = exec.Where(h => h.Event.Source == "Prefetch").Select(h => F(h.Event, "RunCount")).Where(rc => rc.Length > 0).ToList();
            string kinds = string.Join(", ", hits.GroupBy(h => h.Kind).Select(g => $"{g.Count()} × {g.Key}"));
            bool dnsOnly = hits.All(h => h.Kind == "interogare DNS");
            // one of every kind first, then the earliest: a flood of one kind does not hide the others
            var evidence = hits.GroupBy(h => h.Kind).Select(g => g.First()).Concat(hits).Distinct().Take(ToolMaxEvidence).ToList();
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = executed ? "REMOTE-TOOL-EXECUTED" : "REMOTE-TOOL-PRESENT",
                Title = $"Unealtă de acces la distanță {(executed ? "rulată" : "prezentă")}: {tool.Name}" + (approved is not null ? " (aprobat în profil)" : ""),
                Severity = severity,
                Category = "Command and Control", Classification = Classification.Direct, Confidence = executed ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1219",
                FirstSeenUtc = times.FirstOrDefault(), LastSeenUtc = times.LastOrDefault(), Process = tool.Executables.FirstOrDefault() ?? "", File = ToolSafe(paths.FirstOrDefault() ?? ""),
                Description = $"{tool.Name}: {kinds}; {Time(times.FirstOrDefault())}" + (times.Count > 1 ? $" – {Time(times.LastOrDefault())}" : "") + "." +
                              (paths.Count > 0 ? $" Căi: {Join(paths.Select(ToolSafe), 4)}." : "") + (runCounts.Count > 0 ? $" Prefetch: număr de rulări {Join(runCounts)}." : "") +
                              (executed ? present.Count > 0 ? $" Alte dovezi de prezență: {present.Count} (instalare/serviciu/amcache/log/DNS)." : "" : dnsOnly ? " Există doar interogări DNS către domeniul uneltei: arată că stația a căutat domeniul, nu că unealta este instalată sau a rulat." : " Prezența unui artefact nu dovedește execuția; nu există în caz un artefact de execuție pentru această unealtă.") +
                              (approved is not null ? $" Software aprobat în profil: {ToolSafe(approved.Name)} ({ToolSafe(approved.PathPattern.Length > 0 ? approved.PathPattern : approved.Publisher)}); aprobat în profil, deci informativ." : "") + why +
                              " Identificarea este după nume (fișier, serviciu, domeniu), nu după semnătură: un alt producător poate folosi același nume." +
                              " Existența unei aplicații de acces de la distanță nu arată o sesiune sau cine s-a conectat.",
                ClassificationReason = executed ? "Artefact de execuție (Prefetch/BAM/UserAssist/4688/Sysmon 1) pentru un executabil din lista de aplicații de acces de la distanță." : "Artefact de prezență (Amcache/ShimCache/serviciu/instalare/log/DNS) pentru o aplicație din lista de acces de la distanță.",
                SemanticType = executed ? SemanticType.Execution : SemanticType.Presence,
                SupportingEvidence = evidence.Select(h => Ref(h.Event, $"{h.Kind} {tool.Name}")).ToList(),
                AlternativeExplanations = ["Suport tehnic sau administrare legitimă; aplicația poate fi aprobată de organizație, dar netrecută în profil.", "O aplicație prezentă doar ca fișier descărcat sau instalată și nefolosită.", "Unealtă folosită legitim de departamentul IT.", "Software preinstalat pe stație."],
            });
        }
    }
}
