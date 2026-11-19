using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Integrity;

/// <summary>
/// Planner #11 A1: what a piece of evidence QUOTES from the original (the key fields of the row, or the raw line), where the time of the row comes from, and its channel,
/// so the owner can put the report next to the original on paper. Built only from the timeline row the reference points to; nothing is invented.
/// </summary>
public static class EvidenceQuote
{
    public const int MaxQuoteLength = 400;
    public const string UnknownTimeSource = "Necunoscut";
    public const string EvtxTime = "EVTX TimeCreated";
    public const string RegistryLastWrite = "Registry LastWrite (cheie)";
    public const string PrefetchTime = "Prefetch: ultima rulare";
    public const string SnapshotTime = "Instantaneu la colectare";
    public const string TextLineTime = "Linie de jurnal text";

    /// <summary>Most relevant first: who, from where, how, what ran, what object (EVTX EventData), then what a registry value says.</summary>
    private static readonly string[] Priority =
    [
        "TargetUserName", "SubjectUserName", "TargetDomainName", "SubjectDomainName", "IpAddress", "WorkstationName", "LogonType",
        "ProcessName", "NewProcessName", "ParentProcessName", "Image", "CommandLine", "ServiceName", "ImagePath", "TaskName", "ObjectName",
        "ValueName", "Name", "Command", "Data", "Value", "Key",
    ];
    private const int MaxFields = 10;
    private static readonly Regex TextLine = new(@"(^|:)line=\d+$", RegexOptions.Compiled);
    private static readonly Regex Control = new(@"[\r\n\t\0]+", RegexOptions.Compiled);

    /// <summary>The quote, time source and channel of one row.</summary>
    public static (string Quote, string TimeSource, string Channel) From(TimelineEvent row)
    {
        bool evtx = row.Source.StartsWith("EventLog:", StringComparison.Ordinal);
        bool text = !evtx && (TextLine.IsMatch(row.Locator) || row.Source.StartsWith("Artifact:", StringComparison.Ordinal) || row.Source == "PowerShellTranscript");
        string quote = text ? Truncate(RawLine(row), MaxQuoteLength) : Truncate(FieldsQuote(row), MaxQuoteLength);
        string channel = evtx ? row.Source["EventLog:".Length..] : row.Source;
        return (quote, TimeSourceOf(row, evtx, text), channel);
    }

    private static string TimeSourceOf(TimelineEvent row, bool evtx, bool text)
    {
        if (row.Source == "live system") return SnapshotTime;
        if (row.Time.Utc is null) return UnknownTimeSource;
        if (evtx) return EvtxTime;
        if (row.Source == "Prefetch") return PrefetchTime;
        if (text) return TextLineTime;
        var sem = row.TimeSemantics.Trim();
        if (sem.Contains("key last written", StringComparison.OrdinalIgnoreCase) || sem.Contains("last write of the key", StringComparison.OrdinalIgnoreCase)) return RegistryLastWrite;
        if (sem.Length == 0 || sem is "t" or "recorded") return UnknownTimeSource;
        return Truncate("Din sursă: " + sem, 120);
    }

    private static string RawLine(TimelineEvent row)
    {
        foreach (var k in new[] { "RawLine", "Raw", "Text" })
            if (row.Fields.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return Clean(v);
        return SummaryQuote(row);
    }

    /// <summary>Main-session review 2026-11-19: the parser's summary is NOT the original text; it is labelled so nobody compares it word for word with the source.</summary>
    public const string SummaryLabel = "Rezumat (nu textul original): ";
    private static string SummaryQuote(TimelineEvent row) => string.IsNullOrWhiteSpace(row.Summary) ? "" : SummaryLabel + Clean(row.Summary);

    private static string FieldsQuote(TimelineEvent row)
    {
        var shown = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string key)
        {
            if (shown.Count >= MaxFields || !used.Add(key) || !row.Fields.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return;
            shown.Add($"{key}={Clean(v)}");
        }
        foreach (var k in Priority) Add(k);
        foreach (var k in row.Fields.Keys.Where(k => !k.StartsWith('_')).OrderBy(k => k, StringComparer.Ordinal)) Add(k);
        return shown.Count > 0 ? string.Join("; ", shown) : SummaryQuote(row);
    }

    private static string Clean(string s) => Control.Replace(s ?? "", " ").Trim();

    /// <summary>At most <paramref name="max"/> characters including the closing "…", cut on a character boundary (a surrogate pair is never split).</summary>
    public static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
        int cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut] + "…";
    }

    /// <summary>
    /// Fills Quote / TimeSource / Channel of the references from their timeline rows (same pass as the source hash). A reference whose row is not found keeps what it
    /// carries (live snapshot references are filled where they are built); when it carries nothing the quote stays empty and the time source "Necunoscut".
    /// </summary>
    public static void Bind(IList<EvidenceRef> refs, IReadOnlyDictionary<(string EvidenceId, string Locator), TimelineEvent> rows)
    {
        for (int i = 0; i < refs.Count; i++)
        {
            var r = refs[i];
            if (!rows.TryGetValue((r.EvidenceId, r.Locator), out var row)) continue;
            var (quote, ts, channel) = From(row);
            refs[i] = r with { Quote = quote, TimeSource = ts, Channel = channel };
        }
    }

    public static Dictionary<(string EvidenceId, string Locator), TimelineEvent> Index(IEnumerable<TimelineEvent> timeline)
    {
        var d = new Dictionary<(string, string), TimelineEvent>();
        foreach (var e in timeline) d.TryAdd((e.EvidenceId, e.Locator), e);
        return d;
    }
}
