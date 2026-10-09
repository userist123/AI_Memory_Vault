using System.Text.Json;

namespace LogAnalyzer.Dfir.Analysis;

public sealed class ControlGapMediaData
{
    public int MinObservedSteps { get; set; } = 2;
    /// <summary>How long after the end of a control gap a medium still counts as "after the gap".</summary>
    public int MediaAfterGapMinutes { get; set; } = 120;
    /// <summary>Words in a POLICY-CONTROL-GAP finding that make its control a USB / device-install / audit / Defender control.</summary>
    public List<string> ControlWords { get; set; } = [];
}

public sealed class SmbStagingUsbData
{
    public int MinObservedSteps { get; set; } = 3;
    /// <summary>With a proven file-name link between the share access and the medium, the staging step may be unobserved.</summary>
    public int MinObservedStepsWithFileLink { get; set; } = 2;
    public int SmbToStagingMinutes { get; set; } = 120;
    public int StagingToMediaMinutes { get; set; } = 240;
    public List<string> IgnoredShares { get; set; } = [];
    public List<string> IgnoredStagingPathParts { get; set; } = [];
}

public sealed class PortableUsbArchiveData
{
    public int MinObservedSteps { get; set; } = 2;
    public int ExecToArchiveMinutes { get; set; } = 480;
    public int ArchiveToMediaMinutes { get; set; } = 240;
    public long ArchiveMinBytes { get; set; } = 100L * 1024 * 1024;
    public List<string> ArchiveExtensions { get; set; } = [];
    public List<string> ArchiverTools { get; set; } = [];
    public List<string> PortableTools { get; set; } = [];
    public List<string> PortableNameWords { get; set; } = [];
    public List<string> UserWritablePathParts { get; set; } = [];
}

/// <summary>
/// The data the WP14b sequence rules read (windows, size threshold, minimum steps, tool lists), from JSON embedded in this assembly
/// (<c>Analysis/Data/sequence_rules.json</c>); a caller or a test can pass its own to change what the rules know without touching code.
/// </summary>
public sealed class SequenceData
{
    public ControlGapMediaData ControlGapMedia { get; set; } = new();
    public SmbStagingUsbData SmbStagingUsb { get; set; } = new();
    public PortableUsbArchiveData PortableUsbArchive { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static SequenceData FromJson(string json) => JsonSerializer.Deserialize<SequenceData>(json, Opts) ?? new();

    private static readonly Lazy<SequenceData> Shipped = new(() => FromJson(Read("sequence_rules.json")));

    /// <summary>The data shipped with the application.</summary>
    public static SequenceData Default => Shipped.Value;

    private static string Read(string file)
    {
        var asm = typeof(SequenceData).Assembly;
        using var s = asm.GetManifestResourceStream("LogAnalyzer.Dfir.Analysis.Data." + file)
                      ?? throw new InvalidOperationException($"Resursa încorporată {file} lipsește din {asm.GetName().Name}.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
