using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// N03 names the Wi-Fi networks NetworkList remembers (research 2026-11-07, plan P2.6). The same facts feed N01/N02, which judge the connections; N03 only
/// stops calling a station with Wi-Fi history CONFORM. The Wi-Fi profile files, which can hold keys, stay unopened. Synthetic names only.
/// </summary>
public sealed class KnownWirelessNetworksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    private static ControlCheck N03(int adapters, params NetworkProfileFact[] profiles)
    {
        var f = new StationFacts { Posture = StationPosture.Standalone, IsAdministrator = true, CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now };
        f.Settings["Wifi.AdapterCount"] = adapters.ToString();
        f.Settings["Wifi.UpCount"] = "0";
        f.Settings["Wifi.Adapters"] = adapters > 0 ? "Intel Wi-Fi 6" : "";
        f.Settings["Wlan.ServiceStart"] = "4";
        f.Settings["Wlan.ProfileCount"] = "0";
        f.NetworkProfiles.AddRange(profiles);
        return ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "N03");
    }

    private static NetworkProfileFact P(string name, string kind, int daysAgo) => new(name, kind, new DateTime(2026, 1, 1), Now.AddDays(-daysAgo).LocalDateTime, "");

    [Fact]
    public void A_remembered_WiFi_network_is_named_and_a_station_with_that_history_is_not_conform()
    {
        var c = N03(0, P("Hotspot-Test", "Wi-Fi", 3), P("Birou", "cablu (Ethernet)", 1));
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("„Hotspot-Test”", c.Detail);
        Assert.DoesNotContain("Birou", c.Detail);                 // only Wi-Fi here; the wired network is N02's
        Assert.Contains("judecate la N01", c.Detail);
    }

    [Fact]
    public void No_adapter_and_no_WiFi_history_is_conform()
    {
        Assert.Equal(ControlStatus.Conform, N03(0).Status);
        Assert.Equal(ControlStatus.Conform, N03(0, P("Birou", "cablu (Ethernet)", 1)).Status);
    }
}
