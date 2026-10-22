using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Two fixes from a real journal (2026-10-22), synthetic ids only: (1) a disconnected row with the SAME instance id as a connected one is folded into it, in any
/// identity scheme; (2) a keyboard or mouse that hangs off Bluetooth (BLE HID service 0x1812, BTHENUM, a BTH* parent) is a "Dispozitiv Bluetooth" (a forbidden
/// class on an isolated station), never a keyboard or mouse; classifier version 3 and its sealed whitelist transitions.
/// </summary>
public sealed class DeviceMonitorBluetoothHidTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private const string Usb = @"USB\VID_1234&PID_00AB\000000000000";

    // ---- 1. the same instance id under another identity -----------------------------------------------------------------------------------------

    private static DeviceJournalEntry Present() => new()
    {
        Identity = "ID|" + Usb, InstanceId = Usb, Name = "USB Input Device", Class = DeviceClass.Mouse, VidPid = "1234:00AB", GenericSerial = true, ClassifierVersion = 2,
        Present = true, FirstSeenUtc = T0.AddDays(3), LastConnectedUtc = T0.AddDays(3),
    };

    private static DeviceJournalEntry Leftover(string identity) => new()
    {
        Identity = identity, InstanceId = Usb, Name = "USB Input Device", Class = DeviceClass.Keyboard, VidPid = "1234:00AB", GenericSerial = true, ClassifierVersion = 0,
        Present = false, FirstSeenUtc = T0, LastDisconnectedUtc = T0.AddHours(1),
    };

    [Fact]
    public void A_disconnected_row_with_the_same_instance_id_folds_into_the_connected_one_in_every_identity_scheme()
    {
        var j = new DeviceJournal { Devices = [Present(), Leftover("SN|1234:00AB|000000000000"), Leftover("SN|1234:00AB|000000000000|DUP|1")] };
        DeviceJournalMerge.MarkSuperseded(j);
        var shown = Assert.Single(j.Devices.Where(DeviceScope.IsShown));
        Assert.Equal(DeviceClass.Mouse, shown.Class);
        Assert.Equal(3, j.Devices.Count);                                      // nothing deleted
        Assert.All(j.Devices.Skip(1), d => Assert.NotEqual("", d.SupersededBy));
        Assert.Equal(DeviceClass.Keyboard, j.Devices[1].Class);                // the journal row is unchanged
    }

    [Fact]
    public void The_same_instance_fold_keeps_its_guards()
    {
        // different registration numbers: never folded
        var a = Present(); a.RegistrationNumber = "REG-1";
        var b = Leftover("SN|1234:00AB|000000000000"); b.RegistrationNumber = "REG-2";
        var j = new DeviceJournal { Devices = [a, b] };
        DeviceJournalMerge.MarkSuperseded(j);
        Assert.Equal(2, j.Devices.Count(DeviceScope.IsShown));
        // a different known VID/PID on the old row: never folded
        var c = Leftover("SN|1234:00AB|000000000000"); c.VidPid = "5678:00AB";
        var j2 = new DeviceJournal { Devices = [Present(), c] };
        DeviceJournalMerge.MarkSuperseded(j2);
        Assert.Equal(2, j2.Devices.Count(DeviceScope.IsShown));
        // the same classifier version and another kind: two different things, not folded
        var d = Leftover("SN|1234:00AB|000000000000"); d.ClassifierVersion = 2;
        var j3 = new DeviceJournal { Devices = [Present(), d] };
        DeviceJournalMerge.MarkSuperseded(j3);
        Assert.Equal(2, j3.Devices.Count(DeviceScope.IsShown));
        // another instance id: not this fold
        var e = Leftover("SN|1234:00AB|000000000000"); e.InstanceId = @"USB\VID_1234&PID_00AB\000000000001";
        var j4 = new DeviceJournal { Devices = [Present(), e] };
        DeviceJournalMerge.MarkSuperseded(j4);
        Assert.Equal(2, j4.Devices.Count(DeviceScope.IsShown));
    }

    [Fact]
    public void The_same_instance_fold_never_folds_a_real_serial_clone_in_either_direction()
    {
        // a comparable serial (not a placeholder): the same serial + VID/PID with another real manufacturer is a clone (SerialConflict)
        const string id = @"USB\VID_1234&PID_00AB\SYNTH0001";
        DeviceJournalEntry Row(string identity, string maker, bool present) => new()
        {
            Identity = identity, InstanceId = id, Name = "USB Input Device", Class = DeviceClass.Mouse, VidPid = "1234:00AB", Serial = "SYNTH0001", Manufacturer = maker,
            ClassifierVersion = 2, Present = present, FirstSeenUtc = present ? T0.AddDays(3) : T0, LastConnectedUtc = T0.AddDays(3),
        };
        // clone disconnected, original connected
        var orig = Row("SN|1234:00AB|SYNTH0001", "Logitech", present: true);
        var clone = Row("SN|1234:00AB|SYNTH0001|DUP|1", "Evil Corp", present: false);
        Assert.True(DeviceJournalMerge.SerialConflict(orig, clone));
        var j = new DeviceJournal { Devices = [orig, clone] };
        DeviceJournalMerge.MarkSuperseded(j);
        Assert.Equal(2, j.Devices.Count(DeviceScope.IsShown));
        Assert.All(j.Devices, d => Assert.Equal("", d.SupersededBy));
        // original disconnected, clone connected: the registered row is never hidden under the clone
        var orig2 = Row("SN|1234:00AB|SYNTH0001", "Logitech", present: false); orig2.RegistrationNumber = "REG-1";
        var clone2 = Row("SN|1234:00AB|SYNTH0001|DUP|1", "Evil Corp", present: true);
        var j2 = new DeviceJournal { Devices = [orig2, clone2] };
        DeviceJournalMerge.MarkSuperseded(j2);
        Assert.Equal(2, j2.Devices.Count(DeviceScope.IsShown));
        Assert.All(j2.Devices, d => Assert.Equal("", d.SupersededBy));
    }

    // ---- 2. Bluetooth HID ------------------------------------------------------------------------------------------------------------------------

    private const string Box = "{CCCCCCCC-0000-0000-0000-00000000A003}";
    private const string Ble = @"{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02FEED_PID&0001_REV&0001_AABBCCDDEEFF";

    private static readonly PnpSnapshotEntry BleParent = new(@"BTHLEDEVICE\" + Ble + @"\8&FAKE&0&AABBCCDDEEFF", "Bluetooth Low Energy GATT compliant HID device", "Bluetooth", "Fake", "", "", 0, Box);
    private static readonly PnpSnapshotEntry BleKeyboard = new(@"HID\" + Ble + @"&COL01\9&FAKE&0&0000", "HID Keyboard Device", "Keyboard", "(Standard keyboards)", "", "kbdhid", 0, Box, BleParent.InstanceId);
    private static readonly PnpSnapshotEntry BleMouse = new(@"HID\" + Ble + @"&COL02\9&FAKE&0&0001", "HID-compliant mouse", "Mouse", "Microsoft", "", "mouhid", 0, Box, BleParent.InstanceId);

    private static readonly PnpSnapshotEntry UsbHub = new(@"USB\VID_1234&PID_0042\FAKEKB0001", "USB Composite Device", "USB", "(Standard USB Host Controller)", @"USB\VID_1234&PID_0042", "usbccgp", 0, "{CCCCCCCC-0000-0000-0000-00000000A004}");
    private static readonly PnpSnapshotEntry UsbKeyboard = new(@"HID\VID_1234&PID_0042&MI_00\8&FAKE&0&0000", "HID Keyboard Device", "Keyboard", "(Standard keyboards)", @"HID\VID_1234&PID_0042&MI_00", "kbdhid", 0, "{CCCCCCCC-0000-0000-0000-00000000A004}", @"USB\VID_1234&PID_0042&MI_00\7&FAKE&0&0000");

    [Fact]
    public void A_BLE_HID_keyboard_is_a_Bluetooth_device_with_the_function_in_the_details_and_the_forbidden_class_applies()
    {
        var d = Assert.Single(DeviceScope.Classify([BleParent, BleKeyboard]));
        Assert.Equal(DeviceClass.Bluetooth, d.Kind);
        Assert.Equal("tastatură prin Bluetooth", d.BluetoothFunction);
        Assert.Equal(DeviceClass.Keyboard, d.KindAtVersion(2));
        Assert.True(DeviceMonitorRules.ForbiddenOnIsolated(d.Kind, d.Primary.InstanceId));
        Assert.Equal(DeviceAlertSeverity.Critical, DeviceMonitorRules.Decide(d.Kind, d.Primary.InstanceId, "", true).Severity);
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [BleParent, BleKeyboard], null, "u", T0, initial: false);
        var row = Assert.Single(j.Devices);
        Assert.Equal(DeviceClass.Bluetooth, row.Class);
        Assert.Equal("tastatură prin Bluetooth", row.Detail);
        Assert.Equal(DeviceScope.ClassifierVersion, row.ClassifierVersion);
        Assert.Equal("Dispozitiv Bluetooth", DeviceMonitorRules.ClassText(row.Class));
    }

    [Fact]
    public void A_Bluetooth_HID_without_its_parent_in_the_snapshot_and_a_mouse_are_Bluetooth_too()
    {
        Assert.Equal(DeviceClass.Bluetooth, Assert.Single(DeviceScope.Classify([BleKeyboard with { Parent = "" }])).Kind);   // the HID service id says it
        var m = Assert.Single(DeviceScope.Classify([BleParent, BleMouse]));
        Assert.Equal((DeviceClass.Bluetooth, "mouse prin Bluetooth", DeviceClass.Mouse), (m.Kind, m.BluetoothFunction, m.KindAtVersion(2)));
        Assert.Equal("tastatură/mouse prin Bluetooth", Assert.Single(DeviceScope.Classify([BleParent, BleKeyboard, BleMouse])).BluetoothFunction);
        // classic Bluetooth: a HID node whose only sign is its BTHENUM parent
        var parent = new PnpSnapshotEntry(@"BTHENUM\DEV_AABBCCDDEEFF\7&FAKE&0&BLUETOOTHDEVICE_AABBCCDDEEFF", "Fake Keyboard", "Bluetooth", "Fake", "", "", 0, Box);
        var kb = new PnpSnapshotEntry(@"HID\FAKEHIDNODE\9&FAKE&0&0000", "HID Keyboard Device", "Keyboard", "(Standard keyboards)", "", "kbdhid", 0, Box, parent.InstanceId);
        Assert.Equal(DeviceClass.Bluetooth, Assert.Single(DeviceScope.Classify([parent, kb])).Kind);
    }

    [Fact]
    public void A_USB_keyboard_stays_a_keyboard()
    {
        var d = Assert.Single(DeviceScope.Classify([UsbHub, UsbKeyboard]));
        Assert.Equal((DeviceClass.Keyboard, ""), (d.Kind, d.BluetoothFunction));
        Assert.False(DeviceScope.IsBluetoothHid(UsbKeyboard, null));
    }

    [Fact]
    public void The_whitelist_accepts_Keyboard_or_Mouse_to_Bluetooth_only_in_the_known_transitions()
    {
        Assert.True(DeviceScope.IsKnownReclassification(2, 3, DeviceClass.Keyboard, DeviceClass.Bluetooth));
        Assert.True(DeviceScope.IsKnownReclassification(2, 3, DeviceClass.Mouse, DeviceClass.Bluetooth));
        Assert.True(DeviceScope.IsKnownReclassification(1, 3, DeviceClass.Keyboard, DeviceClass.Bluetooth));
        Assert.False(DeviceScope.IsKnownReclassification(2, 3, DeviceClass.Bluetooth, DeviceClass.Keyboard));   // never the other way
        Assert.False(DeviceScope.IsKnownReclassification(2, 3, DeviceClass.Printer, DeviceClass.Bluetooth));
        Assert.False(DeviceScope.IsKnownReclassification(2, 3, DeviceClass.UsbStorage, DeviceClass.Bluetooth));
    }

    private static Func<IReadOnlyDictionary<string, SealedKind>?> Sealed(string identity, string kind, int version) =>
        () => new Dictionary<string, SealedKind>(StringComparer.OrdinalIgnoreCase) { [OperationAudit.DeviceField(identity)] = new(kind, version) };

    [Fact]
    public void A_v2_Keyboard_row_becomes_Bluetooth_as_a_sealed_reclassification_only_with_the_Bluetooth_parent_in_the_live_snapshot()
    {
        // the row the old classifier wrote for the BLE keyboard: its identity is the one the monitor gives the same node now
        var probe = new DeviceJournal();
        DeviceJournalMerge.Apply(probe, [BleParent, BleKeyboard], null, "u", T0, initial: false);
        var identity = Assert.Single(probe.Devices).Identity;
        DeviceJournal Old() => new() { Devices = [new DeviceJournalEntry { Identity = identity, InstanceId = BleKeyboard.InstanceId, Class = DeviceClass.Keyboard, ClassifierVersion = 2, FirstSeenUtc = T0 }] };

        var j = Old();
        var rec = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j, [BleParent, BleKeyboard], null, "u", T0.AddDays(1), initial: false, reclassified: rec, sealedKinds: Sealed(identity, "Keyboard", 2));
        var n = Assert.Single(rec);
        Assert.Equal((DeviceClass.Keyboard, DeviceClass.Bluetooth, 2, DeviceScope.ClassifierVersion), (n.OldClass, n.NewClass, n.OldVersion, n.NewVersion));
        Assert.Equal(DeviceClass.Bluetooth, j.Devices[0].Class);

        // the same row, the live node is a plain USB keyboard now (no Bluetooth parent): no reclassification note
        var j2 = Old();
        var rec2 = new List<DeviceReclassification>();
        var usbAsBle = UsbKeyboard with { InstanceId = BleKeyboard.InstanceId, HardwareIds = "", Parent = "" };
        DeviceJournalMerge.Apply(j2, [usbAsBle], null, "u", T0.AddDays(1), initial: false, reclassified: rec2, sealedKinds: Sealed(identity, "Keyboard", 2));
        Assert.Empty(rec2);

        // a Bluetooth device sealed as a Printer under v2 is not a known change
        var j3 = Old(); j3.Devices[0].Class = DeviceClass.Printer;
        var rec3 = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j3, [BleParent, BleKeyboard], null, "u", T0.AddDays(1), initial: false, reclassified: rec3, sealedKinds: Sealed(identity, "Printer", 2));
        Assert.Empty(rec3);

        // sealed with this very version: nothing to reclassify
        var j4 = Old();
        var rec4 = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j4, [BleParent, BleKeyboard], null, "u", T0.AddDays(1), initial: false, reclassified: rec4, sealedKinds: Sealed(identity, "Keyboard", DeviceScope.ClassifierVersion));
        Assert.Empty(rec4);
    }

    // ---- 3. a stored row of an older classifier is SHOWN as Bluetooth (display and alert presentation only) ---------------------------------------

    private const string StoredBleId = @"HID\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02AAAA_PID&BBBB_REV&0001_AABBCCDDEEFF&COL01\8&1A2B3C4D&0&0000";

    private static DeviceJournalEntry StoredBleKeyboard() => new()
    {
        Identity = "ID|" + StoredBleId, InstanceId = StoredBleId, Name = "HID Keyboard Device", Class = DeviceClass.Keyboard, Manufacturer = "Synthetic Inc", ClassifierVersion = 0,
        Present = false, FirstSeenUtc = T0, LastConnectedUtc = T0, LastDisconnectedUtc = T0.AddHours(1),
    };

    [Fact]
    public void A_stored_ble_keyboard_row_is_shown_as_bluetooth_with_the_note_and_the_forbidden_class_alert_without_rewriting_the_stored_kind()
    {
        var e = StoredBleKeyboard();
        Assert.Equal(DeviceClass.Bluetooth, DeviceMonitorRules.DisplayClass(e));
        Assert.Equal("tastatură prin Bluetooth (tip vechi înregistrat: Tastatură externă)", DeviceMonitorRules.DisplayDetail(e));
        var d = DeviceMonitorRules.Decide(DeviceMonitorRules.DisplayClass(e), e.InstanceId, "", isolated: true);
        Assert.Equal(DeviceAlertSeverity.Critical, d.Severity);
        Assert.True(DeviceMonitorRules.IsForbiddenClassAlert(d.Reason));
        Assert.Equal(DeviceClass.Keyboard, e.Class);                           // the stored kind is untouched
        Assert.Equal(0, e.ClassifierVersion);

        var line = DeviceMonitorReport.Line(e);
        Assert.Contains("Dispozitiv Bluetooth", line);
        Assert.Contains("tip vechi înregistrat: Tastatură externă", line);
        Assert.True(DeviceMonitorReport.ForbiddenSeen(e, isolated: true));
        Assert.False(DeviceMonitorReport.ForbiddenSeen(e, isolated: false));
    }

    [Fact]
    public void A_stored_usb_keyboard_row_and_a_bluetooth_bus_mouse_row_are_decided_by_their_instance_id()
    {
        var usb = new DeviceJournalEntry { Identity = "ID|" + Usb, InstanceId = Usb, Name = "USB Keyboard", Class = DeviceClass.Keyboard, FirstSeenUtc = T0 };
        Assert.Equal(DeviceClass.Keyboard, DeviceMonitorRules.DisplayClass(usb));
        Assert.Equal("", DeviceMonitorRules.DisplayDetail(usb));
        Assert.Contains("Tastatură externă", DeviceMonitorReport.Line(usb));
        Assert.False(DeviceMonitorReport.ForbiddenSeen(usb, isolated: true));

        var bth = new DeviceJournalEntry { Identity = "ID|bth", InstanceId = @"BTHENUM\DEV_001122334455\7&1&0&BLUETOOTHDEVICE_001122334455", Class = DeviceClass.Mouse, FirstSeenUtc = T0 };
        Assert.Equal(DeviceClass.Bluetooth, DeviceMonitorRules.DisplayClass(bth));
        Assert.StartsWith("mouse prin Bluetooth", DeviceMonitorRules.DisplayDetail(bth));
    }
}
