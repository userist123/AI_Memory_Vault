using System.IO;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>WP15a: the "Profil de proceduri" screen's view model without a dialog or a window (these tests need Windows to run, like the rest of this project).</summary>
public sealed class ProcedureProfileViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-ppvm-" + Guid.NewGuid().ToString("N"));
    public ProcedureProfileViewModelTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private ProcedureProfileViewModel New(out ProfileProvider provider)
    {
        provider = new ProfileProvider(Path.Combine(_dir, "procedure_profile.json"));
        return new ProcedureProfileViewModel(provider);
    }

    [Fact]
    public void Without_a_saved_profile_every_section_is_nedefinit_and_there_is_a_table_for_each_part()
    {
        var vm = New(out _);
        Assert.Equal(ProfileTables.All.Count, vm.Tables.Count);
        Assert.All(vm.SectionLines, l => Assert.EndsWith("nedefinit", l));
        Assert.DoesNotContain(vm.SectionLines, l => l.Contains("conform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Paste_adds_valid_rows_and_lists_invalid_lines_without_dropping_them_silently()
    {
        var vm = New(out _);
        vm.SelectedTable = vm.Tables.Single(t => t.Table == ProfileTable.WorkingHours);
        vm.ImportText("Mon\t08:00\t16:00\nFunday\t08:00\t16:00", "test");
        Assert.Single(vm.SelectedTable.Rows());
        Assert.Contains(vm.Issues, i => i.Contains("linia 2") && i.Contains("Funday"));
        Assert.Contains("1 rânduri adăugate, 1 respinse", vm.Status);
    }

    [Fact]
    public void Save_refuses_while_errors_remain_and_saves_and_activates_the_profile_once_fixed()
    {
        var vm = New(out var provider);
        var zones = vm.Tables.Single(t => t.Table == ProfileTable.Zones);
        zones.Data.Rows.Add("", "fără nume");
        vm.SaveCommand.Execute(null);
        Assert.False(File.Exists(provider.Path));
        Assert.Contains(vm.Issues, i => i.Contains("numele zonei"));
        zones.Data.Rows.Clear(); zones.Data.Rows.Add("ROSU", "clasificat");
        vm.SaveCommand.Execute(null);
        Assert.True(File.Exists(provider.Path));
        Assert.Equal(64, vm.Sha256.Length);
        Assert.Equal(["ROSU"], provider.Current!.Zones.Select(z => z.Name));
        Assert.Contains(vm.SectionLines, l => l.StartsWith("Zone și transferuri: definit"));
    }

    [Fact]
    public void Json_import_with_a_bad_file_changes_nothing()
    {
        var vm = New(out _);
        vm.ImportJsonText("{ nu e json", "test.json");
        Assert.Contains("a eșuat", vm.Status);
        Assert.All(vm.Tables, t => Assert.Empty(t.Rows()));
    }
}
