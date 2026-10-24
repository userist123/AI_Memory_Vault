using Xunit;

namespace LogAnalyzer.Edition.Tests;

/// <summary>Each edition fixes its station posture at compile time (a constant in its own composition) and ships as its own executable in its own folder.</summary>
public sealed class EditionPostureTests
{
    // StationPosture: Standalone = 0, IsolatedNetwork = 1, Internet = 2 (LogAnalyzer.Core.Services.Edition.StationPosture).
    public static TheoryData<string, string, int> Editions => new()
    {
        { "StandaloneOutput", "LogAnalyzer.Standalone", 0 },
        { "NetworkOutput", "LogAnalyzer.Retea", 1 },
        { "InternetOutput", "LogAnalyzer.Internet", 2 },
    };

    [Theory, MemberData(nameof(Editions))]
    public void Each_edition_reports_its_fixed_posture_and_ships_its_own_executable(string outputKey, string assemblyName, int expectedPosture)
    {
        var dir = BuildOutputInspector.Output(outputKey);
        var dll = Path.Combine(dir, assemblyName + ".dll");
        Assert.True(File.Exists(dll), "missing " + dll);
        Assert.True(File.Exists(Path.Combine(dir, assemblyName + ".exe")), "missing the executable " + assemblyName + ".exe");
        Assert.Equal(expectedPosture, BuildOutputInspector.ConstantInt32(dll, "LogAnalyzer.UI.Services.EditionComposition", "Posture"));
    }

    [Fact]
    public void The_three_editions_use_three_different_output_folders()
    {
        var dirs = new[] { "StandaloneOutput", "NetworkOutput", "InternetOutput" }.Select(k => Path.GetFullPath(BuildOutputInspector.Output(k))).ToList();
        Assert.Equal(3, dirs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
