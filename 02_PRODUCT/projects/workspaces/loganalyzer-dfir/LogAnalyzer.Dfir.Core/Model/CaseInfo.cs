namespace LogAnalyzer.Dfir.Model;

/// <summary>case.json (spec §7, §106).</summary>
public sealed class CaseInfo
{
    public string SchemaVersion { get; init; } = DfirInfo.SchemaVersion;
    public required string CaseId { get; init; }
    public required string Name { get; init; }
    public string Host { get; init; } = "";
    public string User { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Investigator { get; init; } = "";
    public string Os { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Timezone { get; init; } = "";
    public DateTimeOffset? StartTimeUtc { get; set; }
    public DateTimeOffset? EndTimeUtc { get; set; }
    public string Notes { get; set; } = "";
    public string CollectionMode { get; init; } = "";
    public string ApplicationVersion { get; init; } = DfirInfo.ApplicationVersion;
    /// <summary>Role of the PC the case was created on (WP18): "control" or "csirt". Empty in cases created before WP18 (read as unknown, never as control).</summary>
    public string StationRole { get; init; } = "";

    /// <summary>Why, for which period, on which systems, approved by whom (owner decision 23). Empty in cases created before WP3.</summary>
    public CaseScope Scope { get; init; } = new();
    /// <summary>While true no evidence may be marked DISPOSED (decision 11).</summary>
    public bool LegalHold { get; set; }
    /// <summary>A copy of this case record with another scope (the scope is init-only: confirming it replaces the record).</summary>
    public CaseInfo WithScope(CaseScope scope) => new()
    {
        SchemaVersion = SchemaVersion, CaseId = CaseId, Name = Name, Host = Host, User = User, CreatedAtUtc = CreatedAtUtc, Investigator = Investigator,
        Os = Os, Architecture = Architecture, Timezone = Timezone, StartTimeUtc = StartTimeUtc, EndTimeUtc = EndTimeUtc, Notes = Notes,
        CollectionMode = CollectionMode, ApplicationVersion = ApplicationVersion, Scope = scope, LegalHold = LegalHold, RetentionUntilUtc = RetentionUntilUtc,
        StationRole = StationRole,
    };

    /// <summary>Retention limit. Empty by default; the application only warns when it is exceeded, it never deletes.</summary>
    public DateTimeOffset? RetentionUntilUtc { get; set; }
}

public enum LegalBasis { Unspecified, Incident, Audit, Control }
public enum NetworkCategory { Unspecified, AirGappedNetwork, StandalonePc, Connected }
public enum ClassificationLevel { Unspecified, Classified, Unclassified }

/// <summary>Mandatory scope of a case (owner decision 23 + system category).</summary>
public sealed class CaseScope
{
    public string Purpose { get; init; } = "";
    public DateTimeOffset? PeriodFromUtc { get; init; }
    public DateTimeOffset? PeriodToUtc { get; init; }
    public List<string> SystemsInScope { get; init; } = [];
    public string Approver { get; init; } = "";
    public LegalBasis LegalBasis { get; init; }
    public NetworkCategory Network { get; init; }
    /// <summary>True when the scope was filled with placeholder values because the operator has not entered one yet (e.g. the LIVE case).
    /// A provisional scope lets work start but is never reported as confirmed.</summary>
    public bool Provisional { get; init; }
    public ClassificationLevel Classification { get; init; }
    /// <summary>Free note, e.g. that the values are provisional defaults which must be confirmed.</summary>
    public string Notes { get; init; } = "";

    /// <summary>True when the scope is complete and was entered by the operator (not the placeholder of a LIVE case, not a pre-WP3 case).</summary>
    public bool IsConfirmed => !Provisional && MissingFields().Count == 0;

    /// <summary>Names of the mandatory fields that are empty or inconsistent. Empty list = complete.</summary>
    public IReadOnlyList<string> MissingFields()
    {
        var m = new List<string>();
        if (string.IsNullOrWhiteSpace(Purpose)) m.Add("purpose");
        if (PeriodFromUtc is null) m.Add("period.from");
        if (PeriodToUtc is null) m.Add("period.to");
        else if (PeriodFromUtc is { } f && PeriodToUtc < f) m.Add("period (to precedes from)");
        if (SystemsInScope.Count(s => !string.IsNullOrWhiteSpace(s)) == 0) m.Add("systems in scope");
        if (string.IsNullOrWhiteSpace(Approver)) m.Add("approver");
        if (LegalBasis == LegalBasis.Unspecified) m.Add("legal basis");
        if (Network == NetworkCategory.Unspecified) m.Add("system category (network)");
        if (Classification == ClassificationLevel.Unspecified) m.Add("system category (classified/unclassified)");
        return m;
    }
}

/// <summary>Create refused: the case scope is incomplete. <see cref="Missing"/> lists the fields.</summary>
public sealed class CaseScopeIncompleteException(IReadOnlyList<string> missing)
    : ArgumentException("Case scope is incomplete (scop incomplet): " + string.Join(", ", missing))
{
    public IReadOnlyList<string> Missing { get; } = missing;
}

public static class DfirInfo
{
    public const string SchemaVersion = "1.0";
    public const string ApplicationVersion = "0.1.0-dfir";
}
