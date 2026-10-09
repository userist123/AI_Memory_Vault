using System.IO;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>WP14a: the "Registru medii" / "Registru utilizatori" screens' view models without a dialog or a window (these tests need Windows to run, like the rest of this project).</summary>
public sealed class RegisterViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-regvm-" + Guid.NewGuid().ToString("N"));
    public RegisterViewModelTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Without_a_saved_register_the_register_is_nedefinit_and_never_conform()
    {
        var vm = new MediaRegisterViewModel(Path.Combine(_dir, "media.json"));
        Assert.Contains("nedefinit", vm.Definition);
        Assert.DoesNotContain("conform", vm.Definition + vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.Rows());
    }

    [Fact]
    public void Paste_adds_valid_rows_and_lists_invalid_lines_without_dropping_them_silently()
    {
        var vm = new MediaRegisterViewModel(Path.Combine(_dir, "media.json"));
        vm.ImportText("M-1\tS1\t\tUSB\t\t\t\t\t\tactive\t\nM-2\tS2\t\tTAPE\t\t\t\t\t\tactive\t", "test");
        Assert.Single(vm.Rows());
        Assert.Contains(vm.Issues, i => i.Contains("linia 2") && i.Contains("TAPE"));
        Assert.Contains("1 rânduri adăugate, 1 respinse", vm.Status);
    }

    [Fact]
    public void Save_refuses_while_errors_remain_and_saves_with_an_audit_line_once_fixed()
    {
        var path = Path.Combine(_dir, "media.json");
        var vm = new MediaRegisterViewModel(path);
        vm.Data.Rows.Add("", "S1", "", "USB", "", "", "", "", "", "active", "");
        vm.SaveCommand.Execute(null);
        Assert.False(File.Exists(path));
        Assert.Contains(vm.Issues, i => i.Contains("număr de înregistrare"));
        vm.Data.Rows.Clear(); vm.Data.Rows.Add("M-1", "S1", "", "USB", "NATO SECRET", "", "", "", "", "active", "");
        vm.SaveCommand.Execute(null);
        Assert.True(File.Exists(path));
        Assert.Equal(64, vm.Sha256.Length);
        Assert.True(File.Exists(RegisterStore.AuditPathFor(path)));
        Assert.Contains("definit (1 rânduri)", vm.Definition);
    }

    [Fact]
    public void The_users_register_shows_the_authentication_gap_notice()
    {
        var vm = new UsersRegisterViewModel(Path.Combine(_dir, "users.json"));
        Assert.Equal("editare permisă administratorului global; autentificarea în aplicație nu este încă implementată", vm.Notice);
        Assert.Equal("", new MediaRegisterViewModel(Path.Combine(_dir, "m.json")).Notice);
    }

    [Fact]
    public void Json_import_with_a_bad_file_changes_nothing()
    {
        var vm = new MediaRegisterViewModel(Path.Combine(_dir, "media.json"));
        vm.ImportJsonText("{ nu e json", "test.json");
        Assert.Contains("a eșuat", vm.Status);
        Assert.Empty(vm.Rows());
    }
}
