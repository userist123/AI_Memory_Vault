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

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static Wp14Data FromJson(string json) => JsonSerializer.Deserialize<Wp14Data>(json, Opts) ?? new();

    private static readonly Lazy<Wp14Data> Shipped = new(() => FromJson(Read("airgap_lists.json")));

    /// <summary>The data shipped with the application.</summary>
    public static Wp14Data Default => Shipped.Value;

    private static string Read(string file)
    {
        var asm = typeof(Wp14Data).Assembly;
        using var s = asm.GetManifestResourceStream("LogAnalyzer.Dfir.Analysis.Data." + file)
                      ?? throw new InvalidOperationException($"Resursa încorporată {file} lipsește din {asm.GetName().Name}.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
