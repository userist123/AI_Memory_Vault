using System.Globalization;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Memory;

/// <summary>
/// Master plan 6.6: the JSON output of Volatility 3 (<c>vol -r json … windows.pslist | psscan | netscan | cmdline | malfind</c>) run by the analyst on
/// a memory image outside the application. The plugin is recognised from the columns, not from the file name. The application never runs
/// Volatility and never reads the memory image itself: it reads what the tool printed, and says so on every row.
/// </summary>
public sealed class VolatilityJsonParser : EvidenceParserBase
{
    public const string SourceTypeName = "volatility_json";
    public const long MaxJsonBytes = 256L * 1024 * 1024;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "VolatilityJsonParser", Version = "1.0", Artifact = "Ieșirea JSON a Volatility 3 (procese, conexiuni, linii de comandă, regiuni suspecte din memorie)",
        SourceTypes = [SourceTypeName],
        FileNames = ["*pslist*.json", "*psscan*.json", "*pstree*.json", "*netscan*.json", "*netstat*.json", "*cmdline*.json", "*malfind*.json", "*volatility*.json"],
        Fingerprints = ["json"],
        SupportedOs = "imagini de memorie Windows analizate cu Volatility 3 (renderer json)",
        FormatVersions = ["Volatility 3 2.x, `-r json`: listă de obiecte cu coloanele pluginului și __children"],
        MaxInputBytes = MaxJsonBytes,
        Limitations =
        [
            "Aplicația nu rulează Volatility și nu citește imaginea de memorie: citește doar ce a scris unealta; corectitudinea depinde de simbolurile și versiunea folosite.",
            "Memoria este o fotografie a unui moment: un proces care s-a încheiat înainte de captură nu mai apare în pslist.",
            "psscan găsește și structuri rămase de la procese încheiate; un proces doar în psscan nu este automat un proces ascuns.",
            "malfind semnalează regiuni executabile și inscriptibile, care apar și la programe legitime (JIT, .NET, browsere).",
            "Orele sunt cele scrise de Volatility (UTC).",
        ],
        Status = ParserMaturity.Tested,
        Validation = "VolatilityJsonParserTests (ieșiri în formatul Volatility 3 pentru pslist, psscan, netscan, cmdline și malfind)",
    };

    private enum Plugin { Unknown, ProcessList, NetScan, CmdLine, Malfind }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(fs, new JsonDocumentOptions { MaxDepth = 64 });
        if (doc.RootElement.ValueKind != JsonValueKind.Array) { result.MalformedRecords++; return; }
        var rows = Flatten(doc.RootElement).ToList();
        var plugin = rows.Count == 0 ? Plugin.Unknown : Detect(rows[0]);
        if (plugin == Plugin.Unknown) { result.MalformedRecords++; return; }
        bool scan = Path.GetFileName(fullPath).Contains("psscan", StringComparison.OrdinalIgnoreCase);
        int i = 0;
        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            var e = plugin switch
            {
                Plugin.ProcessList => Process(r, item, i, scan),
                Plugin.NetScan => Net(r, item, i),
                Plugin.CmdLine => Cmd(r, item, i),
                _ => Malfind(r, item, i),
            };
            if (e is null) { result.MalformedRecords++; continue; }
            sink.Add(e);
            result.Records++;
        }
    }

    /// <summary>Rows and their __children (pstree), depth-first.</summary>
    private static IEnumerable<JsonElement> Flatten(JsonElement arr)
    {
        foreach (var r in arr.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            yield return r;
            if (r.TryGetProperty("__children", out var ch) && ch.ValueKind == JsonValueKind.Array)
                foreach (var c in Flatten(ch)) yield return c;
        }
    }

    private static Plugin Detect(JsonElement r) =>
        Has(r, "ForeignAddr") && Has(r, "LocalAddr") ? Plugin.NetScan
        : Has(r, "Protection") && (Has(r, "Start VPN") || Has(r, "Hexdump")) ? Plugin.Malfind
        : Has(r, "Args") && Has(r, "PID") ? Plugin.CmdLine
        : Has(r, "PPID") && Has(r, "ImageFileName") ? Plugin.ProcessList
        : Plugin.Unknown;

    private static bool Has(JsonElement r, string k) => r.TryGetProperty(k, out _);
    private static string S(JsonElement r, string k) => r.TryGetProperty(k, out var v) ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Number => v.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => "",
    } : "";
    private static int? I(JsonElement r, string k) => int.TryParse(S(r, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static Timestamp Time(string raw, string what) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? Timestamp.FromUtc(t.UtcDateTime, raw, $"Volatility 3 ({what}, UTC)") : Timestamp.Unknown(raw);

    private static TimelineEvent Base(EvidenceItem item, string plugin, int row, Timestamp time, string summary, string semantics,
        string process = "", int? pid = null, int? ppid = null, string remoteIp = "", int? remotePort = null) => new()
    {
        Time = time, Source = "Memory:" + plugin, EvidenceId = item.EvidenceId, Provider = "Volatility 3", EventId = plugin, Summary = summary,
        Process = process, Pid = pid, Ppid = ppid, RemoteIp = remoteIp, RemotePort = remotePort,
        TimeSemantics = semantics, TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
        Locator = $"row={row}", Fields = { ["Plugin"] = plugin },
    };

    private static TimelineEvent? Process(JsonElement r, EvidenceItem item, int row, bool scan)
    {
        var name = S(r, "ImageFileName");
        if (I(r, "PID") is not { } pid || name.Length == 0) return null;
        var plugin = scan ? "psscan" : "pslist";
        var exit = S(r, "ExitTime");
        var e = Base(item, plugin, row, Time(S(r, "CreateTime"), "CreateTime"),
            $"Proces în memorie ({plugin}): {name} PID {pid}, părinte {S(r, "PPID")}" + (exit.Length > 0 && exit != "N/A" ? $", încheiat la {exit}" : ""), "process created (memory)", name, pid, I(r, "PPID"));
        var p = e;
        p.Fields["ExitTime"] = exit; p.Fields["Offset"] = S(r, "Offset(V)") is { Length: > 0 } o ? o : S(r, "Offset(P)");
        p.Fields["SessionId"] = S(r, "SessionId"); p.Fields["Threads"] = S(r, "Threads");
        return p;
    }

    private static TimelineEvent? Net(JsonElement r, EvidenceItem item, int row)
    {
        if (I(r, "PID") is not { } pid) return null;
        var e = Base(item, "netscan", row, Time(S(r, "Created"), "Created"),
            $"Conexiune în memorie: {S(r, "Owner")} (PID {pid}) {S(r, "Proto")} {S(r, "LocalAddr")}:{S(r, "LocalPort")} → {S(r, "ForeignAddr")}:{S(r, "ForeignPort")} {S(r, "State")}".TrimEnd(),
            "connection created (memory)", S(r, "Owner"), pid, null, S(r, "ForeignAddr") is var f && f is not ("*" or "0.0.0.0" or "::") ? f : "", I(r, "ForeignPort"));
        var n = e;
        n.Fields["State"] = S(r, "State"); n.Fields["Proto"] = S(r, "Proto");
        return n;
    }

    private static TimelineEvent? Cmd(JsonElement r, EvidenceItem item, int row)
    {
        if (I(r, "PID") is not { } pid) return null;
        var args = S(r, "Args");
        var e = Base(item, "cmdline", row, Timestamp.Unknown(""), $"Linie de comandă în memorie: {S(r, "Process")} (PID {pid}): {args}", "command line at capture time", S(r, "Process"), pid);
        var c = e;
        c.Fields["CommandLine"] = args;
        return c;
    }

    private static TimelineEvent? Malfind(JsonElement r, EvidenceItem item, int row)
    {
        if (I(r, "PID") is not { } pid) return null;
        var hex = S(r, "Hexdump");
        bool mz = hex.TrimStart().StartsWith("4d 5a", StringComparison.OrdinalIgnoreCase);
        var e = Base(item, "malfind", row, Timestamp.Unknown(""),
            $"Regiune de memorie {S(r, "Protection")} în {S(r, "Process")} (PID {pid}) la {S(r, "Start VPN")}" + (mz ? ", începe cu antet de executabil (MZ)" : ""), "memory region at capture time", S(r, "Process"), pid);
        var m = e;
        m.Fields["Protection"] = S(r, "Protection"); m.Fields["StartVpn"] = S(r, "Start VPN"); m.Fields["MzHeader"] = mz ? "da" : "nu";
        return m;
    }

    /// <summary>
    /// Processes psscan found that pslist did not, among those not marked as ended: candidates for an unlinked (hidden) process. Not a verdict:
    /// psscan also finds leftover structures of ended processes, and a process can start or end between two plugin runs.
    /// </summary>
    public static IReadOnlyList<TimelineEvent> PsscanOnly(IReadOnlyList<TimelineEvent> timeline)
    {
        var listed = timeline.Where(e => e.Source == "Memory:pslist" && e.Pid is not null).Select(e => (e.Pid!.Value, e.Process.ToLowerInvariant())).ToHashSet();
        if (listed.Count == 0) return [];
        return timeline.Where(e => e.Source == "Memory:psscan" && e.Pid is { } p && !listed.Contains((p, e.Process.ToLowerInvariant()))
                                   && e.Fields.GetValueOrDefault("ExitTime", "") is "" or "N/A").ToList();
    }
}
