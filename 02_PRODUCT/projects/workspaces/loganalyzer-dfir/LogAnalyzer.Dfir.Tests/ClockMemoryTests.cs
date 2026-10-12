using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>The application's own time memory: the clock against the latest time the application recorded in its chained audit.</summary>
public class ClockMemoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Behind_by_one_hour_is_High_with_the_cmos_wording()
    {
        var r = ClockMemoryCheck.Evaluate(Now, Now.AddHours(1));
        Assert.Equal(ClockMemoryState.Behind, r.State);
        Assert.Equal("High", r.Severity);
        Assert.True(r.TimeUnreliable);
        Assert.Contains("dinaintea ultimei activități înregistrate de aplicație (cu 1 h 0 min)", r.Message);
        Assert.Contains("baterie CMOS descărcată", r.Message);
    }

    [Fact]
    public void Within_tolerance_is_Ok()
    {
        Assert.Equal(ClockMemoryState.Ok, ClockMemoryCheck.Evaluate(Now, Now.AddMinutes(2)).State);
        Assert.Equal(ClockMemoryState.Ok, ClockMemoryCheck.Evaluate(Now, Now.AddSeconds(-5)).State);
        Assert.False(ClockMemoryCheck.Evaluate(Now, Now.AddMinutes(2)).TimeUnreliable);
    }

    [Fact]
    public void Before_install_date_names_the_cmos_battery()
    {
        var r = ClockMemoryCheck.Evaluate(Now, Now.AddDays(-1), Now.AddDays(10));
        Assert.Equal(ClockMemoryState.BeforeInstall, r.State);
        Assert.Equal("High", r.Severity);
        Assert.Contains("dinaintea instalării Windows", r.Message);
        Assert.Contains("CMOS", r.Message);
        Assert.True(r.TimeUnreliable);
    }

    [Fact]
    public void Forty_five_days_forward_is_info_not_an_error()
    {
        var r = ClockMemoryCheck.Evaluate(Now, Now.AddDays(-45));
        Assert.Equal(ClockMemoryState.ForwardJump, r.State);
        Assert.Equal("Info", r.Severity);
        Assert.Contains("sărit înainte cu 45 zile", r.Message);
        Assert.False(r.TimeUnreliable);
    }

    [Fact]
    public void No_history_is_Nedeterminat()
    {
        var r = ClockMemoryCheck.Evaluate(Now, null);
        Assert.Equal(ClockMemoryState.Nedeterminat, r.State);
        Assert.Equal(ClockMemoryState.Nedeterminat, ClockMemoryCheck.Evaluate(Now, null, Now.AddYears(-1)).State);
    }

    [Fact]
    public void Seen_time_is_written_to_the_chained_audit_and_read_back_with_the_chain_intact()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clockmem_" + Guid.NewGuid().ToString("N"));
        try
        {
            var audit = Path.Combine(dir, "auth_audit.jsonl");
            var first = ClockMemory.CheckAndRecord(audit, Now, null, "test");
            Assert.Equal(ClockMemoryState.Nedeterminat, first.State);
            ClockMemory.RecordSeen(audit, Now.AddMinutes(5), "test");
            Assert.Equal(Now.AddMinutes(5), ClockMemory.LatestRecorded(audit));

            var behind = ClockMemory.CheckAndRecord(audit, Now.AddMinutes(-60), null, "test");
            Assert.Equal(ClockMemoryState.Behind, behind.State);
            Assert.Equal(Now.AddMinutes(5), ClockMemory.LatestRecorded(audit));   // an earlier clock never lowers the record

            var v = new HashChain(audit).Verify();
            Assert.Equal(ChainStatus.Valid, v.Status);
            Assert.Equal(3, v.Entries);
            Assert.Contains("\"clock.seen\"", File.ReadAllText(audit));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Station_control_check_S05_follows_the_verdict_and_has_a_guide()
    {
        var f = new StationFacts { StationShouldBeIsolated = true, IsAdministrator = true, CollectedUtc = Now, LatestAppRecordUtc = Now.AddHours(1) };
        var behind = ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "S05");
        Assert.Equal(ControlStatus.Neconform, behind.Status);
        Assert.Contains("CMOS", behind.Detail);

        var ok = ControlEvaluator.Evaluate(new StationFacts { CollectedUtc = Now, LatestAppRecordUtc = Now.AddMinutes(-3) }).Checks.Single(c => c.Id == "S05");
        Assert.Equal(ControlStatus.Conform, ok.Status);
        var fwd = ControlEvaluator.Evaluate(new StationFacts { CollectedUtc = Now, LatestAppRecordUtc = Now.AddDays(-45) }).Checks.Single(c => c.Id == "S05");
        Assert.Equal(ControlStatus.DeVerificat, fwd.Status);
        var none = ControlEvaluator.Evaluate(new StationFacts { CollectedUtc = Now }).Checks.Single(c => c.Id == "S05");
        Assert.Equal(ControlStatus.Nedeterminat, none.Status);

        var install = new StationFacts { CollectedUtc = Now, LatestAppRecordUtc = Now.AddDays(-1) };
        install.Settings["OS.InstallDate"] = Now.AddDays(5).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(ControlStatus.Neconform, ControlEvaluator.Evaluate(install).Checks.Single(c => c.Id == "S05").Status);

        var g = CheckGuides.For("S05");
        Assert.DoesNotContain("nu are încă o descriere", g.What);
        Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 20));
    }
}
