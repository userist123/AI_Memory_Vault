using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// A20 (research 2026-11-07, plan P1.5): the logs that record a stick or disk being connected. Partition/Diagnostic is on by default, so finding it off is a
/// finding; DriverFrameworks-UserMode is off by default and is recommended. Neither shows which files were copied.
/// </summary>
public sealed class DeviceConnectionLogsTests
{
    private static ControlCheck A20(string? partition, string? driverFrameworks)
    {
        var f = new StationFacts { Posture = StationPosture.Standalone, IsAdministrator = true };
        if (partition is not null) f.Settings["DeviceLog.PartitionEnabled"] = partition;
        if (driverFrameworks is not null) f.Settings["DeviceLog.DriverFrameworksEnabled"] = driverFrameworks;
        return ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "A20");
    }

    [Fact]
    public void Both_logs_on_is_conform_and_says_that_copied_files_are_not_shown_here()
    {
        var c = A20("1", "1");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains("Ce fișiere s-au copiat nu se vede aici", c.Detail);
    }

    [Fact]
    public void The_Windows_default_only_Partition_on_is_to_be_checked_with_the_fix_named()
    {
        var c = A20("1", "0");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("cum este implicit în Windows", c.Detail);
        Assert.Contains("DriverFrameworks-UserMode/Operational", c.Recommendation);
    }

    [Fact]
    public void Partition_Diagnostic_turned_off_is_non_conform_because_Windows_keeps_it_on()
    {
        var c = A20("0", "1");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("cineva l-a oprit", CheckGuides.For("A20").Why);
        Assert.Contains("Aflați cine l-a oprit", c.Detail);
    }

    [Fact]
    public void Unreadable_logs_are_never_conform()
    {
        Assert.Equal(ControlStatus.Nedeterminat, A20(null, null).Status);
        Assert.Equal(ControlStatus.DeVerificat, A20("1", null).Status);
    }
}
