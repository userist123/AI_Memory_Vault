namespace LogAnalyzer.Dfir.Model;

/// <summary>Normalized timeline row (spec §54). Every row points back to its evidence (§70).</summary>
public sealed class TimelineEvent
{
    public required Timestamp Time { get; init; }
    public required string Source { get; init; }
    public required string EvidenceId { get; init; }
    public string EventId { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Host { get; init; } = "";
    public string User { get; init; } = "";
    public string Process { get; init; } = "";
    public int? Pid { get; init; }
    public int? Ppid { get; init; }
    public string Path { get; init; } = "";
    public string RemoteIp { get; init; } = "";
    public int? RemotePort { get; init; }
    public string Dns { get; init; } = "";
    public string Task { get; init; } = "";
    public string Service { get; init; } = "";
    public string Hash { get; init; } = "";
    public required string Summary { get; init; }
    /// <summary>Meaning of the timestamp, e.g. "event recorded", "last run", "file created", "SRUM hour end".</summary>
    public string TimeSemantics { get; init; } = "recorded";
    public TemporalType TemporalType { get; init; } = TemporalType.Historical;
    public Classification Classification { get; set; } = Classification.Unproven;
    public Confidence Confidence { get; set; } = Confidence.Low;
    /// <summary>Locator inside the evidence: record id, frame number, row, offset.</summary>
    public string Locator { get; init; } = "";
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Notes { get; set; } = "";
    /// <summary>Provenance bound by the pipeline after parsing: SHA-256 of the source and the parser that produced the row.</summary>
    public string SourceSha256 { get; set; } = "";
    public string ParserId { get; set; } = "";
    public string ParserVersion { get; set; } = "";
}

/// <summary>A pointer from a finding to the exact evidence behind it.</summary>
/// <remarks><see cref="Sha256"/> is the source hash at acquisition, bound by <c>ProvenanceBinder</c>.</remarks>
public sealed record EvidenceRef(string EvidenceId, string Locator, string Description, string Sha256 = "");

/// <summary>Finding (spec §52). Severity and confidence are deliberately separate.</summary>
public sealed class Finding
{
    public required string FindingId { get; init; }
    public required string RuleId { get; init; }
    public required string Title { get; init; }
    public Severity Severity { get; init; }
    public string Category { get; init; } = "";
    public Classification Classification { get; init; }
    public Confidence Confidence { get; init; }
    public DateTimeOffset? FirstSeenUtc { get; init; }
    public DateTimeOffset? LastSeenUtc { get; init; }
    public string Host { get; init; } = "";
    public string User { get; init; } = "";
    public string Process { get; init; } = "";
    public int? Pid { get; init; }
    public string File { get; init; } = "";
    public string Ip { get; init; } = "";
    public string Domain { get; init; } = "";
    public required string Description { get; init; }
    public List<EvidenceRef> SupportingEvidence { get; init; } = [];
    public List<string> ContradictingEvidence { get; init; } = [];
    public List<string> AlternativeExplanations { get; init; } = [];
    public List<string> MissingEvidence { get; init; } = [];
    public List<string> RecommendedNextSteps { get; init; } = [];
    public string MitreTechniqueId { get; init; } = "";
    /// <summary>Why this classification was assigned (spec §68).</summary>
    public string ClassificationReason { get; init; } = "";
}

/// <summary>Evidence gap (spec §48, §67). "Not available" never means "did not happen".</summary>
public sealed record EvidenceGap(string Artifact, EvidenceStatus Status, string Reason, string Impact, string AlternativeSource, string Recoverability, string Notes = "");

/// <summary>Outcome of one parser over one evidence item (spec §75). Exceptions become FAILED, never EMPTY.</summary>
public sealed class ParseResult
{
    public required string EvidenceId { get; init; }
    public required string Parser { get; init; }
    public required string ParserVersion { get; init; }
    /// <summary>Parser maturity at the time of the run (VALIDATED / TESTED / EXPERIMENTAL), from its descriptor.</summary>
    public string ParserStatus { get; set; } = "";
    public EvidenceStatus Status { get; set; }
    public string Error { get; set; } = "";
    public int Records { get; set; }
    public int MalformedRecords { get; set; }
    public List<EvidenceGap> Gaps { get; } = [];
    /// <summary>Case-relative paths of derived files this parser produced (each hashed into custody).</summary>
    public List<string> DerivedOutputs { get; } = [];
    /// <summary>SHA-256 recorded at acquisition.</summary>
    public string ExpectedSha256 { get; set; } = "";
    /// <summary>SHA-256 of the source right before parsing (preflight) and right after; all three must match.</summary>
    public string SourceSha256Before { get; set; } = "";
    public string SourceSha256After { get; set; } = "";
    /// <summary>Format recognised from content (<c>EvidenceFingerprint</c>), e.g. "evtx", "regf", "ese".</summary>
    public string SourceFingerprint { get; set; } = "";

    public ParseResult Finish()
    {
        if (Status is EvidenceStatus.Failed or EvidenceStatus.NotAvailable or EvidenceStatus.SkippedByDesign) return this;
        Status = Records == 0 ? EvidenceStatus.Empty : MalformedRecords > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return this;
    }
}
