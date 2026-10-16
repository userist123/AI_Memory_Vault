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

public enum VolumeKind { Sistem, Fix, Amovibil }

/// <summary>
/// One volume as BitLocker reports it (Win32_EncryptableVolume). ProtectionStatus: 0 = neprotejat, 1 = protejat, 2 = necunoscut (for example an
/// encrypted medium that is still locked); null = Windows reported nothing for this drive. Protectors: KeyProtectorType codes (1 = TPM, 4 = TPM + PIN,
/// 3 = recovery password, ...); null = they could not be read.
/// </summary>
public sealed record VolumeFact(string Drive, VolumeKind Kind, int? ProtectionStatus, int? ConversionStatus, IReadOnlyList<int>? Protectors, string Note = "");

/// <summary>An antivirus registered in Windows Security Center (root\SecurityCenter2 AntiVirusProduct); ProductState is the raw bit field.</summary>
public sealed record AntivirusProductFact(string Name, uint? ProductState);

/// <summary>A shared folder or device (Win32_Share). Type 0x80000000 and above = the default administrative shares (C$, ADMIN$, IPC$).</summary>
public sealed record ShareFact(string Name, string Path, uint Type)
{
    public bool IsDefaultAdminShare => Type >= 0x80000000u;
}

/// <summary>The audit rules (SACL) of one classified folder from the procedure profile. AuditRules null = not read (see Error).</summary>
public sealed record FolderAuditFact(string Path, bool Exists, int? AuditRules, string Rules, string Error);

/// <summary>One installed Windows update (Win32_QuickFixEngineering).</summary>
public sealed record InstalledUpdateFact(string HotFixId, DateTime? InstalledOn, string Description);

/// <summary>Everything collected from the station for a control audit. Collection never changes the station.</summary>
public sealed class StationFacts
{
    public string Host { get; init; } = Environment.MachineName;
    public DateTimeOffset CollectedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset PeriodStartUtc { get; init; }
    public DateTimeOffset PeriodEndUtc { get; init; }
    public bool IsAdministrator { get; init; }
    /// <summary>The edition's compile-time posture; decides which checks apply (see ControlApplicability). Default: the strictest, so a forgotten value never relaxes a rule.</summary>
    public LogAnalyzer.Core.Services.Edition.StationPosture Posture { get; init; } = LogAnalyzer.Core.Services.Edition.StationPosture.Standalone;
    public List<LocalAccount> Accounts { get; init; } = [];
    public PasswordPolicy? PasswordPolicy { get; set; }
    public Dictionary<string, AuditSetting>? AuditPolicy { get; set; }
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DefenderExclusions { get; init; } = [];
    public List<NetworkProfileFact> NetworkProfiles { get; init; } = [];
    public List<string> ConnectedInterfacesNow { get; init; } = [];
    /// <summary>Configured or present ways out of the station, read passively (check N11); null = not collected.</summary>
    public List<EgressPathFact>? EgressPaths { get; set; }
    /// <summary>The sources of check N11 that could not be read ("name (reason)"): with any of them N11 is never CONFORM.</summary>
    public List<string> EgressUnread { get; init; } = [];
    /// <summary>Result of the active N13 probe (Network edition only); null = not probed.</summary>
    public DomainControllerProbeResult? DomainController { get; set; }
    public List<UsbDeviceFact> UsbDevices { get; init; } = [];
    public List<LogFact> Logs { get; init; } = [];
    public List<ChannelCoverage> Coverage { get; init; } = [];
    public List<EvidenceGap> Gaps { get; init; } = [];

    // Air-gap station requirements (comparison 2026-10-12, rows A2-A16, B8, C8). For every list, null = the source could not be read.
    /// <summary>Every volume with a drive letter and its BitLocker state; null = BitLocker could not be queried (administrator).</summary>
    public List<VolumeFact>? Volumes { get; set; }
    /// <summary>Drive letters of the removable volumes connected now (readable without administrator rights).</summary>
    public List<string> RemovableDrivesNow { get; init; } = [];
    public List<AntivirusProductFact>? AntivirusProducts { get; set; }
    public List<ShareFact>? Shares { get; set; }
    /// <summary>One entry per classified folder of the procedure profile; an empty list = the profile lists no folder; null = not collected.</summary>
    public List<FolderAuditFact>? FolderAudits { get; set; }
    public List<InstalledUpdateFact>? InstalledUpdates { get; set; }
    /// <summary>The register of offline updates kept by the organisation; an empty list = the register is not filled in; null = not given.</summary>
    public List<LogAnalyzer.Dfir.Registers.UpdateRow>? OfflineUpdateRegister { get; set; }
    /// <summary>The latest time the application itself recorded in its chained audit logs ("time memory", check S05); null = no earlier record.</summary>
    public DateTimeOffset? LatestAppRecordUtc { get; set; }
    /// <summary>Set when a recorded time was made under a wrong clock and the correction is not yet confirmed (check S05).</summary>
    public string ClockSuspectNote { get; set; } = "";
    /// <summary>"corectat de ... la ..., motiv: ..." when the operator confirmed a clock correction (check S05); empty otherwise.</summary>
    public string ClockAckText { get; set; } = "";
    /// <summary>
    /// Installation date of the application on this station: the time of the first chained record of its audit chain (owner directive 2026-10-16).
    /// On a Standalone station every network connection after it is NECONFORM; earlier ones are history (DE VERIFICAT). Null = unknown, see <see cref="AppInstallBasis"/>.
    /// </summary>
    public DateTimeOffset? AppInstalledUtc { get; set; }
    public string AppInstallBasis { get; set; } = "";
    /// <summary>What contradicts the installation date (see <see cref="LogAnalyzer.Dfir.Case.AppInstall.Violations"/>); NECONFORM on Standalone.</summary>
    public List<string> AppInstallViolations { get; set; } = [];
    /// <summary>DE VERIFICAT notes about the installation date (see <see cref="LogAnalyzer.Dfir.Case.AppInstall.ToVerify"/>).</summary>
    public List<string> AppInstallToVerify { get; set; } = [];
    /// <summary>Time of the first chained record of the application's audit chain; printed in the report next to the computed date.</summary>
    public DateTimeOffset? AppInstallChainFirstUtc { get; set; }
    /// <summary>Check A11: the previous anchor code checked against the application's audit log; null = not checked (the check is NEDETERMINAT).</summary>
    public LogAnalyzer.Dfir.Case.AnchorContinuity? PreviousAnchor { get; set; }
    /// <summary>True when the previous code checked in A11 is the one the station proposed (unsafe), false when it was typed from the paper.</summary>
    public bool PreviousAnchorFromStation { get; set; }
    /// <summary>Check A13: the officer list and trust folder against the state at the paper anchor; null = the audit log was not available.</summary>
    public LogAnalyzer.Dfir.Reporting.OfficerStateAssessment? OfficerAssessment { get; set; }
    /// <summary>Check A14: the install date recorded with the anchor typed from the paper; null = the audit log was not available.</summary>
    public LogAnalyzer.Dfir.Case.AnchoredInstall? InstallAtPaperAnchor { get; set; }
}

public static class StationFactCollector
{
    // Channel → event IDs read for the control audit.
    public static readonly IReadOnlyDictionary<string, int[]> Queries = new Dictionary<string, int[]>
    {
        ["Security"] = [4624, 4625, 4648, 4672, 4720, 4722, 4723, 4724, 4725, 4726, 4738, 4740, 4732, 4733, 4728, 4729, 4756, 4757,
                        4719, 1102, 1100, 4616, 4697, 4698, 4702, 6416],
        ["System"] = [104, 7045, 6005, 6006, 6008, 1074],
        ["Application"] = [11707, 11724, 1033, 1034],
        ["Microsoft-Windows-Partition/Diagnostic"] = [1006],
        ["Microsoft-Windows-PowerShell/Operational"] = [4104],
        ["Microsoft-Windows-TerminalServices-LocalSessionManager/Operational"] = [21, 24, 25],
        ["Microsoft-Windows-WLAN-AutoConfig/Operational"] = [8001, 8003],
        ["Microsoft-Windows-NetworkProfile/Operational"] = [10000, 10001],
        // Application allow-listing: AppLocker would-block (8003, 8006) / blocked (8004, 8007); App Control (WDAC) audit (3076) / blocked (3077).
        [AppLockerExeChannel] = [8003, 8004],
        [AppLockerMsiChannel] = [8006, 8007],
        [CodeIntegrityChannel] = [3076, 3077],
        // Defender scans (1000 started, 1001 finished) and detections (1116, 1117): was a medium scanned right after it was connected?
        [DefenderChannel] = [1000, 1001, 1116, 1117],
    };

    public const string AppLockerExeChannel = "Microsoft-Windows-AppLocker/EXE and DLL";
    public const string AppLockerMsiChannel = "Microsoft-Windows-AppLocker/MSI and Script";
    public const string CodeIntegrityChannel = "Microsoft-Windows-CodeIntegrity/Operational";
    public const string DefenderChannel = "Microsoft-Windows-Windows Defender/Operational";

    /// <param name="classifiedFolders">The classified folders of the procedure profile (their audit rules are read); null = not collected.</param>
    /// <param name="offlineUpdates">The organisation's register of offline updates (compared with the installed updates); null = not given.</param>
    /// <param name="dcProbe">The ACTIVE domain-controller probe (check N13); only the Network edition has one. Used only on the IsolatedNetwork posture and
    /// only on a domain-joined station; null = not probed.</param>
    public static StationFacts Collect(DateTimeOffset periodStartUtc, LogAnalyzer.Core.Services.Edition.StationPosture posture, int maxPerId = 5000, CancellationToken ct = default,
                                       IReadOnlyList<string>? classifiedFolders = null, IReadOnlyList<LogAnalyzer.Dfir.Registers.UpdateRow>? offlineUpdates = null,
                                       IDomainControllerProbe? dcProbe = null)
    {
        using var id = WindowsIdentity.GetCurrent();
        var facts = new StationFacts
        {
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = DateTimeOffset.UtcNow,
            IsAdministrator = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator),
            Posture = posture,
        };
        var install = LogAnalyzer.Dfir.Windows.Acquisition.AppInstallTraces.ReadInstall();
        facts.AppInstalledUtc = install.Utc; facts.AppInstallBasis = install.Basis; facts.AppInstallViolations.AddRange(install.Violations);
        facts.AppInstallToVerify.AddRange(install.ToVerify); facts.AppInstallChainFirstUtc = install.ChainFirstUtc;
        Try(facts, "Conturi locale", () => facts.Accounts.AddRange(ReadAccounts()));
        Try(facts, "Politica de parole", () => facts.PasswordPolicy = LocalPasswordPolicy.Query());
        facts.AuditPolicy = AuditPolicy.Query() is { } ap ? new Dictionary<string, AuditSetting>(ap) : null;
        if (facts.AuditPolicy is null)
            facts.Gaps.Add(new EvidenceGap("Politica de audit", EvidenceStatus.NotAvailable, "nu poate fi citită fără drepturi de administrator",
                "Nu se poate spune ce activitate înregistrează stația", "auditpol /get /category:* (administrator)", "Cu drepturi de administrator"));
        Try(facts, "Setări de securitate", () => ReadSettings(facts));
        Try(facts, "Setări de securitate (extinse)", () => ReadSettingsWave2(facts));
        Try(facts, "Setări de securitate (stație izolată)", () => ReadSettingsWave3(facts));
        Try(facts, "Volume și BitLocker", () => ReadVolumes(facts));
        Try(facts, "Căi spre internet (configurație locală)", () => facts.EgressPaths = EgressPathCollector.Collect(facts.EgressUnread));
        Try(facts, "Apartenența la domeniu", () => ReadDomainMembership(facts));
        if (dcProbe is not null && posture == LogAnalyzer.Core.Services.Edition.StationPosture.IsolatedNetwork
            && facts.Settings.TryGetValue("Domain.PartOfDomain", out var joined) && joined == "da" && facts.Settings.TryGetValue("Domain.Name", out var domain))
            Try(facts, "Controlerul de domeniu (verificare activă)", () => facts.DomainController = dcProbe.Probe(domain, ct));
        if (classifiedFolders is not null) facts.FolderAudits = ReadFolderAudits(classifiedFolders);
        if (offlineUpdates is not null) facts.OfflineUpdateRegister = [.. offlineUpdates];
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

    /// <summary>
    /// Runs one source. A source that cannot be read, or whose content cannot be parsed (a malformed registry value, a truncated blob), becomes a
    /// gap and the checks that depend on it NEDETERMINATE; it never aborts the whole control run.
    /// </summary>
    internal static void Try(StationFacts f, string what, Action a)
    {
        try { a(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Security.SecurityException or IOException or System.Runtime.InteropServices.COMException or InvalidOperationException
                                      or ArgumentException or FormatException or IndexOutOfRangeException or OverflowException)
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

    /// <summary>
    /// The WQL query for the members of a local group (security audit #16). The domain and name come from WMI but a group can be renamed to
    /// contain a quote or a backslash, so both are escaped twice: as key values of the object path (single quotes: \ and ') and then as the
    /// WQL string that holds the path (double quotes: \ and "). Without it a renamed group breaks the query and its members come back empty.
    /// </summary>
    public static string GroupUserQuery(string domain, string name)
    {
        static string Key(string v) => v.Replace("\\", "\\\\").Replace("'", "\\'");
        var path = $"Win32_Group.Domain='{Key(domain)}',Name='{Key(name)}'";
        return $"SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent = \"{path.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    /// <summary>Members (user and group names) of the local group with the given well-known SID; the group name is localized.</summary>
    private static HashSet<string> GroupMembers(string groupSid)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var g = new ManagementObjectSearcher($"SELECT Name, Domain FROM Win32_Group WHERE LocalAccount = TRUE AND SID = '{groupSid}'");
        foreach (ManagementObject grp in g.Get())
        {
            using var m = new ManagementObjectSearcher(GroupUserQuery(grp["Domain"]?.ToString() ?? "", grp["Name"]?.ToString() ?? ""));
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
        s["UAC.PromptOnSecureDesktop"] = Reg(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop") ?? "";
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

    /// <summary>Windows install date (HKLM\...\CurrentVersion\InstallDate, Unix seconds); null when it cannot be read.</summary>
    public static DateTimeOffset? ReadInstallDate() =>
        long.TryParse(Hklm(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallDate"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var secs) && secs > 0
            ? DateTimeOffset.FromUnixTimeSeconds(secs) : null;

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
            // Directory.Exists says false on "access denied": a folder is "0 profiles" only when confirmed missing, otherwise the count stays unread.
            if (EgressPathCollector.CountWifiProfiles(dir, out var why) is { } n) s["Wlan.ProfileCount"] = n.ToString();
            else s["Wlan.ProfileUnread"] = why;
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
                parts.Add($"{col}: mod {mode}, {LogAnalyzer.Dfir.Language.RoText.CountFem(rules, "regulă", "reguli")}");
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
        s["OS.InstallDate"] = ReadInstallDate() is { } inst ? inst.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) : Absent;
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

    // ---- settings, third wave: air-gap station requirements (read-only) ---------------------------------------------

    private static readonly Guid SubNoneGuid = new("fea3413e-7e05-4911-9a71-700331f1c294");        // powercfg SUB_NONE
    private static readonly Guid ConsoleLockGuid = new("0e796bdb-100d-47d6-a2d5-f7d2daa51f51");    // powercfg CONSOLELOCK: password on wake

    /// <summary>Domain membership (check N12), read from WMI Win32_ComputerSystem: "da" / "nu" and the domain or workgroup name. Local read only.</summary>
    private static void ReadDomainMembership(StationFacts f)
    {
        using var cs = new ManagementObjectSearcher("SELECT PartOfDomain, Domain FROM Win32_ComputerSystem");
        foreach (ManagementObject o in cs.Get())
        {
            f.Settings["Domain.PartOfDomain"] = o["PartOfDomain"] is true ? "da" : "nu";
            f.Settings["Domain.Name"] = o["Domain"]?.ToString() ?? "";
            break;
        }
    }

    private static void ReadSettingsWave3(StationFacts f)
    {
        var s = f.Settings;

        // TPM (WMI class readable by administrators only; without rights the keys stay empty = not read).
        Guard(() =>
        {
            using var tpm = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftTpm", "SELECT * FROM Win32_Tpm");
            var list = tpm.Get().Cast<ManagementObject>().ToList();
            if (list.Count == 0) { s["TPM.Present"] = "nu"; return; }
            var o = list[0];
            s["TPM.Present"] = "da";
            s["TPM.Enabled"] = o["IsEnabled_InitialValue"]?.ToString() ?? "";
            s["TPM.Activated"] = o["IsActivated_InitialValue"]?.ToString() ?? "";
            s["TPM.SpecVersion"] = o["SpecVersion"]?.ToString() ?? "";
            try { using var ready = o.InvokeMethod("IsReady", null, null); s["TPM.Ready"] = ready?["IsReady"]?.ToString() ?? ""; }
            catch (ManagementException) { s["TPM.Ready"] = ""; }
        });

        // Kernel DMA protection (what msinfo32 shows) and the device enumeration policy.
        s["DMA.KernelProtection"] = KernelDmaProtection();
        s["DMA.EnumerationPolicy"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\Kernel DMA Protection", "DeviceEnumerationPolicy");

        // Virtualization-based security and App Control (WDAC) enforcement, from the same WMI class as Credential Guard.
        Guard(() =>
        {
            using var dg = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard",
                "SELECT VirtualizationBasedSecurityStatus, CodeIntegrityPolicyEnforcementStatus, UsermodeCodeIntegrityPolicyEnforcementStatus FROM Win32_DeviceGuard");
            foreach (ManagementBaseObject o in dg.Get())
            {
                s["DeviceGuard.VbsStatus"] = o["VirtualizationBasedSecurityStatus"]?.ToString() ?? "";
                s["CI.KernelEnforcement"] = o["CodeIntegrityPolicyEnforcementStatus"]?.ToString() ?? "";
                s["CI.UserModeEnforcement"] = o["UsermodeCodeIntegrityPolicyEnforcementStatus"]?.ToString() ?? "";
            }
        });
        Guard(() =>
        {
            // Only the number of active policy files; their content is never opened.
            var ci = Path.Combine(Environment.SystemDirectory, "CodeIntegrity");
            var active = Path.Combine(ci, "CiPolicies", "Active");
            s["WDAC.ActivePolicyFiles"] = Directory.Exists(active) ? Directory.EnumerateFiles(active, "*.cip").Count().ToString() : "0";
            s["WDAC.LegacyPolicy"] = File.Exists(Path.Combine(ci, "SIPolicy.p7b")) ? "da" : "nu";
        });

        // Password on wake: the policy (STIG WN11-CC-000145 / -000150), the value of the active power plan, and the current user's sign-in delay.
        const string wakePolicy = @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\0e796bdb-100d-47d6-a2d5-f7d2daa51f51";
        s["Wake.PolicyAC"] = Hklm(wakePolicy, "ACSettingIndex");
        s["Wake.PolicyDC"] = Hklm(wakePolicy, "DCSettingIndex");
        (s["Wake.EffectiveAC"], s["Wake.EffectiveDC"]) = ActiveSchemeConsoleLock();
        s["Wake.DelayLockInterval"] = RegRead(Registry.CurrentUser, @"Control Panel\Desktop", "DelayLockInterval");   // current user only

        // Defender scans (a separate query: an old Defender without these properties must not hide the main status read above).
        Guard(() =>
        {
            using var mp = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT FullScanEndTime, QuickScanEndTime, AMProductVersion FROM MSFT_MpComputerStatus");
            foreach (ManagementBaseObject o in mp.Get())
            {
                s["Defender.FullScanEndUtc"] = WmiTime(o["FullScanEndTime"]);
                s["Defender.QuickScanEndUtc"] = WmiTime(o["QuickScanEndTime"]);
                s["Defender.ProductVersion"] = o["AMProductVersion"]?.ToString() ?? "";
            }
        });
        Guard(() =>
        {
            using var sc = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
            f.AntivirusProducts = sc.Get().Cast<ManagementBaseObject>()
                .Select(o => new AntivirusProductFact(o["displayName"]?.ToString() ?? "", o["productState"] is { } ps ? Convert.ToUInt32(ps, System.Globalization.CultureInfo.InvariantCulture) : null)).ToList();
        });

        // Other interconnection paths (STIG WN11-00-000060, WN11-CC-000155, WN11-00-000395, WN11-CC-000044).
        Guard(() =>
        {
            using var sh = new ManagementObjectSearcher("SELECT Name, Path, Type FROM Win32_Share");
            f.Shares = sh.Get().Cast<ManagementBaseObject>()
                .Select(o => new ShareFact(o["Name"]?.ToString() ?? "", o["Path"]?.ToString() ?? "", o["Type"] is { } t ? Convert.ToUInt32(t, System.Globalization.CultureInfo.InvariantCulture) : 0u)).ToList();
        });
        s["RA.AllowToGetHelp"] = Hklm(@"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp");
        s["RA.PolicyAllowToGetHelp"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "fAllowToGetHelp");
        s["WinRM.ServiceStart"] = Hklm(@"SYSTEM\CurrentControlSet\Services\WinRM", "Start");
        s["WinRM.Listeners"] = SubKeyNames(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WSMAN\Listener");   // [NESIGUR] listener location; confirm with „winrm enumerate” in the lab
        Guard(() =>
        {
            using var svc = new ManagementObjectSearcher("SELECT State FROM Win32_Service WHERE Name = 'WinRM'");
            foreach (ManagementBaseObject o in svc.Get()) s["WinRM.State"] = o["State"]?.ToString() ?? "";
        });
        s["PortProxy.Entries"] = PortProxyEntries();
        s["ICS.ServiceStart"] = Hklm(@"SYSTEM\CurrentControlSet\Services\SharedAccess", "Start");
        s["ICS.PolicyShowUI"] = Hklm(@"SOFTWARE\Policies\Microsoft\Windows\Network Connections", "NC_ShowSharedAccessUI");
        s["ICS.Shared"] = IcsSharedConnections();

        // BitLocker To Go: write access to unencrypted removable drives (same keys as DevicePolicyCheck).
        var rdv = Hklm(@"SYSTEM\CurrentControlSet\Policies\Microsoft\FVE", "RDVDenyWriteAccess");
        var rdvPolicy = Hklm(@"SOFTWARE\Policies\Microsoft\FVE", "RDVDenyWriteAccess");
        s["BitLocker.RDVDenyWriteAccess"] = rdv == Absent || (rdv == "" && rdvPolicy != Absent) ? rdvPolicy : rdv;

        // Installed Windows updates, compared with the register of offline updates.
        Guard(() => f.InstalledUpdates = ReadInstalledUpdates());
    }

    /// <summary>The installed Windows updates (Win32_QuickFixEngineering), read-only. Throws ManagementException when WMI cannot be read.</summary>
    public static List<InstalledUpdateFact> ReadInstalledUpdates()
    {
        using var q = new ManagementObjectSearcher("SELECT HotFixID, InstalledOn, Description FROM Win32_QuickFixEngineering");
        return q.Get().Cast<ManagementBaseObject>()
            .Select(o => new InstalledUpdateFact(o["HotFixID"]?.ToString() ?? "", ParseInstalledOn(o["InstalledOn"]?.ToString()), o["Description"]?.ToString() ?? "")).ToList();
    }

    /// <summary>A WMI date as UTC ISO text; <see cref="Absent"/> when there is none (a scan that never ran is reported as empty or as the year 1601).</summary>
    private static string WmiTime(object? v)
    {
        if (v is not string d || d.Length < 14) return Absent;
        try { var t = ManagementDateTimeConverter.ToDateTime(d).ToUniversalTime(); return t.Year < 2000 ? Absent : t.ToString("o", System.Globalization.CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { return Absent; }
    }

    /// <summary>Subkey names joined by "; ", "(niciuna)" when the key has none, <see cref="Absent"/> when the key does not exist, "" when it cannot be read.</summary>
    private static string SubKeyNames(string key)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(key);
            if (k is null) return Absent;
            var names = k.GetSubKeyNames();
            return names.Length == 0 ? "(niciuna)" : string.Join("; ", names);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }

    /// <summary>netsh interface portproxy entries, read from the registry: "v4tov4/tcp: 0.0.0.0/8080 → 10.0.0.5/80"; <see cref="Absent"/> when there is none.</summary>
    private static string PortProxyEntries()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PortProxy");
            if (root is null) return Absent;
            var list = new List<string>();
            foreach (var kind in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(kind);
                if (k is null) continue;
                foreach (var proto in k.GetSubKeyNames())
                {
                    using var p = k.OpenSubKey(proto);
                    if (p is null) continue;
                    foreach (var n in p.GetValueNames()) list.Add($"{kind}/{proto}: {n} → {p.GetValue(n)}");
                }
            }
            return list.Count == 0 ? Absent : string.Join("; ", list);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }

    /// <summary>Connections with Internet Connection Sharing on, read through the Windows sharing interface (no change): "da: …", "nu", or "" when unreadable.</summary>
    private static string IcsSharedConnections()
    {
        try
        {
            var t = Type.GetTypeFromProgID("HNetCfg.HNetShare", false);
            if (t is null) return "";
            dynamic mgr = Activator.CreateInstance(t)!;
            var names = new List<string>();
            foreach (var conn in mgr.EnumEveryConnection)
            {
                dynamic cfg = mgr.INetSharingConfigurationForINetConnection[conn];
                if (!(bool)cfg.SharingEnabled) continue;
                dynamic props = mgr.NetConnectionProps[conn];
                names.Add($"{(string)props.Name} ({((int)cfg.SharingConnectionType == 0 ? "își partajează conexiunea" : "primește conexiunea partajată")})");
            }
            return names.Count == 0 ? "nu" : "da: " + string.Join("; ", names);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or UnauthorizedAccessException) { return ""; }
    }

    // [NESIGUR] SystemDmaGuardPolicyInformation = 202, from the public Windows headers (phnt); it is the value msinfo32 shows as „Kernel DMA Protection”. Confirm in the lab.
    private const int SystemDmaGuardPolicyInformation = 202;
    [System.Runtime.InteropServices.DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int infoClass, out byte info, int length, out int returnLength);

    /// <summary>"1" = on, "0" = off, "" = unknown (the call failed or the Windows version does not know the information class).</summary>
    private static string KernelDmaProtection()
    {
        try { return NtQuerySystemInformation(SystemDmaGuardPolicyInformation, out var on, 1, out _) == 0 ? (on != 0 ? "1" : "0") : ""; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ""; }
    }

    [System.Runtime.InteropServices.DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
    [System.Runtime.InteropServices.DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);
    [System.Runtime.InteropServices.DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    /// <summary>CONSOLELOCK of the active power plan, on mains (AC) and on battery (DC): "1" = password on wake, "0" = no password, "" = not read.</summary>
    private static (string Ac, string Dc) ActiveSchemeConsoleLock()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0 || p == IntPtr.Zero) return ("", "");
            try
            {
                var scheme = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(p);
                Guid sub = SubNoneGuid, setting = ConsoleLockGuid;
                var ac = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var a) == 0 ? a.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
                var dc = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var d) == 0 ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
                return (ac, dc);
            }
            finally { LocalFree(p); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ("", ""); }
    }

    // ---- volumes (BitLocker on every fixed volume, BitLocker To Go on connected media) -------------------------

    private static void ReadVolumes(StationFacts f)
    {
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable).ToList();
        foreach (var d in drives.Where(d => d.DriveType == DriveType.Removable)) f.RemovableDrivesNow.Add(d.Name.TrimEnd('\\'));
        var sys = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        try
        {
            var list = new List<VolumeFact>();
            using var s = new ManagementObjectSearcher(@"root\cimv2\Security\MicrosoftVolumeEncryption", "SELECT * FROM Win32_EncryptableVolume");
            foreach (ManagementObject v in s.Get())
            {
                using (v)
                {
                    // Volumes without a letter (recovery or system partitions) hold no user data that can be opened directly; they are not evaluated.
                    if (v["DriveLetter"]?.ToString() is not { Length: > 0 } drive) continue;
                    int? type = Int(v.Properties.Cast<PropertyData>().Any(p => p.Name == "VolumeType") ? v["VolumeType"] : null);
                    var kind = drive.Equals(sys, StringComparison.OrdinalIgnoreCase) || type == 0 ? VolumeKind.Sistem
                        : type == 2 || f.RemovableDrivesNow.Contains(drive, StringComparer.OrdinalIgnoreCase) ? VolumeKind.Amovibil : VolumeKind.Fix;
                    list.Add(new VolumeFact(drive, kind, Int(v["ProtectionStatus"]), Int(v["ConversionStatus"]), Protectors(v)));
                }
            }
            foreach (var d in drives)
            {
                var letter = d.Name.TrimEnd('\\');
                if (!list.Any(x => x.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new VolumeFact(letter, d.DriveType == DriveType.Removable ? VolumeKind.Amovibil : letter.Equals(sys, StringComparison.OrdinalIgnoreCase) ? VolumeKind.Sistem : VolumeKind.Fix,
                        null, null, null, "Windows nu raportează starea BitLocker pentru această unitate"));
            }
            f.Volumes = list;
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            f.Settings["BitLocker.Volumes.Error"] = ex.Message;
        }

        static int? Int(object? o) => o is null ? null : Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The key protector types of a volume (read-only WMI methods GetKeyProtectors / GetKeyProtectorType); null when they cannot be read.</summary>
    private static IReadOnlyList<int>? Protectors(ManagementObject v)
    {
        try
        {
            using var input = v.GetMethodParameters("GetKeyProtectors");
            input["KeyProtectorType"] = 0u;
            using var output = v.InvokeMethod("GetKeyProtectors", input, null);
            if (output?["VolumeKeyProtectorID"] is not string[] ids) return [];
            var types = new List<int>();
            foreach (var id in ids)
            {
                using var ti = v.GetMethodParameters("GetKeyProtectorType");
                ti["VolumeKeyProtectorID"] = id;
                using var to = v.InvokeMethod("GetKeyProtectorType", ti, null);
                if (to?["KeyProtectorType"] is { } kt) types.Add(Convert.ToInt32(kt, System.Globalization.CultureInfo.InvariantCulture));
            }
            return types;
        }
        catch (ManagementException) { return null; }
    }

    // ---- classified folders: audit rules (SACL) -----------------------------------------------------------------

    /// <summary>
    /// Reads the audit rules of each folder. Reading a SACL needs the "Manage auditing and security log" right (administrators); the .NET call turns it
    /// on in this process only, for the read, and nothing on the folder changes. Without the right the folder is reported as not read.
    /// </summary>
    internal static List<FolderAuditFact> ReadFolderAudits(IEnumerable<string> folders)
    {
        var r = new List<FolderAuditFact>();
        foreach (var path in folders.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var di = new DirectoryInfo(path);
                if (!di.Exists) { r.Add(new FolderAuditFact(path, false, null, "", "")); continue; }
                var sec = di.GetAccessControl(System.Security.AccessControl.AccessControlSections.Audit);
                var rules = sec.GetAuditRules(true, true, typeof(SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAuditRule>().ToList();
                r.Add(new FolderAuditFact(path, true, rules.Count, string.Join("; ", rules.Select(x => $"{Account(x.IdentityReference)}: {x.FileSystemRights} ({x.AuditFlags})")), ""));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                r.Add(new FolderAuditFact(path, true, null, "", ex is UnauthorizedAccessException ? "regulile de audit nu au putut fi citite (necesită administrator)" : ex.Message));
            }
        }
        return r;

        static string Account(IdentityReference id)
        {
            try { return id.Translate(typeof(NTAccount)).Value; }
            catch (Exception ex) when (ex is IdentityNotMappedException or SystemException) { return id.Value; }
        }
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
            var x = LogAnalyzer.Dfir.IO.SafeXml.Parse(rec.ToXml());
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
