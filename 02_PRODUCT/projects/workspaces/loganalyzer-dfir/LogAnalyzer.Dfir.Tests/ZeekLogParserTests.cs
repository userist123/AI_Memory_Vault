using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.5: Zeek logs (TSV and JSON lines) read into the timeline through the parser registry.</summary>
public sealed class ZeekLogParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_zeek_" + Guid.NewGuid().ToString("N"));
    public ZeekLogParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static string Tsv(string path, string fields, string[] rows, bool close = true)
    {
        var h = "#separator \\x09\n#set_separator\t,\n#empty_field\t(empty)\n#unset_field\t-\n#path\t" + path + "\n#open\t2026-11-07-08-00-00\n#fields\t" + fields + "\n#types\tx\n";
        return h + string.Join("\n", rows.Select(r => r.Replace('|', '\t'))) + "\n" + (close ? "#close\t2026-11-07-09-00-00\n" : "");
    }

    private string F(string name, string content) { var p = Path.Combine(_dir, name); File.WriteAllText(p, content); return p; }

    private const string ConnFields = "ts\tuid\tid.orig_h\tid.orig_p\tid.resp_h\tid.resp_p\tproto\tservice\tduration\torig_bytes\tresp_bytes\tconn_state";
    private string Conn(bool close = true, params string[] extra) => Tsv("conn", ConnFields,
        ["1730966400.123456|CAbc1|10.66.0.21|50123|203.0.113.9|443|tcp|ssl|1.5|100|2000|SF",
         "1730966401.000001|CAbc2|10.66.0.21|50124|203.0.113.10|80|tcp|-|-|-|-|S0", ..extra], close);

    private InvestigationResultView Run(params string[] files)
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases_" + Guid.NewGuid().ToString("N")[..6]), "net", TestScopes.Valid());
        var items = InvestigationPipeline.Import(ws, files);
        var routed = items.Select(e => WindowsParsers.Registry.Select(e, ws.FullPath(e.StoredPath)).Parsers.Any(p => p is ZeekLogParser)).ToList();
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        return new(r.Timeline, r.Parsing, routed);
    }

    private sealed record InvestigationResultView(IReadOnlyList<LogAnalyzer.Dfir.Model.TimelineEvent> Timeline, IReadOnlyList<LogAnalyzer.Dfir.Model.ParseResult> Parsing, IReadOnlyList<bool> Routed);

    [Fact]
    public void Tsv_conn_dns_http_ssl_are_routed_by_the_registry_and_reach_the_timeline()
    {
        var dns = Tsv("dns", "ts\tuid\tid.orig_h\tid.orig_p\tid.resp_h\tid.resp_p\tproto\tquery\tqtype_name\trcode_name\tanswers",
            ["1730966402.5|CD1|10.66.0.21|5353|10.66.0.1|53|udp|evil.example|A|NOERROR|203.0.113.9,203.0.113.10"]);
        var http = Tsv("http", "ts\tuid\tid.orig_h\tid.orig_p\tid.resp_h\tid.resp_p\tmethod\thost\turi\tuser_agent\tstatus_code",
            ["1730966403.25|CH1|10.66.0.21|50200|203.0.113.9|80|GET|evil.example|/a.exe|curl/8|200"]);
        var ssl = Tsv("ssl", "ts\tuid\tid.orig_h\tid.orig_p\tid.resp_h\tid.resp_p\tversion\tcipher\tserver_name\tvalidation_status",
            ["1730966404.75|CS1|10.66.0.21|50300|203.0.113.9|443|TLSv13|TLS_AES_128_GCM_SHA256|evil.example|self signed certificate"]);
        var v = Run(F("conn.log", Conn()), F("dns.log", dns), F("http.log", http), F("ssl.log", ssl));

        Assert.All(v.Routed, Assert.True);
        Assert.Equal(2, v.Timeline.Count(t => t.Source == "Zeek:conn"));
        var c = Assert.Single(v.Timeline, t => t.Source == "Zeek:conn" && t.Fields["Uid"] == "CAbc1");
        Assert.Equal(("203.0.113.9", 443, "Zeek", "conn"), (c.RemoteIp, c.RemotePort ?? 0, c.Provider, c.EventId));
        Assert.Equal(("10.66.0.21", "50123", "tcp", "ssl"), (c.Fields["OrigH"], c.Fields["OrigP"], c.Fields["Proto"], c.Fields["Service"]));
        var d = Assert.Single(v.Timeline, t => t.Source == "Zeek:dns");
        Assert.Equal(("evil.example", "203.0.113.9,203.0.113.10"), (d.Fields["Query"], d.Fields["Answers"]));
        var h = Assert.Single(v.Timeline, t => t.Source == "Zeek:http");
        Assert.Equal(("evil.example", "/a.exe", "curl/8"), (h.Fields["Host"], h.Fields["Uri"], h.Fields["UserAgent"]));
        var s = Assert.Single(v.Timeline, t => t.Source == "Zeek:ssl");
        Assert.Equal("evil.example", s.Fields["ServerName"]);
        Assert.StartsWith("line=", s.Locator);
    }

    [Fact]
    public void Flow_timestamp_keeps_microsecond_precision()
    {
        var v = Run(F("conn.log", Conn()));
        var c = Assert.Single(v.Timeline, t => t.Fields["Uid"] == "CAbc1");
        Assert.Equal("1730966400.123456", c.Time.Raw);
        Assert.Equal(new DateTime(2024, 11, 7, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234560), c.Time.Utc!.Value.UtcDateTime);
        Assert.Equal(1234560, c.Time.Utc.Value.UtcDateTime.Ticks % TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void Unset_and_empty_values_become_empty_strings()
    {
        var v = Run(F("conn.log", Conn()));
        var c = Assert.Single(v.Timeline, t => t.Fields["Uid"] == "CAbc2");
        Assert.Equal(("", "", "", "S0"), (c.Fields["Service"], c.Fields["Duration"], c.Fields["OrigBytes"], c.Fields["ConnState"]));
    }

    [Fact]
    public void A_tsv_log_without_close_is_reported_as_possibly_truncated_and_one_with_close_is_not()
    {
        var open = Run(F("conn.log", Conn(close: false)));
        Assert.Contains(open.Parsing, p => p.Parser == "ZeekLogParser" && p.Gaps.Any(g => g.Reason.Contains("#close")));
        Assert.Equal(2, open.Timeline.Count(t => t.Source == "Zeek:conn"));

        var closed = Run(F("conn2.log", Conn()));
        Assert.DoesNotContain(closed.Parsing, p => p.Parser == "ZeekLogParser" && p.Gaps.Count > 0);
    }

    [Fact]
    public void Json_lines_conn_is_read()
    {
        var json = """
            {"ts":1730966400.123456,"uid":"CJ1","id.orig_h":"10.66.0.21","id.orig_p":50123,"id.resp_h":"203.0.113.9","id.resp_p":443,"proto":"tcp","service":"ssl","duration":1.5,"orig_bytes":100,"resp_bytes":2000,"conn_state":"SF"}
            {"ts":1730966401.5,"uid":"CJ2","id.orig_h":"10.66.0.21","id.orig_p":50124,"id.resp_h":"203.0.113.10","id.resp_p":80,"proto":"tcp","conn_state":"S0"}
            """;
        var v = Run(F("conn.log", json));
        Assert.All(v.Routed, Assert.True);
        Assert.Equal(2, v.Timeline.Count(t => t.Source == "Zeek:conn"));
        var c = Assert.Single(v.Timeline, t => t.Fields["Uid"] == "CJ1");
        Assert.Equal(("203.0.113.9", 443, "ssl"), (c.RemoteIp, c.RemotePort ?? 0, c.Fields["Service"]));
        Assert.Equal(1234560, c.Time.Utc!.Value.UtcDateTime.Ticks % TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void A_plain_log_that_is_not_zeek_is_not_claimed_and_gives_no_zeek_events()
    {
        var v = Run(F("conn.log", "2026-11-07 08:00:00 INFO service started\n2026-11-07 08:00:01 WARN disk low\n"));
        Assert.DoesNotContain(v.Routed, x => x);
        Assert.DoesNotContain(v.Timeline, t => t.Source.StartsWith("Zeek:", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_without_zeek_columns_gives_no_events_and_is_counted_malformed()
    {
        var v = Run(F("dns.log", """{"level":"info","msg2":"hello"}""" + "\n"));
        Assert.DoesNotContain(v.Timeline, t => t.Source.StartsWith("Zeek:", StringComparison.Ordinal));
        Assert.Contains(v.Parsing, p => p.Parser == "ZeekLogParser" && p.MalformedRecords > 0);
    }

    [Fact]
    public void Malformed_lines_are_counted_and_good_lines_kept()
    {
        var content = Conn(true, "1730966405.0|CBad|only|three");
        var v = Run(F("conn.log", content));
        Assert.Equal(2, v.Timeline.Count(t => t.Source == "Zeek:conn"));
        Assert.Contains(v.Parsing, p => p.Parser == "ZeekLogParser" && p.MalformedRecords == 1 && p.Records == 2);
    }

    [Fact]
    public void An_unknown_tsv_path_emits_nothing_and_is_malformed()
    {
        var weird = Tsv("weird", "ts\tuid", ["1730966400.1|X1"]);
        var v = Run(F("conn_weird.log", weird));
        Assert.DoesNotContain(v.Timeline, t => t.Source.StartsWith("Zeek:", StringComparison.Ordinal));
        Assert.Contains(v.Parsing, p => p.Parser == "ZeekLogParser" && p.MalformedRecords > 0);
    }
}
