using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Second re-review of PR #290: a normal deployment (start, read the Hardware ID, wait days for the licence, configure) must not be NECONFORM; only the
/// installed edition executable counts in Prefetch / Amcache; a run from another folder or a missing Prefetch file while Prefetch is active is DE VERIFICAT;
/// the Amcache key time is a last-write time, not a first sighting.
/// </summary>
public sealed class Pr290Round2ProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_pr290b_" + Guid.NewGuid().ToString("N"));
    public Pr290Round2ProbeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly DateTimeOffset Start = new DateTimeOffset(T0);
    private static readonly DateTimeOffset Now = Start.AddDays(40);
    private const string Installed = @"C:\Program Files\LogAnalyzer\LogAnalyzer.Standalone.exe";

    private static AuthAuditEntry E(string ev, DateTimeOffset when) => new(ev, "x", "admin", "", "", "ok", "", when, "", "");

    [Fact]
    public void Probe_a_normal_deployment_with_the_licence_days_after_the_first_start_is_not_NECONFORM()
    {
        // First start writes app.first_start before the licence window; the licence comes 3 days later; then the store is created. Because clock.seen is written
        // before the primary administrator, AuthService logs auth.store_reinitialised at that FIRST creation: it is setup, not a reinitialisation.
        var path = Path.Combine(_dir, "auth_audit.jsonl");
        Assert.True(AppInstallDate.RecordFirstStart(path, Start));
        Assert.False(AppInstallDate.RecordFirstStart(path, Start.AddDays(1)));      // only once
        var c = new HashChain(path);
        c.Append(E(ClockMemory.Event, Start.AddDays(3)));
        c.Append(E(AppInstallDate.StoreReinitialisedEvent, Start.AddDays(3)));
        c.Append(E(AppInstallDate.StoreCreatedEvent, Start.AddDays(3)));
        var r = AppInstallDate.Read(path, [new AppTrace("Prefetch LOGANALYZER.STANDALONE.EXE-1.pf (rulare)", Start.AddMinutes(1), AppTraceKind.WindowsRun)], Now);
        Assert.Equal(Start, r.Utc);
        Assert.Empty(r.Violations);
        Assert.Equal(Start, r.ChainFirstUtc);
    }

    [Fact]
    public void A_reinitialisation_after_a_primary_administrator_existed_is_still_reported()
    {
        var path = Path.Combine(_dir, "a2.jsonl");
        AppInstallDate.RecordFirstStart(path, Start);
        var c = new HashChain(path);
        c.Append(E(AppInstallDate.StoreCreatedEvent, Start.AddMinutes(5)));
        c.Append(E(AppInstallDate.StoreReinitialisedEvent, Start.AddDays(5)));
        c.Append(E(AppInstallDate.StoreCreatedEvent, Start.AddDays(5)));
        var r = AppInstallDate.Read(path, [], Now);
        Assert.Contains(r.Violations, v => v.Contains("reinițializat offline"));
    }

    [Fact]
    public void The_first_start_record_is_written_before_licence_activation()
    {
        var src = File.ReadAllText(RepoFile(Path.Combine("LogAnalyzer.App", "App.xaml.cs")));
        int first = src.IndexOf("AppInstallDate.RecordFirstStart(", StringComparison.Ordinal);
        int licence = src.IndexOf("licenseService.IsActivated()", StringComparison.Ordinal);
        Assert.True(first > 0 && licence > 0 && first < licence, "RecordFirstStart must run before the licence check");
    }

    private static string RepoFile(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        throw new FileNotFoundException(relative);
    }

    // ---- Prefetch and Amcache: only the installed edition executable ----

    private static AppInstallTraces.PrefetchSeen Pf(string file, string exe, string? full, DateTimeOffset t) => new(file, t, [t.AddMinutes(1)], exe, full);

    [Fact]
    public void Probe_LicenseManager_and_other_programs_never_count_and_the_installed_exe_does()
    {
        var (traces, notes) = AppInstallTraces.ClassifyPrefetch(
        [
            Pf("LOGANALYZER.LICENSEMANAGER-11.pf", "LOGANALYZER.LICENSEMANAGER.EXE", @"\VOLUME{01}\PROGRAM FILES\LOGANALYZER\LOGANALYZER.LICENSEMANAGER.EXE", Start.AddDays(-30)),
            Pf("LOGANALYZER.STANDALONE.EXE-22.pf", "LOGANALYZER.STANDALONE.EXE", @"\VOLUME{01}\PROGRAM FILES\LOGANALYZER\LOGANALYZER.STANDALONE.EXE", Start),
        ], Installed, prefetchEnabled: true, otherPrefetchFiles: 100);
        Assert.Equal(2, traces.Count);                                                // creation + one run of the installed exe
        Assert.All(traces, t => Assert.Equal(AppTraceKind.WindowsRun, t.Kind));
        Assert.DoesNotContain(traces, t => t.What.Contains("LICENSEMANAGER", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(notes);
    }

    [Fact]
    public void Probe_a_run_of_the_same_exe_from_another_folder_is_DE_VERIFICAT_and_does_not_set_the_date()
    {
        var (traces, notes) = AppInstallTraces.ClassifyPrefetch(
        [
            Pf("LOGANALYZER.STANDALONE.EXE-33.pf", "LOGANALYZER.STANDALONE.EXE", @"\VOLUME{02}\USERS\X\DOWNLOADS\LOGANALYZER.STANDALONE.EXE", Start.AddDays(-30)),
            Pf("LOGANALYZER.STANDALONE.EXE-22.pf", "LOGANALYZER.STANDALONE.EXE", @"\VOLUME{01}\PROGRAM FILES\LOGANALYZER\LOGANALYZER.STANDALONE.EXE", Start),
        ], Installed, true, 100);
        Assert.DoesNotContain(traces, t => t.Utc < Start);
        Assert.Contains(notes, n => n.StartsWith("aplicația a rulat și din altă locație:") && n.Contains(@"\USERS\X\DOWNLOADS\LOGANALYZER.STANDALONE.EXE"));
    }

    [Fact]
    public void Probe_a_missing_app_prefetch_file_while_prefetch_is_active_is_DE_VERIFICAT()
    {
        var (traces, notes) = AppInstallTraces.ClassifyPrefetch([], Installed, prefetchEnabled: true, otherPrefetchFiles: 120);
        Assert.Empty(traces);
        Assert.Contains(notes, n => n.StartsWith("lipsește urma Prefetch a aplicației deși Prefetch este activ — posibilă ștergere"));
        var (_, off) = AppInstallTraces.ClassifyPrefetch([], Installed, prefetchEnabled: false, otherPrefetchFiles: 0);
        Assert.DoesNotContain(off, n => n.Contains("posibilă ștergere"));
    }

    [Fact]
    public void Probe_the_Amcache_key_time_is_a_last_write_time_and_only_the_installed_exe_counts()
    {
        var t = AppInstallTraces.ClassifyAmcache(
        [
            (Installed.ToLowerInvariant(), Start.UtcDateTime),
            (@"c:\users\x\downloads\loganalyzer.standalone.exe", Start.AddDays(-10).UtcDateTime),
            (@"c:\program files\loganalyzer\loganalyzer.licensemanager.exe", Start.AddDays(-20).UtcDateTime),
        ], Installed);
        var one = Assert.Single(t);
        Assert.Equal(AppTraceKind.WindowsSeen, one.Kind);
        Assert.DoesNotContain("prima apariție", one.What);
        Assert.Contains("ultimei scrieri", one.What);
    }

    [Fact]
    public void The_residual_risk_and_the_paper_anchor_follow_up_are_documented()
    {
        var doc = File.ReadAllText(RepoFile(Path.Combine("docs", "dfir", "EDITIONS_AND_POSTURE.md")));
        Assert.Contains("## Application installation date: residual risk", doc);
        Assert.Contains("#289", doc);
    }
}
