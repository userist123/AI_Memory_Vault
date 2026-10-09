using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP14a items 6 and 7: NIC / Wi-Fi / Bluetooth / DHCP on an air-gapped or standalone scope, in the "Air-gap integrity" category.</summary>
public class Wp14AirGapTests
{
    private static readonly string[] AirGapRules = ["AIRGAP-NETWORK-CONNECTED", "AIRGAP-WIFI-ASSOCIATED", "AIRGAP-BLUETOOTH-PAIRED", "AIRGAP-DHCP-LEASE", "AIRGAP-NIC-ADDED"];

    private static TimelineEvent NetUp(double m, string name = "Rețea-X") => Ev("Microsoft-Windows-NetworkProfile/Operational", 10000, m, "Microsoft-Windows-NetworkProfile", ("Name", name), ("Category", "1"));
    private static TimelineEvent NetDown(double m, string name = "Rețea-X") => Ev("Microsoft-Windows-NetworkProfile/Operational", 10001, m, "Microsoft-Windows-NetworkProfile", ("Name", name));
    private static TimelineEvent Wifi(double m, int id = 8001, string ssid = "Hotspot-Telefon") => Ev("Microsoft-Windows-WLAN-AutoConfig/Operational", id, m, "Microsoft-Windows-WLAN-AutoConfig", ("SSID", ssid));
    private static TimelineEvent Dhcp(double m, int id = 50036, params (string, string)[] f) => Ev("Microsoft-Windows-Dhcp-Client/Operational", id, m, "Microsoft-Windows-Dhcp-Client", f);
    private static TimelineEvent Bt(double m, string id = @"BTHENUM\DEV_001A7DDA7113\7&1&0&BLUETOOTHDEVICE_001A7DDA7113") =>
        Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, m, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", id));
    private static TimelineEvent Nic(double m, string id = @"PCI\VEN_8086&DEV_15BB\3&11583659&0&FE", string guid = "{4d36e972-e325-11ce-bfc1-08002be10318}", string name = "Intel Ethernet Connection") =>
        Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, m, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", id), ("ClassGuid", guid), ("DriverDescription", name));

    private static Finding Only(List<Finding> f, string rule) => Assert.Single(f, x => x.RuleId == rule);
    private static List<Finding> Air(List<Finding> f) => f.Where(x => x.RuleId.StartsWith("AIRGAP-", StringComparison.Ordinal)).ToList();

    [Fact]
    public void Every_rule_fires_on_an_air_gapped_scope_in_the_category_with_a_subcategory_from_the_list()
    {
        var events = new[] { NetUp(1), Wifi(2), Bt(3), Dhcp(4, 50036, ("IPAddress", "192.168.1.9")), Nic(5) };
        var f = Air(Go(events, Classified()));
        Assert.Equal(AirGapRules.OrderBy(x => x), f.Select(x => x.RuleId).OrderBy(x => x));
        foreach (var x in f)
        {
            Assert.Equal("Air-gap integrity", x.Category);
            Assert.Contains(x.AirGap!.Subcategory, Wp14Data.Default.Subcategories);
            Assert.NotEmpty(x.SupportingEvidence);
        }
    }

    [Fact]
    public void Standalone_pc_is_in_scope_too()
    {
        Assert.Equal(5, Air(Go(new[] { NetUp(1), Wifi(2), Bt(3), Dhcp(4), Nic(5) }, Classified(NetworkCategory.StandalonePc))).Count);
    }

    [Theory]
    [InlineData(NetworkCategory.Connected)]
    [InlineData(NetworkCategory.Unspecified)]
    public void On_a_connected_or_unspecified_scope_no_air_gap_rule_fires(NetworkCategory n)
    {
        Assert.Empty(Air(Go(new[] { NetUp(1), Wifi(2), Bt(3), Dhcp(4), Nic(5) }, Classified(n))));
    }

    [Fact]
    public void Negative_cases_nothing_observed_nothing_reported_and_no_conform_style_finding()
    {
        Assert.Empty(Go([], Classified()));
        // identification-only network names, a disconnect alone, a failed Wi-Fi attempt alone, a non-network PnP device
        var quiet = new[]
        {
            NetUp(1, "Identifying..."), NetDown(2), Wifi(3, 11000), Wifi(4, 11002),
            Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, 5, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"HID\VID_046D&PID_C52B\7&1"), ("ClassGuid", "{745a17a0-74d3-11d0-b6fe-00a0c90f57da}")),
            Ev("Microsoft-Windows-Kernel-PnP/Configuration", 410, 6, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"PCI\VEN_8086&DEV_15BB\3&1"), ("ClassGuid", "{4d36e972-e325-11ce-bfc1-08002be10318}")),
        };
        Assert.Empty(Air(Go(quiet, Classified())));
    }

    // ---- authorization from the profile ----

    [Fact]
    public void Without_a_profile_connectivity_is_observed_and_requires_explanation()
    {
        var x = Only(Go([NetUp(1)], Classified()), "AIRGAP-NETWORK-CONNECTED");
        Assert.Equal(AirGapAuthorization.Undefined, x.AirGap!.Authorized);
        Assert.Contains("conectivitate observată, necesită explicație", x.Description);
        Assert.Equal(Severity.High, x.Severity);
    }

    [Fact]
    public void A_connection_to_an_authorized_destination_is_Info_and_one_to_another_network_is_not()
    {
        var p = ProfileWithDestinations(("Rețea-X", "ROSU"), ("10.20.0.0/16", "ROSU"));
        var ok = Only(Go([NetUp(1, "Rețea-X")], Classified(), profile: p), "AIRGAP-NETWORK-CONNECTED");
        Assert.Equal(Severity.Info, ok.Severity);
        Assert.Equal(AirGapAuthorization.Authorized, ok.AirGap!.Authorized);
        Assert.Contains("ROSU", ok.AirGap.AuthorizedBasis);
        var bad = Only(Go([NetUp(1, "Rețea-Y")], Classified(), profile: p), "AIRGAP-NETWORK-CONNECTED");
        Assert.Equal(Severity.High, bad.Severity);
        Assert.Equal(AirGapAuthorization.NotAuthorized, bad.AirGap!.Authorized);
    }

    [Fact]
    public void A_dhcp_lease_inside_an_authorized_cidr_is_Info_and_outside_it_is_not()
    {
        var p = ProfileWithDestinations(("10.20.0.0/16", "ROSU"));
        var inside = Only(Go([Dhcp(1, 50036, ("IPAddress", "10.20.4.7"))], Classified(), profile: p), "AIRGAP-DHCP-LEASE");
        Assert.Equal(Severity.Info, inside.Severity);
        var outside = Only(Go([Dhcp(1, 50036, ("IPAddress", "192.168.1.9"))], Classified(), profile: p), "AIRGAP-DHCP-LEASE");
        Assert.Equal(Severity.Medium, outside.Severity);
        Assert.Contains("192.168.1.9", outside.Description);
    }

    [Fact]
    public void An_approved_transfer_channel_of_the_matching_medium_authorizes_the_channel()
    {
        var wifiChannel = ProfileWithChannel("Wi-Fi dedicat");
        Assert.Equal(Severity.Info, Only(Go([Wifi(1)], Classified(), profile: wifiChannel), "AIRGAP-WIFI-ASSOCIATED").Severity);
        Assert.Equal(Severity.High, Only(Go([Wifi(1)], Classified(), profile: ProfileWithChannel("USB marcat")), "AIRGAP-WIFI-ASSOCIATED").Severity);
        Assert.Equal(Severity.Info, Only(Go([Bt(1)], Classified(), profile: ProfileWithChannel("Bluetooth")), "AIRGAP-BLUETOOTH-PAIRED").Severity);
    }

    // ---- severities ----

    [Fact]
    public void Severity_on_a_classified_scope_and_one_level_lower_on_an_unclassified_one()
    {
        var events = new[] { NetUp(1), Wifi(2), Bt(3), Dhcp(4), Nic(5) };
        var hi = Air(Go(events, Classified())).ToDictionary(x => x.RuleId, x => x.Severity);
        var lo = Air(Go(events, Unclassified())).ToDictionary(x => x.RuleId, x => x.Severity);
        Assert.Equal(Severity.High, hi["AIRGAP-NETWORK-CONNECTED"]);
        Assert.Equal(Severity.High, hi["AIRGAP-WIFI-ASSOCIATED"]);
        Assert.Equal(Severity.Medium, hi["AIRGAP-BLUETOOTH-PAIRED"]);
        Assert.Equal(Severity.Medium, hi["AIRGAP-DHCP-LEASE"]);
        Assert.Equal(Severity.Medium, hi["AIRGAP-NIC-ADDED"]);
        foreach (var k in hi.Keys) Assert.Equal(hi[k] - 1, lo[k]);
    }

    // ---- individual sources ----

    [Fact]
    public void Network_connection_reports_the_observed_duration_when_the_disconnect_was_recorded()
    {
        var x = Only(Go([NetUp(0), NetDown(17)], Classified()), "AIRGAP-NETWORK-CONNECTED");
        Assert.Contains("17 min", x.Description);
        Assert.Equal("Rețea-X", x.AirGap!.Destination);
    }

    [Fact]
    public void Wifi_association_is_8001_or_11001_or_11005_and_names_the_ssid_and_counts_attempts()
    {
        var f = Go([Wifi(1, 11000), Wifi(2, 11001), Wifi(3, 11002, "Alta")], Classified());
        var x = Only(f, "AIRGAP-WIFI-ASSOCIATED");
        Assert.Contains("Hotspot-Telefon", x.Title);
        Assert.Equal("WIRELESS", x.AirGap!.Subcategory);
        Assert.DoesNotContain("Alta", x.Title);              // a failed attempt to another network is not an association
        Assert.Equal(1, f.Count(y => y.RuleId == "AIRGAP-WIFI-ASSOCIATED"));
    }

    [Fact]
    public void Bluetooth_device_node_is_a_pairing_signal_and_a_bluetooth_radio_is_an_added_adapter_not_a_pairing()
    {
        var radio = Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, 1, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"USB\VID_8087&PID_0A2B\5&2"), ("ClassGuid", "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}"), ("DriverDescription", "Intel Wireless Bluetooth"));
        var f = Go([radio], Classified());
        Assert.DoesNotContain(f, x => x.RuleId == "AIRGAP-BLUETOOTH-PAIRED");
        var nic = Only(f, "AIRGAP-NIC-ADDED");
        Assert.Equal("BLUETOOTH", nic.AirGap!.Subcategory);
        var paired = Only(Go([Bt(2)], Classified()), "AIRGAP-BLUETOOTH-PAIRED");
        Assert.Equal("BLUETOOTH", paired.AirGap!.Subcategory);
        Assert.Contains("001A7DDA7113", paired.Description);
    }

    [Fact]
    public void Dhcp_ids_50036_50037_50065_1103_are_read_and_the_addresses_are_quoted_not_invented()
    {
        foreach (var id in new[] { 50036, 50037, 50065, 1103 })
            Assert.Single(Air(Go([Dhcp(1, id, ("IPAddress", "10.0.0.5"), ("DhcpServer", "10.0.0.1"))], Classified())));
        var x = Only(Go([Dhcp(1, 50036)], Classified()), "AIRGAP-DHCP-LEASE");
        Assert.Contains("adresa nu este în eveniment", x.Description);
        Assert.Empty(Air(Go([Dhcp(1, 50099)], Classified())));
    }

    [Fact]
    public void A_new_physical_adapter_is_Medium_and_a_virtual_one_is_lower()
    {
        var phys = Only(Go([Nic(1)], Classified()), "AIRGAP-NIC-ADDED");
        Assert.Equal(Severity.Medium, phys.Severity);
        Assert.Equal("NETWORK INTERFACE", phys.AirGap!.Subcategory);
        var virt = Only(Go([Nic(1, @"ROOT\NET\0000", name: "Hyper-V Virtual Ethernet Adapter")], Classified()), "AIRGAP-NIC-ADDED");
        Assert.Equal(Severity.Low, virt.Severity);
        Assert.Contains("virtual", virt.Description);
    }

    // ---- the fields every air-gap finding carries ----

    [Fact]
    public void Every_air_gap_finding_carries_channel_authorization_observation_time_who_object_classification_direction_destination_and_evidence()
    {
        var events = new[] { NetUp(1), Wifi(2), Bt(3), Dhcp(4, 50036, ("IPAddress", "192.168.1.9")), Nic(5) };
        foreach (var x in Air(Go(events, Classified())))
        {
            var d = x.AirGap!;
            Assert.NotEmpty(d.Channel); Assert.NotEmpty(d.AuthorizedBasis); Assert.NotEmpty(d.Observed); Assert.NotNull(d.WhenUtc); Assert.NotEmpty(d.Who);
            Assert.NotEmpty(d.ObjectName); Assert.Contains("clasificat", d.CaseClassification); Assert.NotEmpty(d.RegisterClassification);
            Assert.NotEmpty(d.TransferDirection); Assert.NotEmpty(d.Destination); Assert.NotEmpty(d.Evidence);
            Assert.StartsWith("necunoscut", d.Who);               // none of these events names an account
        }
    }

    [Fact]
    public void The_findings_make_no_stronger_claim_than_their_evidence()
    {
        var events = new[] { NetUp(1), Wifi(2), Bt(3), Dhcp(4), Nic(5), Usb("AA001") };
        foreach (var x in Go(events, Classified(), W14.Media(Row("OTHER"))))
        {
            Assert.Equal(Classification.Direct, x.Classification);
            Assert.NotEmpty(x.AlternativeExplanations);
            Assert.DoesNotContain("exfiltr", x.Description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("conform", x.Description.Replace("neconform", ""), StringComparison.OrdinalIgnoreCase);
        }
    }
}
