using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #11 A5: configuration findings from the stored SOFTWARE / SYSTEM hives; facts with exact locators and quotes, a gap (never a finding) for what cannot be read. Synthetic values only.</summary>
public sealed class ConfigurationFindingsTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { InvestigationReportPdf.TextObserver.Value = null; _k.Dispose(); }

    private const string Sha = "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12";
    private static readonly DateTimeOffset Written = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    private static ConfigHive SoftwareHive(string lua = "1", string consent = "5", string? rtpLocal = null, string? rtpPolicy = null, string? av = null, Dictionary<string, string[]?>? excl = null)
    {
        var h = new ConfigHive { Hive = "SOFTWARE", EvidenceId = "E-0001", Sha256 = Sha };
        void V(ConfigProbe p, string? d) => h.Values[p.Locator] = new ConfigValue(d, Written);
        V(ConfigurationAudit.UacEnable, lua); V(ConfigurationAudit.UacConsent, consent);
        V(ConfigurationAudit.RtpLocal, rtpLocal); V(ConfigurationAudit.RtpPolicy, rtpPolicy); V(ConfigurationAudit.AvPolicy, av);
        foreach (var key in ConfigurationAudit.ListKeys("SOFTWARE"))
            h.Lists["SOFTWARE\\" + key] = new ConfigNames(excl is not null && excl.TryGetValue(key, out var n) ? n : null, Written);
        foreach (var p in ConfigurationAudit.Probes("SOFTWARE", "ControlSet001")) h.Values.TryAdd(p.Locator, new ConfigValue(null, Written));   // planner #14 E2 probes: absent unless a test sets them
        return h;
    }

    private static ConfigHive SystemHive(string smb1 = "0", string wdigest = "0", string? ppl = "1", string deny = "1", string nla = "1", string cs = "ControlSet001")
    {
        var h = new ConfigHive { Hive = "SYSTEM", EvidenceId = "E-0002", Sha256 = Sha, ControlSet = cs };
        void V(ConfigProbe p, string? d) => h.Values[p.Locator] = new ConfigValue(d, Written);
        V(ConfigurationAudit.Smb1(cs), smb1); V(ConfigurationAudit.WDigest(cs), wdigest); V(ConfigurationAudit.Lsa(cs), ppl);
        V(ConfigurationAudit.RdpDeny(cs), deny); V(ConfigurationAudit.RdpNla(cs), nla);
        foreach (var p in ConfigurationAudit.Probes("SYSTEM", cs)) h.Values.TryAdd(p.Locator, new ConfigValue(null, Written));   // planner #14 E2 probes: absent unless a test sets them
        return h;
    }

    private static ConfigAuditResult Run(params ConfigHive[] hives) { int n = 0; return ConfigurationAudit.Evaluate(hives, "WS-ALFA-07", () => $"F-{++n:D4}"); }

    private static readonly string[] Verdicts = ["neconform", "conform", "NECONFORM", "CONFORM"];

    [Fact]
    public void Unsafe_software_values_give_findings_with_exact_locator_quote_channel_time_source_and_configuration_type()
    {
        var excl = new Dictionary<string, string[]?> { [@"Microsoft\Windows Defender\Exclusions\Paths"] = [@"C:\Tools", @"D:\Share"], [@"Microsoft\Windows Defender\Exclusions\Extensions"] = [".ps1"] };
        var res = Run(SoftwareHive(lua: "0", consent: "0", rtpLocal: "1", av: "1", excl: excl));
        Assert.Empty(res.Gaps);
        Assert.Equal([ConfigurationAudit.RuleUac, ConfigurationAudit.RuleUacPrompt, ConfigurationAudit.RuleDefenderOff, ConfigurationAudit.RuleDefenderExcl], res.Findings.Select(f => f.RuleId));

        var uac = res.Findings[0];
        Assert.Equal(Severity.High, uac.Severity);
        Assert.Equal("UAC este dezactivat: EnableLUA = 0", uac.Title);
        Assert.Equal(SemanticType.Configuration, uac.SemanticType);
        var e = Assert.Single(uac.SupportingEvidence);
        Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA", e.Locator);
        Assert.Equal("EnableLUA = 0", e.Quote);
        Assert.Equal("Registry", e.Channel);
        Assert.Equal("Registry LastWrite (cheie)", e.TimeSource);
        Assert.Equal(Sha, e.Sha256);
        Assert.Equal(Written, uac.FirstSeenUtc);
        Assert.Contains(uac.AlternativeExplanations, a => a.Contains("politica de grup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(uac.Limitations, l => l.Contains("nu arată de când", StringComparison.Ordinal));

        Assert.Equal("ConsentPromptBehaviorAdmin = 0", res.Findings[1].SupportingEvidence.Single().Quote);
        Assert.Equal(Severity.Medium, res.Findings[1].Severity);

        var def = res.Findings[2];
        Assert.Equal(["DisableRealtimeMonitoring = 1", "DisableAntiSpyware = 1"], def.SupportingEvidence.Select(x => x.Quote));
        Assert.Equal(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection\DisableRealtimeMonitoring", def.SupportingEvidence[0].Locator);
        Assert.Equal(@"SOFTWARE\Policies\Microsoft\Windows Defender\DisableAntiSpyware", def.SupportingEvidence[1].Locator);

        var ex = res.Findings[3];
        Assert.Contains(@"căi: C:\Tools", ex.Description);
        Assert.Contains(@"căi: D:\Share", ex.Description);
        Assert.Contains("extensii: .ps1", ex.Description);
        Assert.Equal(@"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths", ex.SupportingEvidence[0].Locator);
        Assert.Equal(@"2 excluderi: C:\Tools; D:\Share", ex.SupportingEvidence[0].Quote);

        foreach (var f in res.Findings)
            foreach (var w in Verdicts) Assert.DoesNotContain(w, f.Title + f.Description + f.PlainWhat + f.ClassificationReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsafe_system_values_use_the_control_set_of_the_hive_and_give_one_finding_each()
    {
        var res = Run(SystemHive(smb1: "1", wdigest: "1", ppl: "0", deny: "0", nla: "0", cs: "ControlSet002"));
        Assert.Empty(res.Gaps);
        Assert.Equal([ConfigurationAudit.RuleSmb1, ConfigurationAudit.RuleWdigest, ConfigurationAudit.RuleLsa, ConfigurationAudit.RuleRdpNla], res.Findings.Select(f => f.RuleId));
        Assert.Equal(@"SYSTEM\ControlSet002\Services\LanmanServer\Parameters\SMB1", res.Findings[0].SupportingEvidence.Single().Locator);
        Assert.Equal("SMB1 = 1", res.Findings[0].SupportingEvidence.Single().Quote);
        Assert.Equal("UseLogonCredential = 1", res.Findings[1].SupportingEvidence.Single().Quote);
        Assert.Equal(Severity.Low, res.Findings[2].Severity);
        Assert.Equal("RunAsPPL = 0", res.Findings[2].SupportingEvidence.Single().Quote);
        var rdp = res.Findings[3];
        Assert.Equal(["fDenyTSConnections = 0", "UserAuthentication = 0"], rdp.SupportingEvidence.Select(x => x.Quote));
        Assert.Equal(@"SYSTEM\ControlSet002\Control\Terminal Server\WinStations\RDP-Tcp\UserAuthentication", rdp.SupportingEvidence[1].Locator);
    }

    [Fact]
    public void A_missing_RunAsPPL_is_a_low_finding_with_honest_wording_and_low_confidence()
    {
        var f = Assert.Single(Run(SystemHive(ppl: null)).Findings);
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal(Confidence.Low, f.Confidence);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("valoarea lipsă nu dovedește", StringComparison.Ordinal));
    }

    [Fact]
    public void Safe_values_give_no_finding_and_no_gap()
    {
        var res = Run(SoftwareHive(), SystemHive());
        Assert.Empty(res.Findings);
        Assert.Empty(res.Gaps);
        // RDP enabled but with NLA: still safe
        Assert.Empty(Run(SystemHive(deny: "0", nla: "1")).Findings);
    }

    [Fact]
    public void An_unreadable_value_or_hive_gives_a_gap_note_and_never_a_finding()
    {
        var h = SoftwareHive();
        h.Values[ConfigurationAudit.UacEnable.Locator] = new ConfigValue("", Written);   // a value that is not a number (binary / empty)
        h.Values.Remove(ConfigurationAudit.UacConsent.Locator);                          // never read
        var res = Run(h);
        Assert.Empty(res.Findings);
        Assert.Contains(res.Gaps, g => g.Artifact == @"Setare SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA" && g.Status == EvidenceStatus.Failed);
        Assert.Contains(res.Gaps, g => g.Artifact.EndsWith("ConsentPromptBehaviorAdmin"));

        var bad = Run(new ConfigHive { Hive = "SYSTEM", EvidenceId = "E-0002", Error = "hive corupt" });
        Assert.Empty(bad.Findings);
        var gap = Assert.Single(bad.Gaps);
        Assert.Equal("Configurație din hive-ul SYSTEM", gap.Artifact);
        Assert.Contains("hive corupt", gap.Reason);
    }

    [Fact]
    public void The_pipeline_step_reports_an_unreadable_stored_hive_and_the_missing_other_hive_as_gaps()
    {
        var p = Path.Combine(_k.Dir, "in", "SOFTWARE");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, "regf\0\0\0\0"u8.ToArray());
        var ws = _k.Case();
        InvestigationPipeline.Import(ws, [p]);
        int n = 0;
        var res = InvestigationPipeline.ConfigurationFindings(ws, () => $"F-{++n:D4}");
        Assert.Empty(res.Findings);
        Assert.Contains(res.Gaps, g => g.Artifact == "Configurație din hive-ul SOFTWARE" && g.Reason.Contains("nu a putut fi citit"));
        Assert.Contains(res.Gaps, g => g.Artifact == "Configurație din hive-ul SYSTEM" && g.Status == EvidenceStatus.NotAvailable);
        // no hive at all: nothing is added (the coverage matrix already says the registry was not collected)
        var empty = InvestigationPipeline.ConfigurationFindings(_k.Case(), () => "F-0001");
        Assert.Empty(empty.Findings);
        Assert.Empty(empty.Gaps);
    }

    [Fact]
    public void Manual_verification_gives_the_reg_query_for_each_configuration_locator()
    {
        var res = Run(SoftwareHive(lua: "0", excl: new() { [@"Microsoft\Windows Defender\Exclusions\Paths"] = [@"C:\Tools"] }), SystemHive(smb1: "1", cs: "ControlSet001"));
        string Query(string rule)
        {
            var f = res.Findings.Single(x => x.RuleId == rule);
            var item = new EvidenceItem { EvidenceId = f.SupportingEvidence[0].EvidenceId, CaseId = "C", Source = "Registry", SourceType = "software_hive", StoredPath = @"Raw\Registry\SOFTWARE", Sha256 = Sha };
            return ManualVerification.For(f.SupportingEvidence[0], item, @"C:\Cazuri\C1").First(s => s.IsCommand).Action;
        }
        Assert.Equal(@"reg query ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"" /v EnableLUA", Query(ConfigurationAudit.RuleUac));
        Assert.Equal(@"reg query ""HKLM\SYSTEM\ControlSet001\Services\LanmanServer\Parameters"" /v SMB1", Query(ConfigurationAudit.RuleSmb1));
        Assert.Equal(@"reg query ""HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths""", Query(ConfigurationAudit.RuleDefenderExcl));
    }

    [Fact]
    public void The_findings_are_printed_in_the_mistakes_section_with_the_reg_query_step()
    {
        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        r.Findings.AddRange(FindingContract.Enrich(Run(SoftwareHive(lua: "0"), SystemHive(wdigest: "1")).Findings, new FindingContractContext { NowUtc = Written, Zone = TimeZoneInfo.Utc }));
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, Guid.NewGuid().ToString("N") + ".pdf"), "investigator");
        int start = text.IndexOf(". Greșeli găsite\n", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var next = Regex.Match(text[(start + 5)..], @"\n\d+\. ");
        var sec = next.Success ? text.Substring(start, next.Index + 5) : text[start..];
        Assert.Contains("controlul contului de utilizator (UAC) dezactivat", sec);
        Assert.Contains("parolele utilizatorilor conectați în clar în memorie (WDigest)", sec);
        Assert.Contains("Citat: „EnableLUA = 0”", sec);
        Assert.Contains("ora din: Registry LastWrite (cheie)", sec);
        Assert.Contains("(canal: Registry)", sec);
        Assert.Contains(@"reg query ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"" /v EnableLUA", sec);
        Assert.Contains("Greșeli găsite: 2 (", text);
    }
    [Fact]
    public void The_autologon_password_appears_nowhere_in_the_findings_or_the_pdf_text()
    {
        const string pwd = "Zx9-Secret-Passw0rd!";
        var h = SoftwareHive();
        void V(ConfigProbe p, string? raw) => h.Values[p.Locator] = new ConfigValue(ConfigurationAudit.ProbeText(p, raw), Written);   // what the reader stores
        V(ConfigurationAudit.AutoAdminLogon, "1"); V(ConfigurationAudit.DefaultUserName, "kiosk"); V(ConfigurationAudit.DefaultPassword, pwd);
        var res = Run(h);
        var f = Assert.Single(res.Findings, x => x.RuleId == ConfigurationAudit.RuleAutologon);
        Assert.Equal("AutoAdminLogon = 1; DefaultUserName = kiosk; DefaultPassword = (prezentă, nu se afișează)", f.SupportingEvidence[0].Quote);
        Assert.DoesNotContain(pwd, System.Text.Json.JsonSerializer.Serialize(res));

        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        r.Findings.AddRange(FindingContract.Enrich(res.Findings, new FindingContractContext { NowUtc = Written, Zone = TimeZoneInfo.Utc }));
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, Guid.NewGuid().ToString("N") + ".pdf"), "investigator");
        Assert.Contains("DefaultPassword = (prezentă, nu se afișează)", text);
        Assert.DoesNotContain(pwd, text);
    }
}
