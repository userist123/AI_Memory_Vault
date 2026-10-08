using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Sigma-lite: subset semantics, load errors, and the shipped rules against Windows' own query engine (wevtutil).</summary>
public sealed class SigmaLiteTests
{
    private static readonly string RulesDir = Path.Combine(AppContext.BaseDirectory, "Rules", "sigma");

    private static TimelineEvent E(string channel, string id, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(new DateTime(2026, 9, 19, 15, 0, 0, DateTimeKind.Utc), "", "t"), Source = "EventLog:" + channel, EventId = id,
        EvidenceId = "EV-1", Locator = "EventRecordID=" + id, Summary = "s", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private const string Rule = """
        title: t
        id: r1
        logsource: { product: windows, service: system }
        detection:
          sel_a:
            EventID: '7045'
            ImagePath|contains|all: ['\Temp\', '.sys']
          sel_b:
            - ServiceName|startswith: 'Cx'
            - ServiceName|endswith: 'Helper'
          filter:
            AccountName|re: '^LocalSystem$'
          condition: (sel_a or 1 of sel_b*) and not filter
        level: high
        tags: [attack.t1543.003]
        """;

    [Fact]
    public void Selections_modifiers_and_conditions()
    {
        var s = SigmaLite.Load(("r.yml", Rule));
        var events = new[]
        {
            E("System", "7045", ("ImagePath", @"C:\Users\u\AppData\Local\Temp\x.sys"), ("AccountName", "NT AUTHORITY\\SYSTEM")),   // sel_a
            E("System", "7045", ("ImagePath", @"C:\Temp\x.exe")),                                                                   // contains|all fails
            E("System", "7036", ("ServiceName", "CxUtilSvc")),                                                                      // sel_b (startswith)
            E("System", "7036", ("ServiceName", "Other Helper")),                                                                   // sel_b (endswith)
            E("System", "7036", ("ServiceName", "CxUtilSvc"), ("AccountName", "LocalSystem")),                                      // filtered
            E("Application", "7045", ("ImagePath", @"C:\Temp\x.sys")),                                                               // other channel
        };
        var m = s.Match(events).Select(x => Array.IndexOf(events, x.Event)).ToArray();
        Assert.Equal([0, 2, 3], m);
        var r = Assert.Single(s.Rules);
        Assert.Equal("T1543.003", r.Identity.MitreTechniqueId);
        Assert.Equal(64, r.Identity.Sha256.Length);
        Assert.Equal(DetectionKind.Sigma, s.Detect(events).First().Kind);
    }

    [Theory]
    [InlineData("title: t\nlogsource: { product: windows, category: process_creation }\ndetection: { s: { EventID: '1' }, condition: s }")]
    [InlineData("title: t\nlogsource: { product: windows, service: system }\ndetection: { s: { CommandLine|base64: x }, condition: s }")]
    [InlineData("title: t\nlogsource: { product: windows, service: system }\ndetection: { s: { EventID: '1' }, condition: s and missing }")]
    [InlineData("title: t\nlogsource: { product: windows, service: system }\ndetection: { s: { EventID: '1' }, timeframe: 5m, condition: s }")]
    [InlineData("title: t\nlogsource: { product: linux, service: auth }\ndetection: { s: { x: y }, condition: s }")]
    public void Rules_outside_the_subset_fail_to_load(string yaml)
    {
        Assert.Throws<FormatException>(() => SigmaLite.Load(("bad.yml", yaml)));
    }

    /// <summary>Counts and record ids of events matching an XPath query, from Windows' own event query engine.</summary>
    private static List<(long RecordId, XElement Event)> Wevtutil(string file, string xpath)
    {
        var psi = new ProcessStartInfo("wevtutil.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "qe", file, "/lf:true", "/f:xml", "/q:" + xpath }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var xml = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        return XElement.Parse("<r>" + xml + "</r>").Elements(ns + "Event")
            .Select(e => (long.Parse(e.Element(ns + "System")!.Element(ns + "EventRecordID")!.Value), e)).ToList();
    }

    private static ListSink Parse(string file)
    {
        var sink = new ListSink();
        new EvtxParser().Parse(new EvidenceItem { EvidenceId = "EV-X", CaseId = "C", Source = "x", SourceType = "evtx", StoredPath = file }, file, sink, default);
        return sink;
    }

    private static HashSet<string> Records(IEnumerable<(SigmaLite.SigmaRule Rule, TimelineEvent Event)> m, string ruleId) =>
        m.Where(x => x.Rule.Id == ruleId).Select(x => Regex.Match(x.Event.Locator, @"\d+").Value).ToHashSet();

    /// <summary>
    /// Each shipped rule on the real logs selects exactly the records Windows' query engine selects for the same criteria
    /// (EventID filters in XPath; for the user-path rule, ImagePath read from wevtutil's XML).
    /// </summary>
    [CorpusFact("defenderEvtx")]
    public void Shipped_rules_select_the_same_records_as_wevtutil()
    {
        var sigma = SigmaLite.LoadDirectory(RulesDir);
        Assert.Equal(4, sigma.Rules.Count);
        var logs = Path.Combine(Corpus.Root, "01_RAW_EVENTLOGS");

        var defender = Path.Combine(logs, "Microsoft-Windows-Windows Defender%4Operational.evtx");
        var dm = sigma.Match(Parse(defender).Events).ToList();
        var expectedDef = Wevtutil(defender, "*[System[(EventID=1116 or EventID=1117)]]").Select(x => x.RecordId.ToString()).ToHashSet();
        Assert.True(expectedDef.Count > 100);
        Assert.Equal(expectedDef, Records(dm, "8c3e2b4a-7d41-4f0e-9a6d-1d2b7f0e1116"));

        var system = Path.Combine(logs, "System.evtx");
        var sm = sigma.Match(Parse(system).Events).ToList();
        Assert.Equal(Wevtutil(system, "*[System[(EventID=104)]]").Select(x => x.RecordId.ToString()).ToHashSet(), Records(sm, "3a9f6c1e-2b8d-4e57-8f3a-0c6e9d2a0104"));
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var userPath = Wevtutil(system, "*[System[(EventID=7045)]]")
            .Where(x => x.Event.Descendants(ns + "Data").FirstOrDefault(d => (string?)d.Attribute("Name") == "ImagePath")?.Value is { } ip
                        && new[] { @"\AppData\", @"\Temp\", @"\Users\Public\" }.Any(s => ip.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.RecordId.ToString()).ToHashSet();
        Assert.NotEmpty(userPath);
        Assert.Equal(userPath, Records(sm, "9e7b1c3d-4a2f-4d8e-b5c6-2e9f0a1d7045"));
    }
}
