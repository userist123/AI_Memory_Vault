using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.IO;

/// <summary>
/// Analysis/timeline.csv: the one writer (used by the pipeline) and the matching reader (used to open an existing case, WP5).
/// The file carries only the columns of <see cref="Header"/>, so a timeline read back has those fields and no more: parser-specific
/// <c>Fields</c>, <c>Task</c>, <c>Service</c>, <c>Hash</c>, <c>Ppid</c>, confidence and notes are not in the file and come back empty.
/// </summary>
public static class TimelineCsv
{
    public static readonly string[] Header =
    [
        "TimeUtc", "TimeSemantics", "Source", "EventId", "Provider", "Host", "User", "Process", "Pid", "Path", "RemoteIp", "RemotePort", "Dns", "Summary", "Classification",
        "EvidenceId", "Locator", "SourceSha256", "Parser", "ParserVersion", "TimeRaw", "TimeConversion", "TimeZoneBasis", "TimeUncertainty", "SemanticType",
    ];

    public static void Write(string path, IEnumerable<TimelineEvent> events)
    {
        using var w = new CsvWriter(path, Header);
        foreach (var e in events)
            w.WriteRow(new object?[] { e.Time.Utc?.ToString("o") ?? "", e.TimeSemantics, e.Source, e.EventId, e.Provider, e.Host, e.User, e.Process, e.Pid, e.Path, e.RemoteIp, e.RemotePort, e.Dns, e.Summary, e.Classification.ToSpec(), e.EvidenceId, e.Locator, e.SourceSha256, e.ParserId, e.ParserVersion,
                                       e.Time.Raw, e.Time.ConversionMethod, e.TimeZoneBasis, e.TimeUncertainty, e.SemanticType.ToSpec() });
    }

    /// <summary>Reads a timeline.csv. A file without the mandatory columns (TimeUtc, Source, EvidenceId, Summary) is refused with the reason.</summary>
    public static List<TimelineEvent> Read(string path)
    {
        var list = new List<TimelineEvent>();
        bool checkedHeader = false;
        foreach (var d in CsvReader.ReadDicts(path))
        {
            if (!checkedHeader)
            {
                foreach (var col in new[] { "TimeUtc", "Source", "EvidenceId", "Summary" })
                    if (!d.ContainsKey(col)) throw new InvalidDataException($"timeline.csv: coloana obligatorie '{col}' lipsește.");
                checkedHeader = true;
            }
            string G(string k) => d.TryGetValue(k, out var v) ? v : "";
            DateTimeOffset? utc = DateTimeOffset.TryParse(G("TimeUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
            int? Int(string k) => int.TryParse(G(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            var raw = G("TimeRaw"); var conv = G("TimeConversion");
            list.Add(new TimelineEvent
            {
                Time = new Timestamp(utc, raw, conv.Length > 0 ? conv : "none"),
                Source = G("Source"), EvidenceId = G("EvidenceId"), EventId = G("EventId"), Provider = G("Provider"), Host = G("Host"), User = G("User"),
                Process = G("Process"), Pid = Int("Pid"), Path = G("Path"), RemoteIp = G("RemoteIp"), RemotePort = Int("RemotePort"), Dns = G("Dns"),
                Summary = G("Summary"), TimeSemantics = G("TimeSemantics").Length > 0 ? G("TimeSemantics") : "recorded",
                Classification = ParseClassification(G("Classification")), Locator = G("Locator"), SourceSha256 = G("SourceSha256"),
                ParserId = G("Parser"), ParserVersion = G("ParserVersion"), TimeZoneBasis = G("TimeZoneBasis"), TimeUncertainty = G("TimeUncertainty"),
                SemanticType = ParseSemantic(G("SemanticType")),
            });
        }
        return list;
    }

    private static Classification ParseClassification(string s)
    {
        foreach (var c in Enum.GetValues<Classification>())
            if (string.Equals(c.ToSpec(), s, StringComparison.OrdinalIgnoreCase)) return c;
        return Classification.Unproven;   // an unreadable value is the weakest claim, never a stronger one
    }

    private static SemanticType ParseSemantic(string s)
    {
        foreach (var c in Enum.GetValues<SemanticType>())
            if (string.Equals(c.ToSpec(), s, StringComparison.OrdinalIgnoreCase)) return c;
        return SemanticType.Observation;
    }
}
