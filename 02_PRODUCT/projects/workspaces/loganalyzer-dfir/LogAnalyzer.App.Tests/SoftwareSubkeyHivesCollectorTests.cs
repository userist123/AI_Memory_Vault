using System.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #13 D1: the Quick profile saves three small SOFTWARE subkey hives (reg save of the subkey) instead of the whole hive; Standard and Full keep the full hive. Tiny fake files only.</summary>
public sealed class SoftwareSubkeyHivesCollectorTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    private LogAnalyzer.Dfir.Case.CaseWorkspace? _ws;
    public void Dispose() => _k.Dispose();

    private static ToolRun Run(string exe, IReadOnlyList<string> args, int exit) =>
        new(exe, args, exit, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

    private static (ToolRunFunc Fn, List<IReadOnlyList<string>> Calls) Fake(Func<string, int> exitFor)
    {
        var calls = new List<IReadOnlyList<string>>();
        return ((exe, args, _, _, _, _) =>
        {
            calls.Add(args);
            int exit = exitFor(args[1]);
            if (exit == 0) File.WriteAllText(args[2], "regf-fake");
            return Run(exe, args, exit);
        }, calls);
    }

    private CollectorOutcome Collect(ToolRunFunc fn, bool elevated = true, Func<string, bool>? exists = null)
    {
        var ws = _ws = _k.Case();
        return new SoftwareSubkeyHivesCollector(fn, exists ?? (_ => true)).Collect(new CollectorContext { Case = ws, IsElevated = elevated }, default);
    }

    [Fact]
    public void Quick_run_saves_the_three_subkeys_with_exact_commands_and_registers_them_with_type_and_original_path()
    {
        var (fn, calls) = Fake(_ => 0);
        var o = Collect(fn);
        Assert.Equal(3, calls.Count);
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", calls[0][1]);
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows Defender", calls[1][1]);
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender", calls[2][1]);
        foreach (var c in calls) { Assert.Equal(4, c.Count); Assert.Equal("save", c[0]); Assert.Equal("/y", c[3]); }
        Assert.Equal(3, calls.Select(c => c[2]).Distinct().Count());
        Assert.Equal(3, o.Evidence.Count);
        Assert.All(o.Evidence, e => { Assert.Equal("software_subkey_hive", e.SourceType); Assert.Equal(TemporalType.CurrentSnapshot, e.TemporalType); Assert.False(string.IsNullOrEmpty(e.Sha256)); });
        Assert.Equal(calls.Select(c => c[1]), o.Evidence.Select(e => e.OriginalPath));
        Assert.Equal([@"Microsoft\Windows\CurrentVersion\Policies\System", @"Microsoft\Windows Defender", @"Policies\Microsoft\Windows Defender"], o.Evidence.Select(e => ConfigurationHiveReader.SubkeyOf(e)));
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Equal(3, o.Commands.Count);
    }

    [Fact]
    public void A_missing_key_is_an_error_of_the_outcome_while_the_others_are_still_saved()
    {
        var (fn, _) = Fake(key => key.Contains(@"\Policies\Microsoft\", StringComparison.Ordinal) ? 1 : 0);
        var o = Collect(fn);
        Assert.Equal(2, o.Evidence.Count);
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Contains(@"Policies\Microsoft\Windows Defender: exit 1", Assert.Single(o.Errors));
    }

    [Fact]
    public void All_failing_is_a_failed_outcome_and_not_elevated_runs_nothing()
    {
        var (fn, _) = Fake(_ => 1);
        Assert.Equal(EvidenceStatus.Failed, Collect(fn).Status);
        var (fn2, calls2) = Fake(_ => 0);
        var o = Collect(fn2, elevated: false);
        Assert.Empty(calls2);
        Assert.Equal(EvidenceStatus.NotAvailable, o.Status);
    }

    [Fact]
    public void Profiles_quick_only_while_standard_and_full_keep_the_full_hive_collector_unchanged()
    {
        Assert.Equal(CollectionProfile.Quick, new SoftwareSubkeyHivesCollector().Profiles);
        Assert.Equal(CollectionProfile.StandardAndUp, new SoftwareHiveCollector().Profiles);
        Assert.Single(InvestigationPipeline.AllCollectors, x => x is SoftwareSubkeyHivesCollector);
        var quick = InvestigationPipeline.AllCollectors.Where(x => (x.Profiles & CollectionProfile.Quick) != 0).ToList();
        Assert.Contains(quick, x => x is SoftwareSubkeyHivesCollector);
        Assert.DoesNotContain(quick, x => x is SoftwareHiveCollector);
        foreach (var p in new[] { CollectionProfile.Standard, CollectionProfile.FullForensic })
        {
            var set = InvestigationPipeline.AllCollectors.Where(x => (x.Profiles & p) != 0).ToList();
            Assert.Contains(set, x => x is SoftwareHiveCollector);
            Assert.DoesNotContain(set, x => x is SoftwareSubkeyHivesCollector);
        }
    }

    [Fact]
    public void An_absent_key_is_not_saved_has_no_error_and_leaves_a_hashed_absence_marker()
    {
        var (fn, calls) = Fake(_ => 0);
        var o = Collect(fn, exists: sub => !sub.StartsWith("Policies", StringComparison.Ordinal));
        Assert.Equal(2, calls.Count);
        Assert.DoesNotContain(calls, c => c[1].Contains(@"SOFTWARE\Policies\", StringComparison.Ordinal));
        Assert.Empty(o.Errors);
        Assert.Equal(EvidenceStatus.Success, o.Status);
        var m = Assert.Single(o.Evidence, e => e.SourceType == "software_subkey_absent");
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender", m.OriginalPath);
        Assert.Equal(TemporalType.CurrentSnapshot, m.TemporalType);
        Assert.False(string.IsNullOrEmpty(m.Sha256));
        var json = File.ReadAllText(_ws!.FullPath(m.StoredPath));
        Assert.Contains("\"exists\":false", json);
        Assert.Contains("Policies", json);
    }

    [Fact]
    public void A_key_that_exists_but_cannot_be_saved_is_still_an_error_and_has_no_marker()
    {
        var (fn, calls) = Fake(key => key.Contains(@"\Policies\Microsoft\", StringComparison.Ordinal) ? 1 : 0);
        var o = Collect(fn);
        Assert.Equal(3, calls.Count);
        Assert.Single(o.Errors);
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == "software_subkey_absent");
    }
}
