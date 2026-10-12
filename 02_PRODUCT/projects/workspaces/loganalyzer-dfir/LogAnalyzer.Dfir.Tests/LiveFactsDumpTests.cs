using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Runs only when this variable names an output directory outside the repository.</summary>
public sealed class LiveFactsDumpFactAttribute : FactAttribute
{
    public const string Variable = "LA_DUMP_FACTS";

    public LiveFactsDumpFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to an output directory (outside the repository) to dump the live facts of this station.";
    }
}

/// <summary>
/// Runtime verification aid: collects the live facts of THIS machine (read-only), evaluates the station checks and writes station_facts.json,
/// airgap_facts.json, checks.json and a control report PDF to the directory named by LA_DUMP_FACTS. Nothing is written inside the repository.
/// </summary>
public class LiveFactsDumpTests
{
    private static readonly JsonSerializerOptions Out = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    [LiveFactsDumpFact]
    public void Dump_the_live_facts_checks_and_control_report_of_this_station()
    {
        var dir = Path.GetFullPath(Environment.GetEnvironmentVariable(LiveFactsDumpFactAttribute.Variable)!);
        var repo = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(repo); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))
                Assert.False(dir.StartsWith(d.FullName, StringComparison.OrdinalIgnoreCase), "LA_DUMP_FACTS must point outside the repository.");
        Directory.CreateDirectory(dir);

        var elevated = ShadowCopies.IsAdministrator();
        var station = StationFactCollector.Collect(DateTimeOffset.UtcNow.AddDays(-30), stationShouldBeIsolated: true);
        var airgap = AirGapFactsCollector.Read(elevated, null);
        File.WriteAllText(Path.Combine(dir, "station_facts.json"), JsonSerializer.Serialize(station, Out));
        File.WriteAllText(Path.Combine(dir, "airgap_facts.json"), airgap.ToJson());

        var report = ControlEvaluator.Evaluate(station);
        var checks = report.Checks.OrderBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => new { c.Id, Status = c.Status.ToString(), Reason = OneLine(c.Detail) });
        File.WriteAllText(Path.Combine(dir, "checks.json"), JsonSerializer.Serialize(checks, Out));

        var pdf = Path.Combine(dir, "control_report.pdf");
        ControlReportPdf.Write(report, pdf, "dump");
        Assert.True(File.Exists(pdf) && new FileInfo(pdf).Length > 1000);
        Assert.True(report.Checks.Count > 0);
    }

    private static string OneLine(string s) => string.Join(' ', s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
