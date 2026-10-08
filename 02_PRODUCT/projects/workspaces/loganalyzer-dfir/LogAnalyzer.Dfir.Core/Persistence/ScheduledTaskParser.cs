using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Persistence;

/// <summary>
/// One scheduled task definition file (System32\Tasks\…, task schema 1.x). Emits one event per action (Exec or
/// ComHandler) with the task's configuration: what runs, as whom, when it triggers, hidden or not. It is the task's
/// configuration as copied, not proof that it ran (that is in TaskScheduler/Operational or Security 4698/4702).
/// </summary>
public sealed partial class ScheduledTaskParser : EvidenceParserBase
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "ScheduledTaskParser", Version = "1.0", Artifact = "Definiție de task programat (System32\\Tasks, XML)",
        SourceTypes = ["task_xml"], Fingerprints = ["task_xml"],
        SupportedOs = "Oricare (XML citit offline)",
        FormatVersions = ["Task Scheduler schema 1.1–1.6 (Windows Vista – 11)"],
        Limitations =
        [
            "Configurația taskului la momentul copierii; nu dovedește că a rulat (vezi TaskScheduler/Operational, 4698/4702).",
            "Data din RegistrationInfo este scrisă de cel care creează taskul și poate fi falsificată; fără fus orar rămâne neconvertită.",
            "Variabilele de mediu din comandă nu sunt expandate.",
            "Nu citește TaskCache din registru (taskuri fără fișier XML sau cu XML șters).",
        ],
        Status = ParserMaturity.Validated,
        Validation = "ScheduledTaskParserTests: 303 fișiere reale din corpus comparate cu schtasks /query /v; XML sintetic cu valori exacte",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        XDocument doc;
        using (var r = XmlReader.Create(fullPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            doc = XDocument.Load(r);
        var task = doc.Root ?? throw new InvalidDataException("XML fără element rădăcină.");
        if (task.Name != Ns + "Task") throw new InvalidDataException($"Rădăcina este {task.Name.LocalName}, nu Task.");

        string V(XElement? e, string path)
        {
            foreach (var part in path.Split('/')) e = e?.Element(Ns + part);
            return e?.Value.Trim() ?? "";
        }
        var reg = task.Element(Ns + "RegistrationInfo");
        var uri = V(reg, "URI") is { Length: > 0 } u ? u : "\\" + Path.GetFileName(fullPath);
        var principal = task.Element(Ns + "Principals")?.Elements(Ns + "Principal").FirstOrDefault();
        var settings = task.Element(Ns + "Settings");
        var triggers = task.Element(Ns + "Triggers")?.Elements().Select(t =>
            V(t, "StartBoundary") is { Length: > 0 } sb ? $"{t.Name.LocalName} {sb}" : t.Name.LocalName).ToList() ?? [];
        var dateRaw = V(reg, "Date");
        var time = ParseDate(dateRaw);

        var common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TaskUri"] = uri, ["Author"] = V(reg, "Author"), ["Description"] = V(reg, "Description"), ["RegistrationDate"] = dateRaw,
            ["Triggers"] = string.Join(", ", triggers), ["UserId"] = V(principal, "UserId"), ["GroupId"] = V(principal, "GroupId"),
            ["LogonType"] = V(principal, "LogonType"), ["RunLevel"] = V(principal, "RunLevel"),
            ["Hidden"] = V(settings, "Hidden") is { Length: > 0 } h ? h : "false",
            ["Enabled"] = V(settings, "Enabled") is { Length: > 0 } en ? en : "true",
            ["SchemaVersion"] = task.Attribute("version")?.Value ?? "",
        };
        var user = common["UserId"].Length > 0 ? common["UserId"] : common["GroupId"];

        var actions = task.Element(Ns + "Actions")?.Elements().ToList() ?? [];
        if (actions.Count == 0)
            result.Gaps.Add(new EvidenceGap(uri, EvidenceStatus.Partial, "taskul nu are nicio acțiune în XML",
                "Nu se știe ce rulează taskul", "TaskCache din hive-ul SOFTWARE", "Posibil"));

        int index = 0;
        foreach (var a in actions.DefaultIfEmpty())
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var kind = a?.Name.LocalName ?? "None";
            var command = a is null ? "" : V(a, "Command");
            var fields = new Dictionary<string, string>(common, StringComparer.OrdinalIgnoreCase)
            {
                ["ActionType"] = kind, ["Command"] = command, ["Arguments"] = a is null ? "" : V(a, "Arguments"),
                ["WorkingDirectory"] = a is null ? "" : V(a, "WorkingDirectory"), ["ComClassId"] = a is null ? "" : V(a, "ClassId"),
            };
            var what = command.Length > 0 ? $"{command} {fields["Arguments"]}".Trim()
                     : fields["ComClassId"].Length > 0 ? $"handler COM {fields["ComClassId"]}" : "fără acțiune";
            sink.Add(new TimelineEvent
            {
                Time = time, TimeSemantics = "task registration date (RegistrationInfo/Date, author-supplied)",
                Source = "ScheduledTask", EvidenceId = item.EvidenceId, Task = uri, User = user,
                // Path is the executable without the quotes the XML may carry; the raw command stays in Fields["Command"].
                Path = command.Trim().Trim('"'), Process = command.Length > 0 ? Path.GetFileName(command.Trim().Trim('"')) : "",
                Summary = $"Task programat {uri}: {what}" + (common["Hidden"] == "true" ? " (ascuns)" : "") +
                          (triggers.Count > 0 ? $"; declanșatori: {common["Triggers"]}" : ""),
                TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = a is null ? "Actions" : $"Actions/{kind}[{index}]", Fields = fields,
            });
            result.Records++;
        }
    }

    /// <summary>ISO time with an offset or Z is converted; without one the zone is unknown and the value stays raw.</summary>
    public static Timestamp ParseDate(string raw)
    {
        if (raw.Length == 0) return Timestamp.Unknown();
        if (OffsetSuffix().IsMatch(raw) && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return new Timestamp(dto.ToUniversalTime(), raw, "ISO 8601 with offset");
        return new Timestamp(null, raw, "no timezone in task XML; not converted");
    }

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex OffsetSuffix();
}
