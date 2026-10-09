using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP15b: the text of the "Cronologie politici" section, shared by the investigation view and the investigation PDF.</summary>
public sealed class PolicyTimelineReportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static PolicyChain Chain(BehaviorState b, string why, LevelState app = LevelState.Unknown) => new()
    {
        Change = new PolicyChange { ChangeId = "PC-001", TimeUtc = T0, Kind = "DS_MODIFIED", EventId = "5136", Who = "CORP\\admin", TargetKind = "GPO", TargetId = "{G}", Attribute = "versionNumber", OldValue = "5", NewValue = "6" },
        ApplicationState = app, ApplicationReason = "fără jurnal", Behavior = b, BehaviorReason = why,
    };

    [Fact]
    public void Each_chain_lists_before_after_application_and_behaviour_with_the_reason()
    {
        var r = new PolicyTimelineResult { Changes = [Chain(BehaviorState.NotEvaluable, "SYSVOL lipsește").Change], Chains = [Chain(BehaviorState.NotEvaluable, "SYSVOL lipsește")] };
        var text = string.Join("\n", PolicyTimelineReport.Lines(r).Select(l => l.Text));
        Assert.Contains("PC-001", text); Assert.Contains("înainte: 5", text); Assert.Contains("după: 6", text);
        Assert.Contains("Aplicare", text); Assert.Contains("Comportament: neevaluabil", text); Assert.Contains("SYSVOL lipsește", text);
        Assert.Contains("observat", string.Join("\n", PolicyTimelineReport.Lines(new PolicyTimelineResult { Chains = [Chain(BehaviorState.Observed, "x")] }).Select(l => l.Text)));
        Assert.Contains("neobservat", string.Join("\n", PolicyTimelineReport.Lines(new PolicyTimelineResult { Chains = [Chain(BehaviorState.NotObserved, "x")] }).Select(l => l.Text)));
    }

    [Fact]
    public void Levels_and_gaps_are_listed_and_an_undefined_policy_is_nedefinit_never_conform()
    {
        var none = string.Join("\n", PolicyTimelineReport.Lines(new PolicyTimelineResult { ExpectedNote = "Politica așteptată: nedefinit." }).Select(l => l.Text));
        Assert.Contains("nedefinit", none); Assert.DoesNotContain("conform", none, StringComparison.OrdinalIgnoreCase);

        var s = new ExpectedSetting("AUD-01", "Audit procese", "audit", "Process Creation", "Success", "Success", "P 1.0");
        var l = new SettingLevels
        {
            Setting = s, Configured = new() { State = LevelState.Observed, Reason = "cerut" }, Applied = new() { State = LevelState.Unknown, Reason = "fără GP" },
            Enforced = new() { State = LevelState.Unknown, Reason = "necolectat" }, Observed = new() { State = LevelState.NotObserved, Reason = "niciun 4688" },
        };
        var r = new PolicyTimelineResult { Levels = [l], Gaps = [new ControlGap { SettingId = "AUD-01", Title = "Audit procese", BrokenLevel = "observed", Severity = Severity.High, Reason = "niciun 4688", Summary = "rezumat" }] };
        var lines = PolicyTimelineReport.Lines(r);
        var text = string.Join("\n", lines.Select(x => x.Text));
        Assert.Contains("configured=OBSERVED", text); Assert.Contains("observed=NOT_OBSERVED", text); Assert.Contains("applied=UNKNOWN", text);
        Assert.Contains("Decalaj", text); Assert.Contains("High", text);
        Assert.Contains(lines, x => x.Style == PolicyLineStyle.Bad && x.Text.Contains("Decalaj"));
    }

    [Fact]
    public void Time_manipulation_rows_show_magnitude_direction_and_the_benign_ones_are_not_alarmed()
    {
        var tm = new TimeManipulationResult
        {
            Jumps = [new ClockJump("EventLog:Security", "4616", T0, T0, T0.AddHours(-3), TimeSpan.FromHours(-3), "înapoi", "admin", "x.exe", false, "", new("EV", "L1", "d")),
                     new ClockJump("EventLog:Security", "4616", T0, T0, T0.AddSeconds(-2), TimeSpan.FromSeconds(-2), "înapoi", "", "svchost.exe", true, "sincronizare", new("EV", "L2", "d"))],
            Windows = [new ManipulatedWindow(T0.AddHours(-3), T0, "CLOCK_JUMP", "ceasul a fost mutat", "EV", "L1")],
        };
        var lines = PolicyTimelineReport.Lines(new PolicyTimelineResult { Time = tm });
        Assert.Contains(lines, x => x.Text.Contains("3h") && x.Text.Contains("înapoi") && x.Style == PolicyLineStyle.Bad);
        Assert.DoesNotContain(lines, x => x.Text.Contains("svchost") && x.Style == PolicyLineStyle.Bad);
        Assert.Contains(lines, x => x.Text.Contains("TEMPORAL") || x.Text.Contains("oră nesigură"));
    }

    [Fact]
    public void Long_lists_are_capped_with_a_visible_note()
    {
        var chains = Enumerable.Range(1, 60).Select(i => Chain(BehaviorState.NotEvaluable, "x")).ToList();
        var lines = PolicyTimelineReport.Lines(new PolicyTimelineResult { Chains = chains }, maxChains: 10);
        Assert.Contains(lines, x => x.Text.Contains("50 modificări suplimentare"));
    }
}
