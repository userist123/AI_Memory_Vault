using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP3b on the production path: what InvestigationPipeline writes is registered, indexed, anchored and re-checked.</summary>
public sealed class PipelineIntegrityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ladfir_wp3b_pipe_{Guid.NewGuid():N}");

    public PipelineIntegrityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private (CaseWorkspace ws, InvestigationResult r) Run()
    {
        var sample = Path.Combine(_dir, "sample.bin");
        File.WriteAllText(sample, "MZ not really a program");
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "wp3b", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [sample]);
        return (ws, new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false));
    }

    private static List<JsonElement> OutputEntries(CaseWorkspace ws) =>
        File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .Where(e => e.GetProperty("action").GetString() == "output.written").ToList();

    [Fact]
    public void Every_file_the_pipeline_writes_is_registered_in_custody_with_its_current_sha256()
    {
        var (ws, _) = Run();
        var registered = OutputEntries(ws).GroupBy(e => e.GetProperty("to").GetString()!).ToDictionary(g => g.Key, g => g.Last());
        string[] expected = ["Analysis/timeline.csv", "Analysis/findings.json", "Analysis/parsing.json", "Analysis/parsers.json", "Analysis/graph.json",
                             "Analysis/detections.json", "Analysis/rules.json", "Analysis/anti_forensics.json", "Analysis/dependencies.json", "Analysis/run_state.json",
                             "Analysis/schema_manifest.json", "Exports/vault_proposals.jsonl", "Exports/vault_refused.json", "Exports/export_manifest.json"];
        foreach (var rel in expected)
        {
            Assert.True(registered.ContainsKey(rel), $"{rel} is not registered as output.written");
            Assert.Equal(Hashing.Sha256File(ws.FullPath(rel)), registered[rel].GetProperty("sha256").GetString());
        }
        // Every file of Analysis/ and Exports/ written by the run is covered (the re-check's own result files are the documented exception).
        var skip = new HashSet<string> { "Analysis/integrity_recheck.json", "Analysis/invalidations.json" };
        foreach (var f in Directory.EnumerateFiles(ws.FullPath("Analysis")).Concat(Directory.EnumerateFiles(ws.FullPath("Exports"))))
        {
            var rel = Path.GetRelativePath(ws.Root, f).Replace('\\', '/');
            if (!skip.Contains(rel)) Assert.True(registered.ContainsKey(rel), $"{rel} was written but is not in custody");
        }
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Custody.Status);
    }

    [Fact]
    public void Run_writes_the_dependency_index_and_anchors_the_chain_heads_in_both_manifests()
    {
        var (ws, _) = Run();
        var dep = DependencyIndex.Read(ws.FullPath("Analysis/dependencies.json"))!;
        Assert.Equal(SchemaVersions.Dependencies, dep.SchemaVersion);
        Assert.NotEmpty(dep.VaultProposals);   // at least the evidence proposal
        Assert.All(dep.VaultProposals.Values.Where(v => v.Count > 0), v => Assert.All(v, id => Assert.StartsWith("EV-", id)));

        var export = JsonDocument.Parse(File.ReadAllText(ws.FullPath("Exports/export_manifest.json"))).RootElement;
        Assert.Equal(64, export.GetProperty("anchor").GetProperty("custodyHead").GetString()!.Length);
        Assert.Equal(64, export.GetProperty("anchor").GetProperty("auditHead").GetString()!.Length);
        var anchorFromExport = new ChainAnchor(export.GetProperty("anchor").GetProperty("custodySeq").GetInt64(), export.GetProperty("anchor").GetProperty("custodyHead").GetString()!,
                                               export.GetProperty("anchor").GetProperty("auditSeq").GetInt64(), export.GetProperty("anchor").GetProperty("auditHead").GetString()!);
        Assert.True(ws.CheckAnchor(anchorFromExport).Ok);

        var schema = SchemaManifest.Read(ws.FullPath("Analysis"));
        Assert.NotNull(schema.Anchor);
        Assert.True(ws.CheckAnchor(schema.Anchor!).Ok);
    }

    [Fact]
    public void Reopening_a_clean_run_is_valid_and_a_later_edit_of_an_output_or_of_evidence_is_caught()
    {
        var (ws, _) = Run();
        Assert.Equal(RecheckVerdict.Valid, CaseWorkspace.Open(ws.Root).LastRecheck!.Verdict);

        File.AppendAllText(ws.FullPath("Analysis/graph.json"), " ");
        var r1 = CaseWorkspace.Open(ws.Root).LastRecheck!;
        Assert.Equal(RecheckVerdict.Modified, r1.Verdict);
        Assert.Contains(r1.Outputs, o => o.Path == "Analysis/graph.json" && o.Status == OutputCheckStatus.Modified);

        var ev = ws.LoadEvidence().Single();
        File.SetAttributes(ws.FullPath(ev.StoredPath), FileAttributes.Normal);
        File.AppendAllText(ws.FullPath(ev.StoredPath), "x");
        var reopened = CaseWorkspace.Open(ws.Root);
        Assert.Equal(1, reopened.LastRecheck!.ModifiedCount);
        Assert.Contains(reopened.LoadInvalidations().Items, i => i.Kind == "vault_proposal" && i.EvidenceIds.Contains(ev.EvidenceId));
    }

    [Fact]
    public void A_new_export_run_on_a_case_with_modified_evidence_refuses_the_invalidated_proposals()
    {
        var (ws, _) = Run();
        var ev = ws.LoadEvidence().Single();
        File.SetAttributes(ws.FullPath(ev.StoredPath), FileAttributes.Normal);
        File.AppendAllText(ws.FullPath(ev.StoredPath), "x");
        var reopened = CaseWorkspace.Open(ws.Root);           // re-check writes invalidations.json
        new InvestigationPipeline().Run(reopened, CollectionProfile.Quick, collect: false);
        var refused = File.ReadAllText(reopened.FullPath("Exports/vault_refused.json"));
        Assert.Contains("Invalidated", refused);
        Assert.Contains(ev.EvidenceId, refused);
        Assert.DoesNotContain($":Evidence:{ev.EvidenceId}", File.ReadAllText(reopened.FullPath("Exports/vault_proposals.jsonl")));
    }
}
