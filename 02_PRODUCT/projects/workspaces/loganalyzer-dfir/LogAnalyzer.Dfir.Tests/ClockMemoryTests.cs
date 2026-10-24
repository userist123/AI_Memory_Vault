using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
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
            Assert.Equal(Now.AddMinutes(-60), ClockMemory.LatestRecorded(audit));   // the baseline is the LAST appended record; the later-timed one before it is suspect

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
        var f = new StationFacts { Posture = LogAnalyzer.Core.Services.Edition.StationPosture.Standalone, IsAdministrator = true, CollectedUtc = Now, LatestAppRecordUtc = Now.AddHours(1) };
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

    private static string TempAudit(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "clockmem_" + Guid.NewGuid().ToString("N"));
        return Path.Combine(dir, "auth_audit.jsonl");
    }

    [Fact]
    public void Wrong_forward_then_corrected_is_Behind_once_and_Ok_after_the_acknowledgement()
    {
        var audit = TempAudit(out var dir);
        try
        {
            ClockMemory.RecordSeen(audit, Now, "start");
            ClockMemory.RecordSeen(audit, Now.AddDays(10), "start");          // clock set wrongly forward
            var corrected = Now.AddHours(1);
            var first = ClockMemory.Startup(audit, corrected, null);
            Assert.Equal(ClockMemoryState.Behind, first.State);
            Assert.NotEqual("", ClockMemory.StartupWarning);

            var next = ClockMemory.Evaluate(audit, corrected.AddMinutes(5), null);
            Assert.Equal(ClockMemoryState.Ok, next.State);                    // no more Behind forever: the baseline is the last appended record
            Assert.Contains("ceasul greșit", next.SuspectNote);               // ... but the bad record is reported until the correction is confirmed
            ClockMemory.Reevaluate(audit, corrected.AddMinutes(5), null);
            Assert.Contains("ceasul greșit", ClockMemory.StartupWarning);

            ClockMemory.Acknowledge(audit, corrected.AddMinutes(6), "Administrator (admin1)", "bateria CMOS înlocuită, ora setată de la sursa aprobată");
            Assert.Equal("", ClockMemory.StartupWarning);
            var after = ClockMemory.Evaluate(audit, corrected.AddMinutes(7), null);
            Assert.Equal(ClockMemoryState.Ok, after.State);
            Assert.Equal("", after.SuspectNote);

            Assert.Equal(ChainStatus.Valid, new HashChain(audit).Verify().Status);
            var text = File.ReadAllText(audit);
            Assert.Contains("\"clock.ack\"", text);
            Assert.Contains("anterior=", text);
        }
        finally { ClockMemory.StartupWarning = ""; try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_record_dated_after_the_next_record_is_suspect_and_skipped_as_baseline()
    {
        var audit = TempAudit(out var dir);
        try
        {
            ClockMemory.RecordSeen(audit, Now, "a");
            ClockMemory.RecordSeen(audit, Now.AddDays(3), "b");     // made under a wrong clock
            ClockMemory.RecordSeen(audit, Now.AddMinutes(10), "c");
            var b = ClockMemory.ReadBaseline(audit);
            Assert.Equal(Now.AddMinutes(10), b.Baseline);
            Assert.Contains("S-a detectat o înregistrare făcută cu ceasul greșit la", b.SuspectNote);
            Assert.Contains(Now.AddDays(3).ToString("yyyy-MM-dd HH:mm:ss"), b.SuspectNote);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_chain_without_clock_records_is_Nedeterminat_and_other_entries_are_ignored()
    {
        var audit = TempAudit(out var dir);
        try
        {
            Directory.CreateDirectory(dir);
            new HashChain(audit).Append(new LogAnalyzer.Dfir.Auth.AuthAuditEntry("auth.signin", "x", "x", "", "", "ok", "", Now.AddYears(5), "", ""));
            Assert.Null(ClockMemory.ReadBaseline(audit).Baseline);
            Assert.Equal(ClockMemoryState.Nedeterminat, ClockMemory.Evaluate(audit, Now, null).State);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Acknowledge_requires_a_reason_and_the_control_text_names_who_when_and_why()
    {
        var audit = TempAudit(out var dir);
        try
        {
            ClockMemory.RecordSeen(audit, Now.AddDays(10), "a");
            Assert.Throws<ArgumentException>(() => ClockMemory.Acknowledge(audit, Now, "op", "  "));
            ClockMemory.Acknowledge(audit, Now, "Administrator (admin1)", "ceas corectat");
            var b = ClockMemory.ReadBaseline(audit);
            Assert.Equal(Now, b.Baseline);
            Assert.Contains("corectată de Administrator (admin1) la 2026-10-12 12:00:00 UTC, motiv: ceas corectat", b.AckText);

            var f = new StationFacts { CollectedUtc = Now.AddMinutes(1), LatestAppRecordUtc = b.Baseline, ClockAckText = b.AckText };
            var s05 = ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "S05");
            Assert.Equal(ControlStatus.Conform, s05.Status);
            Assert.Contains("motiv: ceas corectat", s05.Detail);
            var pending = ControlEvaluator.Evaluate(new StationFacts { CollectedUtc = Now, LatestAppRecordUtc = Now.AddMinutes(-1), ClockSuspectNote = "S-a detectat o înregistrare făcută cu ceasul greșit la x" })
                .Checks.Single(c => c.Id == "S05");
            Assert.Equal(ControlStatus.DeVerificat, pending.Status);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Investigation_confidence_is_lowered_by_the_unconfirmed_clock_and_restored_after_the_acknowledgement()
    {
        var audit = TempAudit(out var dir);
        try
        {
            ClockMemory.RecordSeen(audit, Now.AddDays(10), "a");
            ClockMemory.Startup(audit, Now, null);                 // Behind: warning set, then the corrected time is recorded
            Finding Timed() => new()
            {
                FindingId = "F-9", RuleId = "DEF-DETECTION", Title = "t", Description = "d", Severity = Severity.High, Confidence = Confidence.High,
                FirstSeenUtc = Now, LastSeenUtc = Now, SupportingEvidence = [new EvidenceRef("EV-1", "L", "x")],
            };
            var before = Timed();
            ClockMemory.Refresh(Now.AddMinutes(1));
            Assert.Equal(1, TimeReliability.Apply([before], [], ClockMemory.StartupWarning.Length > 0));
            Assert.Equal(Confidence.Medium, before.Confidence);

            ClockMemory.Acknowledge(audit, Now.AddMinutes(2), "operator", "ora corectată");
            var after = Timed();
            ClockMemory.Refresh(Now.AddMinutes(3));
            Assert.Equal(0, TimeReliability.Apply([after], [], ClockMemory.StartupWarning.Length > 0));
            Assert.Equal(Confidence.High, after.Confidence);
        }
        finally { ClockMemory.StartupWarning = ""; try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
