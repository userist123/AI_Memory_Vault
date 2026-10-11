using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Station-control checks added for the second channels of an isolated station: N05 adapter kinds, P28 CD/DVD burning, P29 printing, P30 clipboard / activity
/// history, P31 camera / microphone / speakers. Pure over <see cref="StationFacts"/> (no system access); the collector smoke test reads this PC read-only and asserts structure only.
/// </summary>
public class AirGapStationChecksTests
{
    private const string Absent = StationFactCollector.Absent;

    private static ControlCheck Check(bool isolated, string id, params (string K, string V)[] settings)
    {
        var f = new StationFacts { StationShouldBeIsolated = isolated };
        foreach (var (k, v) in settings) f.Settings[k] = v;
        return ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == id);
    }

    // ---- N05 ----

    [Fact]
    public void N05_phone_or_cellular_adapter_on_an_isolated_station_is_non_compliant_and_names_it()
    {
        var c = Check(true, "N05", ("Net.AdapterCount", "3"), ("Net.PhoneAdapters", "Remote NDIS based Internet Sharing Device"), ("Net.CellularAdapters", ""), ("Net.UsbEthernetAdapters", ""));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("telefon folosit ca modem", c.Detail);
        Assert.Contains("Remote NDIS", c.Detail);
        Assert.Equal(ControlStatus.DeVerificat, Check(false, "N05", ("Net.AdapterCount", "3"), ("Net.PhoneAdapters", "x"), ("Net.CellularAdapters", ""), ("Net.UsbEthernetAdapters", "")).Status);
    }

    [Fact]
    public void N05_usb_ethernet_is_to_be_verified_and_no_such_adapter_is_compliant_and_unread_is_undetermined()
    {
        Assert.Equal(ControlStatus.DeVerificat, Check(true, "N05", ("Net.AdapterCount", "2"), ("Net.UsbEthernetAdapters", "ASIX USB Ethernet")).Status);
        Assert.Equal(ControlStatus.Conform, Check(true, "N05", ("Net.AdapterCount", "2"), ("Net.PhoneAdapters", ""), ("Net.CellularAdapters", ""), ("Net.UsbEthernetAdapters", "")).Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(true, "N05").Status);
    }

    // ---- P28 ----

    [Fact]
    public void P28_a_writer_blocked_only_in_Explorer_is_non_compliant_with_the_explanation()
    {
        var c = Check(true, "P28", ("Optical.WriterCount", "1"), ("Optical.WriterNames", "DVD-RW test"), ("Optical.DriveCount", "1"), ("Optical.NoCDBurningMachine", "1"), ("Optical.DenyWrite", Absent), ("Optical.DenyAll", Absent), ("Optical.NoCDBurningUser", Absent));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("doar inscripționarea din Explorer", c.Detail);
        Assert.Contains("Deny_Write", c.Recommendation);
    }

    [Fact]
    public void P28_deny_write_or_no_writer_is_compliant_and_a_writer_with_no_block_on_a_non_isolated_station_is_to_verify()
    {
        Assert.Equal(ControlStatus.Conform, Check(true, "P28", ("Optical.WriterCount", "1"), ("Optical.WriterNames", "W"), ("Optical.DenyWrite", "1")).Status);
        Assert.Equal(ControlStatus.Conform, Check(true, "P28", ("Optical.WriterCount", "0"), ("Optical.DriveCount", "1")).Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(false, "P28", ("Optical.WriterCount", "1"), ("Optical.WriterNames", "W"), ("Optical.DenyWrite", Absent)).Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(true, "P28").Status);
    }

    // ---- P29 ----

    [Fact]
    public void P29_print_log_off_with_printers_on_an_isolated_station_is_non_compliant_and_network_printer_too()
    {
        var off = Check(true, "P29", ("Print.LogEnabled", "0"), ("Print.PrinterCount", "2"), ("Print.PrinterNames", "A; B"), ("Print.NetworkPrinters", ""));
        Assert.Equal(ControlStatus.Neconform, off.Status);
        Assert.Contains("OPRIT", off.Detail);
        var net = Check(true, "P29", ("Print.LogEnabled", "1"), ("Print.PrinterCount", "2"), ("Print.NetworkPrinters", "Imprimanta de rețea"));
        Assert.Equal(ControlStatus.Neconform, net.Status);
        Assert.Contains("imprimante de rețea", net.Detail);
    }

    [Fact]
    public void P29_log_on_without_network_printers_or_no_printers_is_compliant_and_unread_log_is_undetermined()
    {
        Assert.Equal(ControlStatus.Conform, Check(true, "P29", ("Print.LogEnabled", "1"), ("Print.PrinterCount", "1"), ("Print.PrinterNames", "A"), ("Print.NetworkPrinters", "")).Status);
        Assert.Equal(ControlStatus.Conform, Check(true, "P29", ("Print.LogEnabled", "0"), ("Print.PrinterCount", "0")).Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(true, "P29", ("Print.PrinterCount", "2")).Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(false, "P29", ("Print.LogEnabled", "0"), ("Print.PrinterCount", "2")).Status);
    }

    // ---- P30 ----

    [Fact]
    public void P30_clipboard_history_on_is_non_compliant_default_activity_history_is_to_verify_and_both_off_is_compliant()
    {
        Assert.Equal(ControlStatus.Neconform, Check(true, "P30", ("Clipboard.HistoryUser", "1"), ("Clipboard.HistoryPolicy", Absent), ("Activity.FeedPolicy", Absent), ("Activity.CachePresent", "nu")).Status);
        var def = Check(true, "P30", ("Clipboard.HistoryUser", Absent), ("Clipboard.HistoryPolicy", Absent), ("Activity.FeedPolicy", Absent), ("Activity.CachePresent", "da"));
        Assert.Equal(ControlStatus.DeVerificat, def.Status);
        Assert.Contains("ActivitiesCache.db", def.Detail);
        Assert.Equal(ControlStatus.Conform, Check(true, "P30", ("Clipboard.HistoryUser", "1"), ("Clipboard.HistoryPolicy", "0"), ("Activity.FeedPolicy", "0"), ("Activity.CachePresent", "da")).Status);
        Assert.Equal(ControlStatus.Conform, Check(true, "P30", ("Clipboard.HistoryUser", Absent), ("Clipboard.HistoryPolicy", Absent), ("Activity.FeedPolicy", Absent), ("Activity.CachePresent", "nu")).Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(true, "P30").Status);
    }

    // ---- P31 ----

    [Fact]
    public void P31_an_active_camera_without_the_deny_policy_on_an_isolated_station_is_non_compliant_and_refers_to_P19_and_M09()
    {
        var c = Check(true, "P31", ("Hardware.Cameras", "1"), ("Hardware.CameraNames", "Integrated Camera"), ("Hardware.Microphones", "0"), ("Hardware.Speakers", "1"), ("Privacy.Webcam", "Allow"), ("Privacy.Microphone", "Deny"));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("M09", c.Detail);
        Assert.Contains("Integrated Camera", c.Detail);
    }

    [Fact]
    public void P31_denied_by_policy_is_to_verify_no_devices_is_compliant_and_unread_is_undetermined()
    {
        var denied = Check(true, "P31", ("Hardware.Cameras", "1"), ("Hardware.Microphones", "1"), ("Hardware.Speakers", "0"), ("Privacy.Webcam", "Deny"), ("Privacy.Microphone", "Deny"));
        Assert.Equal(ControlStatus.DeVerificat, denied.Status);
        Assert.Contains("rămân pornite în driver", denied.Detail);
        Assert.Equal(ControlStatus.Conform, Check(true, "P31", ("Hardware.Cameras", "0"), ("Hardware.Microphones", "0"), ("Hardware.Speakers", "0")).Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(true, "P31").Status);
    }

    [Fact]
    public void Every_new_check_has_a_guide_so_the_screen_and_the_annex_can_explain_it()
    {
        foreach (var id in new[] { "N05", "P28", "P29", "P30", "P31" })
        {
            Assert.Contains(id, CheckGuides.Ids);
            var g = CheckGuides.For(id);
            Assert.True(g.What.Length > 20 && g.Why.Length > 20 && g.How.Length > 20 && g.Meaning.Length > 20);
        }
    }

    // ---- collector: read-only smoke test on this PC; structure only, nothing about the machine is asserted ----

    [Fact]
    public void The_facts_collector_reads_this_station_without_throwing_and_reports_failed_reads_instead_of_hiding_them()
    {
        var f = AirGapFactsCollector.Read(false, null);
        Assert.True(f.CollectedUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.All(f.Errors, e => Assert.Contains(":", e));
        Assert.Equal(12, f.StepMs.Count);   // every read is timed, so a slow one is visible
        Assert.All(f.Adapters, a => Assert.Equal("Net", a.PnpClass));
        // the JSON the collector stores is the JSON the pipeline reads
        Assert.Equal(f.Adapters.Count, AirGapFacts.FromJson(f.ToJson()).Adapters.Count);
    }

    [Fact]
    public void Seven_zip_binary_history_is_decoded_as_nul_separated_utf16_strings()
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes("E:\\arhive\0F:\\alt\0");
        Assert.Equal(["E:\\arhive", "F:\\alt"], AirGapFactsCollector.Decode(bytes));
        Assert.Equal(["E:\\x"], AirGapFactsCollector.Decode("E:\\x"));
        Assert.Empty(AirGapFactsCollector.Decode(null));
    }
}
