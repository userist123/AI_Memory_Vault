using System.Text.Json;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>WP3b: outputs in custody, dependency index, re-check on open, exact invalidation, head anchor.</summary>
public sealed partial class CaseWorkspace
{
    private const string OutputAction = "output.written";
    private const string DependsPrefix = "depends=";

    public string DependenciesPath => Path.Combine(Root, "Analysis", "dependencies.json");
    public string InvalidationsPath => Path.Combine(Root, "Analysis", "invalidations.json");
    public string RecheckPath => Path.Combine(Root, "Analysis", "integrity_recheck.json");

    /// <summary>Result of the re-check run by <see cref="Open"/> (null when the case was opened with recheck: false or not yet checked).</summary>
    public RecheckResult? LastRecheck { get; private set; }

    // ---- 1. outputs in custody ----

    private string RelativeInside(string relPath)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relPath));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Output must live inside the case: {relPath}");
        return Path.GetRelativePath(Root, full).Replace('\\', '/');
    }

    /// <summary>
    /// Registers a file written by the pipeline, a report or an export in the custody chain (action <c>output.written</c>, one entry per call)
    /// with its SHA-256, producer, version and the evidence ids it was derived from. The file is not changed. Registering the same path again
    /// after it was rewritten supersedes the earlier entry for the re-check.
    /// </summary>
    public OutputRecord RecordOutput(string relPath, string producer, string version, IEnumerable<string>? dependsOnEvidenceIds = null)
    {
        var rel = RelativeInside(relPath);
        var full = FullPath(rel);
        if (!File.Exists(full)) throw new FileNotFoundException("Output file not found in the case.", full);
        var sha = Hashing.Sha256File(full);
        var size = new FileInfo(full).Length;
        var deps = (dependsOnEvidenceIds ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();
        Custody(new CustodyEntry(DateTimeOffset.UtcNow, Environment.UserName, OutputAction, "", "", rel, sha, producer, version, "",
            deps.Count > 0 ? DependsPrefix + string.Join(";", deps) : ""));
        return new OutputRecord(rel, sha, size, producer, version, deps);
    }

    /// <summary>Writes <paramref name="relManifestPath"/> listing <paramref name="relFiles"/> with SHA-256 and the current chain heads, then registers it as an output.</summary>
    public OutputManifest WriteManifest(string relManifestPath, IEnumerable<string> relFiles, string producer, string version)
    {
        var m = new OutputManifest { CaseId = Info.CaseId, CreatedUtc = DateTimeOffset.UtcNow, Producer = producer, Version = version, Anchor = Anchor(), ScopeNote = ScopeNote ?? "" };
        foreach (var f in relFiles)
        {
            var rel = RelativeInside(f);
            var full = FullPath(rel);
            if (!File.Exists(full)) throw new FileNotFoundException("Manifest entry not found in the case.", full);
            m.Files.Add(new OutputManifestFile(rel, Hashing.Sha256File(full), new FileInfo(full).Length));
        }
        Json.Write(FullPath(RelativeInside(relManifestPath)), m);
        RecordOutput(relManifestPath, producer, version);
        return m;
    }

    // ---- 2. dependency index ----

    /// <summary>Builds and writes Analysis/dependencies.json (finding and Vault proposal id → evidence ids) and registers it as an output.</summary>
    public DependencyIndex WriteDependencies(IReadOnlyList<Finding> findings, IReadOnlyList<VaultProposal> proposals)
    {
        var idx = DependencyIndex.Build(Info.CaseId, LoadEvidence(), findings, proposals);
        idx.Write(DependenciesPath);
        RecordOutput("Analysis/dependencies.json", "DependencyIndex", DfirInfo.ApplicationVersion, idx.AllEvidenceIds());
        return idx;
    }

    private (DependencyIndex? Index, string Source) LoadDependencies(List<string> notes)
    {
        try
        {
            if (DependencyIndex.Read(DependenciesPath) is { } idx) return (idx, "dependencies.json");
            var findingsPath = Path.Combine(Root, "Analysis", "findings.json");
            if (!File.Exists(findingsPath)) return (null, "indisponibil (fără dependencies.json și fără findings.json)");
            var ff = FindingsFile.Read(findingsPath);
            var proposals = VaultExport.Read(Path.Combine(Root, "Exports", "vault_proposals.jsonl"));
            return (DependencyIndex.Build(Info.CaseId, LoadEvidence(), ff.Findings, proposals), "derivat din findings.json (caz fără dependencies.json)");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            notes.Add("dependențele nu au putut fi citite: " + ex.Message);
            return (null, "indisponibil (" + ex.GetType().Name + ")");
        }
    }

    // ---- 5. external head anchor ----

    public ChainAnchor Anchor()
    {
        lock (_gate)
        {
            var c = _custodyChain.Head(); var a = _auditChain.Head();
            return new ChainAnchor(c.Seq, c.Head, a.Seq, a.Head);
        }
    }

    /// <summary>
    /// Compares a head held outside the case with the logs: both chains must still contain the anchored entry. A chain cut back below
    /// the anchor (or rewritten) fails. A chain cut back only to entries newer than the anchor cannot be detected.
    /// </summary>
    public StateResult CheckAnchor(ChainAnchor anchor)
    {
        lock (_gate)
        {
            if (anchor.CustodySeq > 0 && !_custodyChain.Contains(anchor.CustodySeq, anchor.CustodyHead))
                return StateResult.Refuse($"lanțul de custodie nu mai conține intrarea Seq {anchor.CustodySeq} din ancoră (sfârșit tăiat sau lanț rescris)");
            if (anchor.AuditSeq > 0 && !_auditChain.Contains(anchor.AuditSeq, anchor.AuditHead))
                return StateResult.Refuse($"lanțul de audit nu mai conține intrarea Seq {anchor.AuditSeq} din ancoră (sfârșit tăiat sau lanț rescris)");
            return StateResult.Success;
        }
    }

    // ---- 3. re-check ----

    /// <summary>
    /// Re-hashes every stored evidence file against evidence_index.jsonl, verifies both chains, re-hashes the registered outputs, derives the exact
    /// invalidations, writes Analysis/integrity_recheck.json and Analysis/invalidations.json and the audit entry <c>case.recheck</c>.
    /// MISSING and MODIFIED are reported; nothing is repaired, moved or deleted. Hashing streams and honours <paramref name="ct"/>.
    /// </summary>
    public RecheckResult Recheck(CancellationToken ct = default)
    {
        var r = new RecheckResult { AtUtc = DateTimeOffset.UtcNow };
        var evidence = LoadEvidence();
        foreach (var e in evidence)
        {
            ct.ThrowIfCancellationRequested();
            r.Evidence.Add(CheckEvidence(e, ct));
        }
        r.Chains = EffectiveChains();
        foreach (var o in RegisteredOutputs())
        {
            ct.ThrowIfCancellationRequested();
            r.Outputs.Add(CheckOutput(o, ct));
        }
        r.Anchor = Anchor();

        r.ModifiedCount = r.Evidence.Count(c => c.Status == EvidenceCheckStatus.Modified);
        r.MissingCount = r.Evidence.Count(c => c.Status == EvidenceCheckStatus.Missing);
        r.UnverifiedCount = r.Evidence.Count(c => c.Status is EvidenceCheckStatus.NoBaseline or EvidenceCheckStatus.Unreadable)
                          + r.Outputs.Count(c => c.Status == OutputCheckStatus.Unreadable);
        r.OutputsModifiedCount = r.Outputs.Count(c => c.Status == OutputCheckStatus.Modified);
        r.OutputsMissingCount = r.Outputs.Count(c => c.Status == OutputCheckStatus.Missing);

        var (index, source) = LoadDependencies(r.Notes);
        r.Invalidations = ComputeInvalidations(index, r.Evidence);
        r.InvalidatedCount = r.Invalidations.Count;

        bool brokenChain = r.Chains.Custody.Status == ChainStatus.Broken || r.Chains.Audit.Status == ChainStatus.Broken;
        bool legacy = r.Chains.Custody.Status == ChainStatus.Legacy || r.Chains.Audit.Status == ChainStatus.Legacy
                      || r.Chains.Custody.LegacyLines > 0 || r.Chains.Audit.LegacyLines > 0;
        r.Verdict = brokenChain ? RecheckVerdict.ChainBroken
                  : r.ModifiedCount + r.OutputsModifiedCount > 0 ? RecheckVerdict.Modified
                  : r.MissingCount + r.OutputsMissingCount > 0 ? RecheckVerdict.Missing
                  : r.UnverifiedCount > 0 ? RecheckVerdict.Unverified
                  : legacy ? RecheckVerdict.Legacy : RecheckVerdict.Valid;
        r.Summary = Summarize(r, legacy);

        try
        {
            Directory.CreateDirectory(Path.Combine(Root, "Analysis"));
            Json.Write(InvalidationsPath, new InvalidationsFile { CaseId = Info.CaseId, GeneratedUtc = r.AtUtc, DependenciesSource = source, Items = r.Invalidations });
            Json.Write(RecheckPath, r);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            r.Notes.Add("rezultatul reverificării nu a putut fi scris în caz: " + ex.Message);
        }
        Audit("case.recheck", $"{r.Verdict} modified={r.ModifiedCount} missing={r.MissingCount} outputs_modified={r.OutputsModifiedCount} outputs_missing={r.OutputsMissingCount} " +
                              $"invalidated={r.InvalidatedCount} custody_head={r.Anchor.CustodySeq}:{r.Anchor.CustodyHead} audit_head={r.Anchor.AuditSeq}:{r.Anchor.AuditHead}");
        LastRecheck = r;
        return r;
    }

    private EvidenceCheck CheckEvidence(EvidenceItem e, CancellationToken ct)
    {
        string full;
        try { full = FullPath(e.StoredPath); }
        catch (ArgumentException) { return new(e.EvidenceId, e.StoredPath, e.Sha256, null, EvidenceCheckStatus.Missing, "cale invalidă"); }
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return new(e.EvidenceId, e.StoredPath, e.Sha256, null, EvidenceCheckStatus.Missing, "calea probei iese din folderul cazului");
        if (!File.Exists(full)) return new(e.EvidenceId, e.StoredPath, e.Sha256, null, EvidenceCheckStatus.Missing);
        if (e.Sha256.Length == 0) return new(e.EvidenceId, e.StoredPath, e.Sha256, null, EvidenceCheckStatus.NoBaseline, "indexul nu are SHA-256 de referință");
        try
        {
            var actual = Hashing.Sha256File(full, ct);
            return new(e.EvidenceId, e.StoredPath, e.Sha256, actual,
                string.Equals(actual, e.Sha256, StringComparison.OrdinalIgnoreCase) ? EvidenceCheckStatus.Intact : EvidenceCheckStatus.Modified);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(e.EvidenceId, e.StoredPath, e.Sha256, null, EvidenceCheckStatus.Unreadable, ex.Message); }
    }

    private OutputCheck CheckOutput(OutputRecord o, CancellationToken ct)
    {
        var full = FullPath(o.Path);
        var deps = o.DependsOn;
        if (!File.Exists(full)) return new(o.Path, o.Producer, o.Sha256, null, OutputCheckStatus.Missing, deps);
        try
        {
            var actual = Hashing.Sha256File(full, ct);
            return new(o.Path, o.Producer, o.Sha256, actual, string.Equals(actual, o.Sha256, StringComparison.OrdinalIgnoreCase) ? OutputCheckStatus.Intact : OutputCheckStatus.Modified, deps);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(o.Path, o.Producer, o.Sha256, null, OutputCheckStatus.Unreadable, deps); }
    }

    /// <summary>Latest <c>output.written</c> custody entry per path. Lines that are not JSON are skipped here; <see cref="VerifyChains"/> reports them.</summary>
    private List<OutputRecord> RegisteredOutputs()
    {
        var latest = new Dictionary<string, OutputRecord>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(CustodyJsonlPath)) return [];
        foreach (var line in File.ReadLines(CustodyJsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var o = JsonNode.Parse(line)!.AsObject();
                if (o["action"]?.GetValue<string>() != OutputAction || o["to"]?.GetValue<string>() is not { Length: > 0 } to) continue;
                var trans = o["transformation"]?.GetValue<string>() ?? "";
                var deps = trans.StartsWith(DependsPrefix, StringComparison.Ordinal) ? trans[DependsPrefix.Length..].Split(';', StringSplitOptions.RemoveEmptyEntries) : [];
                latest[to] = new OutputRecord(to, o["sha256"]?.GetValue<string>() ?? "", 0, o["tool"]?.GetValue<string>() ?? "", o["toolVersion"]?.GetValue<string>() ?? "", deps);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return latest.Values.ToList();
    }

    /// <summary>
    /// <see cref="VerifyChains"/> plus the honest reading of a case created before WP3: a missing or empty chain next to a non-empty CSV custody
    /// or audit log is "legacy", not an empty valid chain.
    /// </summary>
    private ChainReport EffectiveChains()
    {
        var rep = VerifyChains();
        var custody = rep.Custody;
        if (custody.Status == ChainStatus.Valid && custody.Entries == 0)
        {
            int rows = Math.Max(0, CountLines(CustodyCsvPath) - 1);   // minus the header
            if (rows > 0) custody = custody with { Status = ChainStatus.Legacy, LegacyLines = rows, Message = HashChain.LegacyMessage };
        }
        var audit = rep.Audit;
        if (audit.Status == ChainStatus.Valid)
        {
            int lines = CountLines(AppAuditLogPath);
            if (lines > audit.Entries + audit.LegacyLines)
            {
                int old = lines - audit.Entries - audit.LegacyLines;
                audit = audit with
                {
                    Status = audit.Entries == 0 ? ChainStatus.Legacy : ChainStatus.Valid, LegacyLines = audit.LegacyLines + old,
                    Message = audit.Entries == 0 ? HashChain.LegacyMessage : $"{audit.Entries} intrări verificate; {old} linii anterioare: {HashChain.LegacyMessage}",
                };
            }
        }
        return new ChainReport(custody, audit);
    }

    private static int CountLines(string path)
    {
        if (!File.Exists(path)) return 0;
        int n = 0;
        try { foreach (var l in File.ReadLines(path)) if (!string.IsNullOrWhiteSpace(l)) n++; }
        catch (IOException) { }
        return n;
    }

    // ---- 4. exact invalidation ----

    private static List<Invalidation> ComputeInvalidations(DependencyIndex? index, IReadOnlyList<EvidenceCheck> checks)
    {
        var bad = checks.Where(c => c.Status is EvidenceCheckStatus.Modified or EvidenceCheckStatus.Missing)
                        .ToDictionary(c => c.EvidenceId, c => c, StringComparer.Ordinal);
        var result = new List<Invalidation>();
        if (index is null || bad.Count == 0) return result;

        void Scan(string kind, IEnumerable<KeyValuePair<string, List<string>>> deps)
        {
            foreach (var (id, evidenceIds) in deps)
            {
                var hit = evidenceIds.Where(bad.ContainsKey).OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (hit.Count == 0) continue;
                var reason = string.Join("; ", hit.Select(h => bad[h].Status == EvidenceCheckStatus.Modified
                    ? $"dovada {h} MODIFICATĂ (SHA-256 diferit de cel de la achiziție)"
                    : $"dovada {h} LIPSĂ din caz"));
                result.Add(new Invalidation(kind, id, Invalidation.Invalidated, reason, hit));
            }
        }
        Scan("finding", index.Findings);
        Scan("vault_proposal", index.VaultProposals);
        return result;
    }

    /// <summary>Invalidations written by the last re-check (empty when none or when the case was never re-checked). A corrupt file throws: the Vault gate must not guess.</summary>
    public InvalidationsFile LoadInvalidations()
    {
        if (!File.Exists(InvalidationsPath)) return new InvalidationsFile { CaseId = Info.CaseId };
        var f = Json.Read<InvalidationsFile>(InvalidationsPath);
        f.SchemaVersion = SchemaVersions.Accept(f.SchemaVersion, "invalidations.json");
        return f;
    }

    private static string Summarize(RecheckResult r, bool legacy)
    {
        var parts = new List<string>();
        if (r.Chains.Custody.Status == ChainStatus.Broken) parts.Add($"lanț rupt (custodie: {r.Chains.Custody.Message})");
        if (r.Chains.Audit.Status == ChainStatus.Broken) parts.Add($"lanț rupt (audit: {r.Chains.Audit.Message})");
        if (r.ModifiedCount > 0) parts.Add($"Modificate: {r.ModifiedCount}");
        if (r.MissingCount > 0) parts.Add($"Lipsă: {r.MissingCount}");
        if (r.OutputsModifiedCount > 0) parts.Add($"rezultate modificate: {r.OutputsModifiedCount}");
        if (r.OutputsMissingCount > 0) parts.Add($"rezultate lipsă: {r.OutputsMissingCount}");
        if (r.UnverifiedCount > 0) parts.Add($"neverificabile: {r.UnverifiedCount}");
        if (r.InvalidatedCount > 0) parts.Add($"invalidate: {r.InvalidatedCount}");
        if (legacy) parts.Add(HashChain.LegacyMessage);
        if (parts.Count == 0) return $"Reverificare: valid ({r.Evidence.Count} probe, {r.Outputs.Count} rezultate, lanțuri intacte)";
        return "Reverificare: " + string.Join("; ", parts);
    }
}
