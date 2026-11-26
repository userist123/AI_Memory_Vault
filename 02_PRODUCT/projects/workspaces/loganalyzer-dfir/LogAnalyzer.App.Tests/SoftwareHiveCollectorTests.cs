using System.IO;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #11 A4: the live SOFTWARE hive is saved (reg save HKLM\SOFTWARE) like SYSTEM, registered as software_hive and read by the SOFTWARE parser. Tiny fake hives only.</summary>
public sealed class SoftwareHiveCollectorTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static ToolRun Run(string exe, IReadOnlyList<string> args, int exit) =>
        new(exe, args, exit, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

    private static (ToolRunFunc Fn, List<IReadOnlyList<string>> Calls) Fake(int exit, bool writeFile = true)
    {
        var calls = new List<IReadOnlyList<string>>();
        return ((exe, args, _, _, _, _) =>
        {
            calls.Add(args);
            if (writeFile && exit == 0) File.WriteAllText(args[2], "regf-fake");   // a few bytes, never a real hive
            return Run(exe, args, exit);
        }, calls);
    }

    private CollectorOutcome Collect(ToolRunFunc fn, bool elevated, out Dfir.Case.CaseWorkspace ws)
    {
        ws = _k.Case();
        return new SoftwareHiveCollector(fn).Collect(new CollectorContext { Case = ws, IsElevated = elevated }, default);
    }

    [Fact]
    public void Elevated_run_saves_software_with_reg_save_and_registers_software_hive_once()
    {
        var (fn, calls) = Fake(0);
        var o = Collect(fn, true, out var ws);
        var call = Assert.Single(calls);
        Assert.Equal(["save", @"HKLM\SOFTWARE", call[2], "/y"], call);
        Assert.Equal("SOFTWARE", Path.GetFileName(call[2]));
        Assert.Contains(@"save HKLM\SOFTWARE", o.Commands[0]);
        Assert.EndsWith("/y", o.Commands[0]);
        var e = Assert.Single(o.Evidence);
        Assert.Equal("software_hive", e.SourceType);
        Assert.Equal(TemporalType.CurrentSnapshot, e.TemporalType);
        Assert.Equal("Registry:SOFTWARE", e.Source);
        Assert.Equal(@"HKLM\SOFTWARE", e.OriginalPath);
        Assert.False(string.IsNullOrEmpty(e.Sha256));   // hashed like the other evidence
        Assert.Equal(EvidenceStatus.Success, o.Status);
    }

    [Fact]
    public void Failing_reg_save_is_a_failed_outcome_with_a_romanian_reason_not_an_exception()
    {
        var (fn, _) = Fake(1);
        var o = Collect(fn, true, out _);
        Assert.Empty(o.Evidence);
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        Assert.Contains("hive-ul SOFTWARE live lipsește din analiză", Assert.Single(o.Errors));
    }

    [Fact]
    public void A_tool_that_cannot_start_is_also_a_gap_not_an_abort()
    {
        var o = Collect((_, _, _, _, _, _) => throw new IOException("disc plin"), true, out _);
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        Assert.Contains("disc plin", Assert.Single(o.Errors));
    }

    [Fact]
    public void Not_elevated_runs_nothing_and_is_not_available_with_a_reason()
    {
        var (fn, calls) = Fake(0);
        var o = Collect(fn, false, out _);
        Assert.Empty(calls);
        Assert.Equal(EvidenceStatus.NotAvailable, o.Status);
        Assert.Contains("drepturi de administrator", Assert.Single(o.Errors));
    }

    [Fact]
    public void Profiles_standard_and_full_only_and_exactly_one_collector_in_the_pipeline()
    {
        var c = new SoftwareHiveCollector();
        Assert.Equal(CollectionProfile.StandardAndUp, c.Profiles);
        Assert.True(c.RequiresElevation);
        Assert.Single(InvestigationPipeline.AllCollectors, x => x is SoftwareHiveCollector);
        Assert.DoesNotContain(InvestigationPipeline.AllCollectors.Where(x => (x.Profiles & CollectionProfile.Quick) != 0), x => x is SoftwareHiveCollector);
        Assert.Contains(InvestigationPipeline.AllCollectors.Where(x => (x.Profiles & CollectionProfile.FullForensic) != 0), x => x is SoftwareHiveCollector);
    }

    [Fact]
    public void Type_inference_maps_a_stored_software_hive_to_the_software_parser()
    {
        var p = Path.Combine(_k.Dir, "in", "SOFTWARE");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, "regf\0\0\0\0"u8.ToArray());
        var ws = _k.Case();
        var item = Assert.Single(InvestigationPipeline.Import(ws, [p]));
        Assert.Equal("software_hive", item.SourceType);
        Assert.True(new SoftwareHiveParser().Descriptor.Accepts(item));
        Assert.False(new SystemHiveExecutionParser().Descriptor.Accepts(item));
    }

    [Fact]
    public void Coverage_counts_the_software_collector_for_the_registry_row()
    {
        var parsers = (IReadOnlyList<ParserDescriptor>)[new SoftwareHiveParser().Descriptor, new UserHiveParser().Descriptor];
        var inp = new CoverageInputs([], [], [new CollectorRun("SoftwareHiveCollector", EvidenceStatus.Failed, 0, "reg save SOFTWARE: exit 1")], [], parsers);
        var row = CoverageMatrix.Build(inp).Rows.Single(r => r.FamilyId == "registry");
        Assert.Contains("SoftwareHiveCollector", row.Reason);   // the failed run is reported, no longer "not collected"
        Assert.Equal(CoverageState.Unavailable, row.State);
        Assert.Contains(ParserCapabilities.For("SoftwareHiveParser")!.Limitations, l => l.Contains("Standard și Complet") && !l.Contains("nu este colectat"));
    }
}
