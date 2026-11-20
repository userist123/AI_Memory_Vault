using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #11 A5: the configuration values are read from a stored hive (synthetic regf written by RegfBuilder) and give the findings with the key's LastWrite as time.</summary>
public sealed class ConfigurationHiveReaderTests
{
    private static ConfigAuditResult Evaluate(params ConfigHive[] hives) { int n = 0; return ConfigurationAudit.Evaluate(hives, "WS", () => $"F-{++n:D4}"); }

    [Fact]
    public void A_software_hive_gives_uac_defender_findings_and_reads_the_exclusion_names()
    {
        var root = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
        var sys = root.Sub(@"Microsoft\Windows\CurrentVersion\Policies\System");
        sys.Value("EnableLUA", RegfBuilder.Dword, 0);
        sys.Value("ConsentPromptBehaviorAdmin", RegfBuilder.Dword, 5);
        root.Sub(@"Microsoft\Windows Defender\Exclusions\Paths").Value(@"C:\Tools", RegfBuilder.Dword, 0);
        root.Sub(@"Microsoft\Windows Defender\Exclusions\Processes").Value("evil.exe", RegfBuilder.Dword, 0);
        var bytes = RegfBuilder.Build(root, "SOFTWARE");
        using var ms = new MemoryStream(bytes);
        var h = ConfigurationHiveReader.Read(new RawRegistry(ms), "E-0001", "ab", "SOFTWARE");
        var res = Evaluate(h);
        Assert.Empty(res.Gaps);
        Assert.Equal([ConfigurationAudit.RuleUac, ConfigurationAudit.RuleDefenderExcl], res.Findings.Select(f => f.RuleId));
        var uac = res.Findings[0];
        Assert.Equal(new DateTimeOffset(SyntheticCorpus.HiveWritten), uac.FirstSeenUtc);
        Assert.Equal("EnableLUA = 0", uac.SupportingEvidence[0].Quote);
        var ex = res.Findings[1];
        Assert.Contains(@"căi: C:\Tools", ex.Description);
        Assert.Contains("procese: evil.exe", ex.Description);
    }

    [Fact]
    public void A_system_hive_is_read_from_the_control_set_named_by_Select_Current()
    {
        var root = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
        root.Sub("Select").Value("Current", RegfBuilder.Dword, 2);
        root.Sub(@"ControlSet001\Control\SecurityProviders\WDigest").Value("UseLogonCredential", RegfBuilder.Dword, 0);
        root.Sub(@"ControlSet002\Control\SecurityProviders\WDigest").Value("UseLogonCredential", RegfBuilder.Dword, 1);
        root.Sub(@"ControlSet002\Control\Terminal Server").Value("fDenyTSConnections", RegfBuilder.Dword, 0);
        root.Sub(@"ControlSet002\Control\Terminal Server\WinStations\RDP-Tcp").Value("UserAuthentication", RegfBuilder.Dword, 0);
        using var ms = new MemoryStream(RegfBuilder.Build(root, "SYSTEM"));
        var h = ConfigurationHiveReader.Read(new RawRegistry(ms), "E-0002", "cd", "SYSTEM");
        Assert.Equal("ControlSet002", h.ControlSet);
        var res = Evaluate(h);
        var rules = res.Findings.Select(f => f.RuleId).ToList();
        Assert.Contains(ConfigurationAudit.RuleWdigest, rules);
        Assert.Contains(ConfigurationAudit.RuleRdpNla, rules);
        Assert.Contains(ConfigurationAudit.RuleLsa, rules);   // RunAsPPL is absent in this hive: low, honest wording
        Assert.Equal(@"SYSTEM\ControlSet002\Control\SecurityProviders\WDigest\UseLogonCredential", res.Findings.First(f => f.RuleId == ConfigurationAudit.RuleWdigest).SupportingEvidence.Single().Locator);
    }

    [Fact]
    public void A_hive_with_safe_values_and_no_exclusions_gives_nothing()
    {
        var root = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
        root.Sub(@"Microsoft\Windows\CurrentVersion\Policies\System").Value("EnableLUA", RegfBuilder.Dword, 1);
        using var ms = new MemoryStream(RegfBuilder.Build(root, "SOFTWARE"));
        var res = Evaluate(ConfigurationHiveReader.Read(new RawRegistry(ms), "E-0001", "ab", "SOFTWARE"));
        Assert.Empty(res.Findings);
        Assert.Empty(res.Gaps);
    }
}
