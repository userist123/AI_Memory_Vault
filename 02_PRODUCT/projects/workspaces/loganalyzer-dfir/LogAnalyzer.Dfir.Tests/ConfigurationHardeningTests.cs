using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #14 E2: CFG-FIREWALL-OFF, CFG-REMOTEREGISTRY, CFG-AUTOLOGON, CFG-LOCALACCOUNT-TOKENFILTER, CFG-ALWAYSINSTALLELEVATED, CFG-SMB-SIGNING from synthetic hives (RegfBuilder).
/// Each rule fires only when unsafe; a probe that was not read (Quick: outside the saved subkeys) is a gap, never "absent". The autologon password is never read into the audit.</summary>
public sealed class ConfigurationHardeningTests
{
    private const string Pwd = "Zx9-Secret-Passw0rd!";
    private const string FwKey = @"Services\SharedAccess\Parameters\FirewallPolicy\";
    private const string PolKey = @"Policies\Microsoft\WindowsFirewall\";
    private const string Winlogon = @"Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string UacSub = @"Microsoft\Windows\CurrentVersion\Policies\System";
    private const string InstSub = @"Policies\Microsoft\Windows\Installer", FwSub = @"Policies\Microsoft\WindowsFirewall";

    private static RegfBuilder.Key NewRoot() => new("ROOT", SyntheticCorpus.HiveWritten);

    private static ConfigHive Load(RegfBuilder.Key root, string label, string id = "E-0001")
    {
        using var ms = new MemoryStream(RegfBuilder.Build(root, label));
        return ConfigurationHiveReader.Read(new RawRegistry(ms), id, "sha-" + id, label);
    }

    private static ConfigHive Sys(Action<RegfBuilder.Key> fill)
    {
        var r = NewRoot();
        r.Sub("Select").Value("Current", RegfBuilder.Dword, 1);
        r.Sub("ControlSet001");
        fill(r);
        return Load(r, "SYSTEM", "E-0002");
    }

    private static ConfigHive Soft(Action<RegfBuilder.Key> fill) { var r = NewRoot(); fill(r); return Load(r, "SOFTWARE"); }

    private static ConfigAuditResult Evaluate(params ConfigHive[] hives) { int n = 0; return ConfigurationAudit.Evaluate(hives, "WS", () => $"F-{++n:D4}"); }

    private static Finding One(ConfigAuditResult r, string rule) => Assert.Single(r.Findings, f => f.RuleId == rule);

    private static void Dw(RegfBuilder.Key r, string path, string name, int v) => r.Sub(path).Value(name, RegfBuilder.Dword, v);

    // ---- firewall ----

    [Fact]
    public void Firewall_off_lists_the_profiles_that_are_off_with_the_local_source_and_nothing_when_on_or_absent()
    {
        var sys = Sys(r =>
        {
            Dw(r, @"ControlSet001\" + FwKey + "DomainProfile", "EnableFirewall", 0);
            Dw(r, @"ControlSet001\" + FwKey + "StandardProfile", "EnableFirewall", 1);
            Dw(r, @"ControlSet001\" + FwKey + "PublicProfile", "EnableFirewall", 0);
        });
        var res = Evaluate(sys, Soft(_ => { }));
        Assert.Empty(res.Gaps);
        var f = One(res, ConfigurationAudit.RuleFirewallOff);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(SemanticType.Configuration, f.SemanticType);
        Assert.Equal("Configurație", f.Category);
        Assert.Contains("Domain, Public", f.Title);
        Assert.DoesNotContain("Private", f.Title);
        Assert.Equal(
        [
            @"SYSTEM\ControlSet001\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile\EnableFirewall",
            @"SYSTEM\ControlSet001\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile\EnableFirewall",
        ], f.SupportingEvidence.Select(e => e.Locator));
        Assert.All(f.SupportingEvidence, e => { Assert.Equal("EnableFirewall = 0", e.Quote); Assert.Equal("Registry LastWrite (cheie)", e.TimeSource); Assert.Equal("Registry", e.Channel); });
        Assert.Contains("setarea locală", f.Description);

        // all on, or the values absent (Windows default = on): nothing
        var safe = Evaluate(Sys(r => { Dw(r, @"ControlSet001\" + FwKey + "DomainProfile", "EnableFirewall", 1); }), Soft(_ => { }));
        Assert.DoesNotContain(safe.Findings, f => f.RuleId == ConfigurationAudit.RuleFirewallOff);   // (RunAsPPL absent in this hive gives its own low finding)
        Assert.Empty(safe.Gaps);
    }

    [Fact]
    public void A_firewall_policy_value_wins_over_the_local_one_and_the_finding_says_which_source_decided()
    {
        var sys = Sys(r =>
        {
            Dw(r, @"ControlSet001\" + FwKey + "DomainProfile", "EnableFirewall", 0);     // local off, policy on: on
            Dw(r, @"ControlSet001\" + FwKey + "StandardProfile", "EnableFirewall", 1);   // local on, policy off: off by policy
        });
        var soft = Soft(r =>
        {
            Dw(r, PolKey + "DomainProfile", "EnableFirewall", 1);
            Dw(r, PolKey + "StandardProfile", "EnableFirewall", 0);
        });
        var f = One(Evaluate(sys, soft), ConfigurationAudit.RuleFirewallOff);
        Assert.Contains("Private (Standard)", f.Title);
        Assert.DoesNotContain("Domain", f.Title);
        var e = Assert.Single(f.SupportingEvidence);
        Assert.Equal(@"SOFTWARE\Policies\Microsoft\WindowsFirewall\StandardProfile\EnableFirewall", e.Locator);
        Assert.Equal("E-0001", e.EvidenceId);   // the SOFTWARE hive item, not the SYSTEM one
        Assert.Contains("politica", f.Description);

        // policy on everywhere: nothing, whatever the local values say
        var none = Evaluate(sys, Soft(r => { Dw(r, PolKey + "DomainProfile", "EnableFirewall", 1); Dw(r, PolKey + "StandardProfile", "EnableFirewall", 1); }));
        Assert.DoesNotContain(none.Findings, f => f.RuleId == ConfigurationAudit.RuleFirewallOff);
    }

    [Fact]
    public void Quick_without_the_firewall_policy_subkey_is_a_gap_and_the_local_off_finding_says_the_policy_was_not_confirmed()
    {
        var sys = Sys(r => Dw(r, @"ControlSet001\" + FwKey + "PublicProfile", "EnableFirewall", 0));
        var quick = new ConfigHive { Hive = "SOFTWARE", EvidenceId = "E-0010" };
        foreach (var p in ConfigurationAudit.Probes("SOFTWARE", "ControlSet001").Where(p => p.Key.StartsWith(UacSub, StringComparison.Ordinal)))
            quick.Values[p.Locator] = new ConfigValue(null, null);   // only the UAC subkey was saved
        var res = Evaluate(sys, quick);
        Assert.Contains(res.Gaps, g => g.Artifact.EndsWith(@"WindowsFirewall\PublicProfile\EnableFirewall"));
        var f = One(res, ConfigurationAudit.RuleFirewallOff);
        Assert.Contains(f.Limitations, l => l.Contains("nu s-a putut confirma"));
    }

    // ---- remote registry, smb signing ----

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void RemoteRegistry_only_when_automatic(int start, bool expected)
    {
        var res = Evaluate(Sys(r => Dw(r, @"ControlSet001\Services\RemoteRegistry", "Start", start)));
        Assert.Equal(expected, res.Findings.Any(f => f.RuleId == ConfigurationAudit.RuleRemoteRegistry));
        if (!expected) return;
        var f = One(res, ConfigurationAudit.RuleRemoteRegistry);
        Assert.Equal(Severity.Medium, f.Severity);
        var e = Assert.Single(f.SupportingEvidence);
        Assert.Equal(@"SYSTEM\ControlSet001\Services\RemoteRegistry\Start", e.Locator);
        Assert.Equal("Start = 2", e.Quote);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void SmbSigning_is_low_with_honest_wording_and_only_when_the_value_is_zero(int v, bool expected)
    {
        var res = Evaluate(Sys(r => Dw(r, @"ControlSet001\Services\LanmanServer\Parameters", "RequireSecuritySignature", v)));
        Assert.Equal(expected, res.Findings.Any(f => f.RuleId == ConfigurationAudit.RuleSmbSigning));
        if (!expected) return;
        var f = One(res, ConfigurationAudit.RuleSmbSigning);
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal(@"SYSTEM\ControlSet001\Services\LanmanServer\Parameters\RequireSecuritySignature", f.SupportingEvidence.Single().Locator);
        Assert.Equal("RequireSecuritySignature = 0", f.SupportingEvidence.Single().Quote);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("24H2"));
    }

    [Fact]
    public void Absent_remote_registry_and_signing_values_give_nothing()
    {
        var res = Evaluate(Sys(_ => { }));
        Assert.DoesNotContain(res.Findings, f => f.RuleId is ConfigurationAudit.RuleRemoteRegistry or ConfigurationAudit.RuleSmbSigning or ConfigurationAudit.RuleFirewallOff);
        Assert.Empty(res.Gaps);
    }

    // ---- local account token filter, always install elevated ----

    [Fact]
    public void TokenFilter_works_from_the_Quick_UAC_subkey_hive_too()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-e2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var uac = NewRoot(); uac.Value("LocalAccountTokenFilterPolicy", RegfBuilder.Dword, 1);
            var path = Path.Combine(dir, "uac"); File.WriteAllBytes(path, RegfBuilder.Build(uac, "uac"));
            var item = new EvidenceItem { EvidenceId = "E-0010", CaseId = "C", Source = "s", SourceType = ConfigurationHiveReader.SubkeyHiveType, OriginalPath = @"HKLM\SOFTWARE\" + UacSub, Sha256 = "s10", TemporalType = TemporalType.CurrentSnapshot };
            var f = One(Evaluate(ConfigurationHiveReader.ReadSoftwareSubkeys([(item, path)])), ConfigurationAudit.RuleTokenFilter);
            Assert.Equal(Severity.Medium, f.Severity);
            var e = Assert.Single(f.SupportingEvidence);
            Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\LocalAccountTokenFilterPolicy", e.Locator);
            Assert.Equal("LocalAccountTokenFilterPolicy = 1", e.Quote);
            Assert.Equal("E-0010", e.EvidenceId);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void TokenFilter_zero_or_absent_gives_nothing()
    {
        Assert.DoesNotContain(Evaluate(Soft(r => Dw(r, UacSub, "LocalAccountTokenFilterPolicy", 0))).Findings, f => f.RuleId == ConfigurationAudit.RuleTokenFilter);
        Assert.DoesNotContain(Evaluate(Soft(_ => { })).Findings, f => f.RuleId == ConfigurationAudit.RuleTokenFilter);
    }

    [Fact]
    public void AlwaysInstallElevated_is_high_in_HKLM_and_says_that_HKCU_is_not_read()
    {
        var f = One(Evaluate(Soft(r => Dw(r, InstSub, "AlwaysInstallElevated", 1))), ConfigurationAudit.RuleAlwaysInstall);
        Assert.Equal(Severity.High, f.Severity);
        var e = Assert.Single(f.SupportingEvidence);
        Assert.Equal(@"SOFTWARE\Policies\Microsoft\Windows\Installer\AlwaysInstallElevated", e.Locator);
        Assert.Equal("AlwaysInstallElevated = 1", e.Quote);
        Assert.Contains(f.Limitations, l => l.Contains("HKCU") && l.Contains("nu este citită"));
        Assert.DoesNotContain(Evaluate(Soft(r => Dw(r, InstSub, "AlwaysInstallElevated", 0))).Findings, x => x.RuleId == ConfigurationAudit.RuleAlwaysInstall);
    }

    [Fact]
    public void Quick_probes_outside_the_saved_subkeys_are_gaps_never_absent_and_a_proven_absent_marker_is_not_a_gap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-e2q-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var uac = NewRoot(); uac.Value("EnableLUA", RegfBuilder.Dword, 1);
            var upath = Path.Combine(dir, "uac"); File.WriteAllBytes(upath, RegfBuilder.Build(uac, "uac"));
            EvidenceItem Item(string id, string sub, string type) => new() { EvidenceId = id, CaseId = "C", Source = "s", SourceType = type, OriginalPath = @"HKLM\SOFTWARE\" + sub, Sha256 = "s" + id, TemporalType = TemporalType.CurrentSnapshot };
            var hives = new[] { (Item("E-1", UacSub, ConfigurationHiveReader.SubkeyHiveType), upath) };

            var res = Evaluate(ConfigurationHiveReader.ReadSoftwareSubkeys(hives));
            Assert.Contains(res.Gaps, g => g.Artifact.EndsWith(@"Installer\AlwaysInstallElevated"));
            Assert.Contains(res.Gaps, g => g.Artifact.EndsWith(@"Winlogon\AutoAdminLogon"));
            Assert.Empty(res.Findings);

            var mpath = Path.Combine(dir, "inst.absent.json"); File.WriteAllText(mpath, "{\"exists\":false}");
            var res2 = Evaluate(ConfigurationHiveReader.ReadSoftwareSubkeys(hives, [(Item("E-2", InstSub, ConfigurationHiveReader.AbsentType), mpath)]));
            Assert.DoesNotContain(res2.Gaps, g => g.Artifact.EndsWith(@"Installer\AlwaysInstallElevated"));
            Assert.Contains(res2.Gaps, g => g.Artifact.EndsWith(@"Winlogon\AutoAdminLogon"));   // Winlogon is never saved in Quick
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    // ---- autologon ----

    private static ConfigHive Winl(string? auto, string? user, string? pwd) => Soft(r =>
    {
        var k = r.Sub(Winlogon);
        if (auto is not null) k.Value("AutoAdminLogon", RegfBuilder.String, auto);
        if (user is not null) k.Value("DefaultUserName", RegfBuilder.String, user);
        if (pwd is not null) k.Value("DefaultPassword", RegfBuilder.String, pwd);
    });

    [Fact]
    public void Autologon_with_a_stored_password_is_high_and_quotes_the_user_but_never_the_password()
    {
        var h = Winl("1", "kiosk", Pwd);
        Assert.DoesNotContain(h.Values.Values, v => (v.Data ?? "").Contains(Pwd));   // the reader never keeps it
        var res = Evaluate(h);
        var f = One(res, ConfigurationAudit.RuleAutologon);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoAdminLogon", f.SupportingEvidence[0].Locator);
        Assert.Equal("AutoAdminLogon = 1; DefaultUserName = kiosk; DefaultPassword = (prezentă, nu se afișează)", f.SupportingEvidence[0].Quote);
        Assert.Equal(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\DefaultPassword", f.SupportingEvidence[1].Locator);
        Assert.Empty(res.Gaps);

        // the password appears nowhere: not in the findings (all fields), not in the manual verification steps
        var all = JsonSerializer.Serialize(res) + JsonSerializer.Serialize(h.Values);
        Assert.DoesNotContain(Pwd, all);
        foreach (var e in f.SupportingEvidence)
            foreach (var s in ManualVerification.For(e, null, @"C:\Cazuri\C1"))
                Assert.DoesNotContain(Pwd, s.Action + s.Compare);
    }

    [Fact]
    public void Autologon_manual_verification_checks_that_the_password_exists_without_asking_to_copy_it()
    {
        var f = One(Evaluate(Winl("1", "kiosk", Pwd)), ConfigurationAudit.RuleAutologon);
        var item = new EvidenceItem { EvidenceId = "E-0001", CaseId = "C", Source = "Registry", SourceType = "software_hive", StoredPath = @"Raw\Registry\SOFTWARE", Sha256 = "x" };
        var a = ManualVerification.For(f.SupportingEvidence[0], item, @"C:\Cazuri\C1").First(s => s.IsCommand);
        Assert.Equal(@"reg query ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v AutoAdminLogon", a.Action);
        var b = ManualVerification.For(f.SupportingEvidence[1], item, @"C:\Cazuri\C1").First(s => s.IsCommand);
        Assert.Equal(@"reg query ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v DefaultPassword", b.Action);
        Assert.Contains("verificați doar că valoarea există; nu o copiați pe hârtie", b.Compare);
    }

    [Fact]
    public void Autologon_without_a_stored_password_is_medium_with_the_LSA_secrets_wording()
    {
        var f = One(Evaluate(Winl("1", "kiosk", null)), ConfigurationAudit.RuleAutologon);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("conectare automată fără parolă stocată în registru (parola poate fi în secretele LSA", f.Description);
        Assert.Equal("AutoAdminLogon = 1; DefaultUserName = kiosk; DefaultPassword = (lipsă)", f.SupportingEvidence[0].Quote);
        // an empty password value counts as no stored password
        Assert.Equal(Severity.Medium, One(Evaluate(Winl("1", "kiosk", "")), ConfigurationAudit.RuleAutologon).Severity);
    }

    [Theory]
    [InlineData("0")]
    [InlineData(null)]
    public void Autologon_off_or_absent_gives_nothing_even_with_a_password_value(string? auto)
    {
        var res = Evaluate(Winl(auto, "kiosk", Pwd));
        Assert.DoesNotContain(res.Findings, f => f.RuleId == ConfigurationAudit.RuleAutologon);
        Assert.Empty(res.Gaps);
    }

    [Fact]
    public void The_password_is_not_in_the_timeline_of_the_software_hive_parser()
    {
        var r = NewRoot();
        var k = r.Sub(Winlogon);
        k.Value("AutoAdminLogon", RegfBuilder.String, "1"); k.Value("DefaultUserName", RegfBuilder.String, "kiosk"); k.Value("DefaultPassword", RegfBuilder.String, Pwd);
        k.Value("Shell", RegfBuilder.String, "explorer.exe");
        var dir = Path.Combine(Path.GetTempPath(), "la-e2t-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "SOFTWARE"); File.WriteAllBytes(p, RegfBuilder.Build(r, "SOFTWARE"));
            var sink = new ListSink();
            new SoftwareHiveParser().Parse(new EvidenceItem { EvidenceId = "E-1", CaseId = "C", Source = "software_hive", SourceType = "software_hive", StoredPath = p }, p, sink, default);
            Assert.NotEmpty(sink.Events);   // the Winlogon Shell row is there
            var text = JsonSerializer.Serialize(sink.Events);
            Assert.DoesNotContain(Pwd, text);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Quick_winlogon_json_gives_the_autologon_probes_without_the_Winlogon_gap_and_a_malformed_json_stays_a_gap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-e2w-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "w.json");
            File.WriteAllText(path, "{\"key\":\"k\",\"AutoAdminLogon\":\"1\",\"DefaultUserName\":\"kiosk\",\"DefaultPasswordPresent\":true,\"note\":\"x\"}");
            var item = new EvidenceItem { EvidenceId = "E-7", CaseId = "C", Source = "s", SourceType = ConfigurationHiveReader.WinlogonType, OriginalPath = @"HKLM\SOFTWARE\" + Winlogon, Sha256 = "s7", TemporalType = TemporalType.CurrentSnapshot };
            var h = ConfigurationHiveReader.ReadSoftwareSubkeys([], null, (item, path));
            var f = One(Evaluate(h), ConfigurationAudit.RuleAutologon);
            Assert.Equal(Severity.High, f.Severity);
            Assert.Equal("Instantaneu la colectare", f.SupportingEvidence[0].TimeSource);
            Assert.Equal("E-7", f.SupportingEvidence[0].EvidenceId);
            Assert.DoesNotContain(Evaluate(h).Gaps, g => g.Artifact.Contains("Winlogon"));

            File.WriteAllText(path, "not json");
            Assert.Contains(Evaluate(ConfigurationHiveReader.ReadSoftwareSubkeys([], null, (item, path))).Gaps, g => g.Artifact.EndsWith(@"Winlogon\AutoAdminLogon"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
