using System.Reflection;
using System.Text.Json;

namespace LogAnalyzer.Dfir.Analysis;

public sealed class BurnToolLists
{
    public List<string> Executables { get; set; } = [];
    public List<string> ImapiProviders { get; set; } = [];
}

public sealed class OpticalLists
{
    public List<string> DeviceWords { get; set; } = [];
    public List<string> ImageExtensions { get; set; } = [];
    public List<string> MountChannelWords { get; set; } = [];
}

public sealed class AirGapNetworkLists
{
    public List<string> WifiKeywords { get; set; } = [];
    public List<string> BluetoothKeywords { get; set; } = [];
    public List<string> NetworkKeywords { get; set; } = [];
    public List<string> VirtualAdapterWords { get; set; } = [];
    public string NetClassGuid { get; set; } = "";
    public List<string> BluetoothClassGuids { get; set; } = [];
    public List<string> IgnoredNetworkNamePrefixes { get; set; } = [];
}

/// <summary>Lists and thresholds of the second-channel rules (BadUSB, SRUM upload, printing, virtual machines, archivers). Defaults are in code so a data file without this section still works.</summary>
public sealed class ChannelLists
{
    public List<string> ShellImages { get; set; } = ["cmd.exe", "powershell.exe", "pwsh.exe", "powershell_ise.exe", "wscript.exe", "cscript.exe", "mshta.exe"];
    public List<string> KeyboardClassGuids { get; set; } = ["{4d36e96b-e325-11ce-bfc1-08002be10318}"];
    public List<string> KeyboardWords { get; set; } = ["keyboard", "tastatur"];
    /// <summary>Seconds between a new keyboard and a shell start for the pair to be reported (D11).</summary>
    public int BadUsbSeconds { get; set; } = 30;
    /// <summary>Bytes sent by one application in one day above which it is an upload outlier (D14); and how many times its own median day.</summary>
    public long SrumUploadBytesPerDay { get; set; } = 100L * 1024 * 1024;
    public double SrumMedianFactor { get; set; } = 5;
    /// <summary>Days of history an application needs before its own median means anything.</summary>
    public int SrumMinDays { get; set; } = 3;
    public int SrumMaxFindings { get; set; } = 10;
    public List<string> PrintToFileDriverWords { get; set; } = ["pdf", "xps", "onenote", "print to file", "document writer", "fax"];
    public List<string> PrintToFilePortWords { get; set; } = ["file:", "portprompt:", ".pdf", ".xps", "nul:"];
    public List<string> DiskImageExtensions { get; set; } = [".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow2", ".avhdx"];
    public List<string> BurnLogHints { get; set; } = ["imgburn", "burn", "write", "disc", "drive"];
}

/// <summary>One weighted text pattern of CLICKFIX-RUNMRU (regular expression, case-insensitive) with the plain-Romanian name shown in the finding.</summary>
public sealed class WeightedPattern
{
    public string Regex { get; set; } = "";
    public int Weight { get; set; } = 1;
    public string Label { get; set; } = "";
}

/// <summary>
/// Lists of the P1 payload rules (MEDIA-LNK-PAYLOAD, LNK-ARGS-PADDED, MEDIA-HIDDEN-MAILBOX, MEDIA-SAME-DROP, EXEC-FROM-REMOVABLE, PERSIST-STARTUP-FOLDER,
/// CLICKFIX-RUNMRU). Only numbers and neutral words have defaults in code: program names and command patterns live in the data file, so the published
/// assembly does not carry attack-like strings (security research 2026-10-12, backlog item 7). A data pack without this section gets the embedded one.
/// </summary>
public sealed class PayloadLists
{
    /// <summary>Program names (file name, lower case) that a shortcut or a Run-dialog command should not start on its own: interpreters and signed proxies.</summary>
    public List<string> Lolbins { get; set; } = [];
    /// <summary>Extensions of files that run when opened (scripts, programs, libraries).</summary>
    public List<string> RunnableExtensions { get; set; } = [];
    /// <summary>Extensions of plain data files (documents, archives, images) counted in a hidden folder.</summary>
    public List<string> DataExtensions { get; set; } = [];
    /// <summary>File names in a Startup folder that Windows itself writes.</summary>
    public List<string> StartupBenignNames { get; set; } = [];
    /// <summary>Weighted patterns of a pasted command (CLICKFIX-RUNMRU); a value is reported when the weights add up to <see cref="ClickFixMinWeight"/>.</summary>
    public List<WeightedPattern> ClickFixPatterns { get; set; } = [];
    public int ClickFixMinWeight { get; set; } = 2;
    /// <summary>Minutes after the Run-dialog entry in which a PowerShell 4104 or a process start counts as its consequence.</summary>
    public int ClickFixMinutes { get; set; } = 5;
    /// <summary>LNK-ARGS-PADDED: leading invisible characters, or the share of invisible characters in an argument field of at least that many.</summary>
    public int PaddingMinRun { get; set; } = 20;
    public double PaddingRatio { get; set; } = 0.8;
    /// <summary>MEDIA-HIDDEN-MAILBOX: days before the collection in which a data file counts as recently changed; the count from which the finding is High.</summary>
    public int MailboxRecentDays { get; set; } = 30;
    public int MailboxHighCount { get; set; } = 10;
    /// <summary>MEDIA-SAME-DROP: smaller files are not compared (empty or tiny files repeat by chance).</summary>
    public long SameDropMinBytes { get; set; } = 1024;
    /// <summary>PERSIST-STARTUP-FOLDER: days before the collection in which a Startup entry counts as new.</summary>
    public int StartupNewDays { get; set; } = 30;
}

/// <summary>
/// The data the WP14a rules read: the 19 subcategories of the AIR-GAP INTEGRITY category, optical-media and burn-tool lists, and the network keywords
/// used to match a profile's transfer channels. Loaded from JSON embedded in this assembly (<c>Analysis/Data/airgap_lists.json</c>); a caller or a test can
/// pass its own to change what the rules know without touching code.
/// </summary>
public sealed class Wp14Data
{
    public List<string> Subcategories { get; set; } = [];
    public BurnToolLists BurnTools { get; set; } = new();
    public OpticalLists Optical { get; set; } = new();
    public AirGapNetworkLists Network { get; set; } = new();
    public ChannelLists Channels { get; set; } = new();
    public PayloadLists Payload { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static Wp14Data FromJson(string json) => JsonSerializer.Deserialize<Wp14Data>(json, Opts) ?? new();

    // a data pack written before a section existed (for example "payload") gets that section from the embedded file instead of an empty list
    private static readonly Lazy<Wp14Data> Shipped = new(() => FromJson(Wp11Data.WithMissingSections(Read("airgap_lists.json"), ShippedData.ReadEmbedded("airgap_lists.json"))));

    /// <summary>The data shipped with the application.</summary>
    public static Wp14Data Default => Shipped.Value;

    // the installed, verified data pack when there is one, else the embedded resource (offline updates, ANALYSIS_2026-10-11_APPLICATION_UPDATES.md)
    private static string Read(string file) => ShippedData.Read(file);
}
