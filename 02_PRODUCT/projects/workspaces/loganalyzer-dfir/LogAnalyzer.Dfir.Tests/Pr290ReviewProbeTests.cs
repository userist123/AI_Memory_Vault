using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The probes of the re-review of PR #290: the installation date must not move LATER when the application's own files are re-dated (Windows run traces
/// decide); a mismatch made only of file copy times is DE VERIFICAT, not "jurnal reinițializat"; missing Windows traces weaken the check and say so;
/// the computed date and the chain's first date are printed in the control report; a junction cannot smuggle the firewall-log path off the disk.
/// </summary>
public sealed class Pr290ReviewProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_pr290_" + Guid.NewGuid().ToString("N"));
    public Pr290ReviewProbeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly DateTimeOffset Install = new DateTimeOffset(T0).AddMinutes(30);
    private static readonly DateTimeOffset Now = Install.AddDays(40);

    private string Chain(DateTimeOffset first, params (string Event, DateTimeOffset When)[] more)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".jsonl");
        var c = new HashChain(path);
        c.Append(new AuthAuditEntry(AppInstallDate.StoreCreatedEvent, "x", "admin", "", "", "ok", "", first, "", ""));
        foreach (var (e, w) in more) c.Append(new AuthAuditEntry(e, "x", "admin", "", "", "ok", "", w, "", ""));
        return path;
    }

    private static AppTrace Run(DateTimeOffset t) => new("Prefetch LOGANALYZER.EXE-1234ABCD.pf (rulare)", t, AppTraceKind.WindowsRun);
    private static AppTrace Seen(DateTimeOffset t) => new("Amcache: loganalyzer.exe", t, AppTraceKind.WindowsSeen);
    private static AppTrace Own(string what, DateTimeOffset t) => new(what, t);

    [Fact]
    public void Probe_re_dated_app_files_and_a_recreated_store_cannot_move_the_date_later_when_Windows_saw_the_app_run_earlier()
    {
        // store recreated 10 days later and every app-owned trace re-dated to that day; Prefetch still holds the first run
        var later = Install.AddDays(10);
        var r = AppInstallDate.Read(Chain(later), [Own("fișierul aplicației LogAnalyzer.exe", later), Own("licența (license.lic)", later), Run(Install)], Now);
        Assert.Equal(Install, r.Utc);
        Assert.Contains(r.Violations, v => v.Contains("a rulat pe stație") && v.Contains("înainte de începutul jurnalului de audit"));
        Assert.Equal(later, r.ChainFirstUtc);
    }

    [Fact]
    public void Probe_program_files_copied_long_before_the_first_configuration_are_DE_VERIFICAT_not_a_reinitialised_journal()
    {
        var later = Install.AddDays(10);
        var r = AppInstallDate.Read(Chain(later), [Own("fișierul aplicației LogAnalyzer.exe", Install), Run(later.AddMinutes(-1))], Now);
        Assert.Equal(Install, r.Utc);                                                     // the earliest trace still bounds the date
        Assert.Empty(r.Violations);
        Assert.Contains(r.ToVerify, v => v.Contains("fie jurnalul a fost reinițializat, fie aplicația a fost copiată pe stație cu mult înainte de prima configurare"));
        Assert.DoesNotContain(r.ToVerify, v => v.Contains("urmele Windows"));
    }

    [Fact]
    public void A_sealed_reinitialisation_stays_NECONFORM()
    {
        var r = AppInstallDate.Read(Chain(Install, (AppInstallDate.StoreReinitialisedEvent, Install.AddDays(3))), [Run(Install)], Now);
        Assert.Contains(r.Violations, v => v.Contains("reinițializat offline"));
    }

    [Fact]
    public void Probe_without_any_Windows_trace_of_the_app_the_check_is_weaker_and_says_so()
    {
        var r = AppInstallDate.Read(Chain(Install), [Own("fișierul aplicației LogAnalyzer.exe", Install)], Now);
        Assert.Equal(Install, r.Utc);
        Assert.Contains(r.ToVerify, v => v.Contains("urmele Windows ale aplicației lipsesc"));
        var seenOnly = AppInstallDate.Read(Chain(Install), [Seen(Install)], Now);
        Assert.DoesNotContain(seenOnly.ToVerify, v => v.Contains("urmele Windows ale aplicației lipsesc"));
    }

    [Fact]
    public void An_Amcache_entry_earlier_than_the_chain_is_DE_VERIFICAT_because_it_shows_presence_not_a_run()
    {
        var later = Install.AddDays(10);
        var r = AppInstallDate.Read(Chain(later), [Seen(Install), Run(later)], Now);
        Assert.Equal(Install, r.Utc);
        Assert.Empty(r.Violations);
        Assert.NotEmpty(r.ToVerify);
    }

    [Fact]
    public void The_DE_VERIFICAT_notes_reach_a_Standalone_finding_and_N01()
    {
        int k = 0;
        var f = Wp14Rules.Run([], () => $"F-{++k}", new Wp14Input { Scope = Classified(NetworkCategory.StandalonePc), AppInstalledUtc = Install, AppInstallToVerify = ["data instalării nu se potrivește: test"] });
        var x = Assert.Single(f, y => y.RuleId == "AIRGAP-APP-INSTALL-UNCONFIRMED");
        Assert.StartsWith("DE VERIFICAT", x.Title);

        var s = new StationFacts { Host = "ST-1", PeriodStartUtc = Install, PeriodEndUtc = Now, CollectedUtc = Now, IsAdministrator = true, Posture = StationPosture.Standalone,
                                   AppInstalledUtc = Install, AppInstallBasis = "test", AppInstallChainFirstUtc = Install.AddDays(10) };
        s.AppInstallToVerify.Add("data instalării nu se potrivește: test");
        foreach (var ch in StationFactCollector.Queries.Keys) s.Coverage.Add(new ChannelCoverage(ch, Install.AddDays(-60), 100, true, ""));
        var n01 = Assert.Single(ControlEvaluator.Evaluate(s).Checks, c => c.Id == "N01");
        Assert.Contains("DE VERIFICAT pentru data instalării", n01.Detail);
        Assert.Contains(n01.Evidence, e => e.StartsWith("primul rând al jurnalului de audit:"));

        // the computed date and the chain's first date are printed in the report header, so two controls can be compared
        var h = ControlReportHeader.From(new ReportHeaderProfile(), s, "HW", "Pop Ion", "", "Stație", "", "");
        Assert.Contains(h.Lines(), l => l.Label == "Data instalării aplicației (calculată)" && l.Value.Contains(Install.ToString("yyyy-MM-dd")));
        Assert.Contains(h.Lines(), l => l.Label == "Primul rând al jurnalului de audit al aplicației" && l.Value.Contains(Install.AddDays(10).ToString("yyyy-MM-dd")));
    }

    // ---- firewall log path through a junction ----

    private static bool MakeJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(10000);
        return p.ExitCode == 0 && Directory.Exists(link);
    }

    [Fact]
    public void Probe_a_junction_in_the_firewall_log_path_is_refused()
    {
        var target = Path.Combine(_dir, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "pfirewall.log"), "#Version: 1.5");
        var link = Path.Combine(_dir, "link");
        Assert.True(MakeJunction(link, target), "the junction could not be created");
        var why = FirewallLogParser.RejectPath(Path.Combine(link, "pfirewall.log"), _ => DriveType.Fixed);
        Assert.StartsWith("cale jurnal firewall nepermisă", why);
        Assert.Contains("legătură", why);
        Assert.Equal("", FirewallLogParser.RejectPath(Path.Combine(target, "pfirewall.log"), _ => DriveType.Fixed));
        Directory.Delete(link);
    }

    [Fact]
    public void A_reparse_point_reported_by_the_check_refuses_the_path()
    {
        Assert.StartsWith("cale jurnal firewall nepermisă", FirewallLogParser.RejectPath(@"C:\fw\pfirewall.log", _ => DriveType.Fixed, p => p.Equals(@"C:\fw", StringComparison.OrdinalIgnoreCase)));
    }
}
