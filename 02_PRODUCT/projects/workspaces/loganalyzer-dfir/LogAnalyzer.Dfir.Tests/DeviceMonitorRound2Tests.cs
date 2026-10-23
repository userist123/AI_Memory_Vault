using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review round 2 of the device monitor: radio kinds seen active (3), the history of the sealed audit after a journal reset and the rows rebuilt from it (4),
/// one classification for the media page and the monitor (5), clone groups (7) and the in-place save note. Neutral fixtures: FAKE serials and invented ids.
/// </summary>
public sealed class DeviceMonitorRound2Tests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private const string Local = DeviceScope.LocalMachineContainer;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "devround2_" + Guid.NewGuid().ToString("N"));
    public DeviceMonitorRound2Tests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static PnpSnapshotEntry Stick(string tail = "FAKESN0001&0", string container = "{BBBBBBBB-0000-0000-0000-000000000001}") =>
        new($@"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.00\{tail}", "Fake Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFake____Stick_3.0", "disk", 0, container);

    // ---- 3: every radio / network kind is seen active ------------------------------------------------------------------------------------------

    [Fact]
    public void A_usb_bluetooth_dongle_that_was_active_and_is_disabled_now_still_counts_as_seen_active()
    {
        var dongle = new PnpSnapshotEntry(@"USB\VID_0A12&PID_0001\6&3C4D5E6F&0&3", "Generic Bluetooth Radio", "Bluetooth", "Fake", @"USB\VID_0A12&PID_0001", "BTHUSB", 0, "{BBBBBBBB-0000-0000-0000-000000000003}");
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [dongle], null, "—", T0, initial: false);
        var row = Assert.Single(j.Devices);
        Assert.Equal(DeviceClass.BluetoothAdapter, row.Class);
        Assert.Equal(T0, row.ActiveSeenUtc);
        DeviceJournalMerge.Apply(j, [dongle with { ConfigManagerErrorCode = 22 }], null, "—", T0.AddHours(1), initial: false);   // switched off afterwards
        Assert.Equal(T0, row.ActiveSeenUtc);
        Assert.True(DeviceMonitorReport.ForbiddenSeen(row, isolated: true, fromUtc: T0.AddMinutes(-5)));
        Assert.False(DeviceMonitorReport.ForbiddenSeen(row, isolated: true, fromUtc: T0.AddMinutes(5)));
    }

    // ---- 4: the whole sealed history survives a reset, the rows come back from it ------------------------------------------------------------------

    [Fact]
    public void A_reset_does_not_drop_the_registrations_first_seen_records_and_alerts_the_audit_holds()
    {
        var svc = new AuthService(Path.Combine(_dir, "auth"));
        svc.AuditOperation(null, OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail("SN||FAKESN0001", "UsbStorage", "Fake Stick", "FAKESN0001", T0));
        svc.AuditOperation(null, OperationAudit.DeviceRegistrationChanged, OperationAudit.DeviceRegistrationChangedDetail("SN||FAKESN0001", "UsbStorage", "Fake Stick", "", "RN-1"));
        svc.AuditOperation(null, OperationAudit.DeviceAlertCritical, OperationAudit.DeviceAlertCriticalDetail("SN||FAKESN0001", "UsbStorage", "Fake Stick", "mediu fără număr"));
        svc.AuditOperation(null, OperationAudit.DeviceJournalReset, OperationAudit.DeviceJournalResetDetail("device_monitor.json.deteriorat-1", "ab12", "jurnal deteriorat"));
        var after = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        Assert.Equal("UsbStorage", Assert.Single(after.Devices).Class);
        Assert.Equal("RN-1", after.LastRegistration["SN||FAKESN0001"]);
        Assert.Single(after.CriticalAlerts);
        Assert.Equal("ab12", Assert.Single(after.Resets).Sha256);     // the sealed reset stays
        Assert.Equal(0, after.Unreadable);
    }

    [Fact]
    public void After_a_damaged_journal_the_monitor_rebuilds_the_rows_numbers_and_alerts_from_the_sealed_audit()
    {
        var svc = new AuthService(Path.Combine(_dir, "auth"));
        var stick = Stick();
        var id = DeviceMonitorRules.Identity(stick);
        svc.AuditOperation(null, OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail(id, "UsbStorage", "Fake Stick USB Device", "FAKESN0001", T0));
        svc.AuditOperation(null, OperationAudit.DeviceRegistrationChanged, OperationAudit.DeviceRegistrationChangedDetail(id, "UsbStorage", "Fake Stick USB Device", "", "RN-7"));
        svc.AuditOperation(null, OperationAudit.DeviceAlertCritical, OperationAudit.DeviceAlertCriticalDetail(id, "UsbStorage", "Fake Stick USB Device", "mediu de stocare fără număr de înregistrare"));
        var path = Path.Combine(_dir, "device_monitor.json");
        File.WriteAllText(path, "{ not json");
        var j = DeviceJournalStore.LoadForMonitor(path, (ev, d) => { svc.AuditOperation(null, ev, d); return null; }, out var problem,
            () => OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true));
        var row = Assert.Single(j.Devices);
        Assert.Equal(id, row.Identity);
        Assert.Equal("RN-7", row.RegistrationNumber);                // the registered stick does not become „fără număr”
        Assert.Equal(DeviceClass.UsbStorage, row.Class);
        Assert.Equal("FAKESN0001", row.Serial);
        Assert.Equal(T0, row.FirstSeenUtc);
        Assert.False(row.Present);
        Assert.Single(j.Alerts);
        Assert.Contains("refăcute din jurnalul sigilat", problem);
        // the sealed reset record is kept
        Assert.Single(OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true).Resets);
    }

    // ---- 5: one classification for the media page and the monitor --------------------------------------------------------------------------------

    [Fact]
    public void An_external_nvme_with_a_pci_parent_and_its_own_container_is_Disc_extern_on_the_media_page_and_the_monitor()
    {
        var nvme = new PnpSnapshotEntry(@"SCSI\Disk&Ven_NVMe&Prod_Fake_Portable\4&AAAAAAAA&0&000000", "NVMe Fake Portable SCSI Disk Device", "DiskDrive", "(Standard disk drives)",
            @"SCSI\DiskNVMe____Fake_Portable SCSI\Disk GenDisk", "disk", 0, "{BBBBBBBB-0000-0000-0000-000000000009}", @"PCI\VEN_8086&DEV_FAKE\4&22222222&0&0010");
        var monitor = Assert.Single(DeviceScope.Classify([nvme]));
        Assert.Equal(DeviceClass.ExternalDisk, monitor.Kind);
        Assert.Equal("Disc extern", DeviceMonitorRules.ClassText(monitor.Kind));
        var page = DeviceScope.InventoryKind(nvme);
        Assert.Equal(DeviceKind.ExternalDisk, page);
        Assert.Equal("Disc extern", DeviceText.Kind(page!.Value));
        // the station's own NVMe stays internal on both
        var own = nvme with { ContainerId = Local, InstanceId = @"SCSI\Disk&Ven_NVMe&Prod_Fake_SSD\4&BBBBBBBB&0&000000" };
        Assert.Empty(DeviceScope.Classify([own]));
        Assert.Equal(DeviceKind.InternalDisk, DeviceScope.InventoryKind(own));
        // a stick behind USBSTOR is a stick on both
        Assert.Equal(DeviceKind.UsbStorage, DeviceScope.InventoryKind(Stick()));
    }

    // ---- 7: clone groups --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Two_present_devices_with_the_same_serial_both_raise_the_clone_alert()
    {
        var a = Stick("FAKECLONE01&0");
        var b = Stick("FAKECLONE01&0", "{BBBBBBBB-0000-0000-0000-000000000002}") with { InstanceId = @"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.01\FAKECLONE01&0" };
        var scoped = DeviceScope.Classify([a, b]);
        var group = Assert.Single(DeviceMonitorRules.CloneGroups(scoped));
        Assert.Equal(2, group.Count);
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [], null, "—", T0, initial: true);
        var changes = DeviceJournalMerge.Apply(j, [a, b], null, "—", T0.AddMinutes(1), initial: false);
        Assert.Equal(2, changes.Count(c => c.Arrived));
        Assert.All(changes.Where(c => c.Arrived), c => Assert.Equal(DeviceMonitorRules.CloneAlertText, c.CloneText));
        Assert.Equal(2, j.Devices.Count);
        Assert.All(j.Devices, d => Assert.True(d.DuplicateSerial));
    }
}
