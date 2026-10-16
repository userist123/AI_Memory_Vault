using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review round 1 of the device monitor: the journal compared with the sealed first-seen records (B1), UAS / USB-NVMe disks (B2), cards without a volume serial
/// (M1), clones with a port-generated id and a volume that changed (M2), the crash-safe save and the sealed reset of a damaged journal (M3), short lost intervals
/// (m1), the USB Bluetooth dongle (m4) and the audit read once (m5). Neutral fixtures: FAKE serials, invented VID/PID and container ids.
/// </summary>
public sealed class DeviceMonitorReviewTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private const string Local = DeviceScope.LocalMachineContainer;
    private const string C1 = "{BBBBBBBB-0000-0000-0000-000000000001}", C2 = "{BBBBBBBB-0000-0000-0000-000000000002}", C3 = "{BBBBBBBB-0000-0000-0000-000000000003}";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "devreview_" + Guid.NewGuid().ToString("N"));
    public DeviceMonitorReviewTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static PnpSnapshotEntry Stick(string tail = "FAKESN0001&0", string container = C1) =>
        new($@"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.00\{tail}", "Fake Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFake____Stick_3.0", "disk", 0, container);

    private static IReadOnlyDictionary<string, MediumInfo> Media(PnpSnapshotEntry drive, MediumInfo m) => new Dictionary<string, MediumInfo>(StringComparer.OrdinalIgnoreCase) { [drive.InstanceId] = m };

    // ---- B2: UAS / USB-NVMe disks are external, never internal ------------------------------------------------------------------------------------

    [Fact]
    public void A_scsi_disk_in_a_device_container_is_an_external_disk_not_an_internal_one()
    {
        var uas = new PnpSnapshotEntry(@"SCSI\Disk&Ven_Fake&Prod_NVMe_Portable\7&1A2B3C4D&0&000000", "Fake NVMe Portable SCSI Disk Device", "DiskDrive", "(Standard disk drives)",
            @"SCSI\DiskFake____NVMe_Portable SCSI\Disk GenDisk", "disk", 0, C1);
        var d = Assert.Single(DeviceScope.Classify([uas]));
        Assert.Equal(DeviceClass.ExternalDisk, d.Kind);
        Assert.True(d.IsMedium);
    }

    [Fact]
    public void A_scsi_disk_under_a_uaspstor_node_is_an_external_disk_and_takes_the_serial_of_its_usb_node()
    {
        // no container id read: the parent decides (the USB node of the enclosure runs UASPStor and carries the serial)
        var usb = new PnpSnapshotEntry(@"USB\VID_0BDA&PID_9210\FAKENVME0001", "USB Attached SCSI (UAS) Mass Storage Device", "SCSIAdapter", "Fake", @"USB\VID_0BDA&PID_9210", "UASPStor", 0);
        var disk = new PnpSnapshotEntry(@"SCSI\Disk&Ven_Fake&Prod_SSD\8&2B3C4D5E&0&000000", "Fake SSD SCSI Disk Device", "DiskDrive", "(Standard disk drives)", @"SCSI\DiskFake____SSD GenDisk", "disk", 0,
            Parent: usb.InstanceId);
        var d = Assert.Single(DeviceScope.Classify([usb, disk]), x => x.IsMedium);
        Assert.Equal(DeviceClass.ExternalDisk, d.Kind);
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [usb, disk], null, "—", T0, initial: true);
        var row = Assert.Single(j.Devices, e => e.Class == DeviceClass.ExternalDisk);
        Assert.Equal("FAKENVME0001", row.Serial);
        // a USB parent alone (no UASPStor) decides as well
        var plain = disk with { Parent = @"USB\VID_0BDA&PID_9210\FAKENVME0002" };
        Assert.Equal(DeviceClass.ExternalDisk, Assert.Single(DeviceScope.Classify([plain])).Kind);
    }

    [Fact]
    public void A_scsi_disk_of_the_station_stays_internal()
    {
        var nvme = new PnpSnapshotEntry(@"SCSI\Disk&Ven_NVMe&Prod_Fake_SSD_1TB\4&77777777&0&000000", "NVMe Fake SSD 1TB", "DiskDrive", "Fake", @"SCSI\DiskNVMe____Fake_SSD_1TB GenDisk", "disk", 0, Local,
            Parent: @"PCI\VEN_144D&DEV_A808\4&11111111&0&0010");
        var unknown = nvme with { InstanceId = @"SCSI\Disk&Ven_Fake&Prod_SATA_HDD\4&88888888&0&010000", ContainerId = "", Parent = "" };
        Assert.Empty(DeviceScope.Classify([nvme, unknown]));
        Assert.Equal(DeviceKind.InternalDisk, DeviceRules.Classify(nvme.InstanceId, nvme.SetupClass, nvme.HardwareIds, nvme.Name));
    }

    // ---- M1: a card without a volume serial ---------------------------------------------------------------------------------------------------

    private static readonly PnpSnapshotEntry Reader = new(@"USB\VID_058F&PID_6366\FAKEREADER1", "USB Mass Storage Device", "USB", "Fake", @"USB\VID_058F&PID_6366", "", 0, C2);
    private static readonly PnpSnapshotEntry Slot = new(@"USBSTOR\Disk&Ven_Generic-&Prod_SD_MMC&Rev_1.00\FAKEREADER1&0", "Generic- SD/MMC USB Device", "DiskDrive", "Fake", @"USBSTOR\DiskGeneric-_SD/MMC", "disk", 0, C2);
    private static readonly PnpSnapshotEntry SlotVolume = new(@"STORAGE\VOLUME\_??_" + Slot.InstanceId.Replace('\\', '#'), "Volume", "Volume", "Microsoft", @"STORAGE\Volume", "volume", 0, Local);

    [Fact]
    public void Two_cards_without_a_volume_serial_are_never_merged()
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [Reader, Slot], null, "—", T0, initial: true);
        // two encrypted cards of the same capacity, told apart by their disk signature
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, new("", 32_000_000_000, "", "", "1A2B3C4D")), "—", T0.AddMinutes(1), initial: false);
        DeviceJournalMerge.Apply(j, [Reader, Slot], null, "—", T0.AddMinutes(2), initial: false);
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, new("", 32_000_000_000, "", "", "5E6F7A8B")), "—", T0.AddMinutes(3), initial: false);
        DeviceJournalMerge.Apply(j, [Reader, Slot], null, "—", T0.AddMinutes(4), initial: false);
        Assert.Equal(2, j.Devices.Count(d => d.Class == DeviceClass.MemoryCard));
        // the first card again is its own row again
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, new("", 32_000_000_000, "", "", "1A2B3C4D")), "—", T0.AddMinutes(5), initial: false);
        Assert.Equal(2, j.Devices.Count(d => d.Class == DeviceClass.MemoryCard));
        Assert.All(j.Devices.Where(d => d.Class == DeviceClass.MemoryCard), d => Assert.False(d.UnidentifiableMedium));
    }

    [Fact]
    public void A_card_that_cannot_be_identified_is_one_row_per_insertion_and_says_so()
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [Reader, Slot], null, "—", T0, initial: true);
        var unreadable = new MediumInfo("", 32_000_000_000, "", "");
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, unreadable), "—", T0.AddMinutes(1), initial: false);
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, unreadable), "—", T0.AddMinutes(2), initial: false);   // still the same insertion
        DeviceJournalMerge.Apply(j, [Reader, Slot], null, "—", T0.AddMinutes(3), initial: false);
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, unreadable), "—", T0.AddMinutes(4), initial: false);
        var cards = j.Devices.Where(d => d.Class == DeviceClass.MemoryCard).ToList();
        Assert.Equal(2, cards.Count);
        Assert.All(cards, c => Assert.True(c.UnidentifiableMedium));
        Assert.All(cards, c => Assert.Contains(DeviceMonitorRules.UnidentifiableMediumText, DeviceMonitorReport.Line(c)));
        Assert.Equal("mediu neidentificabil (criptat/RAW)", DeviceMonitorRules.UnidentifiableMediumText);
    }

    [Fact]
    public void A_card_unlocked_while_in_the_slot_is_re_keyed_and_the_link_is_returned_for_sealing()
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [Reader, Slot], null, "—", T0, initial: true);
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, new("", 32_000_000_000, "", "")), "—", T0.AddMinutes(1), initial: false);
        var before = Assert.Single(j.Devices, d => d.Class == DeviceClass.MemoryCard).Identity;
        var links = new List<MediumRekey>();
        var readable = new MediumInfo("BEEF0001", 32_000_000_000, "F:", "CARD");
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, readable), "—", T0.AddMinutes(2), initial: false, links);
        var row = Assert.Single(j.Devices, d => d.Class == DeviceClass.MemoryCard);
        Assert.Equal(DeviceMonitorRules.MediumIdentity(Slot, readable), row.Identity);
        Assert.False(row.UnidentifiableMedium);
        var link = Assert.Single(links);
        Assert.Equal(before, link.OldIdentity);
        Assert.Equal(row.Identity, link.NewIdentity);
        Assert.False(link.Merged);

        // a card that was known: the insertion's row goes, the known row continues
        DeviceJournalMerge.Apply(j, [Reader, Slot], null, "—", T0.AddMinutes(3), initial: false);
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, new("", 32_000_000_000, "", "")), "—", T0.AddMinutes(4), initial: false);
        Assert.Equal(2, j.Devices.Count(d => d.Class == DeviceClass.MemoryCard));
        links.Clear();
        DeviceJournalMerge.Apply(j, [Reader, Slot, SlotVolume], Media(Slot, readable), "—", T0.AddMinutes(5), initial: false, links);
        Assert.True(Assert.Single(links).Merged);
        Assert.Equal(row.Identity, Assert.Single(j.Devices, d => d.Class == DeviceClass.MemoryCard).Identity);
    }

    [Fact]
    public void A_sealed_re_key_moves_the_records_of_the_old_identity_to_the_new_one()
    {
        var svc = new AuthService(Path.Combine(_dir, "auth"));
        svc.AuditOperation(null, OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail("OLD|T1", "MemoryCard", "Fake card", "", T0));
        svc.AuditOperation(null, OperationAudit.DeviceAlertCritical, OperationAudit.DeviceAlertCriticalDetail("OLD|T1", "MemoryCard", "Fake card", "mediu fără număr"));
        svc.AuditOperation(null, OperationAudit.DeviceMediumRekeyed, OperationAudit.DeviceMediumRekeyedDetail("OLD|T1", "NEW|BEEF", "MemoryCard", "Fake card"));
        var r = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        Assert.Equal("NEW|BEEF", Assert.Single(r.Devices).Identity);
        Assert.Equal("NEW|BEEF", Assert.Single(r.CriticalAlerts).Identity);
        Assert.Equal(0, r.Unreadable);
    }

    // ---- M2: clones ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_second_stick_with_the_same_serial_gets_a_port_generated_id_and_is_a_possible_clone_on_its_own_row()
    {
        // Windows gives the second device reporting an already-present serial an instance id generated from the port ('&' at index 1)
        var original = Stick();
        var clone = Stick(tail: "7&2A3B4C5D&0", container: C3);
        var j = new DeviceJournal();
        var changes = DeviceJournalMerge.Apply(j, [original, clone], null, "—", T0, initial: true);
        Assert.Equal(2, j.Devices.Count);
        var c = Assert.Single(changes);
        Assert.Equal(clone.InstanceId, c.Device.InstanceId);
        Assert.Equal(DeviceMonitorRules.PortCloneAlertText, c.CloneText);
        Assert.True(DeviceMonitorRules.IsCloneAlert(c.CloneText!));
        Assert.True(DeviceMonitorRules.IsCloneAlert(DeviceMonitorRules.CloneAlertText));
        Assert.All(j.Devices, d => Assert.True(d.DuplicateSerial));
        Assert.True(j.Devices.Single(d => d.InstanceId == clone.InstanceId).NoUniqueSerial);
        // a lone device without a serial is not a clone
        var alone = new DeviceJournal();
        Assert.Empty(DeviceJournalMerge.Apply(alone, [clone], null, "—", T0, initial: true));
        Assert.False(Assert.Single(alone.Devices).DuplicateSerial);
    }

    [Fact]
    public void The_same_stick_back_with_another_volume_raises_the_volume_alert_and_keeps_its_row()
    {
        var stick = Stick();
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [stick], Media(stick, new("11112222", 16_000_000_000, "E:", "A")), "—", T0, initial: false);
        DeviceJournalMerge.SetRegistrationNumber(j, j.Devices[0].Identity, "RN-1");
        DeviceJournalMerge.Apply(j, [], null, "—", T0.AddHours(1), initial: false);
        var back = Assert.Single(DeviceJournalMerge.Apply(j, [stick], Media(stick, new("99998888", 16_000_000_000, "E:", "B")), "—", T0.AddDays(2), initial: false), x => x.Arrived);
        Assert.Equal(DeviceMonitorRules.VolumeChangedAlertText, back.InfoText);
        Assert.Equal("volum diferit (reformatat sau clonă)", DeviceMonitorRules.VolumeChangedAlertText);
        Assert.Single(j.Devices);
        // the same volume back raises nothing
        DeviceJournalMerge.Apply(j, [], null, "—", T0.AddDays(3), initial: false);
        Assert.Null(Assert.Single(DeviceJournalMerge.Apply(j, [stick], Media(stick, new("99998888", 16_000_000_000, "E:", "B")), "—", T0.AddDays(4), initial: false)).InfoText);
    }

    // ---- B1: the journal against the sealed first-seen records ------------------------------------------------------------------------------------

    private static (DeviceJournal Journal, DeviceAuditReading Audit) Sealed()
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [], null, "—", T0, initial: true);
        DeviceJournalMerge.Apply(j, [Stick()], null, "—", T0.AddHours(1), initial: false);
        var row = j.Devices[0];
        var audit = new DeviceAuditReading(true, new Dictionary<string, string>(),
            [new DeviceAuditAlert(T0.AddHours(1), OperationAudit.DeviceField(row.Identity), row.Name, "mediu de stocare fără număr de înregistrare", "UsbStorage")], 0)
        {
            Devices = [new SealedDevice(OperationAudit.DeviceField(row.Identity), "UsbStorage", row.Serial, row.FirstSeenUtc, row.FirstSeenUtc, row.Name)],
        };
        return (j, audit);
    }

    private static IReadOnlyList<string> Findings(DeviceJournal j, DeviceAuditReading a)
    {
        var from = T0.AddDays(-1);
        var to = T0.AddDays(1);
        return DeviceMonitorAuditCheck.DeviceMismatches(j, a, from, to, DeviceMonitorReport.Build(j, from, to, isolated: true).Devices);
    }

    [Fact]
    public void The_journal_that_agrees_with_the_sealed_records_has_no_finding()
    {
        var (j, a) = Sealed();
        Assert.Empty(Findings(j, a));
    }

    [Fact]
    public void A_row_re_classed_out_of_scope_is_a_finding()
    {
        var (j, a) = Sealed();
        j.Devices[0].Class = DeviceClass.Controller;                                       // "Hub": out of scope, the row would vanish from the report
        Assert.Contains(Findings(j, a), f => f.Contains("alt tip"));
    }

    [Fact]
    public void A_row_whose_dates_were_moved_before_the_period_is_a_finding()
    {
        var (j, a) = Sealed();
        var row = j.Devices[0];
        row.FirstSeenUtc = T0.AddDays(-40);
        row.LastConnectedUtc = row.LastDisconnectedUtc = T0.AddDays(-40);
        row.Present = false;
        var f = Findings(j, a);
        Assert.Contains(f, x => x.Contains("altă dată a primei observări"));
        Assert.Contains(f, x => x.Contains("nu are rând în perioadă"));
    }

    [Fact]
    public void A_deleted_row_and_its_deleted_alert_are_findings()
    {
        var (j, a) = Sealed();
        j.Devices.Clear();
        j.Alerts.Clear();
        var f = Findings(j, a);
        Assert.Contains(f, x => x.Contains("rândul lui lipsește"));
        Assert.Contains(f, x => x.Contains("alerta critică sigilată"));
    }

    [Fact]
    public void A_row_added_by_hand_is_a_finding()
    {
        var (j, a) = Sealed();
        j.Devices.Add(new DeviceJournalEntry { Identity = "SN|FAKE|ADDED", Class = DeviceClass.UsbStorage, Name = "Added stick", FirstSeenUtc = T0.AddHours(2), Present = true, RegistrationNumber = "RN-X" });
        Assert.Contains(Findings(j, a), x => x.Contains("Added stick") && x.Contains("nu are înregistrarea primei observări"));
    }

    [Fact]
    public void The_first_seen_and_reset_records_are_read_from_the_sealed_audit_and_a_reset_drops_what_came_before()
    {
        var svc = new AuthService(Path.Combine(_dir, "auth"));
        svc.AuditOperation(null, OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail("SN|A|B", "UsbStorage", "Fake Stick", "FAKESN0001", T0));
        svc.AuditOperation(null, OperationAudit.DeviceRegistrationChanged, OperationAudit.DeviceRegistrationChangedDetail("SN|A|B", "UsbStorage", "Fake Stick", "", "RN-1"));
        svc.AuditOperation(null, OperationAudit.DeviceAlertInfo, OperationAudit.DeviceAlertCriticalDetail("SN|A|B", "UsbStorage", "Fake Stick", "conectat"));
        var before = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        var dev = Assert.Single(before.Devices);
        Assert.Equal("UsbStorage", dev.Class);
        Assert.Equal("FAKESN0001", dev.Serial);
        Assert.Equal(T0, dev.FirstSeenUtc);
        Assert.Equal("UsbStorage", Assert.Single(before.InfoAlerts).Class);
        Assert.Equal("RN-1", before.LastRegistration["SN|A|B"]);

        svc.AuditOperation(null, OperationAudit.DeviceJournalReset, OperationAudit.DeviceJournalResetDetail("device_monitor.json.deteriorat-1", "ab12", "jurnal deteriorat"));
        var after = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        Assert.Empty(after.Devices);
        Assert.Empty(after.LastRegistration);
        Assert.Equal("ab12", Assert.Single(after.Resets).Sha256);
        Assert.Equal(0, after.Unreadable);
    }

    // ---- M3: save and the damaged journal ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_journal_is_saved_through_a_temporary_file_and_keeps_its_acl()
    {
        var path = Path.Combine(_dir, "j", "device_monitor.json");
        Assert.Null(DeviceJournalStore.Save(path, new DeviceJournal()));
        if (OperatingSystem.IsWindows())
        {
            var fi = new FileInfo(path);
            var sec = fi.GetAccessControl();
            var au = new System.Security.Principal.SecurityIdentifier("S-1-5-11");
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(au, System.Security.AccessControl.FileSystemRights.Modify, System.Security.AccessControl.AccessControlType.Allow));
            fi.SetAccessControl(sec);
        }
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [Stick()], null, "—", T0, initial: true);
        Assert.Null(DeviceJournalStore.Save(path, j));
        Assert.Single(DeviceJournalStore.Load(path, out var problem).Devices);
        Assert.Equal("", problem);
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));               // no temporary file left behind
        if (OperatingSystem.IsWindows())
        {
            var rules = new FileInfo(path).GetAccessControl().GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>();
            Assert.Contains(rules, r => r.IdentityReference.Value == "S-1-5-11" && r.FileSystemRights.HasFlag(System.Security.AccessControl.FileSystemRights.Modify));
        }
    }

    [Fact]
    public void Reading_a_damaged_journal_for_the_control_never_moves_it()
    {
        var path = Path.Combine(_dir, "device_monitor.json");
        File.WriteAllText(path, "{ not json");
        var j = DeviceJournalStore.Load(path, out var problem);
        Assert.Empty(j.Devices);
        Assert.Contains("deteriorat", problem);
        Assert.True(File.Exists(path));
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void The_monitor_seals_the_reset_of_a_damaged_journal_with_its_sha256_and_sets_it_aside()
    {
        var path = Path.Combine(_dir, "device_monitor.json");
        File.WriteAllText(path, "{ not json");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var audited = new List<(string Ev, string Detail)>();
        var j = DeviceJournalStore.LoadForMonitor(path, (e, d) => { audited.Add((e, d)); return null; }, out var problem);
        Assert.Empty(j.Devices);
        Assert.Contains("deteriorat", problem);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_dir, "device_monitor.json.deteriorat-*"));
        var reset = Assert.Single(audited);
        Assert.Equal(OperationAudit.DeviceJournalReset, reset.Ev);
        Assert.Contains("sha256=" + sha, reset.Detail);
        // a sound journal is loaded as it is, nothing sealed
        Assert.Null(DeviceJournalStore.Save(path, new DeviceJournal()));
        DeviceJournalStore.LoadForMonitor(path, (e, d) => { audited.Add((e, d)); return null; }, out problem);
        Assert.Single(audited);
        Assert.Equal("", problem);
    }

    // ---- m1: a short lost interval always counts ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_lost_interval_of_one_minute_is_still_a_gap()
    {
        var from = T0.AddDays(-1);
        var run = new MonitorRun { RunId = "r", StartUtc = from.AddHours(-1), LastBeatUtc = T0.AddHours(1), EndUtc = T0.AddHours(1) };
        var c = DeviceMonitorCoverage.Analyze([run], [new SealedGap(from.AddHours(3), from.AddHours(3).AddMinutes(1), "eveniment pierdut")], from, T0);
        Assert.Equal(new CoverageGap(from.AddHours(3), from.AddHours(3).AddMinutes(1)), Assert.Single(c.Gaps));
        Assert.False(c.Complete);
    }

    // ---- m4: a USB Bluetooth dongle -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_usb_bluetooth_dongle_outside_the_station_is_in_scope_and_the_station_radio_is_not()
    {
        var dongle = new PnpSnapshotEntry(@"USB\VID_0A12&PID_0001\6&3C4D5E6F&0&3", "Generic Bluetooth Radio", "Bluetooth", "Fake", @"USB\VID_0A12&PID_0001", "BTHUSB", 0, C3);
        var own = new PnpSnapshotEntry(@"USB\VID_8087&PID_0AAA\5&55555555&0&10", "Intel(R) Wireless Bluetooth(R)", "Bluetooth", "Intel", "", "BTHUSB", 0, Local);
        var d = Assert.Single(DeviceScope.Classify([dongle, own]));
        Assert.Equal(DeviceClass.BluetoothAdapter, d.Kind);
        Assert.Equal("Adaptor Bluetooth USB", DeviceMonitorRules.ClassText(d.Kind));
        Assert.True(DeviceScope.IsInScope(DeviceClass.BluetoothAdapter));
        Assert.True(DeviceMonitorRules.ForbiddenOnIsolated(DeviceClass.BluetoothAdapter, dongle.InstanceId));
    }

    // ---- m5: the audit is read once ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_chain_verdict_and_the_device_records_come_from_the_same_bytes()
    {
        var svc = new AuthService(Path.Combine(_dir, "auth"));
        svc.AuditOperation(null, OperationAudit.DeviceMonitorStarted, OperationAudit.DeviceMonitorRunDetail("run1", "pornit"));
        svc.AuditOperation(null, OperationAudit.DeviceAlertCritical, OperationAudit.DeviceAlertCriticalDetail("SN|A|B", "UsbStorage", "Fake Stick", "mediu fără număr"));
        var v = svc.VerifyAuditSnapshot(out var bytes);
        Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, v.Status);
        Assert.Equal(File.ReadAllBytes(svc.AuditPath), bytes);
        // the file changes afterwards: the reading is still about the verified bytes
        svc.AuditOperation(null, OperationAudit.DeviceAlertCritical, OperationAudit.DeviceAlertCriticalDetail("SN|C|D", "UsbStorage", "Other Stick", "mediu fără număr"));
        var r = OperationAudit.ReadDeviceMonitor(bytes, chainValid: true);
        Assert.Single(r.CriticalAlerts);
        Assert.Single(r.Runs);
        Assert.Equal(2, OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true).CriticalAlerts.Count);
        Assert.Equal(svc.VerifyAudit().Status, svc.VerifyAuditSnapshot(out _).Status);
    }
}
