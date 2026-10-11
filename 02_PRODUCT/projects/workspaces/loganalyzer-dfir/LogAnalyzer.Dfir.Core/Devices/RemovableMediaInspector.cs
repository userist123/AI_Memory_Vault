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
/// RECYCLER on removable media, a lone shortcut named like the drive or label (classic USB-worm trick) and executables/scripts at the root.
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
            }
        }
        return new MediaInspection { Root = rootPath, Findings = findings };
    }

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
