using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Real-PC finding 2026-10-22 (page "Monitor dispozitive"): one Bluetooth headset showed as about twelve red rows (one per service / profile / GATT node), the
/// BLE mouse was named after a GATT service, and the station's own USB Bluetooth radio was listed as "Dispozitiv Bluetooth". Fixtures are modeled on the real
/// instance ids with the Bluetooth addresses anonymised (AABBCCDDEEFF, 112233445566). Everything is decided from ids and the PnP tree, never from a name.
/// </summary>
public sealed class DeviceMonitorBluetoothFoldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private const string HeadsetBox = "{AAAAAAAA-0000-0000-0000-00000000B001}";
    private const string MouseBox = "{AAAAAAAA-0000-0000-0000-00000000B002}";
    private const string Mac = "AABBCCDDEEFF";
    private const string Mac2 = "112233445566";

    private static PnpSnapshotEntry Svc(string uuid, string name, string cont, string mac = Mac) =>
        new($@"BTHENUM\{{{uuid}}}_VID&0002054C_PID&0EAF\7&35A2E243&0&{mac}_C00000000", name, "Bluetooth", "Fake", "", "", 0, cont, @"BTH\MS_BTHBRB\6&FAKE&0&1");

    private static PnpSnapshotEntry HeadsetNode(string cont, string mac = Mac, string name = "WH-FAKE") =>
        new($@"BTHENUM\DEV_{mac}\7&35A2E243&0&BLUETOOTHDEVICE_{mac}", name, "Bluetooth", "Fake", "", "", 0, cont, @"BTH\MS_BTHBRB\6&FAKE&0&1");

    private static PnpSnapshotEntry[] HeadsetNodes(string cont, string mac = Mac, string name = "WH-FAKE") =>
    [
        Svc("0000110C-0000-1000-8000-00805F9B34FB", name + " Avrcp Transport", cont, mac),
        Svc("956C7B26-D49A-4BA8-B03F-B17D393CB6E2", "SPP Serial Port", cont, mac),
        Svc("81C2E72A-0591-443E-A1FF-05F988593351", "GSOUND_BT_AUDIO", cont, mac),
        Svc("931C7E8A-540F-4686-B798-E8DF0A2AD9F7", "Bluetooth Peripheral Device", cont, mac),
        HeadsetNode(cont, mac, name),
    ];

    private static string Gatt(string svc, string mac = Mac) => $@"BTHLEDEVICE\{{{svc}}}_DEV_VID&02FEED_PID&0001_REV&0001_{mac}\8&FAKE&0&00";

    private static PnpSnapshotEntry BleDeviceNode(string cont, string mac = Mac, string name = "FAKE MOUSE") =>
        new($@"BTHLE\DEV_{mac}\7&115F2FE1&0&{mac}", name, "Bluetooth", "Fake", "", "BthLEEnum", 0, cont, @"BTH\MS_BTHLE\6&FAKE&0&3");

    private static PnpSnapshotEntry BleGatt(string svc, string name, string cont, string mac = Mac, string suffix = "13") =>
        new(Gatt(svc, mac) + suffix, name, "Bluetooth", "Fake", "", "UmPass", 0, cont, $@"BTHLE\DEV_{mac}\7&115F2FE1&0&{mac}");

    // ---- live ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_service_of_one_headset_is_one_device_named_by_the_device_node()
    {
        var nodes = HeadsetNodes(HeadsetBox).Reverse().ToArray();   // the device node last, then first: the order must not matter
        foreach (var order in new[] { nodes, nodes.Reverse().ToArray() })
        {
            var d = Assert.Single(DeviceScope.Classify(order));
            Assert.Equal(DeviceClass.Bluetooth, d.Kind);
            Assert.Equal($@"BTHENUM\DEV_{Mac}\7&35A2E243&0&BLUETOOTHDEVICE_{Mac}", d.Primary.InstanceId);
            Assert.Equal("WH-FAKE", d.Primary.Name);
        }
    }

    [Fact]
    public void A_BLE_device_with_GATT_services_listed_first_is_named_and_keyed_by_the_BTHLE_device_node_not_by_a_service()
    {
        var gatt1 = BleGatt("0000180F-0000-1000-8000-00805F9B34FB", "Bluetooth LE Generic Attribute Service", MouseBox);
        var gatt2 = BleGatt("00001801-0000-1000-8000-00805F9B34FB", "Generic Attribute Profile", MouseBox, suffix: "0A");
        var dev = BleDeviceNode(MouseBox);
        foreach (var order in new[] { new[] { gatt1, gatt2, dev }, new[] { dev, gatt2, gatt1 }, new[] { gatt2, dev, gatt1 } })
        {
            var d = Assert.Single(DeviceScope.Classify(order));
            Assert.Equal(dev.InstanceId, d.Primary.InstanceId);
            var j = new DeviceJournal();
            DeviceJournalMerge.Apply(j, order, null, "u", T0, initial: false);
            var row = Assert.Single(j.Devices);
            Assert.Equal("ID|" + dev.InstanceId.ToUpperInvariant(), row.Identity);
            Assert.Equal("FAKE MOUSE", row.Name);
        }
    }

    [Fact]
    public void Nodes_of_one_remote_address_without_a_container_id_group_by_the_address_and_the_parent_chain()
    {
        var nodes = HeadsetNodes("").Concat([
            BleGatt("0000180F-0000-1000-8000-00805F9B34FB", "Generic Attribute Service", "", Mac2),
            BleDeviceNode("", Mac2),
            // a HID child with no Bluetooth address in its own id: reached through its parent
            new PnpSnapshotEntry(@"HID\FAKEHIDNODE\9&FAKE&0&0000", "HID Keyboard Device", "Keyboard", "x", "", "kbdhid", 0, "", $@"BTHLE\DEV_{Mac2}\7&115F2FE1&0&{Mac2}"),
        ]).ToArray();
        var devices = DeviceScope.Classify(nodes);
        Assert.Equal(2, devices.Count);
        Assert.All(devices, d => Assert.Equal(DeviceClass.Bluetooth, d.Kind));
        Assert.Contains(devices, d => d.Members.Count == 5 && d.Primary.InstanceId.Contains(Mac));
        Assert.Contains(devices, d => d.Members.Count == 3 && d.Primary.InstanceId.Contains(Mac2));
    }

    [Fact]
    public void A_service_without_a_container_id_joins_the_container_group_of_the_same_address()
    {
        var withContainer = HeadsetNodes(HeadsetBox);
        var noContainer = Svc("DF21FE2C-2515-4FDB-8886-F12C4D67927C", "BTFASTPAIR", "");
        var d = Assert.Single(DeviceScope.Classify([.. withContainer, noContainer]));
        Assert.Equal(withContainer.Length + 1, d.Members.Count);
    }

    [Fact]
    public void Two_different_addresses_are_never_one_row_even_with_the_same_name_and_VID_PID()
    {
        var a = HeadsetNodes("", Mac, "SAME NAME");
        var b = HeadsetNodes("", Mac2, "SAME NAME");
        var devices = DeviceScope.Classify([.. a, .. b]);
        Assert.Equal(2, devices.Count);
        Assert.NotEqual(devices[0].Primary.InstanceId, devices[1].Primary.InstanceId);
    }

    [Fact]
    public void Malformed_Bluetooth_ids_never_crash_and_fail_closed_to_separate_rows()
    {
        string[] bad =
        [
            @"BTHENUM\DEV_AABBCCDDEE\7&FAKE&0&BLUETOOTHDEVICE_AABBCCDDEE",                              // short address
            @"BTHENUM\DEV_AABBCCDDEEFF00\7&FAKE&0&BLUETOOTHDEVICE_AABBCCDDEEFF00",                      // long address
            @"BTHENUM\{0000110C-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0EAF\7&FAKE&0&AABBCCDDEEFF_C00000000_EXTRA",   // extra suffix
            @"BTHENUM\{0000110C-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0EAF\7&FAKE&0&AABBCCDDEEXY_C00000000",         // not hex
            @"BTHENUM\DEV_\7&FAKE&0&X",
            @"BTHLE\DEV_AABBCCDDEEFF",                                                                  // no instance part
            @"BTHENUM\",
            "",
        ];
        foreach (var id in bad) Assert.Equal("", DeviceScope.BluetoothAddress(id));
        var nodes = bad.Where(b => b.Length > 0).Select((id, i) => new PnpSnapshotEntry(id, "Bad " + i, "Bluetooth", "x", "", "", 0, "")).ToArray();
        var devices = DeviceScope.Classify(nodes);   // must not throw
        Assert.True(devices.Count <= nodes.Length);
        Assert.All(devices, d => Assert.Single(d.Members));
        Assert.Equal("AABBCCDDEEFF", DeviceScope.BluetoothAddress($@"BTHENUM\DEV_{Mac.ToLowerInvariant()}\7&fake&0&bluetoothdevice_{Mac.ToLowerInvariant()}"));   // ids are case-insensitive in Windows
    }

    [Fact]
    public void One_connected_headset_raises_one_connection_and_one_row_however_many_services_it_has()
    {
        var j = new DeviceJournal();
        var changes = DeviceJournalMerge.Apply(j, HeadsetNodes(HeadsetBox), null, "u", T0, initial: false);
        Assert.Single(changes);
        Assert.Single(j.Devices);
        Assert.Equal(1, j.Devices.Count(DeviceScope.IsShown));
    }

    // ---- old rows --------------------------------------------------------------------------------------------------------------------------------

    private static DeviceJournalEntry Row(PnpSnapshotEntry n, bool present, int version = 0, DeviceClass cls = DeviceClass.Bluetooth) => new()
    {
        Identity = "ID|" + n.InstanceId.ToUpperInvariant(), InstanceId = n.InstanceId, Name = n.Name, Class = cls, SetupClass = n.SetupClass, ClassifierVersion = version,
        Present = present, FirstSeenUtc = T0, LastConnectedUtc = T0, LastDisconnectedUtc = present ? null : T0.AddHours(15),
    };

    [Fact]
    public void Old_service_rows_of_one_address_fold_under_the_device_row_for_display_and_nothing_is_deleted_or_rekeyed()
    {
        var nodes = HeadsetNodes(HeadsetBox);
        var rows = nodes.Select(n => Row(n, present: false)).ToList();
        var deviceRow = rows.Single(r => r.InstanceId.Contains(@"\DEV_"));
        deviceRow.Present = true; deviceRow.ClassifierVersion = DeviceScope.ClassifierVersion; deviceRow.LastDisconnectedUtc = null;
        var j = new DeviceJournal { Devices = rows };
        var identities = rows.Select(r => r.Identity).ToList();
        var classes = rows.Select(r => r.Class).ToList();
        var folded = DeviceJournalMerge.MarkSuperseded(j);
        Assert.Equal(nodes.Length - 1, folded);
        Assert.Same(deviceRow, Assert.Single(j.Devices.Where(DeviceScope.IsShown)));
        Assert.Equal(identities, j.Devices.Select(r => r.Identity));          // not re-keyed, not deleted
        Assert.Equal(classes, j.Devices.Select(r => r.Class));                // the sealed kind of each row is untouched (check D05 compares every row)
        Assert.All(j.Devices.Where(r => r != deviceRow), r => { Assert.Equal(deviceRow.Identity, r.SupersededBy); Assert.True(DeviceScope.IsInScope(r)); });
        Assert.Equal(nodes.Length, DeviceJournalMerge.FoldedIdentities(j, deviceRow.Identity).Count);
    }

    [Fact]
    public void Old_service_rows_fold_under_the_device_row_also_when_the_device_is_disconnected()
    {
        var rows = HeadsetNodes(HeadsetBox).Select(n => Row(n, present: false)).ToList();
        var j = new DeviceJournal { Devices = rows };
        DeviceJournalMerge.MarkSuperseded(j);
        var shown = Assert.Single(j.Devices.Where(DeviceScope.IsShown));
        Assert.Contains(@"\DEV_", shown.InstanceId);
    }

    [Fact]
    public void Rows_of_different_addresses_never_fold_even_with_the_same_name_and_VID_PID()
    {
        var a = HeadsetNodes("", Mac, "SAME NAME").Select(n => Row(n, present: false)).ToList();
        var b = HeadsetNodes("", Mac2, "SAME NAME").Select(n => Row(n, present: false)).ToList();
        var j = new DeviceJournal { Devices = [.. a, .. b] };
        DeviceJournalMerge.MarkSuperseded(j);
        var shown = j.Devices.Where(DeviceScope.IsShown).ToList();
        Assert.Equal(2, shown.Count);
        Assert.All(shown, r => Assert.Contains(@"\DEV_", r.InstanceId));
        Assert.All(j.Devices.Where(r => r.SupersededBy.Length > 0), r =>
            Assert.Equal(DeviceScope.BluetoothAddress(r.InstanceId), DeviceScope.BluetoothAddress(j.Find(r.SupersededBy)!.InstanceId)));
    }

    [Fact]
    public void The_fold_keeps_its_guards_different_registration_numbers_a_serial_clone_and_a_missing_device_row()
    {
        var nodes = HeadsetNodes(HeadsetBox);
        // different registration numbers: the service row stays visible
        var rows = nodes.Select(n => Row(n, present: false)).ToList();
        rows.Single(r => r.InstanceId.Contains(@"\DEV_")).RegistrationNumber = "REG-1";
        rows.First(r => !r.InstanceId.Contains(@"\DEV_")).RegistrationNumber = "REG-2";
        var j = new DeviceJournal { Devices = rows };
        DeviceJournalMerge.MarkSuperseded(j);
        Assert.Equal(2, j.Devices.Count(DeviceScope.IsShown));
        // a service row in a serial conflict is never folded
        var rows2 = nodes.Select(n => Row(n, present: false)).ToList();
        var clone = rows2.First(r => !r.InstanceId.Contains(@"\DEV_"));
        clone.Serial = "SYNTH0001"; clone.VidPid = "054C:0EAF"; clone.Manufacturer = "Sony";
        rows2.Add(new DeviceJournalEntry
        {
            Identity = "SN|054C:0EAF|SYNTH0001|DUP|1", InstanceId = @"USB\VID_054C&PID_0EAF\SYNTH0001", Name = "Other", Class = DeviceClass.Bluetooth, Serial = "SYNTH0001",
            VidPid = "1234:0EAF", Present = false, FirstSeenUtc = T0,
        });
        var j2 = new DeviceJournal { Devices = rows2 };
        DeviceJournalMerge.MarkSuperseded(j2);
        Assert.Contains(rows2, o => DeviceJournalMerge.SerialConflict(clone, o));
        Assert.Equal("", clone.SupersededBy);
        // no device row for the address: service rows are not folded into each other
        var only = nodes.Where(n => !n.InstanceId.Contains(@"\DEV_")).Select(n => Row(n, present: false)).ToList();
        var j3 = new DeviceJournal { Devices = only };
        DeviceJournalMerge.MarkSuperseded(j3);
        Assert.Equal(only.Count, j3.Devices.Count(DeviceScope.IsShown));
    }

    [Fact]
    public void Old_GATT_rows_fold_under_the_BTHLE_device_row_and_a_stale_connected_service_row_stays_visible_until_it_is_disconnected()
    {
        var dev = Row(BleDeviceNode(MouseBox), present: false, version: 2);
        var g1 = Row(BleGatt("00001801-0000-1000-8000-00805F9B34FB", "Generic Attribute Profile", MouseBox), present: false);
        var g2 = Row(BleGatt("0000180F-0000-1000-8000-00805F9B34FB", "Bluetooth LE Generic Attribute Service", MouseBox, suffix: "0A"), present: true, version: 3);
        var j = new DeviceJournal { Devices = [g1, g2, dev] };
        DeviceJournalMerge.MarkSuperseded(j);
        Assert.Equal(dev.Identity, g1.SupersededBy);
        Assert.Equal("", g2.SupersededBy);     // connected rows are always listed; the next reading disconnects it
        // the next reading: the device node is the row now
        DeviceJournalMerge.Apply(j, [BleGatt("0000180F-0000-1000-8000-00805F9B34FB", "Bluetooth LE Generic Attribute Service", MouseBox, suffix: "0A"), BleDeviceNode(MouseBox)], null, "u", T0.AddDays(1), initial: false);
        Assert.Equal(dev.Identity, g2.SupersededBy);
        var shown = Assert.Single(j.Devices.Where(DeviceScope.IsShown));
        Assert.Same(dev, shown);
        Assert.True(dev.Present);
    }

    // ---- the station's own radio -----------------------------------------------------------------------------------------------------------------

    private const string Radio = @"USB\VID_0BDA&PID_4853\00E04C000001";

    private static PnpSnapshotEntry RadioNode(string cont) =>
        new(Radio, "Fake Bluetooth Adapter", "Bluetooth", "Fake", "", "BTHUSB", 0, cont, @"USB\ROOT_HUB30\4&FAKE&0&0");

    private static DeviceJournalEntry OldRadioRow() => new()
    {
        Identity = "SN|0BDA:4853|00E04C000001", InstanceId = Radio, Name = "Fake Bluetooth Adapter", Class = DeviceClass.Bluetooth, SetupClass = "Bluetooth", VidPid = "0BDA:4853",
        Serial = "00E04C000001", ClassifierVersion = 0, Present = false, FirstSeenUtc = T0, LastDisconnectedUtc = T0.AddHours(1),
    };

    private static Func<IReadOnlyDictionary<string, SealedKind>?> Sealed(int version) =>
        () => new Dictionary<string, SealedKind>(StringComparer.OrdinalIgnoreCase) { ["SN|0BDA:4853|00E04C000001"] = new("Bluetooth", version) };

    [Fact]
    public void The_old_row_of_the_stations_own_radio_is_hidden_only_when_the_live_snapshot_and_the_sealed_audit_attest_it()
    {
        // unknown (no snapshot attests it): still listed - it cannot be told from a dongle
        var j0 = new DeviceJournal { Devices = [OldRadioRow()] };
        DeviceJournalMerge.MarkSuperseded(j0);
        Assert.Single(j0.Devices, DeviceScope.IsShown);
        // the node is in the local machine container now, the row is a legacy one, the sealed audit says legacy too
        var j = new DeviceJournal { Devices = [OldRadioRow()] };
        DeviceJournalMerge.Apply(j, [RadioNode(DeviceScope.LocalMachineContainer)], null, "u", T0.AddDays(1), initial: false, sealedKinds: Sealed(1));
        var row = Assert.Single(j.Devices);                                   // never deleted, still in scope for D05 and the report
        Assert.True(DeviceScope.IsInScope(row));
        Assert.Equal(DeviceClass.Bluetooth, row.Class);
        Assert.False(DeviceScope.IsShown(row));
        Assert.False(row.Present);
        // no sealed audit readable, or the sealed audit says a container-aware version wrote it: fail closed, shown
        var j1 = new DeviceJournal { Devices = [OldRadioRow()] };
        DeviceJournalMerge.Apply(j1, [RadioNode(DeviceScope.LocalMachineContainer)], null, "u", T0.AddDays(1), initial: false);
        Assert.True(DeviceScope.IsShown(j1.Devices[0]));
        var j2 = new DeviceJournal { Devices = [OldRadioRow()] };
        DeviceJournalMerge.Apply(j2, [RadioNode(DeviceScope.LocalMachineContainer)], null, "u", T0.AddDays(1), initial: false, sealedKinds: Sealed(3));
        Assert.True(DeviceScope.IsShown(j2.Devices[0]));
        // the next reading without the attesting snapshot: the display is recomputed, not remembered
        DeviceJournalMerge.Apply(j, [], null, "u", T0.AddDays(2), initial: false, sealedKinds: Sealed(1));
        Assert.True(DeviceScope.IsShown(row));
    }

    [Fact]
    public void A_hand_edited_flag_in_the_journal_file_hides_nothing()
    {
        var headset = Row(HeadsetNode(HeadsetBox), present: false, version: 3);
        var json = System.Text.Json.JsonSerializer.Serialize(headset);
        Assert.DoesNotContain("station_internal", json);                      // never stored
        var edited = System.Text.Json.JsonSerializer.Deserialize<DeviceJournalEntry>(json.TrimEnd('}') + ",\"station_internal\":true}")!;
        Assert.True(DeviceScope.IsShown(edited));
        var j = new DeviceJournal { Devices = [edited] };
        DeviceJournalMerge.Apply(j, [], null, "u", T0.AddDays(1), initial: false, sealedKinds: Sealed(1));
        Assert.True(DeviceScope.IsShown(edited));
    }

    [Fact]
    public void A_dongle_seen_with_its_own_container_is_never_hidden_when_the_same_instance_id_later_shows_in_the_local_container()
    {
        var row = OldRadioRow();
        var j = new DeviceJournal { Devices = [row] };
        var changes = DeviceJournalMerge.Apply(j, [RadioNode("{AAAAAAAA-0000-0000-0000-00000000B777}")], null, "u", T0.AddDays(2), initial: false, sealedKinds: Sealed(1));
        Assert.True(row.Present);
        Assert.True(DeviceScope.IsShown(row));
        Assert.Contains(changes, c => c.Arrived && ReferenceEquals(c.Device, row));
        DeviceJournalMerge.Apply(j, [], null, "u", T0.AddDays(3), initial: false, sealedKinds: Sealed(1));
        Assert.False(row.Present);
        DeviceJournalMerge.Apply(j, [RadioNode(DeviceScope.LocalMachineContainer)], null, "u", T0.AddDays(4), initial: false, sealedKinds: Sealed(1));
        Assert.True(DeviceScope.IsShown(row));                                // it was an outside device once: it stays listed
    }

    [Fact]
    public void A_remote_device_named_like_a_built_in_component_is_still_one_shown_row()
    {
        var rows = HeadsetNodes(HeadsetBox, Mac, "Built-in Speaker").Select(n => Row(n, present: false)).ToList();
        var j = new DeviceJournal { Devices = rows };
        DeviceJournalMerge.MarkSuperseded(j);
        var shown = Assert.Single(j.Devices, DeviceScope.IsShown);
        Assert.Contains(@"\DEV_", shown.InstanceId);
    }

    [Fact]
    public void An_all_zero_or_all_F_address_is_no_address_and_two_radios_are_not_grouped()
    {
        Assert.Equal("", DeviceScope.BluetoothAddress(@"BTHENUM\DEV_000000000000\7&FAKE&0&BLUETOOTHDEVICE_000000000000"));
        Assert.Equal("", DeviceScope.BluetoothAddress(@"BTHLE\DEV_FFFFFFFFFFFF\7&FAKE&0&FFFFFFFFFFFF"));
        var a = new PnpSnapshotEntry(@"BTHENUM\DEV_000000000000\7&AAAA&0&BLUETOOTHDEVICE_000000000000", "Radio A", "Bluetooth", "x", "", "", 0, "");
        var b = new PnpSnapshotEntry(@"BTHLE\DEV_000000000000\7&BBBB&0&000000000000", "Radio B", "Bluetooth", "x", "", "", 0, "");
        Assert.All(DeviceScope.Classify([a, b]), d => Assert.Single(d.Members));
    }
}
