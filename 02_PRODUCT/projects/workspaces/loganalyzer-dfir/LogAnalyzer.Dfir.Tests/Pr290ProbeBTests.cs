using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Probe B of the third re-review of PR #290: an administrator recreates the store, reinstalls the application in a NEW folder and deletes the old .pf
/// (Prefetch on, other .pf present). The date moves later (the paper anchor of #289 is the strong control), but the station must still show DE VERIFICAT:
/// the old-folder Amcache entry and the new .pf created long after older Windows traces of the application.
/// </summary>
public sealed class Pr290ProbeBTests
{
    private static readonly DateTimeOffset Day0 = new(T0);
    private const string OldPath = @"C:\Program Files\LogAnalyzer\LogAnalyzer.Standalone.exe";
    private const string NewPath = @"D:\Apps\LA2\LogAnalyzer.Standalone.exe";

    private static AppInstallTraces.PrefetchSeen NewPf(DateTimeOffset t) =>
        new("LOGANALYZER.STANDALONE.EXE-9F.pf", t, [t.AddMinutes(1)], "LOGANALYZER.STANDALONE.EXE", @"\VOLUME{02}\APPS\LA2\LOGANALYZER.STANDALONE.EXE");

    [Fact]
    public void Probe_B_reinstall_in_a_new_folder_after_deleting_the_old_pf_leaves_DE_VERIFICAT_notes()
    {
        var reinstall = Day0.AddDays(20);
        var (traces, notes) = AppInstallTraces.Classify(
            [NewPf(reinstall)],
            [(OldPath.ToLowerInvariant(), Day0.UtcDateTime), (NewPath.ToLowerInvariant(), reinstall.UtcDateTime)],
            [], NewPath, prefetchEnabled: true, otherPrefetchFiles: 150);

        // (1) the old-folder Amcache entry is reported, DE VERIFICAT, with its path and time; it does not set the date (a last-write time is no proof of a run)
        Assert.Contains(notes, n => n.StartsWith("aplicația a existat/rulat anterior în altă locație:") && n.Contains(OldPath.ToLowerInvariant()) && n.Contains(Day0.ToString("yyyy-MM-dd")));
        Assert.DoesNotContain(traces, t => t.Utc < reinstall.AddMinutes(-1));
        // (2) probe E: the other-path trace keeps its own "altă locație" note; it is NOT compared with the new installed path's .pf
        Assert.DoesNotContain(notes, n => n.StartsWith("urmă Prefetch"));
    }

    [Fact]
    public void Probe_E_an_other_path_trace_never_marks_the_installed_pf_as_newer()
    {
        var (_, notes) = AppInstallTraces.Classify(
            [NewPf(Day0.AddDays(20))],
            [(OldPath.ToLowerInvariant(), Day0.UtcDateTime)],
            [], NewPath, true, 150);
        Assert.Contains(notes, n => n.StartsWith("aplicația a existat/rulat anterior în altă locație:"));
        Assert.DoesNotContain(notes, n => n.StartsWith("urmă Prefetch"));
    }

    [Fact]
    public void Probe_F_a_pf_newer_than_an_older_trace_of_the_same_path_is_worded_without_blaming_a_deletion()
    {
        var (_, notes) = AppInstallTraces.Classify(
            [NewPf(Day0.AddDays(20))], [(NewPath.ToLowerInvariant(), Day0.UtcDateTime)], [], NewPath, true, 150);
        var n = Assert.Single(notes, x => x.StartsWith("urmă Prefetch mai nouă decât alte urme Windows — poate fi recreată de Windows sau ștearsă"));
        Assert.DoesNotContain("probabil", n);
    }

    [Fact]
    public void A_single_installation_in_one_folder_gives_no_relocation_or_recreation_note()
    {
        var (traces, notes) = AppInstallTraces.Classify(
            [NewPf(Day0)], [(NewPath.ToLowerInvariant(), Day0.AddMinutes(2).UtcDateTime)], [], NewPath, true, 150);
        Assert.NotEmpty(traces);
        Assert.Empty(notes);
    }

    [Fact]
    public void An_older_4688_start_of_the_installed_exe_also_marks_a_much_newer_pf_as_recreated()
    {
        var (_, notes) = AppInstallTraces.Classify(
            [NewPf(Day0.AddDays(20))], [], [new AppTrace("Security 4688: LogAnalyzer.Standalone.exe", Day0, AppTraceKind.WindowsRun)], NewPath, true, 150);
        Assert.Contains(notes, n => n.StartsWith("urmă Prefetch mai nouă decât alte urme Windows — poate fi recreată de Windows sau ștearsă"));
    }

    [Fact]
    public void The_residual_risk_section_describes_probe_B()
    {
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null && root is null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "docs", "dfir", "EDITIONS_AND_POSTURE.md"))) root = d.FullName;
        var doc = File.ReadAllText(Path.Combine(root!, "docs", "dfir", "EDITIONS_AND_POSTURE.md"));
        Assert.Contains("Probe B", doc);
        Assert.Contains("existed/ran earlier in another location", doc);
        Assert.Contains("newer Prefetch trace", doc);
        Assert.Contains("same installed path", doc);
        Assert.Contains("PR #289 (merged)", doc);
    }
}
