using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Verification;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP4 gates: the Vault export respects the verdict (R15.2) and the pipeline runs the verifier before it (R9.7).</summary>
public sealed class VerificationGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ladfir_wp4_gate_{Guid.NewGuid():N}");
    public VerificationGateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static readonly string Sha = new('A', 64);

    private static (List<VaultProposal> Proposals, List<VaultRefusal> Refused) Export(Dictionary<string, FindingVerification>? verdicts)
    {
        var ev = new List<EvidenceItem> { new() { EvidenceId = "EV-1", CaseId = "CASE-G", Source = "System", SourceType = "evtx", StoredPath = @"Raw\System.evtx", Sha256 = Sha, OriginalName = "System.evtx" } };
        Finding F(string id) => new()
        {
            FindingId = id, RuleId = "R-" + id, Title = "t " + id, Description = "d", Classification = Classification.Direct, Confidence = Confidence.Medium,
            SupportingEvidence = [new EvidenceRef("EV-1", "EventRecordID=5", "x")],
        };
        var findings = new[] { "F-1", "F-2", "F-3", "F-4", "F-5", "F-6", "F-7", "F-8" }.Select(F).ToList();
        return VaultExport.FromCase("CASE-G", ev, findings, [], [], "test", verdicts);
    }

    private static FindingVerification V(StandardState s, string reason = "motiv") => new(s, reason, VerificationReport.VerifierId, DateTimeOffset.UnixEpoch);

    private static Dictionary<string, FindingVerification> Verdicts() => new()
    {
        ["F-1"] = V(StandardState.Verified), ["F-2"] = V(StandardState.Supported), ["F-3"] = V(StandardState.Unproven), ["F-4"] = V(StandardState.Unknown),
        ["F-5"] = V(StandardState.Contradicted, "execuția precede crearea"), ["F-6"] = V(StandardState.Rejected, "nicio probă validă"), ["F-7"] = V(StandardState.NotAssessed),
        // F-8 has no verdict at all
    };

    private static JsonElement Meta(VaultProposal p) =>
        JsonDocument.Parse(p.Body[(p.Body.IndexOf("```json\n", StringComparison.Ordinal) + 8)..p.Body.LastIndexOf("```", StringComparison.Ordinal)]).RootElement;

    private static VaultProposal Of(IEnumerable<VaultProposal> ps, string id) => ps.Single(p => p.Provenance["source_ref"].EndsWith(":" + id));

    [Fact]
    public void Contradicted_and_rejected_findings_are_refused_with_the_verdict_and_reason_not_exported()
    {
        var (proposals, refused) = Export(Verdicts());
        Assert.DoesNotContain(proposals, p => p.Provenance["source_ref"].EndsWith(":F-5") || p.Provenance["source_ref"].EndsWith(":F-6"));
        var r5 = Assert.Single(refused, r => r.Id == "F-5"); var r6 = Assert.Single(refused, r => r.Id == "F-6");
        Assert.Contains("CONTRADICTED", r5.Reason); Assert.Contains("execuția precede crearea", r5.Reason);
        Assert.Contains("REJECTED", r6.Reason); Assert.Contains("nicio probă validă", r6.Reason);
        Assert.Equal("Finding", r5.Kind);
    }

    [Fact]
    public void Unproven_unknown_and_not_assessed_are_exported_marked_neverificat_verified_and_supported_are_not()
    {
        var (proposals, _) = Export(Verdicts());
        foreach (var id in new[] { "F-3", "F-4", "F-7" })
        {
            var p = Of(proposals, id);
            Assert.Contains("NEVERIFICAT", p.Body);
            Assert.True(Meta(p).GetProperty("unverified").GetBoolean());
        }
        foreach (var id in new[] { "F-1", "F-2" })
        {
            var p = Of(proposals, id);
            Assert.DoesNotContain("NEVERIFICAT", p.Body);
            Assert.False(Meta(p).GetProperty("unverified").GetBoolean());
        }
    }

    [Fact]
    public void The_verdict_is_written_into_each_exported_finding_proposal()
    {
        var (proposals, _) = Export(Verdicts());
        var expected = new Dictionary<string, string> { ["F-1"] = "VERIFIED", ["F-2"] = "SUPPORTED", ["F-3"] = "UNPROVEN", ["F-4"] = "UNKNOWN", ["F-7"] = "NOT_ASSESSED" };
        foreach (var (id, state) in expected)
        {
            var v = Meta(Of(proposals, id)).GetProperty("verification");
            Assert.Equal(state, v.GetProperty("state").GetString());
            Assert.Equal(VerificationReport.VerifierId, v.GetProperty("verifier").GetString());
        }
        Assert.Contains("motiv", Meta(Of(proposals, "F-1")).GetProperty("verification").GetProperty("reason").GetString());
    }

    [Fact]
    public void Without_verdicts_the_export_is_unchanged_and_a_finding_missing_from_the_verdicts_stays_not_assessed()
    {
        var (plain, refusedPlain) = Export(null);
        Assert.Equal(9, plain.Count); Assert.Empty(refusedPlain);   // 8 findings + the evidence item
        Assert.Equal(8, plain.Count(p => p.Provenance["source_ref"].Contains(":F-")));
        Assert.All(plain.Where(p => p.Provenance["source_ref"].Contains(":F-")), p => Assert.Equal("NOT_ASSESSED", Meta(p).GetProperty("verification").GetProperty("state").GetString()));
        Assert.All(plain, p => Assert.DoesNotContain("NEVERIFICAT", p.Body));   // no verifier ran: nothing is claimed either way

        var (withV, _) = Export(Verdicts());
        var f8 = Of(withV, "F-8");
        Assert.Equal("NOT_ASSESSED", Meta(f8).GetProperty("verification").GetProperty("state").GetString());
        Assert.Contains("NEVERIFICAT", f8.Body);   // a verifier ran and did not cover this finding
    }

    // ---------------------------------------------------------------- pipeline

    private const string DisabledTask = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Date>2026-09-19T17:58:12.5+03:00</Date><Author>NANAGENT\u</Author><URI>\orchestratormaintain</URI></RegistrationInfo>
          <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
          <Principals><Principal id="Author"><UserId>S-1-5-21-1-2-3-1001</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
          <Settings><Hidden>true</Hidden><Enabled>false</Enabled></Settings>
          <Actions Context="Author"><Exec><Command>C:\Users\u\AppData\Local\Conexant\pro_cmd_core.exe</Command><Arguments>-run silent</Arguments></Exec></Actions>
        </Task>
        """;

    private (CaseWorkspace ws, InvestigationResult r) Run()
    {
        var task = Path.Combine(_dir, "orchestratormaintain");
        File.WriteAllText(task, DisabledTask, System.Text.Encoding.Unicode);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "wp4", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [task]);
        return (ws, new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false));
    }

    private static List<JsonElement> Outputs(CaseWorkspace ws) =>
        File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement.Clone()).Where(e => e.GetProperty("action").GetString() == "output.written").ToList();

    [Fact]
    public void The_pipeline_writes_verification_json_and_registers_it_in_custody()
    {
        var (ws, r) = Run();
        var path = ws.FullPath("Analysis/verification.json");
        Assert.True(File.Exists(path));
        Assert.NotNull(r.Verification);
        Assert.NotEmpty(r.Verification!.Findings);
        Assert.Equal(r.Findings.Count, r.Verification.Findings.Count);
        var entry = Outputs(ws).Last(e => e.GetProperty("to").GetString() == "Analysis/verification.json");
        Assert.Equal(Hashing.Sha256File(path), entry.GetProperty("sha256").GetString());
        Assert.Equal(SchemaVersions.Verification, SchemaManifest.Read(Path.Combine(ws.Root, "Analysis")).VersionOf("verification.json"));
        Assert.Contains(File.ReadAllLines(ws.AppAuditLogPath), l => l.Contains("\tverification.run\t"));
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Custody.Status);
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Audit.Status);
    }

    [Fact]
    public void The_pipeline_runs_the_verifier_after_graph_detections_and_anti_forensics_and_before_the_vault_export()
    {
        var (ws, _) = Run();
        var order = Outputs(ws).Select(e => e.GetProperty("to").GetString()!).ToList();
        int At(string rel) => order.LastIndexOf(rel);
        Assert.True(At("Analysis/verification.json") > At("Analysis/graph.json"));
        Assert.True(At("Analysis/verification.json") > At("Analysis/detections.json"));
        Assert.True(At("Analysis/verification.json") > At("Analysis/anti_forensics.json"));
        Assert.True(At("Analysis/verification.json") < At("Exports/vault_proposals.jsonl"));
        Assert.True(At("Analysis/verification.json") < At("Exports/vault_refused.json"));
    }

    [Fact]
    public void The_pipeline_never_rewrites_findings_json_and_the_verdict_is_in_the_exported_proposals()
    {
        var (ws, r) = Run();
        var findingsText = File.ReadAllText(ws.FullPath("Analysis/findings.json"));
        using (var doc = JsonDocument.Parse(findingsText))
            Assert.All(doc.RootElement.GetProperty("Findings").EnumerateArray(), f => Assert.Equal("NOT_ASSESSED", f.GetProperty("Verification").GetProperty("State").GetString()));
        var registered = Outputs(ws).Last(e => e.GetProperty("to").GetString() == "Analysis/findings.json").GetProperty("sha256").GetString();
        Assert.Equal(registered, Hashing.Sha256File(ws.FullPath("Analysis/findings.json")));   // untouched since it was written

        // In memory (UI, PDF) the findings carry the verdict.
        Assert.All(r.Findings, f => Assert.Equal(r.Verification!.Of(f.FindingId)!.Verdict, f.Verification.State));
        Assert.All(r.Findings, f => Assert.Equal(VerificationReport.VerifierId, f.Verification.Verifier));
        Assert.All(r.Findings, f => Assert.NotEqual(StandardState.Verified, f.Status));   // the mapped status is untouched

        var proposals = VaultExport.Read(Path.Combine(ws.Root, "Exports", "vault_proposals.jsonl"));
        foreach (var f in r.Findings)
        {
            var v = r.Verification!.Of(f.FindingId)!.Verdict;
            var p = proposals.SingleOrDefault(x => x.Provenance["source_ref"].EndsWith(":" + f.FindingId));
            if (v is StandardState.Rejected or StandardState.Contradicted) { Assert.Null(p); continue; }
            Assert.NotNull(p);
            Assert.Equal(v.ToSpec(), Meta(p!).GetProperty("verification").GetProperty("state").GetString());
        }
    }

    [Fact]
    public void A_case_can_be_reopened_and_verified_again_after_the_pipeline()
    {
        var (ws, r) = Run();
        var reopened = CaseWorkspace.Open(ws.Root);
        var again = CaseVerifier.Verify(reopened);
        Assert.Equal(r.Verification!.Banner, again.Banner);
    }
}
