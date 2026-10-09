using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Case;

public enum EvidenceCheckStatus { Intact, Modified, Missing, NoBaseline, Unreadable }
public enum OutputCheckStatus { Intact, Modified, Missing, Unreadable }

/// <summary>Worst finding of a re-check, in this order: chain broken, modified, missing, unverified, legacy, valid. Legacy is never reported as valid.</summary>
public enum RecheckVerdict { Valid, Legacy, Unverified, Missing, Modified, ChainBroken }

public sealed record EvidenceCheck(string EvidenceId, string StoredPath, string ExpectedSha256, string? ActualSha256, EvidenceCheckStatus Status, string Detail = "");
public sealed record OutputCheck(string Path, string Producer, string ExpectedSha256, string? ActualSha256, OutputCheckStatus Status, IReadOnlyList<string> DependsOn);

/// <summary>
/// Heads of the custody and audit chains at one moment. Held outside the case (a manifest copy, a printout, a ticket) it lets a later
/// check detect that entries at the END of a chain were removed, which the chain alone cannot show. It proves nothing about the case if
/// the copy lives only inside the case folder.
/// </summary>
public sealed record ChainAnchor(long CustodySeq, string CustodyHead, long AuditSeq, string AuditHead);

public sealed record OutputRecord(string Path, string Sha256, long Size, string Producer, string Version, IReadOnlyList<string> DependsOn);

/// <summary>Result of <see cref="CaseWorkspace.Recheck"/>; also written to Analysis/integrity_recheck.json.</summary>
public sealed class RecheckResult
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = SchemaVersions.IntegrityRecheck;
    public DateTimeOffset AtUtc { get; set; }
    public RecheckVerdict Verdict { get; set; }
    /// <summary>One line for the UI (Romanian).</summary>
    public string Summary { get; set; } = "";
    public int ModifiedCount { get; set; }
    public int MissingCount { get; set; }
    public int UnverifiedCount { get; set; }
    public int OutputsModifiedCount { get; set; }
    public int OutputsMissingCount { get; set; }
    public int InvalidatedCount { get; set; }
    public ChainReport Chains { get; set; } = null!;
    /// <summary>Chain heads at verification time, before the re-check's own audit entry.</summary>
    public ChainAnchor Anchor { get; set; } = null!;
    public List<EvidenceCheck> Evidence { get; set; } = [];
    public List<OutputCheck> Outputs { get; set; } = [];
    public List<Invalidation> Invalidations { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

/// <summary>A manifest next to the files of a report or export (Exports/export_manifest.json, Control/.../manifest.json).</summary>
public sealed class OutputManifest
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = SchemaVersions.ExportManifest;
    public string CaseId { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public string Producer { get; set; } = "";
    public string Version { get; set; } = "";
    public List<OutputManifestFile> Files { get; set; } = [];
    /// <summary>Chain heads when the manifest was written. Keep a copy outside the case to detect truncation at the end of a chain.</summary>
    public ChainAnchor Anchor { get; set; } = null!;
}

public sealed record OutputManifestFile(string Path, string Sha256, long Size);
