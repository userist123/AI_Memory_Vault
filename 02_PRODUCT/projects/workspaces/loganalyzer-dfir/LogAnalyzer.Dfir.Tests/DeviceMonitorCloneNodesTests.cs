using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Real-station walkthrough, round 2: one physical stick appears as several Windows nodes sharing its serial (USB node, USBSTOR disk, UAS node), with
/// placeholder manufacturers and capacity 0 on some of them. They are one device, never a clone; a genuine clone (another known capacity, another VID/PID) is
/// still flagged, and the stored flag is recomputed when a journal is read. Neutral fixtures: FAKE serials and invented ids.
/// </summary>
public sealed class DeviceMonitorCloneNodesTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "devclonenodes_" + Guid.NewGuid().ToString("N"));
    public DeviceMonitorCloneNodesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static DeviceJournalEntry Row(string identity, string instanceId, DeviceClass cls, string mfr, long cap, string vidPid, string serial, bool flagged = true) => new()
    {
        Identity = identity, InstanceId = instanceId, Class = cls, Manufacturer = mfr, CapacityBytes = cap, VidPid = vidPid, Serial = serial, DuplicateSerial = flagged,
        FirstSeenUtc = T0, ConfigManagerErrorCode = 0,
    };

    private DeviceJournal SaveAndLoad(DeviceJournal j)
    {
        var path = Path.Combine(_dir, "device_monitor.json");
        Assert.Null(DeviceJournalStore.Save(path, j));
        var loaded = DeviceJournalStore.Load(path, out var problem);
        Assert.Equal("", problem);
        return loaded;
    }

    /// <summary>The rows an older version wrote for one portable SSD: its USBSTOR disk and its USB node, each split once by a placeholder manufacturer.</summary>
    private static DeviceJournal PortableSsdJournal() => new()
    {
        Devices =
        [
            Row("SN|FAKEVEN PSSD|FAKESSD0001", @"USBSTOR\Disk&Ven_FAKEVEN&Prod_PSSD&Rev_0\FAKESSD0001&0", DeviceClass.ExternalDisk, "(Standard disk drives)", 0, "FAKEVEN PSSD", "FAKESSD0001"),
            Row("SN|0E8E:4001|FAKESSD0001", @"USB\VID_0E8E&PID_4001\FAKESSD0001", DeviceClass.ExternalDisk, "Compatible USB storage device", 0, "0E8E:4001", "FAKESSD0001"),
            Row("SN|FAKEVEN PSSD|FAKESSD0001|DUP|1", @"USBSTOR\Disk&Ven_FAKEVEN&Prod_PSSD&Rev_0\FAKESSD0001&0", DeviceClass.ExternalDisk, "FAKEVEN", 256_000_000_000, "FAKEVEN PSSD", "FAKESSD0001"),
            Row("SN|0E8E:4001|FAKESSD0001|DUP|1", @"USB\VID_0E8E&PID_4001\FAKESSD0001", DeviceClass.ExternalDisk, "Fakeven ", 0, "0E8E:4001", "FAKESSD0001"),
        ],
    };

    [Fact]
    public void The_nodes_of_one_portable_ssd_are_not_a_clone_once_the_journal_is_read_again()
    {
        var j = SaveAndLoad(PortableSsdJournal());
        Assert.Equal(4, j.Devices.Count);
        Assert.All(j.Devices, d => Assert.False(d.DuplicateSerial));     // the stored flags of the older version are not trusted
    }

    [Fact]
    public void The_nodes_of_one_stick_are_not_a_clone()
    {
        var j = new DeviceJournal
        {
            Devices =
            [
                Row("SN|0F1F:1666|FAKEKING0001", @"USB\VID_0F1F&PID_1666\FAKEKING0001", DeviceClass.UsbStorage, "Compatible USB storage device", 0, "0F1F:1666", "FAKEKING0001"),
                Row("SN|FAKEKING DATASTICK|FAKEKING0001", @"USBSTOR\Disk&Ven_FAKEKING&Prod_DataStick&Rev_PMAP\FAKEKING0001&0", DeviceClass.UsbStorage, "(Standard disk drives)", 0, "FAKEKING DATASTICK", "FAKEKING0001"),
                Row("SN|FAKEKING DATASTICK|FAKEKING0001|DUP|1", @"USBSTOR\Disk&Ven_FAKEKING&Prod_DataStick&Rev_PMAP\FAKEKING0001&0", DeviceClass.UsbStorage, "FAKEKING", 128_000_000_000, "FAKEKING DATASTICK", "FAKEKING0001"),
            ],
        };
        Assert.Equal(3, DeviceJournalMerge.RecomputeDuplicateSerials(j));
        Assert.All(j.Devices, d => Assert.False(d.DuplicateSerial));
    }

    [Fact]
    public void A_usb_peripheral_with_host_controller_placeholders_is_not_a_clone()
    {
        var j = new DeviceJournal
        {
            Devices =
            [
                Row("SN|1A2B:0084|FAKEPERIPH01", @"USB\VID_1A2B&PID_0084\FAKEPERIPH01", DeviceClass.Mouse, "(Standard USB Host Controller)", 0, "1A2B:0084", "FAKEPERIPH01"),
                Row("SN|1A2B:0084|FAKEPERIPH01|DUP|1", @"USB\VID_1A2B&PID_0084\FAKEPERIPH01", DeviceClass.Mouse, "(Standard USB Host Controller)", 0, "1A2B:0084", "FAKEPERIPH01"),
                Row("SN|1A2B:0084|FAKEPERIPH01|DUP|2", @"USB\VID_1A2B&PID_0084\FAKEPERIPH01", DeviceClass.Mouse, "Fake Peripherals Inc", 0, "1A2B:0084", "FAKEPERIPH01"),
                Row("SN|1A2B:0101|FAKEKBD01", @"USB\VID_1A2B&PID_0101\FAKEKBD01", DeviceClass.Keyboard, "(Standard keyboards)", 0, "1A2B:0101", "FAKEKBD01"),
                // a phone changes its PID with its USB mode (file transfer / debugging): same vendor, same serial, one phone
                Row("SN|0E8E:6860|FAKEPHONE01", @"USB\VID_0E8E&PID_6860\FAKEPHONE01", DeviceClass.Phone, "Fake Phones", 0, "0E8E:6860", "FAKEPHONE01"),
                Row("SN|0E8E:6866|FAKEPHONE01", @"USB\VID_0E8E&PID_6866\FAKEPHONE01", DeviceClass.Phone, "FAKE PHONES LTD", 0, "0E8E:6866", "FAKEPHONE01"),
            ],
        };
        DeviceJournalMerge.RecomputeDuplicateSerials(j);
        Assert.All(j.Devices, d => Assert.False(d.DuplicateSerial));
    }

    [Fact]
    public void Placeholder_manufacturers_are_unknown_and_real_ones_compare_trimmed_and_case_insensitively()
    {
        foreach (var p in new[] { "", "  ", "(Standard disk drives)", "Compatible USB storage device", "(Standard system devices)", "(Standard USB Host Controller)", "(Standard keyboards)", "Microsoft", "Generic" })
            Assert.Equal("", DeviceJournalMerge.KnownManufacturer(p));
        Assert.True(DeviceJournalMerge.SameManufacturer("Fakeven ", "FAKEVEN"));
        Assert.True(DeviceJournalMerge.SameManufacturer("(Standard disk drives)", "FAKEVEN"));
        Assert.True(DeviceJournalMerge.SameManufacturer("Fakeven Electronics", "FAKEVEN"));
        Assert.False(DeviceJournalMerge.SameManufacturer("Fakeven", "Othermaker"));
    }

    [Fact]
    public void The_same_stick_seen_with_a_placeholder_manufacturer_and_no_capacity_before_is_the_same_row_live()
    {
        var stick = new PnpSnapshotEntry(@"USBSTOR\Disk&Ven_FAKEVEN&Prod_Stick&Rev_1.00\FAKESTK0001&0", "FAKEVEN Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFAKEVEN_Stick", "disk", 0, "{BBBBBBBB-0000-0000-0000-00000000C001}");
        var j = new DeviceJournal
        {
            Devices = [Row(DeviceMonitorRules.Identity(stick), stick.InstanceId, DeviceClass.UsbStorage, "(Standard disk drives)", 0, "FAKEVEN Stick", "FAKESTK0001", flagged: false)],
        };
        var changes = DeviceJournalMerge.Apply(j, [stick], new Dictionary<string, MediumInfo> { [stick.InstanceId] = new("ABCD1234", 64_000_000_000, "E:", "STICK") }, "user", T0, initial: false);
        var row = Assert.Single(j.Devices);
        Assert.False(row.DuplicateSerial);
        Assert.Null(Assert.Single(changes).CloneText);
        Assert.Equal("FAKEVEN", row.Manufacturer);
    }

    [Fact]
    public void The_old_false_flags_are_cleared_and_the_real_snapshot_raises_no_new_clone_alert()
    {
        var j = SaveAndLoad(PortableSsdJournal());
        // the SSD as UAS: its USB node with the serial and the SCSI disk under it
        var usb = new PnpSnapshotEntry(@"USB\VID_0E8E&PID_4001\FAKESSD0001", "USB Attached SCSI (UAS) Mass Storage Device", "SCSIAdapter", "Compatible USB storage device", @"USB\VID_0E8E&PID_4001", "UASPStor", 0);
        var disk = new PnpSnapshotEntry(@"SCSI\Disk&Ven_Fakeven_&Prod_PSSD\8&2B3C4D5E&0&000000", "Fakeven PSSD SCSI Disk Device", "DiskDrive", "(Standard disk drives)", @"SCSI\DiskFakeven_PSSD", "disk", 0, Parent: usb.InstanceId);
        var changes = DeviceJournalMerge.Apply(j, [usb, disk], new Dictionary<string, MediumInfo> { [disk.InstanceId] = new("11112222", 256_000_000_000, "F:", "SSD") }, "user", T0.AddDays(1), initial: false);
        Assert.All(changes, c => Assert.Null(c.CloneText));
        Assert.All(j.Devices, d => Assert.False(d.DuplicateSerial));
    }

    // ---- genuine clones stay flagged ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_same_serial_with_two_known_capacities_stays_a_clone_after_reading_the_journal()
    {
        var j = SaveAndLoad(new DeviceJournal
        {
            Devices =
            [
                Row("SN|FAKE STICK|FAKECLN0001", @"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKECLN0001&0", DeviceClass.UsbStorage, "Fake", 16_000_000_000, "Fake Stick", "FAKECLN0001", flagged: false),
                Row("SN|FAKE STICK|FAKECLN0001|DUP|1", @"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKECLN0001&0", DeviceClass.UsbStorage, "Fake", 64_000_000_000, "Fake Stick", "FAKECLN0001", flagged: false),
            ],
        });
        Assert.All(j.Devices, d => Assert.True(d.DuplicateSerial));     // recomputed, even though the stored flag was false
    }

    [Fact]
    public void The_same_serial_under_another_vid_pid_is_a_clone_live_and_on_reading()
    {
        static PnpSnapshotEntry[] Uas(string vid, string pid)
        {
            var usb = new PnpSnapshotEntry($@"USB\VID_{vid}&PID_{pid}\FAKECLN0002", "USB Attached SCSI (UAS) Mass Storage Device", "SCSIAdapter", "Fake", $@"USB\VID_{vid}&PID_{pid}", "UASPStor", 0);
            var disk = new PnpSnapshotEntry($@"SCSI\Disk&Ven_Fake&Prod_SSD\8&{vid}{pid}&0&000000", "Fake SSD SCSI Disk Device", "DiskDrive", "(Standard disk drives)", @"SCSI\DiskFake____SSD", "disk", 0, Parent: usb.InstanceId);
            return [usb, disk];
        }
        var j = new DeviceJournal();
        Assert.All(DeviceJournalMerge.Apply(j, Uas("0BDA", "9210"), null, "user", T0, initial: false), c => Assert.Null(c.CloneText));
        DeviceJournalMerge.Apply(j, [], null, "—", T0.AddHours(1), initial: false);
        var changes = DeviceJournalMerge.Apply(j, Uas("1D6B", "0042"), null, "user", T0.AddDays(3), initial: false);
        Assert.Equal(DeviceMonitorRules.DuplicateSerialAlertText, Assert.Single(changes, c => c.Arrived).CloneText);
        var rows = j.Devices.Where(d => d.Serial == "FAKECLN0002").ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, d => Assert.True(d.DuplicateSerial));
        // the clone coming back is its own row: no second alert
        DeviceJournalMerge.Apply(j, [], null, "—", T0.AddDays(4), initial: false);
        Assert.All(DeviceJournalMerge.Apply(j, Uas("1D6B", "0042"), null, "user", T0.AddDays(5), initial: false), c => Assert.Null(c.CloneText));
        // and the flag survives a reading of the journal
        Assert.All(SaveAndLoad(j).Devices.Where(d => d.Serial == "FAKECLN0002"), d => Assert.True(d.DuplicateSerial));
    }

    [Fact]
    public void The_marks_of_the_simultaneous_and_port_generated_clone_rules_survive_a_reading()
    {
        var a = new PnpSnapshotEntry(@"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.00\FAKECLONE01&0", "Fake Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFake____Stick_3.0", "disk", 0, "{BBBBBBBB-0000-0000-0000-000000000001}");
        var b = a with { InstanceId = @"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.01\FAKECLONE01&0", ContainerId = "{BBBBBBBB-0000-0000-0000-000000000002}" };
        var port = a with { InstanceId = @"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.00\7&2A3B4C5D&0", ContainerId = "{BBBBBBBB-0000-0000-0000-000000000003}" };
        var simultaneous = new DeviceJournal();
        DeviceJournalMerge.Apply(simultaneous, [a, b], null, "—", T0, initial: false);
        Assert.All(SaveAndLoad(simultaneous).Devices, d => Assert.True(d.DuplicateSerial));
        var portJ = new DeviceJournal();
        DeviceJournalMerge.Apply(portJ, [a, port], null, "—", T0, initial: true);
        Assert.Equal(2, portJ.Devices.Count);
        Assert.All(SaveAndLoad(portJ).Devices, d => Assert.True(d.DuplicateSerial));
    }

    // ---- the row's name ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_phone_whose_storage_view_is_named_after_its_drive_letter_shows_its_own_name()
    {
        const string container = "{BBBBBBBB-0000-0000-0000-00000000F001}";
        var wpd = new PnpSnapshotEntry(@"SWD\WPDBUSENUM\{FAKE0000-0000-0000-0000-000000000001}#0000000000007E00", @"F:\", "WPD", "Fake Phones", "", "WUDFWpdFs", 0, container);
        var usb = new PnpSnapshotEntry(@"USB\VID_0E8E&PID_6860\FAKEPHONE02", "Fake Phone X1", "USBDevice", "Fake Phones", @"USB\VID_0E8E&PID_6860", "WUDFRd", 0, container);
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [wpd, usb], null, "user", T0, initial: false);
        var row = Assert.Single(j.Devices);
        Assert.Equal(DeviceClass.Phone, row.Class);
        Assert.Equal("Fake Phone X1", row.Name);
        Assert.True(DeviceMonitorRules.IsDriveLetterName(@"F:\"));
        Assert.True(DeviceMonitorRules.IsDriveLetterName("F:"));
        Assert.False(DeviceMonitorRules.IsDriveLetterName("Fake Phone X1"));
        Assert.Equal("Model X", DeviceMonitorRules.FriendlyName([@"F:\"], "Model X"));
        Assert.Equal(@"F:\", DeviceMonitorRules.FriendlyName([@"F:\"]));
    }
}
