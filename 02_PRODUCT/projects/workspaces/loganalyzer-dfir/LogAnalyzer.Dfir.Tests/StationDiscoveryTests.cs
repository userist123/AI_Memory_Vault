using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public sealed class StationDiscoveryTests
{
    private const string Wpd = "SWD#WPDBUSENUM#_??_USBSTOR#DISK&VEN_SANDISK&PROD_CRUZER&REV_1.00#4C530001234567&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}";

    [Fact] public void Serial_is_taken_from_wpd_key() => Assert.Equal("4C530001234567", StationDiscovery.SerialFromDeviceKey(Wpd));

    [Fact]
    public void Serial_is_null_for_other_keys()
    {
        Assert.Null(StationDiscovery.SerialFromDeviceKey("SWD#WPDBUSENUM#{guid}"));
        Assert.Null(StationDiscovery.SerialFromDeviceKey(null));
    }

    [Fact]
    public void VidPid_from_key_name()
    {
        Assert.Equal("0781:5567", StationDiscovery.VidPidFromKeyName("VID_0781&PID_5567"));
        Assert.Null(StationDiscovery.VidPidFromKeyName("ROOT_HUB30"));
    }

    [Fact]
    public void Merge_joins_label_and_vidpid_by_serial_and_adds_unmatched_mounted_volumes()
    {
        var now = DateTimeOffset.UtcNow;
        var list = StationDiscovery.MergeMedia(
            [("4C530001234567&0", "SanDisk Cruzer"), ("4C530001234567", "dup")],
            new Dictionary<string, string> { ["4c530001234567"] = "0781:5567" },
            new Dictionary<string, string> { [StationDiscovery.SerialFromDeviceKey(Wpd)!] = "1234-56SV" },
            [("1234-56SV", "USB"), ("DVD1", "CD/DVD")], now);
        Assert.Equal(2, list.Count);
        Assert.Equal(("4C530001234567", "0781:5567", "1234-56SV", "USB"), (list[0].Serial, list[0].VidPid, list[0].VolumeLabel, list[0].Type));
        Assert.Equal(("", "DVD1", "CD/DVD"), (list[1].Serial, list[1].VolumeLabel, list[1].Type));
    }

    [Fact]
    public void Service_sids_are_skipped_and_accounts_merge_by_sid()
    {
        Assert.True(StationDiscovery.IsServiceSid("S-1-5-18"));
        var m = StationDiscovery.MergeAccounts([new ObservedAccount("ion", "S-1-5-21-1", "Conturi locale", null)],
            [new ObservedAccount(@"PC\ion", "S-1-5-21-1", "Profil", null), new ObservedAccount(@"PC\ana", "S-1-5-21-2", "Profil", null)]);
        Assert.Equal(2, m.Count);
    }
}
