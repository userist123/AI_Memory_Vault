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
}

public static class DfirInfo
{
    public const string SchemaVersion = "1.0";
    public const string ApplicationVersion = "0.1.0-dfir";
}
