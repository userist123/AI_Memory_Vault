using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP14a item 8 (owner decision 24): the absence of observations is never "Conform" (D01 USB, N01/N02 network).</summary>
public class Wp14ControlAbsenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static StationFacts Quiet(bool isolated = true, bool partitionReadable = true, bool networkReadable = true, bool usbGap = false, bool profilesGap = false)
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = Start, PeriodEndUtc = Start.AddDays(30), CollectedUtc = Start.AddDays(30), IsAdministrator = true, StationShouldBeIsolated = isolated };
        foreach (var ch in StationFactCollector.Queries.Keys)
        {
            bool readable = ch switch
            {
                "Microsoft-Windows-Partition/Diagnostic" => partitionReadable,
                "Microsoft-Windows-NetworkProfile/Operational" or "Microsoft-Windows-WLAN-AutoConfig/Operational" => networkReadable,
                _ => true,
            };
            f.Coverage.Add(new ChannelCoverage(ch, Start.AddDays(-60), 100, readable, ""));
        }
        if (usbGap) f.Gaps.Add(new EvidenceGap("Dispozitive USB (USBSTOR)", EvidenceStatus.Failed, "acces refuzat", "x", "y", "z"));
        if (profilesGap) f.Gaps.Add(new EvidenceGap("Profiluri de rețea", EvidenceStatus.Failed, "acces refuzat", "x", "y", "z"));
        return f;
    }

    private static ControlCheck Check(ControlReport r, string id) => Assert.Single(r.Checks, c => c.Id == id);

    [Fact]
    public void D01_with_no_usb_observed_in_readable_sources_is_not_conform_and_says_what_was_not_observed()
    {
        var c = Check(ControlEvaluator.Evaluate(Quiet()), "D01");
        Assert.NotEqual(ControlStatus.Conform, c.Status);
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("nu s-a observat în sursele colectate", c.Detail);
        Assert.Contains("nu dovedește", c.Detail);
    }

    [Fact]
    public void D01_with_unreadable_sources_is_undetermined()
    {
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(Quiet(partitionReadable: false)), "D01").Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(Quiet(usbGap: true)), "D01").Status);
    }

    [Fact]
    public void D01_with_observed_devices_is_still_de_verificat()
    {
        var f = Quiet();
        f.UsbDevices.Add(new UsbDeviceFact("Disk&Ven_X", "S1", "X USB", "USBSTOR"));
        Assert.Equal(ControlStatus.DeVerificat, Check(ControlEvaluator.Evaluate(f), "D01").Status);
    }

    [Fact]
    public void N01_with_no_connection_observed_is_not_conform()
    {
        var c = Check(ControlEvaluator.Evaluate(Quiet()), "N01");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("nu s-a observat în sursele colectate", c.Detail);
        Assert.Contains("nu dovedește", c.Detail);
    }

    [Fact]
    public void N01_with_unreadable_network_sources_is_undetermined_and_a_connection_is_still_neconform()
    {
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(Quiet(networkReadable: false)), "N01").Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(Quiet(profilesGap: true)), "N01").Status);
        var f = Quiet();
        f.ConnectedInterfacesNow.Add("Ethernet 10.0.0.5");
        Assert.Equal(ControlStatus.Neconform, Check(ControlEvaluator.Evaluate(f), "N01").Status);
    }

    [Fact]
    public void N02_with_no_saved_profiles_is_not_conform()
    {
        var c = Check(ControlEvaluator.Evaluate(Quiet()), "N02");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("nu s-a observat în sursele colectate", c.Detail);
    }

    [Fact]
    public void No_check_of_the_device_and_network_areas_is_conform_when_nothing_was_observed()
    {
        var r = ControlEvaluator.Evaluate(Quiet());
        Assert.DoesNotContain(r.Checks, c => c.Area is "Dispozitive" or "Rețea" && c.Status == ControlStatus.Conform);
    }
}
