using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Graph;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>P4 evidence graph: no edge without evidence or derivation; edges come only from what events and findings show.</summary>
public sealed class EvidenceGraphTests
{
    private static readonly DateTime T0 = new(2026, 9, 19, 14, 52, 42, DateTimeKind.Utc);

    private static TimelineEvent E(string source, string ev, string locator, int minute, string path = "", string process = "", string dns = "",
                                   string eventId = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = source, EvidenceId = ev, Locator = locator, Path = path,
        Process = process, Dns = dns, EventId = eventId, Summary = "t", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static List<TimelineEvent> Incident() =>
    [
        E("BrowserDownload", "EV-H", "downloads.id=1", 0, @"C:\Users\u\Downloads\Tool_302044.zip", "Tool_302044.zip", "mailjilq.tzd4is.cyou", "",
          ("TabUrl", "https://mailjilq.tzd4is.cyou/"), ("UrlChain", "blob:x")),
        E("Prefetch", "EV-PF1", "pf1", 4, "", "SETUP.EXE", "", "", ("ReferencedFiles", @"\VOLUME{x}\USERS\U\DOWNLOADS\TOOL_302044\SETUP.EXE|\VOLUME{x}\WINDOWS\SYSTEM32\NTDLL.DLL"), ("RunCount", "4")),
        E("Prefetch", "EV-PF2", "pf2", 19, "", "MSIEXEC.EXE", "", "", ("ReferencedFiles", @"\VOLUME{x}\WINDOWS\SYSTEM32\MSIEXEC.EXE|\VOLUME{x}\USERS\U\APPDATA\LOCAL\CONEXANT\CXUTILSVC HELPER\BOOTSTRAP_7D57.CMD"), ("RunCount", "9")),
        E("EventLog:Microsoft-Windows-Windows Defender/Operational", "EV-DEF", "EventRecordID=7", 14, "", "", "", "1116",
          ("Threat Name", "Behavior:Win32/GenCodeInjected.H"), ("Path", @"process:_pid:52196; file:_C:\Users\u\Downloads\TOOL_302044\NanAgent32.exe")),
        new()
        {
            Time = Timestamp.FromUtc(T0.AddMinutes(20), "", "test"), Source = "Service", EvidenceId = "EV-SYS", Locator = @"SYSTEM\ControlSet001\Services\CxUtilSvc",
            Service = "CxUtilSvc", Path = @"C:\ProgramData\Conexant\CxUtilSvc.exe", Process = "CxUtilSvc.exe", Summary = "svc",
        },
    ];

    [Fact]
    public void Explorer_lists_an_entitys_edges_with_their_support_and_the_path_between_two_entities()
    {
        var events = Incident();
        var g = EvidenceGraph.Build(events, Correlation.Run(events), "MARIUS-PC");
        var found = GraphExplorer.Search(g, "302044").Select(e => e.Type).ToList();
        Assert.Contains("File", found);
        var zip = g.Find("File", "Tool_302044.zip")!;
        var rows = GraphExplorer.EdgesOf(g, zip.Id);
        var dl = Assert.Single(rows, r => r.Relation == "DOWNLOADED" && r.OtherType == "Domain" && r.OtherLabel.Contains("tzd4is.cyou") && r.Reason.Contains("final"));
        Assert.Equal(("←", "Observat", "EV-H · downloads.id=1"), (dl.Direction, dl.Classification, dl.Support));
        var derived = Assert.Single(rows, r => r.Relation == "DERIVED_FROM");
        Assert.Equal(("←", "Corelat"), (derived.Direction, derived.Classification));
        Assert.StartsWith("derivat: DOWNLOAD-THEN-EXEC", derived.Support);

        var domain = g.Find("Domain", "tzd4is.cyou")!;
        var setup = g.Find("File", @"TOOL_302044\SETUP.EXE")!;
        var path = GraphExplorer.PathBetween(g, domain.Id, setup.Id);
        Assert.Equal(["DOWNLOADED", "DERIVED_FROM"], path.Select(p => p.Relation));
        Assert.Equal(setup.Id, path[^1].OtherId);
        Assert.Empty(GraphExplorer.PathBetween(g, domain.Id, "File:NU-EXISTA"));
        Assert.Equal(("Observat", "Deducție", "Nedemonstrat"), (StateLabels.ForClassification(Classification.Direct), StateLabels.ForClassification(Classification.Candidate), StateLabels.ForClassification(Classification.Unproven)));
    }
    [Fact]
    public void A_relationship_needs_evidence_or_an_explicit_derivation()
    {
        Assert.Throws<ArgumentException>(() => new Relationship("R", "a", "b", RelationType.Executed, Timestamp.Unknown(), "", "", "",
            Classification.Direct, Confidence.High, ""));
        Assert.Throws<ArgumentException>(() => new Relationship("R", "a", "b", RelationType.Executed, Timestamp.Unknown(), "EV-1", "", "",
            Classification.Direct, Confidence.High, ""));
        Assert.Equal("DERIVED_FROM", new Relationship("R", "a", "b", RelationType.DerivedFrom, Timestamp.Unknown(), "", "", "rule X",
            Classification.Correlated, Confidence.Medium, "").TypeName);
    }

    [Fact]
    public void Graph_holds_only_what_the_evidence_shows_and_marks_correlation_as_such()
    {
        var events = Incident();
        var findings = Correlation.Run(events);
        var g = EvidenceGraph.Build(events, findings, "MARIUS-PC");

        Assert.All(g.Relationships, r => Assert.True(r.EvidenceId.Length > 0 && r.Locator.Length > 0 || r.Derivation.Length > 0));
        var domain = g.Find("Domain", "tzd4is.cyou")!;
        var zip = g.Find("File", "Tool_302044.zip")!;
        var setup = g.Find("File", @"TOOL_302044\SETUP.EXE")!;
        var nan = g.Find("File", "NanAgent32.exe")!;
        var threat = g.Find("Threat", "GenCodeInjected")!;

        var dl = Assert.Single(g.Edges(domain.Id), r => r.Type == RelationType.Downloaded);
        Assert.Equal((zip.Id, "EV-H", "downloads.id=1", Classification.Direct), (dl.TargetEntity, dl.EvidenceId, dl.Locator, dl.Classification));
        Assert.Contains(g.Edges(threat.Id), r => r.Type == RelationType.Detected && r.TargetEntity == nan.Id && r.EvidenceId == "EV-DEF");
        var msiexec = g.Find("File", "MSIEXEC.EXE")!;
        Assert.Contains(g.Edges(msiexec.Id), r => r.Type == RelationType.Loaded && r.TargetEntity.Contains("BOOTSTRAP_7D57.CMD"));
        Assert.DoesNotContain(g.Edges(msiexec.Id), r => r.TargetEntity.Contains("NTDLL"));          // system DLLs are not drawn
        Assert.Contains(g.Relationships, r => r.Type == RelationType.Persisted && r.TargetEntity == "Service:cxutilsvc");

        // SETUP.EXE ← zip is a correlation (DOWNLOAD-THEN-EXEC), carried as a derivation, not as direct evidence.
        var derived = Assert.Single(g.Relationships, r => r.Type == RelationType.DerivedFrom);
        Assert.Equal((setup.Id, zip.Id, Classification.Correlated), (derived.SourceEntity, derived.TargetEntity, derived.Classification));
        Assert.StartsWith("DOWNLOAD-THEN-EXEC", derived.Derivation);

        // No evidence links SETUP.EXE to NanAgent32.exe directly; the only path goes through findings (correlation).
        Assert.DoesNotContain(g.Relationships, r => (r.SourceEntity == setup.Id && r.TargetEntity == nan.Id) || (r.SourceEntity == nan.Id && r.TargetEntity == setup.Id));
        var path = g.Path(domain.Id, threat.Id);
        Assert.NotEmpty(path);
        Assert.Contains(path, r => r.Type is RelationType.PartOf or RelationType.Supports or RelationType.DerivedFrom);
    }

    [Fact]
    public void Snapshot_is_deterministic_json_with_hash()
    {
        var events = Incident();
        var g1 = EvidenceGraph.Build(events, Correlation.Run(events), "H");
        var g2 = EvidenceGraph.Build(events, Correlation.Run(events), "H");
        var (json, sha) = g1.Snapshot("C-1");
        Assert.Equal(sha, g2.Snapshot("C-1").Sha256);
        Assert.Contains("\"DOWNLOADED\"", json);
        Assert.Equal(64, sha.Length);
    }
}
