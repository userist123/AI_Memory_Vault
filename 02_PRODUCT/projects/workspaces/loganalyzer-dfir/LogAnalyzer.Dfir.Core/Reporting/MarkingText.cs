namespace LogAnalyzer.Dfir.Reporting;

/// <summary>
/// The classification marking line printed on a report (header and every footer). The marking is typed by the operator and never checked by the
/// application (it is not verified against the content and not per paragraph); an empty marking is printed as "fără marcaj introdus", never omitted.
/// </summary>
public static class MarkingText
{
    public const string Note = "marcaj introdus manual, neverificat de aplicație";
    public const string None = "fără marcaj introdus";

    /// <summary>"Marcaj: … (marcaj introdus manual, neverificat de aplicație)".</summary>
    public static string Line(string? marking) =>
        string.IsNullOrWhiteSpace(marking) ? $"Marcaj: {None} ({Note})" : $"Marcaj: {marking.Trim()} ({Note})";
}
