using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Regression tests for the independent review of the air-gap rules: second-OS false positive (WinRE) and BadUSB false positives. Synthetic data only.</summary>
public class ReviewFixAirGapTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    private const string KbGuid = "{4d36e96b-e325-11ce-bfc1-08002be10318}";
    private static int _rec;

    private const string NormalOs = """
        Windows Boot Loader
        -------------------
        identifier              {current}
        device                  partition=C:
        path                    \WINDOWS\system32\winload.efi
        description             Windows 11
        locale                  ro-RO
        osdevice                partition=C:
        systemroot              \WINDOWS
        resumeobject            {11111111-2222-3333-4444-555555555555}

        """;

    private const string Winre = """
        Windows Boot Loader
        -------------------
        identifier              {a1b2c3d4-0000-1111-2222-333344445555}
        device                  ramdisk=[\Device\HarddiskVolume4]\Recovery\WindowsRE\Winre.wim,{ramdiskoptions}
        path                    \windows\system32\winload.efi
        description             Windows Recovery Environment
        osdevice                ramdisk=[\Device\HarddiskVolume4]\Recovery\WindowsRE\Winre.wim,{ramdiskoptions}
        systemroot              \windows
        winpe                   Yes

        """;

    private const string Resume = """
        Windows Boot Loader
        -------------------
        identifier              {11111111-2222-3333-4444-555555555555}
        device                  partition=C:
        path                    \WINDOWS\system32\winresume.efi
        description             Windows Resume Application
        filedevice              partition=C:

        """;

    private const string SecondOs = """
        Windows Boot Loader
        -------------------
        identifier              {99999999-0000-0000-0000-000000000001}
        device                  partition=D:
        path                    \Windows\system32\winload.efi
        description             Alt sistem
        osdevice                partition=D:

        """;

    [Fact]
    public void A_normal_station_with_winre_and_the_resume_loader_has_one_operating_system()
    {
        var names = BootEntries.OsLoaderDescriptions(NormalOs + "\n" + Winre + "\n" + Resume);
        Assert.Equal(["Windows 11"], names);
    }

    [Fact]
    public void Two_real_operating_system_loaders_are_both_counted_even_next_to_winre()
    {
        var names = BootEntries.OsLoaderDescriptions(NormalOs + "\n" + Winre + "\n" + SecondOs);
        Assert.Equal(["Windows 11", "Alt sistem"], names);
    }

    [Fact]
    public void Multiboot_raises_nothing_on_a_default_station_and_a_finding_with_two_systems()
    {
        var one = BootEntries.OsLoaderDescriptions(NormalOs + "\n" + Winre);
        Assert.DoesNotContain(Run([], new AirGapFacts { CollectedUtc = new DateTimeOffset(T0), Virtual = new() { BootOsEntries = one.Count, BootEntryNames = one } }), f => f.RuleId == "AIRGAP-MULTIBOOT");
        var two = BootEntries.OsLoaderDescriptions(NormalOs + "\n" + Winre + "\n" + SecondOs);
        Assert.Contains(Run([], new AirGapFacts { CollectedUtc = new DateTimeOffset(T0), Virtual = new() { BootOsEntries = two.Count, BootEntryNames = two } }), f => f.RuleId == "AIRGAP-MULTIBOOT");
    }

    // ---- BadUSB ----

    private static TimelineEvent Ev(string channel, int id, double seconds, string provider, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddSeconds(seconds), "", "test"), Source = "EventLog:" + channel, EventId = id.ToString(), Provider = provider,
        EvidenceId = "EV-1", Locator = $"EventRecordID={Interlocked.Increment(ref _rec)}", Summary = "t",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Pnp(double seconds, string instance, string name, params (string K, string V)[] extra) =>
        Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, seconds, "Microsoft-Windows-Kernel-PnP", [("DeviceInstanceId", instance), ("DriverDescription", name), ("ClassGuid", KbGuid), .. extra]);

    private static TimelineEvent Shell(double seconds, string image, string user, string logonId = "0x5f3a1") =>
        Ev("Security", 4688, seconds, "Microsoft-Windows-Security-Auditing", ("NewProcessName", image), ("CommandLine", image), ("NewProcessId", "0x10"), ("SubjectUserName", user), ("SubjectLogonId", logonId));

    private static TimelineEvent Logon(double seconds, string logonId, string type) =>
        Ev("Security", 4624, seconds, "Microsoft-Windows-Security-Auditing", ("TargetLogonId", logonId), ("LogonType", type), ("TargetUserName", "utilizator-test"));

    private static List<Finding> Run(IEnumerable<TimelineEvent> events, AirGapFacts? facts = null)
    {
        int n = 0;
        if (facts is not null && facts.EvidenceId.Length == 0) facts.EvidenceId = "EV-FACTS";
        var scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified };
        return Wp14Rules.Run(events.ToList(), () => $"F-{++n:D4}", new Wp14Input { Scope = scope, Facts = facts });
    }

    private const string Cmd = @"C:\Windows\System32\cmd.exe";
    private const string Ps = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private static bool Bad(List<Finding> f) => f.Any(x => x.RuleId == "AIRGAP-BADUSB-PATTERN");

    [Fact]
    public void An_internal_keyboard_and_a_SYSTEM_console_raise_nothing()
    {
        Assert.False(Bad(Run([Pnp(1, @"ACPI\MSFT0001\3&2", "Tastatura HID I2C"), Shell(5, Cmd, "SYSTEM", "0x3e7")])));
        Assert.False(Bad(Run([Pnp(1, @"HID\ELAN0001&Col01\5&1", "Tastatura HID", ("ParentId", @"ACPI\PNP0C50\1")), Shell(5, Cmd, "utilizator-test")])));
    }

    [Fact]
    public void A_new_usb_keyboard_followed_by_a_system_service_or_non_interactive_logon_console_raises_nothing()
    {
        var kb = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000", "HID Keyboard Device");
        Assert.False(Bad(Run([kb, Shell(5, Cmd, "SYSTEM", "0x3e7")])));
        Assert.False(Bad(Run([kb, Shell(5, Cmd, "LOCAL SERVICE", "0x3e5")])));
        Assert.False(Bad(Run([kb, Shell(5, Cmd, "WKS01$", "0x3e7")])));
        Assert.False(Bad(Run([Logon(0, "0x7777", "3"), kb, Shell(5, Cmd, "utilizator-test", "0x7777")])));      // network logon
        Assert.False(Bad(Run([Logon(0, "0x7778", "5"), kb, Shell(5, Cmd, "utilizator-test", "0x7778")])));      // service logon
    }

    [Fact]
    public void A_new_usb_keyboard_and_a_user_powershell_within_30_seconds_is_still_reported()
    {
        var kb = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000", "HID Keyboard Device");
        Assert.True(Bad(Run([kb, Shell(20, Ps, "utilizator-test")])));
        Assert.True(Bad(Run([Logon(0, "0x7779", "2"), kb, Shell(20, Ps, "utilizator-test", "0x7779")])));
        Assert.False(Bad(Run([kb, Shell(40, Ps, "utilizator-test")])));                                          // 39 s later
    }

    [Fact]
    public void A_keyboard_that_was_already_present_before_is_not_new()
    {
        var first = Pnp(-3600, @"HID\VID_1234&PID_5678\7&1&0&0000", "HID Keyboard Device");
        var again = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000", "HID Keyboard Device");
        Assert.False(Bad(Run([first, again, Shell(5, Cmd, "utilizator-test")])));
    }
}
