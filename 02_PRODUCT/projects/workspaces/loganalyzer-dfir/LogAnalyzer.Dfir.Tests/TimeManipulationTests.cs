using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP15b: clock jumps (magnitude, direction), time-zone changes and record-order inversions; W32Time stays benign.</summary>
public sealed class TimeManipulationTests
{
    private static readonly DateTime T0 = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private static int _n;

    private static TimelineEvent E(string source, string id, DateTime t, string provider = "", Dictionary<string, string>? f = null, long? record = null, string evidence = "EV-1") => new()
    {
        Time = Timestamp.FromUtc(t, "t", "test"), Source = source, EventId = id, Provider = provider, EvidenceId = evidence, Summary = "s",
        Locator = record is { } r ? $"EventRecordID={r}" : $"L{Interlocked.Increment(ref _n)}",
        Fields = f is null ? new(StringComparer.OrdinalIgnoreCase) : new(f, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent E(string source, string id) => E(source, id, T0);

    private static TimelineEvent Clock4616(DateTime at, DateTime previous, DateTime now, string process = @"C:\Windows\System32\cmd.exe") =>
        E("EventLog:Security", "4616", at, "Microsoft-Windows-Security-Auditing", new()
        { ["PreviousTime"] = previous.ToString("o"), ["NewTime"] = now.ToString("o"), ["ProcessName"] = process, ["SubjectUserName"] = "admin" });

    [Fact]
    public void A_backward_jump_by_a_manual_process_has_magnitude_direction_and_a_window()
    {
        var r = TimeManipulation.Analyze([Clock4616(T0, T0, T0.AddHours(-3).AddMinutes(-12))]);
        var j = Assert.Single(r.Jumps);
        Assert.False(j.Benign);
        Assert.Equal(TimeSpan.FromMinutes(-192), j.Delta);
        Assert.Equal("înapoi", j.Direction);
        var w = Assert.Single(r.Windows, x => x.Kind == "CLOCK_JUMP");
        Assert.Equal(T0.AddHours(-3).AddMinutes(-12), w.Start.UtcDateTime);
        Assert.Equal(T0, w.End.UtcDateTime);
    }

    [Fact]
    public void A_forward_jump_is_reported_as_forward()
    {
        var j = Assert.Single(TimeManipulation.Analyze([Clock4616(T0, T0, T0.AddDays(2))]).Jumps);
        Assert.Equal("înainte", j.Direction);
        Assert.Equal(TimeSpan.FromDays(2), j.Delta);
    }

    [Fact]
    public void Kernel_General_1_with_reason_1_is_a_jump_and_reason_2_hardware_clock_is_benign()
    {
        var manual = E("EventLog:System", "1", T0, "Microsoft-Windows-Kernel-General", new() { ["Reason"] = "1", ["OldTime"] = T0.ToString("o"), ["NewTime"] = T0.AddHours(-1).ToString("o"), ["ProcessName"] = @"C:\x\tool.exe" });
        var hw = E("EventLog:System", "1", T0.AddMinutes(5), "Microsoft-Windows-Kernel-General", new() { ["Reason"] = "2", ["OldTime"] = T0.ToString("o"), ["NewTime"] = T0.AddHours(-9).ToString("o") });
        var r = TimeManipulation.Analyze([manual, hw]);
        Assert.Equal(2, r.Jumps.Count);
        Assert.Single(r.Jumps, j => !j.Benign);
        Assert.Single(r.Windows);
    }

    [Fact]
    public void W32Time_synchronisation_stays_benign_even_with_a_large_delta()
    {
        var r = TimeManipulation.Analyze([Clock4616(T0, T0, T0.AddMinutes(-40), @"C:\Windows\System32\svchost.exe")]);
        Assert.True(Assert.Single(r.Jumps).Benign);
        Assert.Empty(r.Windows);
    }

    [Fact]
    public void A_time_zone_change_is_recorded_with_a_window_but_is_not_a_manipulation_verdict()
    {
        var tz = E("EventLog:System", "22", T0, "Microsoft-Windows-Kernel-General");
        var tz2 = E("EventLog:System", "1", T0.AddDays(1), "Microsoft-Windows-Kernel-General", new() { ["Reason"] = "3" });
        var r = TimeManipulation.Analyze([tz, tz2]);
        Assert.Equal(2, r.ZoneChanges.Count);
        Assert.Equal(2, r.Windows.Count(w => w.Kind == "TIMEZONE_CHANGE"));
        Assert.Empty(r.Jumps.Where(j => !j.Benign));
    }

    [Fact]
    public void Record_id_increasing_while_time_goes_back_is_an_inversion_within_one_channel()
    {
        var r = TimeManipulation.Analyze([
            E("EventLog:System", "7036", T0, record: 10), E("EventLog:System", "7036", T0.AddMinutes(1), record: 11),
            E("EventLog:System", "7036", T0.AddMinutes(-30), record: 12), E("EventLog:System", "7036", T0.AddMinutes(-29), record: 13)]);
        var inv = Assert.Single(r.Inversions);
        Assert.Equal(11, inv.PreviousRecordId); Assert.Equal(12, inv.RecordId);
        Assert.Equal(TimeSpan.FromMinutes(31), inv.Back);
        Assert.Single(r.Windows, w => w.Kind == "RECORD_ORDER_INVERSION");
    }

    [Fact]
    public void A_small_step_back_inside_the_tolerance_and_other_channels_are_not_inversions()
    {
        var r = TimeManipulation.Analyze([
            E("EventLog:System", "1", T0, record: 1), E("EventLog:System", "1", T0.AddSeconds(-20), record: 2),
            E("EventLog:Security", "1", T0.AddHours(-5), record: 3, evidence: "EV-2")]);
        Assert.Empty(r.Inversions);
    }

    [Fact]
    public void Windows_answer_overlap_questions()
    {
        var r = TimeManipulation.Analyze([Clock4616(T0, T0, T0.AddHours(-2))]);
        Assert.True(r.OverlapsAny(T0.AddHours(-1), T0.AddHours(-1)));
        Assert.False(r.OverlapsAny(T0.AddHours(1), T0.AddHours(2)));
    }

    [Fact]
    public void AF05_reports_magnitude_and_direction_zone_changes_and_inversions_and_keeps_W32Time_benign()
    {
        var jump = AntiForensics.Evaluate([Clock4616(T0, T0, T0.AddHours(-3)), E("EventLog:System", "7036")], []);
        var af = Assert.Single(jump, c => c.Id == "AF05");
        Assert.Equal(AntiForensicResult.Detected, af.Result);
        Assert.Contains("înapoi", af.Reason); Assert.Contains("3h", af.Reason.Replace(" ", ""));

        var inv = AntiForensics.Evaluate([E("EventLog:System", "7036", T0, record: 1), E("EventLog:System", "7036", T0.AddHours(-2), record: 2)], []);
        var afInv = Assert.Single(inv, c => c.Id == "AF05");
        Assert.Equal(AntiForensicResult.Detected, afInv.Result);
        Assert.Contains("ordine", afInv.Reason, StringComparison.OrdinalIgnoreCase);

        var sync = AntiForensics.Evaluate([Clock4616(T0, T0, T0.AddMinutes(-3), @"C:\Windows\System32\svchost.exe"), E("EventLog:System", "7036")], []);
        Assert.Equal(AntiForensicResult.NotDetected, Assert.Single(sync, c => c.Id == "AF05").Result);

        var tz = AntiForensics.Evaluate([E("EventLog:System", "22", T0, "Microsoft-Windows-Kernel-General")], []);
        var afTz = Assert.Single(tz, c => c.Id == "AF05");
        Assert.Equal(AntiForensicResult.NotDetected, afTz.Result);
        Assert.Contains("fus orar", afTz.Reason);
    }
}
