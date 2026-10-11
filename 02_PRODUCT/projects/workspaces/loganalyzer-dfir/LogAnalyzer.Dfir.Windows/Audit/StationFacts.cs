using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Audit;

public sealed record LocalAccount(string Name, string Sid, bool Disabled, bool PasswordRequired, bool PasswordExpires, bool LockedOut,
                                  DateTimeOffset? LastLogonUtc, bool IsAdministrator, bool IsRemoteDesktopUser, string Description);

/// <summary>One log record reduced to what the evaluation needs. Data holds EventData/UserData by name.</summary>
public sealed record LogFact(string Channel, int Id, DateTimeOffset TimeUtc, long RecordId, string UserSid, IReadOnlyDictionary<string, string> Data);

public sealed record NetworkProfileFact(string Name, string Kind, DateTime? CreatedLocal, DateTime? LastConnectedLocal, string Description);

public sealed record UsbDeviceFact(string Device, string Serial, string FriendlyName, string Source);

public sealed record ChannelCoverage(string Channel, DateTimeOffset? OldestUtc, long Records, bool Readable, string Note);

/// <summary>Everything collected from the station for a control audit. Collection never changes the station.</summary>
public sealed class StationFacts
{
    public string Host { get; init; } = Environment.MachineName;
    public DateTimeOffset CollectedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset PeriodStartUtc { get; init; }
    public DateTimeOffset PeriodEndUtc { get; init; }
    public bool IsAdministrator { get; init; }
    public bool StationShouldBeIsolated { get; init; }
    public List<LocalAccount> Accounts { get; init; } = [];
    public PasswordPolicy? PasswordPolicy { get; set; }
    public Dictionary<string, AuditSetting>? AuditPolicy { get; set; }
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DefenderExclusions { get; init; } = [];
    public List<NetworkProfileFact> NetworkProfiles { get; init; } = [];
    public List<string> ConnectedInterfacesNow { get; init; } = [];
    public List<UsbDeviceFact> UsbDevices { get; init; } = [];
    public List<LogFact> Logs { get; init; } = [];
    public List<ChannelCoverage> Coverage { get; init; } = [];
    public List<EvidenceGap> Gaps { get; init; } = [];
}

public static class StationFactCollector
{
    // Channel → event IDs read for the control audit.
    public static readonly IReadOnlyDictionary<string, int[]> Queries = new Dictionary<string, int[]>
    {
        ["Security"] = [4624, 4625, 4648, 4672, 4720, 4722, 4723, 4724, 4725, 4726, 4738, 4740, 4732, 4733, 4728, 4729, 4756, 4757,
                        4719, 1102, 4616, 4697, 4698, 4702, 6416],
        ["System"] = [104, 7045, 6005, 6006, 6008, 1074],
        ["Application"] = [11707, 11724, 1033, 1034],
        ["Microsoft-Windows-Partition/Diagnostic"] = [1006],
        ["Microsoft-Windows-PowerShell/Operational"] = [4104],
        ["Microsoft-Windows-TerminalServices-LocalSessionManager/Operational"] = [21, 24, 25],
        ["Microsoft-Windows-WLAN-AutoConfig/Operational"] = [8001, 8003],
        ["Microsoft-Windows-NetworkProfile/Operational"] = [10000, 10001],
    };

    public static StationFacts Collect(DateTimeOffset periodStartUtc, bool stationShouldBeIsolated, int maxPerId = 5000, CancellationToken ct = default)
    {
        using var id = WindowsIdentity.GetCurrent();
        var facts = new StationFacts
        {
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = DateTimeOffset.UtcNow,
            IsAdministrator = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator),
            StationShouldBeIsolated = stationShouldBeIsolated,
        };
        Try(facts, "Conturi locale", () => facts.Accounts.AddRange(ReadAccounts()));
        Try(facts, "Politica de parole", () => facts.PasswordPolicy = LocalPasswordPolicy.Query());
        facts.AuditPolicy = AuditPolicy.Query() is { } ap ? new Dictionary<string, AuditSetting>(ap) : null;
        if (facts.AuditPolicy is null)
            facts.Gaps.Add(new EvidenceGap("Politica de audit", EvidenceStatus.NotAvailable, "nu poate fi citită fără drepturi de administrator",
                "Nu se poate spune ce activitate înregistrează stația", "auditpol /get /category:* (administrator)", "Cu drepturi de administrator"));
        Try(facts, "Setări de securitate", () => ReadSettings(facts));
        Try(facts, "Setări de securitate (extinse)", () => ReadSettingsWave2(facts));
        Try(facts, "Profiluri de rețea", () => facts.NetworkProfiles.AddRange(ReadNetworkProfiles()));
        Try(facts, "Interfețe de rețea", () => facts.ConnectedInterfacesNow.AddRange(ReadConnectedInterfaces()));
        Try(facts, "Dispozitive USB (USBSTOR)", () => facts.UsbDevices.AddRange(ReadUsbStor()));
        foreach (var (channel, ids) in Queries)
        {
            ct.ThrowIfCancellationRequested();
            ReadChannel(facts, channel, ids, periodStartUtc, maxPerId);
        }
        return facts;
    }

    private static void Try(StationFacts f, string what, Action a)
    {
        try { a(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Security.SecurityException or IOException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            f.Gaps.Add(new EvidenceGap(what, EvidenceStatus.Failed, ex.Message, "Verificările care depind de această sursă sunt NEDETERMINATE", "Rulare ca administrator", "Posibil"));
        }
    }

    // ---- accounts ---------------------------------------------------------------------------------------------

    internal static IEnumerable<LocalAccount> ReadAccounts()
    {
        var admins = GroupMembers("S-1-5-32-544");
        var rdp = GroupMembers("S-1-5-32-555");
        var lastLogon = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        using (var s = new ManagementObjectSearcher("SELECT Name, LastLogon FROM Win32_NetworkLoginProfile"))
            foreach (ManagementObject mo in s.Get())
                if (mo["Name"] is string n && mo["LastLogon"] is string ll && ll.Length >= 14)
                    try { lastLogon[n.Split('\\').Last()] = ManagementDateTimeConverter.ToDateTime(ll).ToUniversalTime(); } catch (ArgumentOutOfRangeException) { }

        using var searcher = new ManagementObjectSearcher($"SELECT Name, SID, Disabled, PasswordRequired, PasswordExpires, Lockout, Description FROM Win32_UserAccount WHERE LocalAccount = TRUE");
        foreach (ManagementObject mo in searcher.Get())
        {
            var name = mo["Name"] as string ?? "";
            yield return new LocalAccount(name, mo["SID"] as string ?? "", mo["Disabled"] is true, mo["PasswordRequired"] is true,
                mo["PasswordExpires"] is true, mo["Lockout"] is true, lastLogon.TryGetValue(name, out var l) ? l : null,
                admins.Contains(name), rdp.Contains(name), mo["Description"] as string ?? "");
        }
    }

    /// <summary>Members (user and group names) of the local group with the given well-known SID; the group name is localized.</summary>
    private static HashSet<string> GroupMembers(string groupSid)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var g = new ManagementObjectSearcher($"SELECT Name, Domain FROM Win32_Group WHERE LocalAccount = TRUE AND SID = '{groupSid}'");
        foreach (ManagementObject grp in g.Get())
        {
            var q = $"SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent = \"Win32_Group.Domain='{grp["Domain"]}',Name='{grp["Name"]}'\"";
            using var m = new ManagementObjectSearcher(q);
            foreach (ManagementObject gu in m.Get())
            {
                var part = gu["PartComponent"] as string ?? "";
                var idx = part.LastIndexOf("Name=\"", StringComparison.Ordinal);
                if (idx >= 0) set.Add(part[(idx + 6)..].TrimEnd('"'));
            }
        }
        return set;
    }

    // ---- settings ---------------------------------------------------------------------------------------------

    private static void ReadSettings(StationFacts f)
    {
        string? Reg(string key, string name) => Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + key, name, null)?.ToString();
        var s = f.Settings;
        s["UAC.EnableLUA"] = Reg(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") ?? "";
        s["UAC.ConsentPromptBehaviorAdmin"] = Reg(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin") ?? "";
        s["Winlogon.AutoAdminLogon"] = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoAdminLogon") ?? "";
        // Only the presence of a stored autologon password is recorded, never its value.
        s["Winlogon.DefaultPasswordPresent"] = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultPassword") is { Length: > 0 } ? "da" : "nu";
        s["Winlogon.DefaultUserName"] = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultUserName") ?? "";
        s["RDP.fDenyTSConnections"] = Reg(@"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections") ?? "";
        s["SMB1.Server"] = Reg(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1") ?? "";
        s["SMB1.ClientDriverStart"] = Reg(@"SYSTEM\CurrentControlSet\Services\mrxsmb10", "Start") ?? "";
        s["USBSTOR.Start"] = Reg(@"SYSTEM\CurrentControlSet\Services\USBSTOR", "Start") ?? "";
        s["RemovableStorage.DenyAll"] = Reg(@"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices", "Deny_All") ?? "";
        s["WDigest.UseLogonCredential"] = Reg(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest", "UseLogonCredential") ?? "";
        s["LSA.RunAsPPL"] = Reg(@"SYSTEM\CurrentControlSet\Control\Lsa", "RunAsPPL") ?? "";

        try
        {
            using var cfg = new EventLogConfiguration("Security");
            s["SecurityLog.MaxSizeMB"] = (cfg.MaximumSizeInBytes / 1024 / 1024).ToString();
            s["SecurityLog.Mode"] = cfg.LogMode.ToString();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or EventLogException) { s["SecurityLog.MaxSizeMB"] = ""; }

        try
        {
            dynamic fw = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true)!)!;
            s["Firewall.Domain"] = ((bool)fw.FirewallEnabled[1]) ? "activ" : "inactiv";
            s["Firewall.Private"] = ((bool)fw.FirewallEnabled[2]) ? "activ" : "inactiv";
            s["Firewall.Public"] = ((bool)fw.FirewallEnabled[4]) ? "activ" : "inactiv";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }

        try
        {
            using var mp = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureLastUpdated, IsTamperProtected FROM MSFT_MpComputerStatus");
            foreach (ManagementObject o in mp.Get())
            {
                s["Defender.AntivirusEnabled"] = o["AntivirusEnabled"]?.ToString() ?? "";
                s["Defender.RealTime"] = o["RealTimeProtectionEnabled"]?.ToString() ?? "";
                s["Defender.TamperProtected"] = o["IsTamperProtected"]?.ToString() ?? "";
                if (o["AntivirusSignatureLastUpdated"] is string d)
                    s["Defender.SignaturesUtc"] = ManagementDateTimeConverter.ToDateTime(d).ToUniversalTime().ToString("o");
            }
            using var pref = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT ExclusionPath, ExclusionProcess, ExclusionExtension FROM MSFT_MpPreference");
            foreach (ManagementObject o in pref.Get())
                foreach (var prop in new[] { "ExclusionPath", "ExclusionProcess", "ExclusionExtension" })
                    if (o[prop] is string[] arr)
                        foreach (var v in arr.Where(v => !v.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)))
                            f.DefenderExclusions.Add($"{prop}: {v}");
        }
        catch (ManagementException) { s["Defender.AntivirusEnabled"] = ""; }

        try
        {
            var sysDrive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
            using var bl = new ManagementObjectSearcher(@"root\cimv2\Security\MicrosoftVolumeEncryption", $"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{sysDrive}'");
            foreach (ManagementObject o in bl.Get()) s["BitLocker.SystemDrive"] = o["ProtectionStatus"]?.ToString() ?? "";
        }
        catch (ManagementException ex) { s["BitLocker.SystemDrive.Error"] = ex.Message; }
    }

    // ---- settings, second wave (read-only) ---------------------------------------------------------------------

    /// <summary>The value is not there (key or value missing). Different from "" (empty string) = the read itself failed, so nothing is known.</summary>
    public const string Absent = "lipsă";
    /// <summary>A value that exists but is an empty string.</summary>
    public const string EmptyValue = "(gol)";

    /// <summary>Reads one registry value: its text, <see cref="Absent"/> when key or value does not exist, "" when access failed.</summary>
    private static string RegRead(RegistryKey hive, string key, string name)
    {
        try
        {
            using var k = hive.OpenSubKey(key);
            if (k is null) return Absent;
            return k.GetValue(name) switch
            {
                null => Absent,
                string { Length: 0 } => EmptyValue,
                string[] sa => string.Join(", ", sa),
                byte[] b => Convert.ToHexString(b),
                var v => v.ToString() ?? "",
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }

    private static string Hklm(string key, string name) => RegRead(Registry.LocalMachine, key, name);

    private static void Guard(Action a)
    {
        try { a(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Security.SecurityException or IOException
                                      or System.Runtime.InteropServices.COMException or InvalidOperationException or NetworkInformationException or EventLogException) { }
    }

    /// <summary>
    /// Configuration facts behind the air-gap channel checks: CD/DVD burning (N-series P28), printing (P29), clipboard and activity history (P30),
    /// camera / microphone / speaker devices (P31) and the kind of every network adapter (N05). A value that cannot be read stays "" (unread), never a default.
    /// </summary>
    private static void ReadAirGapChannelSettings(StationFacts f)
    {
        var s = f.Settings;
        const string Rs = @"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices";
        s["Optical.NoCDBurningMachine"] = Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoCDBurning");
        s["Optical.NoCDBurningUser"] = RegRead(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoCDBurning");
        s["Optical.DenyWrite"] = Hklm(Rs + @"\{53f56308-b6bf-11d0-94f2-00a0c91efb8b}", "Deny_Write");
        s["Optical.DenyAll"] = Hklm(Rs, "Deny_All");
        Guard(() =>
        {
            using var cd = new ManagementObjectSearcher("SELECT Name, Capabilities FROM Win32_CDROMDrive");
            var writers = new List<string>();
            int drives = 0;
            foreach (ManagementObject mo in cd.Get())
            {
                drives++;
                if (mo["Capabilities"] is ushort[] caps && caps.Contains((ushort)4)) writers.Add(mo["Name"] as string ?? "unitate");
            }
            s["Optical.DriveCount"] = drives.ToString();
            s["Optical.WriterCount"] = writers.Count.ToString();
            s["Optical.WriterNames"] = string.Join("; ", writers);
        });

        Guard(() =>
        {
            using var cfg = new System.Diagnostics.Eventing.Reader.EventLogConfiguration("Microsoft-Windows-PrintService/Operational");
            s["Print.LogEnabled"] = cfg.IsEnabled ? "1" : "0";
        });
        Guard(() =>
        {
            using var pr = new ManagementObjectSearcher("SELECT Name, PortName, Network FROM Win32_Printer");
            var all = new List<string>(); var net = new List<string>();
            foreach (ManagementObject mo in pr.Get())
            {
                var name = mo["Name"] as string ?? ""; var port = mo["PortName"] as string ?? "";
                all.Add(name);
                if (mo["Network"] is true || port.StartsWith("\\\\", StringComparison.Ordinal) || port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) || port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase)) net.Add(name);
            }
            s["Print.PrinterCount"] = all.Count.ToString();
            s["Print.PrinterNames"] = string.Join("; ", all.Take(8));
            s["Print.NetworkPrinters"] = string.Join("; ", net.Take(8));
        });

        s["Clipboard.HistoryUser"] = RegRead(Registry.CurrentUser, @"Software\Microsoft\Clipboard", "EnableClipboardHistory");
        s["Clipboard.HistoryPolicy"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory");
        s["Activity.FeedPolicy"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed");
        Guard(() =>
        {
            var cdp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConnectedDevicesPlatform");
            s["Activity.CachePresent"] = Directory.Exists(cdp) && Directory.EnumerateFiles(cdp, "ActivitiesCache.db", SearchOption.AllDirectories).Any() ? "da" : "nu";
        });

        Guard(() =>
        {
            var hw = Acquisition.AirGapFactsCollector.ReadHardware().Where(h => h.Enabled).ToList();
            List<string> Names(LogAnalyzer.Dfir.Analysis.HardwareKind k) => hw.Where(h => LogAnalyzer.Dfir.Analysis.HardwareKinds.Of(h) == k).Select(h => h.Name).Distinct().ToList();
            var cams = Names(LogAnalyzer.Dfir.Analysis.HardwareKind.Camera); var mics = Names(LogAnalyzer.Dfir.Analysis.HardwareKind.Microphone); var spk = Names(LogAnalyzer.Dfir.Analysis.HardwareKind.Speaker);
            s["Hardware.Cameras"] = cams.Count.ToString(); s["Hardware.CameraNames"] = string.Join("; ", cams.Take(5));
            s["Hardware.Microphones"] = mics.Count.ToString(); s["Hardware.MicrophoneNames"] = string.Join("; ", mics.Take(5));
            s["Hardware.Speakers"] = spk.Count.ToString(); s["Hardware.SpeakerNames"] = string.Join("; ", spk.Take(5));
        });

        Guard(() =>
        {
            var virt = LogAnalyzer.Dfir.Analysis.Wp14Data.Default.Network.VirtualAdapterWords;
            var byKind = Acquisition.AirGapFactsCollector.ReadAdapters().Where(a => a.Enabled)
                .Select(a => (A: a, K: LogAnalyzer.Dfir.Analysis.AdapterKinds.Classify(a.Name, a.InstanceId, a.CompatibleIds, a.Service, a.PnpClass, virt).Kind)).ToList();
            string Of(LogAnalyzer.Dfir.Analysis.AdapterKind k) => string.Join("; ", byKind.Where(x => x.K == k).Select(x => x.A.Name).Take(5));
            s["Net.AdapterCount"] = byKind.Count.ToString();
            s["Net.PhoneAdapters"] = Of(LogAnalyzer.Dfir.Analysis.AdapterKind.PhoneTethering);
            s["Net.CellularAdapters"] = Of(LogAnalyzer.Dfir.Analysis.AdapterKind.Cellular);
            s["Net.UsbEthernetAdapters"] = Of(LogAnalyzer.Dfir.Analysis.AdapterKind.UsbEthernet);
        });
    }

    private static void ReadSettingsWave2(StationFacts f)
    {
        var s = f.Settings;

        // Wireless radios. Facts only: presence and configuration, never a change.
        Guard(() =>
        {
            var wifi = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToList();
            s["Wifi.AdapterCount"] = wifi.Count.ToString();
            s["Wifi.UpCount"] = wifi.Count(n => n.OperationalStatus == OperationalStatus.Up).ToString();
            s["Wifi.Adapters"] = string.Join("; ", wifi.Select(n => $"{n.Description} ({(n.OperationalStatus == OperationalStatus.Up ? "pornit" : "oprit sau neconectat")})"));
        });
        s["Wlan.ServiceStart"] = Hklm(@"SYSTEM\CurrentControlSet\Services\WlanSvc", "Start");
        Guard(() =>
        {
            // Only the number of saved Wi-Fi profiles (the files are named by GUID); the profile contents are never opened.
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Wlansvc", "Profiles", "Interfaces");
            s["Wlan.ProfileCount"] = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories).Count().ToString() : "0";
        });
        Guard(() =>
        {
            using var bt = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth'");
            var names = bt.Get().Cast<ManagementBaseObject>().Select(o => o["Name"]?.ToString() ?? "").Where(n => n.Length > 0).ToList();
            s["Bluetooth.ComponentCount"] = names.Count.ToString();
            s["Bluetooth.Components"] = string.Join("; ", names.Take(5));
        });
        s["Bluetooth.ServiceStart"] = Hklm(@"SYSTEM\CurrentControlSet\Services\bthserv", "Start");

        // Boot.
        s["SecureBoot.Enabled"] = Hklm(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        s["Firmware.Type"] = Hklm(@"SYSTEM\CurrentControlSet\Control", "PEFirmwareType");   // [NESIGUR] 1 = BIOS vechi, 2 = UEFI (known from Windows setup, no source cited in the research)

        // Session.
        s["ScreenLock.InactivityTimeoutSecs"] = Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "InactivityTimeoutSecs");
        s["LegalNotice.Caption"] = Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "LegalNoticeCaption");
        var legalText = Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "LegalNoticeText");
        s["LegalNotice.Text"] = legalText.Length > 300 ? legalText[..300] + "…" : legalText;

        // Application control: presence and mode only (AppLocker rule collections; App Control / WDAC policies are not read).
        Guard(() =>
        {
            using var srp = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\SrpV2");
            if (srp is null) { s["AppLocker.Collections"] = Absent; return; }
            var parts = new List<string>();
            foreach (var col in srp.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
            {
                using var c = srp.OpenSubKey(col);
                var mode = c?.GetValue("EnforcementMode")?.ToString() ?? "nesetat";
                var rules = c?.GetSubKeyNames().Length ?? 0;
                parts.Add($"{col}: mod {mode}, {rules} reguli");
            }
            s["AppLocker.Collections"] = parts.Count == 0 ? "(fără colecții)" : string.Join("; ", parts);
        });
        s["AppLocker.AppIDSvcStart"] = Hklm(@"SYSTEM\CurrentControlSet\Services\AppIDSvc", "Start");

        // AutoPlay / AutoRun (same keys as DevicePolicyCheck).
        s["AutoRun.NoDriveTypeAutoRun"] = Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun");
        s["AutoRun.NoAutorun"] = Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoAutorun");

        ReadAirGapChannelSettings(f);

        // Windows version and last installed update.
        const string cv = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        s["OS.ProductName"] = Hklm(cv, "ProductName");
        s["OS.DisplayVersion"] = Hklm(cv, "DisplayVersion");
        s["OS.Build"] = Hklm(cv, "CurrentBuild");
        s["OS.UBR"] = Hklm(cv, "UBR");
        Guard(() =>
        {
            using var qfe = new ManagementObjectSearcher("SELECT InstalledOn FROM Win32_QuickFixEngineering");
            DateTime? max = null;
            foreach (ManagementBaseObject o in qfe.Get())
                if (ParseInstalledOn(o["InstalledOn"]?.ToString()) is { } d && (max is null || d > max)) max = d;
            s["OS.LastUpdateDate"] = max?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? Absent;
        });

        // Camera and microphone consent (machine level).
        s["Privacy.Webcam"] = Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam", "Value");
        s["Privacy.Microphone"] = Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone", "Value");   // [NESIGUR] by analogy with webcam

        // Recall / Copilot policies. [NESIGUR] The Copilot policy path comes from the control-station research without a cited source.
        s["AI.DisableAIDataAnalysis"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis");
        s["AI.AllowRecallEnablement"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement");
        s["AI.CopilotTurnOffHklm"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");
        s["AI.CopilotTurnOffHkcu"] = RegRead(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");   // current user only

        // Credential protection.
        s["LSA.KeyReadable"] = Hklm(@"SYSTEM\CurrentControlSet\Control\Lsa", "RunAsPPL") == "" ? "nu" : "da";
        Guard(() =>
        {
            using var dg = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT SecurityServicesRunning FROM Win32_DeviceGuard");
            foreach (ManagementBaseObject o in dg.Get())
                if (o["SecurityServicesRunning"] is Array arr)
                    s["DeviceGuard.ServicesRunning"] = arr.Length == 0 ? "niciunul" : string.Join(",", arr.Cast<object>().Select(x => x.ToString()));
        });

        // PowerShell.
        s["PS.ScriptBlockLogging"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging", "EnableScriptBlockLogging");
        s["PS.Transcription"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\PowerShell\Transcription", "EnableTranscripting");
        s["PS.V2Engine"] = Hklm(@"SOFTWARE\Microsoft\PowerShell\1\PowerShellEngine", "PowerShellVersion");   // [NESIGUR] detection by this value; confirm in the lab

        // Data left on disk.
        s["Disk.ClearPageFile"] = Hklm(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "ClearPageFileAtShutdown");
        s["Disk.HibernateEnabled"] = Hklm(@"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
        s["Disk.CrashDumpEnabled"] = Hklm(@"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled");

        // Removable storage auditing helper and time source.
        s["Storage.HotPlugSecureOpen"] = Hklm(@"SYSTEM\CurrentControlSet\Control\Storage", "HotPlugSecureOpen");
        s["Time.Type"] = Hklm(@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters", "Type");
        s["Time.NtpServer"] = Hklm(@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters", "NtpServer");
    }

    /// <summary>Win32_QuickFixEngineering.InstalledOn is text: a date in the machine's format or a hexadecimal FILETIME.</summary>
    public static DateTime? ParseInstalledOn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out var ft))
        {
            try { return DateTime.FromFileTimeUtc(ft).Date; } catch (ArgumentOutOfRangeException) { return null; }
        }
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return d.Date;
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out d)) return d.Date;
        return null;
    }

    // ---- network ----------------------------------------------------------------------------------------------

    private static IEnumerable<NetworkProfileFact> ReadNetworkProfiles()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles");
        if (root is null) yield break;
        foreach (var guid in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(guid);
            if (k is null) continue;
            var kind = (k.GetValue("NameType") as int?) switch { 71 => "Wi-Fi", 6 => "cablu (Ethernet)", 23 => "VPN", 243 => "bandă largă mobilă", var x => $"tip {x}" };
            yield return new NetworkProfileFact(k.GetValue("ProfileName")?.ToString() ?? guid, kind,
                SystemTime(k.GetValue("DateCreated") as byte[]), SystemTime(k.GetValue("DateLastConnected") as byte[]), k.GetValue("Description")?.ToString() ?? "");
        }
    }

    /// <summary>NetworkList dates are SYSTEMTIME structures in local time.</summary>
    public static DateTime? SystemTime(byte[]? b)
    {
        if (b is null || b.Length < 16) return null;
        int U(int i) => b[i] | (b[i + 1] << 8);
        try { return new DateTime(U(0), U(2), U(6), U(8), U(10), U(12), U(14), DateTimeKind.Local); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static IEnumerable<string> ReadConnectedInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Where(n => n.GetIPProperties().UnicastAddresses.Any(a => !System.Net.IPAddress.IsLoopback(a.Address) && !a.Address.IsIPv6LinkLocal &&
                                                                   !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)))
            .Select(n => $"{n.Name} ({n.NetworkInterfaceType}): {string.Join(", ", n.GetIPProperties().UnicastAddresses.Select(a => a.Address))}");

    internal static IEnumerable<UsbDeviceFact> ReadUsbStor()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBSTOR");
        if (root is null) yield break;
        foreach (var dev in root.GetSubKeyNames())
        {
            using var d = root.OpenSubKey(dev);
            if (d is null) continue;
            foreach (var serial in d.GetSubKeyNames())
            {
                using var s = d.OpenSubKey(serial);
                yield return new UsbDeviceFact(dev, serial, s?.GetValue("FriendlyName")?.ToString() ?? "", @"HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR");
            }
        }
    }

    // ---- logs -------------------------------------------------------------------------------------------------

    private static void ReadChannel(StationFacts f, string channel, int[] ids, DateTimeOffset since, int maxPerId)
    {
        DateTimeOffset? oldest = null;
        long count = 0;
        try
        {
            using (var r = new EventLogReader(new EventLogQuery(channel, PathType.LogName, "*")))
            using (var first = r.ReadEvent())
                oldest = first?.TimeCreated?.ToUniversalTime();

            var perId = new Dictionary<int, int>();
            // Windows rejects an event-log XPath with more than 22 OR-ed conditions ("The specified query is invalid"); Security asks for 25 IDs.
            foreach (var xpath in EventLogXPath.ForIds(ids, since))
            {
                var q = new EventLogQuery(channel, PathType.LogName, xpath) { ReverseDirection = true };
                using var reader = new EventLogReader(q);
                for (var rec = reader.ReadEvent(); rec is not null; rec = reader.ReadEvent())
                {
                    using (rec)
                    {
                        perId.TryGetValue(rec.Id, out var n);
                        if (n >= maxPerId) continue;
                        perId[rec.Id] = n + 1;
                        count++;
                        f.Logs.Add(new LogFact(channel, rec.Id, rec.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue, rec.RecordId ?? 0,
                            rec.UserId?.Value ?? "", ReadData(rec)));
                    }
                }
            }
            foreach (var (eid, n) in perId.Where(p => p.Value >= maxPerId))
                f.Gaps.Add(new EvidenceGap($"{channel} {eid}", EvidenceStatus.Partial, $"limitat la cele mai recente {LogAnalyzer.Dfir.Language.RoText.Count(maxPerId, "eveniment", "evenimente")}",
                    "Numărătorile pentru acest eveniment sunt minime, nu totale", "Analiză offline a jurnalului exportat", "Da"));
            f.Coverage.Add(new ChannelCoverage(channel, oldest, count, true,
                oldest is { } o && o > since ? $"jurnalul începe abia la {o:yyyy-MM-dd HH:mm} UTC (după începutul perioadei)" : ""));
        }
        catch (EventLogNotFoundException)
        {
            f.Coverage.Add(new ChannelCoverage(channel, null, 0, false, "jurnalul nu există pe această stație"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or EventLogException)
        {
            f.Coverage.Add(new ChannelCoverage(channel, oldest, count, false, ex.Message));
            f.Gaps.Add(new EvidenceGap($"Jurnal {channel}", EvidenceStatus.NotAvailable, ex is UnauthorizedAccessException ? "acces refuzat (necesită administrator)" : ex.Message,
                "Activitatea înregistrată în acest jurnal nu este evaluată", "Export wevtutil ca administrator", "Cu drepturi de administrator"));
        }
    }

    private static Dictionary<string, string> ReadData(EventRecord rec)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var x = XDocument.Parse(rec.ToXml());
            var root = x.Root!;
            int i = 0;
            foreach (var section in root.Elements().Where(e => e.Name.LocalName is "EventData" or "UserData"))
                foreach (var e in section.Descendants().Where(e => !e.HasElements))
                    d[e.Attribute("Name")?.Value ?? (e.Name.LocalName == "Data" ? $"Data{i++}" : e.Name.LocalName)] = e.Value;
        }
        catch (System.Xml.XmlException ex) { d["_XmlError"] = ex.Message; }
        return d;
    }
}

/// <summary>
/// Event-log XPath for a list of event IDs. The Windows event-log query engine refuses more than 22 OR-ed conditions in one expression
/// ("The specified query is invalid", measured on Windows 11), so the IDs are split into groups of at most <see cref="MaxIdsPerQuery"/>.
/// </summary>
public static class EventLogXPath
{
    public const int MaxIdsPerQuery = 20;

    public static IEnumerable<string> ForIds(IReadOnlyCollection<int> ids, DateTimeOffset since)
    {
        var time = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var chunk in ids.Distinct().Chunk(MaxIdsPerQuery))
            yield return $"*[System[({string.Join(" or ", chunk.Select(i => $"EventID={i}"))}) and TimeCreated[@SystemTime>='{time}']]]";
    }
}
