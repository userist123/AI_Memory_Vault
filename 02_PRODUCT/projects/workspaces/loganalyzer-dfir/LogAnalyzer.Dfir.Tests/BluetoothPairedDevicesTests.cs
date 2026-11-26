using System.Text;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// N04 with the paired devices Windows remembers (research 2026-11-07, plan P2.2: BTHPORT\Parameters\Devices). A pairing on an isolated station means a
/// device was linked to it; the link keys are never read. Synthetic names and addresses only.
/// </summary>
public sealed class BluetoothPairedDevicesTests
{
    private static ControlCheck N04(StationPosture posture, string components, string service, string paired, string list = "")
    {
        var f = new StationFacts { Posture = posture, IsAdministrator = true };
        f.Settings["Bluetooth.ComponentCount"] = components;
        f.Settings["Bluetooth.Components"] = components == "0" ? "" : "Generic Bluetooth Radio";
        f.Settings["Bluetooth.ServiceStart"] = service;
        f.Settings["Bluetooth.PairedCount"] = paired;
        f.Settings["Bluetooth.Paired"] = list;
        return ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "N04");
    }

    [Fact]
    public void The_pairing_record_is_named_with_its_address_and_a_damaged_name_is_cleaned()
    {
        Assert.Equal("Telefon test (00:1A:7D:DA:71:13)", StationFactCollector.BluetoothPairedText("001a7dda7113", [.. Encoding.UTF8.GetBytes("Telefon test"), 0, 0x41]));
        Assert.Equal("00:1A:7D:DA:71:13", StationFactCollector.BluetoothPairedText("001a7dda7113", null));
        Assert.Equal("Căști (00:1A:7D:DA:71:14)", StationFactCollector.BluetoothPairedText("001A7DDA7114", [.. Encoding.UTF8.GetBytes("Căști\r\n"), 0]));
    }

    [Fact]
    public void A_device_paired_on_an_isolated_station_is_non_conform_even_with_the_service_now_disabled()
    {
        var c = N04(StationPosture.Standalone, "1", "4", "1", "Telefon test (00:1A:7D:DA:71:13)");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Telefon test (00:1A:7D:DA:71:13)", c.Detail);
        Assert.Contains("nu ce s-a transmis", c.Detail);
        Assert.Contains(c.Evidence, e => e.Contains(@"BTHPORT\Parameters\Devices → 1 dispozitive"));
    }

    [Fact]
    public void A_pairing_left_after_the_radio_was_removed_is_still_reported()
    {
        var c = N04(StationPosture.Standalone, "0", "4", "2", "A (00:00:00:00:00:01); B (00:00:00:00:00:02)");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("nu are acum componente Bluetooth", c.Detail);
    }

    [Fact]
    public void No_radio_and_no_pairing_is_conform_and_a_radio_with_the_service_disabled_and_no_pairing_stays_to_check()
    {
        Assert.Equal(ControlStatus.Conform, N04(StationPosture.Standalone, "0", "4", "0").Status);
        Assert.Equal(ControlStatus.DeVerificat, N04(StationPosture.Standalone, "1", "4", "0").Status);
    }
}
