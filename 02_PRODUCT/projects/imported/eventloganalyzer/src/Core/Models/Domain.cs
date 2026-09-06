namespace EventLogAnalyzer.Core.Models;

public enum Severity
{
    Info,
    Warning,
    Error,
    Critical,
    Suspicious
}

public enum Impact
{
    Low,
    Medium,
    High
}

/// <summary>
/// One parsed Windows event, enriched (optionally) by the knowledge base.
/// Enrichment fields are nullable because a raw parse happens before
/// EventKnowledgeBase.Explain() is called - keeps parsing and
/// interpretation as separate, independently testable steps.
/// </summary>
public sealed record EventRecordModel
{
    public required int EventId { get; init; }
    public required string Provider { get; init; }
    public required Severity Level { get; init; }
    public required DateTimeOffset TimeCreated { get; init; }
    public string? UserSid { get; init; }
    public required string Computer { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public required string SourceFile { get; init; }

    // Populated by EventKnowledgeBase.Explain()
    public string? HumanTitle { get; init; }
    public string? Explanation { get; init; }
    public IReadOnlyList<string>? CommonCauses { get; init; }
    public string? RecommendedAction { get; init; }
    public string? DocsUrl { get; init; }
}

/// <summary>
/// A detected pattern across N events - what the Dashboard and
/// Remediation panel actually operate on.
/// </summary>
public sealed record Issue
{
    public required string Category { get; init; } // e.g. "RepeatedServiceCrash"
    public required string Title { get; init; }
    public required Severity Severity { get; init; }
    public required Impact Impact { get; init; }
    public required int Count { get; init; }
    public required DateTimeOffset FirstSeen { get; init; }
    public required DateTimeOffset LastSeen { get; init; }
    public required IReadOnlyList<EventRecordModel> RelatedEvents { get; init; }
    public required string Summary { get; init; }
    public Recommendation? Recommendation { get; init; }
}

public sealed record Recommendation
{
    public required string Title { get; init; }
    public required IReadOnlyList<string> Steps { get; init; }
    /// <summary>True if a PowerShell script can be generated for this recommendation.</summary>
    public bool CanGenerateScript { get; init; }
}

/// <summary>
/// A single decoded item of user activity from NTUSER.DAT
/// (UserAssist, RecentDocs/MRU, RunMRU, etc).
/// </summary>
public sealed record UserActivityItem
{
    public required string HiveFile { get; init; }
    public required string Category { get; init; } // "UserAssist" | "RecentDocs" | "RunMRU" | "Shellbag"
    public required string Description { get; init; }
    public DateTimeOffset? LastExecuted { get; init; }
    public int? RunCount { get; init; }
    public string? Path { get; init; }
}

/// <summary>
/// A finding from the HKLM hive - config/driver items worth surfacing
/// (not necessarily malicious, just relevant to stability/security).
/// </summary>
public sealed record ConfigFinding
{
    public required string HiveFile { get; init; }
    public required string KeyPath { get; init; }
    public required string Finding { get; init; }
    public required Severity Severity { get; init; }
    public string? Explanation { get; init; }
}
