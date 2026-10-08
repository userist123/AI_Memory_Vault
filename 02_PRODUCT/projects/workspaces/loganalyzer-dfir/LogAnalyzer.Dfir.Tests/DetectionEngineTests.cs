using System.Text;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public sealed class DetectionEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_det_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Bad_rule_files_become_gaps_and_good_ones_still_run()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "yara"));
        Directory.CreateDirectory(Path.Combine(_dir, "sigma"));
        Directory.CreateDirectory(Path.Combine(_dir, "ioc"));
        File.WriteAllText(Path.Combine(_dir, "yara", "good.yar"), "rule mz { strings: $m = { 4D 5A } condition: $m }");
        File.WriteAllText(Path.Combine(_dir, "yara", "bad.yar"), "rule x { strings: $a = \"q\" xor condition: $a }");
        File.WriteAllText(Path.Combine(_dir, "sigma", "bad.yml"), "title: t\nlogsource: { product: windows, category: x }\ndetection: { s: { a: b }, condition: s }");
        File.WriteAllText(Path.Combine(_dir, "ioc", "list.csv"), "type,value,description\nsha256,AABB,test\n");

        var engine = DetectionEngine.Load(_dir);
        Assert.Equal(2, engine.LoadErrors.Count);
        Assert.Contains(engine.LoadErrors, g => g.Artifact.Contains("bad.yar") && g.Reason.Contains("xor"));
        Assert.Contains(engine.LoadErrors, g => g.Artifact.Contains("bad.yml"));
        Assert.Equal(2, engine.Rules.Count);

        var evidence = new List<EvidenceItem>
        {
            new() { EvidenceId = "EV-1", CaseId = "C", Source = "s", SourceType = "file", OriginalName = "a.exe", Sha256 = "AABB", Size = 4 },
            new() { EvidenceId = "EV-2", CaseId = "C", Source = "s", SourceType = "evtx", OriginalName = "x.evtx", Sha256 = "CC", Size = 4 },
        };
        var r = engine.Run([], evidence, e => Encoding.ASCII.GetBytes("MZ.."));
        Assert.Contains(r, d => d.Kind == DetectionKind.Yara && d.EvidenceId == "EV-1" && d.RuleId == "YARA:mz");
        Assert.Contains(r, d => d.Kind == DetectionKind.Hash && d.EvidenceId == "EV-1");
        Assert.DoesNotContain(r, d => d.EvidenceId == "EV-2");                     // YARA only scans evidence of type "file"
    }
}
