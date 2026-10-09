using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace LogAnalyzer.Dfir.Language;

/// <summary>UI language. Romanian is the default and the fallback; English is supported (U17).</summary>
public enum AppLanguage { Romanian, English }

/// <summary>
/// The resource layer (WP6b, U17): one key to text table per language, embedded as JSON (<c>Strings.ro.json</c>, <c>Strings.en.json</c>).
/// A key that has no English text falls back to the Romanian text (never to the raw key) and is listed in <see cref="FallbackKeys"/> so a test can fail on it;
/// a key that is deliberately Romanian-only is declared with a reason in <c>Strings.roonly.json</c>.
/// Identifiers (rule ids, event ids, channel names, file names, hashes) are data and never go through this table.
/// </summary>
public static class Loc
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Ro = new(() => Read("Strings.ro.json"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> En = new(() => Read("Strings.en.json"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> RoOnlyTable = new(() => Read("Strings.roonly.json"));
    private static readonly AsyncLocal<AppLanguage?> Scoped = new();
    private static readonly object FallbackGate = new();
    private static readonly HashSet<string> Fallbacks = [];
    private static volatile AppLanguage _global = AppLanguage.Romanian;

    /// <summary>The language in force: the scoped one (<see cref="Use"/>) if any, else the application-wide one.</summary>
    public static AppLanguage Current => Scoped.Value ?? _global;

    /// <summary>Raised after <see cref="SetLanguage"/> changed the application-wide language; views and view models re-read their text.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>Changes the application-wide language and tells the views. No restart is needed.</summary>
    public static void SetLanguage(AppLanguage language)
    {
        if (_global == language) return;
        _global = language;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Uses <paramref name="language"/> for the current async flow only (an export in the chosen language, a test). Dispose to restore.</summary>
    public static IDisposable Use(AppLanguage language)
    {
        var previous = Scoped.Value;
        Scoped.Value = language;
        return new Restore(previous);
    }

    private sealed class Restore(AppLanguage? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }

    public static string Code(AppLanguage l) => l == AppLanguage.English ? "en" : "ro";
    public static AppLanguage FromCode(string? code) => string.Equals(code?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? AppLanguage.English : AppLanguage.Romanian;

    /// <summary>The text of <paramref name="key"/> in the current language.</summary>
    public static string T(string key) => T(key, Current);

    /// <summary>The text of <paramref name="key"/> in <paramref name="language"/>; Romanian when English is missing; the key itself only when the key does not exist at all.</summary>
    public static string T(string key, AppLanguage language)
    {
        if (language == AppLanguage.English)
        {
            if (En.Value.TryGetValue(key, out var en)) return en;
            if (Ro.Value.TryGetValue(key, out var ro)) { NoteFallback(key); return ro; }
        }
        else if (Ro.Value.TryGetValue(key, out var ro2)) return ro2;
        NoteFallback(key);
        return key;
    }

    /// <summary>Like <see cref="T(string)"/> with <c>{0}</c>, <c>{1}</c>... filled by <paramref name="args"/> (invariant culture).</summary>
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);
    public static string Format(string key, AppLanguage language, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(key, language), args);

    public static bool Has(string key) => Ro.Value.ContainsKey(key);
    public static bool Has(string key, AppLanguage language) => language == AppLanguage.English ? En.Value.ContainsKey(key) : Ro.Value.ContainsKey(key);

    /// <summary>Text for a stable reason code (e.g. an auth failure) when the table has <c>{prefix}.{code}</c>; otherwise the original (Romanian) message the backend gave.</summary>
    public static string Reason(string prefix, string code, string fallbackMessage) => Has($"{prefix}.{code}") ? T($"{prefix}.{code}") : fallbackMessage;

    public static IReadOnlyCollection<string> Keys(AppLanguage language) => (language == AppLanguage.English ? En : Ro).Value.Keys.ToList();

    /// <summary>Keys that are deliberately Romanian-only, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> RomanianOnly => RoOnlyTable.Value;

    /// <summary>Keys that were asked for and had to fall back (English missing) or did not exist. A test asserts this stays empty for the keys the UI uses.</summary>
    public static IReadOnlyCollection<string> FallbackKeys { get { lock (FallbackGate) return Fallbacks.ToList(); } }

    private static void NoteFallback(string key) { lock (FallbackGate) Fallbacks.Add(key); }

    private static IReadOnlyDictionary<string, string> Read(string file)
    {
        var asm = typeof(Loc).Assembly;
        using var stream = asm.GetManifestResourceStream("LogAnalyzer.Dfir.Language." + file)
            ?? throw new InvalidOperationException("Embedded resource missing: " + file);
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        return d;
    }
}
