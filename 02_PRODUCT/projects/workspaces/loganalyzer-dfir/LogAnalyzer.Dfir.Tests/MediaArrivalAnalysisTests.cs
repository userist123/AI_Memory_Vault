using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Row E6: pure analysis of a medium listing (copied-onto-medium observation, arrival timeline) and one live listing of a temp folder standing in for a volume. Synthetic data only.</summary>
public class MediaArrivalAnalysisTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static MediaEntryFact F(string path, double createdDays, double modifiedDays, bool dir = false) =>
        new() { RelativePath = path, IsDirectory = dir, CreatedUtc = T.AddDays(createdDays), LastWriteUtc = T.AddDays(modifiedDays) };

    [Fact]
    public void Modified_before_created_is_reported_as_a_copy_and_not_as_suspicious()
    {
        var l = new MediaListingFact { Letter = "E", Entries = [F("a.docx", 0, -30), F("b.txt", 0, 0), F("c.txt", 0, 0.00001), F("Dir", 0, -9, dir: true)] };
        var r = MediaArrivalAnalysis.Analyze(l);
        var c = Assert.Single(r.Copied);
        Assert.Equal("a.docx", c.Path);
        Assert.Contains("pare copiat pe mediu la 2026-10-05 09:00", c.Text);
        Assert.Contains("dezarhivare", c.Text);
        Assert.Equal(3, r.Files);
        Assert.DoesNotContain("suspect", r.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nu este un semn de rău", r.Text);
    }

    [Fact]
    public void Arrival_timeline_groups_files_by_creation_day_oldest_first()
    {
        var l = new MediaListingFact { Letter = "E", Entries = [F("x1", 2, 2), F("x2", 0.1, 0.1), F("x3", 0, 0)] };
        var r = MediaArrivalAnalysis.Analyze(l);
        Assert.Equal(2, r.Timeline.Count);
        Assert.Equal(2, r.Timeline[0].Files);
        Assert.Equal(new DateOnly(2026, 10, 7), r.Timeline[1].Day);
        Assert.Contains("în 2 zile distincte", r.Text);
    }

    [Theory]
    [InlineData("FAT32", true)]
    [InlineData("exFAT", true)]
    [InlineData("FAT", true)]
    [InlineData("NTFS", false)]
    [InlineData("", false)]
    public void Fat_family_media_get_a_local_time_zone_caveat_and_others_do_not(string fs, bool expected)
    {
        var l = new MediaListingFact { Letter = "E", Entries = [F("x1", 0, 0)] };
        var text = MediaArrivalAnalysis.Analyze(l, fs).Text;
        Assert.Equal(expected, text.Contains("ora locală a calculatorului care a scris fișierul"));
        Assert.Contains("ora ultimului acces poate reflecta chiar inspecția", text);
    }

    [Fact]
    public void Truncation_and_access_errors_are_stated_and_the_empty_listing_does_not_throw()
    {
        var r = MediaArrivalAnalysis.Analyze(new MediaListingFact { Letter = "E", Truncated = true, AccessErrors = 2, LinksNotFollowed = 1 });
        Assert.Contains("trunchiată", r.Text);
        Assert.Contains("2 elemente nu au putut fi citite", r.Text);
        Assert.Contains("1 legături nu au fost urmărite", r.Text);
    }

    [Fact]
    public void The_usn_note_appears_for_ntfs_only()
    {
        Assert.Contains("fsutil usn readjournal E: csv", MediaArrivalReport.UsnNote("NTFS", "E"));
        Assert.Equal("", MediaArrivalReport.UsnNote("FAT32", "E"));
    }

    [Fact]
    public void Live_listing_of_a_temp_folder_stands_in_for_a_volume_and_respects_the_entry_cap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la_e6_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(dir, $"f{i}.txt"), "x");
            var copy = Path.Combine(dir, "old.txt");
            File.WriteAllText(copy, "y");
            File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddYears(-1));
            using (new FileStream(Path.Combine(dir, "f0.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))   // a file held open must still be listed
            {
                var l = RemovableMediaInspector.List(dir, "STICK");
                Assert.Equal(6, l.Entries.Count);
                Assert.All(l.Entries, e => Assert.NotNull(e.LastAccessUtc));
                var r = MediaArrivalAnalysis.Analyze(l);
                Assert.Equal("old.txt", Assert.Single(r.Copied).Path);
            }
            var capped = RemovableMediaInspector.List(dir, "STICK", maxEntries: 3);
            Assert.True(capped.Truncated);
            Assert.Equal(3, capped.Entries.Count);
        }
        finally { Directory.Delete(dir, true); }
    }
}
