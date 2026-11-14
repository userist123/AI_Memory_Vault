using System.Text.Json;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>R7b: the per-run acquisition record ("fișa colectării") and its verifier.</summary>
public sealed class AcquisitionRunTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_acqrun_" + Guid.NewGuid().ToString("N"));

    public AcquisitionRunTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private int _n;

    private CaseWorkspace NewCase() => InvestigationPipeline.NewCase(Path.Combine(_dir, "cases" + ++_n), "acq", TestScopes.Valid());

    private static EvidenceItem Store(CaseWorkspace ws, string name, string content, string collector = "col")
    {
        var path = Path.Combine(ws.RawDir("acq"), name);
        File.WriteAllText(path, content);
        return ws.RegisterStored(path, @"C:\orig\" + name, "acq", "txt", TemporalType.Historical, collector, "1");
    }

    /// <summary>Writes a file into the records folder; <paramref name="register"/> also enters it in the custody chain as an output (as a genuine run would).</summary>
    private static string Plant(CaseWorkspace ws, string fileName, string content, bool register)
    {
        var dir = Path.Combine(ws.Root, "Logs", "acquisition_runs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        if (register) ws.RecordOutput("Logs/acquisition_runs/" + fileName, "planted", "0");
        return path;
    }

    private static string RecordPath(CaseWorkspace ws, string runId) => Path.Combine(ws.Root, "Logs", "acquisition_runs", runId + ".json");

    private static string Finish(CaseWorkspace ws, params EvidenceItem[] items)
    {
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        rec.Add("Col", "1", "tool", "2", EvidenceStatus.Success, [], ["cmd"], items, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        rec.Complete(OperationState.Completed, "");
        return rec.RunId;
    }

    /// <summary>Edits a record and registers the edited file again, as a record that was genuinely registered in that (inconsistent) form: the deeper checks are then reached, not RecordModified.</summary>
    private static void RewriteRegistered(CaseWorkspace ws, string runId, Action<JsonNode> edit)
    {
        Rewrite(RecordPath(ws, runId), edit);
        ws.RecordOutput($"Logs/acquisition_runs/{runId}.json", "planted", "0");
    }

    private static void Rewrite(string path, Action<JsonNode> edit)
    {
        var n = JsonNode.Parse(File.ReadAllText(path))!;
        edit(n);
        File.WriteAllText(path, n.ToJsonString());
    }

    [Fact]
    public void Review2_a_read_only_case_gets_no_record_file_and_no_false_unknown_record_later()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        rec.Add("Col", "1", "tool", "2", EvidenceStatus.Success, [], [], [a], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        ws.MarkReadOnly("caz sigilat");

        Assert.Throws<InvalidOperationException>(() => rec.Complete(OperationState.Completed, ""));
        Assert.False(File.Exists(RecordPath(ws, rec.RunId)));
        Assert.False(Directory.Exists(Path.Combine(ws.Root, "Logs", "acquisition_runs")) &&
                     Directory.EnumerateFileSystemEntries(Path.Combine(ws.Root, "Logs", "acquisition_runs")).Any());
        Assert.Empty(AcquisitionRunVerifier.Verify(ws).Where(f => f.IsProblem));
    }

    [Fact]
    public void Complete_writes_a_record_registered_as_output_and_audited_once()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var id = Finish(ws, a);
        Assert.Matches("^[0-9a-f]{32}$", id);
        var rec = JsonSerializer.Deserialize<AcquisitionRunRecord>(File.ReadAllText(RecordPath(ws, id)), Json.Options)!;
        Assert.Equal(OperationState.Completed, rec.State);
        Assert.Equal(ws.Info.CaseId, rec.CaseId);
        Assert.Equal("1.0", rec.SchemaVersion);
        var item = Assert.Single(Assert.Single(rec.Collectors).Items);
        Assert.Equal(a.EvidenceId, item.EvidenceId);
        Assert.All(AcquisitionRunVerifier.Verify(ws), f => Assert.Equal(AcquisitionFindingKind.ItemIntact, f.Kind));
        var re = ws.Recheck();
        Assert.Contains(re.Outputs, o => o.Path.EndsWith(id + ".json") && o.Status == OutputCheckStatus.Intact && o.DependsOn.Contains(a.EvidenceId));
        Assert.Equal(RecheckVerdict.Valid, re.Verdict);
        Assert.Empty(re.Notes.Where(n => n.Contains("fișa colectării")));
    }

    [Fact]
    public void T1_record_edited_after_writing_is_reported_modified_by_recheck()
    {
        var ws = NewCase();
        var id = Finish(ws, Store(ws, "a.txt", "alpha"));
        Rewrite(RecordPath(ws, id), n => n["operator"] = "someone-else");
        var r = ws.Recheck();
        Assert.Equal(RecheckVerdict.Modified, r.Verdict);
        Assert.Contains(r.Outputs, o => o.Path.EndsWith(id + ".json") && o.Status == OutputCheckStatus.Modified);
    }

    [Fact]
    public void T2_replaced_evidence_file_is_reported_modified()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        Finish(ws, a);
        var full = ws.FullPath(a.StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        File.WriteAllText(full, "tampered");
        var f = Assert.Single(AcquisitionRunVerifier.Verify(ws), x => x.Kind != AcquisitionFindingKind.ItemIntact);
        Assert.Equal(AcquisitionFindingKind.ItemModified, f.Kind);
        Assert.Contains(ws.Recheck().Notes, n => n.Contains("modificat după colectare"));
    }

    [Fact]
    public void T3_deleted_evidence_file_is_reported_missing()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        Finish(ws, a);
        var full = ws.FullPath(a.StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        File.Delete(full);
        Assert.Contains(AcquisitionRunVerifier.Verify(ws), x => x.Kind == AcquisitionFindingKind.ItemMissing && x.EvidenceId == a.EvidenceId);
    }

    [Fact]
    public void T4_deleted_record_is_reported_missing_by_recheck()
    {
        var ws = NewCase();
        var id = Finish(ws, Store(ws, "a.txt", "alpha"));
        File.Delete(RecordPath(ws, id));
        var r = ws.Recheck();
        Assert.Equal(RecheckVerdict.Missing, r.Verdict);
        Assert.Contains(r.Outputs, o => o.Path.EndsWith(id + ".json") && o.Status == OutputCheckStatus.Missing);
    }

    [Fact]
    public void T5_planted_records_are_reported_without_exception_and_nothing_outside_the_folder_is_touched()
    {
        var ws = NewCase();
        var good = Finish(ws, Store(ws, "a.txt", "alpha"));
        var dir = Path.Combine(ws.Root, "Logs", "acquisition_runs");
        var outside = Path.Combine(ws.Root, "Logs", "outside_marker.txt");
        File.WriteAllText(outside, "x");
        var before = File.GetLastWriteTimeUtc(outside);

        Plant(ws, new string('a', 32) + ".json", new JsonObject { ["schema_version"] = "1.0", ["runId"] = "../x", ["caseId"] = ws.Info.CaseId }.ToJsonString(), true);
        Plant(ws, new string('b', 32) + ".json", "not json at all {{{", true);
        Plant(ws, "evil name.json", "{}", false);
        Plant(ws, new string('c', 32) + ".json", new JsonObject { ["runId"] = new string('c', 32), ["caseId"] = "OTHER-CASE" }.ToJsonString(), true);
        Plant(ws, new string('d', 32) + ".json", File.ReadAllText(RecordPath(ws, good)), true);   // valid content, name != RunId

        var findings = AcquisitionRunVerifier.Verify(ws);
        Assert.Equal(5, findings.Count(f => f.Kind == AcquisitionFindingKind.RecordInvalid));
        Assert.Contains(findings, f => f.RunId == good && f.Kind == AcquisitionFindingKind.ItemIntact);
        Assert.Equal(before, File.GetLastWriteTimeUtc(outside));
        Assert.Equal(6, Directory.GetFiles(dir, "*.json").Length);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        var r = ws.Recheck();   // never throws on planted records
        Assert.Contains(r.Notes, n => n.Contains("fișa colectării"));
    }

    [Fact]
    public void Findings_1_null_collector_null_item_missing_sha_and_empty_path_are_invalid_and_never_crash_verify_recheck_or_open()
    {
        var ws = NewCase();
        Finish(ws, Store(ws, "a.txt", "alpha"));
        string Coll(JsonNode? collector) => new JsonObject { ["caseId"] = ws.Info.CaseId, ["collectors"] = new JsonArray(collector) }.ToJsonString();
        JsonNode Item(string? sha, string? path = "Raw/x", string? evId = "EV-1") =>
            new JsonObject { ["name"] = "x", ["manifestSha256"] = "", ["items"] = new JsonArray(new JsonObject { ["evidenceId"] = evId, ["storedPath"] = path, ["sha256"] = sha }) };
        var bodies = new[]
        {
            Coll(null),
            new JsonObject { ["caseId"] = ws.Info.CaseId, ["collectors"] = new JsonArray(new JsonObject { ["name"] = "x", ["manifestSha256"] = "", ["items"] = new JsonArray((JsonNode?)null) }) }.ToJsonString(),
            Coll(Item(null)),
            Coll(Item("zz")),
            Coll(Item(new string('a', 64), "")),
            Coll(Item(new string('a', 64), "Raw/x", "")),
            new JsonObject { ["caseId"] = ws.Info.CaseId, ["collectors"] = null }.ToJsonString(),
            new JsonObject { ["caseId"] = ws.Info.CaseId, ["collectors"] = new JsonArray(new JsonObject { ["name"] = "x", ["items"] = null }) }.ToJsonString(),
        };
        var ids = new List<string>();
        for (int k = 0; k < bodies.Length; k++)
        {
            var id = new string((char)('0' + k), 32);
            var node = JsonNode.Parse(bodies[k])!; node["runId"] = id;
            Plant(ws, id + ".json", node.ToJsonString(), true);
            ids.Add(id);
        }
        var findings = AcquisitionRunVerifier.Verify(ws);
        foreach (var id in ids) Assert.Contains(findings, f => f.RunId == id && f.Kind == AcquisitionFindingKind.RecordInvalid);
        var r = ws.Recheck();
        Assert.NotEqual(RecheckVerdict.Valid, r.Verdict);
        var reopened = CaseWorkspace.Open(ws.Root);
        Assert.NotNull(reopened.LastRecheck);
    }

    [Fact]
    public void Findings_2_an_unregistered_extra_record_is_reported_and_raises_the_verdict()
    {
        var ws = NewCase();
        var id = Finish(ws, Store(ws, "a.txt", "alpha"));
        Assert.Equal(RecheckVerdict.Valid, ws.Recheck().Verdict);
        var node = JsonNode.Parse(File.ReadAllText(RecordPath(ws, id)))!;
        var fake = new string('7', 32);
        node["runId"] = fake; node["operator"] = "fabricated-op";
        Plant(ws, fake + ".json", node.ToJsonString(), false);

        Assert.Contains(AcquisitionRunVerifier.Verify(ws), f => f.RunId == fake && f.Kind == AcquisitionFindingKind.Unregistered);
        var r = ws.Recheck();
        Assert.Equal(RecheckVerdict.Modified, r.Verdict);
        Assert.True(r.AcquisitionProblemsCount >= 1);
        Assert.Contains("fișele colectării", r.Summary);
        Assert.Contains(r.Notes, n => n.Contains("necunoscută lanțului de custodie"));
    }

    [Fact]
    public void Findings_2_a_registered_record_with_a_manifest_mismatch_raises_the_verdict_to_modified()
    {
        var ws = NewCase();
        var id = Finish(ws, Store(ws, "a.txt", "alpha"));
        var node = JsonNode.Parse(File.ReadAllText(RecordPath(ws, id)))!;
        var other = new string('8', 32);
        node["runId"] = other; node["collectors"]![0]!["manifestSha256"] = new string('1', 64);
        Plant(ws, other + ".json", node.ToJsonString(), true);   // registered, so the output itself is Intact
        var r = ws.Recheck();
        Assert.Equal(RecheckVerdict.Modified, r.Verdict);
        Assert.Equal(0, r.OutputsModifiedCount);
        Assert.Contains(AcquisitionRunVerifier.Verify(ws), f => f.Kind == AcquisitionFindingKind.ManifestMismatch);
    }

    [Fact]
    public void Findings_3_a_failing_Complete_is_audited_and_shown_as_a_gap_and_never_throws()
    {
        var ws = NewCase();
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        Plant(ws, rec.RunId + ".json", "occupied", false);   // File.Move(overwrite:false) must fail
        var r = new InvestigationResult { Case = ws };
        InvestigationPipeline.CompleteRunRecord(rec, ws, r, OperationState.Cancelled, "x");
        Assert.Contains(r.Gaps, g => g.Impact.Contains("Fișa colectării nu a putut fi scrisă"));
        Assert.Contains(File.ReadAllLines(ws.AppAuditLogPath), l => l.Contains("acquisition.run_record_failed") && l.Contains(rec.RunId));
        Assert.Equal("occupied", File.ReadAllText(RecordPath(ws, rec.RunId)));
    }

    [Fact]
    public void Findings_4_an_interrupted_collector_is_recorded_with_the_evidence_it_had_already_stored()
    {
        var ws = NewCase();
        Store(ws, "old.txt", "before", "Interrupted");           // registered before the collector started
        Thread.Sleep(30);
        var start = DateTimeOffset.UtcNow;
        var mine = Store(ws, "mine.txt", "partial", "Interrupted");
        Store(ws, "other.txt", "elsewhere", "OtherCollector");
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        var manifest = rec.AddInterrupted("Interrupted", "1", EvidenceStatus.Partial, "colectare întreruptă (anulată)", start, DateTimeOffset.UtcNow);
        rec.Complete(OperationState.Cancelled, "anulat");
        var r = JsonSerializer.Deserialize<AcquisitionRunRecord>(File.ReadAllText(RecordPath(ws, rec.RunId)), Json.Options)!;
        var c = Assert.Single(r.Collectors);
        Assert.Equal(mine.EvidenceId, Assert.Single(c.Items).EvidenceId);
        Assert.Equal(manifest, c.ManifestSha256);
        Assert.NotEmpty(manifest);
        Assert.Equal(EvidenceStatus.Partial, c.Status);
    }

    [Fact]
    public void Findings_5_verify_uses_hashes_the_caller_already_computed_and_honours_cancellation()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        Finish(ws, a);
        var full = ws.FullPath(a.StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        File.WriteAllText(full, "tampered");
        // The map says what the caller measured; the file on disk is not read again for that path.
        var claimsIntact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [full] = a.Sha256 };
        Assert.DoesNotContain(AcquisitionRunVerifier.Verify(ws, default, claimsIntact), f => f.Kind == AcquisitionFindingKind.ItemModified);
        var claimsOther = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [full] = new string('0', 64) };
        Assert.Contains(AcquisitionRunVerifier.Verify(ws, default, claimsOther), f => f.Kind == AcquisitionFindingKind.ItemModified);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AcquisitionRunVerifier.Verify(ws, cts.Token));
    }

    [Fact]
    public void Findings_6_manifest_cannot_be_forged_by_a_path_that_looks_like_a_second_item()
    {
        var s1 = new string('1', 64); var s2 = new string('2', 64);
        var two = AcquisitionRunRecord.ComputeManifestSha256([new AcquisitionItem("EV-1", "o", "a", 1, s1), new AcquisitionItem("EV-2", "o", "b", 2, s2)]);
        var one = AcquisitionRunRecord.ComputeManifestSha256([new AcquisitionItem("EV-1", "o", "a\n" + s2 + " 2 b", 1, s1)]);
        Assert.NotEqual(two, one);
        Assert.Matches("^[0-9a-f]{64}$", two);
    }

    [Fact]
    public void Findings_7_a_junction_as_the_records_folder_is_refused_for_reading_and_writing()
    {
        var ws = NewCase();
        var outside = Path.Combine(_dir, "outside");
        Directory.CreateDirectory(outside);
        var id = new string('6', 32);
        File.WriteAllText(Path.Combine(outside, id + ".json"), new JsonObject { ["runId"] = id, ["caseId"] = "OUTSIDE-READ" }.ToJsonString());
        Directory.CreateDirectory(Path.Combine(ws.Root, "Logs"));
        var link = Path.Combine(ws.Root, "Logs", "acquisition_runs");
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd(); p.WaitForExit();
        Assert.True(Directory.Exists(link) && AcquisitionRunVerifier.IsReparsePoint(link), "junction could not be created in this environment");

        var findings = AcquisitionRunVerifier.Verify(ws);
        var f = Assert.Single(findings);
        Assert.Equal(AcquisitionFindingKind.RecordInvalid, f.Kind);
        Assert.Contains("legătură", f.Detail);

        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        Assert.Throws<IOException>(() => rec.Complete(OperationState.Completed, ""));
        Assert.False(File.Exists(Path.Combine(outside, rec.RunId + ".json")));
        Assert.Single(Directory.GetFiles(outside));
        Directory.Delete(link);   // removes the junction only
    }

    [Fact]
    public void T6_cancellation_writes_a_cancelled_record_with_the_collectors_done_so_far()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        rec.Add("Done", "1", "", "", EvidenceStatus.Success, [], [], [a], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        rec.Complete(OperationState.Cancelled, "anulat");
        var r = JsonSerializer.Deserialize<AcquisitionRunRecord>(File.ReadAllText(RecordPath(ws, rec.RunId)), Json.Options)!;
        Assert.Equal(OperationState.Cancelled, r.State);
        Assert.Equal("Done", Assert.Single(r.Collectors).Name);
    }

    [Fact]
    public void T6_pipeline_cancellation_still_propagates_and_leaves_a_cancelled_record()
    {
        var ws = NewCase();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: true, ct: cts.Token));
        var file = Assert.Single(Directory.GetFiles(Path.Combine(ws.Root, "Logs", "acquisition_runs"), "*.json"));
        var r = JsonSerializer.Deserialize<AcquisitionRunRecord>(File.ReadAllText(file), Json.Options)!;
        Assert.Equal(OperationState.Cancelled, r.State);
        Assert.Equal(Path.GetFileNameWithoutExtension(file), r.RunId);
    }

    [Fact]
    public void T7_manifest_is_deterministic_order_independent_and_sensitive()
    {
        var x = new AcquisitionItem("EV-1", "o1", @"Raw\a\x.txt", 5, "AA");
        var y = new AcquisitionItem("EV-2", "o2", @"Raw\a\y.txt", 7, "BB");
        var m = AcquisitionRunRecord.ComputeManifestSha256([x, y]);
        Assert.Matches("^[0-9a-f]{64}$", m);
        Assert.Equal(m, AcquisitionRunRecord.ComputeManifestSha256([y, x]));
        Assert.Equal(m, AcquisitionRunRecord.ComputeManifestSha256([x, y]));
        Assert.NotEqual(m, AcquisitionRunRecord.ComputeManifestSha256([x with { Sha256 = "AB" }, y]));
        Assert.NotEqual(m, AcquisitionRunRecord.ComputeManifestSha256([x with { Size = 6 }, y]));
        Assert.NotEqual(m, AcquisitionRunRecord.ComputeManifestSha256([x with { StoredPath = @"Raw\a\z.txt" }, y]));
        Assert.Equal("", AcquisitionRunRecord.ComputeManifestSha256([]));
    }

    [Fact]
    public void T7_add_returns_the_manifest_and_empty_for_no_items()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        var withItems = rec.Add("C1", "1", "", "", EvidenceStatus.Success, [], [], [a], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var none = rec.Add("C2", "1", "", "", EvidenceStatus.Failed, ["err"], [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.NotEmpty(withItems);
        Assert.Equal("", none);
        Assert.Equal(AcquisitionRunRecord.ComputeManifestSha256([new AcquisitionItem(a.EvidenceId, a.OriginalPath, a.StoredPath, a.Size, a.Sha256)]), withItems);
    }

    [Fact]
    public void T8_item_unknown_to_the_store_or_with_a_different_hash_is_reported()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var id = Finish(ws, a);
        RewriteRegistered(ws, id, n =>
        {
            var it = n["collectors"]![0]!["items"]![0]!;
            it["evidenceId"] = "EV-999999";
        });
        Assert.Contains(AcquisitionRunVerifier.Verify(ws), f => f.Kind == AcquisitionFindingKind.EvidenceNotInStore);

        var ws2 = NewCase();
        var b = Store(ws2, "b.txt", "beta");
        var id2 = Finish(ws2, b);
        var fake = new string('0', 64);
        RewriteRegistered(ws2, id2, n =>
        {
            var c = n["collectors"]![0]!;
            c["items"]![0]!["sha256"] = fake;
            c["manifestSha256"] = AcquisitionRunRecord.ComputeManifestSha256([new AcquisitionItem(b.EvidenceId, b.OriginalPath, b.StoredPath, b.Size, fake)]);
        });
        var f2 = AcquisitionRunVerifier.Verify(ws2);
        Assert.Contains(f2, f => f.Kind == AcquisitionFindingKind.EvidenceStoreMismatch);
        Assert.Contains(f2, f => f.Kind == AcquisitionFindingKind.ItemModified);
    }

    [Fact]
    public void Manifest_edited_without_items_is_reported_and_outside_case_path_is_reported()
    {
        var ws = NewCase();
        var id = Finish(ws, Store(ws, "a.txt", "alpha"));
        RewriteRegistered(ws, id, n => n["collectors"]![0]!["manifestSha256"] = new string('1', 64));
        Assert.Contains(AcquisitionRunVerifier.Verify(ws), f => f.Kind == AcquisitionFindingKind.ManifestMismatch);

        var ws2 = NewCase();
        var id2 = Finish(ws2, Store(ws2, "a.txt", "alpha"));
        RewriteRegistered(ws2, id2, n => n["collectors"]![0]!["items"]![0]!["storedPath"] = @"..\..\secret.txt");
        Assert.Contains(AcquisitionRunVerifier.Verify(ws2), f => f.Kind == AcquisitionFindingKind.ItemOutsideCase);
    }

    [Fact]
    public void T9_complete_twice_writes_one_file_and_one_audit_entry()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var rec = AcquisitionRunRecorder.Begin(ws, "Quick", false);
        rec.Add("C", "1", "", "", EvidenceStatus.Success, [], [], [a], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        rec.Complete(OperationState.Completed, "");
        rec.Complete(OperationState.Failed, "late");
        Assert.Single(Directory.GetFiles(Path.Combine(ws.Root, "Logs", "acquisition_runs")));
        Assert.Single(File.ReadAllLines(ws.AppAuditLogPath), l => l.Contains("acquisition.run_recorded"));
        Assert.Contains("\"COMPLETED\"", File.ReadAllText(RecordPath(ws, rec.RunId)));
    }
}
