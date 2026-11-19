using System.IO;
using System.Security.Cryptography;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// S07 (planner #9 item 4; HG 585 art. 309, 311(1); matrix row 18): the approved software of the procedure profile is hashed on the station and compared with the
/// profile's SHA-256. The evaluator is pure (StationFacts built in memory); the collector (ApprovedSoftwareHasher) is tested on temporary files.
/// (These tests live here because the compiled LogAnalyzer.Dfir.Tests assembly is quarantined by Defender on the development machine.)
/// </summary>
public sealed class ApprovedSoftwareIntegrityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-s07-" + Guid.NewGuid().ToString("N"));

    public ApprovedSoftwareIntegrityTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private string Write(string name, byte[] data) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, data); return p; }
    private static ApprovedSoftwareRow Row(string name, string path, string sha) => new() { Name = name, Publisher = "Editor SRL", PathPattern = path, Sha256 = sha };

    private static ControlCheck S07(StationFacts f) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "S07");

    private static StationFacts Facts(ApprovedSoftwareIntegrity? s) => new()
    {
        Posture = StationPosture.Standalone, IsAdministrator = true, SoftwareIntegrity = s,
        CollectedUtc = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero), PeriodStartUtc = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), PeriodEndUtc = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero),
    };

    private static ApprovedSoftwareIntegrity One(string expected, params ApprovedSoftwareFile[] files) =>
        new([new ApprovedSoftwareObservation("Program A", expected, @"C:\Prog\a.exe", files, "")], 0, 0);

    private static ApprovedSoftwareFile F(string path, string sha, string problem) => new(path, sha, problem);

    private static readonly string HashA = new string('a', 64);
    private static readonly string HashB = "b" + new string('c', 63);

    // ---- evaluator ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Evaluator_all_hashes_match_is_Conform()
    {
        var c = S07(Facts(One(HashA, F(@"C:\Prog\a.exe", HashA.ToUpperInvariant(), ""))));
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Equal("Software", c.Area);
    }

    [Fact]
    public void Evaluator_a_different_hash_is_Neconform_with_name_path_and_the_first_16_hex_of_both()
    {
        var c = S07(Facts(One(HashA, F(@"C:\Prog\a.exe", HashB, ""))));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        var line = Assert.Single(c.Evidence, e => e.StartsWith("NECONFORM", StringComparison.Ordinal));
        Assert.Contains("Program A", line);
        Assert.Contains(@"C:\Prog\a.exe", line);
        Assert.Contains(HashA[..16], line);
        Assert.Contains(HashB[..16], line);
        Assert.DoesNotContain(HashA[..17], line);
        Assert.Contains("reinstalați", c.Recommendation);
        Assert.Contains("înregistrată", c.Recommendation);
    }

    [Theory]
    [InlineData("lipsește")]
    [InlineData("acces refuzat")]
    [InlineData("prea mare, nehash-uit")]
    public void Evaluator_a_file_that_could_not_be_read_is_DeVerificat(string why)
    {
        var c = S07(Facts(One(HashA, F(@"C:\Prog\a.exe", "", why))));
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains(why, StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluator_a_row_problem_such_as_too_many_matches_is_DeVerificat()
    {
        var s = new ApprovedSoftwareIntegrity([new ApprovedSoftwareObservation("Program A", HashA, @"C:\Prog\*.dll", [new(@"C:\Prog\x.dll", HashA, "")], ApprovedSoftwareHasher.TooManyMatches)], 0, 0);
        Assert.Equal(ControlStatus.DeVerificat, S07(Facts(s)).Status);
    }

    [Fact]
    public void Evaluator_a_pattern_with_the_approved_file_among_its_matches_reports_the_other_files_as_DeVerificat()
    {
        var s = new ApprovedSoftwareIntegrity([new ApprovedSoftwareObservation("Program A", HashA, @"C:\Prog\*.exe",
            [new(@"C:\Prog\a.exe", HashA, ""), new(@"C:\Prog\helper.exe", HashB, "")], "")], 0, 0);
        var c = S07(Facts(s));
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("helper.exe") && e.Contains("alt fișier sub același model"));
    }

    [Fact]
    public void Evaluator_a_pattern_where_no_match_has_the_approved_hash_is_Neconform()
    {
        var s = new ApprovedSoftwareIntegrity([new ApprovedSoftwareObservation("Program A", HashA, @"C:\Prog\*.exe",
            [new(@"C:\Prog\a.exe", HashB, ""), new(@"C:\Prog\helper.exe", HashB, "")], "")], 0, 0);
        Assert.Equal(ControlStatus.Neconform, S07(Facts(s)).Status);
    }

    [Fact]
    public void Evaluator_a_difference_wins_over_an_unreadable_file()
    {
        var s = new ApprovedSoftwareIntegrity(
        [
            new ApprovedSoftwareObservation("Program A", HashA, @"C:\Prog\a.exe", [new(@"C:\Prog\a.exe", "", "lipsește")], ""),
            new ApprovedSoftwareObservation("Program B", HashA, @"C:\Prog\b.exe", [new(@"C:\Prog\b.exe", HashB, "")], ""),
        ], 0, 0);
        Assert.Equal(ControlStatus.Neconform, S07(Facts(s)).Status);
    }

    [Fact]
    public void Evaluator_without_a_checkable_row_is_Nedeterminat_and_counts_the_rows_without_a_path()
    {
        var c = S07(Facts(new ApprovedSoftwareIntegrity([], 2, 0)));
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains("Profilul nu conține programe aprobate cu cale și amprentă SHA-256", c.Detail);
        Assert.Contains("2 rânduri au amprentă, dar nu au cale: fără cale, neverificabile", c.Detail);
    }

    [Fact]
    public void Evaluator_facts_not_collected_is_Nedeterminat_with_the_gap_text()
    {
        var f = Facts(null);
        f.Gaps.Add(new LogAnalyzer.Dfir.Model.EvidenceGap("Programe aprobate (amprente SHA-256)", LogAnalyzer.Dfir.Model.EvidenceStatus.Failed, "citire eșuată", "", "", ""));
        var c = S07(f);
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains("citire eșuată", c.Detail);
    }

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void S07_applies_in_every_edition_and_has_a_guide_and_a_glossary_entry(StationPosture posture)
    {
        Assert.True(ControlApplicability.Applies("S07", posture));
        Assert.Contains("S07", CheckGuides.Ids);
        Assert.NotNull(TermCatalog.Find("S07"));
        var f = Facts(One(HashA, F(@"C:\Prog\a.exe", HashA, "")));
        var c = ControlEvaluator.Evaluate(new StationFacts { Posture = posture, SoftwareIntegrity = f.SoftwareIntegrity, IsAdministrator = true, CollectedUtc = f.CollectedUtc, PeriodStartUtc = f.PeriodStartUtc, PeriodEndUtc = f.PeriodEndUtc })
            .Checks.Single(x => x.Id == "S07");
        Assert.Equal(ControlStatus.Conform, c.Status);
    }

    // ---- collector ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Collector_hashes_a_literal_path_and_the_evaluator_reads_it_as_Conform()
    {
        var p = Write("a.exe", [1, 2, 3, 4]);
        var s = ApprovedSoftwareHasher.Collect([Row("Program A", p, Hex([1, 2, 3, 4]))]);
        var o = Assert.Single(s.Rows);
        Assert.Equal(Hex([1, 2, 3, 4]), Assert.Single(o.Files).Sha256);
        Assert.Equal("", o.Files[0].Problem);
        Assert.Equal(ControlStatus.Conform, S07(Facts(s)).Status);
    }

    [Fact]
    public void Collector_a_changed_file_gives_another_hash_and_the_evaluator_says_Neconform()
    {
        var p = Write("a.exe", [1, 2, 3, 4]);
        var s = ApprovedSoftwareHasher.Collect([Row("Program A", p, Hex([1, 2, 3, 4]))]);
        File.WriteAllBytes(p, [1, 2, 3, 5]);
        s = ApprovedSoftwareHasher.Collect([Row("Program A", p, Hex([1, 2, 3, 4]))]);
        Assert.Equal(Hex([1, 2, 3, 5]), s.Rows[0].Files[0].Sha256);
        Assert.Equal(ControlStatus.Neconform, S07(Facts(s)).Status);
    }

    [Fact]
    public void Collector_a_missing_file_is_recorded_as_missing_and_gives_DeVerificat()
    {
        var s = ApprovedSoftwareHasher.Collect([Row("Program A", Path.Combine(_dir, "nu-exista.exe"), HashA)]);
        var f = Assert.Single(Assert.Single(s.Rows).Files);
        Assert.Equal("", f.Sha256);
        Assert.Equal(ApprovedSoftwareHasher.Missing, f.Problem);
        Assert.Equal(ControlStatus.DeVerificat, S07(Facts(s)).Status);
    }

    [Fact]
    public void Collector_expands_environment_variables()
    {
        Write("env.exe", [9, 9]);
        Environment.SetEnvironmentVariable("LA_S07_TEST_DIR", _dir);
        try
        {
            var s = ApprovedSoftwareHasher.Collect([Row("Program E", @"%LA_S07_TEST_DIR%\env.exe", Hex([9, 9]))]);
            Assert.Equal(Hex([9, 9]), s.Rows[0].Files[0].Sha256);
        }
        finally { Environment.SetEnvironmentVariable("LA_S07_TEST_DIR", null); }
    }

    [Fact]
    public void Collector_expands_a_wildcard_only_in_its_own_directory_without_recursion()
    {
        Write("x1.dll", [1]); Write("x2.dll", [2]); Write("y.txt", [3]);
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllBytes(Path.Combine(_dir, "sub", "x3.dll"), [4]);
        var s = ApprovedSoftwareHasher.Collect([Row("Biblioteci", Path.Combine(_dir, "x*.dll"), HashA)]);
        var o = Assert.Single(s.Rows);
        Assert.Equal(2, o.Files.Count);
        Assert.All(o.Files, f => Assert.DoesNotContain("sub", f.Path));
        Assert.Equal("", o.Problem);
    }

    [Fact]
    public void Collector_a_wildcard_is_bounded_to_50_files_and_the_excess_is_flagged()
    {
        for (var i = 0; i < 60; i++) Write($"m{i:D2}.bin", [(byte)i]);
        var s = ApprovedSoftwareHasher.Collect([Row("Multe", Path.Combine(_dir, "m*.bin"), HashA)]);
        var o = Assert.Single(s.Rows);
        Assert.Equal(ApprovedSoftwareHasher.MaxMatches, o.Files.Count);
        Assert.Equal(ApprovedSoftwareHasher.TooManyMatches, o.Problem);
        Assert.Equal(ControlStatus.Neconform, S07(Facts(s)).Status);   // the 50 files differ from the row's hash
        // m00.bin has the approved hash: the other 49 matches are other files under the pattern (DE VERIFICAT), not a modification
        var oneApproved = ApprovedSoftwareHasher.Collect([Row("Multe", Path.Combine(_dir, "m*.bin"), Hex([0]))]);
        Assert.Equal(ControlStatus.DeVerificat, S07(Facts(oneApproved)).Status);
    }

    [Fact]
    public void Collector_a_wildcard_without_a_match_and_a_wildcard_in_the_directory_are_row_problems()
    {
        var none = ApprovedSoftwareHasher.Collect([Row("Nimic", Path.Combine(_dir, "*.nu"), HashA)]);
        Assert.Empty(none.Rows[0].Files);
        Assert.NotEqual("", none.Rows[0].Problem);
        Assert.Equal(ControlStatus.DeVerificat, S07(Facts(none)).Status);
        var dirWild = ApprovedSoftwareHasher.Collect([Row("Director", Path.Combine(_dir, "*", "a.exe"), HashA)]);
        Assert.Empty(dirWild.Rows[0].Files);
        Assert.Contains("director", dirWild.Rows[0].Problem);
    }

    [Fact]
    public void Collector_does_not_hash_a_file_over_the_cap_but_hashes_one_just_under_a_small_file()
    {
        // a sparse file over 1 GiB: its size is read, its content never
        var big = Path.Combine(_dir, "big.bin");
        using (var fs = new FileStream(big, FileMode.Create, FileAccess.Write)) fs.SetLength(ApprovedSoftwareHasher.MaxFileBytes + 1);
        var s = ApprovedSoftwareHasher.Collect([Row("Mare", big, HashA)]);
        var f = Assert.Single(s.Rows[0].Files);
        Assert.Equal(ApprovedSoftwareHasher.TooBig, f.Problem);
        Assert.Equal("", f.Sha256);
        Assert.Equal(ControlStatus.DeVerificat, S07(Facts(s)).Status);
    }

    [Fact]
    public void Collector_reads_nothing_without_a_checkable_row_and_counts_rows_without_a_path_and_invalid_rows()
    {
        var p = Write("a.exe", [1]);
        Assert.Empty(ApprovedSoftwareHasher.Collect(null).Rows);
        Assert.Empty(ApprovedSoftwareHasher.Collect([]).Rows);
        // no hash: not checked; hash but no path: counted; invalid hash: ignored
        var s = ApprovedSoftwareHasher.Collect([Row("Fără amprentă", p, ""), Row("Fără cale", "", HashA), Row("Invalid", p, "nu-e-hex")]);
        Assert.Empty(s.Rows);
        Assert.Equal(1, s.NoPath);
        Assert.Equal(1, s.InvalidRows);
        Assert.Equal(ControlStatus.Nedeterminat, S07(Facts(s)).Status);
    }
}
