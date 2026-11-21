using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #14 E4: CFG-OS-EOL (dated lifecycle table, compared with the COLLECTION date) and CFG-UPDATES-STALE (System log, WindowsUpdateClient 19; a log shorter than 60 days is a gap).</summary>
public sealed class OsLifecycleAndUpdatesTests
{
    private static DateTimeOffset Utc(int y, int m, int d) => new(y, m, d, 12, 0, 0, TimeSpan.Zero);

    // ---- the table ----

    [Fact]
    public void Lifecycle_table_is_dated_has_a_source_and_knows_the_builds_of_the_brief()
    {
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", WindowsLifecycle.TableDate);
        Assert.Contains("Microsoft Lifecycle", WindowsLifecycle.Source);
        foreach (var b in new[] { 19045, 22000, 22621, 22631, 26100, 26200 }) Assert.Contains(b, WindowsLifecycle.KnownBuilds);
    }

    [Fact]
    public void Lookup_past_near_current_and_unknown_on_the_collection_date()
    {
        var past = WindowsLifecycle.Evaluate(19045, "Professional", Utc(2026, 1, 10));
        Assert.Equal(LifecycleState.Past, past.State);
        Assert.Equal(new DateOnly(2025, 10, 14), past.EndOfServicing);
        Assert.True(past.DaysToEnd < 0);

        var near = WindowsLifecycle.Evaluate(19045, "Core", Utc(2025, 9, 1));
        Assert.Equal(LifecycleState.Near, near.State);
        Assert.Equal(43, near.DaysToEnd);

        Assert.Equal(LifecycleState.Current, WindowsLifecycle.Evaluate(26100, "Professional", Utc(2026, 1, 10)).State);
        Assert.Equal(LifecycleState.UnknownBuild, WindowsLifecycle.Evaluate(26300, "Professional", Utc(2026, 11, 21)).State);
        Assert.Equal(LifecycleState.UnknownEdition, WindowsLifecycle.Evaluate(19045, "EnterpriseS", Utc(2026, 11, 21)).State);
    }

    [Fact]
    public void The_edition_family_changes_the_date_and_the_end_day_itself_is_still_inside_servicing()
    {
        // Windows 11 22H2: Home/Pro 2024-10-08, Enterprise/Education 2025-10-14
        var home = WindowsLifecycle.Evaluate(22621, "Core", Utc(2025, 1, 1));
        var ent = WindowsLifecycle.Evaluate(22621, "Enterprise", Utc(2025, 1, 1));
        Assert.Equal(LifecycleState.Past, home.State);
        Assert.Equal(LifecycleState.Current, ent.State);
        Assert.Equal(LifecycleState.Near, WindowsLifecycle.Evaluate(22621, "Core", Utc(2024, 10, 8)).State);   // 0 days left
        Assert.Equal(LifecycleState.Past, WindowsLifecycle.Evaluate(22621, "Core", Utc(2024, 10, 9)).State);
    }

    // ---- the audit over a SOFTWARE hive ----

    private const string CurVer = @"Microsoft\Windows NT\CurrentVersion";

    private static ConfigHive Soft(string build, string edition, DateTimeOffset? collected, string ubr = "5000")
    {
        var r = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
        var k = r.Sub(CurVer);
        k.Value("ProductName", RegfBuilder.String, "Windows 10 Pro");
        k.Value("EditionID", RegfBuilder.String, edition);
        k.Value("DisplayVersion", RegfBuilder.String, "22H2");
        k.Value("CurrentBuild", RegfBuilder.String, build);
        k.Value("UBR", RegfBuilder.Dword, int.Parse(ubr));
        using var ms = new MemoryStream(RegfBuilder.Build(r, "SOFTWARE"));
        var h = ConfigurationHiveReader.Read(new RawRegistry(ms), "E-0001", "sha-1", "SOFTWARE");
        h.CollectedUtc = collected;
        return h;
    }

    private static ConfigAuditResult Eval(ConfigHive h) { int n = 0; return ConfigurationAudit.Evaluate([h], "WS", () => $"F-{++n:D4}"); }

    [Fact]
    public void Past_end_of_servicing_is_high_and_prints_the_table_date_and_the_collection_date()
    {
        var res = Eval(Soft("19045", "Professional", Utc(2026, 1, 10)));
        var f = Assert.Single(res.Findings, x => x.RuleId == "CFG-OS-EOL");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("Windows 10 22H2", f.Title);
        Assert.Contains("2025-10-14", f.Title);
        Assert.Contains("2026-01-10", f.Title);
        Assert.Contains(WindowsLifecycle.TableDate, f.Description);
        Assert.Contains(f.Limitations, l => l.Contains(WindowsLifecycle.TableDate));
        Assert.Contains(f.SupportingEvidence, e => e.Locator == @"SOFTWARE\" + CurVer + @"\CurrentBuild" && e.Quote == "CurrentBuild = 19045");
        Assert.Empty(res.Gaps);
    }

    [Fact]
    public void Within_sixty_days_is_medium_and_a_later_collection_date_is_what_counts_not_today()
    {
        var near = Eval(Soft("19045", "Professional", Utc(2025, 9, 1)));
        Assert.Equal(Severity.Medium, Assert.Single(near.Findings, x => x.RuleId == "CFG-OS-EOL").Severity);
        // collected long before the end: no finding, although today (2026-11) is already past it
        Assert.Empty(Eval(Soft("19045", "Professional", Utc(2025, 6, 1))).Findings);
        // the supported build of the same day gives nothing
        Assert.Empty(Eval(Soft("26100", "Core", Utc(2026, 1, 10))).Findings);
    }

    [Fact]
    public void Unknown_build_gives_no_finding_but_an_informational_gap_and_an_unknown_edition_too()
    {
        var res = Eval(Soft("26300", "Professional", Utc(2026, 11, 21)));
        Assert.Empty(res.Findings);
        var gap = Assert.Single(res.Gaps);
        Assert.Contains("build necunoscut în tabel (posibil Insider/Canary)", gap.Reason);
        var ltsc = Eval(Soft("19045", "EnterpriseS", Utc(2026, 11, 21)));
        Assert.Empty(ltsc.Findings);
        Assert.Contains("ediție", Assert.Single(ltsc.Gaps).Reason);
    }

    [Fact]
    public void Unknown_collection_date_is_a_gap_never_a_comparison_with_today()
    {
        var res = Eval(Soft("19045", "Professional", null));
        Assert.Empty(res.Findings);
        Assert.Contains("data colectării", Assert.Single(res.Gaps).Reason);
    }

    // ---- stale updates ----

    private static TimelineEvent Row(string channel, string id, string provider, DateTimeOffset when, string title = "", string rec = "1")
    {
        var e = new TimelineEvent
        {
            Time = Timestamp.FromUtc(when.UtcDateTime, when.UtcDateTime.ToString("o"), "EVTX SystemTime"), Source = "EventLog:" + channel, EvidenceId = "E-0100", EventId = id, Provider = provider,
            Summary = $"{provider} {id}: {title}", Locator = $"EventRecordID={rec}", SourceSha256 = "sha-sys",
        };
        if (title.Length > 0) e.Fields["updateTitle"] = title;
        return e;
    }

    private static ConfigAuditResult Updates(DateTimeOffset collected, params TimelineEvent[] rows) { int n = 0; return WindowsUpdateAudit.Evaluate(rows, collected, "WS", () => $"F-{++n:D4}"); }

    private const string Wuc = "Microsoft-Windows-WindowsUpdateClient";
    private const string Kb = "2026-10 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5099999)";

    [Fact]
    public void Update_older_than_sixty_days_is_medium_and_quotes_the_title()
    {
        var c = Utc(2026, 11, 21);
        var res = Updates(c, Row("System", "6005", "EventLog", c.AddDays(-200)), Row("System", "19", Wuc, c.AddDays(-75), Kb, "7"), Row("System", "6005", "EventLog", c.AddDays(-1)));
        var f = Assert.Single(res.Findings);
        Assert.Equal("CFG-UPDATES-STALE", f.RuleId);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("KB5099999", f.Description);
        Assert.Contains("75", f.Title);
        Assert.Contains(f.SupportingEvidence, e => e.Locator == "EventRecordID=7" && e.Quote.Contains("KB5099999"));
        Assert.Empty(res.Gaps);
    }

    [Fact]
    public void A_recent_quality_update_gives_nothing_and_definition_updates_do_not_count()
    {
        var c = Utc(2026, 11, 21);
        var fresh = Updates(c, Row("System", "6005", "EventLog", c.AddDays(-200)), Row("System", "19", Wuc, c.AddDays(-10), Kb));
        Assert.Empty(fresh.Findings); Assert.Empty(fresh.Gaps);
        // only a Defender definition update is recent: it does not make the PC "up to date"
        var def = Updates(c, Row("System", "6005", "EventLog", c.AddDays(-200)), Row("System", "19", Wuc, c.AddDays(-3), "Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.1.1.1)"));
        Assert.Single(def.Findings);
        Assert.Contains("nicio actualizare", def.Findings[0].Title);
    }

    [Fact]
    public void A_system_log_shorter_than_sixty_days_without_event_19_is_a_gap_not_a_finding()
    {
        var c = Utc(2026, 11, 21);
        var res = Updates(c, Row("System", "6005", "EventLog", c.AddDays(-20)), Row("System", "6005", "EventLog", c.AddDays(-1), rec: "2"));
        Assert.Empty(res.Findings);
        var gap = Assert.Single(res.Gaps);
        Assert.Contains("jurnalul System nu acoperă 60 de zile", gap.Reason);
    }

    [Fact]
    public void A_long_log_without_any_event_19_says_none_found_and_no_system_log_gives_nothing()
    {
        var c = Utc(2026, 11, 21);
        var res = Updates(c, Row("System", "6005", "EventLog", c.AddDays(-120)), Row("System", "6005", "EventLog", c.AddDays(-1), rec: "2"));
        var f = Assert.Single(res.Findings);
        Assert.Contains("nu există niciun eveniment", f.Description);
        Assert.Empty(Updates(c, Row("Application", "1000", "App", c.AddDays(-5))).Findings);
        Assert.Empty(Updates(c, Row("Application", "1000", "App", c.AddDays(-5))).Gaps);
    }

    [Fact]
    public void The_comparison_uses_the_collection_date_not_today()
    {
        // update 30 days before a collection made in 2024: fresh, although it is years before today
        var c = Utc(2024, 3, 1);
        var res = Updates(c, Row("System", "6005", "EventLog", c.AddDays(-100)), Row("System", "19", Wuc, c.AddDays(-30), Kb));
        Assert.Empty(res.Findings);
    }
    [Fact]
    public void Manual_verification_of_the_version_values_gives_the_reg_query_winver_and_the_lifecycle_page()
    {
        var f = Assert.Single(Eval(Soft("19045", "Professional", Utc(2026, 1, 10))).Findings);
        var build = f.SupportingEvidence.First(e => e.Locator.EndsWith(@"\CurrentBuild"));
        var steps = ManualVerification.For(build, null, @"C:\Cazuri\C1");
        Assert.Contains(steps, s => s.IsCommand && s.Action == @"reg query ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion"" /v CurrentBuild");
        Assert.Contains(steps, s => s.Compare.Contains("winver") && s.Compare.Contains("Microsoft Lifecycle"));
    }
}
