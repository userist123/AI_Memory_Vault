using System.Runtime.InteropServices;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The station facts the classified-access analysis needs besides the event log: how Windows names the volumes in its records, the files present in the
/// classified folders (names and SHA-256), and the shortcuts of every profile's Recent folder that point into a classified folder or to a removable drive.
/// Nothing on the station is changed (no audit setting, no file content, no file time): the files are opened only to be hashed, with the update of their
/// last-access time switched off for that handle (SetFileTime with -1). Because the folders are audited, these reads are themselves recorded by Windows: the
/// reading window (start, end, process id, image path, account) is written to the sealed audit and later controls list those records apart, as the
/// application's own activity.
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
    /// <summary>
    /// Which 4656 / 4660 / 4663 records count against the separate classified budget when the Security log is read: an object under a classified folder (or one of
    /// its aliases), on a removable drive, on an unmapped volume ("\Device\HarddiskVolumeN", a medium no longer connected), or of the "Removable Storage"
    /// subcategory (task 12802); 4660 carries no object name and is always kept. Everything else stays under the general per-id cap.
    /// </summary>
    public static Func<int, IReadOnlyDictionary<string, string>, bool> ClassifiedEventFilter(IEnumerable<string> roots, IEnumerable<string> aliases, IReadOnlySet<string> removable,
        IReadOnlyDictionary<string, string>? volumeMap)
    {
        var all = roots.Concat(aliases).Select(r => ClassifiedAccess.Normalize(r, volumeMap)).Where(r => r.Length > 0).ToList();
        return (id, d) =>
        {
            if (id == 4660) return true;
            if (d.TryGetValue("_Task", out var task) && task == "12802") return true;
            var obj = d.TryGetValue("ObjectName", out var o) ? ClassifiedAccess.Normalize(o, volumeMap) : "";
            if (obj.Length == 0) return false;
            if (obj.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return true;
            if (ClassifiedAccess.Drive(obj) is { Length: > 0 } drive && removable.Contains(drive)) return true;
            return all.Any(r => ClassifiedAccess.IsUnder(obj, r));
        };
    }

    /// <summary>
    /// Files of the classified folders (capped) and the SHA-256 of the first <paramref name="maxHashed"/> files up to <paramref name="maxHashBytes"/> bytes. Called
    /// AFTER the event logs are read, so this control's own reads are not in its own evidence. The last-access time of each file is kept (SetFileTime -1 on the
    /// handle); the reading window goes to <see cref="StationFacts.SelfWindow"/>. Files without a fingerprint (too large, over the limit, unreadable) are a gap.
    /// </summary>
    public static void ReadFiles(StationFacts f, IEnumerable<string> folders, int maxHashed = MaxHashed, long maxHashBytes = MaxHashBytes)
    {
        var files = new List<string>();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int tooBig = 0, overLimit = 0, unreadable = 0, timeNotKept = 0;
        var start = DateTimeOffset.UtcNow;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var root in folders.Select(x => x.Trim()).Where(x => x.Length > 0 && Directory.Exists(x)))
            foreach (var file in Directory.EnumerateFiles(root, "*", opts))
            {
                if (files.Count >= MaxFiles) break;
                files.Add(file);
                try
                {
                    if (new FileInfo(file).Length > maxHashBytes) { tooBig++; continue; }
                    if (hashes.Count >= maxHashed) { overLimit++; continue; }
                    var (sha, kept) = HashKeepingAccessTime(file);
                    hashes[ClassifiedAccess.Normalize(file)] = sha;
                    if (!kept) timeNotKept++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; }
            }
        using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
            f.SelfWindow = new SelfActivityWindow(start, DateTimeOffset.UtcNow, Environment.ProcessId, Environment.ProcessPath ?? "", id.Name, hashes.Count, AppSha256: AppSha256());
        if (files.Count >= MaxFiles)
            f.Gaps.Add(new LogAnalyzer.Dfir.Model.EvidenceGap("Fișierele din folderele clasificate", LogAnalyzer.Dfir.Model.EvidenceStatus.Partial, $"limitat la {MaxFiles} fișiere",
                "Copiile fișierelor care nu au fost listate se recunosc doar dacă accesul lor a fost auditat", "Listarea completă a folderelor", "Da"));
        var without = tooBig + overLimit + unreadable;
        if (without > 0)
        {
            var why = new List<string>();
            if (tooBig > 0) why.Add($"{tooBig} peste {maxHashBytes} octeți");
            if (overLimit > 0) why.Add($"{overLimit} peste limita de {maxHashed} fișiere");
            if (unreadable > 0) why.Add($"{unreadable} care nu au putut fi citite");
            f.Gaps.Add(new LogAnalyzer.Dfir.Model.EvidenceGap("Amprentele fișierelor clasificate", LogAnalyzer.Dfir.Model.EvidenceStatus.Partial,
                $"fișiere fără amprentă: {without} ({string.Join(", ", why)})",
                "Pentru aceste fișiere o ieșire se poate potrivi cu registrul transferurilor doar după nume, nu după amprenta SHA-256", "Calculul amprentelor la stație", "Da"));
        }
        if (timeNotKept > 0)
            f.Gaps.Add(new LogAnalyzer.Dfir.Model.EvidenceGap("Ora ultimului acces la fișierele clasificate", LogAnalyzer.Dfir.Model.EvidenceStatus.Partial,
                $"pentru {timeNotKept} fișiere ora ultimului acces nu a putut fi păstrată (fișierul nu a putut fi deschis cu dreptul de a-i păstra atributele)",
                "Ora ultimului acces a acestor fișiere poate arăta momentul controlului", "-", "Nu"));
        f.ClassifiedFiles = files;
        f.ClassifiedHashes = hashes;
    }

    /// <summary>
    /// SHA-256 of a file opened for reading with FILE_WRITE_ATTRIBUTES only so that SetFileTime(-1) can switch off the last-access update for this handle; when
    /// that open is refused the file is read normally and <c>kept</c> is false. Nothing is written to the file.
    /// </summary>
    /// <summary>SHA-256 of the running application's executable (the build fingerprint written with each hashing window); "" when it cannot be read.</summary>
    public static string AppSha256()
    {
        try { return Environment.ProcessPath is { } p && File.Exists(p) ? LogAnalyzer.Dfir.IO.Hashing.Sha256File(p) : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    internal static (string Sha256, bool Kept) HashKeepingAccessTime(string path)
    {
        const uint GenericRead = 0x80000000, FileWriteAttributes = 0x100, OpenExisting = 3, ShareAll = 0x7, SequentialScan = 0x08000000;
        var h = CreateFileW(path, GenericRead | FileWriteAttributes, ShareAll, IntPtr.Zero, OpenExisting, SequentialScan, IntPtr.Zero);
        if (h.IsInvalid)
        {
            h.Dispose();
            return (LogAnalyzer.Dfir.IO.Hashing.Sha256File(path), false);
        }
        var noUpdate = new FILETIME { Low = 0xFFFFFFFF, High = 0xFFFFFFFF };
        bool kept = SetFileTime(h, IntPtr.Zero, ref noUpdate, IntPtr.Zero);
        using var fs = new FileStream(h, FileAccess.Read);
        return (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)).ToLowerInvariant(), kept);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileTime(Microsoft.Win32.SafeHandles.SafeFileHandle hFile, IntPtr lpCreationTime, ref FILETIME lpLastAccessTime, IntPtr lpLastWriteTime);

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
