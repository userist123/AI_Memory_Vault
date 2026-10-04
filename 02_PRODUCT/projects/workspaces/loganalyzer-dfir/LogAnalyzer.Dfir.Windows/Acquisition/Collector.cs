using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

public sealed class CollectorContext
{
    public required CaseWorkspace Case { get; init; }
    public required bool IsElevated { get; init; }
    public IProgress<CollectorProgress>? Progress { get; init; }
    /// <summary>Live-capture duration for network collectors (spec §35, §60).</summary>
    public TimeSpan CaptureDuration { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Max bytes a capture may write (ring/multi-file limit).</summary>
    public long CaptureMaxBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public TimeSpan SnapshotInterval { get; init; } = TimeSpan.FromSeconds(10);
    public int SnapshotCount { get; init; } = 1;
}

public sealed record CollectorProgress(string Collector, string Message, int? Percent = null);

/// <summary>A collector writes raw evidence into the case and returns its audit record(s) (spec §11).</summary>
public interface ICollector
{
    string Name { get; }
    string Version { get; }
    bool RequiresElevation { get; }
    /// <summary>Profiles this collector belongs to (spec §14).</summary>
    CollectionProfile Profiles { get; }
    CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct);
}

[Flags]
public enum CollectionProfile
{
    None = 0, Quick = 1, Standard = 2, FullForensic = 4, LiveNetwork = 8,
    QuickAndUp = Quick | Standard | FullForensic,
    StandardAndUp = Standard | FullForensic,
}

public sealed class CollectorOutcome
{
    public EvidenceStatus Status { get; set; } = EvidenceStatus.Success;
    public List<EvidenceItem> Evidence { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Commands { get; } = [];
    public string Tool { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public int? ExitCode { get; set; }
    public string Source { get; set; } = "";

    public static CollectorOutcome NotAvailable(string reason, string source = "") =>
        new() { Status = EvidenceStatus.NotAvailable, Source = source, Errors = { reason } };
}
