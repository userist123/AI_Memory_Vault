using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Flow;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>WP18 S4: guided flows (back, stop, resume, validation), the control period, the comparison with a previous control, the single result screen and the intake scan.</summary>
public sealed class Wp18FlowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-flow-" + Guid.NewGuid().ToString("N"));
    public Wp18FlowTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void A_flow_goes_forward_only_when_the_step_check_passes_and_back_stop_resume_keep_the_place()
    {
        string answer = "";
        var flow = new GuidedFlow("Verifică această stație",
        [
            new("station", "Ce stație?", "Această stație.", null),
            new("period", "Ce perioadă?", "Alegeți perioada.", () => answer.Length == 0 ? "Alegeți o perioadă." : null),
            new("procedures", "Ce proceduri?", "Confirmați profilul.", null),
        ]);
        Assert.Equal("Pasul 1 din 3", flow.ProgressText);
        Assert.False(flow.CanGoBack);
        Assert.True(flow.Next());
        Assert.False(flow.Next());                       // the period is not chosen
        Assert.Equal("Alegeți o perioadă.", flow.LastError);
        Assert.Equal(1, flow.StepIndex);
        flow.Stop();
        Assert.True(flow.Stopped);
        Assert.Equal(1, flow.StepIndex);                 // nothing lost
        flow.Resume();
        answer = "30";
        Assert.True(flow.Next());
        Assert.Equal("", flow.LastError);
        Assert.True(flow.IsLastStep);
        Assert.True(flow.Back());
        Assert.Equal(1, flow.StepIndex);
        Assert.True(flow.Next());
        Assert.True(flow.Next());                        // last step completes
        Assert.True(flow.Completed);
        Assert.Equal("Gata", flow.ProgressText);
        Assert.False(flow.Next());
        flow.Restart();
        Assert.Equal(0, flow.StepIndex); Assert.False(flow.Completed);
    }

    private static ControlCheck C(string id, ControlStatus s, string area = "Conturi") => new(id, area, "Verificarea " + id, s, "detaliu", [], "recomandare");

    private static StationFacts Facts(bool admin = true, bool readable = true, DateTimeOffset? collected = null) => new()
    {
        Host = "PC1", CollectedUtc = collected ?? new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero), IsAdministrator = admin,
        Coverage = { new ChannelCoverage("Security", null, readable ? 100 : 0, readable, ""), new ChannelCoverage("System", null, 10, true, "") },
    };

    [Fact]
    public void The_period_choice_since_last_control_uses_the_previous_control_and_falls_back_to_90_days_with_a_note()
    {
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var prev = new PreviousControl("x", now.AddDays(-20), "PC1", []);
        Assert.Equal(21, ControlPeriods.Days(ControlPeriodChoice.SinceLastControl, prev, 0, now, out var note));
        Assert.Contains("suprapunere", note);
        Assert.Equal(90, ControlPeriods.Days(ControlPeriodChoice.SinceLastControl, null, 0, now, out note));
        Assert.Contains("Nu există un control anterior", note);
        Assert.Equal(30, ControlPeriods.Days(ControlPeriodChoice.Days30, null, 0, now, out _));
        Assert.Equal(0, ControlPeriods.Days(ControlPeriodChoice.Custom, null, 0, now, out note));
        Assert.Contains("între 1 și", note);
        Assert.Equal(400, ControlPeriods.Days(ControlPeriodChoice.Custom, null, 400, now, out _));
    }

    [Fact]
    public void Previous_controls_are_read_from_the_case_and_compared_as_a_difference()
    {
        var folder = Path.Combine(_dir, "Control", "CONTROL_20261001_080000"); Directory.CreateDirectory(folder);
        var facts = Facts(collected: new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var json = JsonSerializer.Serialize(new { Facts = facts, Checks = new[] { C("A01", ControlStatus.Conform), C("A02", ControlStatus.Neconform), C("A03", ControlStatus.DeVerificat) }, Users = Array.Empty<object>(), Actions = Array.Empty<object>() },
            new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
        File.WriteAllText(Path.Combine(folder, "control_report.json"), json);
        Directory.CreateDirectory(Path.Combine(_dir, "Control", "CONTROL_bad"));   // no report inside: listed as a problem, never guessed

        var list = ControlArchive.List(_dir, out var problems);
        var prev = Assert.Single(list);
        Assert.Equal(3, prev.Checks.Count);
        Assert.Single(problems);
        Assert.Contains("CONTROL_bad", problems[0]);

        var cmp = ControlComparison.Compare(prev, [C("A01", ControlStatus.Neconform), C("A02", ControlStatus.Conform), C("A03", ControlStatus.DeVerificat), C("A04", ControlStatus.Nedeterminat)]);
        Assert.Equal("A01", Assert.Single(cmp.Worse).Id);
        Assert.Equal("A02", Assert.Single(cmp.Better).Id);
        Assert.Equal("A04", Assert.Single(cmp.New).Id);
        Assert.Empty(cmp.Removed);
        Assert.Equal(1, cmp.Unchanged);
        Assert.Contains("1 înrăutățite, 1 îmbunătățite, 1 noi", cmp.Summary);
    }

    [Fact]
    public void Result_screen_answers_the_five_questions_and_nothing_found_carries_the_coverage()
    {
        var profile = ProfileSummary.Sections(null);
        Assert.All(profile, s => Assert.False(s.Defined));

        var bad = new ControlReport { Facts = Facts(admin: false) };
        bad.Checks.AddRange([C("A01", ControlStatus.Neconform, "Conturi"), C("P01", ControlStatus.Neconform, "Politici"), C("U01", ControlStatus.DeVerificat), C("N01", ControlStatus.Nedeterminat), C("S01", ControlStatus.Conform)]);
        var r = ControlResultScreen.Build(bad, profile);
        Assert.StartsWith("ATENȚIE", r.Headline);
        Assert.Contains("2 verificări sunt NECONFORME", r.Problem);
        Assert.Contains("Conturi", r.Seriousness); Assert.Contains("Politici", r.Seriousness);
        Assert.Contains("FĂRĂ drepturi de administrator", r.Trust);
        Assert.Contains("NECONFORM 2", r.Found);
        Assert.InRange(r.NextSteps.Count, 1, ControlResultScreen.MaxNextSteps);
        Assert.Contains(r.NextSteps, s => s.Contains("administrator"));
        Assert.Contains(r.NextSteps, s => s.Contains("profilul de proceduri"));
        Assert.False(r.NothingFound);

        var clean = new ControlReport { Facts = Facts(readable: false) };
        clean.Checks.AddRange([C("S01", ControlStatus.Conform), C("N01", ControlStatus.Nedeterminat)]);
        var n = ControlResultScreen.Build(clean, profile);
        Assert.True(n.NothingFound);
        Assert.StartsWith("NIMIC NECONFORM GĂSIT", n.Headline);
        Assert.Contains("necitite: Security", n.Problem);          // the coverage travels with "nothing found"
        Assert.Contains("NEDETERMINATE", n.Seriousness);
        Assert.DoesNotContain("SAFE", n.Headline + n.Problem + n.Trust + n.Found);
        Assert.DoesNotContain("sistem curat", (n.Headline + n.Problem).ToLowerInvariant());
    }

    [Fact]
    public void Intake_scan_lists_what_is_present_what_is_missing_and_what_is_not_evidence()
    {
        var f = Path.Combine(_dir, "incoming"); Directory.CreateDirectory(Path.Combine(f, "sub"));
        File.WriteAllText(Path.Combine(f, "Security.evtx"), "x"); File.WriteAllText(Path.Combine(f, "sub", "NOTEPAD.EXE-1234.pf"), "xx");
        File.WriteAllText(Path.Combine(f, "SRUDB.dat"), "xxx"); File.WriteAllText(Path.Combine(f, "readme.txt"), "not evidence");
        var scan = IncomingEvidence.Scan(f);
        Assert.True(scan.HasAnything);
        Assert.Equal(3, scan.Importable.Count);
        Assert.Single(scan.Ignored);
        Assert.Equal(3, scan.PresentFamilies);
        Assert.Contains("Captură de rețea", scan.MissingFamilies);
        Assert.Contains("lipsesc:", scan.Summary);
        Assert.Contains("nu sunt probe recunoscute", scan.Summary);
        var none = IncomingEvidence.Scan(Path.Combine(_dir, "nope"));
        Assert.False(none.HasAnything);
        Assert.NotEmpty(none.Problems);
    }

    [Fact]
    public void Station_control_guided_flow_refuses_an_empty_custom_period_and_keeps_answers_across_back_and_stop()
    {
        var vm = new StationControlViewModel();
        Assert.Equal("Pasul 1 din 3", vm.FlowProgress);
        vm.FlowNextCommand.Execute(null);
        Assert.Equal(1, vm.FlowStepIndex);
        vm.PeriodChoice = ControlPeriodChoice.Custom; vm.CustomDays = 0;
        vm.FlowNextCommand.Execute(null);
        Assert.Equal(1, vm.FlowStepIndex);
        Assert.Contains("între 1 și", vm.FlowError);
        vm.CustomDays = 45;
        vm.FlowNextCommand.Execute(null);
        Assert.Equal(2, vm.FlowStepIndex);
        Assert.Equal(45, vm.PeriodDays);
        Assert.NotEmpty(vm.ProfileSections);
        vm.FlowStopCommand.Execute(null);
        Assert.True(vm.FlowStopped);
        vm.FlowBackCommand.Execute(null);
        Assert.Equal(1, vm.FlowStepIndex); Assert.Equal(45, vm.CustomDays); Assert.False(vm.FlowStopped);
    }

    [Fact]
    public void Investigation_intake_flow_scans_the_folder_before_anything_is_imported()
    {
        var f = Path.Combine(_dir, "in2"); Directory.CreateDirectory(f); File.WriteAllText(Path.Combine(f, "App.evtx"), "x");
        var inv = new InvestigationViewModel();
        Assert.Equal("Pasul 1 din 3", inv.IntakeProgress);
        inv.IntakeNextCommand.Execute(null);                       // no folder chosen yet
        Assert.Equal(0, inv.IntakeStepIndex); Assert.Contains("folder", inv.IntakeError, StringComparison.OrdinalIgnoreCase);
        inv.IntakeFolder = f;
        inv.IntakeNextCommand.Execute(null);
        Assert.Equal(1, inv.IntakeStepIndex);
        Assert.NotNull(inv.IntakeScan);
        Assert.Single(inv.ImportFiles);
        Assert.False(inv.CollectFromThisStation);
        Assert.Contains("lipsesc", inv.IntakeScan!.Summary);
    }
}
