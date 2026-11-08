using System.Globalization;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Network;

/// <summary>
/// Master plan 6.5: Zeek logs (<c>conn.log</c>, <c>dns.log</c>, <c>http.log</c>, <c>ssl.log</c>, <c>notice.log</c>) produced by the analyst's Zeek sensor, in the
/// classic tab-separated format (header lines starting with '#') or as JSON lines (<c>LogAscii::use_json=T</c>). The kind of log is taken from <c>#path</c>
/// (TSV) or from the columns (JSON); an unknown kind is counted as malformed and nothing is emitted. Zeek writes <c>ts</c> as UTC epoch seconds with a fraction:
/// it is converted without losing the microseconds and the raw text is kept. The application never runs Zeek and never reads the capture itself.
/// </summary>
public sealed class ZeekLogParser : EvidenceParserBase
{
    public const string SourceTypeName = "zeek_log";
    /// <summary>Fingerprint written by <c>EvidenceFingerprint</c> for a TSV log that starts with the "#separator" header line.</summary>
    public const string TsvFingerprint = "zeek_tsv";
    public const long MaxLogBytes = 512L * 1024 * 1024;
    /// <summary>Longest line read; a longer one is skipped and counted as malformed.</summary>
    public const int MaxLineChars = 64 * 1024;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "ZeekLogParser", Version = "1.0", Artifact = "Jurnalele Zeek (conn, dns, http, ssl, notice), format TSV sau JSON pe linii",
        SourceTypes = [SourceTypeName],
        FileNames = ["conn*.log", "dns*.log", "http*.log", "ssl*.log", "notice*.log"],
        Fingerprints = [TsvFingerprint, "json"],
        SupportedOs = "Oricare (jurnale text scrise de un senzor Zeek)",
        FormatVersions = ["Zeek 4.x – 6.x: TSV cu antet #separator / #fields / #close, sau JSON pe linii"],
        MaxInputBytes = MaxLogBytes,
        Limitations =
        [
            "Zeek vede doar rețeaua: aplicația nu poate atribui o conexiune unui proces (nu există PID) și nici unui utilizator.",
            "Conținutul TLS nu este decodat: se citesc doar metadatele handshake-ului pe care le-a scris Zeek (nume server, versiune, cifru, validare).",
            "Din jurnale nu se văd golurile de captură: o perioadă fără trafic și o perioadă în care senzorul nu a ascultat arată la fel.",
            "Un jurnal TSV fără linia #close poate fi trunchiat sau încă în scriere.",
            "Orele sunt epoch UTC scrise de Zeek; o conexiune este datată la începutul ei, iar durata și octeții apar doar dacă Zeek le-a scris.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "ZeekLogParserTests (jurnale conn, dns, http, ssl în TSV și JSON pe linii, sintetice, în formatul documentat de Zeek)",
    };

    private enum Kind { Unknown, Conn, Dns, Http, Ssl, Notice }

    /// <summary>True when the head of a file starts like a Zeek TSV log ("#separator" first).</summary>
    public static bool LooksLikeZeekTsv(string head) =>
        head.TrimStart('﻿', ' ', '\r', '\n').StartsWith("#separator", StringComparison.Ordinal);

    private static Kind FromPath(string path) => path.Trim().ToLowerInvariant() switch
    {
        "conn" => Kind.Conn, "dns" => Kind.Dns, "http" => Kind.Http, "ssl" => Kind.Ssl, "notice" => Kind.Notice, _ => Kind.Unknown,
    };

    private static Kind FromColumns(IReadOnlyDictionary<string, string> r)
    {
        if (r.ContainsKey("note") && r.ContainsKey("msg") && r.ContainsKey("ts")) return Kind.Notice;
        if (!(r.ContainsKey("uid") && r.ContainsKey("id.orig_h"))) return Kind.Unknown;
        if (r.ContainsKey("query")) return Kind.Dns;
        if (r.ContainsKey("method") || r.ContainsKey("status_code")) return Kind.Http;
        if (r.ContainsKey("server_name") || r.ContainsKey("cipher")) return Kind.Ssl;
        if (r.ContainsKey("conn_state") || r.ContainsKey("proto")) return Kind.Conn;
        return Kind.Unknown;
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        char sep = '\t';
        string empty = "(empty)", unset = "-";
        string[] cols = [];
        var kind = Kind.Unknown;
        bool tsvHeader = false, sawClose = false, tsvData = false, unknownReported = false;
        long line = 0;
        while (FirewallLogParser.ReadBoundedLine(sr, MaxLineChars, out var tooLong) is { } l)
        {
            ct.ThrowIfCancellationRequested();
            line++;
            if (tooLong) { result.MalformedRecords++; continue; }
            if (line == 1 && l.Length > 0 && l[0] == '﻿') l = l[1..];
            if (l.Length == 0) continue;

            if (l[0] == '#')
            {
                if (l.StartsWith("#separator", StringComparison.Ordinal))
                {
                    // The separator is announced as a hex escape ("\x09") on the first header line, after a space.
                    sep = DecodeSep(l.Length > 10 ? l[10..].Trim() : "");
                    tsvHeader = true; sawClose = false; kind = Kind.Unknown; cols = []; unknownReported = false;
                    continue;
                }
                var parts = l.Split(sep, 2);
                var key = parts[0];
                var val = parts.Length > 1 ? parts[1] : "";
                if (key == "#empty_field") empty = val;
                else if (key == "#unset_field") unset = val;
                else if (key == "#path") { kind = FromPath(val); unknownReported = false; }
                else if (key == "#fields") cols = val.Split(sep);
                else if (key == "#close") sawClose = true;
                continue;
            }

            if (l[0] == '{')
            {
                if (ParseJsonLine(l) is not { } row) { result.MalformedRecords++; continue; }
                var k = FromColumns(row);
                if (k == Kind.Unknown) { result.MalformedRecords++; continue; }
                Emit(k, row, item, line, sink, result);
                continue;
            }

            // A data row of a TSV log needs the header that names its columns and its kind.
            if (!tsvHeader || cols.Length == 0) { result.MalformedRecords++; continue; }
            tsvData = true;
            if (kind == Kind.Unknown)
            {
                if (!unknownReported) { result.MalformedRecords++; unknownReported = true; }
                continue;
            }
            var cells = l.Split(sep);
            if (cells.Length != cols.Length) { result.MalformedRecords++; continue; }
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < cols.Length; i++)
                dict[cols[i]] = cells[i] == unset || cells[i] == empty ? "" : cells[i];
            Emit(kind, dict, item, line, sink, result);
        }

        if (tsvHeader && !sawClose && tsvData)
        {
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial,
                "Jurnalul Zeek TSV nu se încheie cu linia #close: poate fi trunchiat sau încă în scriere",
                "Ultimele conexiuni sau interogări pot lipsi din jurnal",
                "Jurnalul arhivat (rotit) de Zeek, care se încheie cu #close, sau captura originală", "Posibil, dacă senzorul păstrează jurnalele rotite"));
        }
    }

    private static char DecodeSep(string raw)
    {
        if (raw.Length >= 4 && raw.StartsWith("\\x", StringComparison.Ordinal)
            && int.TryParse(raw.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c)) return (char)c;
        return raw.Length == 1 ? raw[0] : '\t';
    }

    private static Dictionary<string, string>? ParseJsonLine(string l)
    {
        try
        {
            using var doc = JsonDocument.Parse(l, new JsonDocumentOptions { MaxDepth = 16 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject())
                d[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.Number => p.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Array => string.Join(",", p.Value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText())),
                    _ => "",
                };
            return d;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Zeek epoch seconds with fraction as a UTC time, to the tick, without going through a double. Unknown when not a plausible time.</summary>
    internal static Timestamp Time(string raw)
    {
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0 && d < 253402300799m)
            return Timestamp.FromUtc(DateTime.UnixEpoch.AddTicks((long)(d * 10_000_000m)), raw, "Zeek ts (epoch UTC)");
        return Timestamp.Unknown(raw);
    }

    private static string G(IReadOnlyDictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? v : "";

    private void Emit(Kind kind, IReadOnlyDictionary<string, string> r, EvidenceItem item, long line, IEventSink sink, ParseResult result)
    {
        var time = Time(G(r, "ts"));
        if (!time.IsKnown) { result.MalformedRecords++; return; }
        var name = kind.ToString().ToLowerInvariant();
        bool notice = kind == Kind.Notice;
        var orig = G(r, "id.orig_h");
        var resp = notice ? G(r, "dst") : G(r, "id.resp_h");
        var respPortText = notice ? G(r, "p") : G(r, "id.resp_p");
        int? respPort = int.TryParse(respPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rp) ? rp : null;
        if (!notice && (G(r, "uid").Length == 0 || orig.Length == 0)) { result.MalformedRecords++; return; }

        string summary, semantics;
        switch (kind)
        {
            case Kind.Conn:
                summary = $"Conexiune de rețea (Zeek): {orig}:{G(r, "id.orig_p")} → {resp}:{G(r, "id.resp_p")} {G(r, "proto")}"
                          + (G(r, "service").Length > 0 ? $" ({G(r, "service")})" : "") + (G(r, "conn_state").Length > 0 ? $", stare {G(r, "conn_state")}" : "")
                          + (G(r, "duration").Length > 0 ? $", durată {G(r, "duration")} s" : "");
                semantics = "connection start (Zeek)"; break;
            case Kind.Dns:
                summary = $"Interogare DNS (Zeek): {orig} a cerut {G(r, "query")} ({G(r, "qtype_name")})"
                          + (G(r, "answers").Length > 0 ? $", răspuns {G(r, "answers")}" : "") + (G(r, "rcode_name").Length > 0 ? $", cod {G(r, "rcode_name")}" : "");
                semantics = "DNS query seen (Zeek)"; break;
            case Kind.Http:
                summary = $"Cerere HTTP (Zeek): {orig} → {G(r, "method")} {G(r, "host")}{G(r, "uri")}"
                          + (G(r, "status_code").Length > 0 ? $", răspuns {G(r, "status_code")}" : "");
                semantics = "HTTP request seen (Zeek)"; break;
            case Kind.Ssl:
                summary = $"Handshake TLS (Zeek): {orig} → {(G(r, "server_name").Length > 0 ? G(r, "server_name") : resp)}"
                          + (G(r, "version").Length > 0 ? $", {G(r, "version")}" : "") + (G(r, "validation_status").Length > 0 ? $", validare {G(r, "validation_status")}" : "");
                semantics = "TLS handshake seen (Zeek)"; break;
            default:
                summary = $"Notificare Zeek: {G(r, "note")}: {G(r, "msg")}";
                semantics = "notice raised (Zeek)"; break;
        }

        var e = new TimelineEvent
        {
            Time = time, Source = "Zeek:" + name, EvidenceId = item.EvidenceId, Provider = "Zeek", EventId = name, Summary = summary,
            RemoteIp = resp, RemotePort = respPort,
            TimeSemantics = semantics, TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
            Locator = $"line={line}",
        };
        e.Fields["Uid"] = G(r, "uid"); e.Fields["OrigH"] = orig; e.Fields["OrigP"] = G(r, "id.orig_p");
        e.Fields["RespH"] = resp; e.Fields["RespP"] = respPortText;
        e.Fields["Proto"] = G(r, "proto"); e.Fields["Service"] = G(r, "service");
        switch (kind)
        {
            case Kind.Conn:
                e.Fields["Duration"] = G(r, "duration"); e.Fields["OrigBytes"] = G(r, "orig_bytes"); e.Fields["RespBytes"] = G(r, "resp_bytes"); e.Fields["ConnState"] = G(r, "conn_state");
                break;
            case Kind.Dns:
                e.Fields["Query"] = G(r, "query"); e.Fields["QType"] = G(r, "qtype_name"); e.Fields["Answers"] = G(r, "answers"); e.Fields["RCode"] = G(r, "rcode_name");
                break;
            case Kind.Http:
                e.Fields["Host"] = G(r, "host"); e.Fields["Uri"] = G(r, "uri"); e.Fields["Method"] = G(r, "method"); e.Fields["UserAgent"] = G(r, "user_agent"); e.Fields["StatusCode"] = G(r, "status_code");
                break;
            case Kind.Ssl:
                e.Fields["ServerName"] = G(r, "server_name"); e.Fields["Version"] = G(r, "version"); e.Fields["Cipher"] = G(r, "cipher");
                e.Fields["Ja3"] = G(r, "ja3"); e.Fields["ValidationStatus"] = G(r, "validation_status");
                break;
            default:
                e.Fields["Note"] = G(r, "note"); e.Fields["Msg"] = G(r, "msg");
                break;
        }
        sink.Add(e);
        result.Records++;
    }
}
