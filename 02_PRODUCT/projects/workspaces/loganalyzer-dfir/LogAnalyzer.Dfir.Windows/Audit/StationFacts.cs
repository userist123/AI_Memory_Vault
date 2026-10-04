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

    private static IEnumerable<LocalAccount> ReadAccounts()
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
        catch (ManagementException) { }
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

    private static IEnumerable<UsbDeviceFact> ReadUsbStor()
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

            var idFilter = string.Join(" or ", ids.Select(i => $"EventID={i}"));
            var q = new EventLogQuery(channel, PathType.LogName,
                $"*[System[({idFilter}) and TimeCreated[@SystemTime>='{since.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}']]]") { ReverseDirection = true };
            var perId = new Dictionary<int, int>();
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
            foreach (var (eid, n) in perId.Where(p => p.Value >= maxPerId))
                f.Gaps.Add(new EvidenceGap($"{channel} {eid}", EvidenceStatus.Partial, $"limitat la cele mai recente {maxPerId} evenimente",
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
        catch (System.Xml.XmlException) { }
        return d;
    }
}
