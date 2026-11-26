using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Team E: Defender detections of command lines are grouped per distinct threat set (not per command text), like files are grouped per file name.</summary>
public class TeamECmdlineGroupingTests
{
    private const string Defender = "Microsoft-Windows-Windows Defender/Operational";
    private const string R = "Trojan:PowerShell/PsAttack.R";
    private const string M = "HackTool:Win32/Mimikatz.A";

    private static TimelineEvent Cmd(int id, double minute, string cmd, string threat, string actionId = "9", string actionName = "Not Applicable") =>
        Ev(Defender, id, minute, "", ("Path", "CmdLine:_" + cmd), ("Threat Name", threat), ("Action ID", actionId), ("Action Name", actionName), ("Detection User", @"PC\owner"));

    private static List<Finding> Def(IEnumerable<TimelineEvent> ev) => Correlation.Run(ev.ToList()).Where(x => x.RuleId == "DEF-DETECTION").ToList();

    [Fact]
    public void Same_threat_in_many_command_lines_and_days_is_one_finding_with_all_evidence()
    {
        var ev = new List<TimelineEvent>
        {
            Cmd(1116, 0, "powershell -enc AAA", R), Cmd(1116, 1, "powershell -enc BBB", R), Cmd(1116, 2, "powershell -enc AAA", R),
            Cmd(1116, 60 * 24 * 11, "powershell -enc CCC", R), Cmd(1116, 60 * 24 * 11 + 5, "powershell -enc DDD", R),
        };
        var f = Assert.Single(Def(ev));
        Assert.Contains("linie de comandă", f.Title);
        Assert.Contains(R, f.Title);
        Assert.Contains("5 detecții", f.Title);
        Assert.Contains("prima 2026-10-01 10:00", f.Title);
        Assert.Equal(5, f.SupportingEvidence.Count);
        Assert.Equal(5, f.SupportingEvidence.Select(r => r.Locator).Distinct().Count());
        Assert.All(f.SupportingEvidence, r => Assert.StartsWith("EventRecordID=", r.Locator));
        Assert.Equal(ev.Min(e => e.Time.Utc), f.FirstSeenUtc);
        Assert.Equal(ev.Max(e => e.Time.Utc), f.LastSeenUtc);
    }

    [Fact]
    public void Different_threats_stay_separate_findings()
    {
        var ev = new List<TimelineEvent> { Cmd(1116, 0, "a", R), Cmd(1116, 1, "b", R), Cmd(1116, 2, "c", M), Cmd(1116, 3, "d", M) };
        var f = Def(ev);
        Assert.Equal(2, f.Count);
        Assert.Contains(f, x => x.Title.Contains(R) && x.Title.Contains("2 detecții"));
        Assert.Contains(f, x => x.Title.Contains(M) && x.Title.Contains("2 detecții"));
    }

    [Fact]
    public void A_combined_threat_set_is_its_own_finding_and_highest_severity_wins()
    {
        var ev = new List<TimelineEvent>
        {
            Cmd(1116, 0, "a", R), Cmd(1117, 1, "a", R, "2", "Quarantine"),
            Cmd(1116, 5, "b", R), // unresolved -> High for the group
            Cmd(1116, 10, "x", "Trojan:PowerShell/PsAttack.A"), Cmd(1116, 11, "x", R),
        };
        var f = Def(ev);
        Assert.Equal(2, f.Count);
        var single = Assert.Single(f, x => x.Title.Contains("detecții") && !x.Title.Contains("PsAttack.A"));
        Assert.Equal(Severity.High, single.Severity);
        Assert.Contains(f, x => x.Title.Contains("PsAttack.A") && x.Title.Contains("PsAttack.R"));
    }
}
