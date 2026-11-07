using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>The independent review of 2026-11-08 found verdicts that could say more than the evidence; each case it reproduced is pinned here.</summary>
public sealed class ControlReviewFixesTests
{
    private static ControlCheck Check(StationFacts f, string id) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == id);
    private static StationFacts Facts(List<string>? media = null) => new() { Posture = StationPosture.Standalone, IsAdministrator = true, ApprovedTransferMedia = media, Host = "LAB-WS02" };

    [Theory]
    [InlineData("Card SD aprobat")]
    [InlineData("HDD portabil criptat")]
    [InlineData("Disk extern criptat")]
    [InlineData("Canal special 7")]          // a medium the application cannot place
    [InlineData("Curier: disc dur criptat")]                       // round 2: a non-storage word beside a storage one
    [InlineData("Transfer pe pendrive către rețeaua secretă")]
    [InlineData("Export scanat pe mediu de stocare criptat")]
    public void P14_never_says_no_USB_channel_for_a_storage_or_unrecognised_medium(string medium)
    {
        var f = Facts([medium]); f.Settings["USBSTOR.Start"] = "3";
        Assert.Equal(ControlStatus.DeVerificat, Check(f, "P14").Status);
    }

    [Fact]
    public void P14_is_non_conform_only_when_every_approved_channel_is_recognisably_not_a_storage_medium()
    {
        var f = Facts(["Imprimantă dedicată (hârtie)", "Rețea clasificată internă", "DVD inscripționat aprobat"]); f.Settings["USBSTOR.Start"] = "3";
        Assert.Equal(ControlStatus.Neconform, Check(f, "P14").Status);
    }

    [Fact]
    public void A20_does_not_say_Partition_Diagnostic_is_on_when_it_was_not_read()
    {
        var f = Facts(); f.Settings["DeviceLog.DriverFrameworksEnabled"] = "0";
        var c = Check(f, "A20");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.DoesNotContain("Partition/Diagnostic este pornit", c.Detail);
        Assert.Contains("nu a putut fi citită", c.Detail);
    }

    [Fact]
    public void N04_with_no_radio_and_an_unread_pairing_list_is_unknown()
    {
        var f = Facts(); f.Settings["Bluetooth.ComponentCount"] = "0"; f.Settings["Bluetooth.ServiceStart"] = "4";
        Assert.Equal(ControlStatus.Nedeterminat, Check(f, "N04").Status);
    }

    [Fact]
    public void A18_ignores_an_archive_written_by_another_computer_and_says_archives_are_taken_as_they_are()
    {
        var now = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
        var live = now.AddMonths(-2);
        StationFacts F(string computer)
        {
            var f = new StationFacts { Host = "LAB-WS02", CollectedUtc = now, PeriodStartUtc = now.AddDays(-30), PeriodEndUtc = now, IsAdministrator = true, Posture = StationPosture.Standalone,
                                       StationLevel = "SECRET", Coverage = { new ChannelCoverage("Security", live, 100, true, "") } };
            f.SecurityArchives.Add(new SecurityArchiveFact("Archive-Security-1.evtx", now.AddYears(-4), live.AddSeconds(-2), "", computer));
            return f;
        }
        var own = Check(F("LAB-WS02.lab.local"), "A18");
        Assert.Equal(ControlStatus.Conform, own.Status);
        Assert.Contains("integritatea lor nu este verificată", own.Detail);
        var other = Check(F("ALT-PC"), "A18");
        Assert.Equal(ControlStatus.DeVerificat, other.Status);
        Assert.Contains("scrise sub alt nume de calculator (ALT-PC: alt calculator sau stația înainte de o redenumire)", other.Detail);
    }

    [Fact]
    public void The_live_setupapi_log_is_always_taken_first_and_a_Windows_made_instance_is_no_serial()
    {
        var rotated = Enumerable.Range(1, 25).Select(i => $@"C:\Windows\INF\setupapi.dev.2026{i:00}01_120000.log");
        var order = SetupApiLogCollector.Order(rotated.Append(@"C:\Windows\INF\setupapi.dev.log"));
        Assert.Equal(@"C:\Windows\INF\setupapi.dev.log", order[0]);
        Assert.Equal(20, order.Count);
        Assert.Equal(@"C:\Windows\INF\setupapi.dev.20262501_120000.log", order[1]);   // then the newest rotated copy
        Assert.Equal("", SetupApiLogParser.SerialOf(@"USBSTOR\Disk&Ven_A&Prod_B&Rev_1.00\7&1a2b3c4d&0"));
        Assert.Equal("", SetupApiLogParser.SerialOf(@"SWD\WPDBUSENUM\_??_USBSTOR#Disk&Ven_A&Prod_B&Rev_1.00#7&1a2b3c4d&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}"));
        Assert.Equal("4C5300TEST01", SetupApiLogParser.SerialOf(@"USBSTOR\Disk&Ven_A&Prod_B&Rev_1.00\4C5300TEST01&0"));
    }

    [Fact]
    public void A_WPD_channel_event_without_a_device_or_for_a_stick_is_not_a_phone()
    {
        var noId = Ev("Microsoft-Windows-WPD-ClassInstaller/Operational", 24576, 1, "Microsoft-Windows-WPD-ClassInstaller", ("FriendlyName", "Ceva"));
        var stick = Ev("Microsoft-Windows-WPD-ClassInstaller/Operational", 24576, 2, "Microsoft-Windows-WPD-ClassInstaller",
            ("DeviceId", @"SWD\WPDBUSENUM\_??_USBSTOR#Disk&Ven_A&Prod_B#4C5300TEST01&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}"));
        Assert.DoesNotContain(Go([noId, stick], Classified()), f => f.RuleId == "AIRGAP-PORTABLE-DEVICE");
    }

    [Fact]
    public void A18_counts_the_archives_left_unread_apart_from_the_read_ones()
    {
        var now = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
        var f = new StationFacts { Host = "LAB-WS02", CollectedUtc = now, PeriodStartUtc = now.AddDays(-30), PeriodEndUtc = now, IsAdministrator = true, Posture = StationPosture.Standalone,
                                   StationLevel = "SECRET", Coverage = { new ChannelCoverage("Security", now.AddMonths(-2), 100, true, "") }, SecurityArchivesNotRead = 300 };
        f.SecurityArchives.Add(new SecurityArchiveFact("Archive-Security-2.evtx", now.AddYears(-1), now.AddMonths(-2).AddSeconds(-1), "", "LAB-WS02"));
        var c = Check(f, "A18");
        Assert.Contains("Pe stație există o arhivă", c.Detail);
        Assert.Contains("alte 300 arhive mai vechi nu au fost citite", c.Detail);
        Assert.DoesNotContain("nu au putut fi citite", c.Detail);
    }

    [Fact]
    public void A_Jump_List_folder_that_is_a_junction_to_another_folder_is_not_followed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ladfir_recent_" + Guid.NewGuid().ToString("N"));
        var recent = Path.Combine(root, "AppData", "Roaming", "Microsoft", "Windows", "Recent");
        var other = Path.Combine(root, "other");
        Directory.CreateDirectory(recent); Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(other, "abc.automaticDestinations-ms"), [1, 2, 3]);
        var junction = Path.Combine(recent, "AutomaticDestinations");
        var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{other}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        mk.WaitForExit();
        try
        {
            Assert.True(File.GetAttributes(junction).HasFlag(FileAttributes.ReparsePoint), "the test could not create a junction");
            var o = new CollectorOutcome();
            new RecentItemsCollector().CollectProfile(null!, "S-1-5-21-1-2-3-1001", root, o, default);   // nothing may be imported, so the case is never touched
            Assert.Empty(o.Evidence);
            Assert.Contains(o.Errors, e => e.Contains("AutomaticDestinations") && e.Contains("nu este urmată"));
        }
        finally { Directory.Delete(junction); Directory.Delete(root, true); }
    }
}