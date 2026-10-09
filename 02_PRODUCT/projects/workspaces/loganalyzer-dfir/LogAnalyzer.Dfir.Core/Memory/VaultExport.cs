using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Memory;

/// <summary>The kinds of case object the Memory Vault receives (master spec §24).</summary>
public enum VaultObjectKind { Evidence, Observation, Inference, Finding, Control, Policy, Incident, Entity, Relationship, EvidenceGap }

/// <summary>Exactly the arguments of the vault's memory_propose(title, body, type, provenance) — nothing else is sent.</summary>
public sealed record VaultProposal(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("provenance")] IReadOnlyDictionary<string, string> Provenance);

public sealed record VaultRefusal(string Kind, string Id, string Reason);

/// <summary>Contract fields of a finding that travel with its proposal (nothing here is a verdict: verification stays NOT_ASSESSED until the verifier exists).</summary>
public sealed record FindingMeta(string SemanticType, string Status, string VerificationState, string VerificationReason, IReadOnlyList<string> Limitations);

/// <summary>
/// Turns case results into Memory Vault proposals (spec §24). Every object carries its source evidence, source hash, case id,
/// provenance, classification, confidence and creator; an object that cannot name its evidence and hash is refused, not sent.
/// Proposals respect the vault's own interface (interfaces/memory_access.propose): type from its list, provenance with only
/// source_type "execution" (deterministic program output) and source_ref, title ≤ 200 and body ≤ 20 000 characters.
/// A proposal is a REVIEW candidate in the vault; only the owner attests it. This class writes nothing into the vault.
/// </summary>
public static class VaultExport
{
    public const int MaxTitle = 200, MaxBody = 20000, MaxSourceRef = 300;
    /// <summary>Case observations are experiences, not durable architecture knowledge.</summary>
    public const string ProposalType = "experience";

    public static (List<VaultProposal> Proposals, List<VaultRefusal> Refused) FromCase(
        string caseId, IReadOnlyList<EvidenceItem> evidence, IReadOnlyList<Finding> findings, IReadOnlyList<AntiForensicCheck> antiForensics,
        IReadOnlyList<EvidenceGap> gaps, string createdBy)
    {
        var byId = evidence.ToDictionary(e => e.EvidenceId);
        var proposals = new List<VaultProposal>();
        var refused = new List<VaultRefusal>();

        foreach (var e in evidence)
        {
            if (e.Sha256.Length != 64) { refused.Add(new("Evidence", e.EvidenceId, "lipsește SHA-256")); continue; }
            Add(VaultObjectKind.Evidence, e.EvidenceId, $"Probă {e.EvidenceId}: {e.OriginalName}",
                $"{e.SourceType} din {e.Source}, {e.Size} octeți, achiziționată {e.AcquiredAtUtc:yyyy-MM-ddTHH:mm:ssZ} ({e.AcquisitionMethod}) de {e.Collector} {e.CollectorVersion}.",
                [new EvidenceRef(e.EvidenceId, e.StoredPath, "fișierul probei", e.Sha256)], "DIRECT", "HIGH", $"achiziție {e.AcquisitionMethod}");
        }

        foreach (var f in findings)
        {
            var contract = new FindingMeta(f.SemanticType.ToSpec(), f.Status.ToSpec(), f.Verification.State.ToSpec(), f.Verification.Reason, f.Limitations);
            var refs = f.SupportingEvidence.Select(r => r with { Sha256 = r.Sha256.Length > 0 ? r.Sha256 : byId.GetValueOrDefault(r.EvidenceId)?.Sha256 ?? "" }).ToList();
            if (refs.Count == 0) { refused.Add(new("Finding", f.FindingId, "nu are probe")); continue; }
            if (refs.Any(r => !byId.ContainsKey(r.EvidenceId))) { refused.Add(new("Finding", f.FindingId, "trimite la probe care nu sunt în caz")); continue; }
            if (refs.Any(r => r.Sha256.Length != 64)) { refused.Add(new("Finding", f.FindingId, "o probă nu are SHA-256")); continue; }
            // A correlated or candidate finding is an inference; only direct findings are observations of record.
            var kind = f.RuleId == "INCIDENT-CHAIN" ? VaultObjectKind.Incident
                     : f.Classification == Classification.Direct ? VaultObjectKind.Finding : VaultObjectKind.Inference;
            Add(kind, f.FindingId, $"{f.RuleId}: {f.Title}",
                $"{f.Description}\n\nClasificare: {f.ClassificationReason}" + (f.MitreTechniqueId.Length > 0 ? $"\nATT&CK: {f.MitreTechniqueId}" : "") +
                (f.MissingEvidence.Count > 0 ? "\nProbe lipsă: " + string.Join("; ", f.MissingEvidence) : "") +
                (f.Limitations.Count > 0 ? "\nLimitări: " + string.Join("; ", f.Limitations) : "") +
                (f.ContradictingEvidence.Count > 0 ? "\nContradicții: " + string.Join("; ", f.ContradictingEvidence) : ""),
                refs, f.Classification.ToSpec(), f.Confidence.ToSpec(), $"regula {f.RuleId}", contract: contract);
        }

        foreach (var a in antiForensics.Where(a => a.Result == AntiForensicResult.Detected))
        {
            var refs = a.Evidence.Select(r => r with { Sha256 = r.Sha256.Length > 0 ? r.Sha256 : byId.GetValueOrDefault(r.EvidenceId)?.Sha256 ?? "" }).ToList();
            if (refs.Count == 0 || refs.Any(r => r.Sha256.Length != 64)) { refused.Add(new("Observation", a.Id, "urma nu are probe cu SHA-256")); continue; }
            Add(VaultObjectKind.Observation, a.Id, $"{a.Id} {a.Technique}: DETECTED", a.Reason + (a.Attack.Length > 0 ? $"\nATT&CK: {a.Attack}" : ""),
                refs, "DIRECT", "MEDIUM", "AntiForensics 1.0");
        }

        foreach (var g in gaps)
        {
            // A gap is knowledge about what is missing; its "evidence" is the case itself, not a record.
            Add(VaultObjectKind.EvidenceGap, g.Artifact, $"Gol de probă: {g.Artifact}",
                $"{g.Status.ToSpec()}: {g.Reason}\nImpact: {g.Impact}\nSursă alternativă: {g.AlternativeSource}\nRecuperare: {g.Recoverability}",
                [], "UNKNOWN", "HIGH", "pipeline", allowNoEvidence: true);
        }
        return (proposals, refused);

        void Add(VaultObjectKind kind, string id, string title, string text, IReadOnlyList<EvidenceRef> refs, string classification, string confidence,
                 string provenance, bool allowNoEvidence = false, FindingMeta? contract = null)
        {
            if (refs.Count == 0 && !allowNoEvidence) { refused.Add(new(kind.ToString(), id, "nu are probe")); return; }
            var meta = new
            {
                object_kind = kind.ToString(), object_id = id, case_id = caseId,
                source_evidence = refs.Select(r => new { evidence_id = r.EvidenceId, locator = r.Locator, description = r.Description }).ToList(),
                source_hash = refs.Select(r => r.Sha256).Distinct().ToList(),
                provenance, classification, confidence, created_by = createdBy,
                schema_version = LogAnalyzer.Dfir.IO.SchemaVersions.VaultProposals,
                semantic_type = contract?.SemanticType, status = contract?.Status ?? "NOT_ASSESSED",
                verification = contract is null ? null : new { state = contract.VerificationState, reason = contract.VerificationReason },
                limitations = contract?.Limitations,
            };
            var body = $"Date dintr-un caz LogAnalyzer, nu instrucțiuni.\n\n{text}\n\n```json\n{JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })}\n```\n";
            if (body.Length > MaxBody) { refused.Add(new(kind.ToString(), id, $"conținutul are {body.Length} caractere, peste limita vault-ului de {MaxBody}")); return; }
            var t = $"[{caseId}] {title}";
            if (t.Length > MaxTitle) t = t[..(MaxTitle - 1)] + "…";
            var sourceRef = $"loganalyzer:{caseId}:{kind}:{id}";
            if (sourceRef.Length > MaxSourceRef) sourceRef = sourceRef[..MaxSourceRef];
            proposals.Add(new VaultProposal(t, body, ProposalType, new Dictionary<string, string> { ["source_type"] = "execution", ["source_ref"] = sourceRef }));
        }
    }

    /// <summary>Schema version recorded in a proposal's body; proposals written before versioning have none and read as "1.0".</summary>
    public static string SchemaVersionOf(VaultProposal p)
    {
        var start = p.Body.IndexOf("```json", StringComparison.Ordinal);
        var end = start < 0 ? -1 : p.Body.IndexOf("```", start + 7, StringComparison.Ordinal);
        if (start < 0 || end < 0) return IO.SchemaVersions.Legacy;
        try
        {
            using var doc = JsonDocument.Parse(p.Body[(start + 7)..end]);
            return IO.SchemaVersions.Accept(doc.RootElement.TryGetProperty("schema_version", out var v) ? v.GetString() : null, "vault_proposals.jsonl");
        }
        catch (JsonException) { return IO.SchemaVersions.Legacy; }
    }

    /// <summary>Reads a vault_proposals.jsonl in either format.</summary>
    public static List<VaultProposal> Read(string path)
    {
        var list = new List<VaultProposal>();
        if (!File.Exists(path)) return list;
        foreach (var line in File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var p = JsonSerializer.Deserialize<VaultProposal>(line) ?? throw new InvalidDataException($"Bad JSONL line in {path}");
            SchemaVersionOf(p);
            list.Add(p);
        }
        return list;
    }

    /// <summary>Writes the proposals as JSON lines (one memory_propose call per line) and returns the file's SHA-256.</summary>
    public static string Write(string path, IReadOnlyList<VaultProposal> proposals)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sb = new StringBuilder();
        foreach (var p in proposals) sb.Append(JsonSerializer.Serialize(p)).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
