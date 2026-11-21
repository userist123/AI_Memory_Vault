using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #15 F1: less noise from the event-log rules on a real PC, with no evidence lost (synthetic events only).</summary>
public class F1NoiseReductionTests
{
    private const string Defender = "Microsoft-Windows-Windows Defender/Operational";
    private const string PowerShell = "Microsoft-Windows-PowerShell/Operational";
    private const string Firewall = "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall";

    private static TimelineEvent Det(int id, double minute, string path, string threat, string actionId = "9", string actionName = "Not Applicable") =>
        Ev(Defender, id, minute, "", ("Path", "file:_" + path), ("Threat Name", threat), ("Action ID", actionId), ("Action Name", actionName), ("Detection User", @"PC\owner"));

    private const string Build = @"C:\dev\app\App.Tests\bin\Debug\net10.0\App.Tests.dll";
    private const string Other = @"C:\Users\owner\Downloads\setup.exe";

    // ---- 1. DEF-DETECTION ----

    [Fact]
    public void Defender_detections_of_one_item_are_one_finding_with_count_time_span_all_evidence_and_the_build_explanation_without_lowering_severity()
    {
        var events = new List<TimelineEvent>();
        for (int i = 0; i < 4; i++) events.Add(Det(1116, i * 10, Build, "HackTool:Win32/PsAttack.A"));
        var f = Assert.Single(Correlation.Run(events), x => x.RuleId == "DEF-DETECTION");
        Assert.Equal(Severity.High, f.Severity);                       // not lowered just because it is a build path
        Assert.Contains("4 detecții", f.Title);
        Assert.Contains("prima 2026-10-01 10:00", f.Title);
        Assert.Equal(4, f.SupportingEvidence.Count);
        Assert.Contains("fișier produs de compilare; detecțiile ML pe binare proprii pot fi fals pozitive", f.AlternativeExplanations);
        // a path outside any build folder gets no such explanation
        var other = Assert.Single(Correlation.Run([Det(1116, 0, Other, "Trojan:Win32/X")]), x => x.RuleId == "DEF-DETECTION");
        Assert.Empty(other.AlternativeExplanations);
        Assert.Equal(Severity.High, other.Severity);
    }

    [Fact]
    public void A_Defender_exclusion_path_gives_the_build_explanation_too()
    {
        var excl = Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\C:\work\out = 0x0"));
        var f = Assert.Single(Correlation.Run([excl, Det(1116, 1, @"C:\work\out\a.dll", "Trojan:Win32/X")]), x => x.RuleId == "DEF-DETECTION");
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("fișier produs de compilare"));
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void Severity_drops_to_Medium_only_when_the_latest_outcome_is_quarantine_remove_or_allowed_by_user()
    {
        var quarantined = Assert.Single(Correlation.Run([Det(1116, 0, Other, "T:A"), Det(1117, 1, Other, "T:A", "2", "Quarantine")]), x => x.RuleId == "DEF-DETECTION");
        Assert.Equal(Severity.Medium, quarantined.Severity);
        Assert.Contains("T:A: pus în carantină", quarantined.Description);
        Assert.Equal(2, quarantined.SupportingEvidence.Count);
        var allowed = Assert.Single(Correlation.Run([Det(1116, 0, Other, "T:A"), Det(1117, 1, Other, "T:A", "6", "Allow")]), x => x.RuleId == "DEF-DETECTION");
        Assert.Equal(Severity.Medium, allowed.Severity);
        Assert.Contains("permis de utilizator", allowed.Description);
        // detected again after the quarantine: still High
        Assert.Equal(Severity.High, Assert.Single(Correlation.Run([Det(1116, 0, Other, "T:A"), Det(1117, 1, Other, "T:A", "2", "Quarantine"), Det(1116, 5, Other, "T:A")]), x => x.RuleId == "DEF-DETECTION").Severity);
        // the action failed (1118): High
        Assert.Equal(Severity.High, Assert.Single(Correlation.Run([Det(1116, 0, Other, "T:A"), Det(1118, 1, Other, "T:A", "2", "Quarantine")]), x => x.RuleId == "DEF-DETECTION").Severity);
        // two threats in one item, only one handled: High
        Assert.Equal(Severity.High, Assert.Single(Correlation.Run([Det(1116, 0, Other, "T:A"), Det(1116, 0, Other, "T:B"), Det(1117, 1, Other, "T:A", "3", "Remove")]), x => x.RuleId == "DEF-DETECTION").Severity);
    }

    [Fact]
    public void Defender_evidence_is_capped_at_50_references_with_a_rest_count_in_the_text()
    {
        var events = Enumerable.Range(0, 60).Select(i => Det(1116, i, Other, "T:A")).ToList();
        var f = Assert.Single(Correlation.Run(events), x => x.RuleId == "DEF-DETECTION");
        Assert.Equal(50, f.SupportingEvidence.Count);
        Assert.Contains("și încă 10", f.Description);
    }

    // ---- 2. PS-SUSPICIOUS ----

    private static TimelineEvent Block(double minute, string text, string id, string path = "")
    {
        var e = Ev(PowerShell, 4104, minute, "", ("ScriptBlockText", text), ("ScriptBlockId", id), ("_xLevel", "3"));
        if (path.Length > 0) e.Fields["Path"] = path;
        return e;
    }

    [Fact]
    public void The_same_script_file_run_from_many_folders_is_one_finding_with_all_runs_and_the_alternatives()
    {
        var events = new List<TimelineEvent>();
        for (int i = 0; i < 5; i++) events.Add(Block(i, "# run " + i + "\nGet-Thing", "id" + i, $@"C:\Users\owner\AppData\Local\Temp\ladfir_remote_{i:D32}\target\collect.ps1"));
        var f = Assert.Single(Correlation.Run(events), x => x.RuleId == "PS-SUSPICIOUS");
        Assert.Equal(Severity.Medium, f.Severity);                      // never auto-dismissed
        Assert.Equal(5, f.SupportingEvidence.Count);
        Assert.Contains("5 texte diferite", f.Description);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("LogAnalyzer însuși"));
        // a script in a dev / build folder: alternative explanation, severity kept
        var dev = Assert.Single(Correlation.Run([Block(0, "Get-Thing", "d1", @"C:\dev\app\bin\Debug\tool.ps1")]), x => x.RuleId == "PS-SUSPICIOUS");
        Assert.Equal(Severity.Medium, dev.Severity);
        Assert.Contains("script de dezvoltare/compilare rulat local", dev.AlternativeExplanations);
        // a script without a Path stays identified by its text: different texts = different findings
        Assert.Equal(2, Correlation.Run([Block(0, "Get-A", "n1"), Block(1, "Get-B", "n2")]).Count(x => x.RuleId == "PS-SUSPICIOUS"));
    }

    [Fact]
    public void The_matched_line_is_quoted_for_a_keyword_script()
    {
        var f = Assert.Single(Correlation.Run([Block(0, "# harmless header\n" + TestFixtureText.EncodedIexBlock, "k1")]), x => x.RuleId == "PS-SUSPICIOUS");
        Assert.Contains("Linia potrivită: „", f.Description);
    }

    // ---- 3. FIREWALL-RULE-USERPATH ----

    private static TimelineEvent Fw(int n, string app, string direction) =>
        Ev(Firewall, 2097, n, "", ("RuleName", "rule" + n), ("ApplicationPath", app), ("Action", "3"), ("Direction", direction), ("ModifyingApplication", @"C:\x\setup.exe"));

    [Fact]
    public void Firewall_rules_of_one_program_are_one_finding_inbound_High_outbound_only_Medium()
    {
        var events = Enumerable.Range(0, 6).Select(i => Fw(i, @"C:\Users\owner\AppData\Local\Vendor\srv.exe", i % 2 == 0 ? "1" : "2")).ToList();
        events.AddRange(Enumerable.Range(10, 3).Select(i => Fw(i, @"C:\Users\owner\AppData\Local\Vendor\out.exe", "2")));
        var f = Correlation.Run(events).Where(x => x.RuleId == "FIREWALL-RULE-USERPATH").ToList();
        Assert.Equal(2, f.Count);
        var srv = Assert.Single(f, x => x.File.EndsWith("srv.exe"));
        Assert.Equal(Severity.High, srv.Severity);
        Assert.Equal(6, srv.SupportingEvidence.Count);
        Assert.Contains("6 reguli", srv.Title);
        var outb = Assert.Single(f, x => x.File.EndsWith("out.exe"));
        Assert.Equal(Severity.Medium, outb.Severity);
        Assert.Equal(3, outb.SupportingEvidence.Count);
    }

    // ---- 4. posture: Internet-connected station ----

    [Fact]
    public void On_a_connected_station_no_air_gap_finding_and_a_write_to_a_stick_without_a_register_is_not_above_Low()
    {
        var scope = new CaseScope { Network = NetworkCategory.Connected, Classification = ClassificationLevel.Unclassified };
        var w = Sec(4663, 5, ("ObjectName", @"E:\a.txt"), ("AccessMask", "0x2"));
        var f = Go([Usb("AA001", letter: "E:"), w], scope);
        Assert.DoesNotContain(f, x => x.RuleId.StartsWith("AIRGAP-", StringComparison.Ordinal));
        Assert.All(f.Where(x => x.RuleId.StartsWith("MEDIA-", StringComparison.Ordinal)), x => Assert.True(x.Severity <= Severity.Low, x.RuleId + " " + x.Severity));
        Assert.Contains(f, x => x.RuleId == "MEDIA-FILE-ACTIVITY");
    }
}
