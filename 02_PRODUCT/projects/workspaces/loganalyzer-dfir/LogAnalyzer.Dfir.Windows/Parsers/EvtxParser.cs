using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Offline EVTX parser over the raw exported file (spec §16-§18). Preserves every EventData/UserData field,
/// uses the EventRecordID as locator, never substitutes "now" for a missing time, and counts malformed
/// records instead of hiding them. Rendered messages are produced only for providers whose text carries
/// the evidence (Defender, MsiInstaller, SCM, PowerShell host lines…), to keep large logs fast.
/// </summary>
public sealed class EvtxParser : EvidenceParserBase
{
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "EvtxParser", Version = "1.0", Artifact = "Jurnal de evenimente Windows (EVTX)",
        SourceTypes = ["evtx", "EventLog:*"], FileNames = [".evtx"], Fingerprints = ["evtx"],
        SupportedOs = "Windows (citește prin API-ul EventLog al Windows, PathType.FilePath)",
        FormatVersions = ["EVTX 3.x (Windows Vista și ulterior)"],
        Limitations =
        [
            "Mesajele sunt randate doar pentru furnizorii a căror descriere conține proba (Defender, MsiInstaller, SCM, PowerShell); restul păstrează câmpurile EventData/UserData.",
            "Chunk-urile corupte sunt eliminate de EvtxRepair; înregistrările lor lipsesc și sunt raportate ca gol de RecordID.",
            "Nu recuperează înregistrări din spațiul nealocat (carving).",
        ],
        Status = ParserMaturity.Validated,
        Validation = "CorpusRegressionTests (defenderEvtx, msiEvtx), InvestigationTests pe corpusul NanAgent, EvtxRepairTests",
    };

    private static readonly HashSet<string> MessageProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft-Windows-Windows Defender", "MsiInstaller", "Service Control Manager", "User32", "PowerShell",
        "Microsoft-Windows-PowerShell", "Microsoft-Windows-Windows Firewall With Advanced Security",
        "Microsoft-Windows-TaskScheduler", "Microsoft-Windows-Bits-Client", "Application Error", "Windows Error Reporting",
        "Microsoft-Windows-Kernel-General", "Microsoft-Windows-Kernel-Power", "Microsoft-Windows-Eventlog",
    };

    private static readonly string[] PathFields = ["Path", "NewProcessName", "ApplicationPath", "Application", "ImagePath", "Image", "TargetFilename", "Process Name", "ProcessName", "ExePath", "HostApplication"];
    private static readonly string[] UserFields = ["Detection User", "TargetUserName", "SubjectUserName", "User", "UserName"];

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        // The Windows API refuses the whole file when one chunk is corrupt: check first, and parse a repaired copy if needed.
        var damaged = LogAnalyzer.Dfir.IO.EvtxRepair.Inspect(fullPath).Where(c => !c.Valid && !c.Problem.StartsWith("chunk gol", StringComparison.Ordinal)).ToList();
        if (damaged.Count > 0)
        {
            var repaired = Path.Combine(Path.GetTempPath(), "LogAnalyzer", "evtx_repair", $"{item.EvidenceId}_{Path.GetFileName(fullPath)}");
            var r = LogAnalyzer.Dfir.IO.EvtxRepair.Repair(fullPath, repaired);
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial,
                $"{damaged.Count} chunk-uri corupte eliminate: " + string.Join("; ", damaged.Take(20).Select(c => $"#{c.Index} ({c.Problem}, RecordID {c.FirstRecordId}–{c.LastRecordId})")),
                "Înregistrările din aceste intervale lipsesc din cronologie (corupere sau alterare a jurnalului)",
                "Copii VSS ale jurnalului, SIEM, alte surse (SRUM, Prefetch)", "Doar din alte copii",
                $"Fișierul original nu a fost modificat; s-a parsat copia reparată {repaired} ({r.KeptChunks}/{r.TotalChunks} chunk-uri)."));
            result.MalformedRecords += damaged.Count;
            fullPath = repaired;
        }
        using var reader = new EventLogReader(fullPath, PathType.FilePath);
        int consecutiveErrors = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            EventRecord? rec;
            try { rec = reader.ReadEvent(); consecutiveErrors = 0; }
            catch (EventLogException ex)
            {
                result.MalformedRecords++;
                if (++consecutiveErrors > 1000) throw new InvalidDataException("Too many consecutive unreadable records.", ex);
                continue;
            }
            if (rec is null) break;
            using (rec)
            {
                sink.Add(ToEvent(item, rec));
                result.Records++;
            }
        }
    }

    private TimelineEvent ToEvent(EvidenceItem item, EventRecord rec)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try { ExtractFields(rec.ToXml(), fields); }
        catch (EventLogException) { fields["_xml"] = "unavailable"; }

        string provider = rec.ProviderName ?? "";
        string message = "";
        if (MessageProviders.Contains(provider))
        {
            try { message = rec.FormatDescription() ?? ""; }
            catch (EventLogException) { /* message templates missing on this host: the raw fields above still hold the data */ }
        }

        string path = PathFields.Select(f => fields.GetValueOrDefault(f, "")).FirstOrDefault(v => v.Length > 0) ?? "";
        if (path.StartsWith("file:_", StringComparison.Ordinal)) path = path[6..];
        string user = UserFields.Select(f => fields.GetValueOrDefault(f, "")).FirstOrDefault(v => v.Length > 0) ?? (rec.UserId?.Value ?? "");

        var time = rec.TimeCreated is DateTime t
            ? Timestamp.FromUtc(t.ToUniversalTime(), t.ToString("o"), "EVTX SystemTime")
            : Timestamp.Unknown();

        string firstLine = message.Split('\n', 2)[0].Trim();
        string summary = firstLine.Length > 0
            ? $"{provider} {rec.Id}: {Truncate(firstLine, 300)}"
            : $"{provider} {rec.Id}: {Truncate(string.Join("; ", fields.Where(kv => !kv.Key.StartsWith('_')).Take(6).Select(kv => $"{kv.Key}={kv.Value}")), 300)}";
        if (message.Length > 0) fields["_message"] = Truncate(message, 4000);

        return new TimelineEvent
        {
            Time = time,
            Source = "EventLog:" + (rec.LogName ?? item.Source),
            EvidenceId = item.EvidenceId,
            EventId = rec.Id.ToString(),
            Provider = provider,
            Host = rec.MachineName ?? "",
            User = user,
            Process = path.Length > 0 ? System.IO.Path.GetFileName(path.TrimEnd('\\')) : "",
            Pid = rec.ProcessId is int p ? p : null,
            Path = path,
            Summary = summary,
            TimeSemantics = "event recorded",
            TemporalType = TemporalType.Historical,
            Classification = Classification.Direct,
            Confidence = Confidence.High,
            Locator = $"EventRecordID={rec.RecordId}",
            Fields = fields,
        };
    }

    internal static void ExtractFields(string xml, Dictionary<string, string> fields)
    {
        var doc = XDocument.Parse(xml);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var data = doc.Root?.Element(ns + "EventData");
        if (data is not null)
        {
            int i = 0;
            foreach (var d in data.Elements())
            {
                var name = (string?)d.Attribute("Name") ?? $"Data{i}";
                fields[name] = d.Value;
                i++;
            }
        }
        var user = doc.Root?.Element(ns + "UserData");
        if (user is not null)
            foreach (var leaf in user.Descendants().Where(e => !e.HasElements))
                fields[leaf.Name.LocalName] = leaf.Value;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
