using System.IO;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Owner decision: the Internet edition on an isolated-looking station shows "Ediție nepotrivită" on Home.</summary>
public sealed class EditionWarningHomeTests
{
    private static HomeViewModel New(Func<string?> warning) =>
        new(new InvestigationViewModel(), null, new RecentCases(Path.Combine(Path.GetTempPath(), "la-edwarn-" + Guid.NewGuid().ToString("N") + ".json")), () => null,
            editionWarning: warning);

    [Fact]
    public void Home_shows_the_wrong_edition_warning_when_there_is_one()
    {
        var vm = New(() => EditionFit.WrongEditionText);
        Assert.True(vm.HasEditionWarning);
        Assert.Equal(EditionFit.WrongEditionText, vm.EditionWarning);
    }

    [Fact]
    public void Home_shows_nothing_without_a_warning()
    {
        var vm = New(() => null);
        Assert.False(vm.HasEditionWarning);
        Assert.Equal("", vm.EditionWarning);
    }

    [Fact]
    public void Home_view_binds_the_warning()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "LogAnalyzer.App", "Views", "HomeView.xaml"));
        Assert.Contains("{Binding EditionWarning}", xaml);
        Assert.Contains("{Binding HasEditionWarning}", xaml);
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }
}
