using System.Runtime.InteropServices;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The station facts the classified-access analysis needs besides the event log: how Windows names the volumes in its records, the files present in the
/// classified folders (names and SHA-256), and the shortcuts of every profile's Recent folder that point into a classified folder or to a removable drive.
/// Read only: nothing on the station is changed (no audit setting, no file).
/// </summary>
public static class ClassifiedFactCollector
{
    public const int MaxFiles = 2000, MaxHashed = 500, MaxLinks = 5000;
    public const long MaxHashBytes = 100L * 1024 * 1024;

    /// <summary>"\Device\HarddiskVolume3" → "C:" (QueryDosDevice, mounted volumes) and "Volume{GUID}" → "C:" (MountedDevices: same data as the letter's value).</summary>
    public static Dictionary<string, string> VolumeMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var buf = new StringBuilder(1024);
        for (char c = 'A'; c <= 'Z'; c++)
        {
            buf.Clear();
            if (QueryDosDevice($"{c}:", buf, buf.Capacity) > 0 && buf.ToString() is { Length: > 0 } dev && dev.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
                map.TryAdd(dev, $"{c}:");
        }
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices");
        if (k is null) return map;
        var letters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in k.GetValueNames().Where(n => n.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase)))
            if (k.GetValue(n) is byte[] b) letters.TryAdd(Convert.ToHexString(b), n[12..]);
        foreach (var n in k.GetValueNames().Where(n => n.StartsWith(@"\??\Volume{", StringComparison.OrdinalIgnoreCase)))
            if (k.GetValue(n) is byte[] b && letters.TryGetValue(Convert.ToHexString(b), out var letter)) map.TryAdd(n[4..], letter);
        return map;
    }

    /// <summary>Files of the classified folders (capped) and the SHA-256 of the first <see cref="MaxHashed"/> under <see cref="MaxHashBytes"/>.</summary>
    public static void ReadFiles(StationFacts f, IEnumerable<string> folders)
    {
        var files = new List<string>();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var root in folders.Select(x => x.Trim()).Where(x => x.Length > 0 && Directory.Exists(x)))
            foreach (var file in Directory.EnumerateFiles(root, "*", opts))
            {
                if (files.Count >= MaxFiles) break;
                files.Add(file);
                if (hashes.Count >= MaxHashed) continue;
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length > MaxHashBytes) continue;
                    hashes[ClassifiedAccess.Normalize(file)] = LogAnalyzer.Dfir.IO.Hashing.Sha256File(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        if (files.Count >= MaxFiles)
            f.Gaps.Add(new LogAnalyzer.Dfir.Model.EvidenceGap("Fișierele din folderele clasificate", LogAnalyzer.Dfir.Model.EvidenceStatus.Partial, $"limitat la {MaxFiles} fișiere",
                "Copiile fișierelor care nu au fost listate se recunosc doar dacă accesul lor a fost auditat", "Listarea completă a folderelor", "Da"));
        f.ClassifiedFiles = files;
        f.ClassifiedHashes = hashes;
    }

    /// <summary>
    /// Shortcuts in C:\Users\*\AppData\Roaming\Microsoft\Windows\Recent of every profile (administrator rights needed for the other profiles), kept only when the
    /// target is in a classified folder or on a removable drive. A shortcut shows that a file was opened, not who opened it nor what was done with it.
    /// </summary>
    public static List<ShellTraceFact> ShellTraces(IReadOnlyList<string> folders)
    {
        var roots = folders.Select(x => ClassifiedAccess.Normalize(x)).Where(x => x.Length > 0).ToList();
        var r = new List<ShellTraceFact>();
        var users = Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\", "Users");
        if (!Directory.Exists(users)) return r;
        int seen = 0;
        foreach (var profile in Directory.EnumerateDirectories(users))
        {
            var recent = Path.Combine(profile, @"AppData\Roaming\Microsoft\Windows\Recent");
            IEnumerable<string> links;
            try { if (!Directory.Exists(recent)) continue; links = Directory.EnumerateFiles(recent, "*.lnk").ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var lnk in links)
            {
                if (++seen > MaxLinks) return r;
                try
                {
                    var fi = new FileInfo(lnk);
                    if (fi.Length > 64 * 1024) continue;
                    var link = LogAnalyzer.Dfir.FileSystem.LnkParser.Decode(File.ReadAllBytes(lnk));
                    var target = ClassifiedAccess.Normalize(link.Target);
                    bool keep = link.DriveType.Equals("REMOVABLE", StringComparison.OrdinalIgnoreCase) || roots.Any(x => ClassifiedAccess.IsUnder(target, x));
                    if (keep) r.Add(new ShellTraceFact(Path.GetFileName(profile), lnk, target, link.DriveType, link.Serial, new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { }
            }
        }
        return r;
    }

    /// <summary>The 8.3 short form of an existing path ("" when the volume has no short names): Windows may log the file under it.</summary>
    public static string ShortPath(string path)
    {
        var sb = new StringBuilder(1024);
        var n = GetShortPathName(path, sb, sb.Capacity);
        var s = n > 0 && n < sb.Capacity ? sb.ToString() : "";
        return s.Equals(path, StringComparison.OrdinalIgnoreCase) ? "" : s;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);
}
