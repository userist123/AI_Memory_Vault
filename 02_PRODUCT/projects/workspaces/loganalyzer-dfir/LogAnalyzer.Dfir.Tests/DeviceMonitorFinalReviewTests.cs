using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The last review round of the device monitor (2026-10-22; probes p4 and p5 of the reviewer), neutral fixtures with invented ids:
/// 1. a built-in component is decided ONLY by what the PnP stack attests (every node not removable), never by a name the device sets itself;
/// 2. a reclassification is never laundered: old kind and version come from the SEALED audit, the change must be one the version bump is known to make, and the
///    old rule re-run on the live snapshot must give the sealed old kind;
/// 3. a registration number on a medium without a unique serial is bound to the medium it was set for.
/// </summary>
public sealed class DeviceMonitorFinalReviewTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private const string C = "{CCCCCCCC-0000-0000-0000-00000000A001}";
    private static string K(string identity) => OperationAudit.DeviceField(identity);

    // ---- 1. integrated or built-in: only from data the device cannot set -------------------------------------------------------------------------

    private static readonly PnpSnapshotEntry Root = new(@"USB\VID_1234&PID_5678\INJ0001", "USB Composite Device", "USB", "(Standard USB Host Controller)", @"USB\VID_1234&PID_5678", "usbccgp", 0, C);
    private static readonly PnpSnapshotEntry Kbd0 = new(@"HID\VID_1234&PID_5678&MI_00\8&AAA&0&0000", "HID Keyboard Device", "Keyboard", "(Standard keyboards)", @"HID\VID_1234&PID_5678&MI_00", "kbdhid", 0, C, @"USB\VID_1234&PID_5678&MI_00\7&BBB&0&0000");
    private static readonly PnpSnapshotEntry Mouse0 = Kbd0 with { Name = "HID-compliant mouse", SetupClass = "Mouse", Manufacturer = "Microsoft", Service = "mouhid" };
    private static readonly PnpSnapshotEntry Kbd1 = Kbd0 with { InstanceId = @"HID\VID_1234&PID_5678&MI_01\8&AAA&0&0000", HardwareIds = @"HID\VID_1234&PID_5678&MI_01" };
    private static readonly PnpSnapshotEntry NamedCamera = new(@"USB\VID_1234&PID_5678&MI_02\7&EEE&0&0002", "Integrated Camera", "", "", @"USB\VID_1234&PID_5678&MI_02", "", 28, C);

    [Fact]
    public void P4_c3_a_keyboard_injector_with_an_interface_named_Integrated_Camera_is_journaled_and_alerted()
    {
        PnpSnapshotEntry[] snap = [Root, Kbd0, NamedCamera];
        Assert.Equal(DeviceClass.Keyboard, Assert.Single(DeviceScope.Classify(snap)).Kind);
        var j = new DeviceJournal();
        var changes = DeviceJournalMerge.Apply(j, snap, null, "u", T0, initial: false);
        var row = Assert.Single(j.Devices);
        Assert.Equal(DeviceClass.Keyboard, row.Class);
        var c = Assert.Single(changes);
        Assert.True(c.Arrived);
        Assert.Same(row, c.Device);
        // the same with the vendor strings on the keyboard node itself, and with every node reporting „removable” or nothing at all
        Assert.Single(DeviceScope.Classify([Root with { Removable = true }, Kbd0 with { Name = "Built-in Keyboard", Removable = true }]));
        Assert.Single(DeviceScope.Classify([Root with { Name = "Integrated Camera" }, Kbd0 with { Name = "Integrated Camera" }]));
    }

    [Fact]
    public void A_group_is_internal_only_when_every_node_is_attested_not_removable()
    {
        // the real internal camera (the PnP stack attests it): hidden, whatever its name
        var cam = new PnpSnapshotEntry(@"USB\VID_13D3&PID_5666&MI_00\6&CCCC0002&0&0000", "USB Video Device", "Camera", "Fakecam", @"USB\VID_13D3&PID_5666&MI_00", "usbvideo", 0, "{AAAA0000-0000-0000-0000-00000000F006}", Removable: false);
        Assert.Empty(DeviceScope.Classify([cam]));
        Assert.Empty(DeviceScope.Classify([cam with { Name = "Integrated Camera" }]));
        // one external indicator on any node of the group (or no indication at all) and the device is journaled
        Assert.Equal(DeviceClass.Camera, Assert.Single(DeviceScope.Classify([cam with { Removable = true }])).Kind);
        Assert.Equal(DeviceClass.Camera, Assert.Single(DeviceScope.Classify([cam with { Removable = null }])).Kind);
        var parent = new PnpSnapshotEntry(@"USB\VID_13D3&PID_5666\6&CCCC0002&0&2", "USB Composite Device", "USB", "(Standard USB Host Controller)", @"USB\VID_13D3&PID_5666", "usbccgp", 0, cam.ContainerId, Removable: true);
        Assert.Equal(DeviceClass.Camera, Assert.Single(DeviceScope.Classify([parent, cam])).Kind);
        // and the station's own container is still out of scope (data the device cannot set)
        Assert.Empty(DeviceScope.Classify([cam with { ContainerId = DeviceScope.LocalMachineContainer, Removable = true }]));
    }

    // ---- 2. no laundering of a reclassification ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Only_the_known_kind_change_of_a_version_bump_is_a_reclassification()
    {
        Assert.True(DeviceScope.IsKnownReclassification(1, 2, DeviceClass.Keyboard, DeviceClass.Mouse));
        Assert.False(DeviceScope.IsKnownReclassification(1, 2, DeviceClass.Mouse, DeviceClass.Keyboard));
        Assert.False(DeviceScope.IsKnownReclassification(1, 2, DeviceClass.Printer, DeviceClass.Scanner));
        Assert.False(DeviceScope.IsKnownReclassification(2, 3, DeviceClass.Keyboard, DeviceClass.Mouse));
        Assert.False(DeviceScope.IsKnownReclassification(1, 2, "Mouse", "Keyboard"));
        Assert.True(DeviceScope.IsKnownReclassification(1, 2, "Keyboard", "Mouse"));
    }

    /// <summary>A keyboard-only injector: the live classifier says Keyboard.</summary>
    private static readonly PnpSnapshotEntry[] Injector = [Root, Kbd0];

    /// <summary>The station's Razer shape: first interface a mouse, second a keyboard: v1 said Keyboard, v2 says Mouse.</summary>
    private static readonly PnpSnapshotEntry[] Pair = [Root, Mouse0, Kbd1];

    private static DeviceJournalEntry LegacyRow(DeviceClass cls, int journalVersion) => new()
    {
        Identity = DeviceMonitorRules.Identity(Root), InstanceId = Root.InstanceId, Class = cls, VidPid = "1234:5678", Serial = "INJ0001", FirstSeenUtc = T0, ClassifierVersion = journalVersion,
    };

    private static Func<IReadOnlyDictionary<string, SealedKind>?> Sealed(string kind, int version) =>
        () => new Dictionary<string, SealedKind>(StringComparer.OrdinalIgnoreCase) { [K(DeviceMonitorRules.Identity(Root))] = new(kind, version) };

    [Fact]
    public void P4_a1_a_mouse_row_that_turns_into_a_keyboard_is_not_laundered_whatever_the_journal_says()
    {
        foreach (var journalVersion in new[] { 0, 1, 2 })
        {
            var j = new DeviceJournal { Devices = [LegacyRow(DeviceClass.Mouse, journalVersion)] };
            var rec = new List<DeviceReclassification>();
            DeviceJournalMerge.Apply(j, Injector, null, "u", T0.AddDays(2), initial: false, reclassified: rec, sealedKinds: Sealed("Mouse", 1));
            Assert.Empty(rec);                                    // Mouse -> Keyboard is no change a version bump makes
        }
        // no sealed audit at all (not readable, chain broken): nothing is a reclassification
        var j2 = new DeviceJournal { Devices = [LegacyRow(DeviceClass.Mouse, 0)] };
        var rec2 = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j2, Injector, null, "u", T0.AddDays(2), initial: false, reclassified: rec2, sealedKinds: () => null);
        DeviceJournalMerge.Apply(new DeviceJournal { Devices = [LegacyRow(DeviceClass.Mouse, 0)] }, Injector, null, "u", T0.AddDays(2), initial: false, reclassified: rec2);
        Assert.Empty(rec2);
    }

    [Fact]
    public void The_old_version_comes_from_the_sealed_audit_not_from_the_journal()
    {
        // the journal says „this very version wrote it”, the sealed audit says version 1: the note is made (the journal's version is not read)
        var j = new DeviceJournal { Devices = [LegacyRow(DeviceClass.Keyboard, DeviceScope.ClassifierVersion)] };
        var rec = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j, Pair, null, "u", T0.AddDays(2), initial: false, reclassified: rec, sealedKinds: Sealed("Keyboard", 1));
        var n = Assert.Single(rec);
        Assert.Equal((DeviceClass.Keyboard, DeviceClass.Mouse, 1, DeviceScope.ClassifierVersion), (n.OldClass, n.NewClass, n.OldVersion, n.NewVersion));
        // the journal says version 0 (an old row), the sealed audit says this version: no note
        var j2 = new DeviceJournal { Devices = [LegacyRow(DeviceClass.Keyboard, 0)] };
        var rec2 = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j2, Pair, null, "u", T0.AddDays(2), initial: false, reclassified: rec2, sealedKinds: Sealed("Keyboard", DeviceScope.ClassifierVersion));
        Assert.Empty(rec2);
        // no sealed audit: none
        var j3 = new DeviceJournal { Devices = [LegacyRow(DeviceClass.Keyboard, 0)] };
        var rec3 = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j3, Pair, null, "u", T0.AddDays(2), initial: false, reclassified: rec3);
        Assert.Empty(rec3);
    }

    [Fact]
    public void The_old_rule_re_run_on_the_live_snapshot_must_give_the_sealed_old_kind()
    {
        // the sealed old kind is Keyboard (v1), the live device has a mouse interface only: the v1 rule gives Mouse here, not Keyboard: not the classifier's doing
        var mouseOnly = new PnpSnapshotEntry[] { Root, Mouse0 };
        var j = new DeviceJournal { Devices = [LegacyRow(DeviceClass.Keyboard, 0)] };
        var rec = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j, mouseOnly, null, "u", T0.AddDays(2), initial: false, reclassified: rec, sealedKinds: Sealed("Keyboard", 1));
        Assert.Empty(rec);
        var d = Assert.Single(DeviceScope.Classify(Pair));
        Assert.Equal((DeviceClass.Mouse, DeviceClass.Keyboard), (d.Kind, d.KindAtVersion(1)));        // the pair: v1 said Keyboard, v2 says Mouse
        Assert.Equal(DeviceClass.Mouse, Assert.Single(DeviceScope.Classify(mouseOnly)).KindAtVersion(1));
    }

    private static DeviceAuditReading Reading(string sealedClass, int sealedVersion, params SealedReclassification[] notes)
    {
        var id = K(DeviceMonitorRules.Identity(Root));
        return new(true, new Dictionary<string, string>(), [], 0) { Devices = [new SealedDevice(id, sealedClass, "INJ0001", T0, T0, "dev", sealedVersion)], Reclassifications = notes };
    }

    private static SealedReclassification Note(string from, string to, int oldV, int newV) =>
        new(T0.AddDays(2), K(DeviceMonitorRules.Identity(Root)), from, to, oldV, newV, "dev", "application");

    [Fact]
    public void P4_a2_a3_a_forged_Mouse_to_Keyboard_note_does_not_continue_the_chain_and_D05_keeps_the_finding()
    {
        var id = K(DeviceMonitorRules.Identity(Root));
        var reading = Reading("Mouse", 1, Note("Mouse", "Keyboard", 1, 2));
        var chain = DeviceMonitorAuditCheck.KindChain(reading, id, reading.Devices[0]);
        Assert.Equal(["Mouse"], chain.Select(c => c.Class));
        var j = new DeviceJournal { Devices = [LegacyRow(DeviceClass.Keyboard, 2)] };
        var finding = Assert.Single(DeviceMonitorAuditCheck.DeviceMismatches(j, reading, T0.AddDays(-1), T0.AddDays(3), j.Devices));
        Assert.Contains("alt tip", finding);
        Assert.Empty(DeviceMonitorAuditCheck.Reclassifications(j, reading));
    }

    [Fact]
    public void P4_a4_a_note_for_a_device_first_sealed_under_the_current_version_is_rejected()
    {
        var id = K(DeviceMonitorRules.Identity(Root));
        var reading = Reading("Mouse", 2, Note("Mouse", "Keyboard", 1, 2));
        Assert.Equal(["Mouse"], DeviceMonitorAuditCheck.KindChain(reading, id, reading.Devices[0]).Select(c => c.Class));
        var reading2 = Reading("Keyboard", 2, Note("Keyboard", "Mouse", 1, 2));
        Assert.Equal(["Keyboard"], DeviceMonitorAuditCheck.KindChain(reading2, id, reading2.Devices[0]).Select(c => c.Class));
    }

    [Fact]
    public void P4_c2_the_stations_Razer_Keyboard_to_Mouse_is_accepted_end_to_end()
    {
        var id = K(DeviceMonitorRules.Identity(Root));
        var j = new DeviceJournal { Devices = [LegacyRow(DeviceClass.Keyboard, 0)] };
        var rec = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j, Pair, null, "u", T0.AddDays(2), initial: false, reclassified: rec, sealedKinds: Sealed("Keyboard", 1));
        var n = Assert.Single(rec);
        Assert.Equal(DeviceClass.Mouse, j.Devices[0].Class);
        // the sealed note the monitor writes is accepted by the audit check and read back as the last kind
        var reading = Reading("Keyboard", 1, Note(n.OldClass.ToString(), n.NewClass.ToString(), n.OldVersion, n.NewVersion));
        Assert.Equal(["Keyboard", "Mouse"], DeviceMonitorAuditCheck.KindChain(reading, id, reading.Devices[0]).Select(c => c.Class));
        Assert.Empty(DeviceMonitorAuditCheck.DeviceMismatches(j, reading, T0.AddDays(-1), T0.AddDays(3), j.Devices));
        Assert.Single(DeviceMonitorAuditCheck.Reclassifications(j, reading));
        var kinds = DeviceMonitorAuditCheck.SealedKinds(reading);
        Assert.Equal(new SealedKind("Mouse", DeviceScope.ClassifierVersion), kinds[id]);
    }

    // ---- 3. a registration number on a medium without a unique serial -----------------------------------------------------------------------------

    private static PnpSnapshotEntry Generic(string container = "{CCCCCCCC-0000-0000-0000-00000000B001}") =>
        new(@"USBSTOR\Disk&Ven_Generic&Prod_Flash&Rev_1.00\000000000000&0", "Generic Flash USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskGeneric_Flash", "disk", 0, container);

    private static IReadOnlyDictionary<string, MediumInfo> Medium(PnpSnapshotEntry e, string volumeSerial, long capacity, string signature = "") =>
        new Dictionary<string, MediumInfo> { [e.InstanceId] = new(volumeSerial, capacity, "E:", "x", signature) };

    private static (DeviceJournal Journal, DeviceJournalEntry Row) RegisteredStick(out DateTimeOffset t)
    {
        var j = new DeviceJournal();
        var g = Generic();
        DeviceJournalMerge.Apply(j, [g], Medium(g, "AAAA0001", 16_000_000_000), "u", T0, initial: false);
        DeviceJournalMerge.SetRegistrationNumber(j, j.Devices[0].Identity, "NR-7/2026");
        DeviceJournalMerge.Apply(j, [], null, "-", T0.AddHours(1), initial: false);
        t = T0.AddDays(1);
        return (j, j.Devices[0]);
    }

    [Fact]
    public void P5_another_stick_with_the_same_placeholder_serial_and_another_capacity_does_not_inherit_the_number()
    {
        var (j, row) = RegisteredStick(out var t);
        var g2 = Generic("{CCCCCCCC-0000-0000-0000-00000000B002}");
        var changes = DeviceJournalMerge.Apply(j, [g2], Medium(g2, "BBBB0002", 64_000_000_000), "u", t, initial: false);
        Assert.Same(row, Assert.Single(j.Devices));                // one row for the model: the number cannot follow the stick, so it must be withdrawn from it
        Assert.True(row.RegistrationMediumMismatch);
        Assert.Equal("", DeviceMonitorRules.EffectiveNumber(row));
        var c = Assert.Single(changes);
        Assert.True(c.Arrived);
        Assert.Equal(DeviceMonitorRules.MediumNotRegisteredAlertText, c.CloneText);
        Assert.Contains("nu corespunde mediului înregistrat", c.CloneText);
        // D04/D05 and the page: unregistered, critical
        Assert.Equal(DeviceAlertSeverity.Critical, DeviceMonitorRules.Decide(row.Class, row.InstanceId, DeviceMonitorRules.EffectiveNumber(row), isolated: false).Severity);
        Assert.Contains(row, DeviceMonitorReport.Build(j, T0.AddDays(-1), t.AddDays(1), isolated: false).Offending);
        Assert.Contains("nu corespunde mediului înregistrat", DeviceMonitorReport.Line(row, null, []));
    }

    [Fact]
    public void P5_a_different_volume_serial_alone_is_enough_and_a_swap_between_two_readings_is_alerted_too()
    {
        var (j, row) = RegisteredStick(out var t);
        var g = Generic();
        DeviceJournalMerge.Apply(j, [g], Medium(g, "AAAA0001", 16_000_000_000), "u", t, initial: false);         // the registered stick back: still registered
        Assert.False(row.RegistrationMediumMismatch);
        // swapped without a disconnection in between (the row stays present): same capacity, another volume
        var changes = DeviceJournalMerge.Apply(j, [g], Medium(g, "CCCC0003", 16_000_000_000), "u", t.AddMinutes(1), initial: false);
        Assert.True(row.RegistrationMediumMismatch);
        Assert.Equal(DeviceMonitorRules.MediumNotRegisteredAlertText, Assert.Single(changes).CloneText);
        // the same medium again does not alert again
        Assert.Empty(DeviceJournalMerge.Apply(j, [g], Medium(g, "CCCC0003", 16_000_000_000), "u", t.AddMinutes(2), initial: false));
    }

    [Fact]
    public void P5_the_same_capacity_and_volume_serial_stays_registered_and_the_registered_stick_coming_back_is_registered_again()
    {
        var (j, row) = RegisteredStick(out var t);
        var g = Generic();
        var back = DeviceJournalMerge.Apply(j, [g], Medium(g, "AAAA0001", 16_000_000_000), "u", t, initial: false);
        Assert.False(row.RegistrationMediumMismatch);
        Assert.Equal("NR-7/2026", DeviceMonitorRules.EffectiveNumber(row));
        Assert.Null(Assert.Single(back).CloneText);
        Assert.Equal(DeviceAlertSeverity.Info, DeviceMonitorRules.Decide(row.Class, row.InstanceId, DeviceMonitorRules.EffectiveNumber(row), isolated: false).Severity);
        // the foreign stick, then the registered one again
        DeviceJournalMerge.Apply(j, [], null, "-", t.AddHours(1), initial: false);
        var other = Generic("{CCCCCCCC-0000-0000-0000-00000000B002}");
        DeviceJournalMerge.Apply(j, [other], Medium(other, "BBBB0002", 64_000_000_000), "u", t.AddHours(2), initial: false);
        Assert.True(row.RegistrationMediumMismatch);
        DeviceJournalMerge.Apply(j, [], null, "-", t.AddHours(3), initial: false);
        DeviceJournalMerge.Apply(j, [g], Medium(g, "AAAA0001", 16_000_000_000), "u", t.AddHours(4), initial: false);
        Assert.False(row.RegistrationMediumMismatch);
        Assert.Equal("NR-7/2026", DeviceMonitorRules.EffectiveNumber(row));
    }

    [Fact]
    public void P5_registering_the_new_stick_on_a_mismatched_row_binds_the_number_to_it_and_a_unique_serial_is_never_bound()
    {
        var (j, row) = RegisteredStick(out var t);
        var other = Generic("{CCCCCCCC-0000-0000-0000-00000000B002}");
        DeviceJournalMerge.Apply(j, [other], Medium(other, "BBBB0002", 64_000_000_000), "u", t, initial: false);
        Assert.True(row.RegistrationMediumMismatch);
        DeviceJournalMerge.SetRegistrationNumber(j, row.Identity, "NR-8/2026");
        Assert.False(row.RegistrationMediumMismatch);
        Assert.Equal((64_000_000_000L, "BBBB0002"), (row.RegistrationBoundCapacityBytes, row.RegistrationBoundVolumeSerial));
        // a stick with a real serial keeps its row whatever the medium: no binding, no mismatch
        var j2 = new DeviceJournal();
        var real = new PnpSnapshotEntry(@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKESN0501&0", "Fake Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFake____Stick", "disk", 0, "{CCCCCCCC-0000-0000-0000-00000000B003}");
        DeviceJournalMerge.Apply(j2, [real], Medium(real, "DDDD0004", 16_000_000_000), "u", T0, initial: false);
        DeviceJournalMerge.SetRegistrationNumber(j2, j2.Devices[0].Identity, "NR-9/2026");
        Assert.False(j2.Devices[0].RegistrationBound);
        DeviceJournalMerge.Apply(j2, [], null, "-", T0.AddHours(1), initial: false);
        DeviceJournalMerge.Apply(j2, [real], Medium(real, "EEEE0005", 16_000_000_000), "u", T0.AddHours(2), initial: false);
        Assert.False(j2.Devices[0].RegistrationMediumMismatch);
    }
}
