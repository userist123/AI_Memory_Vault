using System.Globalization;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>One suspicious sign found on a removable volume, in plain Romanian.</summary>
public sealed record MediaFinding(string Path, string Message);

/// <summary>Result of the quick check of an inserted stick.</summary>
public sealed class MediaInspection
{
    public string Root { get; init; } = "";
    public List<MediaFinding> Findings { get; init; } = [];
    public bool Unreadable { get; init; }
    public string Error { get; init; } = "";
    public bool IsSuspicious => Findings.Count > 0;

    /// <summary>One line for the medium card.</summary>
    public string Text => Unreadable ? $"Verificare suport: nu s-a putut citi ({Error})."
        : Findings.Count == 0 ? "Verificare suport: nimic suspect la rădăcină și primul nivel."
        : $"Verificare suport: {Findings.Count} semne suspecte — {string.Join("; ", Findings.Take(4).Select(f => f.Message))}{(Findings.Count > 4 ? "; …" : "")}.";
}

/// <summary>
/// Quick, read-only check of a removable volume (research 2026-10-12, item 4): root plus one level. Only names and attributes are listed; nothing is
/// opened or executed and reparse points (junctions, symlinks) are not followed. Flags autorun.inf, hidden/system folders, blank-named entries,
/// RECYCLER on removable media, a lone shortcut named like the drive or label (classic USB-worm trick), executables/scripts at the root, and shortcuts
/// (root and level 1) whose decoded target is a system interpreter / proxy program or whose arguments are padded (decoded as bytes, never run).
/// </summary>
public static class RemovableMediaInspector
{
    public static readonly string[] RiskyExtensions = [".exe", ".scr", ".com", ".bat", ".cmd", ".vbs", ".js", ".ps1", ".hta", ".lnk"];
    private const int MaxEntriesPerFolder = 5000;
    // folders Windows itself creates on volumes; hidden+system by design, so not a sign by themselves
    private static readonly string[] Benign = ["System Volume Information", "$RECYCLE.BIN"];

    public static MediaInspection Inspect(string root, string volumeLabel = "")
    {
        var findings = new List<MediaFinding>();
        var rootPath = root.TrimEnd('\\', '/') + "\\";
        List<FileSystemInfo> top;
        try { top = Read(rootPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new MediaInspection { Root = rootPath, Unreadable = true, Error = ex.Message }; }

        var driveName = rootPath.Length >= 2 && rootPath[1] == ':' ? char.ToUpperInvariant(rootPath[0]).ToString() : "";
        foreach (var e in top)
        {
            var name = e.Name;
            var isDir = e is DirectoryInfo;
            var reparse = e.Attributes.HasFlag(FileAttributes.ReparsePoint);
            var shown = IsBlank(name) ? "(nume gol)" : name;
            if (name.Equals("autorun.inf", StringComparison.OrdinalIgnoreCase)) findings.Add(new(name, "autorun.inf la rădăcină (pornire automată)"));
            if (isDir)
            {
                if (IsBlank(name)) findings.Add(new(shown, "folder cu nume gol sau doar spații"));
                else if (name.Equals("RECYCLER", StringComparison.OrdinalIgnoreCase) || name.Equals("RECYCLER.BIN", StringComparison.OrdinalIgnoreCase))
                    findings.Add(new(name, $"folderul {name} pe un suport detașabil (loc folosit de viermii USB)"));
                else if (reparse) findings.Add(new(name, $"folderul „{name}” este o legătură (junction/link); nu a fost urmărită"));
                else if ((e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 && !Benign.Contains(name, StringComparer.OrdinalIgnoreCase))
                    findings.Add(new(name, $"folder ascuns sau de sistem „{name}”"));
            }
            else
            {
                var ext = Path.GetExtension(name).ToLowerInvariant();
                var stem = Path.GetFileNameWithoutExtension(name);
                if (ext == ".lnk" && LooksLikeDrive(stem, volumeLabel, driveName))
                    findings.Add(new(name, $"o singură scurtătură „{name}” cu numele unității sau etichetei (truc clasic de vierme USB)"));
                else if (RiskyExtensions.Contains(ext))
                    findings.Add(new(name, $"fișier executabil sau script la rădăcină: {name}"));
                if (ext == ".lnk" && ShortcutSign(name, e) is { Length: > 0 } sign) findings.Add(new(name, sign));
            }
        }

        // one level down: folders only that are real directories (not reparse points, not Windows' own)
        foreach (var d in top.OfType<DirectoryInfo>().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint) && !Benign.Contains(d.Name, StringComparer.OrdinalIgnoreCase)))
        {
            List<FileSystemInfo> inner;
            try { inner = Read(d.FullName); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            var rel = IsBlank(d.Name) ? "(nume gol)" : d.Name;
            foreach (var e in inner)
            {
                var shown = IsBlank(e.Name) ? "(nume gol)" : e.Name;
                if (e.Name.Equals("autorun.inf", StringComparison.OrdinalIgnoreCase)) findings.Add(new($"{rel}\\{shown}", $"autorun.inf în folderul „{rel}”"));
                else if (IsBlank(e.Name)) findings.Add(new($"{rel}\\{shown}", $"intrare cu nume gol în folderul „{rel}”"));
                else if (e is DirectoryInfo && (e.Name.Equals("RECYCLER", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("RECYCLER.BIN", StringComparison.OrdinalIgnoreCase)))
                    findings.Add(new($"{rel}\\{e.Name}", $"folderul {e.Name} în „{rel}”"));
                else if (e is FileInfo && (e.Attributes & FileAttributes.Hidden) != 0 && RiskyExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
                    findings.Add(new($"{rel}\\{e.Name}", $"executabil sau script ASCUNS „{e.Name}” în folderul „{rel}”"));
                if (e.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && ShortcutSign(e.Name, e) is { Length: > 0 } sign1) findings.Add(new($"{rel}\\{e.Name}", sign1));
            }
        }
        return new MediaInspection { Root = rootPath, Findings = findings };
    }

    /// <summary>
    /// Read-only listing of a removable volume for the P1 rules (MEDIA-LNK-PAYLOAD, MEDIA-HIDDEN-MAILBOX, MEDIA-SAME-DROP): root and two levels, names,
    /// attributes, sizes and times; SHA-256 of files up to <paramref name="hashMaxBytes"/>; shortcuts decoded by <see cref="FileSystem.LnkParser.Decode"/>
    /// (read as bytes, never opened by the shell, never run). Reparse points are listed but not followed; Windows' own folders are not entered.
    /// </summary>
    public static LogAnalyzer.Dfir.Analysis.MediaListingFact List(string root, string volumeLabel = "", int maxEntries = 3000, long hashMaxBytes = 16 * 1024 * 1024, int maxHashed = 300)
    {
        var rootPath = root.TrimEnd('\\', '/') + "\\";
        var listing = new LogAnalyzer.Dfir.Analysis.MediaListingFact
        {
            Letter = rootPath.Length >= 2 && rootPath[1] == ':' ? char.ToUpperInvariant(rootPath[0]).ToString() : "", Label = volumeLabel,
        };
        int hashed = 0;
        void Walk(string dir, string rel, int depth)
        {
            List<FileSystemInfo> items;
            try { items = Read(dir); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
            foreach (var e in items)
            {
                if (listing.Entries.Count >= maxEntries) { listing.Truncated = true; return; }
                var entry = new LogAnalyzer.Dfir.Analysis.MediaEntryFact
                {
                    RelativePath = rel.Length == 0 ? e.Name : rel + "\\" + e.Name, IsDirectory = e is DirectoryInfo,
                    Hidden = e.Attributes.HasFlag(FileAttributes.Hidden), System = e.Attributes.HasFlag(FileAttributes.System),
                    CreatedUtc = Utc(e.CreationTimeUtc), LastWriteUtc = Utc(e.LastWriteTimeUtc),
                };
                if (e is FileInfo fi)
                {
                    entry.Size = fi.Length;
                    if (fi.Length <= hashMaxBytes && hashed < maxHashed && !e.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        try
                        {
                            using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            entry.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
                            hashed++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    if (fi.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && fi.Length is > 0 and <= MaxLinkBytes && ReadLink(fi.FullName) is { } link)
                    { entry.LnkTarget = link.Target; entry.LnkArguments = link.Arguments; entry.LnkWorkingDir = link.WorkingDir; }
                }
                listing.Entries.Add(entry);
            }
            if (depth >= 2) return;
            foreach (var d in items.OfType<DirectoryInfo>().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint) && !Benign.Contains(d.Name, StringComparer.OrdinalIgnoreCase)))
                Walk(d.FullName, rel.Length == 0 ? d.Name : rel + "\\" + d.Name, depth + 1);
        }
        Walk(rootPath, "", 0);
        return listing;
    }

    private const int MaxLinkBytes = 1024 * 1024;

    private static DateTimeOffset? Utc(DateTime t) => t.Year > 1601 ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null;

    /// <summary>The decoded shortcut, or null when the bytes are not a valid link (nothing is executed; the shell is not involved).</summary>
    internal static FileSystem.LnkParser.Link? ReadLink(string path)
    {
        try { return FileSystem.LnkParser.Decode(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    /// <summary>What a shortcut on the medium starts, in plain Romanian, when it is a system interpreter / proxy program or its arguments are padded; "" otherwise.</summary>
    internal static string ShortcutSign(string name, FileSystemInfo file)
    {
        if (file is not FileInfo { Length: > 0 and <= MaxLinkBytes } || ReadLink(file.FullName) is not { } link) return "";
        var exe = Path.GetFileName(link.Target.Replace('/', '\\')).ToLowerInvariant();
        var depad = FileSystem.LnkText.Depad(link.Arguments);
        var parts = new List<string>();
        if (exe.Length > 0 && LogAnalyzer.Dfir.Analysis.Wp14Data.Default.Payload.Lolbins.Contains(exe, StringComparer.OrdinalIgnoreCase))
            parts.Add($"scurtătura „{name}” pornește {exe} ({link.Target}{(depad.Text.Length > 0 ? " " + Short(depad.Text) : "")}), nu un document");
        if (depad.Padded) parts.Add($"argumentele scurtăturii „{name}” încep cu {depad.Leading} caractere invizibile (comanda e ascunsă în fereastra Proprietăți): {Short(depad.Text)}");
        return string.Join("; ", parts);
    }

    private static string Short(string s) => s.Length <= 160 ? s : s[..160] + "…";

    private static List<FileSystemInfo> Read(string dir) =>
        new DirectoryInfo(dir).EnumerateFileSystemInfos().Take(MaxEntriesPerFolder).ToList();

    /// <summary>Empty, whitespace-only or made only of invisible characters (zero-width, control, format).</summary>
    public static bool IsBlank(string name) =>
        name.All(c => char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Control);

    private static bool LooksLikeDrive(string stem, string label, string driveName)
    {
        if (stem.Length == 0) return false;
        if (label.Length > 0 && stem.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        // „E”, „E:” or the Windows default label forms
        var s = stem.TrimEnd(':');
        return driveName.Length > 0 && s.Equals(driveName, StringComparison.OrdinalIgnoreCase);
    }
}
