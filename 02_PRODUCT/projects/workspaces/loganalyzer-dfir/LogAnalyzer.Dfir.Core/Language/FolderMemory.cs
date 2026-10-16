using System.Text.Json;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// Usability 2026-10-16 (owner: „cât mai puține clicuri”): the last folder each user chose for each kind of file dialog (report copy, export for the officer,
/// CD-R copy of the log, evidence), kept with the other per-user conveniences under %LOCALAPPDATA%\LogAnalyzer\preferences\ (one file per Windows user).
/// It is a convenience, never evidence: a missing or unreadable file, or a folder that no longer exists, simply means "not remembered".
/// </summary>
public sealed class FolderMemory
{
    /// <summary>Dialog kinds (keys of the file).</summary>
    public const string ReportCopy = "report_copy", OfficerExport = "officer_export", AnchorExport = "anchor_export", SignedReport = "signed_report",
        Evidence = "evidence", MediaRegisterExport = "media_register_export";

    private readonly string _file;

    public FolderMemory(string? directory = null, string? user = null)
    {
        var dir = directory ?? LanguagePreferences.DefaultDirectory();
        var who = user ?? Environment.UserName;
        var safe = string.Concat(who.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        _file = Path.Combine(dir, "folders_" + (safe.Length == 0 ? "user" : safe) + ".json");
    }

    /// <summary>The memory of the Windows user running the application.</summary>
    public static FolderMemory Shared { get; } = new();

    private Dictionary<string, string> Read()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    /// <summary>The folder remembered for <paramref name="kind"/> when it still exists; null otherwise.</summary>
    public string? Get(string kind)
    {
        var d = Read();
        return d.TryGetValue(kind, out var p) && !string.IsNullOrWhiteSpace(p) && Directory.Exists(p) ? p : null;
    }

    /// <summary>Remembers the folder (or the folder of a file) chosen for <paramref name="kind"/>. Best effort: false when it cannot be written.</summary>
    public bool Remember(string kind, string? folderOrFile)
    {
        if (string.IsNullOrWhiteSpace(folderOrFile)) return false;
        var folder = Directory.Exists(folderOrFile) ? folderOrFile : Path.GetDirectoryName(folderOrFile);
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try
        {
            var d = Read();
            d[kind] = folder;
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(d));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Forgets the folder of <paramref name="kind"/> (the "Alege alt folder" path starts from scratch).</summary>
    public void Forget(string kind)
    {
        try
        {
            var d = Read();
            if (!d.Remove(kind)) return;
            File.WriteAllText(_file, JsonSerializer.Serialize(d));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
