using System.Security.Principal;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Registers;

/// <summary>
/// Read-only discovery of what exists on this station, to pre-fill the users and media registers. Reads the registry, WMI and the mounted volumes;
/// never writes to the host. The parsing and merging parts are pure static functions that take plain strings, so they can be tested without a registry.
/// </summary>
public static class StationDiscovery
{
    // ---- pure helpers ------------------------------------------------------------------------------------------

    /// <summary>
    /// Serial from a WPD / device instance key such as
    /// <c>SWD#WPDBUSENUM#_??_USBSTOR#DISK&amp;VEN_SANDISK&amp;PROD_CRUZER&amp;REV_1.00#4C530001234567&amp;0#{guid}</c> -> <c>4C530001234567</c>
    /// (the segment before the interface GUID, without the "&amp;0" instance suffix). Null when the key does not carry one.
    /// </summary>
    public static string? SerialFromDeviceKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase)) return null;
        var seg = key.Split('#', StringSplitOptions.TrimEntries);
        int g = Array.FindIndex(seg, s => s.StartsWith('{'));
        var serial = g >= 1 ? seg[g - 1] : seg.Length >= 2 ? seg[^1] : "";
        serial = MediaRegister.NormalizeSerial(serial);
        return serial.Length == 0 || serial.StartsWith("DISK&", StringComparison.OrdinalIgnoreCase) ? null : serial;
    }

    /// <summary>"VID_0781&amp;PID_5567" -> "0781:5567"; null when it is not of that form.</summary>
    public static string? VidPidFromKeyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var m = Regex.Match(name, @"VID_(?<v>[0-9A-Fa-f]{4})&PID_(?<p>[0-9A-Fa-f]{4})", RegexOptions.CultureInvariant);
        return m.Success ? (m.Groups["v"].Value + ":" + m.Groups["p"].Value).ToLowerInvariant() : null;
    }

    /// <summary>
    /// Merges the sources by normalised serial: USBSTOR devices + VID/PID by serial + volume label by serial + currently mounted removable volumes.
    /// A mounted volume without a known serial is added only if no USB device already carries the same label.
    /// </summary>
    public static List<ObservedMedium> MergeMedia(
        IEnumerable<(string Serial, string Model)> usbStor,
        IReadOnlyDictionary<string, string> vidPidBySerial,
        IReadOnlyDictionary<string, string> labelBySerial,
        IEnumerable<(string Label, string Type)> mounted,
        DateTimeOffset now)
    {
        var result = new List<ObservedMedium>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static string Lookup(IReadOnlyDictionary<string, string> d, string key) =>
            d.FirstOrDefault(p => MediaRegister.NormalizeSerial(p.Key) == key).Value ?? "";
        foreach (var (rawSerial, model) in usbStor)
        {
            var serial = MediaRegister.NormalizeSerial(rawSerial);
            if (serial.Length == 0 || !seen.Add(serial)) continue;
            result.Add(new ObservedMedium(serial, Lookup(vidPidBySerial, serial), model ?? "", Lookup(labelBySerial, serial), "USB", "USBSTOR", null));
        }
        foreach (var (label, type) in mounted)
        {
            if (!string.IsNullOrWhiteSpace(label) && result.Any(r => r.VolumeLabel.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new ObservedMedium("", "", "", (label ?? "").Trim(), type, "Volum montat", now));
        }
        return result;
    }

    /// <summary>Profile accounts and local accounts, one entry per SID or name (local accounts win, they carry the last logon).</summary>
    public static List<ObservedAccount> MergeAccounts(IEnumerable<ObservedAccount> local, IEnumerable<ObservedAccount> profiles)
    {
        var result = new List<ObservedAccount>(local);
        foreach (var p in profiles)
            if (!result.Any(r => (p.Sid.Length > 0 && r.Sid.Equals(p.Sid, StringComparison.OrdinalIgnoreCase)) || r.Account.Equals(p.Account, StringComparison.OrdinalIgnoreCase)))
                result.Add(p);
        return result;
    }

    /// <summary>S-1-5-18 / 19 / 20 (system, local service, network service) are not people.</summary>
    public static bool IsServiceSid(string sid) => sid is "S-1-5-18" or "S-1-5-19" or "S-1-5-20";

    // ---- station reads (read-only) -----------------------------------------------------------------------------

    public static List<ObservedAccount> ObservedAccounts()
    {
        var local = new List<ObservedAccount>();
        try
        {
            foreach (var a in StationFactCollector.ReadAccounts())
                local.Add(new ObservedAccount(a.Name, a.Sid, "Conturi locale", a.LastLogonUtc));
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }

        var profiles = new List<ObservedAccount>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            foreach (var sid in root?.GetSubKeyNames() ?? [])
            {
                if (IsServiceSid(sid)) continue;
                using var k = root!.OpenSubKey(sid);
                var path = k?.GetValue("ProfileImagePath")?.ToString() ?? "";
                string account;
                try { account = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; }
                catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException or SystemException) { account = Path.GetFileName(path.TrimEnd('\\', '/')); }
                if (account.Length == 0) continue;
                profiles.Add(new ObservedAccount(account, sid, "Profil utilizator", LoadTime(k)));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        return MergeAccounts(local, profiles);
    }

    private static DateTimeOffset? LoadTime(RegistryKey? k)
    {
        if (k?.GetValue("LocalProfileLoadTimeHigh") is int hi && k.GetValue("LocalProfileLoadTimeLow") is int lo)
        {
            try { return DateTimeOffset.FromFileTime(((long)hi << 32) | (uint)lo).ToUniversalTime(); }
            catch (ArgumentOutOfRangeException) { }
        }
        return null;
    }

    public static List<ObservedMedium> ObservedMedia(DateTimeOffset? now = null)
    {
        var usb = new List<(string, string)>();
        var vidPid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var d in StationFactCollector.ReadUsbStor()) usb.Add((d.Serial, d.FriendlyName));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            foreach (var dev in root?.GetSubKeyNames() ?? [])
            {
                var vp = VidPidFromKeyName(dev);
                if (vp is null) continue;
                using var d = root!.OpenSubKey(dev);
                foreach (var serial in d?.GetSubKeyNames() ?? []) vidPid[MediaRegister.NormalizeSerial(serial)] = vp;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Portable Devices\Devices");
            foreach (var key in root?.GetSubKeyNames() ?? [])
            {
                var serial = SerialFromDeviceKey(key);
                if (serial is null) continue;
                using var k = root!.OpenSubKey(key);
                if (k?.GetValue("FriendlyName")?.ToString() is { Length: > 0 } label) labels[serial] = label;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }

        var mounted = new List<(string, string)>();
        foreach (var di in DriveInfo.GetDrives())
        {
            try
            {
                if (di.DriveType == DriveType.CDRom && di.IsReady) mounted.Add((di.VolumeLabel, "CD/DVD"));
                else if (di.DriveType == DriveType.Removable && di.IsReady) mounted.Add((di.VolumeLabel, "USB"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return MergeMedia(usb, vidPid, labels, mounted, now ?? DateTimeOffset.UtcNow);
    }
}
