using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// N03 with the wireless networks Windows remembers (research 2026-11-07, plan P2.6): names and first/last connection from NetworkList, where no password is
/// kept; the Wi-Fi profile files, which can hold keys, stay unopened. Synthetic names only.
/// </summary>
public sealed class KnownWirelessNetworksTests
{
    private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    private static byte[] SystemTime(int y, int mo, int d, int h, int mi, int s)
    {
        var b = new byte[16];
        void W(int i, int v) => BitConverter.GetBytes((ushort)v).CopyTo(b, i);
        W(0, y); W(2, mo); W(4, 0); W(6, d); W(8, h); W(10, mi); W(12, s); W(14, 0);
        return b;
    }

    private static ControlCheck N03(int adapters, string known, string lastUtc, StationPosture posture = StationPosture.Standalone)
    {
        var f = new StationFacts { Posture = posture, IsAdministrator = true, CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now };
        f.Settings["Wifi.AdapterCount"] = adapters.ToString();
        f.Settings["Wifi.UpCount"] = "0";
        f.Settings["Wifi.Adapters"] = adapters > 0 ? "Intel Wi-Fi 6" : "";
        f.Settings["Wlan.ServiceStart"] = "4";
        f.Settings["Wlan.ProfileCount"] = "0";
        f.Settings["Network.KnownWirelessCount"] = known.Length == 0 ? "0" : "1";   // one network per test (the text itself holds "; ")
        f.Settings["Network.KnownWireless"] = known;
        f.Settings["Network.KnownWirelessLastUtc"] = lastUtc;
        return ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "N03");
    }

    [Fact]
    public void The_NetworkList_time_is_local_time_and_becomes_UTC()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 13, 15, 0, TimeSpan.Zero), StationFactCollector.ParseSystemTime(SystemTime(2026, 11, 6, 15, 15, 0), Bucharest));
        Assert.Null(StationFactCollector.ParseSystemTime(SystemTime(2026, 13, 40, 0, 0, 0), Bucharest));
        Assert.Null(StationFactCollector.ParseSystemTime([1, 2, 3], Bucharest));
        Assert.Equal("„Hotspot-Test” (Wi-Fi; prima conectare 2026-10-01, ultima 2026-11-06)",
            StationFactCollector.KnownNetworkText("Hotspot-Test\n", 0x47, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero)));
        Assert.Contains("bandă largă mobilă", StationFactCollector.KnownNetworkText("Orange", 0x17, null, null));
    }

    [Fact]
    public void A_remembered_network_last_joined_in_the_period_makes_an_isolated_station_non_conform_even_without_an_adapter_now()
    {
        var c = N03(0, "„Hotspot-Test” (Wi-Fi; prima conectare 2026-10-01, ultima 2026-11-06)", "2026-11-06T08:00:00.0000000+00:00");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Hotspot-Test", c.Detail);
        Assert.Contains("Ultima conectare este din perioada controlată", c.Detail);
        Assert.Contains(c.Evidence, e => e.Contains("NetworkList") && e.Contains("o rețea"));
    }

    [Fact]
    public void An_old_remembered_network_is_to_be_checked_and_none_at_all_with_no_adapter_is_conform()
    {
        Assert.Equal(ControlStatus.DeVerificat, N03(0, "„Vechi” (Wi-Fi; prima conectare 2024-01-01, ultima 2024-02-01)", "2024-02-01T08:00:00.0000000+00:00").Status);
        Assert.Equal(ControlStatus.Conform, N03(0, "", "").Status);
    }
}
