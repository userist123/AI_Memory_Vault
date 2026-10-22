using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner decisions 2026-10-22 (follow-up): a kind that changed because the CLASSIFIER changed (the station's Razer mouse, sealed as a keyboard by the old
/// classifier) is one sealed informational note "device.reclassified", never the D05 finding „alt tip”; a kind that changes under the same classifier version,
/// or on another identity, keeps the finding. And a placeholder serial (000000000000, 111111) is no serial. Neutral fixtures: FAKE serials, invented ids.
/// </summary>
public sealed class DeviceMonitorReclassifyTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "devreclass_" + Guid.NewGuid().ToString("N"));
    public DeviceMonitorReclassifyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const string Box = "{CCCCCCCC-0000-0000-0000-00000000A002}";

    /// <summary>The station's Razer shape (fake ids): a composite device whose first interface is a mouse and whose second a keyboard (macros): version 1 said keyboard, version 2 says mouse.</summary>
    private static readonly PnpSnapshotEntry[] Razer =
    [
        new(@"USB\VID_1532&PID_0042\FAKERZ0001", "USB Composite Device", "USB", "(Standard USB Host Controller)", @"USB\VID_1532&PID_0042", "usbccgp", 0, Box),
        new(@"HID\VID_1532&PID_0042&MI_00\8&AAA&0&0000", "HID-compliant mouse", "Mouse", "Microsoft", @"HID\VID_1532&PID_0042&MI_00", "mouhid", 0, Box),
        new(@"HID\VID_1532&PID_0042&MI_01\8&BBB&0&0000", "HID Keyboard Device", "Keyboard", "(Standard keyboards)", @"HID\VID_1532&PID_0042&MI_01", "kbdhid", 0, Box),
    ];

    /// <summary>What the sealed audit says about the device: its kind and the classifier version it was sealed with.</summary>
    private static Func<IReadOnlyDictionary<string, SealedKind>?> Sealed(string identity, string kind, int version) =>
        () => new Dictionary<string, SealedKind>(StringComparer.OrdinalIgnoreCase) { [K(identity)] = new(kind, version) };

    private static string K(string identity) => OperationAudit.DeviceField(identity);

    /// <summary>A journal whose mouse row was written by the OLD classifier (kind Keyboard, no classifier version) and the sealed first-seen record of that time.</summary>
    private static (DeviceJournal Journal, DeviceJournalEntry Row, SealedDevice First) OldRow(int sealedClassifier = 1)
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, Razer, null, "u", T0, initial: false);
        var row = Assert.Single(j.Devices);
        row.Class = DeviceClass.Keyboard;
        row.ClassifierVersion = 0;
        return (j, row, new SealedDevice(K(row.Identity), "Keyboard", row.Serial, row.FirstSeenUtc, row.FirstSeenUtc, row.Name, sealedClassifier));
    }

    private static SealedReclassification Note(DeviceJournalEntry row, string from = "Keyboard", string to = "Mouse", int oldV = 1, int newV = 2, string? identity = null) =>
        new(T0.AddDays(1), identity ?? K(row.Identity), from, to, oldV, newV, row.Name, "application");

    private static IReadOnlyList<string> Findings(DeviceJournal j, DeviceAuditReading a)
    {
        var from = T0.AddDays(-1);
        var to = T0.AddDays(3);
        return DeviceMonitorAuditCheck.DeviceMismatches(j, a, from, to, DeviceMonitorReport.Build(j, from, to, isolated: true).Devices);
    }

    private static DeviceAuditReading Audit(SealedDevice first, params SealedReclassification[] notes) =>
        new(true, new Dictionary<string, string>(), [], 0) { Devices = [first], Reclassifications = notes };

    // ---- the monitor: one note per classifier change -----------------------------------------------------------------------------------------

    [Fact]
    public void A_row_of_an_older_classifier_whose_kind_changes_is_reported_once_for_sealing_and_stamped_with_the_new_version()
    {
        var (j, row, _) = OldRow();
        var notes = new List<DeviceReclassification>();
        DeviceJournalMerge.Apply(j, Razer, null, "u", T0.AddDays(1), initial: false, reclassified: notes, sealedKinds: Sealed(row.Identity, "Keyboard", 1));
        var n = Assert.Single(notes);
        Assert.Equal((row.Identity, DeviceClass.Keyboard, DeviceClass.Mouse, 1, DeviceScope.ClassifierVersion), (n.Identity, n.OldClass, n.NewClass, n.OldVersion, n.NewVersion));
        Assert.Equal(DeviceClass.Mouse, row.Class);
        Assert.Equal(DeviceScope.ClassifierVersion, row.ClassifierVersion);
        notes.Clear();
        DeviceJournalMerge.Apply(j, Razer, null, "u", T0.AddDays(2), initial: false, reclassified: notes, sealedKinds: Sealed(row.Identity, "Keyboard", 1));
        Assert.Empty(notes);                                       // sealed once, not at every reading
    }

    [Fact]
    public void A_kind_that_changes_under_the_same_classifier_version_is_not_a_reclassification()
    {
        var (j, row, _) = OldRow();
        var notes = new List<DeviceReclassification>();
        // the SEALED audit says this very classifier version wrote the kind: the kind has no excuse (the journal's own version is not read)
        DeviceJournalMerge.Apply(j, Razer, null, "u", T0.AddDays(1), initial: false, reclassified: notes, sealedKinds: Sealed(row.Identity, "Keyboard", DeviceScope.ClassifierVersion));
        Assert.Empty(notes);
        Assert.Equal(DeviceClass.Mouse, row.Class);
    }

    [Fact]
    public void A_new_row_carries_the_classifier_version_and_the_first_seen_record_seals_it()
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, Razer, null, "u", T0, initial: false);
        Assert.Equal(DeviceScope.ClassifierVersion, Assert.Single(j.Devices).ClassifierVersion);
        var svc = new AuthService(Path.Combine(_dir, "auth1"));
        var row = j.Devices[0];
        svc.AuditOperation(null, OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail(row.Identity, "Mouse", row.Name, row.Serial, row.FirstSeenUtc, classifier: DeviceScope.ClassifierVersion));
        var r = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        Assert.Equal(DeviceScope.ClassifierVersion, Assert.Single(r.Devices).ClassifierVersion);
    }

    [Fact]
    public void The_sealed_note_round_trips_through_the_audit_with_both_kinds_both_versions_and_the_operator()
    {
        var (_, row, _) = OldRow();
        var svc = new AuthService(Path.Combine(_dir, "auth2"));
        svc.AuditOperation(null, OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail(row.Identity, "Keyboard", row.Name, row.Serial, row.FirstSeenUtc));
        svc.AuditOperation(null, OperationAudit.DeviceReclassified, OperationAudit.DeviceReclassifiedDetail(row.Identity, "Keyboard", "Mouse", 1, 2, row.Name));
        var r = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        var n = Assert.Single(r.Reclassifications);
        Assert.Equal(("Keyboard", "Mouse", 1, 2, "application"), (n.OldClass, n.NewClass, n.OldVersion, n.NewVersion, n.Operator));
        Assert.Equal(1, Assert.Single(r.Devices).ClassifierVersion);   // a first-seen record without the field is classifier version 1
        row.Class = DeviceClass.Mouse;
        var j = new DeviceJournal { Devices = [row] };
        Assert.Empty(DeviceMonitorAuditCheck.DeviceMismatches(j, r, T0.AddDays(-1), T0.AddDays(3), DeviceMonitorReport.Build(j, T0.AddDays(-1), T0.AddDays(3), true).Devices));
        Assert.Contains(DeviceHistory.Build(row.Identity, r), h => h.Kind == DeviceHistory.Reclassified && h.Details.Contains("v1 → v2"));
    }

    // ---- D05: the three cases ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void D05_Case1_same_identity_kind_changed_by_the_classifier_with_a_sealed_note_is_informational_with_its_reason()
    {
        var (j, row, first) = OldRow();
        row.Class = DeviceClass.Mouse;
        row.ClassifierVersion = DeviceScope.ClassifierVersion;
        var audit = Audit(first, Note(row));
        Assert.Empty(Findings(j, audit));                          // not „alt tip”
        var info = Assert.Single(DeviceMonitorAuditCheck.Reclassifications(j, audit));
        Assert.Contains("RECLASIFICAT (informativ)", info);
        Assert.Contains("versiunea 1 la 2", info);
        Assert.Contains("operator: application", info);
    }

    [Fact]
    public void D05_Case2_kind_changed_without_a_sealed_note_or_under_the_same_classifier_version_keeps_the_finding()
    {
        var (j, row, first) = OldRow();
        row.Class = DeviceClass.Mouse;
        Assert.Contains("alt tip", Assert.Single(Findings(j, Audit(first))));                                          // no note: the file was edited
        Assert.Contains("alt tip", Assert.Single(Findings(j, Audit(first with { ClassifierVersion = 2 }, Note(row)))));  // sealed with this very version: nothing to reclassify
        Assert.Contains("alt tip", Assert.Single(Findings(j, Audit(first, Note(row, oldV: 2, newV: 2)))));            // a note that does not change the version
        Assert.Contains("alt tip", Assert.Single(Findings(j, Audit(first, Note(row, from: "Printer", to: "Mouse")))));  // a note that does not continue the chain
        Assert.Contains("alt tip", Assert.Single(Findings(j, Audit(first, Note(row, to: "Scanner")))));                // a note for another kind than the row's
        Assert.Empty(DeviceMonitorAuditCheck.Reclassifications(j, Audit(first)));
    }

    [Fact]
    public void D05_Case3_a_note_of_another_identity_does_not_excuse_this_row()
    {
        var (j, row, first) = OldRow();
        row.Class = DeviceClass.Mouse;
        var audit = Audit(first, Note(row, identity: K("ID|HID\\VID_FAKE&PID_0000\\OTHER")));
        Assert.Contains("alt tip", Assert.Single(Findings(j, audit)));
        Assert.Empty(DeviceMonitorAuditCheck.Reclassifications(j, audit));
    }

    [Fact]
    public void A_sealed_alert_written_under_the_old_kind_does_not_become_a_finding_after_a_valid_reclassification()
    {
        var (j, row, first) = OldRow();
        row.Class = DeviceClass.Mouse;
        var alert = new DeviceAuditAlert(T0.AddHours(1), K(row.Identity), row.Name, "dispozitiv conectat", "Keyboard");
        var audit = Audit(first, Note(row)) with { InfoAlerts = [alert] };
        Assert.DoesNotContain(Findings(j, audit), f => f.Contains("alt tip"));
        var withoutNote = Audit(first) with { InfoAlerts = [alert] };
        Assert.Contains(Findings(j, withoutNote), f => f.Contains("alt tip"));
    }

    // ---- item 3: a placeholder serial is no serial -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(@"USB\VID_0AAA&PID_0001\000000000000", true)]
    [InlineData(@"USB\VID_0AAA&PID_0001\111111", true)]
    [InlineData(@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\AAAAAAAAAA&0", true)]
    [InlineData(@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKESER01&0", false)]
    [InlineData(@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\0000000A&0", false)]
    public void An_all_same_character_serial_is_not_a_serial(string instanceId, bool generic)
    {
        Assert.Equal(generic, DeviceMonitorRules.HasGenericSerial(instanceId));
        Assert.Equal(generic, DeviceMonitorRules.Serial(instanceId).Length == 0);
        Assert.Equal(generic, DeviceMonitorRules.HasNoUniqueSerial(instanceId));
    }

    private static PnpSnapshotEntry GenericStick(string serial = "000000000000") =>
        new($@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\{serial}&0", "Fake Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFake____Stick", "disk", 0);

    [Fact]
    public void Two_sticks_with_the_same_placeholder_serial_are_never_compared_for_clones_and_show_the_generic_serial_text()
    {
        var a = GenericStick();
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [a], new Dictionary<string, MediumInfo> { [a.InstanceId] = new("AAAA0001", 16_000_000_000, "E:", "S") }, "u", T0, initial: false);
        DeviceJournalMerge.Apply(j, [], null, "-", T0.AddHours(1), initial: false);
        // another 64 GB stick of the same model with the same placeholder serial, the next day
        var ch = DeviceJournalMerge.Apply(j, [a], new Dictionary<string, MediumInfo> { [a.InstanceId] = new("BBBB0002", 64_000_000_000, "E:", "S") }, "u", T0.AddDays(1), initial: false);
        Assert.All(j.Devices, d => Assert.False(d.DuplicateSerial));
        Assert.Null(Assert.Single(ch, c => c.Arrived).CloneText);
        var row = j.Devices[0];
        Assert.Equal("", row.Serial);
        Assert.True(row.NoUniqueSerial);
        Assert.True(row.GenericSerial);
        Assert.StartsWith("ID|", row.Identity, StringComparison.Ordinal);   // told apart by the instance id, not by the placeholder
        Assert.Equal("fără serie unică (serie generică)", DeviceMonitorRules.SerialText(row.Serial, row.NoUniqueSerial, row.GenericSerial));
        Assert.Contains("fără serie unică (serie generică)", DeviceMonitorReport.Line(row, null, []));
    }

    [Fact]
    public void A_real_serial_still_compares_and_a_stored_placeholder_row_of_an_older_version_is_not_flagged_when_read()
    {
        var j = new DeviceJournal();
        var old1 = new DeviceJournalEntry { Identity = "SN|FAKE STICK|000000000000", InstanceId = @"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\000000000000&0", Class = DeviceClass.UsbStorage,
            Serial = "000000000000", VidPid = "Fake Stick", CapacityBytes = 16_000_000_000, DuplicateSerial = true, FirstSeenUtc = T0 };
        var old2 = new DeviceJournalEntry { Identity = "SN|OTHER STICK|000000000000", InstanceId = @"USBSTOR\Disk&Ven_Other&Prod_Stick&Rev_1.00\000000000000&0", Class = DeviceClass.UsbStorage,
            Serial = "000000000000", VidPid = "Other Stick", CapacityBytes = 8_000_000_000, DuplicateSerial = true, FirstSeenUtc = T0 };
        j.Devices = [old1, old2];
        DeviceJournalMerge.RecomputeDuplicateSerials(j);
        Assert.All(j.Devices, d => Assert.False(d.DuplicateSerial));
        Assert.Equal("fără serie unică (serie generică)", DeviceMonitorRules.SerialText(old1.Serial, old1.NoUniqueSerial, old1.GenericSerial));
        Assert.Equal("FAKESER01", DeviceMonitorRules.SerialText("FAKESER01", false, false));
    }
}
