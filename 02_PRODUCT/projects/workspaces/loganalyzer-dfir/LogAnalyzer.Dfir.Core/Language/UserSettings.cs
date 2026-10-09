using System.Text.Json;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// Per-user settings (WP6b): today only the UI language. Stored next to the other per-user files (<c>%LOCALAPPDATA%\LogAnalyzer\settings.json</c>, like
/// <c>recent_cases.json</c>); default Romanian. A missing, unreadable or unknown value means Romanian; a file that cannot be written is not an error
/// (the choice then lasts for the session).
/// </summary>
public sealed class UserSettings(string file)
{
    public string File { get; } = file;

    public static string DefaultFile() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "settings.json");

    private sealed record Stored(string Language);

    public AppLanguage LoadLanguage()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return AppLanguage.Romanian;
            var s = JsonSerializer.Deserialize<Stored>(System.IO.File.ReadAllText(File), Json.Options);
            return Loc.FromCode(s?.Language);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return AppLanguage.Romanian; }
    }

    /// <summary>Returns false when the choice could not be written (it still applies for this session).</summary>
    public bool SaveLanguage(AppLanguage language)
    {
        try { Json.Write(File, new Stored(Loc.Code(language))); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
