using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Display-fold residuals (owner decision 2026-10-22, fail-closed: an extra visible row is acceptable, a wrongly hidden one is not): the fold between two kinds only
/// follows a KNOWN reclassification of the classifier; a row with a cloned real serial is never folded in steps 1b, 2 and 3. Synthetic ids only.
/// </summary>
public sealed class DeviceMonitorFoldResidualTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static DeviceJournalEntry Connected(DeviceClass cls, int version, string vidPid = "1532:009C") => new()
    {
        Identity = $@"ID|USB\VID_{vidPid[..4]}&PID_{vidPid[5..]}\6&FAKE01&0&2", InstanceId = $@"USB\VID_{vidPid[..4]}&PID_{vidPid[5..]}\6&FAKE01&0&2",
        Name = "USB Input Device", Class = cls, VidPid = vidPid, GenericSerial = true, ClassifierVersion = version, Present = true, FirstSeenUtc = T0.AddDays(3), LastConnectedUtc = T0.AddDays(3),
    };

    private static DeviceJournalEntry Legacy(DeviceClass cls, int version, string mi = "MI_01&COL01", string vidPid = "1532:009C") => new()
    {
        Identity = $@"ID|HID\VID_{vidPid[..4]}&PID_{vidPid[5..]}&{mi}\7&AAAA&0&0000", InstanceId = $@"HID\VID_{vidPid[..4]}&PID_{vidPid[5..]}&{mi}\7&AAAA&0&0000",
        Name = "HID Device", Class = cls, VidPid = vidPid, ClassifierVersion = version, Present = false, FirstSeenUtc = T0, LastConnectedUtc = T0, LastDisconnectedUtc = T0.AddHours(1),
    };

    private static int Fold(params DeviceJournalEntry[] rows)
    {
        var j = new DeviceJournal { Devices = [.. rows] };
        return DeviceJournalMerge.MarkSuperseded(j);
    }

    // ---- 1-3: the kind change must be a known reclassification ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_legacy_network_adapter_interface_is_not_folded_under_a_connected_keyboard(int legacyVersion)
    {
        var legacy = Legacy(DeviceClass.NetworkAdapter, legacyVersion, "MI_02");
        var keyboard = Connected(DeviceClass.Keyboard, 3);
        Assert.Equal(0, Fold(legacy, keyboard));
        Assert.Equal("", legacy.SupersededBy);
        Assert.Equal("", keyboard.SupersededBy);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    public void A_legacy_keyboard_interface_still_folds_under_the_connected_mouse_of_the_same_device(int legacyVersion, int targetVersion)
    {
        var legacy = Legacy(DeviceClass.Keyboard, legacyVersion);
        var mouse = Connected(DeviceClass.Mouse, targetVersion);
        Assert.Equal(1, Fold(legacy, mouse));
        Assert.Equal(mouse.Identity, legacy.SupersededBy);
        Assert.True(DeviceScope.IsInScope(legacy));   // D05 / the report still compare the row
        Assert.Equal(DeviceClass.Keyboard, legacy.Class);
    }

    [Fact]
    public void A_legacy_keyboard_folds_under_a_connected_bluetooth_row_only_when_the_pair_is_known()
    {
        var legacy = Legacy(DeviceClass.Keyboard, 1);
        Assert.True(DeviceScope.IsKnownReclassification(1, 3, DeviceClass.Keyboard, DeviceClass.Bluetooth));
        Assert.Equal(1, Fold(legacy, Connected(DeviceClass.Bluetooth, 3)));
        // 1 -> 2 knows no Bluetooth pair: not folded
        Assert.False(DeviceScope.IsKnownReclassification(1, 2, DeviceClass.Keyboard, DeviceClass.Bluetooth));
        Assert.Equal(0, Fold(Legacy(DeviceClass.Keyboard, 1), Connected(DeviceClass.Bluetooth, 2)));
        // a Hid row is no known pair either
        Assert.Equal(0, Fold(Legacy(DeviceClass.Hid, 1), Connected(DeviceClass.Mouse, 3)));
    }

    [Fact]
    public void The_same_instance_fold_follows_the_same_rule()
    {
        DeviceJournalEntry Old(DeviceClass c) => new() { Identity = "SN|OLD", InstanceId = @"USB\VID_1532&PID_009C\6&FAKE01&0&2", Name = "x", Class = c, VidPid = "1532:009C", ClassifierVersion = 0, Present = false, FirstSeenUtc = T0 };
        Assert.Equal(0, Fold(Old(DeviceClass.NetworkAdapter), Connected(DeviceClass.Keyboard, 3)));
        Assert.Equal(1, Fold(Old(DeviceClass.Keyboard), Connected(DeviceClass.Mouse, 3)));
    }

    // ---- 4: step 3 (the USB node of a storage enclosure) ----------------------------------------------------------------------------------------

    private static DeviceJournalEntry Bridge(string vidPid = "9A9B:9C9D") => new()
    {
        Identity = $"SN|{vidPid}|FAKESSD0042", InstanceId = $@"USB\VID_{vidPid[..4]}&PID_{vidPid[5..]}\FAKESSD0042", Name = "USB Mass Storage Device", Class = DeviceClass.Phone,
        VidPid = vidPid, Serial = "FAKESSD0042", Present = false, FirstSeenUtc = T0,
    };

    private static DeviceJournalEntry Disk(long cap = 250_000_000_000, string id = "SN|FAKEVEND SSD|FAKESSD0042") => new()
    {
        Identity = id, InstanceId = @"USBSTOR\DISK&VEN_FAKEVEND&PROD_SSD&REV_1.02\FAKESSD0042&0", Name = "Fakevend SSD USB Device", Manufacturer = "FAKEVEND", Class = DeviceClass.ExternalDisk,
        Serial = "FAKESSD0042", CapacityBytes = cap, Present = false, FirstSeenUtc = T0,
    };

    [Fact]
    public void The_storage_enclosure_node_folds_onto_the_disk_when_nothing_conflicts()
    {
        var node = Bridge(); var disk = Disk();
        Assert.Equal(1, Fold(node, disk));
        Assert.Equal(disk.Identity, node.SupersededBy);
    }

    [Fact]
    public void The_storage_enclosure_node_with_a_cloned_serial_is_not_folded_onto_the_disk()
    {
        var node = Bridge(); var other = Bridge("7777:0001"); var disk = Disk();
        Fold(node, other, disk);
        Assert.True(DeviceJournalMerge.SerialConflict(node, other));
        Assert.Equal("", node.SupersededBy);
        Assert.Equal("", other.SupersededBy);
    }

    [Fact]
    public void The_storage_enclosure_node_is_not_folded_onto_a_disk_whose_serial_conflicts_with_another_row()
    {
        var node = Bridge(); var disk = Disk(); var cloneDisk = Disk(64_000_000_000, "SN|FAKEVEND SSD|FAKESSD0042|DUP|1");
        Fold(node, disk, cloneDisk);
        Assert.True(DeviceJournalMerge.SerialConflict(disk, cloneDisk));
        Assert.Equal("", node.SupersededBy);
    }

    // ---- 5: steps 1b and 2 ---------------------------------------------------------------------------------------------------------------------

    private static (DeviceJournalEntry Clone, DeviceJournalEntry Target) CloneAndTarget()
    {
        var clone = Connected(DeviceClass.Mouse, 2, "4444:0001"); clone.GenericSerial = false; clone.Serial = "FAKECLONE01"; clone.InstanceId = clone.InstanceId.Replace("FAKE01", "FAKE03"); clone.Identity += "C";
        var target = Connected(DeviceClass.Mouse, 2); target.Identity += "X"; target.InstanceId = target.InstanceId.Replace("FAKE01", "FAKE02");
        return (clone, target);
    }

    [Fact]
    public void A_legacy_interface_row_with_a_cloned_real_serial_is_not_folded_in_step_1b()
    {
        var legacy = Legacy(DeviceClass.Keyboard, 1); legacy.Serial = "FAKECLONE01";
        var (clone, target) = CloneAndTarget();
        Fold(legacy, clone, target);
        Assert.True(DeviceJournalMerge.SerialConflict(legacy, clone));
        Assert.Equal("", legacy.SupersededBy);
    }

    [Fact]
    public void An_interface_row_with_a_cloned_real_serial_is_not_folded_in_step_2()
    {
        var iface = Legacy(DeviceClass.Keyboard, 1); iface.Present = true; iface.Serial = "FAKECLONE01";
        var (clone, target) = CloneAndTarget();
        Fold(iface, clone, target);
        Assert.Equal("", iface.SupersededBy);
    }

    [Fact]
    public void An_interface_row_without_a_serial_conflict_still_folds_in_step_2()
    {
        var iface = Legacy(DeviceClass.Keyboard, 1); iface.Present = true;
        var target = Connected(DeviceClass.Mouse, 2);
        Assert.Equal(1, Fold(iface, target));
        Assert.Equal(target.Identity, iface.SupersededBy);
    }

    // ---- review round 1 of the fail-closed fold: step 2 must not cross families on its other paths ----------------------------------------------

    private static DeviceJournalEntry OldSerialRow(DeviceClass cls, string serial = "SYNTHKB01") => new()
    {
        Identity = "SN|1532:009C|" + serial, InstanceId = @"USB\VID_1532&PID_009C\" + serial, Name = "USB Input Device", Class = cls, VidPid = "1532:009C",
        Serial = serial, ClassifierVersion = 0, Present = false, FirstSeenUtc = T0, LastConnectedUtc = T0, LastDisconnectedUtc = T0.AddHours(1),
    };

    [Fact]
    public void Step_2_with_a_single_old_target_does_not_fold_another_family_under_it()
    {
        var target = OldSerialRow(DeviceClass.Keyboard);
        var net = Legacy(DeviceClass.NetworkAdapter, 0, "MI_02");
        var mouse = Legacy(DeviceClass.Mouse, 0, "MI_00");
        Fold(target, net, mouse);
        Assert.Equal("", net.SupersededBy);                       // a Critical kind on an isolated station is never hidden under a keyboard
        Assert.Equal(target.Identity, mouse.SupersededBy);        // the keyboard/mouse family of the old classifier still folds (station shape)
    }

    [Fact]
    public void Step_2_without_a_target_folds_only_within_the_family_of_the_first_interface()
    {
        var kb = Legacy(DeviceClass.Keyboard, 0, "MI_00");
        var mouse = Legacy(DeviceClass.Mouse, 0, "MI_01");
        var bt = Legacy(DeviceClass.Bluetooth, 0, "MI_02");
        Fold(kb, mouse, bt);
        Assert.Equal("", kb.SupersededBy);
        Assert.Equal(kb.Identity, mouse.SupersededBy);
        Assert.Equal("", bt.SupersededBy);
    }

    [Fact]
    public void Step_2_with_two_current_targets_does_not_fold_a_row_step_1b_refused()
    {
        var a = Connected(DeviceClass.Keyboard, 3);
        var b = Connected(DeviceClass.Keyboard, 3); b.Identity = b.Identity.Replace("FAKE01", "FAKE02"); b.InstanceId = b.InstanceId.Replace("FAKE01", "FAKE02");
        var kb = Legacy(DeviceClass.Keyboard, 1, "MI_00");
        var net = Legacy(DeviceClass.NetworkAdapter, 1, "MI_02");
        Fold(a, b, kb, net);
        Assert.Equal("", net.SupersededBy);
    }

    [Fact]
    public void A_v2_keyboard_row_folds_under_its_v3_bluetooth_row_when_the_pair_is_known()
    {
        Assert.True(DeviceScope.IsKnownReclassification(2, 3, DeviceClass.Keyboard, DeviceClass.Bluetooth));
        var legacy = Legacy(DeviceClass.Keyboard, 2);
        var bt = Connected(DeviceClass.Bluetooth, 3);
        Assert.Equal(1, Fold(legacy, bt));
        Assert.Equal(bt.Identity, legacy.SupersededBy);
        // the same version on both sides is never a reclassification
        Assert.Equal(0, Fold(Legacy(DeviceClass.Keyboard, 3), Connected(DeviceClass.Bluetooth, 3)));
    }
}
