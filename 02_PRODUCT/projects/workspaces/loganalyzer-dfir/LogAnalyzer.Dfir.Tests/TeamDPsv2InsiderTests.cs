using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Team D: CFG-PSV2 (CBS package CurrentState, not the PowerShellEngine leftover) and CFG-OS-INSIDER (WindowsSelfHost\Applicability instead of the unknown-build gap).</summary>
public sealed class TeamDPsv2InsiderTests
{
    private const string CurVer = @"Microsoft\Windows NT\CurrentVersion";
    private const string Pkg = @"Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages\Microsoft-Windows-PowerShell-V2-Client-Package~31bf3856ad364e35~amd64~~10.0.26100.1";
    private const string PkgLang = @"Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages\Microsoft-Windows-PowerShell-V2-Client-Package~31bf3856ad364e35~amd64~ro-RO~10.0.26100.1";

    private static ConfigHive Soft(string build, Action<RegfBuilder.Key>? fill = null)
    {
        var r = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
        var k = r.Sub(CurVer);
        k.Value("ProductName", RegfBuilder.String, "Windows 11 Pro");
        k.Value("EditionID", RegfBuilder.String, "Professional");
        k.Value("DisplayVersion", RegfBuilder.String, "25H2");
        k.Value("CurrentBuild", RegfBuilder.String, build);
        k.Value("UBR", RegfBuilder.Dword, 1);
        fill?.Invoke(r);
        using var ms = new MemoryStream(RegfBuilder.Build(r, "SOFTWARE"));
        var h = ConfigurationHiveReader.Read(new RawRegistry(ms), "E-0001", "sha-1", "SOFTWARE");
        h.CollectedUtc = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);
        return h;
    }

    private static ConfigAuditResult Eval(ConfigHive h) { int n = 0; return ConfigurationAudit.Evaluate([h], "WS", () => $"F-{++n:D4}"); }

    [Fact]
    public void Psv2_installed_state_0x70_gives_a_low_finding_with_the_package_and_a_dism_step()
    {
        var res = Eval(Soft("19045", r => r.Sub(Pkg).Value("CurrentState", RegfBuilder.Dword, 0x70)));
        var f = Assert.Single(res.Findings, x => x.RuleId == "CFG-PSV2");
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("Motorul PowerShell 2.0 este instalat (pachetul CBS Microsoft-Windows-PowerShell-V2-Client-Package~31bf3856ad364e35~amd64~~10.0.26100.1, CurrentState = 0x70)", f.Title);
        Assert.DoesNotContain(res.Gaps, g => g.Artifact.Contains("PowerShell 2.0"));
        var ev = Assert.Single(f.SupportingEvidence);
        Assert.EndsWith(@"\CurrentState", ev.Locator);
        Assert.Contains("MicrosoftWindowsPowerShellV2", string.Join("\n", ManualVerification.For(ev, null, @"C:\Cazuri\C1").Select(s => s.Action)));
    }

    [Fact]
    public void Psv2_other_state_gives_nothing()
    {
        var res = Eval(Soft("19045", r => r.Sub(Pkg).Value("CurrentState", RegfBuilder.Dword, 0x40)));
        Assert.DoesNotContain(res.Findings, x => x.RuleId == "CFG-PSV2");
        Assert.DoesNotContain(res.Gaps, g => g.Artifact.Contains("PowerShell 2.0"));
    }

    [Fact]
    public void Psv2_language_specific_package_is_ignored_and_neutral_one_is_used()
    {
        var res = Eval(Soft("19045", r => { r.Sub(PkgLang).Value("CurrentState", RegfBuilder.Dword, 0x70); r.Sub(Pkg).Value("CurrentState", RegfBuilder.Dword, 0x40); }));
        Assert.DoesNotContain(res.Findings, x => x.RuleId == "CFG-PSV2");
    }

    [Fact]
    public void Psv2_engine_leftover_alone_is_not_a_finding_regression()
    {
        // PowerShell\1\PowerShellEngine PowerShellVersion = 2.0 stays after the engine is removed: it must never produce the finding
        var res = Eval(Soft("26900", r => r.Sub(@"Microsoft\PowerShell\1\PowerShellEngine").Value("PowerShellVersion", RegfBuilder.String, "2.0")));
        Assert.DoesNotContain(res.Findings, x => x.RuleId == "CFG-PSV2");
    }

    [Fact]
    public void Psv2_missing_package_is_silent_from_build_26100_and_a_gap_below()
    {
        var modern = Eval(Soft("26100"));
        Assert.DoesNotContain(modern.Findings, x => x.RuleId == "CFG-PSV2");
        Assert.DoesNotContain(modern.Gaps, g => g.Artifact.Contains("PowerShell 2.0"));
        var old = Eval(Soft("19045"));
        var gap = Assert.Single(old.Gaps, g => g.Artifact.Contains("PowerShell 2.0"));
        Assert.Contains("starea PowerShell 2.0 nu poate fi stabilită din registru", gap.Reason);
    }

    [Fact]
    public void Insider_key_gives_an_info_finding_instead_of_the_unknown_build_gap()
    {
        var res = Eval(Soft("26900", r =>
        {
            var k = r.Sub(@"Microsoft\WindowsSelfHost\Applicability");
            k.Value("BranchName", RegfBuilder.String, "CanaryChannel");
            k.Value("Ring", RegfBuilder.String, "External");
            k.Value("ContentType", RegfBuilder.String, "Mainline");
        }));
        var f = Assert.Single(res.Findings, x => x.RuleId == "CFG-OS-INSIDER");
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("Windows Insider (CanaryChannel/External): versiune de test, fără date de sfârșit de suport în tabel", f.Title);
        Assert.Contains(f.SupportingEvidence, e => e.Locator == @"SOFTWARE\Microsoft\WindowsSelfHost\Applicability\BranchName");
        Assert.DoesNotContain(res.Gaps, g => g.Reason.Contains("compilare necunoscută"));
        var ev = f.SupportingEvidence[0];
        Assert.Contains(ManualVerification.For(ev, null, @"C:\Cazuri\C1"), s => s.Action.StartsWith("reg query"));
    }

    [Fact]
    public void Without_the_insider_key_the_unknown_build_gap_stays()
    {
        var res = Eval(Soft("26900"));
        Assert.DoesNotContain(res.Findings, x => x.RuleId == "CFG-OS-INSIDER");
        Assert.Contains(res.Gaps, g => g.Reason.Contains("compilare necunoscută în tabel (posibil Insider/Canary)"));
    }

    [Fact]
    public void New_rule_ids_have_owner_actions_and_contracts()
    {
        Assert.StartsWith("Dezactivați caracteristica Windows PowerShell 2.0", OwnerActions.ForRule("CFG-PSV2"));
        Assert.StartsWith("Folosiți o versiune Insider doar pe un calculator de test", OwnerActions.ForRule("CFG-OS-INSIDER"));
    }
}
