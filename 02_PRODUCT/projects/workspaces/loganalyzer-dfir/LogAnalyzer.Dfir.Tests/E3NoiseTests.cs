using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #14 E3: less noise from benign findings seen on a real PC (Docker hosts entries, Quick Assist, the EXEC-USERPATH bulk, Add-MpPreference history lines).</summary>
public sealed class E3NoiseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_e3_" + Guid.NewGuid().ToString("N"));

    public E3NoiseTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private List<Finding> Hosts(string text)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + "_hosts");
        File.WriteAllText(path, text);
        var sink = new ListSink();
        new HostsFileParser().Parse(new EvidenceItem { EvidenceId = "EV-1", CaseId = "C", Source = "t", SourceType = HostsFileParser.SourceTypeName, StoredPath = path }, path, sink, default);
        Assert.NotEmpty(sink.Events);   // the timeline events stay
        return Correlation.Run(sink.Events).Where(f => f.RuleId.StartsWith("HOSTS-", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void Docker_Desktop_hosts_entries_are_benign_known_and_not_a_mistake()
    {
        var f = Assert.Single(Hosts("# Added by Docker Desktop\r\n192.168.1.50 host.docker.internal\r\n192.168.1.50 gateway.docker.internal\r\n# End of section\r\n").Take(1));
        Assert.Equal("HOSTS-REDIRECT", f.RuleId);
        Assert.Equal(Classification.BenignKnown, f.Classification);
        Assert.Contains("intrare adăugată de Docker Desktop", f.ClassificationReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(InvestigationReportText.IsMistake(f));
        // the block marker alone (an address line under "# Added by Docker Desktop" with another name) is covered as well
        var g = Assert.Single(Hosts("# Added by Docker Desktop\r\n10.0.0.7 some.docker.local\r\n# End of section\r\n"));
        Assert.Equal(Classification.BenignKnown, g.Classification);
    }

    [Fact]
    public void A_redirect_outside_the_docker_block_is_still_a_finding_and_a_mistake()
    {
        var f = Assert.Single(Hosts("# Added by Docker Desktop\r\n192.168.1.50 host.docker.internal\r\n# End of section\r\n192.168.1.20 intranet.example.org\r\n"),
            x => x.Title.Contains("intranet.example.org"));
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Equal(Severity.Low, f.Severity);
        Assert.True(InvestigationReportText.IsMistake(f));
    }

    [Fact]
    public void Quick_Assist_present_is_info_and_TeamViewer_present_stays_low()
    {
        var qa = Assert.Single(W11.Run([Row("Amcache", 0, "quickassist.exe", @"C:\Windows\System32\quickassist.exe", "")]), f => f.RuleId == "REMOTE-TOOL-PRESENT");
        Assert.Equal(Severity.Info, qa.Severity);
        var tv = Assert.Single(W11.Run([Row("Amcache", 0, "teamviewer.exe", @"C:\Program Files\TeamViewer\TeamViewer.exe", "")]), f => f.RuleId == "REMOTE-TOOL-PRESENT");
        Assert.Equal(Severity.Low, tv.Severity);
    }

    [Fact]
    public void The_uncorrelated_EXEC_USERPATH_bulk_is_info_coverage_context()
    {
        var all = Correlation.Run([Row("Amcache", 0, "A.EXE", @"C:\Users\u\Downloads\a.exe", "h"), Row("BAM", 0, "B.EXE", @"C:\Users\u\Downloads\b.exe")]);
        var bulk = Assert.Single(all, f => f.RuleId == "EXEC-USERPATH");
        Assert.Contains("fără alte semnale", bulk.Title);
        Assert.Equal(Severity.Info, bulk.Severity);
    }

    // ---- Add-MpPreference history lines ----

    private static TimelineEvent History(int line, string text) => new()
    {
        Time = Timestamp.Unknown(), Source = "PsReadLineHistory", EvidenceId = "EV-H", EventId = "Command", Summary = text, Locator = $"line={line}", Confidence = Confidence.High,
        Fields = { ["RawLine"] = text, ["Command"] = text, ["Line"] = line.ToString(), ["Profile"] = "S-1-5-21-1" },
    };

    private static Finding Cfg(params string[] paths) => new()
    {
        FindingId = "F-CFG", RuleId = "CFG-DEFENDER-EXCL", Title = "Microsoft Defender are excluderi de la scanare", Description = "d", Severity = Severity.Medium,
        SupportingEvidence = [new EvidenceRef("EV-S", @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths", "cheia", "", $"{paths.Length} excluderi: " + string.Join("; ", paths))],
    };

    private static List<Finding> HistoryFindings(params string[] lines) =>
        Correlation.Run(lines.Select((l, i) => History(i + 1, l)).ToList()).Where(f => f.RuleId == "PS-HISTORY-SUSPICIOUS").ToList();

    [Fact]
    public void A_literal_Add_MpPreference_path_that_is_listed_is_merged_into_the_CFG_finding_and_the_history_finding_goes()
    {
        var ps = HistoryFindings(@"Add-MpPreference -ExclusionPath ""C:\Tools\Build""");
        Assert.Single(ps);
        var cfg = Cfg(@"C:\Tools\Build", @"D:\other");
        var r = DefenderExclusionHistoryMerger.Merge([cfg, .. ps]);
        Assert.Equal(1, r.Merged);
        Assert.DoesNotContain(r.Findings, f => f.RuleId == "PS-HISTORY-SUSPICIOUS");
        var kept = Assert.Single(r.Findings);
        Assert.Equal(2, kept.SupportingEvidence.Count);
        Assert.Equal("line=1", kept.SupportingEvidence[1].Locator);
        Assert.Contains("comandă din istoricul PowerShell care poate fi sursa excluderii; PSReadLine nu are ore", kept.SupportingEvidence[1].Description);
        // deterministic: the same input gives the same result
        var again = DefenderExclusionHistoryMerger.Merge([Cfg(@"C:\Tools\Build", @"D:\other"), .. HistoryFindings(@"Add-MpPreference -ExclusionPath ""C:\Tools\Build""")]);
        Assert.Equal(kept.SupportingEvidence.Select(e => e.Locator + e.Description), Assert.Single(again.Findings).SupportingEvidence.Select(e => e.Locator + e.Description));
    }

    [Fact]
    public void A_variable_path_or_an_unlisted_path_is_not_merged_and_stays_a_PS_HISTORY_finding()
    {
        var ps = HistoryFindings(@"Add-MpPreference -ExclusionPath $env:USERPROFILE\Downloads", @"Add-MpPreference -ExclusionPath 'C:\NotListed'");
        Assert.Equal(2, ps.Count);
        var r = DefenderExclusionHistoryMerger.Merge([Cfg(@"C:\Users\ana\Downloads", @"D:\other"), .. ps]);
        Assert.Equal(0, r.Merged);
        Assert.Equal(2, r.Findings.Count(f => f.RuleId == "PS-HISTORY-SUSPICIOUS"));
        Assert.Single(r.Findings.Single(f => f.RuleId == "CFG-DEFENDER-EXCL").SupportingEvidence);
    }
}
