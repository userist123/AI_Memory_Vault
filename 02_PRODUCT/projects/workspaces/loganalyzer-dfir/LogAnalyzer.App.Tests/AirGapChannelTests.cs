using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Second-channel rules of the air-gap category (adapter kinds, optical/burn, printing, screenshots and clipboard, BadUSB, virtual machines, archivers,
/// per-application upload outliers, camera / microphone, round-trip media, medium lifecycle), each with a synthetic positive and a benign negative case.
/// Synthetic facts only: no real machine, user, path or serial. Lives in App.Tests because Dfir.Tests is not built in this worktree (Defender).
/// </summary>
public class AirGapChannelTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset P0 = new(T0, TimeSpan.Zero);
    private const string NetGuid = "{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string KbGuid = "{4d36e96b-e325-11ce-bfc1-08002be10318}";
    private static int _rec;

    private static TimelineEvent Ev(string channel, int id, double minute, string provider = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = "EventLog:" + channel, EventId = id.ToString(), Provider = provider,
        EvidenceId = "EV-1", Locator = $"EventRecordID={Interlocked.Increment(ref _rec)}", Summary = "t",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Row(string source, double minute, string process = "", string path = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = source, EvidenceId = "EV-2", Locator = $"row={Interlocked.Increment(ref _rec)}",
        Summary = "t", Process = process, Path = path, Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Pnp(double minute, string instance, string name, string classGuid) =>
        Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, minute, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", instance), ("DriverDescription", name), ("ClassGuid", classGuid));

    private static TimelineEvent Usb(string serial, double minute, string letter = "") =>
        Row("USB", minute, f: [("Serial", serial), ("VidPid", "VID_0951&PID_1666"), ("FriendlyName", "Stick de test"), ("DriveLetter", letter)]);

    private static CaseScope Classified(NetworkCategory n = NetworkCategory.AirGappedNetwork) => new() { Network = n, Classification = ClassificationLevel.Classified };

    private static List<Finding> Go(IEnumerable<TimelineEvent> events, AirGapFacts? facts = null, CaseScope? scope = null, MediaRegister? media = null, string zone = "")
    {
        int n = 0;
        facts ??= null;
        if (facts is not null && facts.EvidenceId.Length == 0) facts.EvidenceId = "EV-FACTS";
        return Wp14Rules.Run(events.ToList(), () => $"F-{++n:D4}", new Wp14Input { Scope = scope ?? Classified(), Media = media, SystemZone = zone, Facts = facts });
    }

    private static Finding One(List<Finding> f, string rule) => Assert.Single(f, x => x.RuleId == rule);
    private static void None(List<Finding> f, string rule) => Assert.DoesNotContain(f, x => x.RuleId == rule);

    private static MediaRegister Media(params string[][] rows) { var r = new MediaRegister(); r.SetCells(rows); return r; }
    // RegistrationNumber, Serial, VidPid, Type, Classification, AssignedUser, Zone, ValidFrom, ValidTo, Status, Notes
    private static string[] Reg(string serial, string zone = "", string status = "active", string from = "2026-01-01", string to = "2026-12-31") => ["M-" + serial, serial, "", "USB", "", "", zone, from, to, status, ""];

    // ---- D4: adapter kinds ----

    [Theory]
    [InlineData(@"USB\VID_04E8&PID_6863\0123", "Remote NDIS based Internet Sharing Device", "telefon folosit ca modem", Severity.High, "PHONE")]
    [InlineData(@"USB\VID_1199&PID_9071\0123", "Mobile Broadband Adapter", "modem celular", Severity.High, "NETWORK INTERFACE")]
    [InlineData(@"USB\VID_0B95&PID_1790\0123", "ASIX AX88179 USB 3.0 to Gigabit Ethernet Adapter", "placă Ethernet pe USB", Severity.Medium, "NETWORK INTERFACE")]
    public void An_adapter_event_names_the_kind_and_raises_the_severity_for_phone_and_cellular(string instance, string name, string kindText, Severity sev, string sub)
    {
        var f = One(Go([Pnp(1, instance, name, NetGuid)]), "AIRGAP-NIC-ADDED");
        Assert.Contains(kindText, f.Title);
        Assert.Contains(kindText, f.Description);
        Assert.Equal(sev, f.Severity);
        Assert.Equal(sub, f.AirGap!.Subcategory);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10EC&DEV_8168\3&1", "Realtek PCIe GbE Family Controller")]
    [InlineData(@"ROOT\NET\0000", "Hyper-V Virtual Ethernet Adapter")]
    public void A_pci_or_virtual_adapter_is_not_named_as_phone_or_modem(string instance, string name)
    {
        var f = One(Go([Pnp(1, instance, name, NetGuid)]), "AIRGAP-NIC-ADDED");
        Assert.DoesNotContain("modem", f.Description);
        Assert.DoesNotContain("telefon", f.Description);
        Assert.True(f.Severity <= Severity.Medium);
    }

    [Fact]
    public void The_facts_inventory_reports_a_phone_adapter_installed_in_the_period_but_not_an_old_one_or_a_virtual_one()
    {
        var scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified, PeriodFromUtc = P0.AddDays(-1), PeriodToUtc = P0.AddDays(1) };
        var facts = new AirGapFacts
        {
            CollectedUtc = P0,
            Adapters =
            [
                new() { Name = "Remote NDIS based Internet Sharing Device", InstanceId = @"USB\VID_04E8&PID_6863\A", FirstInstallUtc = P0 },
                new() { Name = "Mobile Broadband Adapter", InstanceId = @"USB\VID_1199&PID_9071\B", FirstInstallUtc = P0.AddYears(-2) },
                new() { Name = "Hyper-V Virtual Ethernet Adapter", InstanceId = @"ROOT\VMS_MP\0", FirstInstallUtc = P0 },
                new() { Name = "Realtek PCIe GbE Family Controller", InstanceId = @"PCI\VEN_10EC&DEV_8168\1", FirstInstallUtc = P0 },
            ],
        };
        var f = One(Go([], facts, scope), "AIRGAP-ADAPTER-KIND");
        Assert.Contains("telefon folosit ca modem", f.Title);
        Assert.Equal("PHONE", f.AirGap!.Subcategory);
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void An_adapter_already_reported_from_the_event_log_is_not_reported_twice()
    {
        var inst = @"USB\VID_04E8&PID_6863\A";
        var facts = new AirGapFacts { CollectedUtc = P0, Adapters = [new() { Name = "RNDIS", InstanceId = inst }] };
        var f = Go([Pnp(1, inst, "Remote NDIS based Internet Sharing Device", NetGuid)], facts);
        One(f, "AIRGAP-NIC-ADDED");
        None(f, "AIRGAP-ADAPTER-KIND");
    }

    [Fact]
    public void Adapter_classifier_does_not_take_realtek_for_lte_and_knows_the_rndis_interface_class()
    {
        Assert.Equal(AdapterKind.Physical, AdapterKinds.Classify("Realtek PCIe GbE Family Controller", @"PCI\VEN_10EC&DEV_8168\1", [], "rt640x64", "Net", []).Kind);
        Assert.Equal(AdapterKind.PhoneTethering, AdapterKinds.Classify("USB Device", @"USB\VID_1234&PID_5678\1", [@"USB\Class_E0&SubClass_01&Prot_03"], "", "Net", []).Kind);
        Assert.Equal(AdapterKind.Cellular, AdapterKinds.Classify("Intel XMM LTE", @"USB\VID_8087&PID_0A1\1", [], "", "Net", []).Kind);
    }

    // ---- D7: optical ----

    private static AirGapFacts BurnFacts(Action<BurnFacts> set)
    {
        var f = new AirGapFacts { CollectedUtc = P0 };
        set(f.Burn);
        return f;
    }

    [Fact]
    public void A_writer_with_only_Explorer_burning_blocked_is_warned_as_insufficient()
    {
        var facts = BurnFacts(b => { b.NoCdBurningMachine = 1; b.CdDvdDenyWrite = false; b.Writers.Add(new() { Name = "Unitate DVD-RW de test", Drive = "D:", CanWrite = true }); });
        var f = One(Go([], facts), "AIRGAP-OPTICAL-WRITE-UNBLOCKED");
        Assert.Contains("doar inscripționarea din Explorer", f.Description);
        Assert.Contains("Deny_Write", f.Description);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal("OPTICAL MEDIA", f.AirGap!.Subcategory);
    }

    [Fact]
    public void A_denied_write_policy_or_a_read_only_drive_raises_no_unblocked_finding()
    {
        None(Go([], BurnFacts(b => { b.CdDvdDenyWrite = true; b.Writers.Add(new() { Name = "W", CanWrite = true }); })), "AIRGAP-OPTICAL-WRITE-UNBLOCKED");
        None(Go([], BurnFacts(b => { b.CdDvdDenyWrite = false; b.Writers.Add(new() { Name = "Cititor", CanWrite = false }); })), "AIRGAP-OPTICAL-WRITE-UNBLOCKED");
    }

    [Fact]
    public void Files_waiting_in_the_burn_staging_folder_are_reported_and_an_empty_folder_is_not()
    {
        var scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified, PeriodFromUtc = P0.AddDays(-1), PeriodToUtc = P0.AddDays(1) };
        var facts = BurnFacts(b => { b.StagingFolderExists = true; b.StagingFiles.Add(new() { Name = "raport.docx", Size = 2048, LastWriteUtc = P0 }); b.StagingFiles.Add(new() { Name = "vechi.docx", Size = 1, LastWriteUtc = P0.AddYears(-1) }); });
        var f = One(Go([], facts, scope), "MEDIA-OPTICAL-STAGING");
        Assert.Contains("raport.docx", f.Description);
        Assert.DoesNotContain("vechi.docx", f.Description);
        Assert.Contains("Nu arată dacă discul a fost scris", f.Description);
        None(Go([], BurnFacts(b => b.StagingFolderExists = true), scope), "MEDIA-OPTICAL-STAGING");
    }

    [Fact]
    public void A_burn_tool_log_is_reported_with_its_excerpt_only_when_in_the_period()
    {
        var scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified, PeriodFromUtc = P0.AddDays(-1), PeriodToUtc = P0.AddDays(1) };
        var facts = BurnFacts(b =>
        {
            b.ToolLogs.Add(new() { Path = @"C:\ImgBurn\Log Files\ImgBurn.log", LastWriteUtc = P0, Excerpt = ["Destination: D: (DVD-RW)", "Operation Successful!"] });
            b.ToolLogs.Add(new() { Path = @"C:\ImgBurn\Log Files\old.log", LastWriteUtc = P0.AddYears(-1), Excerpt = ["x"] });
        });
        var f = One(Go([], facts, scope), "MEDIA-OPTICAL-TOOL-LOG");
        Assert.Contains("ImgBurn.log", f.Title);
        Assert.Contains("Operation Successful", f.Description);
    }

    // ---- D8: printing ----

    private static TimelineEvent Print307(double minute, string doc, string printer, string port = "USB001", string pages = "2", string user = "utilizator-test") =>
        Ev("Microsoft-Windows-PrintService/Operational", 307, minute, "Microsoft-Windows-PrintService", ("Param1", "5"), ("Param2", doc), ("Param3", user), ("Param4", "STATIE"), ("Param5", printer), ("Param6", port), ("Param7", "1000"), ("Param8", pages));

    [Fact]
    public void Printed_documents_are_grouped_by_printer_with_document_user_and_pages()
    {
        var f = Go([Print307(1, "plan.docx", "Imprimanta A"), Print307(2, "lista.xlsx", "Imprimanta A", pages: "3")]);
        var job = One(f, "AIRGAP-PRINT-JOB");
        Assert.Contains("plan.docx", job.Description);
        Assert.Contains("lista.xlsx", job.Description);
        Assert.Contains("5 pagini", job.Description);
        Assert.Contains("utilizator-test", job.Description);
        Assert.Equal("PRINT/SCAN", job.AirGap!.Subcategory);
        Assert.Equal(Severity.Low, job.Severity);
    }

    [Fact]
    public void Printing_to_a_pdf_printer_is_named_as_a_file_output_and_rated_higher()
    {
        var f = One(Go([Print307(1, "secret.docx", "Microsoft Print to PDF", "PORTPROMPT:")]), "AIRGAP-PRINT-JOB");
        Assert.Contains("scrie un fișier", f.Description);
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void No_print_events_no_print_job_finding()
    {
        None(Go([Ev("Security", 4624, 1)]), "AIRGAP-PRINT-JOB");
    }

    [Fact]
    public void A_disabled_print_log_with_printers_and_a_network_printer_are_reported_but_an_enabled_log_is_not()
    {
        var facts = new AirGapFacts { CollectedUtc = P0, Print = new() { OperationalLogEnabled = false, Printers = [new() { Name = "Imprimanta de rețea", Port = "IP_10.0.0.9", IsNetwork = true }, new() { Name = "Locală", Port = "USB001" }] } };
        var f = Go([], facts);
        Assert.Contains("oprit", One(f, "AIRGAP-PRINT-UNLOGGED").Description);
        Assert.Contains("IP_10.0.0.9", One(f, "AIRGAP-PRINT-NETWORK-PRINTER").Description);
        var ok = Go([], new AirGapFacts { CollectedUtc = P0, Print = new() { OperationalLogEnabled = true, Printers = [new() { Name = "Locală", Port = "USB001" }] } });
        None(ok, "AIRGAP-PRINT-UNLOGGED"); None(ok, "AIRGAP-PRINT-NETWORK-PRINTER");
        None(Go([], new AirGapFacts { CollectedUtc = P0, Print = new() { OperationalLogEnabled = false } }), "AIRGAP-PRINT-UNLOGGED");   // no printers: nothing to log
    }

    // ---- D9: screenshots, clipboard ----

    [Fact]
    public void Screenshots_in_the_period_are_reported_as_a_possible_second_channel_and_old_ones_are_not()
    {
        var scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified, PeriodFromUtc = P0.AddDays(-1), PeriodToUtc = P0.AddDays(1) };
        var facts = new AirGapFacts { CollectedUtc = P0, Screen = new() { Screenshots = [new() { Path = @"C:\Pictures\Screenshots\captura1.png", CreatedUtc = P0 }, new() { Path = @"C:\Pictures\Screenshots\veche.png", CreatedUtc = P0.AddYears(-1) }] } };
        var f = One(Go([], facts, scope), "AIRGAP-SCREENSHOTS");
        Assert.Contains("captura1.png", f.Description);
        Assert.DoesNotContain("veche.png", f.Description);
        Assert.Contains("al doilea canal", f.Description);
        None(Go([], new AirGapFacts { CollectedUtc = P0, Screen = new() { Screenshots = [new() { Path = @"C:\x\veche.png", CreatedUtc = P0.AddYears(-1) }] } }, scope), "AIRGAP-SCREENSHOTS");
    }

    [Fact]
    public void Clipboard_history_on_or_default_activity_history_is_reported_and_both_off_is_not()
    {
        var on = One(Go([], new AirGapFacts { CollectedUtc = P0, Screen = new() { ClipboardHistoryUser = 1 } }), "AIRGAP-CLIPBOARD-TIMELINE");
        Assert.Contains("clipboard este pornit", on.Description);
        Assert.Contains("Timeline", One(Go([], new AirGapFacts { CollectedUtc = P0, Screen = new() { ActivitiesCachePresent = true } }), "AIRGAP-CLIPBOARD-TIMELINE").Description);
        None(Go([], new AirGapFacts { CollectedUtc = P0, Screen = new() { ClipboardHistoryUser = 1, ClipboardHistoryPolicy = 0, ActivitiesCachePresent = true, ActivityFeedPolicy = 0 } }), "AIRGAP-CLIPBOARD-TIMELINE");
    }

    // ---- D11: BadUSB ----

    private static TimelineEvent Shell4688(double minute, string image) =>
        Ev("Security", 4688, minute, "Microsoft-Windows-Security-Auditing", ("NewProcessName", image), ("CommandLine", image), ("NewProcessId", "0x10"), ("SubjectUserName", "utilizator-test"));

    [Fact]
    public void A_new_keyboard_followed_by_a_shell_within_30_seconds_is_reported_with_the_honest_alternatives()
    {
        var kb = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000", "HID Keyboard Device", KbGuid);
        var f = One(Go([kb, Shell4688(1.33, @"C:\Windows\System32\cmd.exe")]), "AIRGAP-BADUSB-PATTERN");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal("T1200", f.MitreTechniqueId);
        Assert.Contains("nu dovedește", f.Description);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("KVM"));
        Assert.Equal(2, f.SupportingEvidence.Count);
    }

    [Fact]
    public void A_prefetch_shell_start_counts_too()
    {
        var kb = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000", "HID Keyboard Device", KbGuid);
        One(Go([kb, Row("Prefetch", 1.2, "POWERSHELL.EXE", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]), "AIRGAP-BADUSB-PATTERN");
    }

    [Fact]
    public void A_shell_long_after_the_keyboard_a_builtin_ps2_keyboard_or_a_mouse_raise_nothing()
    {
        var kb = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000", "HID Keyboard Device", KbGuid);
        None(Go([kb, Shell4688(3, @"C:\Windows\System32\cmd.exe")]), "AIRGAP-BADUSB-PATTERN");                                   // 120 s later
        None(Go([Pnp(1, @"ACPI\PNP0303\4&1", "Standard PS/2 Keyboard", KbGuid), Shell4688(1.1, @"C:\Windows\System32\cmd.exe")]), "AIRGAP-BADUSB-PATTERN");
        None(Go([Pnp(1, @"HID\VID_1234&PID_0001\7&1", "HID-compliant mouse", "{4d36e96f-e325-11ce-bfc1-08002be10318}"), Shell4688(1.1, @"C:\Windows\System32\cmd.exe")]), "AIRGAP-BADUSB-PATTERN");
        None(Go([kb, Shell4688(1.1, @"C:\Windows\System32\notepad.exe")]), "AIRGAP-BADUSB-PATTERN");
    }

    // ---- D12: virtual machines, second OS ----

    [Fact]
    public void Virtual_machines_wsl_and_hypervisor_products_are_reported_as_a_possible_second_channel()
    {
        var facts = new AirGapFacts { CollectedUtc = P0, Virtual = new() { HyperVMachines = ["VM-test"], WslDistributions = ["Distro-test"], Hypervisors = ["Oracle VirtualBox 7"] } };
        var f = One(Go([], facts), "AIRGAP-VM-PRESENT");
        Assert.Contains("VM-test", f.Description);
        Assert.Contains("Distro-test", f.Description);
        Assert.Contains("VirtualBox", f.Description);
        Assert.Contains("al doilea sistem", f.Description);
    }

    [Fact]
    public void Containers_virtual_networks_and_bridge_drivers_are_reported_and_a_bridge_driver_alone_is_named_as_a_path_to_the_physical_network()
    {
        var facts = new AirGapFacts { CollectedUtc = P0, Virtual = new()
        {
            ContainerEngines = ["Docker Desktop", "serviciul Docker Engine"], Containers = ["container-test"],
            VirtualNetworks = ["comutator virtual Hyper-V: Comutator-test"], BridgeDrivers = ["VMware Bridge Protocol"],
        } };
        var f = One(Go([], facts), "AIRGAP-VM-PRESENT");
        Assert.Contains("Docker Desktop", f.Description);
        Assert.Contains("container-test", f.Description);
        Assert.Contains("Comutator-test", f.Description);
        Assert.Contains("rețeaua fizică", f.Description);
        // a virtual network alone (no machine, no engine) is still a nested network on an isolated station
        Assert.Contains("Comutator-test", One(Go([], new AirGapFacts { CollectedUtc = P0, Virtual = new() { VirtualNetworks = ["comutator virtual Hyper-V: Comutator-test"] } }), "AIRGAP-VM-PRESENT").Description);
    }

    [Fact]
    public void Disk_images_and_a_second_boot_entry_are_reported_and_one_entry_or_nothing_is_not()
    {
        var facts = new AirGapFacts { CollectedUtc = P0, Virtual = new() { DiskImages = [new() { Path = @"D:\VMs\masina.vhdx", Size = 40_000_000_000, LastWriteUtc = P0 }], BootOsEntries = 2, BootEntryNames = ["Windows 11", "Alt sistem"] } };
        var f = Go([], facts);
        Assert.Contains("masina.vhdx", One(f, "AIRGAP-VM-DISK-IMAGE").Description);
        Assert.Equal(Severity.High, One(f, "AIRGAP-MULTIBOOT").Severity);
        var clean = Go([], new AirGapFacts { CollectedUtc = P0, Virtual = new() { BootOsEntries = 1, BootEntryNames = ["Windows 11"] } });
        None(clean, "AIRGAP-MULTIBOOT"); None(clean, "AIRGAP-VM-PRESENT"); None(clean, "AIRGAP-VM-DISK-IMAGE");
    }

    [Fact]
    public void The_boot_configuration_text_is_read_into_one_description_per_windows_boot_loader()
    {
        const string bcd = "Windows Boot Loader\n-------------------\nidentifier              {current}\ndevice                  partition=C:\ndescription             Windows 11\nlocale                  ro-RO\n\n" +
                           "Windows Boot Loader\n-------------------\nidentifier              {abc}\ndescription             Alt sistem\n";
        Assert.Equal(["Windows 11", "Alt sistem"], BootEntries.OsLoaderDescriptions(bcd));
        Assert.Empty(BootEntries.OsLoaderDescriptions("Windows Boot Manager\n-------------------\nidentifier {bootmgr}\n"));
    }

    // ---- D13: archivers ----

    [Fact]
    public void Archiver_history_paths_on_a_removable_letter_are_reported_and_fixed_drive_paths_are_not()
    {
        var facts = new AirGapFacts
        {
            CollectedUtc = P0, RemovableLetters = ["E"],
            Archivers = [new() { Tool = "WinRAR", List = "ArcHistory", Value = @"E:\transfer\documente.rar" }, new() { Tool = "7-Zip", List = "FolderHistory", Value = @"E:\" }, new() { Tool = "WinRAR", List = "ArcHistory", Value = @"C:\Users\x\lucru.zip" }],
        };
        var f = One(Go([], facts), "MEDIA-ARCHIVER-REMOVABLE");
        Assert.Contains("documente.rar", f.Description);
        Assert.DoesNotContain("lucru.zip", f.Description);
        Assert.Contains("nu arată dacă arhiva a fost creată sau doar deschisă", f.Description);
        Assert.Equal("", f.MitreTechniqueId);
        None(Go([], new AirGapFacts { CollectedUtc = P0, RemovableLetters = ["E"], Archivers = [new() { Tool = "WinRAR", List = "ArcHistory", Value = @"C:\lucru.zip" }] }), "MEDIA-ARCHIVER-REMOVABLE");
        None(Go([], new AirGapFacts { CollectedUtc = P0, RemovableLetters = [], Archivers = [new() { Tool = "WinRAR", List = "ArcHistory", Value = @"E:\x.rar" }] }), "MEDIA-ARCHIVER-REMOVABLE");
    }

    [Fact]
    public void A_drive_letter_known_removable_from_the_usb_history_also_counts()
    {
        var facts = new AirGapFacts { CollectedUtc = P0, Archivers = [new() { Tool = "7-Zip", List = "CopyHistory", Value = @"F:\arhiva.7z" }] };
        One(Go([Usb("AA001", 1, "F:")], facts), "MEDIA-ARCHIVER-REMOVABLE");
    }

    // ---- D14: SRUM upload outliers ----

    private static TimelineEvent Srum(int day, string app, long bytes, int hour = 12) => new()
    {
        Time = Timestamp.FromUtc(T0.Date.AddDays(day).AddHours(hour), "", "test"), Source = "SRUM", EvidenceId = "EV-3", Locator = $"srum={Interlocked.Increment(ref _rec)}",
        Summary = "t", Path = app, Process = System.IO.Path.GetFileName(app), Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["BytesSent"] = bytes.ToString() },
    };

    private const long MB = 1024 * 1024;

    [Fact]
    public void A_day_far_above_the_applications_own_median_and_the_threshold_is_an_outlier()
    {
        List<TimelineEvent> ev = [Srum(0, @"C:\App\sync.exe", 10 * MB), Srum(1, @"C:\App\sync.exe", 12 * MB), Srum(2, @"C:\App\sync.exe", 9 * MB), Srum(3, @"C:\App\sync.exe", 11 * MB), Srum(4, @"C:\App\sync.exe", 400 * MB)];
        var f = One(Go(ev), "AIRGAP-UPLOAD-OUTLIER");
        Assert.Contains("sync.exe", f.Title);
        Assert.Contains("nu arată destinația", f.Description);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("Actualizări"));
        Assert.Equal("", f.MitreTechniqueId);
    }

    [Fact]
    public void A_steady_high_volume_application_a_small_volume_and_a_short_history_are_not_outliers()
    {
        None(Go([.. Enumerable.Range(0, 5).Select(d => Srum(d, @"C:\App\backup.exe", 300 * MB))]), "AIRGAP-UPLOAD-OUTLIER");                      // always high: not above its own median
        None(Go([Srum(0, @"C:\App\a.exe", MB), Srum(1, @"C:\App\a.exe", 2 * MB), Srum(2, @"C:\App\a.exe", 3 * MB), Srum(3, @"C:\App\a.exe", 50 * MB)]), "AIRGAP-UPLOAD-OUTLIER");   // below the threshold
        None(Go([Srum(0, @"C:\App\b.exe", 200 * MB), Srum(1, @"C:\App\b.exe", MB)]), "AIRGAP-UPLOAD-OUTLIER");                                    // 2 days only: needs 5 x the threshold
        One(Go([Srum(0, @"C:\App\b.exe", 600 * MB), Srum(1, @"C:\App\b.exe", MB)]), "AIRGAP-UPLOAD-OUTLIER");
    }

    [Fact]
    public void Upload_outliers_are_capped_and_the_cap_is_stated()
    {
        var ev = new List<TimelineEvent>();
        for (int a = 0; a < 15; a++)
        {
            var app = $@"C:\App\app{a:D2}.exe";
            ev.AddRange([Srum(0, app, MB), Srum(1, app, MB), Srum(2, app, MB), Srum(3, app, (300 + a) * MB)]);
        }
        var f = Go(ev).Where(x => x.RuleId == "AIRGAP-UPLOAD-OUTLIER").ToList();
        Assert.Equal(11, f.Count);   // 10 detailed + 1 summary
        Assert.Contains(f, x => x.Title.StartsWith("Alte 5 zile"));
        Assert.Contains("app14.exe", f[0].Title);   // the biggest first
    }

    // ---- D15: camera, microphone, speakers ----

    [Fact]
    public void An_active_camera_and_microphone_without_a_blocking_policy_are_reported_at_medium_and_at_low_when_the_policy_denies()
    {
        HardwareFact H(string name, string cls, bool on = true) => new() { Name = name, PnpClass = cls, InstanceId = @"USB\VID_1\" + name, Enabled = on };
        var exposed = new AirGapFacts { CollectedUtc = P0, Hardware = [H("Integrated Camera", "Camera"), H("Microphone Array (test)", "AudioEndpoint"), H("Speakers (test)", "AudioEndpoint")] };
        var f = One(Go([], exposed), "AIRGAP-COVERT-HARDWARE");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("Integrated Camera", f.Description);
        Assert.Contains("M09", f.Description);
        exposed.WebcamPolicy = "Deny"; exposed.MicrophonePolicy = "Deny";
        var denied = One(Go([], exposed), "AIRGAP-COVERT-HARDWARE");
        Assert.Equal(Severity.Low, denied.Severity);
        Assert.Contains("P19", denied.Description);
    }

    [Fact]
    public void Disabled_devices_and_a_scanner_do_not_count()
    {
        HardwareFact H(string name, string cls, bool on) => new() { Name = name, PnpClass = cls, InstanceId = @"USB\" + name, Enabled = on };
        None(Go([], new AirGapFacts { CollectedUtc = P0, Hardware = [H("Integrated Camera", "Camera", false), H("Microphone (test)", "AudioEndpoint", false)] }), "AIRGAP-COVERT-HARDWARE");
        None(Go([], new AirGapFacts { CollectedUtc = P0, Hardware = [H("Flatbed Scanner", "Image", true)] }), "AIRGAP-COVERT-HARDWARE");
        Assert.Equal(HardwareKind.Camera, HardwareKinds.Of(new HardwareFact { Name = "USB Webcam", PnpClass = "Image" }));
        Assert.Equal(HardwareKind.Sound, HardwareKinds.Of(new HardwareFact { Name = "High Definition Audio Controller", PnpClass = "MEDIA" }));
    }

    // ---- B10: round-trip media ----

    [Fact]
    public void A_medium_of_a_restricted_zone_connected_while_the_station_was_on_a_network_is_flagged()
    {
        var events = new List<TimelineEvent>
        {
            Ev("Microsoft-Windows-NetworkProfile/Operational", 10000, 0, "Microsoft-Windows-NetworkProfile", ("Name", "Rețea-X")),
            Usb("AA001", 5),
            Ev("Microsoft-Windows-NetworkProfile/Operational", 10001, 30, "Microsoft-Windows-NetworkProfile", ("Name", "Rețea-X")),
        };
        var f = One(Go(events, media: Media(Reg("AA001", "ROSU")), zone: "ROSU"), "MEDIA-ZONE-ROUNDTRIP");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal("T1091", f.MitreTechniqueId);
        Assert.Contains("Rețea-X", f.Description);
        Assert.Contains("nu arată ce s-a copiat", f.Description);
    }

    [Fact]
    public void A_zone_mismatch_alone_is_medium_and_the_same_zone_or_no_network_is_not_flagged()
    {
        var f = One(Go([Usb("AA001", 5)], media: Media(Reg("AA001", "ROSU")), zone: "PUBLIC"), "MEDIA-ZONE-ROUNDTRIP");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal("", f.MitreTechniqueId);
        None(Go([Usb("AA001", 5)], media: Media(Reg("AA001", "ROSU")), zone: "ROSU"), "MEDIA-ZONE-ROUNDTRIP");
        // connected after the network went down: outside the window
        None(Go([Ev("Microsoft-Windows-NetworkProfile/Operational", 10000, 0, "x", ("Name", "Rețea-X")), Ev("Microsoft-Windows-NetworkProfile/Operational", 10001, 10, "x", ("Name", "Rețea-X")), Usb("AA001", 60)],
                media: Media(Reg("AA001", "ROSU")), zone: "ROSU"), "MEDIA-ZONE-ROUNDTRIP");
        // a medium with no zone and no level has nothing to carry across
        None(Go([Usb("AA001", 5)], media: Media(Reg("AA001")), zone: "PUBLIC"), "MEDIA-ZONE-ROUNDTRIP");
    }

    // ---- B11: medium lifecycle (history through the rules, live through the device assessment) ----

    [Theory]
    [InlineData("withdrawn", "retras")]
    [InlineData("destroyed", "distrus")]
    public void A_withdrawn_or_destroyed_medium_seen_in_the_history_is_unauthorized_and_says_why(string status, string word)
    {
        var f = One(Go([Usb("AA001", 5)], media: Media(Reg("AA001", status: status))), "MEDIA-UNAUTHORIZED");
        Assert.Contains(word, f.Description);
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void A_medium_seen_after_its_validity_ended_is_unauthorized_and_a_valid_one_is_authorized()
    {
        var expired = One(Go([Usb("AA001", 5)], media: Media(Reg("AA001", from: "2025-01-01", to: "2025-12-31"))), "MEDIA-UNAUTHORIZED");
        Assert.Contains("în afara perioadei de valabilitate", expired.Description);
        var ok = Go([Usb("AA001", 5)], media: Media(Reg("AA001")));
        None(ok, "MEDIA-UNAUTHORIZED");
        One(ok, "MEDIA-AUTHORIZED");
    }

    [Theory]
    [InlineData("withdrawn", RegisterMatch.Withdrawn)]
    [InlineData("destroyed", RegisterMatch.Withdrawn)]
    [InlineData("active", RegisterMatch.Registered)]
    public void The_live_assessment_flags_withdrawn_and_destroyed_media_and_expired_validity(string status, RegisterMatch expected)
    {
        var d = new DeviceRecord { Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\DISK&VEN_X&PROD_Y\AA001&0", Serial = "AA001", ConnectedNow = true };
        var a = DeviceRules.Assess(d, Media(Reg("AA001", status: status)), new DateOnly(2026, 10, 1), true);
        Assert.Equal(expected, a.Match);
        Assert.Equal(expected != RegisterMatch.Registered, a.NeedsAttention);
        var exp = DeviceRules.Assess(d, Media(Reg("AA001", to: "2026-06-30")), new DateOnly(2026, 10, 1), true);
        Assert.Equal(RegisterMatch.Expired, exp.Match);
        Assert.True(exp.NeedsAttention);
    }

    // ---- contract: every new rule is in the catalog, fires without overclaiming, uses one of the 19 subcategories and never exceeds High ----

    [Fact]
    public void Every_new_rule_has_a_contract_and_findings_pass_the_overclaim_check()
    {
        var facts = new AirGapFacts
        {
            CollectedUtc = P0, RemovableLetters = ["E"], WebcamPolicy = "Allow",
            Adapters = [new() { Name = "Remote NDIS based Internet Sharing Device", InstanceId = @"USB\VID_04E8&PID_6863\A" }],
            Burn = new() { NoCdBurningMachine = 1, CdDvdDenyWrite = false, Writers = [new() { Name = "W", CanWrite = true }], StagingFiles = [new() { Name = "a.doc", Size = 1, LastWriteUtc = P0 }], ToolLogs = [new() { Path = @"C:\l\ImgBurn.log", LastWriteUtc = P0, Excerpt = ["Destination: D:"] }] },
            Print = new() { OperationalLogEnabled = false, Printers = [new() { Name = "Rețea", Port = "IP_1", IsNetwork = true }] },
            Screen = new() { Screenshots = [new() { Path = @"C:\s\a.png", CreatedUtc = P0 }], ClipboardHistoryUser = 1 },
            Virtual = new() { HyperVMachines = ["VM"], DiskImages = [new() { Path = @"D:\a.vhdx", Size = 1 }], BootOsEntries = 2, BootEntryNames = ["A", "B"] },
            Archivers = [new() { Tool = "WinRAR", List = "ArcHistory", Value = @"E:\a.rar" }],
            Hardware = [new() { Name = "Integrated Camera", PnpClass = "Camera", InstanceId = @"USB\C" }],
        };
        var events = new List<TimelineEvent>
        {
            Print307(1, "d.docx", "Imprimanta A"), Pnp(2, @"HID\VID_1234&PID_5678\7&1", "HID Keyboard Device", KbGuid), Shell4688(2.2, @"C:\Windows\System32\cmd.exe"),
            Srum(0, @"C:\A\s.exe", MB), Srum(1, @"C:\A\s.exe", MB), Srum(2, @"C:\A\s.exe", MB), Srum(3, @"C:\A\s.exe", 500 * MB), Usb("AA001", 5),
        };
        var found = Go(events, facts, media: Media(Reg("AA001", "ROSU")), zone: "PUBLIC");
        string[] rules = ["AIRGAP-ADAPTER-KIND", "AIRGAP-BADUSB-PATTERN", "AIRGAP-COVERT-HARDWARE", "AIRGAP-PRINT-JOB", "AIRGAP-PRINT-NETWORK-PRINTER", "AIRGAP-PRINT-UNLOGGED", "AIRGAP-SCREENSHOTS",
                          "AIRGAP-CLIPBOARD-TIMELINE", "AIRGAP-VM-PRESENT", "AIRGAP-VM-DISK-IMAGE", "AIRGAP-MULTIBOOT", "AIRGAP-UPLOAD-OUTLIER", "MEDIA-ARCHIVER-REMOVABLE", "AIRGAP-OPTICAL-WRITE-UNBLOCKED",
                          "MEDIA-OPTICAL-STAGING", "MEDIA-OPTICAL-TOOL-LOG", "MEDIA-ZONE-ROUNDTRIP"];
        foreach (var r in rules) Assert.Contains(found, f => f.RuleId == r);
        foreach (var r in rules)
        {
            var c = RuleContracts.For(r);
            Assert.NotNull(c);
            Assert.EndsWith("Wp14Rules", c!.Producer);
            Assert.NotEmpty(c.Limitations); Assert.NotEmpty(c.MissingEvidence); Assert.NotEmpty(c.NextSteps); Assert.True(c.Meaning.Length > 20);
        }
        var enriched = FindingContract.Enrich(found).Where(f => rules.Contains(f.RuleId)).ToList();
        Assert.NotEmpty(enriched);
        foreach (var f in enriched)
        {
            Assert.True(f.HasSemanticType, f.RuleId);
            Assert.NotEmpty(f.Limitations); Assert.NotEmpty(f.AlternativeExplanations); Assert.NotEmpty(f.SupportingEvidence); Assert.NotEmpty(f.ClassificationReason);
            Assert.Empty(AntiOverclaim.Violations(f));
            Assert.NotNull(f.AirGap);
            Assert.Contains(f.AirGap!.Subcategory, Wp14Data.Default.Subcategories);
            Assert.True(f.Severity <= Severity.High, f.RuleId);
        }
    }

    [Fact]
    public void The_declared_semantic_type_is_supported_by_the_cited_artifacts_per_the_sufficiency_check()
    {
        var facts = new AirGapFacts
        {
            CollectedUtc = P0, Print = new() { OperationalLogEnabled = false, Printers = [new() { Name = "Rețea", Port = "IP_1", IsNetwork = true }] }, Virtual = new() { BootOsEntries = 2, BootEntryNames = ["A", "B"] },
            Hardware = [new() { Name = "Integrated Camera", PnpClass = "Camera", InstanceId = @"USB\C" }], Burn = new() { CdDvdDenyWrite = false, Writers = [new() { Name = "W", CanWrite = true }] },
        };
        var events = new List<TimelineEvent> { Print307(1, "d.docx", "Imprimanta A"), Pnp(2, @"HID\VID_1234&PID_5678\7&1", "HID Keyboard Device", KbGuid), Shell4688(2.2, @"C:\Windows\System32\cmd.exe"), Usb("AA001", 5) };
        foreach (var f in Go(events, facts, media: Media(Reg("AA001", "ROSU")), zone: "PUBLIC"))
        {
            var refs = f.SupportingEvidence.Select(r =>
            {
                if (r.EvidenceId == "EV-FACTS") return new LogAnalyzer.Verification.ResolvedRef(r, null, [], "airgap_facts", "airgap_facts", false);
                var e = events.First(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator);
                var row = new LogAnalyzer.Verification.TimelineRow(e.Time.Utc, e.TimeSemantics, e.Source, e.EventId, e.Path, e.Process, e.Summary, e.EvidenceId, e.Locator, "");
                var kind = e.Source.StartsWith("EventLog:", StringComparison.Ordinal) && e.EventId.Length > 0 ? $"{e.Source} {e.EventId}" : e.Source;
                return new LogAnalyzer.Verification.ResolvedRef(r, null, [row], e.Source, kind, true);
            }).ToList();
            var res = LogAnalyzer.Verification.Checks.Sufficiency(f, refs);
            Assert.True(res.Outcome != LogAnalyzer.Verification.CheckOutcome.Fail, $"{f.RuleId} ({f.SemanticType.ToSpec()}): {res.Reason}");
        }
    }

    [Fact]
    public void On_an_unclassified_scope_the_new_rules_are_one_level_lower()
    {
        var kb = Pnp(1, @"HID\VID_1234&PID_5678\7&1&0&0000", "HID Keyboard Device", KbGuid);
        var hi = One(Go([kb, Shell4688(1.2, @"C:\Windows\System32\cmd.exe")]), "AIRGAP-BADUSB-PATTERN");
        var lo = One(Go([kb, Shell4688(1.2, @"C:\Windows\System32\cmd.exe")], scope: new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Unclassified }), "AIRGAP-BADUSB-PATTERN");
        Assert.Equal(hi.Severity - 1, lo.Severity);
    }

    [Fact]
    public void Outside_an_air_gapped_scope_the_channel_rules_that_need_it_stay_silent()
    {
        var facts = new AirGapFacts { CollectedUtc = P0, Virtual = new() { HyperVMachines = ["VM"] }, Print = new() { OperationalLogEnabled = false, Printers = [new() { Name = "x" }] } };
        var f = Go([], facts, new CaseScope { Network = NetworkCategory.Connected, Classification = ClassificationLevel.Classified });
        None(f, "AIRGAP-VM-PRESENT"); None(f, "AIRGAP-PRINT-UNLOGGED");
    }

    [Fact]
    public void The_facts_file_round_trips_through_json()
    {
        var facts = new AirGapFacts { CollectedUtc = P0, WebcamPolicy = "Deny", Adapters = [new() { Name = "A", InstanceId = "USB\\X", CompatibleIds = ["USB\\Class_E0"] }], Archivers = [new() { Tool = "7-Zip", List = "FolderHistory", Value = @"E:\" }] };
        var back = AirGapFacts.FromJson(facts.ToJson());
        Assert.Equal("Deny", back.WebcamPolicy);
        Assert.Equal("USB\\Class_E0", Assert.Single(Assert.Single(back.Adapters).CompatibleIds));
        Assert.Equal(@"E:\", Assert.Single(back.Archivers).Value);
    }
}
