using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Turns the path forms artifacts write into a path on this station, or null when it cannot be done with certainty
/// (evaluation 2026-11-05: SRUM writes \device\harddiskvolume3\…, Prefetch \VOLUME{01d9a0b1c2d3e4f5-1a2b3c4d}\…, some rows \users\…):
/// a device path through the drive letters' DOS device names, a Prefetch volume through the volume serial number (its last 8 hex digits),
/// a rooted path without drive on the system drive. Used only on a live collection, to check a file that is still on this station.
/// </summary>
public sealed partial class ArtifactPathResolver
{
    private readonly IReadOnlyDictionary<string, string> _deviceToDrive;   // "\device\harddiskvolume3" -> "C:"
    private readonly IReadOnlyDictionary<uint, string> _serialToDrive;     // 0x1A2B3C4D -> "C:"
    private readonly string _systemDrive;

    public ArtifactPathResolver(IReadOnlyDictionary<string, string> deviceToDrive, IReadOnlyDictionary<uint, string> serialToDrive, string systemDrive)
    {
        _deviceToDrive = new Dictionary<string, string>(deviceToDrive, StringComparer.OrdinalIgnoreCase);
        _serialToDrive = serialToDrive;
        _systemDrive = systemDrive.TrimEnd('\\');
    }

    public string? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Replace('/', '\\');
        if (SignatureCalibration.IsLocalPath(path)) return path;
        if (path.StartsWith(@"\device\", StringComparison.OrdinalIgnoreCase))
        {
            var cut = path.IndexOf('\\', @"\device\".Length);
            if (cut < 0) return null;
            return _deviceToDrive.TryGetValue(path[..cut], out var drive) ? drive + path[cut..] : null;
        }
        if (VolumePath().Match(path) is { Success: true } m)
            return uint.TryParse(m.Groups["serial"].Value, System.Globalization.NumberStyles.HexNumber, null, out var serial) && _serialToDrive.TryGetValue(serial, out var d)
                ? d + path[m.Length..] : null;
        if (path.StartsWith('\\') && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\?", StringComparison.Ordinal)) return _systemDrive + path;
        return null;
    }

    [GeneratedRegex(@"^\\VOLUME\{[0-9a-f]+-(?<serial>[0-9a-f]{8})\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex VolumePath();

    /// <summary>The resolver for this station: fixed drives with their DOS device name and volume serial number.</summary>
    public static ArtifactPathResolver ForThisStation()
    {
        var devices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var serials = new Dictionary<uint, string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType != DriveType.Fixed) continue;
            var letter = d.Name.TrimEnd('\\');
            var sb = new StringBuilder(512);
            if (QueryDosDevice(letter, sb, sb.Capacity) > 0) devices[sb.ToString()] = letter;
            if (GetVolumeInformation(d.Name, null, 0, out var serial, out _, out _, null, 0)) serials[serial] = letter;
        }
        return new ArtifactPathResolver(devices, serials, Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int max);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? name, int nameSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder? fsName, int fsNameSize);
}
