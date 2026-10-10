using System.Text.Json;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// How much the interface says in words (WP18 S3). <see cref="Simple"/> is the default for EVERY account, administrators included: the account
/// role says nothing about the person's technical background. <see cref="Expert"/> keeps the technical names beside the human phrases.
/// The level changes words, order and how much detail is open; it never changes which data exist.
/// </summary>
public enum UiLanguageLevel { Simple, Expert }

/// <summary>The level in force for this process. Set at sign-in from the account's preference; changed by the header switch.</summary>
public static class LanguageLevelContext
{
    private static UiLanguageLevel _current = UiLanguageLevel.Simple;

    public static UiLanguageLevel Current => _current;
    public static bool IsExpert => _current == UiLanguageLevel.Expert;
    /// <summary>Raised after a change; bound texts (glossary terms) recompute from it.</summary>
    public static event Action? Changed;

    public static void Set(UiLanguageLevel level)
    {
        if (_current == level) return;
        _current = level;
        Changed?.Invoke();
    }

    /// <summary>For tests only.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void ResetForTests() { _current = UiLanguageLevel.Simple; }
}

/// <summary>
/// The language level of each application account, kept next to the other per-user conveniences (recent cases) under
/// %LOCALAPPDATA%\LogAnalyzer\preferences\. It is a convenience, never evidence: a missing or unreadable file means Simple.
/// </summary>
public sealed class LanguagePreferences
{
    private readonly string _dir;

    public LanguagePreferences(string? directory = null) => _dir = directory ?? DefaultDirectory();

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "preferences");

    private sealed class Record { public string Account { get; set; } = ""; public string LanguageLevel { get; set; } = "Simple"; }

    private string FileFor(string account)
    {
        var safe = string.Concat(account.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return Path.Combine(_dir, (safe.Length == 0 ? "account" : safe) + ".json");
    }

    public UiLanguageLevel Load(string account)
    {
        try
        {
            var f = FileFor(account);
            if (!File.Exists(f)) return UiLanguageLevel.Simple;
            var r = JsonSerializer.Deserialize<Record>(File.ReadAllText(f));
            return r is not null && Enum.TryParse<UiLanguageLevel>(r.LanguageLevel, true, out var lvl) ? lvl : UiLanguageLevel.Simple;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return UiLanguageLevel.Simple; }
    }

    /// <summary>Best effort: a preference that cannot be written is simply not remembered; the session keeps the chosen level.</summary>
    public bool Save(string account, UiLanguageLevel level)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FileFor(account), JsonSerializer.Serialize(new Record { Account = account, LanguageLevel = level.ToString() }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
