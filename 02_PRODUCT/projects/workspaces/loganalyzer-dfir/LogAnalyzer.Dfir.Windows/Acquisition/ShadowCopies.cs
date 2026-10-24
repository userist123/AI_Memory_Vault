using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using EnumerationOptions = System.IO.EnumerationOptions;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>One Volume Shadow Copy: its id, the device object to read it through, the volume it copies and when it was made.</summary>
public sealed record ShadowCopyInfo(string Id, string DeviceObject, string VolumeName, DateTimeOffset? CreatedUtc)
{
    /// <summary>Root to read files from: "\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN\".</summary>
    public string Root => DeviceObject.TrimEnd('\\') + "\\";
    public string Label => $"{System.IO.Path.GetFileName(DeviceObject.TrimEnd('\\'))} · creată {(CreatedUtc is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "la o dată necunoscută")} · {Id}";
}

public enum ShadowListStatus { Listed, None, NotAdministrator, Failed }

public sealed record ShadowListResult(ShadowListStatus Status, IReadOnlyList<ShadowCopyInfo> Items, string Message);

/// <summary>
/// E10: Volume Shadow Copies as an analysis target. Lists the copies through WMI Win32_ShadowCopy (read only: nothing is created, mounted or deleted)
/// and enumerates, inside a chosen copy, the files the investigation can import (event logs, configuration hives, Prefetch, user hives). The paths are
/// "\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN\..."; importing them records that origin in the evidence notes and in custody. Administrator only:
/// without it the list is "Nedeterminat", never "nicio copie".
/// </summary>
public static class ShadowCopies
{
    private static readonly Regex DeviceRx = new(@"^\\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy\d+\\?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public static bool IsShadowDevice(string deviceObject) => DeviceRx.IsMatch(deviceObject);

    /// <summary>Lists the shadow copies. <paramref name="isAdministrator"/> and <paramref name="query"/> can be supplied (tests); by default WMI is asked.</summary>
    public static ShadowListResult List(bool? isAdministrator = null, Func<IEnumerable<IReadOnlyDictionary<string, object?>>>? query = null)
    {
        if (!(isAdministrator ?? IsAdministrator()))
            return new(ShadowListStatus.NotAdministrator, [], "Nedeterminat: copiile de umbră se pot lista doar cu drepturi de administrator. Reporniți aplicația ca administrator.");
        try
        {
            var rows = (query ?? QueryWmi)();
            var items = rows.Select(r => new ShadowCopyInfo(S(r, "ID"), S(r, "DeviceObject"), S(r, "VolumeName"), Cim(S(r, "InstallDate"))))
                            .Where(i => IsShadowDevice(i.DeviceObject))
                            .OrderByDescending(i => i.CreatedUtc).ToList();
            return items.Count == 0
                ? new(ShadowListStatus.None, items, "Nu există copii de umbră pe această stație (Win32_ShadowCopy nu întoarce nicio copie).")
                : new(ShadowListStatus.Listed, items, $"{items.Count} copii de umbră. Alegeți una ca țintă a analizei: jurnalele și hive-urile ei se importă ca probe, cu originea trecută în custodie.");
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException)
        {
            return new(ShadowListStatus.Failed, [], "Nedeterminat: copiile de umbră nu au putut fi citite prin WMI (" + ex.Message + ").");
        }
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> QueryWmi()
    {
        using var s = new ManagementObjectSearcher(@"root\cimv2", "SELECT ID, DeviceObject, VolumeName, InstallDate FROM Win32_ShadowCopy");
        var list = new List<IReadOnlyDictionary<string, object?>>();
        foreach (ManagementObject o in s.Get())
            using (o)
                list.Add(new Dictionary<string, object?> { ["ID"] = o["ID"], ["DeviceObject"] = o["DeviceObject"], ["VolumeName"] = o["VolumeName"], ["InstallDate"] = o["InstallDate"] });
        return list;
    }

    private static string S(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";

    /// <summary>CIM datetime "yyyyMMddHHmmss.ffffff+UUU" (UUU = minutes from UTC) to UTC.</summary>
    public static DateTimeOffset? Cim(string s)
    {
        if (s.Length < 14 || !DateTime.TryParseExact(s[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
        var offset = s.Length >= 25 && int.TryParse(s[21..25], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var min) ? TimeSpan.FromMinutes(min) : TimeSpan.Zero;
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>Relative locations, inside a copy, of what the investigation imports.</summary>
    public static readonly string[] ArtifactFolders = [@"Windows\System32\winevt\Logs", @"Windows\System32\config", @"Windows\Prefetch", @"Windows\INF", "Users"];

    /// <summary>The files of a chosen copy that the investigation can import (event logs, SYSTEM / SOFTWARE, Prefetch, setupapi.dev.log, NTUSER.DAT).</summary>
    public static List<string> ArtifactPaths(ShadowCopyInfo copy, int max = 5000)
    {
        var list = new List<string>();
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        void Add(string dir, string pattern, bool recurse = false)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.EnumerateFiles(dir, pattern, new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = recurse, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 2 }))
                    if (list.Count < max) list.Add(f);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var root = copy.Root;
        Add(System.IO.Path.Combine(root, @"Windows\System32\winevt\Logs"), "*.evtx");
        foreach (var hive in new[] { "SYSTEM", "SOFTWARE" })
        {
            var p = System.IO.Path.Combine(root, @"Windows\System32\config", hive);
            if (File.Exists(p)) list.Add(p);
        }
        Add(System.IO.Path.Combine(root, @"Windows\Prefetch"), "*.pf");
        var setup = System.IO.Path.Combine(root, @"Windows\INF\setupapi.dev.log");
        if (File.Exists(setup)) list.Add(setup);
        var users = System.IO.Path.Combine(root, "Users");
        try
        {
            if (Directory.Exists(users))
                foreach (var u in Directory.EnumerateDirectories(users, "*", opts))
                {
                    var nt = System.IO.Path.Combine(u, "NTUSER.DAT");
                    if (File.Exists(nt)) list.Add(nt);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (System.Security.SecurityException) { return false; }
    }
}
