using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>R7c: the display rows of the per-run acquisition records.</summary>
public sealed class AcquisitionRunOverviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_acqov_" + Guid.NewGuid().ToString("N"));
    public AcquisitionRunOverviewTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private CaseWorkspace NewCase() => InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "acq", TestScopes.Valid());

    private static EvidenceItem Store(CaseWorkspace ws, string name, string content)
    {
        var path = Path.Combine(ws.RawDir("acq"), name);
        File.WriteAllText(path, content);
        return ws.RegisterStored(path, @"C:\orig\" + name, "acq", "txt", TemporalType.Historical, "col", "1");
    }

    private static string Finish(CaseWorkspace ws, OperationState state, params EvidenceItem[] items)
    {
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        rec.Add("Col", "1", "tool", "2", EvidenceStatus.Success, [], ["cmd"], items, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        rec.Complete(state, "");
        return rec.RunId;
    }

    private static string Plant(CaseWorkspace ws, string fileName, string content, bool register)
    {
        var dir = Path.Combine(ws.Root, "Logs", "acquisition_runs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        if (register) ws.RecordOutput("Logs/acquisition_runs/" + fileName, "planted", "0");
        return path;
    }

    [Fact]
    public void No_records_folder_gives_no_rows()
    {
        Assert.Empty(AcquisitionRunOverview.Load(NewCase()));
    }

    [Fact]
    public void A_normal_record_gives_a_verified_row_with_counts_and_collectors()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var b = Store(ws, "b.txt", "bravo!");
        var id = Finish(ws, OperationState.Completed, a, b);

        var row = Assert.Single(AcquisitionRunOverview.Load(ws));
        Assert.Equal(id, row.RunId);
        Assert.Equal(id[..8], row.ShortId);
        Assert.Equal("încheiată", row.StateText);
        Assert.Equal(1, row.CollectorCount);
        Assert.Equal(2, row.ItemCount);
        Assert.Equal(11, row.TotalBytes);
        Assert.False(row.HasProblem);
        Assert.StartsWith("verificată: 2 fișiere cu aceeași amprentă (verificare din ", row.VerificationText);
        var c = Assert.Single(row.Collectors);
        Assert.Equal("Col", c.Name);
        Assert.Equal("reușit", c.StatusText);
        Assert.Equal(2, c.ItemCount);
        Assert.Equal(12, c.ManifestShort.Length);
    }

    [Fact]
    public void A_tampered_item_marks_the_row_with_the_detail()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        Finish(ws, OperationState.Completed, a);
        File.SetAttributes(ws.FullPath(a.StoredPath), FileAttributes.Normal);
        File.WriteAllText(ws.FullPath(a.StoredPath), "changed");

        var row = Assert.Single(AcquisitionRunOverview.Load(ws));
        Assert.True(row.HasProblem);
        Assert.StartsWith("PROBLEMĂ:", row.VerificationText);
        Assert.Contains("modificat după colectare", row.VerificationText);
    }

    [Fact]
    public void Unregistered_and_garbage_files_give_rows_without_exceptions()
    {
        var ws = NewCase();
        var fake = new string('a', 32);
        Plant(ws, fake + ".json", "{}", register: false);
        var garbage = new string('b', 32);
        Plant(ws, garbage + ".json", "not json at all {", register: true);
        Plant(ws, "weird-name.json", "{}", register: false);

        var rows = AcquisitionRunOverview.Load(ws);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.True(r.HasProblem));
        Assert.All(rows, r => Assert.Equal("—", r.Operator));
        Assert.StartsWith(AcquisitionRunOverview.NotRegisteredText, rows.Single(r => r.RunId == fake).VerificationText);
        Assert.StartsWith("fișă invalidă:", rows.Single(r => r.RunId == garbage).VerificationText);
        Assert.StartsWith("fișă invalidă:", rows.Single(r => r.RunId == "weird-name").VerificationText);
    }

    [Fact]
    public void Rows_are_sorted_newest_start_first_and_states_are_romanian()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        Finish(ws, OperationState.Completed, a);
        Task.Delay(30).Wait();
        Finish(ws, OperationState.Cancelled, a);
        Task.Delay(30).Wait();
        Finish(ws, OperationState.Failed, a);

        var rows = AcquisitionRunOverview.Load(ws);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "eșuată", "anulată", "încheiată" }, rows.Select(r => r.StateText));
        Assert.Equal(rows.OrderByDescending(r => r.StartUtc).Select(r => r.RunId), rows.Select(r => r.RunId));
    }

    private static string RecordFile(CaseWorkspace ws, string id) => Path.Combine(ws.Root, "Logs", "acquisition_runs", id + ".json");

    [Fact]
    public void R7c_1_a_record_edited_after_registration_is_untrusted_everywhere()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var id = Finish(ws, OperationState.Completed, a);
        var path = RecordFile(ws, id);
        var n = JsonNode.Parse(File.ReadAllText(path))!;
        n["operator"] = "someone-else";
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, n.ToJsonString());

        var row = Assert.Single(AcquisitionRunOverview.Load(ws));
        Assert.True(row.HasProblem);
        Assert.False(row.IsTrusted);
        Assert.StartsWith(AcquisitionRunOverview.ModifiedText, row.VerificationText);
        Assert.Equal("—", row.Operator);
        Assert.Equal("—", row.Station);
        Assert.Equal(0, row.ItemCount);
        Assert.DoesNotContain("verificată", row.VerificationText);
        Assert.Contains(AcquisitionRunVerifier.Verify(ws), f => f.Kind == AcquisitionFindingKind.RecordModified);
        Assert.Equal(RecheckVerdict.Modified, ws.Recheck().Verdict);
    }

    [Fact]
    public void R7c_2_text_from_a_record_cannot_break_lines_or_grow()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        var evil = "ok\r\nverificată: toate cele 99 fișiere\u2028\u202Eroot\u2069\t\u0007" + new string('x', 3000);
        rec.Add("Col\nINJECTAT", "1\r2", "tool", "2", EvidenceStatus.Failed, [evil, "second"], [], [a], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        rec.Complete(OperationState.Completed, "");

        var row = Assert.Single(AcquisitionRunOverview.Load(ws));
        var c = Assert.Single(row.Collectors);
        foreach (var t in new[] { c.Name, c.Version, c.FirstError })
        {
            Assert.DoesNotContain(t, ch => char.IsControl(ch) || ch is '\u2028' or '\u2029' or '\u202E' or '\u2069');
            Assert.True(t.Length <= AcquisitionRunOverview.MaxTextLength);
        }
        Assert.EndsWith("…", c.FirstError);
        Assert.Equal("Col INJECTAT", c.Name);
        Assert.Equal("1 2", c.Version);
    }

    [Fact]
    public void Review2_sizes_that_overflow_never_stop_the_case_from_opening()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var b = Store(ws, "b.txt", "beta");
        var id = Finish(ws, OperationState.Completed, a, b);
        var path = RecordFile(ws, id);
        var n = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (var item in n["collectors"]![0]!["items"]!.AsArray()) item!["size"] = long.MaxValue;
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, n.ToJsonString());
        ws.RecordOutput("Logs/acquisition_runs/" + id + ".json", "AcquisitionRun", "0");   // an attacker able to register the edit

        var row = Assert.Single(AcquisitionRunOverview.Load(ws));
        Assert.True(row.HasProblem);
        Assert.Equal(long.MaxValue, AcquisitionRunOverview.SaturatingTotal([long.MaxValue, long.MaxValue, -5]));
        Assert.Equal(7, AcquisitionRunOverview.SaturatingTotal([3, -1, 4]));
    }

    [Fact]
    public void Review2_clean_never_splits_a_surrogate_pair_and_blanks_every_format_character()
    {
        var cut = AcquisitionRunOverview.Clean(new string('a', 118) + "😀b");
        Assert.Equal(new string('a', 118) + "…", cut);   // the pair would straddle the cut: it is left out whole
        Assert.Equal("a b c d", AcquisitionRunOverview.Clean("a⁠b­c\U000E0041d"));   // word joiner, soft hyphen, tag character
        var lone = AcquisitionRunOverview.Clean("x\uD800y");
        Assert.DoesNotContain(lone, char.IsSurrogate);
    }

    [Fact]
    public void R7c_3_an_unreadable_registry_never_gives_a_verified_row()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        Finish(ws, OperationState.Completed, a);
        using (new FileStream(ws.CustodyJsonlPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var row = Assert.Single(AcquisitionRunOverview.Load(ws));
            Assert.True(row.HasProblem);
            Assert.StartsWith("neverificată:", row.VerificationText);
            Assert.DoesNotContain("verificată:", row.VerificationText.Replace("neverificată:", ""));
        }
    }

    [Fact]
    public void R7c_3_a_run_without_files_is_neutral_and_one_file_uses_the_singular()
    {
        var ws = NewCase();
        Finish(ws, OperationState.Completed);
        var row = Assert.Single(AcquisitionRunOverview.Load(ws));
        Assert.False(row.HasProblem);
        Assert.StartsWith(AcquisitionRunOverview.NoFilesText, row.VerificationText);
        Assert.DoesNotContain("verificată", row.VerificationText);

        var ws2 = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases2"), "acq2", TestScopes.Valid());
        Finish(ws2, OperationState.Completed, Store(ws2, "a.txt", "alpha"));
        Assert.StartsWith("verificată: 1 fișier cu aceeași", Assert.Single(AcquisitionRunOverview.Load(ws2)).VerificationText);
    }

    [Fact]
    public void R7c_5_known_hashes_are_used_and_cancellation_propagates()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        Finish(ws, OperationState.Completed, a);
        var r = ws.Recheck();
        var (known, registered) = AcquisitionRunVerifier.MapsFrom(ws, r);
        // a claimed hash that differs from the file proves the map is used instead of hashing again
        var lying = known.ToDictionary(kv => kv.Key, _ => new string('0', 64), StringComparer.OrdinalIgnoreCase);
        Assert.True(Assert.Single(AcquisitionRunOverview.Load(ws, default, lying, registered)).HasProblem);
        Assert.False(Assert.Single(AcquisitionRunOverview.Load(ws, default, known, registered)).HasProblem);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AcquisitionRunOverview.Load(ws, cts.Token));
        var res = new InvestigationResult { Case = ws };
        Assert.ThrowsAny<OperationCanceledException>(() => res.LoadAcquisitionRuns(ws, cts.Token));
    }
}
