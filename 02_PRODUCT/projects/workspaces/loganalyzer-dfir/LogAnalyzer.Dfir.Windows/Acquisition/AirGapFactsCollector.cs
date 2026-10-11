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
/// The user-specific reads (screenshots, clipboard, WSL, archivers, burn folder) cover the account that runs the collector only.
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

    private static void ReadBurn(BurnFacts b)
    {
        const string Pol = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        b.NoCdBurningMachine = RegInt(Registry.LocalMachine, Pol, "NoCDBurning");
        b.NoCdBurningUser = RegInt(Registry.CurrentUser, Pol, "NoCDBurning");
        b.CdDvdDenyWrite = RegInt(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56308-b6bf-11d0-94f2-00a0c91efb8b}", "Deny_Write") is { } v ? v == 1 : false;
        using (var s = new ManagementObjectSearcher("SELECT Name, Drive, Capabilities, CapabilityDescriptions FROM Win32_CDROMDrive"))
            foreach (ManagementObject mo in s.Get())
            {
                var caps = mo["Capabilities"] is ushort[] c ? c : [];
                var desc = Strings(mo["CapabilityDescriptions"]).ToList();
                // Capabilities: 4 = "Supports Writing" (CIM_MediaAccessDevice)
                b.Writers.Add(new OpticalWriterFact { Name = mo["Name"] as string ?? "", Drive = mo["Drive"] as string ?? "", CanWrite = caps.Contains((ushort)4) || desc.Any(d => d.Contains("Writing", StringComparison.OrdinalIgnoreCase)), Capabilities = desc });
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
        var run = ToolRunner.Run(ToolRunner.System32("bcdedit.exe"), ["/enum", "osloader"], logDir, "bcdedit_osloader", TimeSpan.FromSeconds(30), ct);
        if (run.ExitCode != 0 || !File.Exists(run.StdoutPath)) throw new IOException($"bcdedit: cod de ieșire {run.ExitCode}");
        var names = BootEntries.OsLoaderDescriptions(File.ReadAllText(run.StdoutPath));
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

    // ---- D15 ----

    public static IEnumerable<HardwareFact> ReadHardware()
    {
        foreach (var n in PnpEnumerator.Present("Camera", "Image", "MEDIA", "AudioEndpoint"))
            yield return new HardwareFact { Name = n.Name, InstanceId = n.InstanceId, PnpClass = n.SetupClass, Status = n.Disabled ? "dezactivat" : "activ", Enabled = !n.Disabled };
    }
}
