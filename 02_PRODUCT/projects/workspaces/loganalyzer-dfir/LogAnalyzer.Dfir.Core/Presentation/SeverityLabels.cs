using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>Severity as text and as an icon shape, so a severity is never shown by colour alone (U18).</summary>
public static class SeverityLabels
{
    private static string Key(Severity s) => "sev." + s.ToString().ToLowerInvariant();

    /// <summary>The word for a severity in the current UI language.</summary>
    public static string Text(Severity s) => Loc.T(Key(s));
    public static string Text(Severity s, AppLanguage lang) => Loc.T(Key(s), lang);
    public static string Romanian(Severity s) => Loc.T(Key(s), AppLanguage.Romanian);

    /// <summary>A distinct geometric shape per level (Segoe UI Symbol has all of them).</summary>
    public static string Icon(Severity s) => s switch
    {
        Severity.Critical => "◆",
        Severity.High => "▲",
        Severity.Medium => "■",
        Severity.Low => "▼",
        _ => "○",
    };
}
