using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using LogAnalyzer.Dfir.Devices;

namespace LogAnalyzer.Dfir.Windows.Devices;

/// <summary>
/// What is in a drive, for the real-time device monitor: the capacity of the disk, its drive letters and, for the first lettered volume, the label and the volume
/// serial (GetVolumeInformation). A card is told apart from another by volume serial and capacity, so this is read when a medium appears. Read-only; a source that
/// cannot be read gives no entry (the monitor then keeps the insertion as a row of its own rather than merging cards).
/// </summary>
public static class MediumReader
{
    private static string Key(object? path) =>
        System.Text.RegularExpressions.Regex.Match(path?.ToString() ?? "", "DeviceID=\"(.*)\"").Groups[1].Value.Replace(@"\\", @"\");

    /// <summary>The medium of each drive (PNP device id of the disk) in <paramref name="driveInstanceIds"/>.</summary>
    public static IReadOnlyDictionary<string, MediumInfo> For(IReadOnlyCollection<string> driveInstanceIds)
    {
        var result = new Dictionary<string, MediumInfo>(StringComparer.OrdinalIgnoreCase);
        if (driveInstanceIds.Count == 0) return result;
        try
        {
            var wanted = new HashSet<string>(driveInstanceIds, StringComparer.OrdinalIgnoreCase);
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
            using var disks = new ManagementObjectSearcher("SELECT DeviceID, PNPDeviceID, Size FROM Win32_DiskDrive");
            foreach (ManagementObject d in disks.Get())
            {
                var pnp = d["PNPDeviceID"]?.ToString() ?? "";
                if (!wanted.Contains(pnp)) continue;
                var letters = partsByDisk.GetValueOrDefault(d["DeviceID"]?.ToString() ?? "", []).SelectMany(p => letterByPart.GetValueOrDefault(p, [])).Distinct().ToList();
                string label = "", serial = "";
                foreach (var letter in letters)
                    if (Volume(letter + @"\") is { } v) { label = v.Label; serial = v.Serial; break; }
                result[pnp] = new MediumInfo(serial, d["Size"] is ulong s ? (long)s : 0, string.Join(" ", letters), label);
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidCastException) { }
        return result;
    }

    private static (string Label, string Serial)? Volume(string root)
    {
        var label = new StringBuilder(261);
        return GetVolumeInformationW(root, label, label.Capacity, out var serial, out _, out _, null, 0) ? (label.ToString(), serial.ToString("X8")) : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? label, int labelSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder? fsName, int fsNameSize);
}

/// <summary>
/// The account of the active console session (WTS), for „utilizatorul conectat la momentul conectării”. Empty when nobody is logged on at the console (or the
/// session cannot be read): the monitor then shows „—”.
/// </summary>
public static class ConsoleUser
{
    private const uint NoSession = 0xFFFFFFFF;
    private const int WTSUserName = 5, WTSDomainName = 7;

    public static string Current()
    {
        try
        {
            var session = WTSGetActiveConsoleSessionId();
            if (session == NoSession) return "";
            var user = Query(session, WTSUserName);
            if (user.Length == 0) return "";
            var domain = Query(session, WTSDomainName);
            return domain.Length > 0 ? domain + @"\" + user : user;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ""; }
    }

    private static string Query(uint session, int infoClass)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, session, infoClass, out var buf, out _)) return "";
        try { return Marshal.PtrToStringUni(buf) ?? ""; }
        finally { WTSFreeMemory(buf); }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, uint session, int infoClass, out IntPtr buffer, out uint bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
