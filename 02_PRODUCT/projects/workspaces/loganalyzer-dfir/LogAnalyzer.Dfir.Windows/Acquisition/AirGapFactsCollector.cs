using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Devices;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// Reads, read-only, what the event logs cannot say about the air gap: the network adapters and their kind, the optical writer and the burn staging folder, printers
/// and the print log state, screenshots and clipboard / activity history, virtual machines and boot entries, archiver histories, and the camera / microphone / speaker
/// inventory. Every read that fails is named in <see cref="AirGapFacts.Errors"/>: an empty list never means "looked and found nothing" unless the read is not in Errors.
/// The user-specific reads (screenshots, clipboard, WSL, archivers, burn folder) cover the account that runs the collector only. The P1 reads (listing of the
/// connected removable volumes, the Startup folders, the top-level files of Downloads and Desktop) cover every profile the account can read, and are bounded.
/// </summary>
public sealed class AirGapFactsCollector : ICollector
{
    public string Name => "AirGapFactsCollector";
    public string Version => "1.0";
    public bool RequiresElevation => false;
    public CollectionProfile Profiles => CollectionProfile.QuickAndUp;

    public const int MaxFiles = 300, MaxScanEntries = 20000, MaxImages = 100, MaxScanDepth = 4;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var facts = Read(ctx.IsElevated, ctx.Case.Root, ct);
        var dir = ctx.Case.RawDir("AirGapFacts");
        var path = Path.Combine(dir, $"airgap_facts_{facts.CollectedUtc:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, facts.ToJson(), new UTF8Encoding(false));
        var o = new CollectorOutcome { Source = "live system", Tool = "WMI/registry/file system", Status = facts.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success };
        o.Errors.AddRange(facts.Errors);
        o.Evidence.Add(ctx.Case.RegisterStored(path, "live system", "AirGapFacts", AirGapFacts.SourceType, TemporalType.CurrentSnapshot, Name, Version));
        return o;
    }

    public static AirGapFacts Read(bool elevated, string? workDir, CancellationToken ct = default)
    {
        var f = new AirGapFacts { CollectedUtc = DateTimeOffset.UtcNow, IsAdministrator = elevated };
        void Try(string what, Action a)
        {
            ct.ThrowIfCancellationRequested();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { a(); }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Security.SecurityException or IOException or System.Runtime.InteropServices.COMException or InvalidOperationException or EventLogException)
            { f.Errors.Add($"{what}: {ex.Message}"); }
            f.StepMs[what] = sw.ElapsedMilliseconds;
        }
        Try("adaptoare de rețea", () => f.Adapters.AddRange(ReadAdapters()));
        Try("unități optice și folderul de inscripționare", () => ReadBurn(f.Burn));
        Try("imprimante", () => ReadPrint(f.Print));
        Try("capturi de ecran și istorice", () => ReadScreen(f.Screen));
        Try("Hyper-V", () => ReadHyperV(f.Virtual));
        Try("WSL", () => ReadWsl(f.Virtual));
        Try("produse de virtualizare", () => ReadHypervisors(f.Virtual));
        Try("imagini de disc virtual", () => ReadDiskImages(f.Virtual, ct));
        Try("configurația de pornire (BCD, cere drepturi de administrator)", () => ReadBcd(f.Virtual, elevated, workDir, ct));
        Try("istoricul arhivatoarelor", () => f.Archivers.AddRange(ReadArchivers()));
        Try("litere de unități amovibile", () => f.RemovableLetters.AddRange(ReadRemovableLetters()));
        Try("cameră, microfon, difuzoare", () => f.Hardware.AddRange(ReadHardware()));
        Try("listarea suporturilor amovibile conectate", () => f.MediaListings.AddRange(ReadMediaListings(ct)));
        Try("folderele Startup", () => f.StartupEntries.AddRange(ReadStartupEntries(ct)));
        Try("fișierele din Downloads și Desktop", () => f.ProfileFiles.AddRange(ReadProfileFiles(ct)));
        Try("profilurile și semnăturile de rețea (NetworkList)", () => ReadNetworkList(f.Network));
        Try("setarea de sondare NCSI", () => ReadNcsi(f.Network));
        Try("jurnalizarea firewall-ului (pfirewall.log)", () => ReadFirewallLogging(f.Network));
        Try("auditul conexiunilor WFP (Security 5156/5157, cere drepturi de administrator)", () => ReadWfpAudit(f.Network));
        Try("retenția jurnalelor de evenimente", () => f.Network.EventLogs.AddRange(ReadEventLogInfo()));
        var install = AppInstallTraces.ReadInstall();
        f.AppInstalledUtc = install.Utc; f.AppInstallBasis = install.Basis; f.AppInstallViolations.AddRange(install.Violations);
        f.AppInstallToVerify.AddRange(install.ToVerify); f.AppInstallChainFirstUtc = install.ChainFirstUtc;
        f.WebcamPolicy = Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam", "Value");
        f.MicrophonePolicy = Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone", "Value");
        return f;
    }

    // ---- helpers ----

    internal static string Reg(RegistryKey hive, string key, string name)
    {
        try { using var k = hive.OpenSubKey(key); return Convert.ToString(k?.GetValue(name), System.Globalization.CultureInfo.InvariantCulture) ?? ""; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return ""; }
    }

    internal static int? RegInt(RegistryKey hive, string key, string name) => int.TryParse(Reg(hive, key, name), out var v) ? v : null;

    private static string[] Strings(object? o) => o switch { string[] a => a, string s => [s], _ => [] };

    // ---- D4 ----

    public static IEnumerable<AdapterFact> ReadAdapters()
    {
        foreach (var n in PnpEnumerator.Present("Net"))
            yield return new AdapterFact
            {
                Name = n.Name, InstanceId = n.InstanceId, PnpClass = "Net", Service = n.Service, Manufacturer = n.Manufacturer,
                CompatibleIds = [.. n.CompatibleIds, .. n.HardwareIds], Enabled = !n.Disabled, FirstInstallUtc = FirstInstall(n.InstanceId),
            };
    }

    /// <summary>First install time Windows keeps under the device's Properties key (DEVPKEY_Device_InstallDate, FILETIME); null when the key cannot be read.</summary>
    private static DateTimeOffset? FirstInstall(string instanceId)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Properties\{{83da6326-97a6-4088-9453-a1923f573b29}}\0065");
            if (k?.GetValue("") is byte[] { Length: 8 } b && BitConverter.ToInt64(b, 0) is > 0 and var ft) return DateTimeOffset.FromFileTime(ft).ToUniversalTime();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentOutOfRangeException) { }
        return null;
    }

    // ---- D7 ----

    /// <summary>Deny_Write = 1 is "blocked", any other number is "not blocked", and a value that is missing or unreadable is undetermined (null), never false.</summary>
    public static bool? DenyWriteFromRegistry(int? raw) => raw is { } v ? v == 1 : null;

    private static void ReadBurn(BurnFacts b)
    {
        const string Pol = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        b.NoCdBurningMachine = RegInt(Registry.LocalMachine, Pol, "NoCDBurning");
        b.NoCdBurningUser = RegInt(Registry.CurrentUser, Pol, "NoCDBurning");
        b.CdDvdDenyWrite = DenyWriteFromRegistry(RegInt(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56308-b6bf-11d0-94f2-00a0c91efb8b}", "Deny_Write"));
        using (var s = new ManagementObjectSearcher("SELECT Name, Drive, Capabilities, CapabilityDescriptions FROM Win32_CDROMDrive"))
            foreach (ManagementObject mo in s.Get())
            {
                var caps = mo["Capabilities"] is ushort[] c ? c : [];
                var desc = Strings(mo["CapabilityDescriptions"]).ToList();
                // Capabilities: 4 = "Supports Writing" (CIM_MediaAccessDevice)
                b.Writers.Add(new OpticalWriterFact { Name = mo["Name"] as string ?? "", Drive = mo["Drive"] as string ?? "", CanWrite = caps.Contains((ushort)4) || desc.Any(d => d.Contains("Writing", StringComparison.OrdinalIgnoreCase)), Capabilities = desc });
            }
        // D7: the drive's own profile list through IMAPI2; a drive WMI reports as read-only but IMAPI2 lists writable profiles for is a writer
        var (imapi, _) = LogAnalyzer.Dfir.Windows.Devices.ImapiRecorders.Read(TimeSpan.FromSeconds(15));
        foreach (var rec in (imapi ?? []).Where(r => r.CanWrite))
        {
            var profiles = "IMAPI2: " + string.Join(", ", rec.WritableProfiles);
            if (b.Writers.FirstOrDefault(w => rec.Drives.Contains(w.Drive, StringComparer.OrdinalIgnoreCase)) is { } known)
            {
                known.CanWrite = true;
                known.Capabilities = [.. known.Capabilities, profiles];
            }
            else
                b.Writers.Add(new OpticalWriterFact { Name = rec.Product, Drive = rec.Drives.FirstOrDefault() ?? "", CanWrite = true, Capabilities = [profiles] });
        }
        var staging = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Burn", "Burn");
        b.StagingPath = staging;
        b.StagingFolderExists = Directory.Exists(staging);
        if (b.StagingFolderExists)
            foreach (var p in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Take(MaxFiles))
            {
                var fi = new FileInfo(p);
                b.StagingFiles.Add(new BurnStagingFile { Name = Path.GetRelativePath(staging, p), Size = fi.Length, LastWriteUtc = fi.LastWriteTimeUtc });
            }
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImgBurn", "Log Files"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImgBurn", "Logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ImgBurn", "Log Files"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ImgBurn", "Log Files"),
        };
        foreach (var dir in roots.Where(Directory.Exists))
            foreach (var p in Directory.EnumerateFiles(dir, "*.log").Concat(Directory.EnumerateFiles(dir, "*.txt")).OrderByDescending(File.GetLastWriteTimeUtc).Take(10))
                b.ToolLogs.Add(new BurnToolLog { Path = p, LastWriteUtc = File.GetLastWriteTimeUtc(p), Excerpt = LogExcerpt(p) });
    }

    private static readonly string[] LogKeys = ["Destination", "Source File", "Source", "Device", "Operation Successful", "Operation Failed", "Write Speed", "Label"];

    private static List<string> LogExcerpt(string path)
    {
        var res = new List<string>();
        try
        {
            foreach (var line in File.ReadLines(path).Take(400))
            {
                if (LogKeys.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase))) res.Add(line.Trim().Length > 200 ? line.Trim()[..200] : line.Trim());
                if (res.Count >= 6) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return res;
    }

    // ---- D8 ----

    private static void ReadPrint(PrintFacts p)
    {
        try { using var cfg = new EventLogConfiguration("Microsoft-Windows-PrintService/Operational"); p.OperationalLogEnabled = cfg.IsEnabled; }
        catch (EventLogNotFoundException) { p.OperationalLogEnabled = null; }
        using var s = new ManagementObjectSearcher("SELECT Name, DriverName, PortName, Network, Local FROM Win32_Printer");
        foreach (ManagementObject mo in s.Get())
        {
            var port = mo["PortName"] as string ?? "";
            bool net = mo["Network"] is true || port.StartsWith("\\\\", StringComparison.Ordinal) || port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) || port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase);
            var name = mo["Name"] as string ?? "";
            var driver = mo["DriverName"] as string ?? "";
            p.Printers.Add(new PrinterFact { Name = name, Driver = driver, Port = port, IsNetwork = net, WritesToFile = new[] { "pdf", "xps", "onenote", "print to file", "document writer" }.Any(w => (name + " " + driver).Contains(w, StringComparison.OrdinalIgnoreCase)) || port.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase) || port.StartsWith("PORTPROMPT:", StringComparison.OrdinalIgnoreCase) });
        }
    }

    // ---- D9 ----

    private static void ReadScreen(ScreenFacts s)
    {
        var shots = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        if (Directory.Exists(shots))
            foreach (var p in Directory.EnumerateFiles(shots).Take(MaxFiles))
                s.Screenshots.Add(new ScreenFile { Path = p, CreatedUtc = File.GetCreationTimeUtc(p) });
        var pk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (Directory.Exists(pk))
            foreach (var pkg in Directory.EnumerateDirectories(pk).Where(d => Path.GetFileName(d).StartsWith("Microsoft.ScreenSketch", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(d).StartsWith("Microsoft.Windows.SnippingTool", StringComparison.OrdinalIgnoreCase)))
            {
                var temp = Path.Combine(pkg, "TempState");
                if (!Directory.Exists(temp)) continue;
                foreach (var p in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories).Take(50))
                    s.SnippingTraces.Add(new ScreenFile { Path = p, CreatedUtc = File.GetCreationTimeUtc(p) });
            }
        s.ClipboardHistoryUser = RegInt(Registry.CurrentUser, @"Software\Microsoft\Clipboard", "EnableClipboardHistory");
        s.ClipboardHistoryPolicy = RegInt(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory");
        s.ActivityFeedPolicy = RegInt(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed");
        var cdp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConnectedDevicesPlatform");
        s.ActivitiesCachePresent = Directory.Exists(cdp) && Directory.EnumerateFiles(cdp, "ActivitiesCache.db", SearchOption.AllDirectories).Any();
    }

    // ---- D12 ----

    private static void ReadHyperV(VirtualFacts v)
    {
        v.HyperVServicePresent = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\vmms") is not null;
        if (v.HyperVServicePresent != true) return;
        using var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\virtualization\v2"), new ObjectQuery("SELECT ElementName FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'"));
        foreach (ManagementObject mo in s.Get()) v.HyperVMachines.Add(mo["ElementName"] as string ?? "(fără nume)");
    }

    private static void ReadWsl(VirtualFacts v)
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (k is null) return;
        foreach (var n in k.GetSubKeyNames())
        {
            using var d = k.OpenSubKey(n);
            if (d?.GetValue("DistributionName") is string name) v.WslDistributions.Add(name);
        }
    }

    private static readonly string[] HypervisorNames = ["VirtualBox", "VMware", "QEMU", "Parallels", "Hyper-V"];

    private static void ReadHypervisors(VirtualFacts v)
    {
        foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var k = Registry.LocalMachine.OpenSubKey(path);
            if (k is null) continue;
            foreach (var n in k.GetSubKeyNames())
            {
                using var d = k.OpenSubKey(n);
                if (d?.GetValue("DisplayName") is string dn && HypervisorNames.Any(h => dn.Contains(h, StringComparison.OrdinalIgnoreCase)) && !v.Hypervisors.Contains(dn)) v.Hypervisors.Add(dn);
            }
        }
    }

    private static void ReadDiskImages(VirtualFacts v, CancellationToken ct)
    {
        var data = ImageExtensions();
        var roots = new List<string>();
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var r = d.RootDirectory.FullName;
            foreach (var sub in new[] { "VMs", "VM", "Virtual Machines", "Hyper-V", @"ProgramData\Microsoft\Windows\Virtual Hard Disks", @"ProgramData\Microsoft\Windows\Hyper-V", @"Users\Public\Documents\Hyper-V", "VirtualBox VMs" })
                if (Directory.Exists(Path.Combine(r, sub))) roots.Add(Path.Combine(r, sub));
            var users = Path.Combine(r, "Users");
            if (Directory.Exists(users))
                foreach (var u in SafeDirs(users))
                    foreach (var sub in new[] { "Documents", "Desktop", "Downloads", "VirtualBox VMs", "Virtual Machines", ".VirtualBox", "Documents\\Virtual Machines" })
                        if (Directory.Exists(Path.Combine(u, sub))) roots.Add(Path.Combine(u, sub));
            roots.Add(r);   // the root itself, one level only (handled by depth below)
        }
        int scanned = 0;
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            int depth = root.Length <= 3 ? 1 : MaxScanDepth;
            Walk(root, depth);
            if (v.DiskImages.Count >= MaxImages) break;
        }
        void Walk(string dir, int depthLeft)
        {
            if (scanned >= MaxScanEntries || v.DiskImages.Count >= MaxImages) { v.DiskImageSearchTruncated = true; return; }
            string[] files;
            try { files = Directory.GetFiles(dir); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }
            foreach (var f in files)
            {
                scanned++;
                if (data.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase) && !v.DiskImages.Any(i => i.Path.Equals(f, StringComparison.OrdinalIgnoreCase)))
                {
                    var fi = new FileInfo(f);
                    v.DiskImages.Add(new DiskImageFile { Path = f, Size = fi.Length, LastWriteUtc = fi.LastWriteTimeUtc });
                }
            }
            if (depthLeft <= 1) return;
            foreach (var sub in SafeDirs(dir))
            {
                var name = Path.GetFileName(sub);
                if (name.Equals("Windows", StringComparison.OrdinalIgnoreCase) || name.Equals("Program Files", StringComparison.OrdinalIgnoreCase) || name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) || name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;
                Walk(sub, depthLeft - 1);
            }
        }
    }

    private static IEnumerable<string> ImageExtensions() => new Wp14Data().Channels.DiskImageExtensions;

    private static string[] SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return []; }
    }

    private static void ReadBcd(VirtualFacts v, bool elevated, string? workDir, CancellationToken ct)
    {
        if (!elevated || workDir is null) return;
        var logDir = Path.Combine(workDir, "Logs", "tools");
        var run = ToolRunner.Run(ToolRunner.System32("bcdedit.exe"), ["/enum", "all", "/v"], logDir, "bcdedit_all", TimeSpan.FromSeconds(30), ct, ToolRunner.ConsoleOemEncoding());
        if (run.ExitCode != 0 || !File.Exists(run.StdoutPath)) throw new IOException($"bcdedit: cod de ieșire {run.ExitCode}");
        var names = BootEntries.OsLoaderDescriptions(File.ReadAllText(run.StdoutPath));   // parsed by element names, not by the localised section headers
        v.BootEntryNames.AddRange(names);
        v.BootOsEntries = names.Count;
    }

    // ---- D13 ----

    private static IEnumerable<ArchiverEntry> ReadArchivers()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\WinRAR\ArcHistory"))
            if (k is not null)
                foreach (var n in k.GetValueNames())
                    if (k.GetValue(n) is string s && s.Length > 0) yield return new ArchiverEntry { Tool = "WinRAR", List = "ArcHistory", Value = s };
        foreach (var sub in new[] { "ArcName", "ExtrPath" })
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\WinRAR\DialogEditHistory\" + sub);
            if (k is null) continue;
            foreach (var n in k.GetValueNames())
                if (k.GetValue(n) is string s && s.Length > 0) yield return new ArchiverEntry { Tool = "WinRAR", List = "DialogEditHistory\\" + sub, Value = s };
        }
        using var fm = Registry.CurrentUser.OpenSubKey(@"Software\7-Zip\FM");
        if (fm is null) yield break;
        foreach (var name in new[] { "FolderHistory", "CopyHistory", "PanelPath0", "PanelPath1" })
            foreach (var s in Decode(fm.GetValue(name)))
                yield return new ArchiverEntry { Tool = "7-Zip", List = name, Value = s };
    }

    /// <summary>7-Zip File Manager keeps history as REG_BINARY (UTF-16 strings separated by NUL) and the panel paths as REG_SZ.</summary>
    public static IEnumerable<string> Decode(object? v)
    {
        if (v is string s) { if (s.Length > 0) yield return s; yield break; }
        if (v is byte[] b)
            foreach (var part in Encoding.Unicode.GetString(b).Split('\0', StringSplitOptions.RemoveEmptyEntries)) yield return part;
        if (v is string[] arr) foreach (var a in arr) if (a.Length > 0) yield return a;
    }

    private static IEnumerable<string> ReadRemovableLetters()
    {
        var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Removable or DriveType.CDRom)) letters.Add(d.Name[..1]);
        using var md = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices");
        if (md is not null)
            foreach (var n in md.GetValueNames().Where(n => n.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase) && n.Length == 14))
                if (md.GetValue(n) is byte[] data && Encoding.Unicode.GetString(data) is var text && (text.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase) || text.Contains("USB#", StringComparison.OrdinalIgnoreCase)))
                    letters.Add(n[^2..^1]);
        return letters.OrderBy(l => l);
    }

    // ---- P1 payload rules: removable volumes, Startup folders, Downloads / Desktop ----

    public const int MaxMedia = 8, MaxMediaEntries = 3000, MaxMediaHashed = 300, MaxStartupPerFolder = 100, MaxProfileFiles = 500;
    /// <summary>Hash limits: per file, and in total for Downloads / Desktop (so a profile full of large files cannot stall the collection; the rest are listed without a hash).</summary>
    public const long MediaHashMaxBytes = 16L * 1024 * 1024, ProfileHashMaxBytes = 64L * 1024 * 1024, ProfileHashTotalBytes = 1024L * 1024 * 1024;

    /// <summary>Profile folders that are not a person's profile.</summary>
    private static readonly string[] NotProfiles = ["Public", "Default", "Default User", "All Users", "defaultuser0"];

    /// <summary>Every removable volume connected now: root and one folder level, bounded (<see cref="MaxMediaEntries"/>, hashes up to <see cref="MediaHashMaxBytes"/>). Nothing on the volume is opened by the shell or run.</summary>
    private static List<MediaListingFact> ReadMediaListings(CancellationToken ct)
    {
        var list = new List<MediaListingFact>();
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable).Take(MaxMedia))
        {
            ct.ThrowIfCancellationRequested();
            string label;
            try { if (!d.IsReady) continue; label = d.VolumeLabel; } catch (IOException) { continue; }
            var m = LogAnalyzer.Dfir.Devices.RemovableMediaInspector.List(d.RootDirectory.FullName, label, MaxMediaEntries, MediaHashMaxBytes, MaxMediaHashed, maxDepth: 1);
            m.VolumeSerial = VolumeSerial(d.RootDirectory.FullName);
            list.Add(m);
        }
        return list;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string rootPathName, IntPtr volumeNameBuffer, uint volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, IntPtr fileSystemNameBuffer, uint fileSystemNameSize);

    /// <summary>The volume serial as Windows stores it in shortcuts and Prefetch (8 hex digits); "" when it cannot be read.</summary>
    private static string VolumeSerial(string root) =>
        GetVolumeInformationW(root, IntPtr.Zero, 0, out var serial, out _, out _, IntPtr.Zero, 0) ? serial.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) : "";

    /// <summary>Profile folders of the system drive (one per account that has logged on); reparse points and the template profiles are skipped.</summary>
    private static List<string> ProfileDirs()
    {
        var users = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Users");
        if (!Directory.Exists(users)) return [];
        return [.. SafeDirs(users).Where(p => !NotProfiles.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase) && !new DirectoryInfo(p).Attributes.HasFlag(FileAttributes.ReparsePoint))];
    }

    /// <summary>The per-user Startup folder of every profile and the common one; shortcuts decoded from their bytes (never resolved by the shell), targets checked with Authenticode.</summary>
    private static List<StartupEntryFact> ReadStartupEntries(CancellationToken ct)
    {
        var list = new List<StartupEntryFact>();
        var folders = ProfileDirs().Select(p => (Scope: "utilizator", Profile: Path.GetFileName(p), Dir: Path.Combine(p, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup"))).ToList();
        folders.Add(("comun", "", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)));
        foreach (var (scope, profile, dir) in folders)
        {
            ct.ThrowIfCancellationRequested();
            if (dir.Length == 0 || !Directory.Exists(dir)) continue;
            IEnumerable<FileInfo> files;
            try { files = new DirectoryInfo(dir).EnumerateFiles().Take(MaxStartupPerFolder).ToList(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
            foreach (var fi in files)
            {
                var e = new StartupEntryFact
                {
                    Scope = scope, Profile = profile, Path = fi.FullName, Size = fi.Length,
                    CreatedUtc = new DateTimeOffset(fi.CreationTimeUtc, TimeSpan.Zero), LastWriteUtc = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
                };
                if (fi.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && fi.Length is > 0 and <= 1024 * 1024)
                    try
                    {
                        var link = LogAnalyzer.Dfir.FileSystem.LnkParser.Decode(File.ReadAllBytes(fi.FullName));
                        e.LnkTarget = link.Target; e.LnkArguments = link.Arguments;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { }
                var signTarget = e.LnkTarget.Length > 0 ? Environment.ExpandEnvironmentVariables(e.LnkTarget) : fi.FullName;
                if (!fi.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && File.Exists(signTarget))
                    try { var s = Native.Authenticode.Verify(signTarget); e.TargetSigned = s.IsSigned && s.IsValid; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
                list.Add(e);
            }
        }
        return list;
    }

    /// <summary>Top-level files of every profile's Downloads and Desktop (names, sizes, times; SHA-256 up to <see cref="ProfileHashMaxBytes"/>), at most <see cref="MaxProfileFiles"/> files in total.</summary>
    private static List<ProfileFileFact> ReadProfileFiles(CancellationToken ct)
    {
        var list = new List<ProfileFileFact>();
        long hashedBytes = 0;
        foreach (var p in ProfileDirs())
            foreach (var folder in new[] { "Downloads", "Desktop" })
            {
                ct.ThrowIfCancellationRequested();
                var dir = Path.Combine(p, folder);
                if (!Directory.Exists(dir)) continue;
                List<FileInfo> files;
                try { files = new DirectoryInfo(dir).EnumerateFiles().Where(f => !f.Attributes.HasFlag(FileAttributes.ReparsePoint)).Take(MaxProfileFiles - list.Count).ToList(); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
                foreach (var fi in files)
                {
                    var e = new ProfileFileFact { Profile = Path.GetFileName(p), Folder = folder, Name = fi.Name, Size = fi.Length, LastWriteUtc = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero) };
                    if (fi.Length <= ProfileHashMaxBytes && hashedBytes + fi.Length <= ProfileHashTotalBytes)
                        try
                        {
                            using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            e.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
                            hashedBytes += fi.Length;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    list.Add(e);
                }
                if (list.Count >= MaxProfileFiles) return list;
            }
        return list;
    }

    // ---- D15 ----

    public static IEnumerable<HardwareFact> ReadHardware()
    {
        foreach (var n in PnpEnumerator.Present("Camera", "Image", "MEDIA", "AudioEndpoint"))
            yield return new HardwareFact { Name = n.Name, InstanceId = n.InstanceId, PnpClass = n.SetupClass, Status = n.Disabled ? "dezactivat" : "activ", Enabled = !n.Disabled };
    }

    // ---- network evidence sources (owner directive 2026-10-16): local, read-only; nothing is sent ----

    private const string NetworkListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList";

    /// <summary>NetworkList\Profiles joined with NetworkList\Signatures\Managed|Unmanaged on ProfileGuid.</summary>
    public static void ReadNetworkList(NetworkEvidenceFacts n)
    {
        using var root = Registry.LocalMachine.OpenSubKey(NetworkListKey + @"\Profiles");
        n.ProfilesRead = true;
        if (root is null) return;
        var sigs = new Dictionary<string, (string Kind, string Mac, string Dns, string First)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in new[] { "Managed", "Unmanaged" })
        {
            using var sk = Registry.LocalMachine.OpenSubKey(NetworkListKey + @"\Signatures\" + kind);
            if (sk is null) continue;
            foreach (var name in sk.GetSubKeyNames())
            {
                using var s = sk.OpenSubKey(name);
                if (s?.GetValue("ProfileGuid")?.ToString() is not { Length: > 0 } pg) continue;
                var mac = s.GetValue("DefaultGatewayMac") is byte[] { Length: > 0 } m ? string.Join("-", m.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) : "";
                sigs[pg] = (kind, mac, s.GetValue("DnsSuffix")?.ToString() ?? "", s.GetValue("FirstNetwork")?.ToString() ?? "");
            }
        }
        foreach (var guid in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(guid);
            if (k is null) continue;
            var p = new NetworkListProfileFact
            {
                ProfileGuid = guid, ProfileName = k.GetValue("ProfileName")?.ToString() ?? "", Description = k.GetValue("Description")?.ToString() ?? "",
                Category = k.GetValue("Category") as int?, NameType = k.GetValue("NameType") as int?,
                DateCreatedUtc = LocalToUtc(LogAnalyzer.Dfir.Windows.Audit.StationFactCollector.SystemTime(k.GetValue("DateCreated") as byte[])),
                DateLastConnectedUtc = LocalToUtc(LogAnalyzer.Dfir.Windows.Audit.StationFactCollector.SystemTime(k.GetValue("DateLastConnected") as byte[])),
            };
            if (sigs.TryGetValue(guid, out var sig)) { p.SignatureKind = sig.Kind; p.DefaultGatewayMac = sig.Mac; p.DnsSuffix = sig.Dns; p.FirstNetwork = sig.First; }
            n.Profiles.Add(p);
        }
    }

    private static DateTimeOffset? LocalToUtc(DateTime? local) => local is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Local)).ToUniversalTime() : null;

    public static void ReadNcsi(NetworkEvidenceFacts n)
    {
        n.NcsiEnableActiveProbing = RegInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\NlaSvc\Parameters\Internet", "EnableActiveProbing");
        n.NcsiPolicyNoActiveProbe = RegInt(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator", "NoActiveProbe");
    }

    /// <summary>Firewall logging per profile: the policy value wins over the local one; the default log is %SystemRoot%\System32\LogFiles\Firewall\pfirewall.log.</summary>
    public static void ReadFirewallLogging(NetworkEvidenceFacts n)
    {
        n.FirewallRead = true;
        foreach (var (profile, local, policy) in new[] { ("Domain", "DomainProfile", "DomainProfile"), ("Private", "StandardProfile", "PrivateProfile"), ("Public", "PublicProfile", "PublicProfile") })
        {
            string L(string name) => Reg(Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{local}\Logging", name);
            string P(string name) => Reg(Registry.LocalMachine, $@"SOFTWARE\Policies\Microsoft\WindowsFirewall\{policy}\Logging", name);
            string V(string name) => P(name) is { Length: > 0 } pv ? pv : L(name);
            var path = V("LogFilePath") is { Length: > 0 } lp ? lp : @"%systemroot%\system32\LogFiles\Firewall\pfirewall.log";
            path = Environment.ExpandEnvironmentVariables(path);
            // Review of PR #288: the path comes from the registry; it is checked BEFORE any file access, so a UNC or network-drive path never makes the
            // collector contact another machine. A rejected path is not read at all.
            var rejected = LogAnalyzer.Dfir.Network.FirewallLogParser.RejectPath(path, LocalDriveType);
            bool exists = false; long size = 0;
            if (rejected.Length == 0)
            {
                var fi = new FileInfo(path);
                try { exists = fi.Exists; size = exists ? fi.Length : 0; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            n.FirewallLogs.Add(new FirewallLogFact
            {
                Profile = profile, Path = path, FileExists = exists, Size = size, Rejected = rejected,
                LogAllowed = int.TryParse(V("LogSuccessfulConnections"), out var a) ? a == 1 : null,
                LogDropped = int.TryParse(V("LogDroppedPackets"), out var d) ? d == 1 : null,
            });
        }
    }

    /// <summary>The drive type of a local drive root ("C:\"), read locally (GetDriveType); never touches a share.</summary>
    internal static DriveType LocalDriveType(string root)
    {
        try { return new DriveInfo(root).DriveType; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return DriveType.Unknown; }
    }

    public static void ReadWfpAudit(NetworkEvidenceFacts n)
    {
        try
        {
            var flags = LogAnalyzer.Dfir.Windows.Native.AuditPolicy.QueryFlags(LogAnalyzer.Dfir.Windows.Native.AuditPolicy.Subcategories["Filtering Platform Connection"]);
            var setting = (LogAnalyzer.Dfir.Windows.Native.AuditSetting)(flags & 3);
            n.WfpAuditSuccess = (setting & LogAnalyzer.Dfir.Windows.Native.AuditSetting.Success) != 0;
            n.WfpAuditFailure = (setting & LogAnalyzer.Dfir.Windows.Native.AuditSetting.Failure) != 0;
        }
        catch (System.ComponentModel.Win32Exception ex) { throw new UnauthorizedAccessException($"politica de audit nu poate fi citită ({ex.NativeErrorCode}); este necesar un cont de administrator", ex); }
    }

    /// <summary>Retention mode, maximum size, record count, oldest record number and "full" flag of every channel the event-log collector exports.</summary>
    public static IEnumerable<EventLogInfoFact> ReadEventLogInfo()
    {
        foreach (var ch in EventLogCollector.Channels)
        {
            var e = new EventLogInfoFact { Channel = ch };
            try
            {
                using (var cfg = new EventLogConfiguration(ch))
                {
                    e.Enabled = cfg.IsEnabled; e.LogMode = cfg.LogMode.ToString(); e.MaxSizeBytes = cfg.MaximumSizeInBytes;
                }
                // The oldest record kept, read with a local reader (no EventLogSession: the Standalone build holds no session type that could open a remote log).
                using var reader = new EventLogReader(new EventLogQuery(ch, PathType.LogName));
                using var oldest = reader.ReadEvent();
                e.OldestRecordNumber = oldest?.RecordId;
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException) { e.Error = ex.Message; }
            yield return e;
        }
    }
}
