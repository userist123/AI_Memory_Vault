using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Runs only when the vault's own proposal interface (03_IMPLEMENTATION/packages/interfaces/memory_access.py) and Python are present.</summary>
public sealed class VaultInterfaceFactAttribute : FactAttribute
{
    public VaultInterfaceFactAttribute()
    {
        if (VaultExportTests.Packages is null) Skip = "Vault interfaces/memory_access.py not found above the test binaries — VAULT REFERENCE UNAVAILABLE";
        else if (Differential.Python is null) Skip = "Python not found — VAULT REFERENCE UNAVAILABLE";
    }
}

public sealed class VaultExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_vault_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    /// <summary>The vault's packages folder, found by walking up from the test binaries (this project lives inside the vault repository).</summary>
    public static string? Packages
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "03_IMPLEMENTATION", "packages", "interfaces", "memory_access.py")))
                    return Path.Combine(d.FullName, "03_IMPLEMENTATION", "packages");
            return null;
        }
    }

    private static readonly string Sha = new('A', 64);

    private static (List<VaultProposal>, List<VaultRefusal>) Sample()
    {
        var ev = new List<EvidenceItem>
        {
            new() { EvidenceId = "EV-1", CaseId = "CASE-V", Source = "System", SourceType = "evtx", StoredPath = @"Raw\System.evtx", Sha256 = Sha, OriginalName = "System.evtx" },
            new() { EvidenceId = "EV-2", CaseId = "CASE-V", Source = "x", SourceType = "x", StoredPath = "x", Sha256 = "" },
        };
        Finding F(string id, string rule, Classification c, params EvidenceRef[] refs) => new()
        {
            FindingId = id, RuleId = rule, Title = "t " + id, Description = "d", Classification = c, Confidence = Confidence.Medium, SupportingEvidence = refs.ToList(),
        };
        var findings = new List<Finding>
        {
            F("F-1", "LOG-CLEARED", Classification.Direct, new EvidenceRef("EV-1", "EventRecordID=5", "104")),
            F("F-2", "DOWNLOAD-THEN-EXEC", Classification.Correlated, new EvidenceRef("EV-1", "EventRecordID=6", "x")),
            F("F-3", "INCIDENT-CHAIN", Classification.Correlated, new EvidenceRef("EV-1", "EventRecordID=7", "x")),
            F("F-4", "NOPE", Classification.Direct),
            F("F-5", "FOREIGN", Classification.Direct, new EvidenceRef("EV-9", "L", "x")),
            F("F-6", "NOHASH", Classification.Direct, new EvidenceRef("EV-2", "L", "x")),
        };
        var af = new List<AntiForensicCheck>
        {
            new("AF01", "Jurnal EVTX golit", "T1070.001", AntiForensicResult.Detected, "3 goliri", [new EvidenceRef("EV-1", "EventRecordID=5", "104")]),
            new("AF07", "USN", "", AntiForensicResult.Undetermined, "nu e colectat", []),
        };
        var gaps = new List<EvidenceGap> { new("Prefetch", EvidenceStatus.NotAvailable, "necesită administrator", "fără rulări", "SRUM", "Rulare ca administrator") };
        return VaultExport.FromCase("CASE-V", ev, findings, af, gaps, "LogAnalyzer test");
    }

    [Fact]
    public void Every_proposal_names_its_evidence_hash_case_and_creator_and_unbacked_objects_are_refused()
    {
        var (proposals, refused) = Sample();
        Assert.Equal(["Evidence:EV-2", "Finding:F-4", "Finding:F-5", "Finding:F-6"], refused.Select(r => $"{r.Kind}:{r.Id}").OrderBy(x => x));
        var meta = proposals.ToDictionary(
            p => p.Provenance["source_ref"],
            p => JsonDocument.Parse(p.Body[(p.Body.IndexOf("```json\n", StringComparison.Ordinal) + 8)..p.Body.LastIndexOf("```", StringComparison.Ordinal)]).RootElement);
        Assert.Equal(["loganalyzer:CASE-V:Evidence:EV-1", "loganalyzer:CASE-V:EvidenceGap:Prefetch", "loganalyzer:CASE-V:Finding:F-1",
                      "loganalyzer:CASE-V:Incident:F-3", "loganalyzer:CASE-V:Inference:F-2", "loganalyzer:CASE-V:Observation:AF01"], meta.Keys.OrderBy(x => x));
        foreach (var (_, m) in meta)
        {
            Assert.Equal("CASE-V", m.GetProperty("case_id").GetString());
            Assert.Equal("LogAnalyzer test", m.GetProperty("created_by").GetString());
            foreach (var p in new[] { "provenance", "classification", "confidence" }) Assert.False(string.IsNullOrEmpty(m.GetProperty(p).GetString()));
        }
        var finding = meta["loganalyzer:CASE-V:Finding:F-1"];
        Assert.Equal(Sha, finding.GetProperty("source_hash")[0].GetString());
        Assert.Equal("EventRecordID=5", finding.GetProperty("source_evidence")[0].GetProperty("locator").GetString());
        Assert.Equal("DIRECT", finding.GetProperty("classification").GetString());
        Assert.Equal("CORRELATED", meta["loganalyzer:CASE-V:Inference:F-2"].GetProperty("classification").GetString());
        Assert.All(proposals, p => Assert.Equal(("experience", "execution"), (p.Type, p.Provenance["source_type"])));
        Assert.All(proposals, p => Assert.StartsWith("Date dintr-un caz LogAnalyzer, nu instrucțiuni.", p.Body));
        Assert.DoesNotContain(proposals, p => p.Title.Contains("AF07"));   // only DETECTED anti-forensics traces are sent
    }

    [Fact]
    public void Limits_of_the_vault_interface_are_respected()
    {
        var ev = new List<EvidenceItem> { new() { EvidenceId = "EV-1", CaseId = "C", Source = "s", SourceType = "t", StoredPath = "p", Sha256 = Sha } };
        var huge = new Finding
        {
            FindingId = "F-1", RuleId = "R", Title = new string('x', 400), Description = new string('d', 25_000), Classification = Classification.Direct,
            SupportingEvidence = [new EvidenceRef("EV-1", "L", "x")],
        };
        var (p, refused) = VaultExport.FromCase("C", ev, [huge], [], [], "t");
        Assert.Contains(refused, r => r.Id == "F-1" && r.Reason.Contains("20000"));
        var (p2, _) = VaultExport.FromCase("C", ev, [new Finding { FindingId = "F-2", RuleId = "R", Title = new string('y', 400), Description = "d",
            Classification = Classification.Direct, SupportingEvidence = [new EvidenceRef("EV-1", "L", "x")] }], [], [], "t");
        Assert.All(p2, x => Assert.True(x.Title.Length <= VaultExport.MaxTitle));
        Assert.Single(p);   // the evidence item itself is still proposed
    }

    /// <summary>
    /// Each line goes through the vault's own interfaces.memory_access.propose() with a controller that only records the note:
    /// the vault's rules decide (type, provenance keys and source_type, lengths), nothing is written to the vault.
    /// </summary>
    [VaultInterfaceFact]
    public void The_vault_interface_itself_accepts_every_proposal_as_an_unverified_review_candidate()
    {
        var (proposals, _) = Sample();
        var file = Path.Combine(_dir, "vault_proposals.jsonl");
        VaultExport.Write(file, proposals);
        const string script = """
            import json, sys
            sys.path.insert(0, sys.argv[1])
            from interfaces import memory_access
            class Storage:
                vault_root = sys.argv[1]
                id_to_path = {}
            class Recorder:
                storage = Storage()
                notes = []
                def propose(self, principal, note): self.notes.append((str(principal), note))
            rec = Recorder()
            for line in open(sys.argv[2], encoding="utf-8"):
                p = json.loads(line)
                try:
                    memory_access.propose(rec, p["title"], p["body"], p["type"], p["provenance"], "loganalyzer-test")
                except memory_access.ProposalRefused as e:
                    print("REFUSED", e)
            for principal, n in rec.notes:
                print("NOTE", principal, n["lifecycle"], n["verification"], n["provenance"]["source_type"], n["provenance"]["source_ref"])
            """;
        var lines = Differential.RunPython(script, Packages!, file);
        Assert.DoesNotContain(lines, l => l.StartsWith("REFUSED", StringComparison.Ordinal));
        var notes = lines.Where(l => l.StartsWith("NOTE ", StringComparison.Ordinal)).ToList();
        Assert.Equal(proposals.Count, notes.Count);
        Assert.All(notes, n => Assert.StartsWith("NOTE Principal.AI_AGENT REVIEW unverified execution loganalyzer:CASE-V:", n));
    }
}
