using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>Severity as text and as an icon shape, so a severity is never shown by colour alone (U18).</summary>
public static class SeverityLabels
{
    public static string Romanian(Severity s) => s switch
    {
        Severity.Critical => "Critică",
        Severity.High => "Ridicată",
        Severity.Medium => "Medie",
        Severity.Low => "Scăzută",
        _ => "Informativă",
    };

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
