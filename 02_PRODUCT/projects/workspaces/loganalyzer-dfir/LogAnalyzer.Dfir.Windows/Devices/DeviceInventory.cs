using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using LogAnalyzer.Dfir.Devices;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Devices;

/// <summary>
/// Every storage medium and connectable device Windows knows about, present or not (docs/dfir/RESEARCH_2026-10-11_MEDII_DE_STOCARE.md §4.1–4.2):
/// SetupAPI over all device classes (with the first-install / last-arrival / last-removal times Windows keeps per device), enriched for the
/// devices present now from WMI (capacity, real disk serial, drive letters, volume label and serial, BitLocker), plus network drives from the
/// loaded user hives. Read-only: nothing is written to the station. Every source that could not be read is listed in <see cref="Problems"/>.
/// </summary>
public static class DeviceInventory
{
    public sealed record Result(IReadOnlyList<DeviceRecord> Devices, IReadOnlyList<string> Problems);

    public static Result Collect()
    {
        var problems = new List<string>();
        var devices = new List<DeviceRecord>();
        var wpdVolumes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try { devices.AddRange(FromSetupApi(wpdVolumes)); }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or EntryPointNotFoundException) { problems.Add("Lista dispozitivelor Windows (SetupAPI) nu a putut fi citită: " + ex.Message); }

        var partition = PartitionEvents(problems);
        var volumeToSerial = new Dictionary<string, string>(MountedDevices(), StringComparer.OrdinalIgnoreCase);
        foreach (var e in partition) if (e.RegistryId.Length > 0 && e.Serial.Length > 0) volumeToSerial[@"\??\Volume" + e.RegistryId] = e.Serial;
        devices = AttachLabels(devices, wpdVolumes, volumeToSerial);
        devices = AttachHistory(devices, partition);

        var present = new Dictionary<string, PresentInfo>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var p in PresentDisks()) present[p.PnpId] = p; }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { problems.Add("Discurile prezente (WMI Win32_DiskDrive) nu au putut fi citite: " + ex.Message); }
        try { foreach (var p in PresentOptical()) present[p.PnpId] = p; }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { problems.Add("Unitățile CD/DVD (WMI Win32_CDROMDrive) nu au putut fi citite: " + ex.Message); }

        var bitlocker = BitLockerByLetter(problems);
        for (int i = 0; i < devices.Count; i++)
        {
            if (!present.TryGetValue(devices[i].InstanceId, out var p)) continue;
            var enc = p.Letters.Select(l => bitlocker.TryGetValue(l, out var s) ? $"{l} {s}" : null).Where(s => s is not null).ToList();
            devices[i] = devices[i] with
            {
                Kind = DeviceRules.WithSystemVolume(devices[i].Kind, p.Letters, Path.GetPathRoot(Environment.SystemDirectory) ?? ""),
                Serial = devices[i].Serial.Length > 0 ? devices[i].Serial : LogAnalyzer.Dfir.Registers.MediaRegister.NormalizeSerial(p.Serial.TrimEnd('.')),
                CapacityBytes = p.Size ?? devices[i].CapacityBytes,
                Bus = p.Interface.Length > 0 ? p.Interface : devices[i].Bus,
                Model = devices[i].Model.Length > 0 ? devices[i].Model : p.Model,
                DriveLetters = string.Join(" ", p.Letters),
                VolumeLabel = string.Join(" · ", p.Labels.Where(l => l.Length > 0)),
                FileSystem = string.Join(" · ", p.FileSystems.Where(l => l.Length > 0).Distinct()),
                Encryption = enc.Count > 0 ? string.Join("; ", enc) : p.Letters.Count > 0 && bitlocker.Count == 0 ? "necunoscut (necesită administrator)" : "",
                ConnectedNow = true,
                Sources = devices[i].Sources + ", WMI",
            };
        }
        devices.AddRange(NetworkDrives(problems));
        devices = AttachUsage(devices, problems);
        return new(devices.OrderByDescending(d => d.ConnectedNow).ThenBy(d => d.Kind).ThenByDescending(d => d.LastConnectedUtc ?? d.FirstConnectedUtc ?? DateTimeOffset.MinValue).ToList(), problems);
    }

    // ---- SetupAPI: every device instance, present or not -------------------------------------------------------------------------------

    private static readonly Guid NameGuid = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    private static readonly Guid TimeGuid = new("83da6326-97a6-4088-9453-a1923f573b29");
    private static readonly Guid BusGuid = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2");
    private static readonly Guid RelGuid = new("4340a6c5-93fa-4706-972c-7b648008a5a7");
    private static readonly Guid InstGuid = new("78c34fc8-104a-4aca-9ea4-524d52996e57");

    internal static List<DeviceRecord> FromSetupApi(Dictionary<string, string> wpdVolumes)
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var list = new List<DeviceRecord>();
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var id = Str(set, ref data, InstGuid, 256);
                if (id.Length == 0) continue;
                var cls = Str(set, ref data, NameGuid, 9);
                var hw = string.Join(" ", StrList(set, ref data, NameGuid, 3));
                var name = Str(set, ref data, NameGuid, 14) is { Length: > 0 } fn ? fn : Str(set, ref data, NameGuid, 2);
                var parent = Str(set, ref data, RelGuid, 8);
                if (id.StartsWith(@"SWD\WPDBUSENUM\{", StringComparison.OrdinalIgnoreCase) && name.Length > 0)
                    wpdVolumes[id.Substring(@"SWD\WPDBUSENUM\".Length, 38)] = name;      // a stick's volume: its label, keyed by volume GUID
                var kind = DeviceRules.Classify(id, cls, hw + " " + parent, name);
                if (kind is null) continue;
                var (ven, prod) = DeviceRules.VendorProduct(id);
                var manufacturer = Str(set, ref data, NameGuid, 13);
                var serialSource = kind == DeviceKind.UsbStorage && id.StartsWith(@"SCSI\", StringComparison.OrdinalIgnoreCase) ? parent : id;
                list.Add(new DeviceRecord
                {
                    Kind = kind.Value,
                    InstanceId = id,
                    Name = name,
                    // "NVMe", "ATA", "SCSI" in a disk id name the bus, not the maker: the model then carries the real name
                    Manufacturer = ven.Length > 0 && ven.ToUpperInvariant() is not ("NVME" or "ATA" or "SCSI" or "GENERIC") ? ven : manufacturer.StartsWith('(') || manufacturer.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) ? "" : manufacturer,
                    Model = prod.Length > 0 ? prod : Str(set, ref data, BusGuid, 4),
                    Serial = kind == DeviceKind.Bluetooth ? BluetoothAddress(id) : DeviceRules.Serial(serialSource),
                    VidPid = DeviceRules.VidPid(parent + " " + id + " " + hw),
                    Bus = id.Split('\\')[0],
                    ConnectedNow = Bool(set, ref data, BusGuid, 5),
                    FirstConnectedUtc = Time(set, ref data, TimeGuid, 101) ?? Time(set, ref data, TimeGuid, 100),
                    LastConnectedUtc = Time(set, ref data, TimeGuid, 102),
                    LastRemovedUtc = Time(set, ref data, TimeGuid, 103),
                    Sources = "Windows SetupAPI",
                });
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    /// <summary>One disk arrival logged by Windows (Microsoft-Windows-Partition/Diagnostic 1006): who it was, when, with the volume id it got.</summary>
    internal sealed record PartitionEvent(DateTimeOffset TimeUtc, string Serial, string Manufacturer, string Model, long? Capacity, string BusType, string ParentId, string RegistryId, IReadOnlyList<string>? VolumeSerials = null);

    internal static List<PartitionEvent> PartitionEvents(List<string> problems, int max = 5000)
    {
        var list = new List<PartitionEvent>();
        try
        {
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(
                new System.Diagnostics.Eventing.Reader.EventLogQuery("Microsoft-Windows-Partition/Diagnostic", System.Diagnostics.Eventing.Reader.PathType.LogName, "*[System[EventID=1006]]") { ReverseDirection = true });
            for (var e = reader.ReadEvent(); e is not null && list.Count < max; e = reader.ReadEvent())
                using (e)
                {
                    var x = LogAnalyzer.Dfir.IO.SafeXml.Parse(e.ToXml());
                    string D(string n) => x.Descendants().FirstOrDefault(d => d.Name.LocalName == "Data" && (string?)d.Attribute("Name") == n)?.Value?.Trim() ?? "";
                    if (D("Capacity") is "0" or "") continue;   // a card reader without a card, or an empty slot
                    list.Add(new(e.TimeCreated is { } t ? new DateTimeOffset(t.ToUniversalTime()) : DateTimeOffset.MinValue,
                                 LogAnalyzer.Dfir.Registers.MediaRegister.NormalizeSerial(D("SerialNumber").TrimEnd('.')), D("Manufacturer"), D("Model"),
                                 long.TryParse(D("Capacity"), out var c) ? c : null, D("BusType"), D("ParentId"), D("RegistryId"),
                                 LogAnalyzer.Dfir.Devices.VolumeBootRecord.FromEvent(D).Select(v => v.Serial).ToList()));
                }
        }
        catch (Exception ex) when (ex is System.Diagnostics.Eventing.Reader.EventLogException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            problems.Add("Jurnalul conectărilor de discuri (Partition/Diagnostic) nu a putut fi citit: " + ex.Message);
        }
        return list;
    }

    /// <summary>
    /// Every logged connection goes to its device (by serial); a medium seen only in the log (Windows removed its device record) is still listed,
    /// with the log as its source. BusType 7 = USB, 12 = SD, 13 = MMC; internal buses are not added from the log.
    /// </summary>
    internal static List<DeviceRecord> AttachHistory(List<DeviceRecord> devices, IReadOnlyList<PartitionEvent> events)
    {
        var bySerial = events.Where(e => e.Serial.Length > 0).GroupBy(e => e.Serial, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.TimeUtc).ToList(), StringComparer.OrdinalIgnoreCase);
        var result = devices.Select(d => d.Serial.Length > 0 && bySerial.TryGetValue(d.Serial, out var ev) ? d with
        {
            Connections = ev.Select(e => e.TimeUtc).ToList(),
            CapacityBytes = d.CapacityBytes ?? ev[0].Capacity,
            VolumeSerials = VolumeSerialsOf(ev),
            Sources = d.Sources + ", jurnal Partition/Diagnostic 1006",
        } : d).ToList();
        foreach (var (serial, ev) in bySerial)
        {
            if (result.Any(d => d.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase))) continue;
            var kind = ev[0].BusType switch { "7" => DeviceKind.UsbStorage, "12" or "13" => DeviceKind.MemoryCard, _ => (DeviceKind?)null };
            if (kind is null) continue;
            result.Add(new DeviceRecord
            {
                Kind = kind.Value, InstanceId = ev[0].ParentId.Length > 0 ? ev[0].ParentId : @"LOG\" + serial, Name = $"{ev[0].Manufacturer} {ev[0].Model}".Trim(),
                Manufacturer = ev[0].Manufacturer, Model = ev[0].Model, Serial = serial, VidPid = DeviceRules.VidPid(ev[0].ParentId), Bus = "USB",
                CapacityBytes = ev[0].Capacity, VolumeSerials = VolumeSerialsOf(ev), FirstConnectedUtc = ev[^1].TimeUtc, LastConnectedUtc = ev[0].TimeUtc, Connections = ev.Select(e => e.TimeUtc).ToList(),
                Sources = "jurnal Partition/Diagnostic 1006 (Windows nu mai are înregistrarea dispozitivului)",
            });
        }
        return result;
    }

    private static List<string> VolumeSerialsOf(IEnumerable<PartitionEvent> ev) =>
        ev.SelectMany(e => e.VolumeSerials ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// HKLM\SYSTEM\MountedDevices: for each volume GUID and drive letter, the device it last belonged to (for MBR removable media the value is
    /// the device path, e.g. <c>_??_USBSTOR#Disk&amp;Ven_X#SERIAL&amp;0#{…}</c>). Gives the medium's label and its last drive letter by serial.
    /// </summary>
    internal static Dictionary<string, string> MountedDevices()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // "\??\Volume{guid}" or "\DosDevices\E:" -> serial
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices");
            foreach (var n in k?.GetValueNames() ?? [])
                if (k!.GetValue(n) is byte[] b && b.Length > 8 && b[1] == 0 && b[0] == (byte)'_')
                    if (LogAnalyzer.Dfir.Windows.Registers.StationDiscovery.SerialFromDeviceKey(System.Text.Encoding.Unicode.GetString(b)) is { } serial) map[n] = serial;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        return map;
    }

    internal static List<DeviceRecord> AttachLabels(List<DeviceRecord> devices, IReadOnlyDictionary<string, string> wpdVolumes, IReadOnlyDictionary<string, string> mounted)
    {
        var labelBySerial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (guid, label) in wpdVolumes)
            if (mounted.TryGetValue(@"\??\Volume" + guid, out var serial)) labelBySerial[serial] = label;
        var letterBySerial = mounted.Where(kv => kv.Key.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase))
                                    .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => string.Join(" ", g.Select(x => x.Key[12..])), StringComparer.OrdinalIgnoreCase);
        return devices.Select(d => d.Serial.Length == 0 ? d : d with
        {
            VolumeLabel = d.VolumeLabel.Length > 0 ? d.VolumeLabel : labelBySerial.GetValueOrDefault(d.Serial, ""),
            DriveLetters = d.DriveLetters.Length > 0 ? d.DriveLetters : letterBySerial.TryGetValue(d.Serial, out var l) ? l + " (ultima literă)" : "",
        }).ToList();
    }

    private static string BluetoothAddress(string id)
    {
        var m = System.Text.RegularExpressions.Regex.Match(id, @"DEV_([0-9A-F]{12})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? string.Join(":", Enumerable.Range(0, 6).Select(i => m.Groups[1].Value.Substring(i * 2, 2))).ToUpperInvariant() : "";
    }

    private static byte[]? Raw(IntPtr set, ref SP_DEVINFO_DATA d, Guid g, uint pid, out uint type)
    {
        var key = new DEVPROPKEY { fmtid = g, pid = pid };
        SetupDiGetDevicePropertyW(set, ref d, ref key, out type, null, 0, out var size, 0);
        if (size == 0) return null;
        var buf = new byte[size];
        return SetupDiGetDevicePropertyW(set, ref d, ref key, out type, buf, size, out _, 0) ? buf : null;
    }

    private static string Str(IntPtr set, ref SP_DEVINFO_DATA d, Guid g, uint pid) =>
        Raw(set, ref d, g, pid, out var t) is { } b && t == 0x12 ? System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0') : "";

    private static string[] StrList(IntPtr set, ref SP_DEVINFO_DATA d, Guid g, uint pid) =>
        Raw(set, ref d, g, pid, out var t) is { } b && t == 0x2012 ? System.Text.Encoding.Unicode.GetString(b).Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];

    private static DateTimeOffset? Time(IntPtr set, ref SP_DEVINFO_DATA d, Guid g, uint pid) =>
        Raw(set, ref d, g, pid, out var t) is { Length: 8 } b && t == 0x10 && BitConverter.ToInt64(b, 0) is > 0 and var ft ? new DateTimeOffset(DateTime.FromFileTimeUtc(ft)) : null;

    private static bool Bool(IntPtr set, ref SP_DEVINFO_DATA d, Guid g, uint pid) =>
        Raw(set, ref d, g, pid, out var t) is { Length: >= 1 } b && t == 0x11 && b[0] != 0;

    // ---- WMI: what is connected now ----------------------------------------------------------------------------------------------------

    private sealed record PresentInfo(string PnpId, string Model, string Serial, long? Size, string Interface, List<string> Letters, List<string> Labels, List<string> FileSystems);

    private static IEnumerable<PresentInfo> PresentDisks()
    {
        // Joined in code from the two relationship classes: ASSOCIATORS OF queries break on device ids that need escaping.
        static string Key(object? path) => System.Text.RegularExpressions.Regex.Match(path?.ToString() ?? "", "DeviceID=\"(.*)\"").Groups[1].Value.Replace(@"\\", @"\");
        var partsByDisk = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var q = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDriveToDiskPartition"))
            foreach (ManagementObject r in q.Get())
            {
                var disk = Key(r["Antecedent"]);
                if (!partsByDisk.TryGetValue(disk, out var l)) partsByDisk[disk] = l = [];
                l.Add(Key(r["Dependent"]));
            }
        var letterByPart = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var q = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDiskToPartition"))
            foreach (ManagementObject r in q.Get())
            {
                var part = Key(r["Antecedent"]);
                if (!letterByPart.TryGetValue(part, out var l)) letterByPart[part] = l = [];
                l.Add(Key(r["Dependent"]));
            }
        var logical = new Dictionary<string, (string Label, string Fs)>(StringComparer.OrdinalIgnoreCase);
        using (var q = new ManagementObjectSearcher("SELECT DeviceID, VolumeName, FileSystem FROM Win32_LogicalDisk"))
            foreach (ManagementObject l in q.Get()) logical[l["DeviceID"]?.ToString() ?? ""] = (l["VolumeName"]?.ToString() ?? "", l["FileSystem"]?.ToString() ?? "");

        var result = new List<PresentInfo>();
        using var disks = new ManagementObjectSearcher("SELECT DeviceID, PNPDeviceID, Model, SerialNumber, Size, InterfaceType FROM Win32_DiskDrive");
        foreach (ManagementObject d in disks.Get())
        {
            var letters = partsByDisk.GetValueOrDefault(d["DeviceID"]?.ToString() ?? "", []).SelectMany(p => letterByPart.GetValueOrDefault(p, [])).Distinct().ToList();
            result.Add(new(d["PNPDeviceID"]?.ToString() ?? "", d["Model"]?.ToString()?.Trim() ?? "", d["SerialNumber"]?.ToString()?.Trim() ?? "",
                           d["Size"] is ulong s ? (long)s : null, d["InterfaceType"]?.ToString() ?? "", letters,
                           letters.Select(l => logical.GetValueOrDefault(l).Label ?? "").ToList(), letters.Select(l => logical.GetValueOrDefault(l).Fs ?? "").ToList()));
        }
        return result;
    }

    private static IEnumerable<PresentInfo> PresentOptical()
    {
        using var cds = new ManagementObjectSearcher("SELECT PNPDeviceID, Name, Drive, MediaLoaded, VolumeName FROM Win32_CDROMDrive");
        foreach (ManagementObject c in cds.Get())
        {
            var loaded = c["MediaLoaded"] is true;
            yield return new(c["PNPDeviceID"]?.ToString() ?? "", c["Name"]?.ToString() ?? "", "", null, loaded ? "disc introdus" : "fără disc",
                             [c["Drive"]?.ToString() ?? ""], [loaded ? c["VolumeName"]?.ToString() ?? "" : ""], []);
        }
    }

    private static Dictionary<string, string> BitLockerByLetter(List<string> problems)
    {
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var s = new ManagementObjectSearcher(@"root\cimv2\Security\MicrosoftVolumeEncryption", "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume");
            foreach (ManagementObject v in s.Get())
                if (v["DriveLetter"]?.ToString() is { Length: > 0 } l)
                    r[l] = v["ProtectionStatus"]?.ToString() switch { "1" => "criptat BitLocker", "0" => "necriptat", _ => "stare BitLocker necunoscută" };
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            problems.Add("Starea BitLocker a unităților nu a putut fi citită (necesită administrator).");
        }
        return r;
    }

    // ---- who used each medium: MountPoints2 of every loaded profile against MountedDevices, arrivals from 1006 -----------------------

    /// <summary>
    /// Attributes each removable medium's connections (live station) with <see cref="MediaUsageRules"/>. Logon sessions are not read here,
    /// so a medium with no profile that mounted it is "not attributable" in the live view; the case analysis reads the logon log.
    /// </summary>
    internal static List<DeviceRecord> AttachUsage(List<DeviceRecord> devices, List<string> problems)
    {
        if (!devices.Any(d => d.Serial.Length > 0 && d.Connections.Count > 0 && DeviceRules.TrackedByRegister(d.Kind))) return devices;
        var volumes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, serial) in MountedDevices())
            if (name.StartsWith(@"\??\Volume{", StringComparison.OrdinalIgnoreCase)) volumes[name[4..].ToUpperInvariant()] = LogAnalyzer.Dfir.Registers.MediaRegister.NormalizeSerial(serial);
        var mounts = LiveMounts(problems);
        return devices.Select(d => d.Serial.Length == 0 || d.Connections.Count == 0 || !DeviceRules.TrackedByRegister(d.Kind) ? d : d with
        {
            Usage = MediaUsageRules.Attribute(d.Serial, d.Connections.OrderBy(t => t).Select(t => new MediumConnection(t, null)).ToList(), volumes, mounts, []),
        }).ToList();
    }

    /// <summary>MountPoints2 volume GUIDs of every loaded profile, each with the key's last write time (RegQueryInfoKey): the profile's last use of that volume. Empty when Windows refuses the query.</summary>
    private static List<VolumeMount> LiveMounts(List<string> problems)
    {
        var list = new List<VolumeMount>();
        try
        {
            foreach (var sid in Registry.Users.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-") && !s.EndsWith("_Classes")))
            {
                using var mp = Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2");
                if (mp is null) continue;
                var account = AccountName(sid);
                foreach (var guid in mp.GetSubKeyNames().Where(n => n.StartsWith('{') && n.EndsWith('}')))
                {
                    using var sub = mp.OpenSubKey(guid);
                    list.Add(new VolumeMount(account, guid.ToUpperInvariant(), sub is null ? null : KeyLastWrite(sub)));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            problems.Add("Registrul profilurilor (MountPoints2) nu a putut fi citit: " + ex.Message);
        }
        return list;
    }

    private static DateTimeOffset? KeyLastWrite(RegistryKey key) =>
        RegQueryInfoKey(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var ft) == 0 && ft > 0
            ? new DateTimeOffset(DateTime.FromFileTimeUtc(ft)) : null;

    private static string AccountName(string sid)
    {
        try { return new System.Security.Principal.SecurityIdentifier(sid).Translate(typeof(System.Security.Principal.NTAccount)).Value; }
        catch (Exception) { return sid; }   // the SID stays visible when it cannot be translated
    }

    // ---- network drives: mapped now (Network\<letter>) and used before (MountPoints2 ##server#share) per loaded user --------------------

    private static IEnumerable<DeviceRecord> NetworkDrives(List<string> problems)
    {
        var list = new List<DeviceRecord>();
        try
        {
            foreach (var sid in Registry.Users.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-") && !s.EndsWith("_Classes")))
            {
                using (var net = Registry.Users.OpenSubKey(sid + @"\Network"))
                    foreach (var letter in net?.GetSubKeyNames() ?? [])
                    {
                        using var k = net!.OpenSubKey(letter);
                        var remote = k?.GetValue("RemotePath")?.ToString() ?? "";
                        list.Add(new DeviceRecord { Kind = DeviceKind.NetworkDrive, InstanceId = $"NET\\{sid}\\{letter}", Name = remote, DriveLetters = letter.ToUpperInvariant() + ":",
                                                    Serial = "", Bus = "rețea", ConnectedNow = Directory.Exists(letter + ":\\"), Sources = $"HKU\\{sid}\\Network (unitate mapată)" });
                    }
                using (var mp = Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2"))
                    foreach (var share in (mp?.GetSubKeyNames() ?? []).Where(n => n.StartsWith("##")))
                    {
                        var path = "\\\\" + share[2..].Replace('#', '\\');
                        if (list.Any(d => d.Name.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
                        list.Add(new DeviceRecord { Kind = DeviceKind.NetworkDrive, InstanceId = $"MP2\\{sid}\\{share}", Name = path, Bus = "rețea",
                                                    Sources = $"HKU\\{sid}\\…\\MountPoints2 (folder din rețea folosit de utilizator)" });
                    }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            problems.Add("Unitățile de rețea ale utilizatorilor nu au putut fi citite: " + ex.Message);
        }
        return list;
    }

    // ---- P/Invoke --------------------------------------------------------------------------------------------------------------------

    private const uint DIGCF_ALLCLASSES = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("advapi32.dll")]
    private static extern int RegQueryInfoKey(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpReserved, IntPtr lpcSubKeys, IntPtr lpcbMaxSubKeyLen,
        IntPtr lpcbMaxClassLen, IntPtr lpcValues, IntPtr lpcbMaxValueNameLen, IntPtr lpcbMaxValueLen, IntPtr lpcbSecurityDescriptor, out long lpftLastWriteTime);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[]? buffer, uint size, out uint required, uint flags);
}
