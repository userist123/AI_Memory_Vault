using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #15 F2: the same file name + threat set in many folders is one DEF-DETECTION finding (synthetic Defender 1116/1117 events only).</summary>
public class F2DefenderGroupingTests
{
    private const string Defender = "Microsoft-Windows-Windows Defender/Operational";
    private const string Threat = "HackTool:Win32/PsAttack.A";

    private static TimelineEvent Det(int id, double minute, string path, string threat, string actionId = "9", string actionName = "Not Applicable") =>
        Ev(Defender, id, minute, "", ("Path", "file:_" + path), ("Threat Name", threat), ("Action ID", actionId), ("Action Name", actionName), ("Detection User", @"PC\owner"));

    private static string BuildDll(int i) => $@"C:\dev\wt{i}\App.Tests\bin\Debug\net10.0\App.Tests.dll";
    private static List<Finding> Def(IEnumerable<TimelineEvent> ev) => Correlation.Run(ev.ToList()).Where(x => x.RuleId == "DEF-DETECTION").ToList();

    [Fact]
    public void Five_folders_same_dll_and_threat_are_one_finding_with_five_locations_and_counts()
    {
        var ev = Enumerable.Range(0, 5).SelectMany(i => new[] { Det(1116, i * 10, BuildDll(i), Threat), Det(1116, i * 10 + 1, BuildDll(i), Threat) }).ToList();
        var f = Assert.Single(Def(ev));
        Assert.Contains("5 locații", f.Title);
        Assert.Contains("10 detecții", f.Title);
        Assert.Contains("prima 2026-10-01 10:00", f.Title);
        Assert.Contains("App.Tests.dll", f.Title);
        Assert.Equal(10, f.SupportingEvidence.Count);
        for (int i = 0; i < 5; i++) Assert.Contains(BuildDll(i), f.Description);
        Assert.Contains("0 din 5", f.Description);
        Assert.Equal("F-0001", f.FindingId);
    }

    [Fact]
    public void Locations_are_capped_at_20_and_evidence_at_50_with_notes()
    {
        var ev = Enumerable.Range(0, 30).SelectMany(i => new[] { Det(1116, i, BuildDll(i), Threat), Det(1116, i + 100, BuildDll(i), Threat) }).ToList();
        var f = Assert.Single(Def(ev));
        Assert.Contains("30 locații", f.Title);
        Assert.Contains("și încă 10 locații", f.Description);
        Assert.Equal(50, f.SupportingEvidence.Count);
        Assert.Contains("și încă 10 sunt în cronologie", f.Description);
    }

    [Fact]
    public void One_High_location_makes_the_merged_finding_High_and_counts_handled_locations()
    {
        var ev = new List<TimelineEvent>();
        for (int i = 0; i < 3; i++) ev.Add(Det(1116, i, BuildDll(i), Threat));
        ev.Add(Det(1117, 10, BuildDll(0), Threat, "2", "Quarantine"));
        ev.Add(Det(1117, 11, BuildDll(1), Threat, "3", "Remove"));
        var f = Assert.Single(Def(ev));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("2 din 3", f.Description);
        // all handled: Medium
        ev.Add(Det(1117, 12, BuildDll(2), Threat, "6", "Allow"));
        Assert.Equal(Severity.Medium, Assert.Single(Def(ev)).Severity);
    }

    [Fact]
    public void A_different_threat_on_the_same_file_name_is_merged_into_one_finding_listing_both_threats()
    {
        var ev = new List<TimelineEvent> { Det(1116, 0, BuildDll(0), Threat), Det(1116, 1, BuildDll(1), Threat), Det(1116, 2, BuildDll(2), "Trojan:Win32/Wacatac.H!ml") };
        var f = Assert.Single(Def(ev));
        Assert.Contains("3 locații", f.Title);
        Assert.Contains(Threat, f.Description);
        Assert.Contains("Wacatac", f.Description);
        Assert.Contains("Wacatac.H!ml (1 detecții", f.Description);
    }

    [Fact]
    public void The_hosts_file_detected_repeatedly_is_one_finding()
    {
        const string T = "SettingsModifier:Win32/PossibleHostsFileHijack";
        var ev = new List<TimelineEvent> { Det(1116, 0, @"C:\Windows\System32\drivers\etc\hosts", T), Det(1116, 5, @"C:\Windows\System32\drivers\etc\HOSTS", T), Det(1116, 9, @"C:\Windows\System32\drivers\etc\hosts", T) };
        var f = Assert.Single(Def(ev));
        Assert.Contains("hosts", f.Title);
        Assert.Contains("3 detecții", f.Title);
        Assert.Equal(3, f.SupportingEvidence.Count);
    }

    [Fact]
    public void The_hosts_file_under_different_container_paths_is_one_finding_with_all_locations()
    {
        const string T = "SettingsModifier:Win32/PossibleHostsFileHijack";
        var ev = new List<TimelineEvent> { Det(1116, 0, @"C:\Windows\System32\drivers\etc\hosts", T), Det(1116, 5, @"D:\img\Windows\System32\drivers\etc\hosts", T), Det(1116, 9, @"E:\old\hosts", T) };
        var f = Assert.Single(Def(ev));
        Assert.Contains("3 locații", f.Title);
        Assert.Contains("3 detecții", f.Title);
        Assert.Empty(f.AlternativeExplanations);
    }

    [Fact]
    public void The_build_alternative_is_kept_only_when_every_location_is_a_build_output()
    {
        var allBuild = Assert.Single(Def([Det(1116, 0, BuildDll(0), Threat), Det(1116, 1, BuildDll(1), Threat), Det(1116, 2, @"C:\dev\x\obj\Release\App.Tests.dll", Threat)]));
        Assert.Contains(allBuild.AlternativeExplanations, a => a.Contains("fișier produs de compilare"));
        var mixed = Assert.Single(Def([Det(1116, 0, BuildDll(0), Threat), Det(1116, 1, BuildDll(1), Threat), Det(1116, 2, @"C:\Users\owner\Downloads\App.Tests.dll", Threat)]));
        Assert.Empty(mixed.AlternativeExplanations);
        Assert.Contains("2 din 3 locații sunt foldere de compilare", mixed.Description);
    }
}
