using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Lab 2026-11-09 (Insula): the device monitor wrote media_live.jsonl into the station's LIVE case folder before the case existed, and from then on every
/// report and officer export of that station failed with "Case folder is not empty". Only the listed pre-case files are kept; anything else is still refused.
/// </summary>
public sealed class CasePreCaseFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_precase_" + Guid.NewGuid().ToString("N"));
    private static readonly string[] Allowed = ["media_live.jsonl"];

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); foreach (var d in Directory.EnumerateDirectories(Path.GetTempPath(), Path.GetFileName(_dir) + ".precase-*")) Directory.Delete(d, true); }

    private static CaseInfo Info() => new()
    {
        CaseId = "LIVE-TEST", Name = "test", Host = "LAB",
        Scope = new CaseScope
        {
            Purpose = "test", PeriodFromUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), PeriodToUtc = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            SystemsInScope = ["LAB"], Approver = "a", LegalBasis = LegalBasis.Incident, Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
        },
    };

    [Fact]
    public void A_device_journal_written_before_the_case_is_kept_and_recorded_and_the_case_is_created()
    {
        Directory.CreateDirectory(_dir);
        var journal = Path.Combine(_dir, "media_live.jsonl");
        File.WriteAllText(journal, "{\"x\":1}\n");
        var sha = Hashing.Sha256File(journal);
        Assert.Throws<IOException>(() => CaseWorkspace.Create(_dir, Info()));   // the plain rule is unchanged
        var ws = CaseWorkspace.CreateKeepingPreCaseFiles(_dir, Info(), Allowed);
        Assert.True(File.Exists(Path.Combine(_dir, "case.json")));
        Assert.Equal(sha, Hashing.Sha256File(journal));
        Assert.Contains("case.precase_file_kept", File.ReadAllText(ws.AppAuditLogPath));
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(_dir)!, Path.GetFileName(_dir) + ".precase-*"));
    }

    [Theory]
    [InlineData("other.txt", false)]
    [InlineData("sub", true)]
    public void Any_other_content_is_refused_and_left_as_it_was(string name, bool folder)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "media_live.jsonl"), "a");
        if (folder) Directory.CreateDirectory(Path.Combine(_dir, name)); else File.WriteAllText(Path.Combine(_dir, name), "b");
        Assert.Throws<IOException>(() => CaseWorkspace.CreateKeepingPreCaseFiles(_dir, Info(), Allowed));
        Assert.False(File.Exists(Path.Combine(_dir, "case.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "media_live.jsonl")));
    }

    [Fact]
    public void An_empty_or_missing_folder_is_created_as_before()
    {
        CaseWorkspace.CreateKeepingPreCaseFiles(_dir, Info(), Allowed);
        Assert.True(File.Exists(Path.Combine(_dir, "case.json")));
    }
}
