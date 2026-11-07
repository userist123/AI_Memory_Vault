using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// AIRGAP-PORTABLE-DEVICE (research 2026-11-07, plan P2.1): a phone, camera or player connected over MTP/PTP gets no drive letter, so neither USBSTOR nor
/// Removable Storage auditing shows it. The rule says it was connected and, plainly, that what was copied cannot be told from the logs.
/// </summary>
public sealed class PortableDeviceRuleTests
{
    private static TimelineEvent Phone(double minute, string instance = @"USB\VID_04E8&PID_6860\R58M12ABCDE", string cls = Wp14Rules.WpdClassGuid, string name = "Galaxy A52") =>
        Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, minute, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", instance), ("ClassGuid", cls), ("DriverDescription", name));

    private static List<Finding> Portable(IEnumerable<Finding> fs) => fs.Where(f => f.RuleId == "AIRGAP-PORTABLE-DEVICE").ToList();

    [Fact]
    public void A_phone_connected_over_MTP_is_reported_once_per_device_with_the_plain_limit()
    {
        var f = Assert.Single(Portable(Go([Phone(1), Phone(5)], Classified())));
        Assert.Contains("Galaxy A52", f.Title);
        Assert.Equal("T1052.001", f.MitreTechniqueId);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("VID_04E8&PID_6860 R58M12ABCDE", f.AirGap!.Destination);
        Assert.Contains("nu se poate spune din jurnale ce s-a copiat", PlainStory.Tell(f, null, TimeZoneInfo.Utc));
        Assert.Contains("nu ce s-a copiat", f.Description);
    }

    [Fact]
    public void The_portable_device_view_of_a_stick_and_other_device_classes_are_not_phones()
    {
        var stickVolume = Phone(1, @"SWD\WPDBUSENUM\_??_USBSTOR#DISK&VEN_SANDISK&PROD_CRUZER#4C53000123&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}");
        var keyboard = Phone(2, @"USB\VID_046D&PID_C31C\6&1", "{4d36e96b-e325-11ce-bfc1-08002be10318}", "Keyboard");
        Assert.Empty(Portable(Go([stickVolume, keyboard], Classified())));
    }

    [Fact]
    public void An_approved_phone_channel_authorizes_it_on_a_network_but_never_on_a_standalone_station()
    {
        Assert.Equal(Severity.Info, Assert.Single(Portable(Go([Phone(1)], Classified(), profile: ProfileWithChannel("telefon de serviciu MTP")))).Severity);
        var standalone = Assert.Single(Portable(Go([Phone(1)], Classified(NetworkCategory.StandalonePc), profile: ProfileWithChannel("telefon de serviciu MTP"))));
        Assert.NotEqual(Severity.Info, standalone.Severity);
        Assert.Contains("Standalone", standalone.AirGap!.AuthorizedBasis);
    }

    [Fact]
    public void An_MTP_driver_event_is_reported_too_and_the_rule_has_a_contract()
    {
        var mtp = Ev("Microsoft-Windows-WPD-MTPClassDriver/Operational", 1005, 3, "Microsoft-Windows-WPD-MTPClassDriver", ("DeviceId", "MTP-DEVICE-1"), ("FriendlyName", "Canon EOS"));
        Assert.Contains("Canon EOS", Assert.Single(Portable(Go([mtp], Classified()))).Title);
        Assert.NotNull(RuleContracts.For("AIRGAP-PORTABLE-DEVICE"));
    }
}
