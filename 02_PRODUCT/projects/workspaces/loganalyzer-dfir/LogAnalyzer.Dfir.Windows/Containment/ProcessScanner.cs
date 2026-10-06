using System.Diagnostics;
using System.Management;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;
using LogAnalyzer.Dfir.Windows.Parsers;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Containment;

public sealed record ProcessIdentity(int? Pid, string Name, string Path, string CommandLine, int? ParentPid, string ParentName,
                                     string ParentPath, DateTimeOffset? StartedUtc, string User);

public sealed record FileIdentity(string Path, long Size, string Sha256, DateTimeOffset? CreatedUtc, DateTimeOffset? ModifiedUtc,
                                  SignatureInfo Signature, string CompanyName, string ProductName, string FileDescription, string OriginalFileName);

public sealed record PeSummary(string Machine, DateTimeOffset? CompileTimeUtc, string Subsystem, bool IsDll,
                               IReadOnlyList<string> Sections, IReadOnlyList<string> ImportedDlls, string Error = "");

public sealed record NetworkIntent(string Kind, string Destination, int? Port, string Protocol, string Source, Timestamp Time, string Detail);

public sealed record PersistenceHit(string Mechanism, string Location, string Value);

public sealed record PrefetchHit(string PrefetchFile, int RunCount, IReadOnlyList<DateTimeOffset> RunTimesUtc);

public sealed record DefenderVerdict(EvidenceStatus Status, bool? ThreatFound, string Output, int? ExitCode);

/// <summary>Everything the scan established about one program, plus what it could not establish (gaps).</summary>
public sealed class ProcessScanReport
{
    public required ProcessIdentity Process { get; init; }
    public FileIdentity? File { get; set; }
    public PeSummary? Pe { get; set; }
    public List<Ioc> Iocs { get; } = [];
    public List<string> NetworkApiReferences { get; } = [];
    public List<NetworkIntent> NetworkIntents { get; } = [];
    public List<PersistenceHit> Persistence { get; } = [];
    public List<PrefetchHit> Prefetch { get; } = [];
    public List<string> SiblingFilesRecentlyChanged { get; } = [];
    public DefenderVerdict? Defender { get; set; }
    public List<string> SuspicionReasons { get; } = [];
    public List<EvidenceGap> Gaps { get; } = [];
    public DateTimeOffset ScannedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Read-only examination of a program and, if it runs, of its process. Nothing on the station is modified:
/// the Defender scan runs with remediation disabled so the sample is preserved as evidence.
/// </summary>
public sealed class ProcessScanner
{
    private static readonly string[] NetworkApis =
    [
        "InternetOpen", "InternetConnect", "HttpSendRequest", "URLDownloadToFile", "WinHttpOpen", "WinHttpConnect",
        "WinHttpSendRequest", "WSAStartup", "WSAConnect", "getaddrinfo", "gethostbyname", "DnsQuery", "System.Net.Http",
        "System.Net.Sockets", "HttpClient", "WebClient", "Invoke-WebRequest", "Net.WebClient",
    ];

    public bool RunDefenderScan { get; init; } = true;

    internal static string Reason(Exception ex) =>
        ex is UnauthorizedAccessException ? "acces refuzat (necesită drepturi de administrator)" : ex.Message;
    public TimeSpan DefenderTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public ProcessScanReport Scan(string programPath, int? pid = null, CancellationToken ct = default)
    {
        var report = new ProcessScanReport { Process = DescribeProcess(programPath, pid) };
        var path = report.Process.Path.Length > 0 ? report.Process.Path : programPath;

        if (File.Exists(path))
        {
            report.File = DescribeFile(path, report.Gaps);
            report.Pe = PeReader.Read(path);
            ExtractStrings(path, report);
            report.SiblingFilesRecentlyChanged.AddRange(RecentSiblings(path));
        }
        else
        {
            report.Gaps.Add(new EvidenceGap("Fișierul programului", EvidenceStatus.NotAvailable, $"{path} nu există (șters sau mutat)",
                "Hash, semnătură și analiza conținutului nu sunt posibile", "Prefetch, Amcache, copie din carantina Defender", "Posibil din alte surse"));
        }

        ct.ThrowIfCancellationRequested();
        if (pid is int p) AddLiveConnections(p, report);
        report.Persistence.AddRange(PersistenceSearch.Find(path, report.Gaps));
        report.Prefetch.AddRange(FindPrefetch(path, report.Gaps));
        if (RunDefenderScan && File.Exists(path)) report.Defender = DefenderScan(path, ct);

        Assess(report);
        return report;
    }

    // ---- process --------------------------------------------------------------------------------------------

    private static ProcessIdentity DescribeProcess(string programPath, int? pid)
    {
        if (pid is not int p) return new ProcessIdentity(null, System.IO.Path.GetFileName(programPath), programPath, "", null, "", "", null, "");
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT Name, ExecutablePath, CommandLine, ParentProcessId, CreationDate FROM Win32_Process WHERE ProcessId = {p}");
            foreach (ManagementObject mo in searcher.Get())
            {
                string name = mo["Name"] as string ?? "";
                string exe = mo["ExecutablePath"] as string ?? programPath;
                int? ppid = mo["ParentProcessId"] is uint pp ? (int)pp : null;
                DateTimeOffset? started = mo["CreationDate"] is string cd ? ManagementDateTimeConverter.ToDateTime(cd).ToUniversalTime() : null;
                string user = Owner(mo);
                var (pName, pPath) = ppid is int x ? ParentOf(x) : ("", "");
                return new ProcessIdentity(p, name, exe, mo["CommandLine"] as string ?? "", ppid, pName, pPath, started, user);
            }
        }
        catch (ManagementException) { }
        return new ProcessIdentity(p, System.IO.Path.GetFileName(programPath), programPath, "", null, "", "", null, "");
    }

    private static string Owner(ManagementObject mo)
    {
        try
        {
            var outParams = mo.InvokeMethod("GetOwner", null, null);
            return outParams?["User"] is string u ? $"{outParams["Domain"]}\\{u}" : "";
        }
        catch (ManagementException) { return ""; }
    }

    private static (string, string) ParentOf(int ppid)
    {
        try
        {
            using var s = new ManagementObjectSearcher($"SELECT Name, ExecutablePath FROM Win32_Process WHERE ProcessId = {ppid}");
            foreach (ManagementObject mo in s.Get()) return (mo["Name"] as string ?? "", mo["ExecutablePath"] as string ?? "");
        }
        catch (ManagementException) { }
        return ("(proces părinte încheiat)", "");
    }

    // ---- file -----------------------------------------------------------------------------------------------

    private static FileIdentity DescribeFile(string path, List<EvidenceGap> gaps)
    {
        var fi = new FileInfo(path);
        string sha;
        try { sha = LogAnalyzer.Dfir.IO.Hashing.Sha256File(path); }
        catch (IOException ex)
        {
            sha = "";
            gaps.Add(new EvidenceGap("SHA-256", EvidenceStatus.Failed, ex.Message, "Fișierul nu poate fi identificat prin hash", "Copie după oprirea procesului", "Da"));
        }
        var vi = FileVersionInfo.GetVersionInfo(path);
        return new FileIdentity(path, fi.Length, sha, fi.CreationTimeUtc, fi.LastWriteTimeUtc, Authenticode.Verify(path),
            vi.CompanyName ?? "", vi.ProductName ?? "", vi.FileDescription ?? "", vi.OriginalFilename ?? "");
    }

    private static void ExtractStrings(string path, ProcessScanReport report)
    {
        const int maxBytes = 64 * 1024 * 1024;
        byte[] data;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int len = (int)Math.Min(fs.Length, maxBytes);
            data = new byte[len];
            fs.ReadExactly(data, 0, len);
            if (fs.Length > maxBytes)
                report.Gaps.Add(new EvidenceGap("Șiruri de caractere", EvidenceStatus.Partial, $"Doar primii {maxBytes / 1024 / 1024} MB au fost analizați",
                    "IOC-uri din restul fișierului pot lipsi", "Analiză offline a copiei", "Da"));
        }
        catch (IOException ex)
        {
            report.Gaps.Add(new EvidenceGap("Șiruri de caractere", EvidenceStatus.Failed, ex.Message, "IOC-urile din fișier nu au fost extrase", "Copie a fișierului", "Da"));
            return;
        }

        var text = new StringBuilder();
        foreach (var s in AsciiStrings(data).Concat(Utf16Strings(data))) text.AppendLine(s);
        var all = text.ToString();
        foreach (var ioc in IocExtractor.Extract(all, "strings:" + System.IO.Path.GetFileName(path))
                     .Where(i => !i.SuppressedAsNoise)
                     .DistinctBy(i => (i.Type, i.Value)))
            report.Iocs.Add(ioc);
        foreach (var api in NetworkApis)
            if (all.Contains(api, StringComparison.OrdinalIgnoreCase)) report.NetworkApiReferences.Add(api);
    }

    internal static IEnumerable<string> AsciiStrings(byte[] data, int min = 6)
    {
        var sb = new StringBuilder();
        foreach (var b in data)
        {
            if (b is >= 0x20 and < 0x7F) { sb.Append((char)b); continue; }
            if (sb.Length >= min) yield return sb.ToString();
            sb.Clear();
        }
        if (sb.Length >= min) yield return sb.ToString();
    }

    internal static IEnumerable<string> Utf16Strings(byte[] data, int min = 6)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            char c = (char)(data[i] | (data[i + 1] << 8));
            if (c is >= ' ' and < '\u007F') { sb.Append(c); continue; }
            if (sb.Length >= min) yield return sb.ToString();
            sb.Clear();
        }
        if (sb.Length >= min) yield return sb.ToString();
    }

    private static IEnumerable<string> RecentSiblings(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (dir is null) yield break;
        var since = DateTime.UtcNow.AddDays(-30);
        IEnumerable<FileInfo> files;
        try { files = new DirectoryInfo(dir).EnumerateFiles().Where(f => f.LastWriteTimeUtc >= since).Take(200).ToList(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { yield break; }
        foreach (var f in files)
            if (!f.FullName.Equals(path, StringComparison.OrdinalIgnoreCase))
                yield return $"{f.FullName} ({f.Length} B, modificat {f.LastWriteTimeUtc:yyyy-MM-dd HH:mm} UTC)";
    }

    // ---- network --------------------------------------------------------------------------------------------

    private static void AddLiveConnections(int pid, ProcessScanReport report)
    {
        foreach (var c in TcpTable.ForProcess(pid))
        {
            if (c.State == "LISTEN")
            {
                report.NetworkIntents.Add(new NetworkIntent("ascultă (inbound)", c.Local.Address.ToString(), c.Local.Port, "TCP",
                    "tabela TCP curentă", Timestamp.FromUtc(DateTime.UtcNow, "", "observed now"), "Port deschis pentru conexiuni primite"));
                continue;
            }
            report.NetworkIntents.Add(new NetworkIntent("conexiune activă", c.Remote.Address.ToString(), c.Remote.Port, "TCP",
                "tabela TCP curentă", Timestamp.FromUtc(DateTime.UtcNow, "", "observed now"), $"{c.State}, port local {c.Local.Port}"));
        }
    }

    // ---- prefetch -------------------------------------------------------------------------------------------

    private static IEnumerable<PrefetchHit> FindPrefetch(string path, List<EvidenceGap> gaps)
    {
        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        var exe = System.IO.Path.GetFileName(path).ToUpperInvariant();
        string[] candidates;
        try { candidates = Directory.GetFiles(dir, exe + "-*.pf"); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            gaps.Add(new EvidenceGap("Prefetch", EvidenceStatus.NotAvailable, Reason(ex), "Istoricul de rulare nu este cunoscut", "Amcache, BAM, SRUM", "Cu drepturi de administrator"));
            return [];
        }
        var hits = new List<PrefetchHit>();
        foreach (var pf in candidates)
        {
            try
            {
                var info = PrefetchParser.Read(pf);
                hits.Add(new PrefetchHit(pf, info.RunCount, info.RunTimesUtc));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                gaps.Add(new EvidenceGap($"Prefetch {System.IO.Path.GetFileName(pf)}", EvidenceStatus.Failed, ex.Message, "Rulările din acest fișier nu sunt cunoscute", "Amcache, BAM", "Posibil"));
            }
        }
        return hits;
    }

    // ---- Defender -------------------------------------------------------------------------------------------

    private DefenderVerdict DefenderScan(string path, CancellationToken ct)
    {
        var mp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        if (!File.Exists(mp)) return new DefenderVerdict(EvidenceStatus.NotAvailable, null, "MpCmdRun.exe nu există pe stație", null);
        var psi = new ProcessStartInfo(mp)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation" }) psi.ArgumentList.Add(a);
        try
        {
            using var proc = Process.Start(psi)!;
            var outTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errTask = proc.StandardError.ReadToEndAsync(ct);
            if (!proc.WaitForExit(DefenderTimeout))
            {
                try { proc.Kill(true); } catch (InvalidOperationException) { }
                return new DefenderVerdict(EvidenceStatus.Partial, null, "Scanarea Defender a depășit timpul alocat", null);
            }
            var output = (outTask.Result + errTask.Result).Trim();
            // MpCmdRun: 0 = no threat, 2 = threat found (file scan with remediation disabled).
            bool? threat = proc.ExitCode switch { 0 => false, 2 => true, _ => null };
            var status = threat is null ? EvidenceStatus.Failed : EvidenceStatus.Success;
            return new DefenderVerdict(status, threat, output, proc.ExitCode);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new DefenderVerdict(EvidenceStatus.Failed, null, ex.Message, null);
        }
    }

    // ---- assessment -----------------------------------------------------------------------------------------

    /// <summary>Plain reasons, each tied to something the scan observed. No score is invented.</summary>
    private static void Assess(ProcessScanReport r)
    {
        var path = r.Process.Path;
        if (r.File is { } f)
        {
            if (!f.Signature.IsSigned) r.SuspicionReasons.Add("Executabilul nu are semnătură digitală.");
            else if (!f.Signature.IsValid) r.SuspicionReasons.Add($"Semnătura nu este validă: {f.Signature.Status}.");
        }
        if (SuspiciousProcessDetector.IsUserWritableLocation(path))
            r.SuspicionReasons.Add("Rulează dintr-o locație în care orice utilizator poate scrie (AppData, Temp, ProgramData, Public, Downloads).");
        if (r.NetworkIntents.Any(n => n.Kind != "ascultă (inbound)" && !SuspiciousProcessDetector.IsPrivateOrLocal(n.Destination)))
            r.SuspicionReasons.Add("Are conexiuni către adrese din Internet.");
        if (r.Persistence.Count > 0)
            r.SuspicionReasons.Add($"Este configurat să pornească automat ({string.Join(", ", r.Persistence.Select(p => p.Mechanism).Distinct())}).");
        if (r.Defender?.ThreatFound == true) r.SuspicionReasons.Add("Microsoft Defender a identificat o amenințare în fișier.");
        if (r.File is { } ff && !string.IsNullOrEmpty(ff.OriginalFileName) &&
            !string.Equals(System.IO.Path.GetFileNameWithoutExtension(ff.OriginalFileName), System.IO.Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))
            r.SuspicionReasons.Add($"Numele fișierului diferă de numele original din resurse ({ff.OriginalFileName}).");
        if (r.Pe is { CompileTimeUtc: { } ct } && r.File?.ModifiedUtc is { } mod && ct > mod.AddDays(1))
            r.SuspicionReasons.Add("Data de compilare PE este după data modificării fișierului (posibilă manipulare a timestamp-urilor).");
    }
}

/// <summary>Autostart locations that reference a program (registry Run keys, services, scheduled tasks, Startup folders).</summary>
public static class PersistenceSearch
{
    private static readonly (RegistryHive Hive, string Key)[] RunKeys =
    [
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"),
    ];

    public static IEnumerable<PersistenceHit> Find(string programPath, List<EvidenceGap> gaps)
    {
        var name = System.IO.Path.GetFileName(programPath);
        bool Matches(string? v) => !string.IsNullOrEmpty(v) &&
            (v.Contains(programPath, StringComparison.OrdinalIgnoreCase) || v.Contains(name, StringComparison.OrdinalIgnoreCase));
        var hits = new List<PersistenceHit>();

        int unreadable = 0;
        foreach (var (hive, key) in RunKeys)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var k = root.OpenSubKey(key);
                if (k is null) continue;
                foreach (var vn in k.GetValueNames())
                    if (Matches(k.GetValue(vn)?.ToString()))
                        hits.Add(new PersistenceHit("Registry Run", $@"{(hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}\{key}\{vn}", k.GetValue(vn)?.ToString() ?? ""));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { unreadable++; }
        }

        using (var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
        {
            if (services is not null)
                foreach (var svc in services.GetSubKeyNames())
                {
                    try
                    {
                        using var s = services.OpenSubKey(svc);
                        var image = s?.GetValue("ImagePath")?.ToString();
                        if (Matches(image)) hits.Add(new PersistenceHit("Serviciu Windows", $@"HKLM\SYSTEM\CurrentControlSet\Services\{svc}", image!));
                        using var p = s?.OpenSubKey("Parameters");
                        var dll = p?.GetValue("ServiceDll")?.ToString();
                        if (Matches(dll)) hits.Add(new PersistenceHit("Serviciu Windows (ServiceDll)", $@"HKLM\SYSTEM\CurrentControlSet\Services\{svc}\Parameters", dll!));
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { unreadable++; }
                }
        }
        if (unreadable > 0)
            gaps.Add(new EvidenceGap("Autostart în registry", EvidenceStatus.Partial, $"{unreadable} chei nu au putut fi citite fără drepturi de administrator",
                "Servicii sau intrări Run care pornesc programul pot lipsi din rezultat", "Rulare ca administrator; hive-ul SYSTEM offline", "Da"));

        var tasksDir = System.IO.Path.Combine(Environment.SystemDirectory, "Tasks");
        try
        {
            foreach (var task in Directory.EnumerateFiles(tasksDir, "*", SearchOption.AllDirectories))
            {
                string xml;
                try { xml = File.ReadAllText(task); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
                if (Matches(xml)) hits.Add(new PersistenceHit("Task programat", task, ExtractCommand(xml)));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            gaps.Add(new EvidenceGap("Task-uri programate", EvidenceStatus.Partial, ProcessScanner.Reason(ex), "Task-urile care pornesc programul pot lipsi din rezultat", "schtasks /query /xml (administrator)", "Cu drepturi de administrator"));
        }

        foreach (var startup in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
        {
            if (!Directory.Exists(startup)) continue;
            foreach (var f in Directory.EnumerateFiles(startup))
            {
                string content;
                try { content = Encoding.Unicode.GetString(File.ReadAllBytes(f)) + Encoding.ASCII.GetString(File.ReadAllBytes(f)); }
                catch (IOException) { continue; }
                if (Matches(content) || System.IO.Path.GetFileNameWithoutExtension(f).Equals(System.IO.Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase))
                    hits.Add(new PersistenceHit("Folder Startup", f, ""));
            }
        }
        return hits;
    }

    private static string ExtractCommand(string xml)
    {
        int a = xml.IndexOf("<Command>", StringComparison.OrdinalIgnoreCase);
        int b = xml.IndexOf("</Command>", StringComparison.OrdinalIgnoreCase);
        var cmd = a >= 0 && b > a ? xml[(a + 9)..b] : "";
        int c = xml.IndexOf("<Arguments>", StringComparison.OrdinalIgnoreCase);
        int d = xml.IndexOf("</Arguments>", StringComparison.OrdinalIgnoreCase);
        return c >= 0 && d > c ? $"{cmd} {xml[(c + 11)..d]}" : cmd;
    }
}

/// <summary>Minimal PE header reader: machine, compile time, subsystem, sections and imported DLL names.</summary>
public static class PeReader
{
    public static PeSummary Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt16() != 0x5A4D) return Empty("nu este un fișier PE (lipsește MZ)");
            fs.Position = 0x3C;
            int peOff = br.ReadInt32();
            fs.Position = peOff;
            if (br.ReadUInt32() != 0x00004550) return Empty("semnătura PE lipsește");
            ushort machine = br.ReadUInt16();
            ushort sectionCount = br.ReadUInt16();
            uint timeStamp = br.ReadUInt32();
            fs.Position += 8;
            ushort optSize = br.ReadUInt16();
            ushort characteristics = br.ReadUInt16();
            long optStart = fs.Position;
            ushort magic = br.ReadUInt16();
            bool pe32Plus = magic == 0x20B;
            fs.Position = optStart + 68;
            ushort subsystem = br.ReadUInt16();
            fs.Position = optStart + (pe32Plus ? 120 : 104); // data directory #1 = import table
            uint importRva = br.ReadUInt32();

            fs.Position = optStart + optSize;
            var sections = new List<(string Name, uint Va, uint VSize, uint RawPtr, uint RawSize)>();
            for (int i = 0; i < sectionCount && i < 96; i++)
            {
                var name = Encoding.ASCII.GetString(br.ReadBytes(8)).TrimEnd('\0');
                uint vsize = br.ReadUInt32(), va = br.ReadUInt32(), rawSize = br.ReadUInt32(), rawPtr = br.ReadUInt32();
                fs.Position += 16;
                sections.Add((name, va, vsize, rawPtr, rawSize));
            }

            long Offset(uint rva)
            {
                foreach (var s in sections)
                    if (rva >= s.Va && rva < s.Va + Math.Max(s.VSize, s.RawSize)) return s.RawPtr + (rva - s.Va);
                return -1;
            }

            var dlls = new List<string>();
            long imp = importRva == 0 ? -1 : Offset(importRva);
            for (int i = 0; imp >= 0 && i < 512; i++)
            {
                fs.Position = imp + i * 20 + 12;
                uint nameRva = br.ReadUInt32();
                if (nameRva == 0) break;
                long no = Offset(nameRva);
                if (no < 0 || no >= fs.Length) break;
                fs.Position = no;
                var sb = new StringBuilder();
                for (int c; (c = fs.ReadByte()) > 0 && sb.Length < 256;) sb.Append((char)c);
                dlls.Add(sb.ToString());
            }

            return new PeSummary(
                machine switch { 0x14C => "x86", 0x8664 => "x64", 0xAA64 => "ARM64", _ => $"0x{machine:X4}" },
                timeStamp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(timeStamp),
                subsystem switch { 2 => "GUI", 3 => "Console", 1 => "Native (driver)", _ => subsystem.ToString() },
                (characteristics & 0x2000) != 0,
                sections.Select(s => s.Name).ToList(),
                dlls.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (EndOfStreamException)
        {
            return Empty("nu este un fișier PE valid (structura se termină prematur)");
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or UnauthorizedAccessException)
        {
            return Empty(ex is UnauthorizedAccessException ? "acces refuzat la fișier" : ex.Message);
        }
    }

    private static PeSummary Empty(string error) => new("", null, "", false, [], [], error);
}
