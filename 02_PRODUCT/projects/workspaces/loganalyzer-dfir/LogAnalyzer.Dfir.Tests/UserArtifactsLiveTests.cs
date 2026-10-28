using System.Security.Principal;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>A [Fact] that runs only elevated and when LADFIR_LIVE_USER=1: it reads the real station (VSS copies of the user hives, the Recycle Bin).</summary>
public sealed class LiveUserFactAttribute : FactAttribute
{
    public LiveUserFactAttribute()
    {
        using var id = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) Skip = "Necesită un proces rulat ca administrator.";
        else if (Environment.GetEnvironmentVariable("LADFIR_LIVE_USER") != "1") Skip = "Rulare live pe stație: setați LADFIR_LIVE_USER=1.";
    }
}

/// <summary>
/// E7 slice 2 on the real station: the production UserArtifactsCollector (esentutl /vss, real ProfileList, real $Recycle.Bin), then the production
/// pipeline. Only counts and states are written to the output, never paths, names or SIDs; the case lives in a temp folder deleted at the end.
/// </summary>
public sealed class UserArtifactsLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_e7live_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch (IOException) { }
    }

    [LiveUserFact]
    public void The_current_user_hives_are_collected_from_a_shadow_copy_and_parsed()
    {
        Directory.CreateDirectory(_dir);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "e7-live", TestScopes.Valid());
        var o = new UserArtifactsCollector().Collect(new CollectorContext { Case = ws, IsElevated = true }, CancellationToken.None);

        using var id = WindowsIdentity.GetCurrent();
        var sid = id.User!.Value;
        var mine = o.Evidence.Where(e => e.Notes.Contains(sid, StringComparison.OrdinalIgnoreCase)).ToList();
        output.WriteLine($"status={o.Status} evidence={o.Evidence.Count} errors={o.Errors.Count} " +
            $"ntuser={o.Evidence.Count(e => e.SourceType == "ntuser_hive")} usrclass={o.Evidence.Count(e => e.SourceType == "usrclass_hive")} " +
            $"recycle_info={o.Evidence.Count(e => e.SourceType == "recycle_info")} mine={mine.Count}");
        foreach (var err in o.Errors) output.WriteLine("gap kind: " + GapKind(err));

        Assert.DoesNotContain(o.Errors, e => e.Contains("copie de umbră nouă", StringComparison.Ordinal));     // esentutl released its snapshot
        var nt = Assert.Single(mine, e => e.SourceType == "ntuser_hive");                                      // the logged-on user's locked hive
        var uc = Assert.Single(mine, e => e.SourceType == "usrclass_hive");
        foreach (var e in new[] { nt, uc })
        {
            var head = new byte[4];
            using (var fs = File.OpenRead(ws.FullPath(e.StoredPath))) fs.ReadExactly(head);
            Assert.Equal("regf"u8.ToArray(), head);
        }

        var r = new InvestigationPipeline().Run(ws, CollectionProfile.FullForensic, collect: false);
        var ntParse = r.Parsing.Single(p => p.EvidenceId == nt.EvidenceId);
        var ucParse = r.Parsing.Single(p => p.EvidenceId == uc.EvidenceId);
        output.WriteLine($"ntuser parser={ntParse.Parser} status={ntParse.Status}; usrclass parser={ucParse.Parser} status={ucParse.Status}");
        output.WriteLine($"timeline ShellBags={r.Timeline.Count(e => e.Source == "ShellBags")} RecentDocs={r.Timeline.Count(e => e.Source == "RecentDocs")} " +
            $"OfficeMru={r.Timeline.Count(e => e.Source.StartsWith("Office", StringComparison.Ordinal))} RecycleBin={r.Timeline.Count(e => e.Source == "RecycleBin")}");
        // real-data quality of the shell item decoder (counts only): unknown item types, and folders left with an 8.3 name where a long name should exist
        var bags = r.Timeline.Where(e => e.Source == "ShellBags").ToList();
        string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
        int unknown = bags.Count(e => F(e, "ShellItemKind") == "Unknown");
        int truncated = bags.Count(e => F(e, "ItemHex").Length > 0 && F(e, "ShellItemKind") != "Unknown");
        int shortOnly = bags.Count(e => F(e, "ShortName").Contains('~') && string.Equals(Path.GetFileName(e.Path ?? ""), F(e, "ShortName"), StringComparison.OrdinalIgnoreCase));
        int withShort = bags.Count(e => F(e, "ShortName").Contains('~'));
        var kinds = bags.GroupBy(e => F(e, "ShellItemType") + "/" + F(e, "ShellItemKind")).OrderByDescending(g => g.Count()).Take(14);
        output.WriteLine($"shellbags unknown={unknown} truncated={truncated} short83_only={shortOnly} of {withShort} with 8.3; kinds: " + string.Join(", ", kinds.Select(g => $"{g.Key}={g.Count()}")));
        var userGaps = r.Gaps.Where(g => g.Artifact is "ShellBags" or "RecentDocs" or "OfficeMru" or "Office MRU" or "RecycleBin").ToList();
        output.WriteLine($"parse gaps={userGaps.Count}");
        foreach (var g in userGaps.Take(10)) output.WriteLine("parse gap: " + g.Artifact + ": " + GapKind(g.Reason));
        Assert.Equal("UserHiveParser", ntParse.Parser);
        Assert.Equal("ShellBagsParser", ucParse.Parser);
        Assert.NotEqual(EvidenceStatus.Failed, ntParse.Status);
        Assert.NotEqual(EvidenceStatus.Failed, ucParse.Status);
    }

    /// <summary>The first words of a gap, without the path or SID that follow (privacy: the output may be pasted into a report).</summary>
    private static string GapKind(string error)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(error, @"S-1-[\d-]+", "<SID>");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[A-Za-z]:\\[^;,)]*", "<cale>");
        return s.Length <= 160 ? s : s[..160] + "…";
    }
}
