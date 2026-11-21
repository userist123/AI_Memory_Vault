using System.IO;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #13 D1: the Quick profile saves three small SOFTWARE subkey hives (reg save of the subkey) instead of the whole hive; Standard and Full keep the full hive. Tiny fake files only.</summary>
public sealed class SoftwareSubkeyHivesCollectorTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    private LogAnalyzer.Dfir.Case.CaseWorkspace? _ws;
    public void Dispose() => _k.Dispose();

    private static ToolRun Run(string exe, IReadOnlyList<string> args, int exit) =>
        new(exe, args, exit, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

    private static (ToolRunFunc Fn, List<IReadOnlyList<string>> Calls) Fake(Func<string, int> exitFor)
    {
        var calls = new List<IReadOnlyList<string>>();
        return ((exe, args, _, _, _, _) =>
        {
            calls.Add(args);
            int exit = exitFor(args[1]);
            if (exit == 0) File.WriteAllText(args[2], "regf-fake");
            return Run(exe, args, exit);
        }, calls);
    }

    private CollectorOutcome Collect(ToolRunFunc fn, bool elevated = true, Func<string, bool>? exists = null, Func<string, string?>? winlogon = null)
    {
        var ws = _ws = _k.Case();
        return new SoftwareSubkeyHivesCollector(fn, exists ?? (_ => true), winlogon ?? (_ => null)).Collect(new CollectorContext { Case = ws, IsElevated = elevated }, default);
    }

    [Fact]
    public void Quick_run_saves_the_five_subkeys_with_exact_commands_and_registers_them_with_type_and_original_path()
    {
        var (fn, calls) = Fake(_ => 0);
        var o = Collect(fn);
        Assert.Equal(5, calls.Count);
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", calls[0][1]);
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows Defender", calls[1][1]);
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender", calls[2][1]);
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Installer", calls[3][1]);      // planner #14 E2
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall", calls[4][1]);         // planner #14 E2
        Assert.DoesNotContain(calls, c => c[1].Contains("Winlogon", StringComparison.OrdinalIgnoreCase));   // the Winlogon key can hold DefaultPassword in clear: never saved
        foreach (var c in calls) { Assert.Equal(4, c.Count); Assert.Equal("save", c[0]); Assert.Equal("/y", c[3]); }
        Assert.Equal(5, calls.Select(c => c[2]).Distinct().Count());
        Assert.Equal(6, o.Evidence.Count);   // 5 subkey hives + the Winlogon autologon JSON
        Assert.Equal(SoftwareSubkeyHivesCollector.WinlogonSourceType, o.Evidence[5].SourceType);
        o.Evidence.RemoveAt(5);
        Assert.All(o.Evidence, e => { Assert.Equal("software_subkey_hive", e.SourceType); Assert.Equal(TemporalType.CurrentSnapshot, e.TemporalType); Assert.False(string.IsNullOrEmpty(e.Sha256)); });
        Assert.Equal(calls.Select(c => c[1]), o.Evidence.Select(e => e.OriginalPath));
        Assert.Equal([@"Microsoft\Windows\CurrentVersion\Policies\System", @"Microsoft\Windows Defender", @"Policies\Microsoft\Windows Defender", @"Policies\Microsoft\Windows\Installer", @"Policies\Microsoft\WindowsFirewall"], o.Evidence.Select(e => ConfigurationHiveReader.SubkeyOf(e)));
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Equal(5, o.Commands.Count);
    }

    [Fact]
    public void A_missing_key_is_an_error_of_the_outcome_while_the_others_are_still_saved()
    {
        var (fn, _) = Fake(key => key.EndsWith(@"\Policies\Microsoft\Windows Defender", StringComparison.Ordinal) ? 1 : 0);
        var o = Collect(fn);
        Assert.Equal(5, o.Evidence.Count);
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Contains(@"Policies\Microsoft\Windows Defender: exit 1", Assert.Single(o.Errors));
    }

    [Fact]
    public void All_failing_is_a_failed_outcome_and_not_elevated_runs_nothing()
    {
        var (fn, _) = Fake(_ => 1);
        Assert.Equal(EvidenceStatus.Failed, Collect(fn).Status);
        var (fn2, calls2) = Fake(_ => 0);
        var o = Collect(fn2, elevated: false);
        Assert.Empty(calls2);
        Assert.Equal(EvidenceStatus.NotAvailable, o.Status);
    }

    [Fact]
    public void Profiles_quick_only_while_standard_and_full_keep_the_full_hive_collector_unchanged()
    {
        Assert.Equal(CollectionProfile.Quick, new SoftwareSubkeyHivesCollector().Profiles);
        Assert.Equal(CollectionProfile.StandardAndUp, new SoftwareHiveCollector().Profiles);
        Assert.Single(InvestigationPipeline.AllCollectors, x => x is SoftwareSubkeyHivesCollector);
        var quick = InvestigationPipeline.AllCollectors.Where(x => (x.Profiles & CollectionProfile.Quick) != 0).ToList();
        Assert.Contains(quick, x => x is SoftwareSubkeyHivesCollector);
        Assert.DoesNotContain(quick, x => x is SoftwareHiveCollector);
        foreach (var p in new[] { CollectionProfile.Standard, CollectionProfile.FullForensic })
        {
            var set = InvestigationPipeline.AllCollectors.Where(x => (x.Profiles & p) != 0).ToList();
            Assert.Contains(set, x => x is SoftwareHiveCollector);
            Assert.DoesNotContain(set, x => x is SoftwareSubkeyHivesCollector);
        }
    }

    [Fact]
    public void An_absent_key_is_not_saved_has_no_error_and_leaves_a_hashed_absence_marker()
    {
        var (fn, calls) = Fake(_ => 0);
        var o = Collect(fn, exists: sub => sub != @"Policies\Microsoft\Windows Defender");
        Assert.Equal(4, calls.Count);
        Assert.DoesNotContain(calls, c => c[1].EndsWith(@"SOFTWARE\Policies\Microsoft\Windows Defender", StringComparison.Ordinal));
        Assert.Empty(o.Errors);
        Assert.Equal(EvidenceStatus.Success, o.Status);
        var m = Assert.Single(o.Evidence, e => e.SourceType == "software_subkey_absent");
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender", m.OriginalPath);
        Assert.Equal(TemporalType.CurrentSnapshot, m.TemporalType);
        Assert.False(string.IsNullOrEmpty(m.Sha256));
        var json = File.ReadAllText(_ws!.FullPath(m.StoredPath));
        Assert.Contains("\"exists\":false", json);
        Assert.Contains("Policies", json);
    }

    [Fact]
    public void A_key_that_exists_but_cannot_be_saved_is_still_an_error_and_has_no_marker()
    {
        var (fn, calls) = Fake(key => key.EndsWith(@"\Policies\Microsoft\Windows Defender", StringComparison.Ordinal) ? 1 : 0);
        var o = Collect(fn);
        Assert.Equal(5, calls.Count);
        Assert.Single(o.Errors);
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == "software_subkey_absent");
    }

    // ---- planner #14 E2: Winlogon autologon facts in Quick (JSON, the password is never read into the case) ----

    private const string Pwd = "Zx9-Secret-Passw0rd!";

    private static string? FakeWinlogon(string name) => name switch { "AutoAdminLogon" => "1", "DefaultUserName" => "kiosk", "DefaultPassword" => Pwd, _ => null };

    [Fact]
    public void Quick_autologon_gives_a_high_finding_without_a_gap_and_the_password_appears_nowhere()
    {
        var (fn, calls) = Fake(_ => 0);
        var o = Collect(fn, exists: sub => sub == SoftwareSubkeyHivesCollector.WinlogonSubkey, winlogon: FakeWinlogon);
        Assert.Empty(calls);   // nothing is saved with reg save (the other keys are absent markers); the Winlogon key is never reg-saved
        var j = Assert.Single(o.Evidence, e => e.SourceType == "winlogon_autologon_json");
        Assert.Equal(TemporalType.CurrentSnapshot, j.TemporalType);
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", j.OriginalPath);
        var json = File.ReadAllText(_ws!.FullPath(j.StoredPath));
        Assert.DoesNotContain(Pwd, json);
        Assert.Contains("\"DefaultPasswordPresent\":true", json);
        Assert.Contains("\"DefaultUserName\":\"kiosk\"", json);
        Assert.Contains("valoarea parolei nu a fost citită în caz", json);

        int n = 0;
        var res = InvestigationPipeline.ConfigurationFindings(_ws!, () => $"F-{++n:D4}");
        var f = Assert.Single(res.Findings, x => x.RuleId == ConfigurationAudit.RuleAutologon);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal("AutoAdminLogon = 1; DefaultUserName = kiosk; DefaultPassword = (prezentă, nu se afișează)", f.SupportingEvidence[0].Quote);
        Assert.Equal("Instantaneu la colectare", f.SupportingEvidence[0].TimeSource);
        Assert.Equal(j.EvidenceId, f.SupportingEvidence[0].EvidenceId);
        Assert.DoesNotContain(res.Gaps, g => g.Artifact.Contains("Winlogon"));

        // manual verification: the same two reg query steps, with the "do not copy it" note, and no hive-load steps for a JSON
        string Step(EvidenceRef e, bool wantNote)
        {
            var steps = ManualVerification.For(e, j, _ws!.Root);
            Assert.DoesNotContain(steps, s => s.Action.Contains("reg load"));
            Assert.DoesNotContain(Pwd, string.Join("|", steps.Select(s => s.Action + s.Compare)));
            var c = steps.First(s => s.IsCommand);
            if (wantNote) Assert.Contains("nu o copiați pe hârtie", c.Compare);
            return c.Action;
        }
        Assert.Equal(@"reg query ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v AutoAdminLogon", Step(f.SupportingEvidence[0], false));
        Assert.Equal(@"reg query ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v DefaultPassword", Step(f.SupportingEvidence[1], true));

        // the password is in no file of the case, in no finding and not in the PDF text
        Assert.DoesNotContain(Pwd, System.Text.Json.JsonSerializer.Serialize(res));
        foreach (var file in Directory.EnumerateFiles(_ws!.Root, "*", SearchOption.AllDirectories).Where(x => !x.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            Assert.DoesNotContain(Pwd, File.ReadAllText(file));
        var r = new InvestigationResult { Case = _ws! };
        r.Findings.AddRange(FindingContract.Enrich(res.Findings, new FindingContractContext { NowUtc = DateTimeOffset.UtcNow, Zone = TimeZoneInfo.Utc }));
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        try { InvestigationReportPdf.Write(r, Path.Combine(_ws!.Root, "r.pdf"), "investigator"); } finally { InvestigationReportPdf.TextObserver.Value = null; }
        Assert.Contains("DefaultPassword = (prezentă, nu se afișează)", text);
        Assert.DoesNotContain(Pwd, text);
    }

    [Fact]
    public void Quick_with_the_Winlogon_key_absent_leaves_a_marker_and_no_gap_and_without_autologon_no_finding()
    {
        var (fn, _) = Fake(_ => 0);
        var o = Collect(fn, exists: sub => sub != SoftwareSubkeyHivesCollector.WinlogonSubkey);
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == "winlogon_autologon_json");
        Assert.Contains(o.Evidence, e => e.SourceType == "software_subkey_absent" && e.OriginalPath == @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
        // no value of the (existing) key: AutoAdminLogon missing, no finding, and the JSON-less pipeline run reports no Winlogon gap
        int n = 0;
        var res = InvestigationPipeline.ConfigurationFindings(_ws!, () => $"F-{++n:D4}");
        Assert.DoesNotContain(res.Gaps, g => g.Artifact.Contains("Winlogon"));
        Assert.DoesNotContain(res.Findings, x => x.RuleId == ConfigurationAudit.RuleAutologon);

        var (fn2, _) = Fake(_ => 0);
        var o2 = Collect(fn2, winlogon: n2 => n2 == "AutoAdminLogon" ? "0" : null);   // key present, autologon off: JSON, no finding, no gap
        Assert.Single(o2.Evidence, e => e.SourceType == "winlogon_autologon_json");
        var res2 = InvestigationPipeline.ConfigurationFindings(_ws!, () => "F-1");
        Assert.DoesNotContain(res2.Gaps, g => g.Artifact.Contains("Winlogon"));
        Assert.DoesNotContain(res2.Findings, x => x.RuleId == ConfigurationAudit.RuleAutologon);
    }
}
