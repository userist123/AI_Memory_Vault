using System;
using System.IO;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Planner #9 item 2: the evidence of an interrupted collector is the evidence registered after the index position taken when it started, not the
/// evidence stamped after its start time (a clock step back or forward made the record miss items or pull in an earlier run's items).
/// </summary>
public sealed class InterruptedCollectorMarkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_acqmark_" + Guid.NewGuid().ToString("N"));

    public InterruptedCollectorMarkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private CaseWorkspace NewCase() => InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "acq", new CaseScope
    {
        Purpose = "test", PeriodFromUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), PeriodToUtc = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        SystemsInScope = ["TESTHOST"], Approver = "approver-1", LegalBasis = LegalBasis.Incident,
        Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
    });

    private static EvidenceItem Store(CaseWorkspace ws, string name, string collector, DateTimeOffset acquired)
    {
        var path = Path.Combine(ws.RawDir("acq"), name);
        File.WriteAllText(path, name);
        return ws.RegisterStored(path, @"C:\orig\" + name, "acq", "txt", TemporalType.Historical, collector, "1", acquiredAtUtc: acquired);
    }

    [Fact]
    public void Evidence_stamped_before_the_start_after_a_clock_step_back_is_still_the_collectors()
    {
        var ws = NewCase();
        var start = DateTimeOffset.UtcNow;
        var mark = AcquisitionRunRecorder.EvidenceMark(ws);
        var mine = Store(ws, "mine.txt", "Col", start.AddMinutes(-10));   // the clock stepped back during the collector
        var got = AcquisitionRunRecorder.EvidenceSince(ws, "Col", start, mark);
        Assert.Equal(mine.EvidenceId, Assert.Single(got).EvidenceId);
    }

    [Fact]
    public void An_earlier_runs_evidence_stamped_after_the_start_is_not_pulled_in()
    {
        var ws = NewCase();
        var start = DateTimeOffset.UtcNow;
        Store(ws, "earlier.txt", "Col", start.AddMinutes(10));             // earlier run, stamped by a clock that was ahead
        var mark = AcquisitionRunRecorder.EvidenceMark(ws);
        var mine = Store(ws, "mine.txt", "Col", start.AddSeconds(1));
        Store(ws, "other.txt", "Other", start.AddSeconds(2));
        var got = AcquisitionRunRecorder.EvidenceSince(ws, "Col", start, mark);
        Assert.Equal(mine.EvidenceId, Assert.Single(got).EvidenceId);

        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        rec.AddInterrupted("Col", "1", EvidenceStatus.Partial, "colectare întreruptă (anulată)", start, DateTimeOffset.UtcNow, mark);
        rec.Complete(OperationState.Cancelled, "anulat");
    }

    [Fact]
    public void Without_a_mark_the_start_time_is_still_used()
    {
        var ws = NewCase();
        var start = DateTimeOffset.UtcNow;
        Store(ws, "old.txt", "Col", start.AddMinutes(-5));
        var mine = Store(ws, "new.txt", "Col", start.AddSeconds(1));
        Assert.Equal(mine.EvidenceId, Assert.Single(AcquisitionRunRecorder.EvidenceSince(ws, "Col", start)).EvidenceId);
    }
}
