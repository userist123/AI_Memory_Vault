using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Pure logic of the device monitor (owner decisions 2026-10-16): coverage from sealed running windows, clones (simultaneous and over time), the card keyed by
/// its medium, the alert rate limiter, the comparison with the sealed audit, the status words and the radios that cannot be observed. Neutral fixtures.
/// </summary>
public sealed class DeviceMonitorLogicTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "devlogic_" + Guid.NewGuid().ToString("N"));
    public DeviceMonitorLogicTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static MonitorRun Run(DateTimeOffset start, DateTimeOffset? lastBeat = null, DateTimeOffset? end = null, string id = "") =>
        new() { RunId = id, StartUtc = start, LastBeatUtc = lastBeat ?? end ?? start, EndUtc = end };

    private static PnpSnapshotEntry Stick(string serial = "FAKESN0001", string container = "{AAAAAAAA-0000-0000-0000-000000000001}") =>
        new($@"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.00\{serial}&0", "Fake Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFake____Stick_3.0", "disk", 0, container);

    // ---- coverage from sealed running windows -------------------------------------------------------------------------------------------------

    [Fact]
    public void Without_any_run_over_the_period_nothing_is_covered()
    {
        var c = DeviceMonitorCoverage.Analyze([Run(T0.AddDays(-40), end: T0.AddDays(-35))], null, T0.AddDays(-30), T0);
        Assert.False(c.AnyCoverage);
        Assert.False(c.Complete);
        var gap = Assert.Single(c.Gaps);
        Assert.Equal(T0.AddDays(-30), gap.FromUtc);
        Assert.Equal(T0, gap.ToUtc);
        Assert.False(DeviceMonitorCoverage.Analyze([], null, T0.AddDays(-1), T0).AnyCoverage);
    }

    [Fact]
    public void A_partial_run_leaves_named_gaps_and_a_full_run_leaves_none()
    {
        var from = T0.AddDays(-10);
        var partial = DeviceMonitorCoverage.Analyze([Run(from.AddDays(2), end: from.AddDays(5)), Run(from.AddDays(7), end: T0)], null, from, T0);
        Assert.True(partial.AnyCoverage);
        Assert.False(partial.Complete);
        Assert.Equal([new CoverageGap(from, from.AddDays(2)), new CoverageGap(from.AddDays(5), from.AddDays(7))], partial.Gaps);

        var full = DeviceMonitorCoverage.Analyze([Run(from.AddHours(-1), end: from.AddDays(4)), Run(from.AddDays(4).AddSeconds(30), end: T0.AddHours(1))], null, from, T0);
        Assert.True(full.Complete);                                                   // a 30-second restart is not a gap
    }

    [Fact]
    public void An_unclean_stop_is_a_gap_from_the_last_sealed_heartbeat()
    {
        var from = T0.AddDays(-2);
        // the first run died after its last heartbeat at -2d+6h; the next one started at -1d
        var runs = new[] { Run(from, lastBeat: from.AddHours(6)), Run(T0.AddDays(-1), end: T0) };
        var c = DeviceMonitorCoverage.Analyze(runs, null, from, T0);
        var gap = Assert.Single(c.Gaps);
        Assert.Equal(from.AddHours(6), gap.FromUtc);
        Assert.Equal(T0.AddDays(-1), gap.ToUtc);
    }

    [Fact]
    public void A_run_still_open_at_the_end_of_the_period_covers_it_only_within_two_heartbeats()
    {
        var from = T0.AddDays(-1);
        Assert.True(DeviceMonitorCoverage.Analyze([Run(from.AddHours(-1), lastBeat: T0.AddMinutes(-14))], null, from, T0).Complete);   // the running app doing the control
        var stale = DeviceMonitorCoverage.Analyze([Run(from.AddHours(-1), lastBeat: T0.AddHours(-3))], null, from, T0);
        Assert.Equal(new CoverageGap(T0.AddHours(-3), T0), Assert.Single(stale.Gaps));
        // an older open run followed by a newer one is NOT stretched to the end of the period
        var older = DeviceMonitorCoverage.Analyze([Run(from, lastBeat: T0.AddMinutes(-20)), Run(T0.AddMinutes(-10), lastBeat: T0.AddMinutes(-5))], null, from, T0.AddMinutes(-4));
        Assert.True(older.AnyCoverage);
    }

    [Fact]
    public void A_lost_interval_the_monitor_recorded_is_subtracted_from_the_coverage()
    {
        var from = T0.AddDays(-1);
        var c = DeviceMonitorCoverage.Analyze([Run(from.AddHours(-1), end: T0.AddHours(1))], [new SealedGap(from.AddHours(5), from.AddHours(7), "eveniment pierdut")], from, T0);
        Assert.Equal(new CoverageGap(from.AddHours(5), from.AddHours(7)), Assert.Single(c.Gaps));
        Assert.True(c.AnyCoverage);
    }

    [Fact]
    public void The_journals_own_runs_are_only_a_cross_check_and_a_faked_window_is_a_finding()
    {
        var from = T0.AddDays(-5);
        var sealedRuns = new[] { Run(T0.AddDays(-1), end: T0, id: "r1") };
        // the journal claims the monitor ran for the whole period: not backed by the sealed records
        var forged = new[] { Run(from.AddHours(-1), end: T0, id: "x") };
        var findings = DeviceMonitorCoverage.CrossCheck(forged, sealedRuns, from, T0);
        Assert.Contains(findings, f => f.Contains("nu are înregistrare în jurnalul sigilat"));
        Assert.Contains(findings, f => f.Contains("lipsește din jurnalul monitorului"));
        // and the coverage is computed from the sealed records alone
        var cov = DeviceMonitorCoverage.Analyze(sealedRuns, null, from, T0);
        Assert.False(cov.Complete);
        // agreement: no finding; a journal window that reaches further than the sealed one proves is one
        Assert.Empty(DeviceMonitorCoverage.CrossCheck([Run(T0.AddDays(-1).AddSeconds(20), end: T0, id: "r1")], sealedRuns, from, T0));
        var longer = DeviceMonitorCoverage.CrossCheck([Run(T0.AddDays(-1), end: T0, id: "r1")], [Run(T0.AddDays(-1), end: T0.AddHours(-5), id: "r1")], from, T0);
        Assert.Contains(longer, f => f.Contains("doar până la"));
    }

    // ---- the sealed audit as the source ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_sealed_audit_yields_runs_gaps_alerts_and_registrations_and_the_chain_stays_valid()
    {
        var svc = new AuthService(Path.Combine(_dir, "auth"));
        svc.AuditOperation(null, OperationAudit.DeviceMonitorStarted, OperationAudit.DeviceMonitorRunDetail("run1", "pornit"));
        svc.AuditOperation(null, OperationAudit.DeviceMonitorHeartbeat, OperationAudit.DeviceMonitorRunDetail("run1", "puls"));
        svc.AuditOperation(null, OperationAudit.DeviceMonitorGap, OperationAudit.DeviceMonitorGapDetail("run1", T0, T0.AddMinutes(10), "overflow; evenimente pierdute"));
        svc.AuditOperation(null, OperationAudit.DeviceObservedOnResume, OperationAudit.DeviceObservedOnResumeDetail("SN|A|B", "UsbStorage", "Fake Stick", true, T0, T0.AddMinutes(10)));
        svc.AuditOperation(null, OperationAudit.DeviceAlertCritical, OperationAudit.DeviceAlertCriticalDetail("SN|A|B", "UsbStorage", "Fake Stick", "mediu fără număr") + "; popup=suprimat");
        svc.AuditOperation(null, OperationAudit.DeviceRegistrationChanged, OperationAudit.DeviceRegistrationChangedDetail("SN|A|B", "UsbStorage", "Fake Stick", "", "RN-1"));
        svc.AuditOperation(null, OperationAudit.DeviceMonitorStopped, OperationAudit.DeviceMonitorRunDetail("run1", "oprire normală"));
        svc.AuditOperation(null, OperationAudit.DeviceMonitorStarted, OperationAudit.DeviceMonitorRunDetail("run2", "pornit"));      // never stopped: unclean
        var r = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        Assert.Equal(2, r.Runs.Count);
        var run1 = r.Runs.Single(x => x.RunId == "run1");
        Assert.NotNull(run1.EndUtc);
        Assert.Null(r.Runs.Single(x => x.RunId == "run2").EndUtc);
        var gap = Assert.Single(r.Gaps);
        Assert.Equal(T0, gap.FromUtc);
        Assert.Equal(T0.AddMinutes(10), gap.ToUtc);
        Assert.Contains("overflow", gap.Reason);
        Assert.Equal("apărut", Assert.Single(r.ObservedOnResume).Change);
        Assert.Equal("mediu fără număr", Assert.Single(r.CriticalAlerts).Text);
        Assert.Equal("RN-1", r.LastRegistration["SN|A|B"]);
        Assert.Equal(0, r.Unreadable);
        Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void A_registration_the_audit_has_for_a_deleted_row_is_a_mismatch()
    {
        var audit = new DeviceAuditReading(true, new Dictionary<string, string> { ["SN|A|B"] = "RN-1", ["SN|C|D"] = "RN-2", ["SN|E|F"] = "" }, [], 0);
        var journal = new[]
        {
            new DeviceJournalEntry { Identity = "SN|A|B", RegistrationNumber = "RN-1" },     // agrees
            new DeviceJournalEntry { Identity = "SN|G|H", RegistrationNumber = "RN-9" },     // a number the audit never recorded
        };
        var bad = DeviceMonitorAuditCheck.Mismatches(journal, audit);
        Assert.Equal(3, bad.Count);
        var invented = Assert.Single(bad, m => m.Identity == "SN|G|H");
        Assert.Null(invented.InAudit);
        var deleted = bad.Where(m => m.Device is null).Select(m => m.Identity).OrderBy(x => x).ToList();
        Assert.Equal(["SN|C|D", "SN|E|F"], deleted);                                         // rows the journal lost, whatever the last value was
        Assert.Equal("RN-2", bad.Single(m => m.Identity == "SN|C|D").InAudit);
        Assert.Empty(DeviceMonitorAuditCheck.Mismatches([new DeviceJournalEntry { Identity = "SN|A|B", RegistrationNumber = "RN-1" }],
            new DeviceAuditReading(true, new Dictionary<string, string> { ["SN|A|B"] = "RN-1" }, [], 0)));
    }

    // ---- clones ---------------------------------------------------------------------------------------------------------------------------------

    // two sticks with the same serial present together: see DeviceMonitorReviewTests (the second one gets a port-generated instance id, as on a real station)

    [Fact]
    public void The_same_serial_back_with_another_capacity_is_a_separate_row_marked_as_a_possible_clone()
    {
        var j = new DeviceJournal();
        var stick = Stick();
        DeviceJournalMerge.Apply(j, [stick], new Dictionary<string, MediumInfo> { [stick.InstanceId] = new("11112222", 16_000_000_000, "E:", "A") }, "user", T0, initial: false);
        DeviceJournalMerge.Apply(j, [], null, "—", T0.AddHours(1), initial: false);
        // weeks later the "same" stick comes back reporting 64 GB
        var changes = DeviceJournalMerge.Apply(j, [stick], new Dictionary<string, MediumInfo> { [stick.InstanceId] = new("33334444", 64_000_000_000, "E:", "B") }, "user", T0.AddDays(20), initial: false);
        Assert.Equal(2, j.Devices.Count);
        var dup = Assert.Single(changes.Where(c => c.Arrived)).Device;
        Assert.Equal(DeviceMonitorRules.DuplicateSerialAlertText, changes.Single(c => c.Arrived).CloneText);
        Assert.True(dup.DuplicateSerial);
        Assert.Contains("|DUP|", dup.Identity);
        Assert.True(j.Devices.All(d => d.DuplicateSerial));                                  // the original row is marked too
        Assert.Equal(64_000_000_000, dup.CapacityBytes);
        Assert.Contains("posibilă clon", DeviceMonitorRules.DuplicateSerialAlertText);
        Assert.True(DeviceMonitorRules.IsCloneAlert(DeviceMonitorRules.DuplicateSerialAlertText));
        // the 16 GB stick coming back is the first row again, not a third one
        DeviceJournalMerge.Apply(j, [], null, "—", T0.AddDays(21), initial: false);
        var back = DeviceJournalMerge.Apply(j, [stick], new Dictionary<string, MediumInfo> { [stick.InstanceId] = new("11112222", 16_000_000_000, "E:", "A") }, "user", T0.AddDays(22), initial: false);
        Assert.Equal(2, j.Devices.Count);
        Assert.Null(Assert.Single(back, c => c.Arrived).CloneText);
    }

    [Fact]
    public void A_capacity_that_was_not_known_before_is_not_a_clone()
    {
        var j = new DeviceJournal();
        var stick = Stick();
        DeviceJournalMerge.Apply(j, [stick], null, "—", T0, initial: true);                 // the medium could not be read yet
        DeviceJournalMerge.Apply(j, [stick], new Dictionary<string, MediumInfo> { [stick.InstanceId] = new("11112222", 16_000_000_000, "E:", "A") }, "—", T0.AddMinutes(1), initial: false);
        var row = Assert.Single(j.Devices);
        Assert.False(row.DuplicateSerial);
        Assert.Equal(16_000_000_000, row.CapacityBytes);
    }

    [Fact]
    public void A_serial_Windows_made_up_is_marked_as_having_no_unique_serial()
    {
        var generic = new PnpSnapshotEntry(@"USB\VID_ABCD&PID_0001\7&1A2B3C4D&0&2", "Fake USB Webcam", "Camera", "Fake", @"USB\VID_ABCD&PID_0001", "", 0, "{AAAAAAAA-0000-0000-0000-000000000005}");
        Assert.True(DeviceMonitorRules.HasNoUniqueSerial(generic.InstanceId));
        Assert.False(DeviceMonitorRules.HasNoUniqueSerial(Stick().InstanceId));
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [generic], null, "—", T0, initial: true);
        var e = Assert.Single(j.Devices);
        Assert.True(e.NoUniqueSerial);
        Assert.Equal("", e.Serial);
        Assert.StartsWith("ID|", e.Identity);                                               // told apart by where it is plugged in
        Assert.Equal("fără serie unică", DeviceMonitorRules.NoUniqueSerialText);
    }

    // ---- a card is its medium, not its insertion -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_same_card_inserted_again_is_the_same_row_keeps_its_number_and_does_not_alert_when_registered()
    {
        const string C = "{AAAAAAAA-0000-0000-0000-000000000004}";
        var parent = new PnpSnapshotEntry(@"USB\VID_058F&PID_6366\FAKEREADER1", "USB Mass Storage Device", "USB", "Fake", @"USB\VID_058F&PID_6366", "", 0, C);
        var slot = new PnpSnapshotEntry(@"USBSTOR\Disk&Ven_Generic-&Prod_SD_MMC&Rev_1.00\FAKEREADER1&0", "Generic- SD/MMC USB Device", "DiskDrive", "Fake", @"USBSTOR\DiskGeneric-_SD/MMC", "disk", 0, C);
        var vol = new PnpSnapshotEntry(@"STORAGE\VOLUME\_??_" + slot.InstanceId.Replace('\\', '#'), "Volume", "Volume", "Microsoft", @"STORAGE\Volume", "volume", 0, DeviceScope.LocalMachineContainer);
        IReadOnlyDictionary<string, MediumInfo> Card(string serial, long cap) => new Dictionary<string, MediumInfo> { [slot.InstanceId] = new(serial, cap, "F:", "CARD") };
        var j = new DeviceJournal();

        DeviceJournalMerge.Apply(j, [parent, slot], null, "—", T0, initial: true);           // the reader alone
        Assert.Equal(DeviceClass.CardReader, Assert.Single(j.Devices).Class);

        var arrived = Assert.Single(DeviceJournalMerge.Apply(j, [parent, slot, vol], Card("BEEF0001", 32_000_000_000), "user", T0.AddMinutes(1), initial: false));
        Assert.Equal(DeviceClass.MemoryCard, arrived.Device.Class);
        Assert.Equal(DeviceMonitorRules.MediumIdentity(slot, new("BEEF0001", 32_000_000_000, "F:", "CARD")), arrived.Device.Identity);
        Assert.Equal("user", arrived.Device.ConnectedUser);
        DeviceJournalMerge.SetRegistrationNumber(j, arrived.Device.Identity, "RN-CARD-1");
        Assert.Equal(DeviceAlertSeverity.Critical, DeviceMonitorRules.Decide(arrived.Device.Class, arrived.Device.InstanceId, "", true).Severity);   // before the number

        // taken out and put in ten times: still two rows (reader + card), the number kept, and a registered card does not alert
        for (int i = 0; i < 10; i++)
        {
            DeviceJournalMerge.Apply(j, [parent, slot], null, "—", T0.AddMinutes(10 + 2 * i), initial: false);
            var again = Assert.Single(DeviceJournalMerge.Apply(j, [parent, slot, vol], Card("BEEF0001", 32_000_000_000), "user", T0.AddMinutes(11 + 2 * i), initial: false), c => c.Arrived);
            Assert.Equal("RN-CARD-1", again.Device.RegistrationNumber);
            Assert.Equal(DeviceAlertSeverity.Info, DeviceMonitorRules.Decide(again.Device.Class, again.Device.InstanceId, again.Device.RegistrationNumber, true).Severity);
        }
        Assert.Equal(2, j.Devices.Count);

        // another card in the same slot is another row without a number
        DeviceJournalMerge.Apply(j, [parent, slot], null, "—", T0.AddHours(2), initial: false);
        DeviceJournalMerge.Apply(j, [parent, slot, vol], Card("CAFE0002", 16_000_000_000), "—", T0.AddHours(2).AddMinutes(1), initial: false);
        Assert.Equal(3, j.Devices.Count);
        Assert.Equal("", j.Devices.Single(d => d.VolumeSerial == "CAFE0002").RegistrationNumber);
    }

    // ---- alert rate limit -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_same_device_and_alert_kind_shows_one_popup_every_ten_minutes()
    {
        var l = new AlertRateLimiter();
        Assert.Equal(AlertGate.Show, l.Check("SN|A|B", "Critical|x", T0));
        Assert.Equal(AlertGate.SuppressedSameDevice, l.Check("SN|A|B", "Critical|x", T0.AddMinutes(9)));
        Assert.Equal(AlertGate.Show, l.Check("SN|A|B", "Critical|y", T0.AddMinutes(9)));      // another kind
        Assert.Equal(AlertGate.Show, l.Check("SN|C|D", "Critical|x", T0.AddMinutes(9)));      // another device
        Assert.Equal(AlertGate.Show, l.Check("SN|A|B", "Critical|x", T0.AddMinutes(10)));
        Assert.Equal(1, l.Suppressed);
    }

    [Fact]
    public void A_flood_is_capped_at_twenty_popups_a_minute_and_the_suppressed_ones_are_counted()
    {
        var l = new AlertRateLimiter();
        int shown = 0;
        for (int i = 0; i < 50; i++) if (l.Check("dev" + i, "Info|c", T0.AddSeconds(i % 30)) == AlertGate.Show) shown++;
        Assert.Equal(20, shown);
        Assert.Equal(30, l.Suppressed);
        Assert.Equal(AlertGate.Show, l.Check("late", "Info|c", T0.AddMinutes(2)));            // the window moved on
        l.ResetCounter();
        Assert.Equal(0, l.Suppressed);
    }

    // ---- status words and the radios that cannot be observed ------------------------------------------------------------------------------------

    [Fact]
    public void The_status_is_one_of_four_words()
    {
        Assert.Equal("Conectat", DeviceMonitorRules.StatusText(DeviceMonitorRules.Status(true, 0)));
        Assert.Equal("Conectat", DeviceMonitorRules.StatusText(DeviceMonitorRules.Status(true, -1)));
        Assert.Equal("Deconectat", DeviceMonitorRules.StatusText(DeviceMonitorRules.Status(false, 0)));
        Assert.Equal("Nu funcționează (fără driver)", DeviceMonitorRules.StatusText(DeviceMonitorRules.Status(true, 28)));
        Assert.Equal("Dezactivat", DeviceMonitorRules.StatusText(DeviceMonitorRules.Status(true, 22)));
        Assert.Equal("Deconectat", DeviceMonitorRules.StatusText(DeviceMonitorRules.Status(false, 22)));
    }

    [Fact]
    public void A_bluetooth_radio_that_is_off_or_absent_and_a_disabled_wifi_adapter_are_named_not_hidden()
    {
        PnpSnapshotEntry Radio(int code) => new(@"USB\VID_8087&PID_0AAA\5&55555555&0&10", "Intel(R) Wireless Bluetooth(R)", "Bluetooth", "Intel", "", "", code, DeviceScope.LocalMachineContainer);
        PnpSnapshotEntry Wifi(int code) => new(@"PCI\VEN_8086&DEV_2723\4&11111111&0&00E0", "Intel(R) Wi-Fi 6 AX200 160MHz", "Net", "Intel", "", "", code, DeviceScope.LocalMachineContainer);
        Assert.Empty(DeviceMonitorRules.BlindRadios([Radio(0), Wifi(0)]));
        var off = DeviceMonitorRules.BlindRadios([Radio(22), Wifi(0)]);
        Assert.Equal("Bluetooth oprit – dispozitivele Bluetooth nu pot fi observate", Assert.Single(off).Text);
        var absent = DeviceMonitorRules.BlindRadios([Wifi(0)]);
        Assert.Contains("Bluetooth absent", Assert.Single(absent).Text);
        var wifiOff = DeviceMonitorRules.BlindRadios([Radio(0), Wifi(22)]);
        Assert.Equal("Wi-Fi oprit – dispozitivele Wi-Fi nu pot fi observate", Assert.Single(wifiOff).Text);
        var journal = new DeviceJournal();
        DeviceJournalMerge.NoteRadios(journal, off, T0);
        DeviceJournalMerge.NoteRadios(journal, [], T0.AddHours(1));
        var note = Assert.Single(journal.Radios);
        Assert.False(note.BlindNow);
        Assert.Equal(T0, note.LastBlindUtc);                                                // remembered for the report of that period
    }

    [Fact]
    public void The_journal_folder_owner_rule_trusts_only_system_and_administrators()
    {
        Assert.True(DeviceJournalStore.IsTrustedOwnerSid("S-1-5-18"));
        Assert.True(DeviceJournalStore.IsTrustedOwnerSid("S-1-5-32-544"));
        Assert.False(DeviceJournalStore.IsTrustedOwnerSid("S-1-5-21-1-2-3-1001"));
        Assert.False(DeviceJournalStore.IsTrustedOwnerSid("S-1-5-32-545"));
    }

    [Fact]
    public void The_old_per_user_journal_is_moved_once_after_the_move_is_sealed()
    {
        var legacy = Path.Combine(_dir, "old", "device_monitor.json");
        var target = Path.Combine(_dir, "new", "device_monitor.json");
        var old = new DeviceJournal();
        DeviceJournalMerge.Apply(old, [Stick()], null, "—", T0, initial: true);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        Assert.Null(DeviceJournalStore.Save(legacy, old));
        var audited = new List<(string Ev, string Detail)>();

        // the seal cannot be written: nothing is moved
        Assert.Contains("audit", DeviceJournalStore.MigrateLegacy(legacy, target, (_, _) => "disc plin")!);
        Assert.False(File.Exists(target));
        Assert.True(File.Exists(legacy));

        Assert.Null(DeviceJournalStore.MigrateLegacy(legacy, target, (e, d) => { audited.Add((e, d)); return null; }));
        Assert.Equal(2, audited.Count);
        Assert.Equal(OperationAudit.DeviceJournalMigrated, audited[0].Ev);
        Assert.Contains("devices=1", audited[0].Detail);
        Assert.Equal(OperationAudit.DeviceFirstSeen, audited[1].Ev);                          // the migrated row gets its sealed first-seen record
        Assert.Single(DeviceJournalStore.Load(target, out var problem).Devices);
        Assert.Equal("", problem);
        Assert.False(File.Exists(legacy));
        // a second call changes nothing
        Assert.Null(DeviceJournalStore.MigrateLegacy(legacy, target, (e, d) => { audited.Add((e, d)); return null; }));
        Assert.Equal(2, audited.Count);
    }

    [Fact]
    public void The_machine_wide_folder_is_created_with_an_explicit_acl_and_a_file_any_account_may_modify()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "machine", "device_monitor", "device_monitor.json");
        var state = DeviceJournalStore.EnsureSecure(path);
        Assert.True(File.Exists(path), state.Warning);
        var dirSec = new DirectoryInfo(Path.GetDirectoryName(path)!).GetAccessControl();
        Assert.True(dirSec.AreAccessRulesProtected);                                         // no inherited rules
        var rules = dirSec.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().ToList();
        Assert.Contains(rules, r => r.IdentityReference.Value == "S-1-5-18" && r.FileSystemRights.HasFlag(System.Security.AccessControl.FileSystemRights.FullControl));
        Assert.Contains(rules, r => r.IdentityReference.Value == "S-1-5-32-544" && r.FileSystemRights.HasFlag(System.Security.AccessControl.FileSystemRights.FullControl));
        Assert.DoesNotContain(rules, r => r.IdentityReference.Value == "S-1-5-11" && r.FileSystemRights.HasFlag(System.Security.AccessControl.FileSystemRights.Write));
        var fileRules = new FileInfo(path).GetAccessControl().GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().ToList();
        Assert.Contains(fileRules, r => r.IdentityReference.Value == "S-1-5-11" && r.FileSystemRights.HasFlag(System.Security.AccessControl.FileSystemRights.Modify));
        Assert.Null(DeviceJournalStore.Save(path, new DeviceJournal()));                       // saved in place
        if (state.Trusted) Assert.Equal("", state.Warning);
    }
}
