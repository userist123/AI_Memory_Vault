using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Team A G1 (Defender AMSI resource, grouping by file name across threat sets) and G4 (4104 script text is not an executed command); synthetic events only.</summary>
public class TeamAG1G4Tests
{
    private const string Defender = "Microsoft-Windows-Windows Defender/Operational";

    private static TimelineEvent Det(int id, double minute, string path, string threat, string process = "", string actionId = "9") =>
        Ev(Defender, id, minute, "", ("Path", path), ("Threat Name", threat), ("Action ID", actionId), ("Process Name", process), ("Detection User", @"PC\owner"));

    private static List<Finding> Def(params TimelineEvent[] ev) => Correlation.Run(ev.ToList()).Where(x => x.RuleId == "DEF-DETECTION").ToList();

    [Fact]
    public void Amsi_resource_gets_an_amsi_title_with_the_process_and_no_pseudo_file_name()
    {
        const string content = "builtin unalias -- 'unsetenv'";
        var f = Assert.Single(Def(Det(1116, 0, "amsi:_" + content, "Trojan:PowerShell/Test.A", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")));
        Assert.StartsWith("Microsoft Defender a blocat conținut scanat în memorie (AMSI) de powershell.exe: Trojan:PowerShell/Test.A", f.Title);
        Assert.DoesNotContain(" în builtin", f.Title);
        Assert.Equal("", f.File);
        Assert.Contains("nu este un fișier pe disc", f.Description);
        Assert.Contains("„" + content + "”", f.Description);
        Assert.Contains("Ce NU dovedește", f.Description);
        Assert.Contains("doar conține textul", f.Description);
    }

    [Fact]
    public void One_dll_with_two_threat_sets_is_one_finding_listing_each_threat_with_count_and_max_severity()
    {
        const string Dll = @"C:\dev\wt1\App.Tests\bin\Debug\net10.0\App.Tests.dll";
        var f = Assert.Single(Def(
            Det(1116, 0, "file:_" + Dll, "HackTool:Win32/PsAttack.A"), Det(1116, 1, "file:_" + Dll, "HackTool:Win32/PsAttack.A"),
            Det(1116, 5, "file:_" + Dll, "Trojan:Win32/Wacatac.H!ml"), Det(1117, 6, "file:_" + Dll, "Trojan:Win32/Wacatac.H!ml", "", "2")));
        Assert.Contains("App.Tests.dll", f.Title);
        Assert.Contains("HackTool:Win32/PsAttack.A (2 detecții, prima 2026-10-01 10:00, ultima 2026-10-01 10:01 UTC)", f.Description);
        Assert.Contains("Trojan:Win32/Wacatac.H!ml (1 detecții", f.Description);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(4, f.SupportingEvidence.Count);
    }

    [Fact]
    public void Different_file_names_stay_separate()
    {
        Assert.Equal(2, Def(Det(1116, 0, @"file:_C:\a\one.dll", "T1"), Det(1116, 1, @"file:_C:\a\two.dll", "T2")).Count);
    }

    private static TimelineEvent Script4104(string text, string path) =>
        Ev("Microsoft-Windows-PowerShell/Operational", 4104, 0, "Microsoft-Windows-PowerShell", ("ScriptBlockText", text), ("Path", path));

    private static string Vssadmin => $"vss{Destructive.Cut}admin delete sha{Destructive.Cut}dows /all /quiet";

    [Fact]
    public void Vss_text_in_a_dev_script_4104_gets_the_dev_alternative_and_the_text_not_action_sentence()
    {
        var f = Assert.Single(Run([Script4104(Vssadmin, @"C:\Users\x\source\repos\app\Tests.ps1")]).Where(x => x.RuleId == "VSS-SNAPSHOT-DELETED"));
        Assert.Contains("Windows a văzut acest text într-un bloc de script PowerShell; nu dovedește că acțiunea s-a executat", f.Description);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("dezvoltare"));
    }

    [Fact]
    public void Vss_4104_outside_dev_folders_has_the_sentence_but_no_dev_alternative()
    {
        var f = Assert.Single(Run([Script4104(Vssadmin, @"C:\Windows\Temp\x.ps1")]).Where(x => x.RuleId == "VSS-SNAPSHOT-DELETED"));
        Assert.Contains("nu dovedește că acțiunea s-a executat", f.Description);
        Assert.DoesNotContain(f.AlternativeExplanations, a => a.Contains("dezvoltare"));
    }

    [Fact]
    public void Vss_real_4688_process_creation_is_unchanged()
    {
        var f = Assert.Single(Run([Sec(4688, 0, ("NewProcessName", @"C:\Windows\System32\vssadmin.exe"), ("CommandLine", Vssadmin))]).Where(x => x.RuleId == "VSS-SNAPSHOT-DELETED"));
        Assert.DoesNotContain("bloc de script", f.Description);
        Assert.DoesNotContain(f.AlternativeExplanations, a => a.Contains("dezvoltare"));
    }

    [Fact]
    public void Defender_tamper_from_dev_4104_text_gets_the_distinction_and_stays_High()
    {
        var f = Assert.Single(Correlation.Run([Script4104("Add-MpPreference -ExclusionPath C:\\x", @"C:\repos\app\Tests.ps1")]).Where(x => x.RuleId == "DEF-TAMPER"));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("nu dovedește că acțiunea s-a executat", f.PlainWhat + f.Description);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("dezvoltare"));
    }
}
