namespace LogAnalyzer.Dfir.Model;

/// <summary>One acquired or imported object with its own identity (spec §8).</summary>
public sealed class EvidenceItem
{
    public required string EvidenceId { get; init; }
    public required string CaseId { get; init; }
    /// <summary>Logical source, e.g. "EventLog:Security", "Prefetch", "SRUM".</summary>
    public required string Source { get; init; }
    /// <summary>Artifact kind used for parser dispatch, e.g. "evtx", "prefetch", "srum", "pcapng".</summary>
    public required string SourceType { get; init; }
    public string OriginalPath { get; init; } = "";
    /// <summary>Path relative to the case root. Raw evidence is never modified after storage.</summary>
    public string StoredPath { get; init; } = "";
    public string OriginalName { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
    public DateTimeOffset AcquiredAtUtc { get; init; }
    public string Host { get; init; } = "";
    public string User { get; init; } = "";
    public string Timezone { get; init; } = "";
    public string Collector { get; init; } = "";
    public string CollectorVersion { get; init; } = "";
    public string Parser { get; set; } = "";
    public string ParserVersion { get; set; } = "";
    public EvidenceStatus Status { get; set; }
    public TemporalType TemporalType { get; init; } = TemporalType.Unknown;
    public Sensitivity Sensitivity { get; init; } = Sensitivity.Confidential;
    public string? ParentEvidenceId { get; init; }
    public string Notes { get; set; } = "";
}
