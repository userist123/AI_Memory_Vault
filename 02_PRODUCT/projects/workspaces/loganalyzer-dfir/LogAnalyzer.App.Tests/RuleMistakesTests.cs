using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #11 A7: high-level rule matches (Sigma, YARA, IOC) appear in "Greșeli găsite" with the same seven labels. Synthetic data only.</summary>
public sealed class RuleMistakesTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { InvestigationReportPdf.TextObserver.Value = null; _k.Dispose(); }

    private static readonly string[] Labels = ["Ce s-a întâmplat:", "Când:", "Unde:", "Dovada:", "Cum verificați:", "Ce NU dovedește:", "Grad de încredere:"];

    private static DetectionRule SigmaRule(string id, string title, string level, string fp = "") =>
        new("SIGMA:" + id, "2026-01-01", DetectionKind.Sigma, title, new string('a', 64), id + ".yml", "", level, fp);

    private static DetectionResult Hit(DetectionRule r, string locator, Confidence c = Confidence.High, DetectionKind? kind = null, string evidence = "E-0001") =>
        new(r.RuleId, r.Version, r.Sha256, kind ?? r.Kind, r.Title, evidence, locator, "ab", "Security 4688: powershell -enc AAAA", Classification.Direct, c, "x",
            new DateTimeOffset(2026, 11, 6, 13, 5, 0, TimeSpan.Zero));

    private string Print(Action<InvestigationResult> fill)
    {
        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        fill(r);
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, Guid.NewGuid().ToString("N") + ".pdf"), "investigator");
        return text;
    }

    private static string Section(string text)
    {
        int start = text.IndexOf(". Greșeli găsite\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "section missing");
        var next = Regex.Match(text[(start + 5)..], @"\n\d+\. ");
        return next.Success ? text.Substring(start, next.Index + 5) : text[start..];
    }

    [Fact]
    public void High_sigma_hit_is_a_mistake_with_seven_labels_and_low_hit_is_not()
    {
        var high = SigmaRule("11111111-aaaa", "PowerShell codat", "high", "Scripturi administrative legitime");
        var low = SigmaRule("22222222-bbbb", "Regula zgomotoasa de nivel mic", "low");
        var row = GapsTestKit.Ev("EventLog:Security", 0, "4688", evidence: "E-0001", f: ("CommandLine", "powershell -enc AAAA"));
        var text = Print(r =>
        {
            r.Timeline.Add(row);
            r.RulesUsed.AddRange([high, low]);
            r.Detections.Add(Hit(high, row.Locator));
            r.Detections.Add(Hit(low, row.Locator, Confidence.Medium));
        });
        var sec = Section(text);
        Assert.Contains("PowerShell codat", sec);
        Assert.Contains("SIGMA:11111111-aaaa", sec);
        Assert.DoesNotContain("Regula zgomotoasa", sec);
        foreach (var l in Labels) Assert.Equal(1, Regex.Matches(sec, "(?m)^" + Regex.Escape(l)).Count);
        Assert.Contains("Citat: „CommandLine=powershell -enc AAAA”", sec);
        Assert.Contains("Căutați regula SIGMA:11111111-aaaa „PowerShell codat”", sec);
        Assert.Contains(InvestigationReportText.RuleNotProven, sec);
        Assert.Contains("Scripturi administrative legitime", sec);
        Assert.Contains("nivelul regulii: high", sec);
        Assert.Contains("Greșeli găsite: 1 (din care 1 potrivire de reguli)", text);
    }

    [Fact]
    public void Yara_high_and_ioc_matches_are_included_with_the_matched_value_when_no_timeline_row()
    {
        var yara = new DetectionRule("YARA:Mimikatz_like", "1", DetectionKind.Yara, "Seamănă cu un instrument de furt de parole", new string('b', 64), "y.yar", "", "high");
        var yaraLow = new DetectionRule("YARA:Doar_info", "1", DetectionKind.Yara, "Fișier cu șiruri neobișnuite", new string('c', 64), "y.yar", "", "low");
        var ioc = new DetectionRule("IOC:lista.csv", "1", DetectionKind.Ioc, "Listă IOC", new string('d', 64), "lista.csv");
        var text = Print(r =>
        {
            r.RulesUsed.AddRange([yara, yaraLow, ioc]);
            r.Detections.Add(Hit(yara, "file", Confidence.High));
            r.Detections.Add(Hit(yaraLow, "file", Confidence.Medium));
            r.Detections.Add(Hit(new DetectionRule("IOC:lista.csv:ip:203.0.113.9", "1", DetectionKind.Ioc, "IOC ip: C2 cunoscut", new string('d', 64), "lista.csv"), "row=3", Confidence.High));
        });
        var sec = Section(text);
        Assert.Contains("Seamănă cu un instrument de furt de parole", sec);
        Assert.Contains("IOC ip: C2 cunoscut", sec);
        Assert.DoesNotContain("Fișier cu șiruri neobișnuite", sec);
        Assert.Contains("Valoare potrivită: „Security 4688: powershell -enc AAAA”", sec);
        foreach (var l in Labels) Assert.Equal(2, Regex.Matches(sec, "(?m)^" + Regex.Escape(l)).Count);
        Assert.Contains("Greșeli găsite: 2 (din care 2 potriviri de reguli)", text);
    }

    [Fact]
    public void Repeated_match_on_the_same_rule_and_evidence_is_one_block_with_a_count()
    {
        var high = SigmaRule("33333333-cccc", "Task suspect", "critical");
        var text = Print(r =>
        {
            r.RulesUsed.Add(high);
            for (int i = 0; i < 3; i++) r.Detections.Add(Hit(high, "row=9"));
            r.Detections.Add(Hit(high, "row=10"));
        });
        var sec = Section(text);
        Assert.Equal(2, Regex.Matches(sec, "(?m)^Grad de încredere:").Count);
        Assert.Contains("apare de 3 ori pe același element", sec);
        Assert.Contains("Greșeli găsite: 2 (din care 2 potriviri de reguli)", text);
    }
}
