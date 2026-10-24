using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Privacy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Second independent review: boot entries (WinRE trust, localisation), BadUSB spoofing, PII masking gaps. Synthetic data only.</summary>
public class ReviewFix2Tests
{
    // ---- boot entries ----

    private const string Sep = "-------------------";

    private static string Block(string header, params string[] lines) => header + "\n" + Sep + "\n" + string.Join("\n", lines) + "\n\n";

    private const string CurGuid = "{aaaaaaaa-0000-1111-2222-333333333333}";
    private const string ReGuid = "{bbbbbbbb-0000-1111-2222-333333333333}";

    private static string Manager(string header) => Block(header, "identifier              {9dea862c-5cdd-4e70-acc1-f32b344d4795}", "device                  partition=\\Device\\HarddiskVolume1", "description             Windows Boot Manager", "default                 " + CurGuid);
    private static string Os(string header, string guid = CurGuid, string desc = "Windows 11", string extra = "recoverysequence        " + ReGuid) =>
        Block(header, "identifier              " + guid, "device                  partition=C:", "path                    \\WINDOWS\\system32\\winload.efi", "description             " + desc, "osdevice                partition=C:", "systemroot              \\WINDOWS", extra);
    private static string Re(string header, string guid = ReGuid, string desc = "Windows Recovery Environment", string wim = "\\Recovery\\WindowsRE\\Winre.wim") =>
        Block(header, "identifier              " + guid, "device                  ramdisk=[\\Device\\HarddiskVolume4]" + wim + ",{ramdiskoptions}", "path                    \\windows\\system32\\winload.efi", "description             " + desc,
              "osdevice                ramdisk=[\\Device\\HarddiskVolume4]" + wim + ",{ramdiskoptions}", "systemroot              \\windows", "winpe                   Yes");

    [Fact]
    public void Normal_os_with_the_default_winre_is_one_entry_in_verbose_all_output()
    {
        var text = Manager("Windows Boot Manager") + Os("Windows Boot Loader") + Re("Windows Boot Loader");
        Assert.Equal(["Windows 11"], BootEntries.OsLoaderDescriptions(text));
    }

    [Fact]
    public void A_winre_in_a_non_standard_folder_is_excluded_only_through_the_recoverysequence_link()
    {
        var text = Os("Windows Boot Loader") + Re("Windows Boot Loader", wim: "\\Recovery\\custom\\Winre.wim");
        Assert.Equal(["Windows 11"], BootEntries.OsLoaderDescriptions(text));
    }

    [Fact]
    public void An_extra_winpe_ramdisk_entry_is_counted_and_named_as_such()
    {
        var toolkit = Block("Windows Boot Loader", "identifier              {cccccccc-0000-1111-2222-333333333333}", "device                  ramdisk=[D:]\\tools\\boot.wim,{ramdiskoptions}",
                            "path                    \\windows\\system32\\winload.efi", "description             Windows Recovery Environment", "osdevice                ramdisk=[D:]\\tools\\boot.wim,{ramdiskoptions}", "winpe                   Yes");
        var names = BootEntries.OsLoaderDescriptions(Os("Windows Boot Loader") + Re("Windows Boot Loader") + toolkit);
        Assert.Equal(2, names.Count);
        Assert.Contains(names, n => n.Contains("intrare de pornire WinPE/ramdisk suplimentară", StringComparison.Ordinal));
    }

    [Fact]
    public void A_renamed_second_os_that_borrows_the_recovery_description_is_still_counted()
    {
        var disguised = Block("Windows Boot Loader", "identifier              {dddddddd-0000-1111-2222-333333333333}", "device                  partition=D:", "path                    \\Windows\\system32\\winload.efi",
                              "description             Windows Recovery Environment", "osdevice                partition=D:", "systemroot              \\Windows");
        var names = BootEntries.OsLoaderDescriptions(Os("Windows Boot Loader") + Re("Windows Boot Loader") + disguised);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void Romanian_and_german_headers_give_the_same_counts()
    {
        var ro = Manager("Manager de pornire Windows") + Os("Încărcător de pornire Windows") + Re("Încărcător de pornire Windows") +
                 Os("Încărcător de pornire Windows", "{eeeeeeee-0000-1111-2222-333333333333}", "Alt sistem", "locale                  ro-RO");
        Assert.Equal(["Windows 11", "Alt sistem"], BootEntries.OsLoaderDescriptions(ro));
        var de = Manager("Windows-Start-Manager") + Os("Windows-Startladeprogramm") + Re("Windows-Startladeprogramm");
        Assert.Equal(["Windows 11"], BootEntries.OsLoaderDescriptions(de));
        var deTwo = de + Os("Windows-Startladeprogramm", "{eeeeeeee-0000-1111-2222-333333333333}", "Zweites System", "locale                  de-DE");
        Assert.Equal(2, BootEntries.OsLoaderDescriptions(deTwo).Count);
        var resume = Block("Windows-Wiederaufnahmeanwendung", "identifier              {ffffffff-0000-1111-2222-333333333333}", "device                  partition=C:", "path                    \\WINDOWS\\system32\\winresume.efi", "description             Windows Resume Application");
        Assert.Equal(["Windows 11"], BootEntries.OsLoaderDescriptions(de + resume));
    }

    // ---- BadUSB ----

    private static readonly DateTime T0 = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    private const string KbGuid = "{4d36e96b-e325-11ce-bfc1-08002be10318}";
    private static int _rec;

    private static TimelineEvent Ev(string source, string channelEvent, double seconds, string provider, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddSeconds(seconds), "", "test"), Source = source, EventId = channelEvent, Provider = provider,
        EvidenceId = "EV-1", Locator = $"EventRecordID={Interlocked.Increment(ref _rec)}", Summary = "t",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Pnp(double seconds, string instance, params (string K, string V)[] extra) =>
        Ev("EventLog:Microsoft-Windows-Kernel-PnP/Configuration", "400", seconds, "Microsoft-Windows-Kernel-PnP", [("DeviceInstanceId", instance), ("DriverDescription", "HID Keyboard Device"), ("ClassGuid", KbGuid), .. extra]);

    private static TimelineEvent PrefetchShell(double seconds, string user = "") => new()
    {
        Time = Timestamp.FromUtc(T0.AddSeconds(seconds), "", "test"), Source = "Prefetch", EvidenceId = "EV-1", Process = "CMD.EXE", User = user, Summary = "t",
        Locator = $"pf={Interlocked.Increment(ref _rec)}",
    };

    private static TimelineEvent Bam(double seconds, string user) => new()
    {
        Time = Timestamp.FromUtc(T0.AddSeconds(seconds), "", "test"), Source = "BAM", EvidenceId = "EV-1", Process = "cmd.exe", User = user, Path = @"C:\Windows\System32\cmd.exe", Summary = "t",
        Locator = $"bam={Interlocked.Increment(ref _rec)}",
    };

    private static TimelineEvent Shell(double seconds, string user) =>
        Ev("EventLog:Security", "4688", seconds, "Microsoft-Windows-Security-Auditing", ("NewProcessName", @"C:\Windows\System32\cmd.exe"), ("CommandLine", "cmd.exe"), ("NewProcessId", "0x10"), ("SubjectUserName", user), ("SubjectLogonId", "0x5f3a1"));

    private static List<Finding> Run(IEnumerable<TimelineEvent> events)
    {
        int n = 0;
        var scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified };
        return Wp14Rules.Run(events.ToList(), () => $"F-{++n:D4}", new Wp14Input { Scope = scope, Facts = null });
    }

    private static Finding? Bad(List<Finding> f) => f.FirstOrDefault(x => x.RuleId == "AIRGAP-BADUSB-PATTERN");

    [Fact]
    public void A_usb_serial_that_contains_I2C_does_not_make_the_keyboard_internal()
    {
        var kb = Pnp(1, @"USB\VID_1234&PID_5678\I2C0001");
        Assert.NotNull(Bad(Run([kb, Shell(5, "utilizator-test")])));
        var hid = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000", ("ParentDeviceInstanceId", @"USB\VID_1234&PID_5678\I2C-serial"));
        Assert.NotNull(Bad(Run([hid, Shell(5, "utilizator-test")])));
    }

    [Fact]
    public void A_keyboard_with_an_acpi_parent_under_the_Kernel_PnP_field_name_is_internal()
    {
        var kb = Pnp(1, @"HID\ELAN0001&Col01\5&1", ("ParentDeviceInstanceId", @"ACPI\PNP0C50\1"));
        Assert.Null(Bad(Run([kb, Shell(5, "utilizator-test")])));
    }

    [Fact]
    public void A_known_vid_pid_on_a_different_port_and_a_serialless_device_after_a_long_gap_are_new()
    {
        var oldPort = Pnp(-3600, @"HID\VID_1234&PID_5678\7&1&0&0000");
        var newPort = Pnp(1, @"HID\VID_1234&PID_5678\7&2&0&0000");
        Assert.NotNull(Bad(Run([oldPort, newPort, Shell(5, "utilizator-test")])));
        var longAgo = Pnp(-3 * 86400, @"HID\VID_1234&PID_5678\7&1&0&0000");
        var again = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000");
        Assert.NotNull(Bad(Run([longAgo, again, Shell(5, "utilizator-test")])));
        // a device with a real serial that is simply plugged again later is the same device
        var s1 = Pnp(-3 * 86400, @"USB\VID_1234&PID_5678\ABC123");
        var s2 = Pnp(1, @"USB\VID_1234&PID_5678\ABC123");
        Assert.Null(Bad(Run([s1, s2, Shell(5, "utilizator-test")])));
    }

    [Fact]
    public void A_prefetch_shell_needs_a_known_interactive_user_for_high_severity()
    {
        var kb = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000");
        var unknown = Bad(Run([kb, PrefetchShell(5)]));
        Assert.NotNull(unknown);
        Assert.Equal(Severity.Medium, unknown!.Severity);
        Assert.Contains("de verificat", unknown.Description, StringComparison.Ordinal);
        Assert.Null(Bad(Run([kb, PrefetchShell(5, "SYSTEM")])));
        Assert.Equal(Severity.High, Bad(Run([kb, PrefetchShell(5, "utilizator-test")]))!.Severity);
        Assert.Equal(Severity.High, Bad(Run([kb, PrefetchShell(5), Bam(5, "utilizator-test")]))!.Severity);   // the user comes from BAM
        Assert.Null(Bad(Run([kb, PrefetchShell(5), Bam(5, "SYSTEM")])));
    }

    // ---- PII masking ----

    [Fact]
    public void Ipv4_mapped_ipv6_is_one_ip_pseudonym_without_leaking_octets()
    {
        var m = new PiiMasker();
        Assert.Equal("de la IP-1 si IP-1", m.Mask("de la ::ffff:192.168.10.20 si 192.168.10.20"));
        Assert.Equal("IP-1", m.Mask("::ffff:c0a8:0a14"));
        Assert.Equal("IP-1", m.Mask("0:0:0:0:0:ffff:192.168.10.20"));
        Assert.Equal("IP-1", m.Mask(m.Mask("::FFFF:192.168.10.20")));
    }

    [Fact]
    public void Mac_addresses_are_masked_in_all_three_notations()
    {
        var m = new PiiMasker();
        var o = m.Mask("aa:bb:cc:dd:ee:ff, AA-BB-CC-DD-EE-01 si aabb.ccdd.ee02");
        Assert.DoesNotContain("aa:bb", o, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EE-01", o, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ee02", o, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("MAC-1, MAC-2 si MAC-3", o);
        Assert.Equal(o, m.Mask(o));
        Assert.Equal("10:20:30", m.Mask("10:20:30"));   // a clock time is not a MAC
    }

    [Fact]
    public void Azure_ad_sids_are_masked()
    {
        var m = new PiiMasker();
        var o = m.Mask("cont S-1-12-1-1111111111-2222222222-3333333333-4444444444 si S-1-5-21-1-2-3-1001");
        Assert.DoesNotContain("1111111111", o);
        Assert.Equal("cont SID-1 si SID-2", o);
    }

    [Theory]
    [InlineData(@"USBSTOR\Disk&Ven_Kingston&Prod_DataTraveler&Rev_1.00\Zx9Qw2Lm7&0")]
    [InlineData(@"\\?\USBSTOR#Disk&Ven_Kingston&Prod_DataTraveler&Rev_1.00#Zx9Qw2Lm7&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}")]
    [InlineData(@"USB#VID_0951&PID_1666#Zx9Qw2Lm7#{a5dcbf10-6530-11d2-901f-00c04fb951ed}")]
    public void Non_hex_usb_serials_are_masked_in_both_path_notations(string path)
    {
        var o = new PiiMasker().Mask("dispozitiv " + path);
        Assert.DoesNotContain("Zx9Qw2Lm7", o);
        Assert.Contains("Serial-1", o);
    }

    [Fact]
    public void The_registrable_domain_of_a_known_host_is_masked_not_only_the_learned_suffix()
    {
        var m = new PiiMasker().KnownHost("pc1.corp.example.com");
        var o = m.Mask("vpn.example.com si https://mail.example.com/x");
        Assert.DoesNotContain("example.com", o, StringComparison.OrdinalIgnoreCase);
        var uk = new PiiMasker().KnownHost("pc1.dept.firma.co.uk");
        Assert.DoesNotContain("firma.co.uk", uk.Mask("portal.firma.co.uk"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Public_hosts_in_TargetServerName_are_not_learned_as_domains()
    {
        var own = new TimelineEvent
        {
            Time = Timestamp.FromUtc(T0, "", "test"), Source = "EventLog:Security", EvidenceId = "EV-1", Summary = "t", Host = "pc1.corp.example.com",
            Fields = new(StringComparer.OrdinalIgnoreCase) { ["Computer"] = "pc1.corp.example.com", ["TargetServerName"] = "www.google.com" },
        };
        var m = PiiMasker.FromTimeline([own]);
        var masked = m.Mask(own);
        Assert.Equal("www.google.com", masked.Fields["TargetServerName"]);
        Assert.Equal("see mail.google.com", m.Mask("see mail.google.com"));
        Assert.DoesNotContain("example.com", m.Mask("mail.example.com"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sha1_sha256_and_md5_hashes_are_untouched()
    {
        var m = new PiiMasker();
        var md5 = "d41d8cd98f00b204e9800998ecf8427e";
        var sha1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709";
        var sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var text = $"md5 {md5} sha1 {sha1} sha256 {sha256}";
        Assert.Equal(text, m.Mask(text));
    }

    [Theory]
    [InlineData("Ion Popescu")]
    [InlineData("Maria-Elena Ionescu")]
    public void A_person_display_name_signer_label_is_pseudonymised_and_an_organisation_label_is_kept(string person)
    {
        var m = new PiiMasker();
        var o = m.MaskSignerLabel(person);
        Assert.StartsWith("Utilizator-", o);
        Assert.DoesNotContain(person.Split(' ')[0], o);
        Assert.Equal("Laborator DFIR", m.MaskSignerLabel("Laborator DFIR"));
        Assert.Equal(o, m.MaskSignerLabel(person));
        Assert.DoesNotContain(person.Split(' ')[^1], m.Mask("semnat de " + person.Split(' ')[^1]), StringComparison.Ordinal);
    }
}
