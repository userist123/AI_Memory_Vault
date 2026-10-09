using System.Security.Cryptography;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP3b: outputs in custody, dependency index, re-check on open, exact invalidation, vault gate, head anchor.</summary>
public sealed class IntegrityRecheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_wp3b_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase(string name = "case") =>
        CaseWorkspace.Create(Path.Combine(_root, name), new CaseInfo { CaseId = "CASE-R", Name = name, CreatedAtUtc = DateTimeOffset.UtcNow, Host = "AUDITED", Scope = TestScopes.Valid() });

    private EvidenceItem Add(CaseWorkspace ws, string name, string content = "", string? parent = null)
    {
        var p = Path.Combine(ws.RawDir("s"), name);
        File.WriteAllText(p, content.Length > 0 ? content : "data-" + name);
        return ws.RegisterStored(p, name, "s", "txt", TemporalType.Historical, "unit", "1", parentEvidenceId: parent);
    }

    private static Finding Finding(string id, params string[] evidenceIds) => new()
    {
        FindingId = id, RuleId = "R-1", Title = "t " + id, Description = "d",
        SupportingEvidence = evidenceIds.Select(e => new EvidenceRef(e, "loc", "desc")).ToList(),
    };

    private static void Tamper(CaseWorkspace ws, EvidenceItem e)
    {
        var full = ws.FullPath(e.StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        File.AppendAllText(full, "TAMPERED");
    }

    private static string WriteFile(CaseWorkspace ws, string rel, string content)
    {
        var full = ws.FullPath(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static JsonElement[] CustodyLines(CaseWorkspace ws) =>
        File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToArray();

    // ---- 1. outputs in custody ----

    [Fact]
    public void RecordOutput_writes_one_output_written_custody_entry_with_sha256_producer_and_dependencies()
    {
        var ws = NewCase();
        var ev = Add(ws, "a.txt");
        var full = WriteFile(ws, "Analysis/findings.json", "{\"x\":1}");
        var before = File.ReadAllText(full);

        var rec = ws.RecordOutput("Analysis/findings.json", "InvestigationPipeline", "9.9", [ev.EvidenceId]);

        Assert.Equal(Hashing.Sha256File(full), rec.Sha256);
        Assert.Equal(before, File.ReadAllText(full));   // content untouched
        var e = Assert.Single(CustodyLines(ws), l => l.GetProperty("action").GetString() == "output.written");
        Assert.Equal("Analysis/findings.json", e.GetProperty("to").GetString());
        Assert.Equal(rec.Sha256, e.GetProperty("sha256").GetString());
        Assert.Equal("InvestigationPipeline", e.GetProperty("tool").GetString());
        Assert.Equal("9.9", e.GetProperty("toolVersion").GetString());
        Assert.Contains(ev.EvidenceId, e.GetProperty("transformation").GetString());
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Custody.Status);
    }

    [Fact]
    public void RecordOutput_refuses_missing_file_and_paths_outside_the_case()
    {
        var ws = NewCase();
        Assert.Throws<FileNotFoundException>(() => ws.RecordOutput("Analysis/nope.json", "p", "1"));
        File.WriteAllText(Path.Combine(_root, "outside.txt"), "x");
        Assert.Throws<ArgumentException>(() => ws.RecordOutput("../outside.txt", "p", "1"));
    }

    [Fact]
    public void Writing_a_manifest_registers_it_and_every_listed_file_is_hashed_in_it()
    {
        var ws = NewCase();
        WriteFile(ws, "Exports/a.jsonl", "a"); WriteFile(ws, "Exports/b.json", "b");
        ws.WriteManifest("Exports/export_manifest.json", ["Exports/a.jsonl", "Exports/b.json"], "Test", "1");
        var m = JsonDocument.Parse(File.ReadAllText(ws.FullPath("Exports/export_manifest.json"))).RootElement;
        Assert.Equal(2, m.GetProperty("files").GetArrayLength());
        Assert.Equal(Hashing.Sha256File(ws.FullPath("Exports/a.jsonl")), m.GetProperty("files")[0].GetProperty("sha256").GetString());
        Assert.Contains(CustodyLines(ws), l => l.GetProperty("action").GetString() == "output.written" && l.GetProperty("to").GetString() == "Exports/export_manifest.json");
    }

    // ---- 2. dependency index ----

    [Fact]
    public void Dependency_index_maps_findings_and_proposals_to_evidence_transitively()
    {
        var ws = NewCase();
        var parent = Add(ws, "p.txt"); var child = Add(ws, "c.txt", parent: parent.EvidenceId); var other = Add(ws, "o.txt");
        var f1 = Finding("F-0001", child.EvidenceId);
        var f2 = Finding("F-0002", other.EvidenceId);
        var chain = new Finding { FindingId = "F-0003", RuleId = "INCIDENT-CHAIN", Title = "c", Description = "d", RelatedFindingIds = ["F-0001", "F-0002"], SupportingEvidence = [] };
        var (proposals, _) = VaultExport.FromCase("CASE-R", ws.LoadEvidence(), [f1, f2], [], [], "unit");

        var idx = DependencyIndex.Build("CASE-R", ws.LoadEvidence(), [f1, f2, chain], proposals);

        Assert.Equal(new[] { child.EvidenceId, parent.EvidenceId }.OrderBy(x => x), idx.Findings["F-0001"].OrderBy(x => x));   // via ParentEvidenceId
        Assert.Equal([other.EvidenceId], idx.Findings["F-0002"]);
        Assert.Equal(3, idx.Findings["F-0003"].Count);   // union of its related findings
        var refOfF2 = proposals.Single(p => p.Provenance["source_ref"].EndsWith(":Inference:F-0002", StringComparison.Ordinal) || p.Provenance["source_ref"].EndsWith(":Finding:F-0002", StringComparison.Ordinal));
        Assert.Contains(other.EvidenceId, idx.VaultProposals[refOfF2.Provenance["source_ref"]]);
        Assert.Equal(SchemaVersions.Dependencies, idx.SchemaVersion);
    }

    [Fact]
    public void WriteDependencies_writes_a_versioned_file_and_registers_it_as_an_output()
    {
        var ws = NewCase();
        var ev = Add(ws, "a.txt");
        ws.WriteDependencies([Finding("F-0001", ev.EvidenceId)], []);
        var path = ws.FullPath("Analysis/dependencies.json");
        Assert.Equal(SchemaVersions.Dependencies, SchemaVersions.ReadVersion(path));
        Assert.Equal([ev.EvidenceId], DependencyIndex.Read(path)!.Findings["F-0001"]);
        Assert.Contains(CustodyLines(ws), l => l.GetProperty("action").GetString() == "output.written" && l.GetProperty("to").GetString() == "Analysis/dependencies.json");
    }

    // ---- 3. re-check ----

    [Fact]
    public void Recheck_clean_case_is_valid_and_writes_result_audit_entry_and_anchor()
    {
        var ws = NewCase();
        var ev = Add(ws, "a.txt");
        WriteFile(ws, "Analysis/findings.json", "{}"); ws.RecordOutput("Analysis/findings.json", "p", "1", [ev.EvidenceId]);

        var r = ws.Recheck();

        Assert.Equal(RecheckVerdict.Valid, r.Verdict);
        Assert.All(r.Evidence, c => Assert.Equal(EvidenceCheckStatus.Intact, c.Status));
        Assert.Single(r.Outputs); Assert.Equal(OutputCheckStatus.Intact, r.Outputs[0].Status);
        Assert.Equal(64, r.Anchor.CustodyHead.Length); Assert.Equal(64, r.Anchor.AuditHead.Length);
        Assert.True(File.Exists(ws.FullPath("Analysis/integrity_recheck.json")));
        Assert.True(File.Exists(ws.FullPath("Analysis/invalidations.json")));
        Assert.Contains("\tcase.recheck\t", File.ReadAllText(ws.AppAuditLogPath));
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Audit.Status);
        var json = JsonDocument.Parse(File.ReadAllText(ws.FullPath("Analysis/integrity_recheck.json"))).RootElement;
        Assert.Equal(r.Anchor.CustodyHead, json.GetProperty("anchor").GetProperty("custodyHead").GetString());
    }

    [Fact]
    public void Recheck_modified_evidence_is_reported_and_never_repaired()
    {
        var ws = NewCase();
        var ev = Add(ws, "a.txt"); Add(ws, "b.txt");
        Tamper(ws, ev);
        var tampered = File.ReadAllBytes(ws.FullPath(ev.StoredPath));

        var r = ws.Recheck();

        Assert.Equal(RecheckVerdict.Modified, r.Verdict);
        Assert.Equal(1, r.ModifiedCount);
        Assert.Equal(EvidenceCheckStatus.Modified, r.Evidence.Single(c => c.EvidenceId == ev.EvidenceId).Status);
        Assert.Contains("Modificate", r.Summary);
        Assert.Equal(tampered, File.ReadAllBytes(ws.FullPath(ev.StoredPath)));   // no auto-repair
    }

    [Fact]
    public void Recheck_missing_evidence_is_reported_as_missing()
    {
        var ws = NewCase();
        var ev = Add(ws, "a.txt");
        File.SetAttributes(ws.FullPath(ev.StoredPath), FileAttributes.Normal); File.Delete(ws.FullPath(ev.StoredPath));
        var r = ws.Recheck();
        Assert.Equal(RecheckVerdict.Missing, r.Verdict);
        Assert.Equal(1, r.MissingCount);
        Assert.Equal(EvidenceCheckStatus.Missing, r.Evidence.Single().Status);
        Assert.Contains("Lipsă", r.Summary);
    }

    [Fact]
    public void Recheck_modified_registered_output_is_reported()
    {
        var ws = NewCase();
        var path = WriteFile(ws, "Analysis/graph.json", "{}"); ws.RecordOutput("Analysis/graph.json", "p", "1");
        File.AppendAllText(path, " ");
        var r = ws.Recheck();
        Assert.Equal(OutputCheckStatus.Modified, r.Outputs.Single().Status);
        Assert.Equal(RecheckVerdict.Modified, r.Verdict);
    }

    [Fact]
    public void Recheck_reports_a_broken_chain()
    {
        var ws = NewCase();
        Add(ws, "a.txt"); Add(ws, "b.txt");
        var lines = File.ReadAllLines(ws.CustodyJsonlPath).ToList();
        lines.RemoveAt(0);
        File.WriteAllLines(ws.CustodyJsonlPath, lines);
        var r = ws.Recheck();
        Assert.Equal(RecheckVerdict.ChainBroken, r.Verdict);
        Assert.Equal(ChainStatus.Broken, r.Chains.Custody.Status);
        Assert.Contains("lanț rupt", r.Summary);
    }

    [Fact]
    public void Recheck_of_a_case_created_before_wp3_is_legacy_never_valid()
    {
        var ws = NewCase();
        var ev = Add(ws, "a.txt");
        // Simulate a pre-WP3 case: only the CSV custody and the plain audit log exist, no chains.
        File.Delete(ws.CustodyJsonlPath); File.Delete(ws.AuditChainPath);
        var reopened = CaseWorkspace.Open(ws.Root);
        var r = reopened.LastRecheck!;
        Assert.Equal(RecheckVerdict.Legacy, r.Verdict);
        Assert.NotEqual(RecheckVerdict.Valid, r.Verdict);
        Assert.Contains("lanț neverificabil (caz creat înainte de WP3)", r.Summary);
        Assert.Equal(EvidenceCheckStatus.Intact, r.Evidence.Single(c => c.EvidenceId == ev.EvidenceId).Status);   // evidence is still checked
    }

    [Fact]
    public void Open_runs_the_recheck_and_exposes_the_verdict()
    {
        var ws = NewCase();
        var ev = Add(ws, "a.txt");
        Tamper(ws, ev);
        var reopened = CaseWorkspace.Open(ws.Root);
        Assert.NotNull(reopened.LastRecheck);
        Assert.Equal(RecheckVerdict.Modified, reopened.LastRecheck!.Verdict);
        Assert.Null(CaseWorkspace.Open(ws.Root, recheck: false).LastRecheck);
    }

    [Fact]
    public void Recheck_is_cancellable()
    {
        var ws = NewCase();
        Add(ws, "a.txt");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ws.Recheck(cts.Token));
    }

    // ---- 4. exact invalidation ----

    [Fact]
    public void Modified_evidence_invalidates_exactly_its_dependents_and_leaves_findings_json_untouched()
    {
        var ws = NewCase();
        var e1 = Add(ws, "a.txt"); var e2 = Add(ws, "b.txt");
        var findings = new[] { Finding("F-0001", e1.EvidenceId), Finding("F-0002", e2.EvidenceId), Finding("F-0003", e1.EvidenceId, e2.EvidenceId) };
        var (proposals, _) = VaultExport.FromCase("CASE-R", ws.LoadEvidence(), findings, [], [], "unit");
        var findingsJson = WriteFile(ws, "Analysis/findings.json", SchemaVersions.WithVersion(new { Findings = findings, Gaps = Array.Empty<object>() }, SchemaVersions.Findings));
        var bytesBefore = File.ReadAllBytes(findingsJson);
        ws.WriteDependencies(findings, proposals);
        Tamper(ws, e1);

        var r = ws.Recheck();

        var inv = ws.LoadInvalidations();
        Assert.Equal(["F-0001", "F-0003"], inv.Items.Where(i => i.Kind == "finding").Select(i => i.Id).OrderBy(x => x).ToArray());
        Assert.All(inv.Items, i => { Assert.Equal("INVALIDATED", i.State); Assert.Contains(e1.EvidenceId, i.EvidenceIds); Assert.Contains(e1.EvidenceId, i.Reason); });
        Assert.DoesNotContain(inv.Items, i => i.Id == "F-0002");
        var invalidatedRefs = inv.Items.Where(i => i.Kind == "vault_proposal").Select(i => i.Id).ToHashSet();
        Assert.NotEmpty(invalidatedRefs);
        Assert.DoesNotContain(invalidatedRefs, id => id.EndsWith(":F-0002", StringComparison.Ordinal) || id.Contains(e2.EvidenceId));
        Assert.Equal(inv.Items.Count, r.InvalidatedCount);
        Assert.Equal(bytesBefore, File.ReadAllBytes(findingsJson));   // findings.json is never rewritten
    }

    [Fact]
    public void Missing_evidence_invalidates_dependents_with_the_missing_reason()
    {
        var ws = NewCase();
        var e1 = Add(ws, "a.txt");
        ws.WriteDependencies([Finding("F-0001", e1.EvidenceId)], []);
        File.SetAttributes(ws.FullPath(e1.StoredPath), FileAttributes.Normal); File.Delete(ws.FullPath(e1.StoredPath));
        ws.Recheck();
        var i = Assert.Single(ws.LoadInvalidations().Items);
        Assert.Contains("LIPSĂ", i.Reason);
    }

    [Fact]
    public void Old_case_without_dependencies_json_derives_the_index_from_findings_json()
    {
        var ws = NewCase();
        var e1 = Add(ws, "a.txt"); var e2 = Add(ws, "b.txt");
        WriteFile(ws, "Analysis/findings.json", SchemaVersions.WithVersion(new { Findings = new[] { Finding("F-0001", e1.EvidenceId), Finding("F-0002", e2.EvidenceId) }, Gaps = Array.Empty<object>() }, SchemaVersions.Findings));
        Tamper(ws, e2);
        ws.Recheck();
        Assert.Equal(["F-0002"], ws.LoadInvalidations().Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void A_clean_recheck_clears_stale_invalidations_without_touching_evidence()
    {
        var ws = NewCase();
        var e1 = Add(ws, "a.txt");
        ws.WriteDependencies([Finding("F-0001", e1.EvidenceId)], []);
        Assert.Empty(ws.Recheck().Invalidations);
        Assert.Empty(ws.LoadInvalidations().Items);
    }

    // ---- 4b. vault gate ----

    [Fact]
    public void Vault_release_refuses_invalidated_proposals_with_the_reason_and_releases_the_rest()
    {
        var ws = NewCase();
        var e1 = Add(ws, "a.txt"); var e2 = Add(ws, "b.txt");
        var findings = new[] { Finding("F-0001", e1.EvidenceId), Finding("F-0002", e2.EvidenceId) };
        var (proposals, _) = VaultExport.FromCase("CASE-R", ws.LoadEvidence(), findings, [], [], "unit");
        ws.WriteDependencies(findings, proposals);
        Tamper(ws, e1);
        ws.Recheck();

        var (released, refused) = VaultExport.Release(ws, proposals);

        Assert.NotEmpty(refused);
        Assert.All(refused, x => { Assert.Contains("INVALIDATED", x.Reason); Assert.Contains(e1.EvidenceId, x.Reason); });
        Assert.Equal(proposals.Count, released.Count + refused.Count);
        Assert.All(released, p => Assert.DoesNotContain(refused.Select(x => x.Id), id => id == p.Provenance["source_ref"]));
        Assert.Contains(released, p => p.Provenance["source_ref"].EndsWith("F-0002", StringComparison.Ordinal));
        Assert.DoesNotContain(released, p => p.Provenance["source_ref"].EndsWith("F-0001", StringComparison.Ordinal));
    }

    [Fact]
    public void Vault_release_without_invalidations_releases_everything()
    {
        var ws = NewCase();
        var e1 = Add(ws, "a.txt");
        var (proposals, _) = VaultExport.FromCase("CASE-R", ws.LoadEvidence(), [Finding("F-0001", e1.EvidenceId)], [], [], "unit");
        var (released, refused) = VaultExport.Release(ws, proposals);
        Assert.Empty(refused); Assert.Equal(proposals.Count, released.Count);
    }

    // ---- 5. external head anchor ----

    [Fact]
    public void Manifests_carry_the_current_chain_heads_and_truncation_below_the_anchor_is_detected()
    {
        var ws = NewCase();
        Add(ws, "a.txt"); WriteFile(ws, "Exports/a.jsonl", "a");
        ws.WriteManifest("Exports/export_manifest.json", ["Exports/a.jsonl"], "Test", "1");
        var anchorJson = JsonDocument.Parse(File.ReadAllText(ws.FullPath("Exports/export_manifest.json"))).RootElement.GetProperty("anchor");
        var anchor = new ChainAnchor(anchorJson.GetProperty("custodySeq").GetInt64(), anchorJson.GetProperty("custodyHead").GetString()!,
                                     anchorJson.GetProperty("auditSeq").GetInt64(), anchorJson.GetProperty("auditHead").GetString()!);
        Assert.True(ws.CheckAnchor(anchor).Ok);
        Add(ws, "b.txt");                       // later appends are fine
        Assert.True(ws.CheckAnchor(anchor).Ok);

        var lines = File.ReadAllLines(ws.CustodyJsonlPath).ToList();
        File.WriteAllLines(ws.CustodyJsonlPath, lines.Take(lines.Count - 3));   // truncate the end: the chain itself still verifies
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Custody.Status);
        var check = ws.CheckAnchor(anchor);
        Assert.False(check.Ok);
        Assert.Contains("custodie", check.Reason);
    }

    [Fact]
    public void Schema_manifest_records_the_anchor_additively()
    {
        var ws = NewCase();
        Add(ws, "a.txt");
        Directory.CreateDirectory(ws.FullPath("Analysis"));
        var m = SchemaManifest.ForRun(); m.Anchor = ws.Anchor();
        m.Write(ws.FullPath("Analysis/schema_manifest.json"));
        var back = SchemaManifest.Read(ws.FullPath("Analysis"));
        Assert.Equal(m.Anchor!.CustodyHead, back.Anchor!.CustodyHead);
        Assert.Equal(SchemaVersions.Dependencies, back.VersionOf("dependencies.json"));
    }

    // ---- 3b. streaming hash ----

    [Fact]
    public void Large_file_is_hashed_in_a_stream_without_loading_it_into_memory_and_matches_the_reference_hash()
    {
        Directory.CreateDirectory(_root);
        var big = Path.Combine(_root, "big.bin");
        var chunk = new byte[1 << 20]; new Random(7).NextBytes(chunk);
        using (var fs = File.Create(big)) for (int i = 0; i < 96; i++) fs.Write(chunk);   // 96 MiB
        string reference;
        using (var fs = File.OpenRead(big)) reference = Convert.ToHexString(SHA256.HashData(fs));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var h = Hashing.Sha256File(big, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(reference, h);
        Assert.True(allocated < 24L * 1024 * 1024, $"hashing allocated {allocated} bytes: the file must be streamed");
    }

    [Fact]
    public void Hashing_a_large_file_is_cancellable()
    {
        Directory.CreateDirectory(_root);
        var big = Path.Combine(_root, "big2.bin");
        File.WriteAllBytes(big, new byte[8 << 20]);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Hashing.Sha256File(big, cts.Token));
    }
}
