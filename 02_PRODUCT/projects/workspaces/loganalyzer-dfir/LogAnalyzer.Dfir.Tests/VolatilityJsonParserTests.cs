using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.6: Volatility 3 JSON output read into the timeline through the parser registry, and the psscan-only candidates.</summary>
public sealed class VolatilityJsonParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_vol_" + Guid.NewGuid().ToString("N"));
    public VolatilityJsonParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private const string PsList = """
        [ { "PID": 4, "PPID": 0, "ImageFileName": "System", "Offset(V)": 123, "Threads": 150, "Handles": null, "SessionId": null, "Wow64": false,
            "CreateTime": "2026-11-07T08:00:00+00:00", "ExitTime": null, "File output": "Disabled",
            "__children": [ { "PID": 640, "PPID": 4, "ImageFileName": "smss.exe", "Offset(V)": 456, "Threads": 2, "Handles": null, "SessionId": null, "Wow64": false,
                              "CreateTime": "2026-11-07T08:00:01+00:00", "ExitTime": null, "File output": "Disabled", "__children": [] } ] } ]
        """;
    private const string PsScan = """
        [ { "PID": 4, "PPID": 0, "ImageFileName": "System", "Offset(V)": 123, "Threads": 150, "CreateTime": "2026-11-07T08:00:00+00:00", "ExitTime": null, "__children": [] },
          { "PID": 640, "PPID": 4, "ImageFileName": "smss.exe", "Offset(V)": 456, "Threads": 2, "CreateTime": "2026-11-07T08:00:01+00:00", "ExitTime": null, "__children": [] },
          { "PID": 6666, "PPID": 640, "ImageFileName": "updsvc.exe", "Offset(V)": 789, "Threads": 3, "CreateTime": "2026-11-07T09:30:00+00:00", "ExitTime": null, "__children": [] },
          { "PID": 7000, "PPID": 640, "ImageFileName": "old.exe", "Offset(V)": 790, "Threads": 0, "CreateTime": "2026-11-07T09:00:00+00:00", "ExitTime": "2026-11-07T09:01:00+00:00", "__children": [] } ]
        """;
    private const string NetScan = """
        [ { "Offset": 1, "Proto": "TCPv4", "LocalAddr": "10.66.0.21", "LocalPort": 50123, "ForeignAddr": "10.66.0.10", "ForeignPort": 445, "State": "ESTABLISHED",
            "PID": 6666, "Owner": "updsvc.exe", "Created": "2026-11-07T09:31:00+00:00", "__children": [] } ]
        """;
    private const string Malfind = """
        [ { "PID": 6666, "Process": "updsvc.exe", "Start VPN": "0x1f0000", "End VPN": "0x1fffff", "Tag": "VadS", "Protection": "PAGE_EXECUTE_READWRITE",
            "CommitCharge": 16, "PrivateMemory": 1, "File output": "Disabled", "Hexdump": "4d 5a 90 00 03 00 00 00", "Disasm": "", "__children": [] } ]
        """;
    private const string CmdLine = """[ { "PID": 6666, "Process": "updsvc.exe", "Args": "C:\\Users\\Public\\updsvc.exe -svc", "__children": [] } ]""";

    private string F(string name, string content) { var p = Path.Combine(_dir, name); File.WriteAllText(p, content); return p; }

    [Fact]
    public void The_registry_routes_volatility_json_to_the_parser_and_the_pipeline_puts_it_in_the_timeline()
    {
        var files = new[] { F("pslist.json", PsList), F("psscan.json", PsScan), F("netscan.json", NetScan), F("malfind.json", Malfind), F("cmdline.json", CmdLine) };
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "mem", TestScopes.Valid());
        var items = InvestigationPipeline.Import(ws, files);
        foreach (var e in items)
            Assert.Contains(WindowsParsers.Registry.Select(e, ws.FullPath(e.StoredPath)).Parsers, p => p is VolatilityJsonParser);

        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        Assert.Equal(2, r.Timeline.Count(t => t.Source == "Memory:pslist"));           // __children are read
        Assert.Equal(4, r.Timeline.Count(t => t.Source == "Memory:psscan"));
        var conn = Assert.Single(r.Timeline, t => t.Source == "Memory:netscan");
        Assert.Equal(("10.66.0.10", 445, 6666), (conn.RemoteIp, conn.RemotePort ?? 0, conn.Pid ?? 0));
        Assert.Contains(r.Timeline, t => t.Source == "Memory:malfind" && t.Fields["MzHeader"] == "da" && t.Summary.Contains("PAGE_EXECUTE_READWRITE"));
        Assert.Contains(r.Timeline, t => t.Source == "Memory:cmdline" && t.Fields["CommandLine"].Contains("-svc"));

        var hidden = VolatilityJsonParser.PsscanOnly(r.Timeline);
        Assert.Equal("updsvc.exe", Assert.Single(hidden).Process);                       // the ended old.exe is not a candidate
    }

    [Fact]
    public void A_json_that_is_not_volatility_output_is_not_read_as_if_it_were()
    {
        var p = F("pslist.json", """[ { "name": "x", "value": 1 } ]""");
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases2"), "mem2", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [p]);
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        Assert.DoesNotContain(r.Timeline, t => t.Source.StartsWith("Memory:", StringComparison.Ordinal));
        Assert.Contains(r.Parsing, x => x.Parser == "VolatilityJsonParser" && x.MalformedRecords > 0);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[ { "PID": "x", "PPID": 0, "ImageFileName": "a.exe" } ]""")]
    public void An_empty_or_unreadable_plugin_output_is_an_explicit_gap_not_a_success(string json)
    {
        var p = F("pslist.json", json);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases3"), "mem3", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [p]);
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        Assert.DoesNotContain(r.Timeline, t => t.Source.StartsWith("Memory:", StringComparison.Ordinal));
        var g = Assert.Single(r.Gaps, x => x.Reason.Contains("simboluri lipsă"));
        Assert.Contains("nu înseamnă că imaginea nu conține", g.Reason);
    }
}
