using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>P5 IOC matching: synthetic list with each indicator type, and the investigation's real inventory against the real samples.</summary>
public sealed class IocMatcherTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_ioc_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static TimelineEvent E(string locator, string path = "", string dns = "", string hash = "", string task = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(new DateTime(2026, 9, 19, 15, 0, 0, DateTimeKind.Utc), "", "t"), Source = "x", EvidenceId = "EV-1", Locator = locator,
        Path = path, Dns = dns, Hash = hash, Task = task, Summary = "s", SourceSha256 = "AB12",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private string Csv()
    {
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, "IOC_INVENTORY.csv");
        File.WriteAllText(p, """
            Type,Value,Context,Classification,Confidence,Source
            sha1,2832AD566FA1A3B12A70341F2D7B34422E76EDE0,bootstrap_7d57.cmd,DIRECT,HIGH,MPLog
            path,C:\Users\Marius\AppData\Local\Conexant\CxUtilSvc Helper\,Loader directory,DIRECT,HIGH,fs
            path,C:\Users\Marius\AppData\Local\Temp\6q5x6m*\6q5x6m*.msi,Random MSI drop,DIRECT,HIGH,MsiInstaller
            domain,tzd4is.cyou,landing domain,DIRECT,MEDIUM,history
            task,\orchestratormaintain,Persistence,DIRECT,HIGH,Defender
            ip,203.0.113.7,c2,DIRECT,LOW,pcap
            """);
        return p;
    }

    [Fact]
    public void Each_indicator_type_matches_with_rule_identity_and_evidence()
    {
        var m = IocMatcher.LoadCsv(Csv());
        Assert.Equal(6, m.Indicators.Count);
        Assert.Equal(64, m.RuleSet.Sha256.Length);

        var events = new List<TimelineEvent>
        {
            E("pf", "", "", "", "", ("ReferencedFiles", @"\VOLUME{x}\WINDOWS\SYSTEM32\MSIEXEC.EXE|\VOLUME{x}\USERS\MARIUS\APPDATA\LOCAL\CONEXANT\CXUTILSVC HELPER\BOOTSTRAP_7D57.CMD")),
            E("msi", path: @"C:\Users\Marius\AppData\Local\Temp\6q5x6mab\6q5x6mab.msi"),
            E("visit", dns: "mailjilq.tzd4is.cyou"),
            E("amcache", hash: "2832ad566fa1a3b12a70341f2d7b34422e76ede0"),
            E("task", task: @"\orchestratormaintain"),
            E("benign", path: @"C:\Users\Marius\AppData\Local\Temp\other\setup.msi", dns: "nottzd4is.cyou"),
        };
        var r = m.MatchEvents(events).ToList();

        Assert.Equal(["amcache", "msi", "pf", "task", "visit"], r.Select(x => x.Locator).Order().ToArray());
        var pf = Assert.Single(r, x => x.Locator == "pf");
        Assert.Equal(Confidence.High, pf.Confidence);
        Assert.Equal(m.RuleSet.Sha256, pf.RuleSha256);
        Assert.Equal("AB12", pf.EvidenceSha256);
        Assert.Contains("nu dovedește", pf.Explanation);
        Assert.DoesNotContain(r, x => x.Locator == "benign");
    }

    /// <summary>
    /// The SHA-256 values in the investigation's IOC inventory were computed during the manual investigation (PowerShell
    /// Get-FileHash); the application hashes the same sample files on import. Every sample whose hash the inventory lists
    /// must match, and the MSI's in particular.
    /// </summary>
    [CorpusFact("iocInventory")]
    public void Real_inventory_hashes_match_the_imported_samples_and_the_timeline()
    {
        var m = IocMatcher.LoadCsv(Corpus.File("iocInventory"));
        var samples = Corpus.S("iocInventory", "samples").Split('|').Select(s => Path.Combine(Corpus.Root, s))
            .SelectMany(s => File.Exists(s) ? [s] : Directory.GetFiles(s, "*", SearchOption.AllDirectories)).ToList();
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "ioc", TestScopes.Valid());
        var evidence = InvestigationPipeline.Import(ws, samples);
        Assert.Equal(samples.Count, evidence.Count);
        Assert.All(evidence.Where(e => e.OriginalName.EndsWith(".msi") || e.OriginalName.EndsWith(".targets")), e => Assert.Equal("file", e.SourceType));

        var hits = m.MatchEvidence(evidence).ToList();
        foreach (var h in hits) output.WriteLine($"{h.Match}");
        var listed = m.Indicators.Where(i => i.Type == "sha256").Select(i => i.Value.ToUpperInvariant()).ToHashSet();
        var expected = evidence.Where(e => listed.Contains(e.Sha256.ToUpperInvariant())).ToList();
        Assert.True(expected.Count >= 8, $"{expected.Count} probe cu hash în inventar");
        Assert.Equal(expected.Count, hits.Count);
        Assert.Contains(hits, h => h.Match.Contains("9520444.msi") && h.Kind == DetectionKind.Hash);

        // Timeline: the loader folder (path IOC) appears in MSIEXEC's Prefetch references.
        var pf = Corpus.File("prefetchMsiexec");
        var sink = new ListSink();
        new PrefetchParser().Parse(new EvidenceItem { EvidenceId = "EV-PF", CaseId = "C", Source = "p", SourceType = "prefetch", StoredPath = pf }, pf, sink, default);
        Assert.Contains(m.MatchEvents(sink.Events), x => x.RuleId.Contains(@"CxUtilSvc Helper\") && x.Match.Contains("BOOTSTRAP_7D57.CMD"));
    }
}
