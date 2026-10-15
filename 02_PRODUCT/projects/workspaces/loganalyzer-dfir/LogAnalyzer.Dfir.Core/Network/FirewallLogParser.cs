using System.Globalization;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Network;

/// <summary>
/// Windows Firewall text log (<c>pfirewall.log</c>, W3C-like: "#Fields: date time action protocol src-ip dst-ip src-port dst-port size … path pid"):
/// one timeline row per allowed or dropped connection, with the addresses, ports and direction. The times are LOCAL time of the station that wrote the log
/// ("#Time Format: Local"); they are converted with the time zone recorded on the evidence item, and the conversion is named on every row.
/// Read as text only; nothing is sent.
/// </summary>
public sealed class FirewallLogParser : EvidenceParserBase
{
    public const string SourceName = "Firewall:pfirewall.log";
    public const string SourceTypeName = "firewall_log";

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "FirewallLogParser", Version = "1.0", Artifact = "Jurnalul text al firewall-ului Windows (pfirewall.log)",
        SourceTypes = [SourceTypeName], FileNames = ["pfirewall.log", "pfirewall.log.old"], Fingerprints = ["pfirewall"],
        SupportedOs = "Oricare (text; jurnalul e scris de Windows Firewall)",
        FormatVersions = ["#Version 1.5 (Windows 7 – Windows 11)"],
        Limitations =
        [
            "Jurnalul există doar dacă jurnalizarea firewall-ului este pornită pe profil (LogSuccessfulConnections / LogDroppedPackets); implicit este oprită.",
            "Fișierul este circular (implicit 4 MB): rândurile vechi se pierd; pfirewall.log.old păstrează doar generația anterioară.",
            "Arată adrese și porturi, nu conținutul și nu numele programului (coloana path / pid există doar pe versiunile noi).",
        ],
        Status = ParserMaturity.Tested,
        Validation = "FirewallLogParserTests (rânduri sintetice în formatul documentat)",
    };

    private static readonly string[] DefaultFields = ["date", "time", "action", "protocol", "src-ip", "dst-ip", "src-port", "dst-port", "size", "tcpflags", "tcpsyn", "tcpack", "tcpwin", "icmptype", "icmpcode", "info", "path", "pid"];

    /// <summary>True when the text starts like a firewall log ("#Version:" then "#Software: Microsoft Windows Firewall" in the first lines).</summary>
    public static bool LooksLikeFirewallLog(string head) =>
        head.TrimStart('﻿', ' ', '\r', '\n').StartsWith("#Version:", StringComparison.Ordinal) && head.Contains("Microsoft Windows Firewall", StringComparison.OrdinalIgnoreCase);

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
        while (sr.ReadLine() is { } l)
        {
            ct.ThrowIfCancellationRequested();
            line++;
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
