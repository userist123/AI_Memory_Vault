using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
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

    // ---- planner #13 D1: SOFTWARE subkey hives (Quick profile). reg save of a subkey makes the subkey the ROOT of the file. ----

    private const string UacSub = @"Microsoft\Windows\CurrentVersion\Policies\System", DefSub = @"Microsoft\Windows Defender", PolSub = @"Policies\Microsoft\Windows Defender",
        InstSub = @"Policies\Microsoft\Windows\Installer", FwSub = @"Policies\Microsoft\WindowsFirewall";

    private static string Save(string dir, string name, RegfBuilder.Key root)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, RegfBuilder.Build(root, name));
        return path;
    }

    private static EvidenceItem SubkeyItem(string id, string subkey, string sha = "aa") => new()
    {
        EvidenceId = id, CaseId = "C", Source = @"Registry:SOFTWARE\" + subkey, SourceType = ConfigurationHiveReader.SubkeyHiveType,
        OriginalPath = @"HKLM\SOFTWARE\" + subkey, Sha256 = sha, TemporalType = TemporalType.CurrentSnapshot,
    };

    [Fact]
    public void Subkey_hives_map_their_root_to_the_full_path_and_give_uac_and_defender_exclusion_findings_with_the_full_locator()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-d1-" + Guid.NewGuid().ToString("N"));
        try
        {
            var uac = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            uac.Value("EnableLUA", RegfBuilder.Dword, 0);
            var def = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            def.Sub(@"Exclusions\Paths").Value(@"C:\Tools", RegfBuilder.Dword, 0);
            def.Sub(@"Real-Time Protection").Value("DisableRealtimeMonitoring", RegfBuilder.Dword, 0);
            var pol = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            var h = ConfigurationHiveReader.ReadSoftwareSubkeys(
            [
                (SubkeyItem("E-0010", UacSub, "s10"), Save(dir, "uac", uac)),
                (SubkeyItem("E-0011", DefSub, "s11"), Save(dir, "def", def)),
                (SubkeyItem("E-0012", PolSub, "s12"), Save(dir, "pol", pol)),
                (SubkeyItem("E-0014", InstSub, "s14"), Save(dir, "inst", new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten))),
                (SubkeyItem("E-0015", FwSub, "s15"), Save(dir, "fw", new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten))),
            ]);
            Assert.Equal("SOFTWARE", h.Hive);
            Assert.Equal("", h.Origin);
            Assert.Equal("0", h.Values[ConfigurationAudit.UacEnable.Locator].Data);
            var res = Evaluate(h);
            Assert.Equal([ConfigurationAudit.RuleUac, ConfigurationAudit.RuleDefenderExcl], res.Findings.Select(f => f.RuleId));
            var u = res.Findings[0].SupportingEvidence.Single();
            Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA", u.Locator);   // the full-path form, as "reg query" needs
            Assert.Equal("EnableLUA = 0", u.Quote);
            Assert.Equal("E-0010", u.EvidenceId);   // the subkey file the value was read from
            Assert.Equal("s10", u.Sha256);
            Assert.Equal("Registry LastWrite (cheie)", u.TimeSource);
            var x = res.Findings[1];
            Assert.Equal(@"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths", x.SupportingEvidence.Single().Locator);
            Assert.Equal("E-0011", x.SupportingEvidence.Single().EvidenceId);
            Assert.Contains(@"căi: C:\Tools", x.Description);
            Assert.Equal(new DateTimeOffset(SyntheticCorpus.HiveWritten), res.Findings[0].FirstSeenUtc);
            // the policy hives are empty: their keys are absent (not gaps); the only gap left is the Winlogon key, which Quick never saves (it can hold DefaultPassword)
            Assert.Equal(@"Setare SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoAdminLogon", Assert.Single(res.Gaps).Artifact);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_subkey_that_was_not_saved_leaves_its_settings_as_gaps_never_as_safe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-d1-" + Guid.NewGuid().ToString("N"));
        try
        {
            var uac = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            uac.Value("EnableLUA", RegfBuilder.Dword, 1);
            var h = ConfigurationHiveReader.ReadSoftwareSubkeys([(SubkeyItem("E-0010", UacSub), Save(dir, "uac", uac))]);
            var res = Evaluate(h);
            Assert.Empty(res.Findings);
            Assert.Contains(res.Gaps, g => g.Artifact.EndsWith("DisableRealtimeMonitoring"));
            Assert.Contains(res.Gaps, g => g.Artifact.Contains("Excluderi Defender"));
            Assert.DoesNotContain(res.Gaps, g => g.Artifact.EndsWith("EnableLUA"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void SubkeyOf_reads_the_subkey_from_OriginalPath_and_only_for_the_subkey_type()
    {
        Assert.Equal(UacSub, ConfigurationHiveReader.SubkeyOf(SubkeyItem("E-1", UacSub)));
        Assert.Null(ConfigurationHiveReader.SubkeyOf(new EvidenceItem { EvidenceId = "E-2", CaseId = "C", Source = "s", SourceType = "software_hive", OriginalPath = @"HKLM\SOFTWARE" }));
        Assert.Null(ConfigurationHiveReader.SubkeyOf(new EvidenceItem { EvidenceId = "E-3", CaseId = "C", Source = "s", SourceType = ConfigurationHiveReader.SubkeyHiveType, OriginalPath = @"HKLM\SOFTWARE" }));
    }

    [Fact]
    public void Pipeline_prefers_the_full_software_hive_when_both_exist_and_uses_the_subkey_hives_when_only_they_exist()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-d1p-" + Guid.NewGuid().ToString("N"));
        try
        {
            // the subkey hive says UAC is OFF; the full hive says it is ON: the full hive wins
            var sub = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            sub.Value("EnableLUA", RegfBuilder.Dword, 0);
            var full = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            full.Sub(UacSub).Value("EnableLUA", RegfBuilder.Dword, 1);
            int n = 0;
            string Id() => $"F-{++n:D4}";

            var onlySub = InvestigationPipeline.NewCase(Path.Combine(dir, "c1"), "d1", TestScopes.Valid());
            onlySub.RegisterStored(Save(onlySub.RawDir("Registry"), "SOFTWARE-Policies-System", sub), @"HKLM\SOFTWARE\" + UacSub, @"Registry:SOFTWARE\" + UacSub, ConfigurationHiveReader.SubkeyHiveType, TemporalType.CurrentSnapshot, "t", "1");
            var r1 = InvestigationPipeline.ConfigurationFindings(onlySub, Id);
            Assert.Contains(r1.Findings, f => f.RuleId == ConfigurationAudit.RuleUac);

            var both = InvestigationPipeline.NewCase(Path.Combine(dir, "c2"), "d1", TestScopes.Valid());
            both.RegisterStored(Save(both.RawDir("Registry"), "SOFTWARE-Policies-System", sub), @"HKLM\SOFTWARE\" + UacSub, @"Registry:SOFTWARE\" + UacSub, ConfigurationHiveReader.SubkeyHiveType, TemporalType.CurrentSnapshot, "t", "1");
            both.RegisterStored(Save(both.RawDir("Registry"), "SOFTWARE", full), @"HKLM\SOFTWARE", "Registry:SOFTWARE", "software_hive", TemporalType.CurrentSnapshot, "t", "1");
            var r2 = InvestigationPipeline.ConfigurationFindings(both, Id);
            Assert.DoesNotContain(r2.Findings, f => f.RuleId == ConfigurationAudit.RuleUac);
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }

    [Fact]
    public void An_absent_policy_key_marker_means_absent_values_not_gaps_while_an_unsaved_key_without_marker_stays_a_gap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-d1m-" + Guid.NewGuid().ToString("N"));
        try
        {
            var uac = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            uac.Value("EnableLUA", RegfBuilder.Dword, 0);
            var def = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            def.Sub(@"Exclusions\Paths").Value(@"C:\Tools", RegfBuilder.Dword, 0);
            Directory.CreateDirectory(dir);
            var markerPath = Path.Combine(dir, "pol.absent.json");
            File.WriteAllText(markerPath, "{\"key\":\"policy\",\"exists\":false}");   // the collector writes the escaped key here; the reader only needs exists=false
            var marker = SubkeyItem("E-0013", PolSub, "s13");
            marker = new EvidenceItem { EvidenceId = marker.EvidenceId, CaseId = "C", Source = marker.Source, SourceType = ConfigurationHiveReader.AbsentType, OriginalPath = marker.OriginalPath, Sha256 = "s13", TemporalType = TemporalType.CurrentSnapshot };
            var empty = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
            var hives = new[] { (SubkeyItem("E-0010", UacSub, "s10"), Save(dir, "uac", uac)), (SubkeyItem("E-0011", DefSub, "s11"), Save(dir, "def", def)),
                                (SubkeyItem("E-0014", InstSub, "s14"), Save(dir, "inst", empty)), (SubkeyItem("E-0015", FwSub, "s15"), Save(dir, "fw", empty)) };

            var withMarker = Evaluate(ConfigurationHiveReader.ReadSoftwareSubkeys(hives, [(marker, markerPath)]));
            Assert.Equal(@"Setare SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoAdminLogon", Assert.Single(withMarker.Gaps).Artifact);   // no gap for the policy key; only the Winlogon one (never saved in Quick)
            Assert.Equal([ConfigurationAudit.RuleUac, ConfigurationAudit.RuleDefenderExcl], withMarker.Findings.Select(f => f.RuleId));

            var withoutMarker = Evaluate(ConfigurationHiveReader.ReadSoftwareSubkeys(hives));
            Assert.Contains(withoutMarker.Gaps, g => g.Artifact.EndsWith("AntiSpyware") || g.Artifact.Contains(@"Policies\Microsoft\Windows Defender"));

            // a marker that does not say exists=false proves nothing
            File.WriteAllText(markerPath, "{\"exists\":true}");
            Assert.NotEmpty(Evaluate(ConfigurationHiveReader.ReadSoftwareSubkeys(hives, [(marker, markerPath)])).Gaps);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
