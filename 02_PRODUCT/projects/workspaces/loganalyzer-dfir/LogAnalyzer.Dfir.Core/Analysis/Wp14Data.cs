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

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static Wp14Data FromJson(string json) => JsonSerializer.Deserialize<Wp14Data>(json, Opts) ?? new();

    private static readonly Lazy<Wp14Data> Shipped = new(() => FromJson(Read("airgap_lists.json")));

    /// <summary>The data shipped with the application.</summary>
    public static Wp14Data Default => Shipped.Value;

    // the installed, verified data pack when there is one, else the embedded resource (offline updates, ANALYSIS_2026-10-11_APPLICATION_UPDATES.md)
    private static string Read(string file) => ShippedData.Read(file);
}
