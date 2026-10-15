using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Network;

/// <summary>
/// Windows Firewall text log (<c>pfirewall.log</c>, W3C-like: "#Fields: date time action protocol src-ip dst-ip src-port dst-port size … path pid"):
/// one timeline row per allowed or dropped connection, with the addresses, ports and direction. The times are LOCAL time of the station that wrote the log
/// ("#Time Format: Local"); they are converted with the time zone recorded on the evidence item, and the conversion is named on every row.
/// Read as text only, bounded in size (<see cref="MaxLogBytes"/>) and in line length (<see cref="MaxLineChars"/>); nothing is sent.
/// </summary>
public sealed class FirewallLogParser : EvidenceParserBase
{
    public const string SourceName = "Firewall:pfirewall.log";
    public const string SourceTypeName = "firewall_log";
    /// <summary>Largest firewall log copied or parsed (Windows' own maximum is 32 MB; the default 4 MB).</summary>
    public const long MaxLogBytes = 64L * 1024 * 1024;
    /// <summary>Longest line read; a longer one is skipped and counted as malformed (a real line is about 100 characters).</summary>
    public const int MaxLineChars = 4096;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "FirewallLogParser", Version = "1.0", Artifact = "Jurnalul text al firewall-ului Windows (pfirewall.log)",
        SourceTypes = [SourceTypeName], FileNames = ["pfirewall.log", "pfirewall.log.old"], Fingerprints = ["pfirewall"],
        SupportedOs = "Oricare (text; jurnalul e scris de Windows Firewall)",
        FormatVersions = ["#Version 1.5 (Windows 7 – Windows 11)"],
        MaxInputBytes = MaxLogBytes,
        Limitations =
        [
            "Jurnalul există doar dacă jurnalizarea firewall-ului este pornită pe profil (LogSuccessfulConnections / LogDroppedPackets); implicit este oprită.",
            "Fișierul este circular (implicit 4 MB): rândurile vechi se pierd; pfirewall.log.old păstrează doar generația anterioară.",
            "Arată adrese și porturi, nu conținutul și nu numele programului (coloana path / pid există doar pe versiunile noi).",
        ],
        Status = ParserMaturity.Tested,
        Validation = "StandaloneNetworkEvidenceTests (rânduri sintetice în formatul documentat)",
    };

    private static readonly string[] DefaultFields = ["date", "time", "action", "protocol", "src-ip", "dst-ip", "src-port", "dst-port", "size", "tcpflags", "tcpsyn", "tcpack", "tcpwin", "icmptype", "icmpcode", "info", "path", "pid"];

    /// <summary>True when the text starts like a firewall log ("#Version:" then "#Software: Microsoft Windows Firewall" in the first lines).</summary>
    public static bool LooksLikeFirewallLog(string head) =>
        head.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("#Version:", StringComparison.Ordinal) && head.Contains("Microsoft Windows Firewall", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Why a configured log path must not be read ("" = it may): only a fully qualified local path on a FIXED drive is accepted. UNC paths (\\server\share,
    /// \\?\UNC\…), device paths (\\?\…, \\.\…), relative paths and paths on a network, removable or unknown drive are refused, so reading the
    /// configured log never contacts another machine. <paramref name="driveType"/> is asked only for a plain "X:\" root.
    /// </summary>
    public static string RejectPath(string path, Func<string, DriveType> driveType)
    {
        const string prefix = "cale jurnal firewall nepermisă";
        if (string.IsNullOrWhiteSpace(path)) return $"{prefix}: calea este goală";
        var p = path.Trim();
        if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal))
            return $"{prefix}: „{p}” este o cale de rețea sau de dispozitiv (UNC, \\\\?\\, \\\\.\\); se citesc doar fișiere de pe un disc local fix";
        if (p.Length < 3 || !char.IsAsciiLetter(p[0]) || p[1] != ':' || p[2] is not ('\\' or '/') || !Path.IsPathFullyQualified(p))
            return $"{prefix}: „{p}” nu este o cale absolută locală (literă de disc)";
        var type = driveType(p[..3]);
        return type == DriveType.Fixed ? "" : $"{prefix}: „{p}” este pe o unitate de tip {type} (unitate de rețea, amovibilă sau necunoscută); se citesc doar fișiere de pe un disc local fix";
    }

    /// <summary>Reads one line of at most <paramref name="max"/> characters; a longer line is consumed to its end and returned as null with <paramref name="tooLong"/> set.</summary>
    private static string? ReadBoundedLine(TextReader r, int max, out bool tooLong)
    {
        tooLong = false;
        var sb = new StringBuilder();
        int c;
        bool any = false;
        while ((c = r.Read()) >= 0)
        {
            any = true;
            if (c == '\n') break;
            if (c == '\r') continue;
            if (sb.Length < max) sb.Append((char)c); else tooLong = true;
        }
        if (!any) return null;
        return tooLong ? "" : sb.ToString();
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        TimeZoneInfo zone;
        string zoneNote;
        try { zone = item.Timezone.Length > 0 ? TimeZoneInfo.FindSystemTimeZoneById(item.Timezone) : TimeZoneInfo.Utc; zoneNote = item.Timezone.Length > 0 ? item.Timezone : "UTC (fusul stației nu este pe probă)"; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = TimeZoneInfo.Utc; zoneNote = $"UTC (fusul „{item.Timezone}” nu este cunoscut)"; }
        var fields = DefaultFields;
        bool localTime = true;
        long line = 0;
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        while (ReadBoundedLine(sr, MaxLineChars, out var tooLong) is { } l)
        {
            ct.ThrowIfCancellationRequested();
            line++;
            if (tooLong) { result.MalformedRecords++; continue; }
            if (l.Length == 0) continue;
            if (l[0] == '#')
            {
                if (l.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase)) fields = l[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                else if (l.StartsWith("#Time Format:", StringComparison.OrdinalIgnoreCase)) localTime = !l.Contains("UTC", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6) { result.MalformedRecords++; continue; }
            string V(string name) { int i = Array.IndexOf(fields, name); return i >= 0 && i < parts.Length && parts[i] != "-" ? parts[i] : ""; }
            if (!DateTime.TryParseExact(V("date") + " " + V("time"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) { result.MalformedRecords++; continue; }
            var utc = localTime ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone) : DateTime.SpecifyKind(t, DateTimeKind.Utc);
            var action = V("action"); var dir = V("path");
            bool inbound = dir.Equals("RECEIVE", StringComparison.OrdinalIgnoreCase);
            var remote = inbound ? V("src-ip") : V("dst-ip");
            var e = new TimelineEvent
            {
                RemoteIp = remote, RemotePort = int.TryParse(inbound ? V("src-port") : V("dst-port"), NumberStyles.None, CultureInfo.InvariantCulture, out var rp) ? rp : null,
                Pid = int.TryParse(V("pid"), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : null,
                Time = Timestamp.FromUtc(utc, V("date") + " " + V("time"), localTime ? $"ora locală a stației ({zoneNote}) → UTC" : "UTC"),
                Source = SourceName, EvidenceId = item.EvidenceId, Provider = "Windows Firewall", EventId = action,
                Summary = $"Firewall {action} {V("protocol")} {V("src-ip")}:{V("src-port")} → {V("dst-ip")}:{V("dst-port")}{(dir.Length > 0 ? " (" + dir + ")" : "")}",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $"line={line}",
            };
            foreach (var f in new[] { "action", "protocol", "src-ip", "dst-ip", "src-port", "dst-port", "size", "path", "pid" })
                if (V(f) is { Length: > 0 } v) e.Fields[f] = v;
            sink.Add(e);
            result.Records++;
        }
    }
}
