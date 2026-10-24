using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Independent review of the clone-detection change (findings 1-3) and the real-PC walkthrough round 3 (2026-10-22): a clone hidden behind another LUN or a
/// shorter manufacturer, a damaged journal row, one physical device as several rows (HID interfaces, old clone rows, the USB-SATA bridge of a disk), an
/// integrated camera, and the primary kind of a mouse with keyboard collections. Neutral fixtures: FAKE serials, invented ids and VID/PIDs.
/// </summary>
public sealed class DeviceMonitorRound3Tests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "devround3_" + Guid.NewGuid().ToString("N"));
    public DeviceMonitorRound3Tests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // ---- finding 1: the LUN number / type is device-controlled ---------------------------------------------------------------------------------

    private static PnpSnapshotEntry Stick(string inst) =>
        new(inst, "Fake Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFake____Stick", "disk", 0, "{BBBBBBBB-0000-0000-0000-00000000C0A1}");

    /// <summary>The original 16 GB stick, removed; a day later a stick with the same vendor/product/serial and 64 GB under <paramref name="cloneInst"/>.</summary>
    private static (DeviceJournal Journal, IReadOnlyList<DeviceJournalChange> Changes) CloneThrough(string cloneInst, long cap = 64_000_000_000)
    {
        var orig = Stick(@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKESER01&0");
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [orig], new Dictionary<string, MediumInfo> { [orig.InstanceId] = new("AAAA0001", 16_000_000_000, "E:", "S") }, "u", T0, initial: false);
        DeviceJournalMerge.Apply(j, [], null, "-", T0.AddHours(1), initial: false);
        var c = Stick(cloneInst);
        var ch = DeviceJournalMerge.Apply(j, [c], new Dictionary<string, MediumInfo> { [c.InstanceId] = new("BBBB0002", cap, "E:", "S") }, "u", T0.AddDays(1), initial: false);
        return (j, ch);
    }

    [Theory]
    [InlineData(@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKESER01&0")]
    [InlineData(@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKESER01&1")]
    [InlineData(@"USBSTOR\CdRom&Ven_Fake&Prod_Stick&Rev_1.00\FAKESER01&0")]
    public void A_serial_back_with_another_capacity_on_any_LUN_is_a_clone_and_the_first_capacity_is_kept(string cloneInst)
    {
        var (j, changes) = CloneThrough(cloneInst);
        Assert.Equal(2, j.Devices.Count);
        Assert.All(j.Devices, d => Assert.True(d.DuplicateSerial));
        Assert.Equal(DeviceMonitorRules.DuplicateSerialAlertText, Assert.Single(changes, c => c.Arrived).CloneText);
        var original = j.Find("SN|FAKE STICK|FAKESER01")!;
        Assert.Equal(16_000_000_000, original.CapacityBytes);          // never overwritten by the clone's 64 GB
        Assert.Contains(j.Devices, d => d.CapacityBytes == 64_000_000_000 && d.Identity.Contains("|DUP|", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_stick_back_on_another_LUN_with_the_same_capacity_is_the_same_row()
    {
        var (j, changes) = CloneThrough(@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKESER01&1", 16_000_000_000);
        var row = Assert.Single(j.Devices);
        Assert.False(row.DuplicateSerial);
        Assert.Null(Assert.Single(changes, c => c.Arrived).CloneText);
    }

    [Fact]
    public void Stored_rows_of_one_identity_with_two_capacities_on_different_LUNs_are_flagged_when_read()
    {
        var j = new DeviceJournal
        {
            Devices =
            [
                new() { Identity = "SN|FAKE STICK|C9", InstanceId = @"USBSTOR\Disk&Ven_Fake&Prod_Stick\C9&0", Class = DeviceClass.UsbStorage, Serial = "C9", VidPid = "Fake Stick", CapacityBytes = 16_000_000_000, FirstSeenUtc = T0 },
                new() { Identity = "SN|FAKE STICK|C9|DUP|1", InstanceId = @"USBSTOR\CdRom&Ven_Fake&Prod_Stick\C9&1", Class = DeviceClass.UsbStorage, Serial = "C9", VidPid = "Fake Stick", CapacityBytes = 64_000_000_000, FirstSeenUtc = T0.AddDays(1) },
            ],
        };
        DeviceJournalMerge.RecomputeDuplicateSerials(j);
        Assert.All(j.Devices, d => Assert.True(d.DuplicateSerial));
        DeviceJournalMerge.MarkSuperseded(j);
        Assert.All(j.Devices, d => Assert.Equal("", d.SupersededBy));   // a real clone is never folded away
    }

    // ---- finding 2: manufacturers --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Kingston", "KINGSTON ", true)]
    [InlineData("Fakeven", "Fakeven Electronics", true)]
    [InlineData("Samsung", "Samsung Electronics Co., Ltd.", true)]
    [InlineData("Razer Inc", "Razer USA Ltd.", true)]        // prefix rule: RAZER is the first whole word of RAZER USA, 5 characters
    [InlineData("(Standard disk drives)", "Kingston", true)] // a placeholder is unknown
    [InlineData("Samsung", "S", false)]
    [InlineData("S", "SanDisk", false)]
    [InlineData("A", "Kingston", false)]
    [InlineData("WD", "Western Digital", false)]
    [InlineData("Sams", "Samsung", false)]                    // a cut word is not a whole-word prefix
    [InlineData("Kingston", "SanDisk", false)]
    public void Manufacturers_match_only_when_equal_or_a_whole_word_prefix_of_at_least_four_characters(string a, string b, bool same)
    {
        Assert.Equal(same, DeviceJournalMerge.SameManufacturer(a, b));
        Assert.Equal(same, DeviceJournalMerge.SameManufacturer(b, a));
    }

    private static PnpSnapshotEntry[] Uas(string ven, string gen)
    {
        var usb = new PnpSnapshotEntry(@"USB\VID_0E8E&PID_4001\FAKESSD9", "UAS", "SCSIAdapter", "Compatible USB storage device", @"USB\VID_0E8E&PID_4001", "UASPStor", 0);
        var disk = new PnpSnapshotEntry($@"SCSI\Disk&Ven_{ven}&Prod_PSSD\8&{gen}&0&000000", "x SCSI Disk Device", "DiskDrive", "(Standard disk drives)", @"SCSI\Diskx", "disk", 0, Parent: usb.InstanceId);
        return [usb, disk];
    }

    private static IReadOnlyList<DeviceJournalChange> ConnectUas(DeviceJournal j, string ven, string gen, DateTimeOffset when)
    {
        var a = Uas(ven, gen);
        var ch = DeviceJournalMerge.Apply(j, a, new Dictionary<string, MediumInfo> { [a[1].InstanceId] = new("1111" + gen[..4], 500_000_000_000, "F:", "S") }, "u", when, initial: false);
        DeviceJournalMerge.Apply(j, [], null, "-", when.AddHours(1), initial: false);
        return ch;
    }

    [Fact]
    public void A_shorter_manufacturer_is_a_clone_and_never_overwrites_the_real_one()
    {
        var j = new DeviceJournal();
        ConnectUas(j, "Samsung", "1111AAAA", T0);
        var ch = ConnectUas(j, "S", "2222BBBB", T0.AddDays(1));
        Assert.Equal(DeviceMonitorRules.DuplicateSerialAlertText, Assert.Single(ch, c => c.Arrived).CloneText);
        Assert.Equal(2, j.Devices.Count);
        Assert.Equal("Samsung", j.Find("SN|0E8E:4001|FAKESSD9")!.Manufacturer);   // not „S”
        // and „S” does not open the door to „SanDisk”
        var ch2 = ConnectUas(j, "SanDisk", "3333CCCC", T0.AddDays(2));
        Assert.Equal(DeviceMonitorRules.DuplicateSerialAlertText, Assert.Single(ch2, c => c.Arrived).CloneText);
        Assert.Equal(3, j.Devices.Count);
        Assert.Equal(["Samsung", "S", "SanDisk"], j.Devices.Select(d => d.Manufacturer).ToArray());
    }

    [Fact]
    public void The_same_manufacturer_padded_or_in_another_case_is_the_same_device()
    {
        var j = new DeviceJournal();
        ConnectUas(j, "Kingston", "1111AAAA", T0);
        var ch = ConnectUas(j, "KINGSTON_", "2222BBBB", T0.AddDays(1));   // „Ven_KINGSTON_” = „KINGSTON ” padded
        Assert.Single(j.Devices);
        Assert.Null(Assert.Single(ch, c => c.Arrived).CloneText);
    }

    // ---- finding 3: a damaged journal row --------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("{\"devices\":[{\"identity\":\"SN|0E8E:4001|X\",\"serial\":null,\"vid_pid\":null,\"manufacturer\":null,\"instance_id\":null}]}", 0)]
    [InlineData("{\"devices\":[{\"identity\":\"SN|A|X\",\"serial\":null},{\"identity\":\"SN|B|X\",\"serial\":\"X\"}]}", 1)]
    [InlineData("{\"devices\":[{\"identity\":\"SN|A|X\",\"serial\":\"X\",\"instance_id\":null,\"capacity_bytes\":5},{\"identity\":\"SN|B|X\",\"serial\":\"X\",\"capacity_bytes\":7}]}", 1)]
    [InlineData("{\"devices\":[{\"identity\":\"SN|A|X\",\"serial\":\"X\",\"vid_pid\":null},{\"identity\":\"SN|B|X\",\"serial\":\"X\"}]}", 1)]
    [InlineData("{\"devices\":[{\"identity\":null},{\"identity\":\"SN|B|X\",\"serial\":\"X\"}]}", 1)]
    [InlineData("{\"devices\":[{\"identity\":\"SN|B|X\",\"serial\":\"X\"}],\"alerts\":[{\"time_utc\":\"2026-10-20T08:00:00+00:00\",\"identity\":null,\"device\":null,\"class\":\"USB_STORAGE\",\"severity\":\"CRITICAL\",\"text\":null}]}", 1)]
    public void A_row_with_a_null_field_is_reported_as_damaged_and_the_rest_is_read(string json, int rowsLeft)
    {
        var path = Path.Combine(_dir, "device_monitor.json");
        File.WriteAllText(path, json);
        var j = DeviceJournalStore.Load(path, out var problem);            // never throws
        Assert.Equal(rowsLeft, j.Devices.Count);
        Assert.Contains("parțial deteriorat", problem);
        Assert.Contains("câmp null", problem);
        Assert.All(j.Devices, d => Assert.Equal("SN|B|X", d.Identity));
    }

    [Fact]
    public void The_running_monitor_keeps_a_partly_damaged_journal_instead_of_resetting_it()
    {
        var path = Path.Combine(_dir, "device_monitor.json");
        File.WriteAllText(path, "{\"devices\":[{\"identity\":\"SN|A|X\",\"serial\":null},{\"identity\":\"SN|B|X\",\"serial\":\"X\"}]}");
        var sealedEvents = new List<string>();
        var j = DeviceJournalStore.LoadForMonitor(path, (ev, _) => { sealedEvents.Add(ev); return null; }, out var problem);
        Assert.Single(j.Devices);
        Assert.Contains("parțial deteriorat", problem);
        Assert.True(File.Exists(path));                                  // not set aside
        Assert.Empty(sealedEvents);
    }

    // ---- round 3: one physical device, one row ---------------------------------------------------------------------------------------------------

    private const string MouseBox = "{AAAA0000-0000-0000-0000-00000000F001}";

    /// <summary>A gaming mouse like the station's: mouse on interface 0, keyboard collections (macros) and a consumer control on later interfaces.</summary>
    private static PnpSnapshotEntry[] MacroMouse(string container = MouseBox)
    {
        const string top = @"USB\VID_1A2B&PID_3C4D\000000000000";
        return
        [
            new(top, "USB Composite Device", "USB", "(Standard USB Host Controller)", @"USB\VID_1A2B&PID_3C4D", "usbccgp", 0, container),
            new(@"USB\VID_1A2B&PID_3C4D&MI_00\7&AAAA0001&0&0000", "USB Input Device", "HIDClass", "(Standard system devices)", @"USB\VID_1A2B&PID_3C4D&MI_00", "HidUsb", 0, container, top),
            new(@"HID\VID_1A2B&PID_3C4D&MI_00\8&AAAA0002&0&0000", "Fake Gaming Mouse", "Mouse", "Fakemaker Inc", @"HID\VID_1A2B&PID_3C4D&MI_00", "mouhid", 0, container, @"USB\VID_1A2B&PID_3C4D&MI_00\7&AAAA0001&0&0000"),
            new(@"USB\VID_1A2B&PID_3C4D&MI_01\7&AAAA0003&0&0001", "USB Input Device", "HIDClass", "(Standard system devices)", @"USB\VID_1A2B&PID_3C4D&MI_01", "HidUsb", 0, container, top),
            new(@"HID\VID_1A2B&PID_3C4D&MI_01&COL01\8&AAAA0004&0&0000", "HID Keyboard Device", "Keyboard", "Fakemaker Inc", @"HID\VID_1A2B&PID_3C4D&MI_01&COL01", "kbdhid", 0, container, @"USB\VID_1A2B&PID_3C4D&MI_01\7&AAAA0003&0&0001"),
            new(@"HID\VID_1A2B&PID_3C4D&MI_01&COL02\8&AAAA0004&0&0001", "HID-compliant consumer control device", "HIDClass", "Microsoft", @"HID\VID_1A2B&PID_3C4D&MI_01&COL02", "", 0, container, @"USB\VID_1A2B&PID_3C4D&MI_01\7&AAAA0003&0&0001"),
            new(@"HID\VID_1A2B&PID_3C4D&MI_02\8&AAAA0005&0&0000", "HID Keyboard Device", "Keyboard", "Fakemaker Inc", @"HID\VID_1A2B&PID_3C4D&MI_02", "kbdhid", 0, container, @"USB\VID_1A2B&PID_3C4D&MI_02\7&AAAA0006&0&0002"),
            new(@"USB\VID_1A2B&PID_3C4D&MI_02\7&AAAA0006&0&0002", "USB Input Device", "HIDClass", "(Standard system devices)", @"USB\VID_1A2B&PID_3C4D&MI_02", "HidUsb", 0, container, top),
        ];
    }

    /// <summary>A keyboard like the station's other one: keyboard on interface 0, a mouse collection (and vendor collections) on interface 1, no serial.</summary>
    private static PnpSnapshotEntry[] KeyboardWithMouseCollection(string container = "{AAAA0000-0000-0000-0000-00000000F002}")
    {
        const string top = @"USB\VID_5E6F&PID_7A8B\6&BBBB0001&0&2";
        return
        [
            new(top, "USB Composite Device", "USB", "(Standard USB Host Controller)", @"USB\VID_5E6F&PID_7A8B", "usbccgp", 0, container),
            new(@"HID\VID_5E6F&PID_7A8B&MI_00\8&BBBB0002&0&0000", "HID Keyboard Device", "Keyboard", "(Standard keyboards)", @"HID\VID_5E6F&PID_7A8B&MI_00", "kbdhid", 0, container, @"USB\VID_5E6F&PID_7A8B&MI_00\7&BBBB0003&0&0000"),
            new(@"USB\VID_5E6F&PID_7A8B&MI_00\7&BBBB0003&0&0000", "USB Input Device", "HIDClass", "(Standard system devices)", "", "HidUsb", 0, container, top),
            new(@"HID\VID_5E6F&PID_7A8B&MI_01&COL04\8&BBBB0004&0&0003", "HID Keyboard Device", "Keyboard", "(Standard keyboards)", "", "kbdhid", 0, container, @"USB\VID_5E6F&PID_7A8B&MI_01\7&BBBB0005&0&0001"),
            new(@"HID\VID_5E6F&PID_7A8B&MI_01&COL05\8&BBBB0004&0&0004", "HID-compliant vendor-defined device", "HIDClass", "(Standard system devices)", "", "", 0, container, @"USB\VID_5E6F&PID_7A8B&MI_01\7&BBBB0005&0&0001"),
            new(@"HID\VID_5E6F&PID_7A8B&MI_01&COL08\8&BBBB0004&0&0007", "HID-compliant mouse", "Mouse", "Microsoft", "", "mouhid", 0, container, @"USB\VID_5E6F&PID_7A8B&MI_01\7&BBBB0005&0&0001"),
            new(@"USB\VID_5E6F&PID_7A8B&MI_01\7&BBBB0005&0&0001", "USB Input Device", "HIDClass", "(Standard system devices)", "", "HidUsb", 0, container, top),
        ];
    }

    [Fact]
    public void A_mouse_with_keyboard_and_consumer_collections_is_one_external_mouse_row()
    {
        var d = Assert.Single(DeviceScope.Classify(MacroMouse()));
        Assert.Equal(DeviceClass.Mouse, d.Kind);
        Assert.Equal("Fake Gaming Mouse", d.Primary.Name);
        Assert.Equal(@"USB\VID_1A2B&PID_3C4D\000000000000", d.IdentityEntry.InstanceId);
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, MacroMouse(), null, "u", T0, initial: true);
        var row = Assert.Single(j.Devices);
        Assert.Equal(DeviceClass.Mouse, row.Class);
        Assert.Equal("Mouse extern", DeviceMonitorRules.ClassText(row.Class));
    }

    [Fact]
    public void A_keyboard_whose_mouse_collection_is_on_a_later_interface_stays_one_keyboard_row()
    {
        var d = Assert.Single(DeviceScope.Classify(KeyboardWithMouseCollection()));
        Assert.Equal(DeviceClass.Keyboard, d.Kind);
        Assert.Equal(@"HID\VID_5E6F&PID_7A8B&MI_00\8&BBBB0002&0&0000", d.Primary.InstanceId);
    }

    [Fact]
    public void Without_container_ids_the_interfaces_are_grouped_with_their_USB_device_through_their_parents()
    {
        var mouse = MacroMouse(container: "");
        Assert.Equal(DeviceClass.Mouse, Assert.Single(DeviceScope.Classify(mouse)).Kind);
        var keyboard = KeyboardWithMouseCollection(container: "");
        Assert.Equal(DeviceClass.Keyboard, Assert.Single(DeviceScope.Classify(keyboard)).Kind);
    }

    private const string SsdBox = "{AAAA0000-0000-0000-0000-00000000F003}";

    /// <summary>A SATA SSD in a USB enclosure: the bridge's USB node (mass storage, the SSD's serial), the USBSTOR disk, and the portable-device view of its volume.</summary>
    private static PnpSnapshotEntry[] SsdInEnclosure() =>
    [
        new(@"USB\VID_9A9B&PID_9C9D\FAKESSD0042", "USB Mass Storage Device", "USB", "Compatible USB storage device", @"USB\VID_9A9B&PID_9C9D USB\Class_08&SubClass_06&Prot_50", "USBSTOR", 0, SsdBox),
        new(@"USBSTOR\Disk&Ven_Fakevend&Prod_SSD_250G&Rev_1.02\FAKESSD0042&0", "Fakevend SSD 250G USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFakevendSSD_250G", "disk", 0, SsdBox, @"USB\VID_9A9B&PID_9C9D\FAKESSD0042"),
        new(@"SWD\WPDBUSENUM\{AAAA1111-0000-0000-0000-000000000001}#0000000000100000", @"F:\", "WPD", "Fakevend", "", "WUDFWpdFs", 0, SsdBox),
        new(@"STORAGE\VOLUME\_??_USBSTOR#DISK&VEN_FAKEVEND&PROD_SSD_250G&REV_1.02#FAKESSD0042&0#{53F56307-B6BF-11D0-94F2-00A0C91EFB8B}", "Volume", "Volume", "Microsoft", "", "volume", 0, SsdBox),
    ];

    [Fact]
    public void The_USB_SATA_bridge_of_a_disk_is_part_of_the_disk_never_a_phone()
    {
        var snap = SsdInEnclosure();
        var media = new Dictionary<string, MediumInfo> { [snap[1].InstanceId] = new("ABCD1234", 250_000_000_000, "F:", "SSD") };
        var d = Assert.Single(DeviceScope.Classify(snap, media));
        Assert.Equal(DeviceClass.ExternalDisk, d.Kind);
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, snap, media, "u", T0, initial: true);
        Assert.DoesNotContain(j.Devices, r => r.Class == DeviceClass.Phone);
        Assert.Single(j.Devices);
    }

    [Fact]
    public void A_phone_with_a_portable_device_view_and_no_storage_drive_is_still_a_phone()
    {
        var box = "{AAAA0000-0000-0000-0000-00000000F004}";
        PnpSnapshotEntry[] snap =
        [
            new(@"USB\VID_04E8&PID_6860\FAKEPHONE01", "Fake Phone", "WPD", "Fakephone", @"USB\VID_04E8&PID_6860", "WUDFWpdMtp", 0, box),
            new(@"SWD\WPDBUSENUM\{AAAA2222-0000-0000-0000-000000000002}#0000000000100000", @"F:\", "WPD", "Fakephone", "", "WUDFWpdFs", 0, box),
        ];
        Assert.Equal(DeviceClass.Phone, Assert.Single(DeviceScope.Classify(snap)).Kind);
    }

    [Fact]
    public void An_integrated_camera_in_a_container_of_its_own_is_not_listed()
    {
        PnpSnapshotEntry[] snap =
        [
            new(@"USB\VID_ABCD&PID_EF01&MI_00\6&CCCC0001&0&0000", "Integrated Camera", "Camera", "Fakecam", @"USB\VID_ABCD&PID_EF01&MI_00", "usbvideo", 0, "{AAAA0000-0000-0000-0000-00000000F005}", Removable: false),
        ];
        Assert.Empty(DeviceScope.Classify(snap));                  // attested by the PnP stack (not removable), whatever its name
        // the name alone decides nothing any more (final review 2026-10-22): without the attestation the same node is listed
        Assert.Equal(DeviceClass.Camera, Assert.Single(DeviceScope.Classify([snap[0] with { Removable = null }])).Kind);
        // and the row an older version wrote for it is not shown (it stays in the journal for check D05)
        var old = new DeviceJournalEntry { Identity = @"ID|USB\VID_ABCD&PID_EF01&MI_00\6&CCCC0001&0&0000", InstanceId = @"USB\VID_ABCD&PID_EF01&MI_00\6&CCCC0001&0&0000", Name = "Integrated Camera", Class = DeviceClass.Camera, VidPid = "ABCD:EF01" };
        Assert.True(DeviceScope.IsInScope(old));
        Assert.False(DeviceScope.IsShown(old));
    }

    [Fact]
    public void Rows_of_station_components_an_older_version_wrote_are_not_shown()
    {
        Assert.False(DeviceScope.IsShown(new DeviceJournalEntry { Identity = @"ID|ACPI\MSFT0001\4&1&0", InstanceId = @"ACPI\MSFT0001\4&1&0", Name = "Standard PS/2 Keyboard", Class = DeviceClass.Keyboard }));
        Assert.False(DeviceScope.IsShown(new DeviceJournalEntry { Identity = @"ID|PCI\VEN_1111&DEV_2222\3&1&0", InstanceId = @"PCI\VEN_1111&DEV_2222\3&1&0", Name = "Fake PCIe WiFi 802.11ax", Class = DeviceClass.WiFi }));
        Assert.False(DeviceScope.IsShown(new DeviceJournalEntry { Identity = @"ID|HID\FAKE06FA&COL01\5&1&0&0000", InstanceId = @"HID\FAKE06FA&COL01\5&1&0&0000", Name = "HID-compliant mouse", Class = DeviceClass.Mouse }));
        Assert.True(DeviceScope.IsShown(new DeviceJournalEntry { Identity = @"ID|USB\VID_5E6F&PID_7A8B\6&BBBB0001&0&2", InstanceId = @"USB\VID_5E6F&PID_7A8B\6&BBBB0001&0&2", Name = "HID Keyboard Device", Class = DeviceClass.Keyboard }));
        Assert.True(DeviceScope.IsShown(new DeviceJournalEntry { Identity = @"SN|FAKE STICK|C1", InstanceId = @"USBSTOR\Disk&Ven_Fake&Prod_Stick\C1&0", Name = "Fake Stick", Class = DeviceClass.UsbStorage }));
    }

    // ---- round 3: the station's journal (old interface rows, old clone rows, the bridge) ---------------------------------------------------------

    private static DeviceJournalEntry R(string identity, string inst, DeviceClass cls, string name, string mfr, string vidPid, string serial, long cap, DateTimeOffset first,
        DateTimeOffset? lastConnected, bool present, bool flagged = false) => new()
    {
        Identity = identity, InstanceId = inst, Class = cls, Name = name, Manufacturer = mfr, VidPid = vidPid, Serial = serial, CapacityBytes = cap, FirstSeenUtc = first,
        LastConnectedUtc = lastConnected, LastDisconnectedUtc = present ? null : lastConnected?.AddMinutes(30), Present = present, DuplicateSerial = flagged,
    };

    /// <summary>The shape of the station's journal (fake values): each device of round 3 with the rows an older version left for it.</summary>
    private static List<DeviceJournalEntry> StationShapedRows()
    {
        var t1 = T0.AddDays(1);
        var t5 = T0.AddDays(5);
        return
        [
            R("SN|FAKEKING DT3|FAKEKING0001", @"USBSTOR\DISK&VEN_FAKEKING&PROD_DT3&REV_0000\FAKEKING0001&0", DeviceClass.UsbStorage, "Fakeking DT3 USB Device", "FAKEKING", "FAKEKING DT3", "FAKEKING0001", 247_000_000_000, T0, t5, true),
            R("SN|FAKEKING DT3|FAKEKING0001|DUP|1", @"USBSTOR\DISK&VEN_FAKEKING&PROD_DT3&REV_0000\FAKEKING0001&0", DeviceClass.UsbStorage, "Fakeking DT3 USB Device", "FAKEKING", "FAKEKING DT3", "FAKEKING0001", 247_000_000_000, t1, t1, false, true),
            R("SN|FAKEVEND SSD 250G|FAKESSD0042", @"USBSTOR\DISK&VEN_FAKEVEND&PROD_SSD_250G&REV_1.02\FAKESSD0042&0", DeviceClass.ExternalDisk, "Fakevend SSD 250G USB Device", "FAKEVEND", "FAKEVEND SSD 250G", "FAKESSD0042", 250_000_000_000, T0, t5, true),
            R("SN|FAKEVEND SSD 250G|FAKESSD0042|DUP|1", @"USBSTOR\DISK&VEN_FAKEVEND&PROD_SSD_250G&REV_1.02\FAKESSD0042&0", DeviceClass.ExternalDisk, "Fakevend SSD 250G USB Device", "FAKEVEND", "FAKEVEND SSD 250G", "FAKESSD0042", 250_000_000_000, t1, t1, false),
            R("SN|9A9B:9C9D|FAKESSD0042", @"USB\VID_9A9B&PID_9C9D\FAKESSD0042", DeviceClass.Phone, "USB Mass Storage Device", "Fakevend ", "9A9B:9C9D", "FAKESSD0042", 0, T0, t5, false),   // absent since the first reading of this version
            R("SN|9A9B:9C9D|FAKESSD0042|DUP|1", @"USB\VID_9A9B&PID_9C9D\FAKESSD0042", DeviceClass.Phone, @"F:\", "Fakevend ", "9A9B:9C9D", "FAKESSD0042", 0, t1, t5.AddHours(-1), false),
            R("SN|1A2B:3C4D|000000000000", @"USB\VID_1A2B&PID_3C4D\000000000000", DeviceClass.Keyboard, "HID Keyboard Device", "Fakemaker Inc", "1A2B:3C4D", "000000000000", 0, T0, t5, true, true),
            R("SN|1A2B:3C4D|000000000000|DUP|1", @"USB\VID_1A2B&PID_3C4D\000000000000", DeviceClass.Keyboard, "HID Keyboard Device", "Fakemaker Inc", "1A2B:3C4D", "000000000000", 0, t1, t1, false, true),
            R(@"ID|HID\VID_1A2B&PID_3C4D&MI_00\7&AAAA0002&0&0000", @"HID\VID_1A2B&PID_3C4D&MI_00\7&AAAA0002&0&0000", DeviceClass.Mouse, "Fake Gaming Mouse", "Fakemaker Inc", "1A2B:3C4D", "", 0, T0.AddHours(-2), T0.AddHours(-2), false),
            R(@"ID|HID\VID_1A2B&PID_3C4D&MI_01&COL01\7&AAAA0004&0&0000", @"HID\VID_1A2B&PID_3C4D&MI_01&COL01\7&AAAA0004&0&0000", DeviceClass.Keyboard, "HID Keyboard Device", "Fakemaker Inc", "1A2B:3C4D", "", 0, T0.AddHours(-2), T0.AddHours(-2), false),
            R(@"ID|HID\VID_1A2B&PID_3C4D&MI_02\7&AAAA0005&0&0000", @"HID\VID_1A2B&PID_3C4D&MI_02\7&AAAA0005&0&0000", DeviceClass.Keyboard, "HID Keyboard Device", "Fakemaker Inc", "1A2B:3C4D", "", 0, T0.AddHours(-2), T0.AddHours(-2), false),
            R(@"ID|USB\VID_5E6F&PID_7A8B\6&BBBB0001&0&2", @"USB\VID_5E6F&PID_7A8B\6&BBBB0001&0&2", DeviceClass.Keyboard, "HID Keyboard Device", "(Standard keyboards)", "5E6F:7A8B", "", 0, T0, t5, true),
            R(@"ID|HID\VID_5E6F&PID_7A8B&MI_00\8&BBBB0002&0&0000", @"HID\VID_5E6F&PID_7A8B&MI_00\8&BBBB0002&0&0000", DeviceClass.Keyboard, "HID Keyboard Device", "(Standard keyboards)", "5E6F:7A8B", "", 0, T0.AddHours(-2), T0.AddHours(-2), false),
            R(@"ID|HID\VID_5E6F&PID_7A8B&MI_01&COL08\8&BBBB0004&0&0007", @"HID\VID_5E6F&PID_7A8B&MI_01&COL08\8&BBBB0004&0&0007", DeviceClass.Mouse, "HID-compliant mouse", "Microsoft", "5E6F:7A8B", "", 0, T0.AddHours(-2), T0.AddHours(-2), false),
        ];
    }

    [Fact]
    public void Each_device_of_the_station_is_one_shown_row_whatever_the_order_of_the_rows()
    {
        foreach (var seed in new[] { 0, 1, 2, 3 })
        {
            var rows = StationShapedRows();
            if (seed > 0) rows = rows.OrderBy(r => (r.Identity.GetHashCode(StringComparison.Ordinal) ^ seed * 7919) & 0xFFFF).ThenBy(r => r.Identity, StringComparer.Ordinal).ToList();
            if (seed == 3) rows.Reverse();
            var j = new DeviceJournal { Devices = rows };
            DeviceJournalMerge.RecomputeDuplicateSerials(j);
            DeviceJournalMerge.MarkSuperseded(j);
            var shown = j.Devices.Where(DeviceScope.IsShown).Select(d => d.Identity).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(
                [@"ID|USB\VID_5E6F&PID_7A8B\6&BBBB0001&0&2", "SN|1A2B:3C4D|000000000000", "SN|FAKEKING DT3|FAKEKING0001", "SN|FAKEVEND SSD 250G|FAKESSD0042"], shown);
            // nothing is deleted, and none of these is a clone
            Assert.Equal(14, j.Devices.Count);
            Assert.All(j.Devices, d => Assert.False(d.DuplicateSerial));
            Assert.Equal("SN|FAKEVEND SSD 250G|FAKESSD0042", j.Find(@"SN|9A9B:9C9D|FAKESSD0042|DUP|1")!.SupersededBy);   // chain followed to the disk
            Assert.Equal("SN|1A2B:3C4D|000000000000", j.Find(@"ID|HID\VID_1A2B&PID_3C4D&MI_00\7&AAAA0002&0&0000")!.SupersededBy);
        }
    }

    [Fact]
    public void The_shown_row_has_the_earliest_first_sight_the_latest_connection_and_the_history_of_every_folded_row()
    {
        var j = new DeviceJournal { Devices = StationShapedRows() };
        DeviceJournalMerge.RecomputeDuplicateSerials(j);
        DeviceJournalMerge.MarkSuperseded(j);
        var mouse = j.Find("SN|1A2B:3C4D|000000000000")!;
        var view = DeviceJournalMerge.ShownView(j, mouse);
        Assert.Equal(T0.AddHours(-2), view.FirstSeenUtc);
        Assert.Equal(T0, mouse.FirstSeenUtc);                                // the journal row keeps the date the sealed audit has
        var ids = DeviceJournalMerge.FoldedIdentities(j, mouse.Identity);
        Assert.Equal(5, ids.Count);
        Assert.Contains("SN|1A2B:3C4D|000000000000|DUP|1", ids);
        var disk = DeviceJournalMerge.ShownView(j, j.Find("SN|FAKEVEND SSD 250G|FAKESSD0042")!);
        Assert.Equal(T0.AddDays(5), disk.LastConnectedUtc);
    }

    [Fact]
    public void The_rows_are_folded_when_the_journal_is_read_and_the_fold_is_not_stored()
    {
        var path = Path.Combine(_dir, "device_monitor.json");
        Assert.Null(DeviceJournalStore.Save(path, new DeviceJournal { Devices = StationShapedRows() }));
        Assert.DoesNotContain("superseded", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        var j = DeviceJournalStore.Load(path, out var problem);
        Assert.Equal("", problem);
        Assert.Equal(4, j.Devices.Count(DeviceScope.IsShown));
    }

    private static List<DeviceJournalEntry> MouseWithLegacyKeyboardRows(bool otherKeyboard = false)
    {
        var rows = new List<DeviceJournalEntry>
        {
            new() { Identity = @"ID|USB\VID_1532&PID_009C\6&FAKE01&0&2", InstanceId = @"USB\VID_1532&PID_009C\6&FAKE01&0&2", Name = "USB Input Device", Manufacturer = "Razer Inc", Class = DeviceClass.Mouse, VidPid = "1532:009C", GenericSerial = true, ClassifierVersion = 2, Present = true, FirstSeenUtc = T0.AddDays(3), LastConnectedUtc = T0.AddDays(3) },
            new() { Identity = @"ID|HID\VID_1532&PID_009C&MI_01&COL01\7&AAAA&0&0000", InstanceId = @"HID\VID_1532&PID_009C&MI_01&COL01\7&AAAA&0&0000", Name = "HID Keyboard Device", Manufacturer = "Razer Inc", Class = DeviceClass.Keyboard, VidPid = "1532:009C", GenericSerial = true, Present = false, FirstSeenUtc = T0, LastConnectedUtc = T0, LastDisconnectedUtc = T0.AddHours(1) },
            new() { Identity = @"ID|HID\VID_1532&PID_009C&MI_02\7&BBBB&0&0000", InstanceId = @"HID\VID_1532&PID_009C&MI_02\7&BBBB&0&0000", Name = "HID Keyboard Device", Manufacturer = "Razer Inc", Class = DeviceClass.Keyboard, VidPid = "", Present = false, FirstSeenUtc = T0.AddDays(1), LastDisconnectedUtc = T0.AddDays(1).AddHours(1) },
        };
        if (otherKeyboard)
            rows.Add(new() { Identity = @"ID|HID\VID_1532&PID_009D&MI_00\7&CCCC&0&0000", InstanceId = @"HID\VID_1532&PID_009D&MI_00\7&CCCC&0&0000", Name = "HID Keyboard Device", Manufacturer = "Razer Inc", Class = DeviceClass.Keyboard, VidPid = "1532:009D", Present = false, FirstSeenUtc = T0 });
        return rows;
    }

    [Fact]
    public void Disconnected_legacy_keyboard_interface_rows_fold_into_the_connected_mouse_of_the_same_device()
    {
        var j = new DeviceJournal { Devices = MouseWithLegacyKeyboardRows() };
        DeviceJournalMerge.MarkSuperseded(j);
        var shown = Assert.Single(j.Devices.Where(DeviceScope.IsShown));
        Assert.Equal(DeviceClass.Mouse, shown.Class);
        Assert.Equal(2, j.Devices.Count(d => d.SupersededBy == shown.Identity));
        Assert.Equal(3, j.Devices.Count);                                   // nothing deleted
        Assert.Equal(3, DeviceJournalMerge.FoldedIdentities(j, shown.Identity).Count);   // the history keeps the legacy rows' events
        Assert.Equal(DeviceClass.Keyboard, j.Devices[1].Class);              // the journal row is unchanged
        Assert.Equal(T0, DeviceJournalMerge.ShownView(j, shown).FirstSeenUtc);
    }

    [Fact]
    public void A_different_keyboard_model_stays_a_row_of_its_own_next_to_the_folded_ones()
    {
        var j = new DeviceJournal { Devices = MouseWithLegacyKeyboardRows(otherKeyboard: true) };
        DeviceJournalMerge.MarkSuperseded(j);
        Assert.Equal("", j.Find(@"ID|HID\VID_1532&PID_009D&MI_00\7&CCCC&0&0000")!.SupersededBy);
        Assert.Equal(2, j.Devices.Count(DeviceScope.IsShown));
    }

    [Fact]
    public void Two_connected_rows_and_different_registration_numbers_are_never_folded_into_the_mouse()
    {
        var rows = MouseWithLegacyKeyboardRows();
        rows[1].Present = true;   // two connected rows
        rows[2].RegistrationNumber = "NR-9";
        rows[0].RegistrationNumber = "NR-1";
        var j = new DeviceJournal { Devices = rows };
        DeviceJournalMerge.MarkSuperseded(j);
        Assert.Equal("", rows[0].SupersededBy);
        Assert.Equal("", rows[2].SupersededBy);
    }

    [Fact]
    public void Rows_with_two_different_registration_numbers_are_not_folded()
    {
        var rows = StationShapedRows();
        rows.First(r => r.Identity == "SN|FAKEKING DT3|FAKEKING0001").RegistrationNumber = "NR-1";
        rows.First(r => r.Identity == "SN|FAKEKING DT3|FAKEKING0001|DUP|1").RegistrationNumber = "NR-2";
        var j = new DeviceJournal { Devices = rows };
        DeviceJournalMerge.MarkSuperseded(j);
        Assert.Equal("", j.Find("SN|FAKEKING DT3|FAKEKING0001|DUP|1")!.SupersededBy);
    }

    [Fact]
    public void A_live_reading_matches_the_base_row_not_a_folded_old_clone_row()
    {
        var rows = StationShapedRows();
        // the folded row first in the file: the reading must still continue the base row
        var dup = rows.First(r => r.Identity == "SN|FAKEKING DT3|FAKEKING0001|DUP|1");
        rows.Remove(dup);
        rows.Insert(0, dup);
        var j = new DeviceJournal { Devices = rows };
        DeviceJournalMerge.RecomputeDuplicateSerials(j);
        DeviceJournalMerge.MarkSuperseded(j);
        DeviceJournalMerge.Apply(j, [], null, "-", T0.AddDays(6), initial: false);
        var stick = new PnpSnapshotEntry(@"USBSTOR\DISK&VEN_FAKEKING&PROD_DT3&REV_0000\FAKEKING0001&0", "Fakeking DT3 USB Device", "DiskDrive", "(Standard disk drives)", "", "disk", 0, "{AAAA0000-0000-0000-0000-00000000F006}");
        var ch = DeviceJournalMerge.Apply(j, [stick], new Dictionary<string, MediumInfo> { [stick.InstanceId] = new("ABCD0001", 247_000_000_000, "E:", "K") }, "u", T0.AddDays(7), initial: false);
        Assert.Equal("SN|FAKEKING DT3|FAKEKING0001", Assert.Single(ch, c => c.Arrived).Device.Identity);
        Assert.False(j.Find("SN|FAKEKING DT3|FAKEKING0001|DUP|1")!.Present);
    }

    [Fact]
    public void The_history_of_a_shown_row_has_the_sealed_events_of_every_folded_identity()
    {
        static SealedDeviceEvent Ev(int minutes, string ev, string id) =>
            new(T0.AddMinutes(minutes), ev, id, "", new Dictionary<string, string> { ["device"] = "Fake Stick", ["user"] = @"LAB-PC\user" });
        var audit = new DeviceAuditReading(true, new Dictionary<string, string>(), [], 0)
        {
            Events =
            [
                Ev(0, LogAnalyzer.Dfir.Auth.OperationAudit.DeviceFirstSeen, "SN|FAKE STICK|C1"),
                Ev(60, LogAnalyzer.Dfir.Auth.OperationAudit.DeviceFirstSeen, "SN|FAKE STICK|C1|DUP|1"),
                Ev(90, LogAnalyzer.Dfir.Auth.OperationAudit.DeviceDisconnected, "SN|FAKE STICK|C1|DUP|1"),
                Ev(30, LogAnalyzer.Dfir.Auth.OperationAudit.DeviceFirstSeen, "SN|OTHER|C2"),
            ],
        };
        var h = DeviceHistory.Build(["SN|FAKE STICK|C1", "SN|FAKE STICK|C1|DUP|1"], audit);
        var times = h.Select(x => x.TimeUtc!.Value).Distinct().ToArray();
        Assert.Equal([T0, T0.AddMinutes(60), T0.AddMinutes(90)], times);            // both rows, in time order, not the other device
        Assert.DoesNotContain(DeviceHistory.Build("SN|FAKE STICK|C1", audit), x => x.TimeUtc == T0.AddMinutes(60));
    }
}
