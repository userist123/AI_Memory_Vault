using System.IO;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Station level on the profile page, and the register of non-conformities on the control page (matrix HG 585 rows 38 and level rules).</summary>
public sealed class CorrectiveActionsUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-cau-" + Guid.NewGuid().ToString("N"));
    public CorrectiveActionsUiTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Station_level_set_in_the_view_model_is_saved_and_found_again_after_reload()
    {
        var path = Path.Combine(_dir, "procedure_profile.json");
        var provider = new ProfileProvider(path);
        var vm = new ProcedureProfileViewModel(provider);
        Assert.Equal("", vm.StationLevel);
        vm.StationLevel = "STRICT SECRET";
        vm.SaveCommand.Execute(null);
        Assert.True(File.Exists(path));
        Assert.Equal("STRICT SECRET", provider.Current!.StationLevel);

        var again = new ProcedureProfileViewModel(new ProfileProvider(path));
        Assert.Equal("STRICT SECRET", again.StationLevel);
    }

    [Fact]
    public void A_level_written_as_an_abbreviation_is_shown_as_the_choice_name()
    {
        var path = Path.Combine(_dir, "procedure_profile.json");
        File.WriteAllText(path, "{\"schema_version\":\"1.0\",\"stationLevel\":\"SSID\"}");
        var vm = new ProcedureProfileViewModel(new ProfileProvider(path));
        Assert.Equal("STRICT SECRET DE IMPORTANȚĂ DEOSEBITĂ", vm.StationLevel);
        Assert.Contains(ProcedureProfileViewModel.StationLevelChoices, c => c.Name == vm.StationLevel);
    }

    [Fact]
    public void The_report_comparison_is_against_the_most_recent_saved_control_whatever_the_period()
    {
        var facts = new StationFacts { Host = "H", CollectedUtc = DateTimeOffset.UtcNow };
        var report = new ControlReport { Facts = facts };
        report.Checks.Add(Check("N03", ControlStatus.Neconform));
        Assert.Null(StationControlViewModel.ReportComparison(report, null));
        var prev = new PreviousControl("p", DateTimeOffset.UtcNow.AddDays(-10), "H", [Check("N03", ControlStatus.Conform)]);
        var cmp = StationControlViewModel.ReportComparison(report, prev)!;
        Assert.Equal("N03", Assert.Single(cmp.Worse).Id);
    }

    [Fact]
    public void Station_level_choices_start_with_nedefinit_then_national_then_nato_and_eu()
    {
        var c = ProcedureProfileViewModel.StationLevelChoices;
        Assert.Equal("", c[0].Name);
        Assert.Equal("nedefinit", c[0].Display);
        Assert.Equal(LogAnalyzer.Dfir.Model.SecrecyLevels.All.Count + 1, c.Count);
        Assert.Contains(c.Take(5), x => x.Name == "SECRET DE SERVICIU");
        Assert.Contains(c.Skip(5), x => x.Name == "NATO SECRET");
    }

    private static ControlCheck Check(string id, ControlStatus s) => new(id, "Test", "Titlu " + id, s, "", [], "");

    [Fact]
    public void Plan_carries_the_open_measure_of_the_previous_control()
    {
        var t0 = DateTimeOffset.UtcNow.AddDays(-30);
        var prev = new PreviousControl("p", t0, "H", [Check("A18", ControlStatus.Neconform)],
            [new CorrectiveAction { CheckId = "A18", Title = "Titlu A18", OpenedUtc = t0, Measure = "Se extinde retenția", Responsible = "Ion", Deadline = "01.12.2026" }]);
        var report = new[] { Check("A18", ControlStatus.Neconform), Check("N14", ControlStatus.Neconform) };
        var plan = CorrectiveActions.Plan(report, DateTimeOffset.UtcNow, prev);
        var carried = plan.Single(a => a.CheckId == "A18");
        Assert.Equal("Se extinde retenția", carried.Measure);
        Assert.Equal("Ion", carried.Responsible);
        Assert.Equal(t0, carried.CarriedFromUtc);
        Assert.Equal("", plan.Single(a => a.CheckId == "N14").Measure);
    }

    [Fact]
    public void A_nonconformity_without_a_measure_blocks_the_report_and_names_the_measure()
    {
        var items = new[] { new CorrectiveAction { CheckId = "N14", Title = "Internet", Responsible = "Ion", Deadline = "01.12.2026" } };
        var gate = StationControlViewModel.CorrectiveActionsGate(items);
        Assert.NotNull(gate);
        Assert.StartsWith("Completați pentru fiecare neconformitate măsura, responsabilul și termenul înainte de procesul-verbal:", gate);
        Assert.Contains("măsura", gate);
        Assert.Contains("N14", gate);
        items[0].Measure = "Se scoate placa de rețea";
        Assert.Null(StationControlViewModel.CorrectiveActionsGate(items));
    }

    [Fact]
    public void The_view_model_starts_with_no_actions_and_FlowRestart_empties_them()
    {
        var vm = new StationControlViewModel();
        Assert.Empty(vm.CorrectiveActionItems);
        vm.CorrectiveActionItems.Add(new CorrectiveAction { CheckId = "X" });
        vm.FlowRestartCommand.Execute(null);
        Assert.Empty(vm.CorrectiveActionItems);
        Assert.Equal("", vm.CorrectiveActionsMissing);
    }
}
