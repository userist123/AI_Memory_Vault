using System.IO;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Lab 2026-11-10 (Insula and Arhipelag on the VMs): after "Colectează tot" the intake assistant kept its default "Un folder adus de la stația afectată"
/// with an empty folder, although the probes had just been collected from this station.
/// </summary>
public class CollectAllShowsItsSourceTests
{
    [Fact]
    public void Collect_all_sets_the_assistant_to_this_station()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "LogAnalyzer.App", "ViewModels"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!, "LogAnalyzer.App", "ViewModels", "InvestigationViewModel.Reports.cs"));
        int start = src.IndexOf("private async Task CollectAll()", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = src.Substring(start, src.IndexOf("await Run();", start, StringComparison.Ordinal) - start);
        Assert.Contains("CollectFromThisStation = true;", body);
        Assert.Contains("IntakeFromThisStation = true;", body);
    }
}
