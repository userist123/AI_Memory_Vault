using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Owner decision 2026-10-16: the real-time device monitor in place of the signed media register (observe and alert only). Snapshot diff, journal merge and
/// registration number by identity, alert severity per posture, the D05 check (sealed coverage, audit comparison, scope) and report section, the monitor's view
/// model with fakes (no WPF window, no station access), and the switched-off register (D04 "Nu se aplică", the tabs gone from the menu). The scope classifier,
/// coverage, clones, rate limit and audit comparison are tested apart in LogAnalyzer.Dfir.Tests. Serials are fake.
/// </summary>
public sealed class DeviceMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    // fake devices: the serials and ids are invented (no container id: the bus of the instance id decides)
    private static PnpSnapshotEntry Stick(string serial = "FAKESN0001", int code = 0) =>
        new($@"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.00\{serial}&0", "Fake Stick USB Device", "DiskDrive", "(Unități de disc standard)", @"USBSTOR\DiskFake____Stick_3.0", "disk", code);
    private static PnpSnapshotEntry UsbParent(string serial = "FAKESN0001") =>
        new($@"USB\VID_1234&PID_5678\{serial}", "USB Mass Storage Device", "USB", "Compatible USB storage device", @"USB\VID_1234&PID_5678&REV_0100", "USBSTOR", 0);
    private static readonly PnpSnapshotEntry Mouse = new(@"HID\VID_046D&PID_C077\7&1A2B3C4D&0&0000", "HID-compliant mouse", "Mouse", "Microsoft", @"HID\VID_046D&PID_C077", "mouhid", 0);
    private static readonly PnpSnapshotEntry Keyboard = new(@"HID\VID_046D&PID_C31C&MI_00\7&2B3C4D5E&0&0000", "HID Keyboard Device", "Keyboard", "(Tastaturi standard)", @"HID\VID_046D&PID_C31C&MI_00", "kbdhid", 0);
    private static readonly PnpSnapshotEntry Phone = new(@"USB\VID_04E8&PID_6860\FAKEPHONE01", "Galaxy (fake)", "WPD", "Samsung", @"USB\VID_04E8&PID_6860", "WUDFWpdMtp", 0);
    private static readonly PnpSnapshotEntry UsbEthernet = new(@"USB\VID_0BDA&PID_8153\FAKENIC0001", "Realtek USB GbE Family Controller", "Net", "Realtek", @"USB\VID_0BDA&PID_8153", "rtux64w10", 0);
    private static readonly PnpSnapshotEntry Printer = new(@"USBPRINT\FAKEPRINTER\7&33333333&0&USB001", "Fake LaserJet", "Printer", "Fake", @"USBPRINT\FAKEPRINTER", "", 0);
    private static readonly PnpSnapshotEntry NoDriver = new(@"USBPRINT\FAKEGADGET\7&44444444&0&USB002", "Fake Label Printer", "Printer", "Fake", @"USBPRINT\FAKEGADGET", "", 28);
    private static readonly PnpSnapshotEntry BluetoothRadioOff = new(@"USB\VID_8087&PID_0AAA\5&55555555&0&10", "Intel(R) Wireless Bluetooth(R)", "Bluetooth", "Intel", "", "", 22, DeviceScope.LocalMachineContainer);
    private static readonly PnpSnapshotEntry Volume = new(@"STORAGE\VOLUME\_??_USBSTOR#DISK&VEN_FAKE#FAKESN0001&0", "Volume", "Volume", "Microsoft", @"STORAGE\Volume", "volume", 0);

    private static readonly IReadOnlyDictionary<string, MediumInfo> NoMedia = new Dictionary<string, MediumInfo>();

    // ---- snapshot diff ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_diff_of_two_snapshots_finds_arrivals_removals_and_problem_code_changes_by_PnP_id()
    {
        var before = new[] { Mouse, Keyboard, NoDriver };
        var after = new[] { Mouse with { InstanceId = Mouse.InstanceId.ToLowerInvariant() }, Stick(), NoDriver with { ConfigManagerErrorCode = 0 } };
        var d = DeviceMonitorRules.Diff(before, after);
        Assert.Equal(Stick().InstanceId, Assert.Single(d.Arrived).InstanceId);
        Assert.Equal(Keyboard.InstanceId, Assert.Single(d.Removed).InstanceId);       // the mouse only changed case: same device
        Assert.Equal(NoDriver.InstanceId, Assert.Single(d.Changed).InstanceId);       // its driver was installed (28 → 0)
        Assert.True(DeviceMonitorRules.Diff(after, after).IsEmpty);
    }

    // ---- journal merge and registration number ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_snapshot_records_without_connections_then_arrivals_and_removals_are_dated()
    {
        var j = new DeviceJournal();
        Assert.Empty(DeviceJournalMerge.Apply(j, [Mouse, Keyboard], NoMedia, "—", T0, initial: true));
        Assert.Equal(2, j.Devices.Count);                                        // without container ids two HID devices with different VID/PID are two rows
        Assert.All(j.Devices, d => { Assert.True(d.Present); Assert.Equal(T0, d.FirstSeenUtc); });
    }

    [Fact]
    public void Arrivals_and_removals_are_dated_and_the_user_at_connection_is_recorded()
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [Mouse], NoMedia, "—", T0, initial: true);
        var t1 = T0.AddMinutes(5);
        var arrived = Assert.Single(DeviceJournalMerge.Apply(j, [Mouse, Stick(), UsbParent()], NoMedia, @"LAB-PC\user", t1, initial: false));
        Assert.True(arrived.Arrived);
        Assert.Equal(DeviceClass.UsbStorage, arrived.Device.Class);
        Assert.Equal(t1, arrived.Device.LastConnectedUtc);
        Assert.Equal("FAKESN0001", arrived.Device.Serial);
        Assert.Equal(@"LAB-PC\user", arrived.Device.ConnectedUser);
        Assert.Equal("—", j.Devices.Single(d => d.Class == DeviceClass.Mouse).ConnectedUser);

        var t2 = T0.AddMinutes(9);
        var gone = Assert.Single(DeviceJournalMerge.Apply(j, [Mouse], NoMedia, "—", t2, initial: false));
        Assert.False(gone.Arrived);
        Assert.False(gone.Device.Present);
        Assert.Equal(t2, gone.Device.LastDisconnectedUtc);
        Assert.Equal(2, j.Devices.Count);                                       // the row stays after disconnection
        Assert.Equal(DeviceStatus.Disconnected, gone.Device.Status);
    }

    [Fact]
    public void The_registration_number_is_kept_by_device_identity_across_disconnection_and_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "la_devmon_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var j = new DeviceJournal();
            DeviceJournalMerge.Apply(j, [Stick()], NoMedia, "—", T0, initial: true);
            var id = DeviceMonitorRules.Identity(Stick());
            Assert.StartsWith("SN|", id);
            Assert.Equal("", DeviceJournalMerge.SetRegistrationNumber(j, id, "  RN-0042 "));
            Assert.Null(DeviceJournalMerge.SetRegistrationNumber(j, "ID|NOT-THERE", "X"));
            DeviceJournalMerge.Apply(j, [], NoMedia, "—", T0.AddMinutes(1), initial: false);   // unplugged
            LogAnalyzer.Dfir.IO.Json.Write(path, j);

            var reloaded = DeviceJournalStore.Load(path, out var problem);
            Assert.Equal("", problem);
            // plugged again after a restart; the id differs only in case: the same identity, the same number
            var again = Stick() with { InstanceId = Stick().InstanceId.ToLowerInvariant() };
            var change = Assert.Single(DeviceJournalMerge.Apply(reloaded, [again], NoMedia, "—", T0.AddHours(1), initial: false));
            Assert.Equal("RN-0042", change.Device.RegistrationNumber);
            Assert.Single(reloaded.Devices);
            // another stick (other serial) has its own identity and no number
            DeviceJournalMerge.Apply(reloaded, [again, Stick("FAKESN0002")], NoMedia, "—", T0.AddHours(2), initial: false);
            Assert.Equal("", reloaded.Find(DeviceMonitorRules.Identity(Stick("FAKESN0002")))!.RegistrationNumber);
            // without a serial the identity is the instance id
            Assert.Equal("ID|" + Mouse.InstanceId.ToUpperInvariant(), DeviceMonitorRules.Identity(Mouse));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_damaged_journal_is_set_aside_by_the_monitor_after_its_reset_is_sealed_and_an_empty_one_is_started()
    {
        var path = Path.Combine(Path.GetTempPath(), "la_devmon_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            // the control only reads it (review M3): the file stays where it is
            Assert.Empty(DeviceJournalStore.Load(path, out var readOnly).Devices);
            Assert.Contains("deteriorat", readOnly);
            Assert.True(File.Exists(path));
            var sealedRecords = new List<string>();
            var j = DeviceJournalStore.LoadForMonitor(path, (ev, _) => { sealedRecords.Add(ev); return null; }, out var problem);
            Assert.Empty(j.Devices);
            Assert.Contains("deteriorat", problem);
            Assert.Equal([OperationAudit.DeviceJournalReset], sealedRecords);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".deteriorat-*"));
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f); }
    }

    // ---- alert severity per posture ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StationPosture.Standalone, true)]
    [InlineData(StationPosture.IsolatedNetwork, true)]
    [InlineData(StationPosture.Internet, false)]
    public void Alert_severity_follows_the_posture_and_the_registration_number(StationPosture posture, bool forbiddenIsCritical)
    {
        bool isolated = posture != StationPosture.Internet;
        DeviceAlertSeverity S(DeviceClass c, string id, string number = "") => DeviceMonitorRules.Decide(c, id, number, isolated).Severity;
        // a storage medium without a number is critical on every posture; with a number it is information
        Assert.Equal(DeviceAlertSeverity.Critical, S(DeviceClass.UsbStorage, Stick().InstanceId));
        Assert.Equal(DeviceAlertSeverity.Critical, S(DeviceClass.MemoryCard, @"SD\FAKE\1"));
        Assert.Equal(DeviceAlertSeverity.Critical, S(DeviceClass.ExternalDisk, Stick().InstanceId));
        Assert.Equal(DeviceAlertSeverity.Info, S(DeviceClass.UsbStorage, Stick().InstanceId, "RN-1"));
        var forbidden = forbiddenIsCritical ? DeviceAlertSeverity.Critical : DeviceAlertSeverity.Info;
        Assert.Equal(forbidden, S(DeviceClass.Phone, Phone.InstanceId, "RN-2"));
        Assert.Equal(forbidden, S(DeviceClass.Bluetooth, @"BTHENUM\DEV_001122334455\1"));
        Assert.Equal(forbidden, S(DeviceClass.WiFi, @"USB\VID_0BDA&PID_B812\FAKEWIFI001"));
        Assert.Equal(forbidden, S(DeviceClass.NetworkAdapter, UsbEthernet.InstanceId));
        // a mouse, a printer or a card reader is information everywhere
        Assert.Equal(DeviceAlertSeverity.Info, S(DeviceClass.Mouse, Mouse.InstanceId));
        Assert.Equal(DeviceAlertSeverity.Info, S(DeviceClass.Printer, Printer.InstanceId));
        Assert.Equal(DeviceAlertSeverity.Info, S(DeviceClass.CardReader, UsbParent().InstanceId));
        Assert.Contains("fără număr de înregistrare", DeviceMonitorRules.Decide(DeviceClass.UsbStorage, "", "", isolated).Reason);
    }

    // ---- the view model with fakes ----------------------------------------------------------------------------------------------------------

    private sealed class Fakes
    {
        public List<PnpSnapshotEntry> Present = [];
        public Dictionary<string, MediumInfo> Media = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Ev, string Detail)> Audited = [];
        public string? AuditRefusal;
        /// <summary>The refusal is the typed one (the sealing key cannot be read), not just text.</summary>
        public bool KeyRefusal;
        public Func<DeviceAuditReading?>? Reader;
        public int Saves;
        public DeviceJournal? Saved;
        public Action? OnSave;
        public DateTimeOffset Now = T0;
        public string User = "";

        public DeviceMonitorViewModel Create(bool isolated, DeviceJournal? journal = null, bool owner = true) => new(
            () => Present.ToList(),
            _ => new Dictionary<string, MediumInfo>(Media, StringComparer.OrdinalIgnoreCase),
            (ev, detail) => { lock (Audited) { if (AuditRefusal is null) Audited.Add((ev, detail)); } return AuditRefusal is null ? null : new LogAnalyzer.Dfir.Devices.AuditRefusal(AuditRefusal, KeyRefusal); },
            journal ?? new DeviceJournal(),
            j => { Saves++; Saved = j.Copy(); OnSave?.Invoke(); return null; },
            isolated, () => Now, consoleUser: () => User, owner: owner, auditReader: Reader);

        public List<(string Ev, string Detail)> Events(string ev) { lock (Audited) return Audited.Where(a => a.Ev == ev).ToList(); }
    }

    [Fact]
    public async Task The_monitor_shows_a_connection_immediately_alerts_and_keeps_the_row_red_after_disconnection()
    {
        var f = new Fakes { Present = [Mouse], User = @"LAB-PC\user" };
        using var vm = f.Create(isolated: true);
        var alerts = new List<DeviceAlertRecord>();
        vm.Alert += alerts.Add;
        await vm.RefreshAsync(initial: true);
        Assert.Empty(alerts);                                                    // what was there at start is not a connection
        Assert.Single(vm.Rows);

        f.Present = [Mouse, Stick(), Phone];
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        var stickRow = Assert.Single(vm.Rows, r => r.Class == DeviceClass.UsbStorage);
        Assert.True(stickRow.Present);
        Assert.True(stickRow.NeedsRegistration);
        Assert.Equal("Conectat", stickRow.StatusText);
        Assert.Equal(@"LAB-PC\user", stickRow.ConnectedUser);
        Assert.Equal("Stick USB", stickRow.ClassText);
        Assert.Contains("fără număr de înregistrare", stickRow.AlertText);
        Assert.True(stickRow.AlertIsCritical);
        Assert.Equal(2, alerts.Count);
        Assert.All(alerts, a => Assert.Equal(DeviceAlertSeverity.Critical, a.Severity));   // stick without number; phone on an isolated station
        Assert.Equal(2, vm.JournalCopy().Alerts.Count);
        Assert.Equal(2, f.Events(OperationAudit.DeviceAlertCritical).Count);                // sealed when raised

        f.Present = [Mouse];
        f.Now = T0.AddMinutes(2);
        await vm.RefreshAsync(initial: false);
        stickRow = Assert.Single(vm.Rows, r => r.Class == DeviceClass.UsbStorage);
        Assert.False(stickRow.Present);                                          // red circle, row kept
        Assert.Equal("Deconectat", stickRow.StatusText);
        Assert.NotEqual("", stickRow.LastDisconnectedText);
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}$", stickRow.LastDisconnectedText);      // local, dd.MM.yyyy HH:mm:ss
        Assert.True(f.Saves > 0);
        Assert.Equal(3, f.Saved!.Devices.Count);
    }

    [Fact]
    public async Task Nobody_logged_on_at_connection_shows_a_dash_and_internal_components_never_become_rows()
    {
        var f = new Fakes { Present = [new(@"ACPI\PNP0C0A\1", "Battery", "Battery", "Microsoft", "", "", 0, DeviceScope.LocalMachineContainer)] };
        using var vm = f.Create(isolated: false);
        await vm.RefreshAsync(initial: true);
        Assert.Empty(vm.Rows);
        f.Present = [.. f.Present, Mouse];
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        Assert.Equal("—", Assert.Single(vm.Rows).ConnectedUser);
    }

    [Fact]
    public async Task A_status_word_shows_no_driver_and_disabled_without_any_driver_column()
    {
        var f = new Fakes { Present = [Mouse] };
        using var vm = f.Create(isolated: false);
        await vm.RefreshAsync(initial: true);
        f.Present = [Mouse, NoDriver, UsbEthernet with { ConfigManagerErrorCode = 22 }];
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        Assert.Equal("Nu funcționează (fără driver)", vm.Rows.Single(r => r.Class == DeviceClass.Printer).StatusText);
        Assert.Equal("Dezactivat", vm.Rows.Single(r => r.Class == DeviceClass.NetworkAdapter).StatusText);
        Assert.DoesNotContain(typeof(DeviceMonitorRow).GetProperties(), p => p.Name.StartsWith("Driver", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_start_heartbeat_and_stop_of_the_monitor_are_sealed_and_written_in_the_journal()
    {
        var f = new Fakes { Present = [Mouse] };
        var vm = f.Create(isolated: true);
        await vm.StartAsync(eventsAvailable: true);
        var started = Assert.Single(f.Events(OperationAudit.DeviceMonitorStarted));
        Assert.StartsWith("mon=", started.Detail);
        f.Now = T0.AddMinutes(15);
        vm.Beat();
        Assert.Single(f.Events(OperationAudit.DeviceMonitorHeartbeat));
        f.Now = T0.AddMinutes(20);
        vm.Dispose();
        Assert.Single(f.Events(OperationAudit.DeviceMonitorStopped));
        var run = Assert.Single(f.Saved!.Runs);
        Assert.Equal(T0, run.StartUtc);
        Assert.Equal(T0.AddMinutes(20), run.EndUtc);
        Assert.Equal(started.Detail.Split("; ")[0][4..], run.RunId);                          // the same id in the journal and in the audit
    }

    [Fact]
    public async Task A_second_instance_shows_the_journal_but_writes_nothing_and_refuses_edits()
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [Stick()], NoMedia, "—", T0, initial: true);
        var f = new Fakes();
        using var vm = f.Create(isolated: true, j, owner: false);
        await vm.StartAsync(eventsAvailable: true);
        var row = Assert.Single(vm.Rows);
        row.RegistrationNumber = "RN-1";
        Assert.Equal("", row.RegistrationNumber);
        Assert.Contains("altă instanță", vm.Problem);
        Assert.Empty(f.Audited);
        Assert.Equal(0, f.Saves);
        Assert.Contains("Monitor nepornit", vm.Mode);
    }

    [Fact]
    public async Task A_flapping_device_raises_one_popup_but_every_alert_is_recorded_and_sealed_and_the_counter_shows_the_rest()
    {
        var f = new Fakes { Present = [Mouse] };
        using var vm = f.Create(isolated: true);
        var popups = new List<DeviceAlertRecord>();
        vm.Alert += popups.Add;
        await vm.RefreshAsync(initial: true);
        for (int i = 0; i < 5; i++)
        {
            f.Present = [Mouse, Stick()];
            f.Now = T0.AddMinutes(1 + 2 * i);
            await vm.RefreshAsync(initial: false);
            f.Present = [Mouse];
            f.Now = T0.AddMinutes(2 + 2 * i);
            await vm.RefreshAsync(initial: false);
        }
        Assert.Single(popups);                                                       // within ten minutes: one pop-up
        Assert.Equal(4, vm.SuppressedAlerts);
        Assert.Equal("4 alerte suprimate", vm.SuppressedText);
        Assert.Equal(5, vm.JournalCopy().Alerts.Count(a => a.Severity == DeviceAlertSeverity.Critical));   // the journal has all of them
        var sealedAlerts = f.Events(OperationAudit.DeviceAlertCritical);
        Assert.Equal(5, sealedAlerts.Count);                                         // so does the sealed audit, critical ones included
        Assert.Equal(4, sealedAlerts.Count(a => a.Detail.Contains("popup=suprimat")));
    }

    [Fact]
    public async Task A_lost_event_source_is_sealed_as_a_gap_and_the_rescan_records_what_changed_meanwhile()
    {
        var f = new Fakes { Present = [Mouse] };
        using var vm = f.Create(isolated: true);
        await vm.StartAsync(eventsAvailable: true);
        f.Present = [Mouse, Phone];                                                  // a phone was connected while the events were lost
        f.Now = T0.AddMinutes(20);
        vm.ReportLoss("depășire de tampon: evenimente pierdute");
        for (int i = 0; i < 100 && f.Events(OperationAudit.DeviceObservedOnResume).Count == 0; i++) await Task.Delay(30);
        var gap = Assert.Single(f.Events(OperationAudit.DeviceMonitorGap));
        Assert.Contains("reason=depășire de tampon", gap.Detail);
        Assert.Contains("from=", gap.Detail);
        var seen = Assert.Single(f.Events(OperationAudit.DeviceObservedOnResume));
        Assert.Contains("change=apărut", seen.Detail);
        Assert.Contains("observat la reluare", seen.Detail);
        Assert.Contains(vm.JournalCopy().Alerts, a => a.Text.StartsWith("observat la reluare: "));
    }

    [Fact]
    public async Task A_loss_whose_rescan_never_ran_is_still_sealed_as_a_gap_when_the_monitor_stops()
    {
        // the monitor belongs to a thread whose dispatcher is alive but never pumps: the posted rescan does not run, the stop must still seal the gap
        var f = new Fakes { Present = [Mouse] };
        DeviceMonitorViewModel? vm = null;
        using var created = new System.Threading.ManualResetEventSlim();
        using var release = new System.Threading.ManualResetEventSlim();
        var owner = new System.Threading.Thread(() =>
        {
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            vm = f.Create(isolated: true);
            created.Set();
            release.Wait();
        }) { IsBackground = true };
        owner.Start();
        created.Wait();
        try
        {
            await vm!.StartAsync(eventsAvailable: true);
            f.Now = T0.AddMinutes(20);
            vm.ReportLoss("serviciul WMI oprit");
            Assert.Empty(f.Events(OperationAudit.DeviceMonitorGap));                 // the rescan is queued on the owner's dispatcher, not run here
            f.Now = T0.AddMinutes(25);
            vm.Dispose();
            var gap = Assert.Single(f.Events(OperationAudit.DeviceMonitorGap));
            Assert.Contains("reason=serviciul WMI oprit", gap.Detail);
            Assert.Single(f.Events(OperationAudit.DeviceMonitorStopped));
        }
        finally { release.Set(); owner.Join(); }
    }

    [Fact]
    public async Task A_silence_between_readings_is_a_lost_interval_too()
    {
        var f = new Fakes { Present = [Mouse] };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        f.Now = T0.AddHours(3);                                                      // the station slept
        await vm.RefreshAsync(initial: false);
        Assert.Contains("pauza monitorului", Assert.Single(f.Events(OperationAudit.DeviceMonitorGap)).Detail);
    }

    [Fact]
    public async Task A_bluetooth_radio_that_is_off_is_shown_and_never_presented_as_no_devices()
    {
        var f = new Fakes { Present = [BluetoothRadioOff, Mouse] };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        Assert.Contains("Bluetooth oprit – dispozitivele Bluetooth nu pot fi observate", vm.RadioNotice);
        Assert.Contains(vm.JournalCopy().Radios, r => r.Kind == "Bluetooth" && r.BlindNow);
    }

    [Fact]
    public async Task Editing_the_registration_number_is_audited_with_old_and_new_value_before_it_is_saved()
    {
        var f = new Fakes { Present = [Stick()] };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        var row = Assert.Single(vm.Rows);
        int saves = f.Saves;

        row.RegistrationNumber = "RN-0042";
        var (ev, detail) = Assert.Single(f.Audited, a => a.Ev == OperationAudit.DeviceRegistrationChanged);   // besides the first-seen and radio records
        Assert.Equal(OperationAudit.DeviceRegistrationChanged, ev);
        Assert.Contains("old=(gol)", detail);
        Assert.Contains("new=RN-0042", detail);
        Assert.Contains("identity=" + DeviceMonitorRules.Identity(Stick()), detail);
        Assert.Equal("RN-0042", f.Saved!.Find(row.Identity)!.RegistrationNumber);
        Assert.True(f.Saves > saves);
        Assert.False(row.NeedsRegistration);

        row.RegistrationNumber = "RN-0043";
        Assert.Contains("old=RN-0042", f.Events(OperationAudit.DeviceRegistrationChanged)[1].Detail);
        Assert.Contains("new=RN-0043", f.Events(OperationAudit.DeviceRegistrationChanged)[1].Detail);

        // the audit cannot be written: the change is refused and the old value stays
        f.AuditRefusal = "disc plin";
        row.RegistrationNumber = "RN-9999";
        Assert.Equal("RN-0043", row.RegistrationNumber);
        Assert.Equal("RN-0043", vm.JournalCopy().Find(row.Identity)!.RegistrationNumber);
        Assert.Contains("disc plin", vm.Problem);
    }

    // ---- check D05 and the report section --------------------------------------------------------------------------------------------------

    private static readonly DateTimeOffset PeriodFrom = T0.AddDays(-30), PeriodTo = T0.AddDays(1);

    private static MonitorRun SealedFullRun(string id = "r1") => new() { RunId = id, StartUtc = PeriodFrom.AddDays(-1), LastBeatUtc = PeriodTo.AddDays(1), EndUtc = PeriodTo.AddDays(1) };

    private static DeviceJournal JournalWithStick(string number)
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver], NoMedia, "—", T0, initial: true);
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver, Stick()], NoMedia, @"LAB-PC\user", T0.AddHours(1), initial: false);
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver], NoMedia, "—", T0.AddHours(2), initial: false);
        if (number.Length > 0) DeviceJournalMerge.SetRegistrationNumber(j, DeviceMonitorRules.Identity(Stick()), number);
        DeviceJournalMerge.AddAlert(j, new DeviceAlertRecord(T0.AddHours(1), DeviceMonitorRules.Identity(Stick()), "Fake Stick USB Device", DeviceClass.UsbStorage,
            DeviceAlertSeverity.Critical, "mediu de stocare fără număr de înregistrare"));
        j.Runs.Add(SealedFullRun());
        return j;
    }

    /// <summary>
    /// The sealed audit as it would read for a journal: the registration numbers and the critical alerts written when they were raised, the first-seen record
    /// of every device (sealed on first sight), and the sealed running windows.
    /// </summary>
    private static DeviceAuditReading AuditFor(DeviceJournal j, params MonitorRun[] runs) => new(true,
        j.Devices.Where(d => d.RegistrationNumber.Length > 0).ToDictionary(d => OperationAudit.DeviceField(d.Identity), d => OperationAudit.DeviceField(d.RegistrationNumber), StringComparer.OrdinalIgnoreCase),
        j.Alerts.Where(a => a.Severity == DeviceAlertSeverity.Critical).Select(a => new DeviceAuditAlert(a.TimeUtc, OperationAudit.DeviceField(a.Identity), OperationAudit.DeviceField(a.Device), OperationAudit.DeviceField(a.Text), a.Class.ToString())).ToList(), 0)
    {
        Runs = runs,
        Devices = j.Devices.Where(DeviceScope.IsInScope).Select(d => new SealedDevice(OperationAudit.DeviceField(d.Identity), d.Class.ToString(), d.Serial, d.FirstSeenUtc, d.FirstSeenUtc, d.Name)).ToList(),
        InfoAlerts = j.Alerts.Where(a => a.Severity == DeviceAlertSeverity.Info).Select(a => new DeviceAuditAlert(a.TimeUtc, OperationAudit.DeviceField(a.Identity), OperationAudit.DeviceField(a.Device), OperationAudit.DeviceField(a.Text), a.Class.ToString())).ToList(),
    };

    private static StationFacts Facts(StationPosture posture, DeviceJournal? journal, DeviceAuditReading? audit = null) => new()
    {
        Host = "LAB-PC", Posture = posture, IsAdministrator = true, CollectedUtc = T0.AddDays(1), PeriodStartUtc = PeriodFrom, PeriodEndUtc = PeriodTo,
        DeviceMonitor = journal, DeviceAudit = audit,
    };

    private static ControlCheck D05(StationFacts f) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "D05");

    private static ControlCheck D05(StationPosture posture, DeviceJournal j) => D05(Facts(posture, j, AuditFor(j, SealedFullRun())));

    [Fact]
    public void D05_lists_every_device_seen_and_judges_storage_without_number_and_forbidden_classes_per_posture()
    {
        var bad = D05(StationPosture.Standalone, JournalWithStick(""));
        Assert.Equal(ControlStatus.Neconform, bad.Status);
        Assert.Contains(bad.Evidence, e => e.StartsWith("ÎNCALCĂ REGULA: ") && e.Contains("Stick USB") && e.Contains("fără număr de înregistrare") && e.Contains("Stare: deconectat") && e.Contains("ultima deconectare 2026-10-16 10:00:00 UTC"));
        Assert.Contains(bad.Evidence, e => e.StartsWith("ALERTĂ CRITICĂ 2026-10-16 09:00:00 UTC"));
        Assert.Contains(bad.Evidence, e => e.Contains("Mouse extern") && e.Contains("Stare: conectat") && e.Contains("ultima conectare") && e.Contains("utilizator la conectare: —") && e.Contains("registru: Neînregistrat"));
        Assert.Contains(bad.Evidence, e => e.Contains("Imprimantă") && e.Contains("nu funcționează (fără driver)"));
        Assert.Contains(bad.Evidence, e => e.Contains("utilizator la conectare: LAB-PC\\user"));
        Assert.Contains("1 fără număr de înregistrare", bad.Detail);
        Assert.Contains("Monitorul vede doar cât timp aplicația este pornită", bad.Detail);
        Assert.Equal(ControlStatus.DeVerificat, D05(StationPosture.Internet, JournalWithStick("")).Status);
        var good = D05(StationPosture.Standalone, JournalWithStick("RN-7"));
        Assert.Equal(ControlStatus.Conform, good.Status);
        Assert.Contains(good.Evidence, e => e.Contains("nr. de înregistrare RN-7"));
        Assert.Equal(ControlStatus.Nedeterminat, D05(Facts(StationPosture.Standalone, null)).Status);
        var empty = new DeviceJournal { Runs = [SealedFullRun()] };
        Assert.Equal(ControlStatus.DeVerificat, D05(StationPosture.Standalone, empty).Status);
        // a phone seen on an isolated station breaks the rule even with a number
        var j = JournalWithStick("RN-7");
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver, Phone], NoMedia, "—", T0.AddHours(3), initial: false);
        Assert.Equal(ControlStatus.Neconform, D05(StationPosture.IsolatedNetwork, j).Status);
        Assert.Equal(ControlStatus.Conform, D05(StationPosture.Internet, j).Status);
    }

    [Fact]
    public void D05_lists_only_storage_media_and_external_devices_not_the_old_internal_rows_or_a_driver_count()
    {
        var j = JournalWithStick("RN-7");
        j.Devices.Add(new DeviceJournalEntry { Identity = "ID|ACPI\\BATTERY", Class = DeviceClass.Internal, Name = "Old internal battery row", Present = true, FirstSeenUtc = T0 });
        j.Devices.Add(new DeviceJournalEntry { Identity = "ID|USB\\ROOT", Class = DeviceClass.Controller, Name = "Old root hub row", Present = true, FirstSeenUtc = T0 });
        var d = D05(StationPosture.Standalone, j);
        Assert.Equal(ControlStatus.Conform, d.Status);
        Assert.DoesNotContain(d.Evidence, e => e.Contains("Old internal battery row") || e.Contains("Old root hub row") || e.Contains("componentă internă"));
        Assert.DoesNotContain("driver nesemnat", d.Detail);
        Assert.DoesNotContain("componente interne", d.Detail);
    }

    [Fact]
    public void D05_without_a_sealed_run_over_the_period_is_undetermined_whatever_the_journal_says()
    {
        var j = JournalWithStick("RN-7");                                            // the journal claims a full run
        var d = D05(Facts(StationPosture.Standalone, j, AuditFor(j)));               // the sealed audit has none
        Assert.Equal(ControlStatus.Nedeterminat, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NICIO ACOPERIRE"));
        Assert.Contains("nu acoperă vreo parte din perioadă", d.Detail);
        // sealed runs that do not touch the period
        Assert.Equal(ControlStatus.Nedeterminat, D05(Facts(StationPosture.Standalone, j, AuditFor(j, new MonitorRun { RunId = "old", StartUtc = PeriodFrom.AddDays(-9), LastBeatUtc = PeriodFrom.AddDays(-5), EndUtc = PeriodFrom.AddDays(-5) }))).Status);
    }

    [Fact]
    public void D05_with_partial_sealed_coverage_is_to_verify_and_names_every_gap()
    {
        var j = JournalWithStick("RN-7");
        var runs = new[]
        {
            new MonitorRun { RunId = "a", StartUtc = PeriodFrom.AddDays(2), LastBeatUtc = PeriodFrom.AddDays(10), EndUtc = PeriodFrom.AddDays(10) },
            new MonitorRun { RunId = "b", StartUtc = PeriodFrom.AddDays(12), LastBeatUtc = PeriodTo.AddHours(1), EndUtc = PeriodTo.AddHours(1) },
        };
        j.Runs = runs.Select(r => r.Copy()).ToList();                                // the journal agrees with the sealed windows
        var d = D05(Facts(StationPosture.Standalone, j, AuditFor(j, runs)));
        Assert.Equal(ControlStatus.DeVerificat, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("INTERVAL NEACOPERIT") && e.Contains("2026-09-16 08:00") && e.Contains("2026-09-18 08:00"));
        Assert.Contains(d.Evidence, e => e.StartsWith("INTERVAL NEACOPERIT") && e.Contains("2026-09-26 08:00") && e.Contains("2026-09-28 08:00"));
        Assert.Contains("Acoperire incompletă", d.Detail);
    }

    [Fact]
    public void D05_a_run_that_only_the_journal_has_is_a_tampering_finding_not_coverage()
    {
        var j = JournalWithStick("RN-7");                                            // journal: one full run "r1"
        var d = D05(Facts(StationPosture.Standalone, j, AuditFor(j, new MonitorRun { RunId = "s1", StartUtc = T0.AddDays(-2), LastBeatUtc = PeriodTo.AddDays(1), EndUtc = PeriodTo.AddDays(1) })));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("nu are înregistrare în jurnalul sigilat"));
        Assert.Contains(d.Evidence, e => e.StartsWith("INTERVAL NEACOPERIT"));        // the coverage still comes from the sealed record alone
    }

    [Fact]
    public void D05_a_deleted_journal_row_with_a_sealed_registration_is_not_conform()
    {
        var j = JournalWithStick("RN-7");
        var audit = AuditFor(j, SealedFullRun());
        j.Devices.RemoveAll(d => d.Class == DeviceClass.UsbStorage);                 // the row is deleted from the plain file
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("rândul lipsește din jurnalul monitorului") && e.Contains("RN-7"));
    }

    [Fact]
    public void D05_an_edited_registration_number_is_not_conform()
    {
        var j = JournalWithStick("RN-7");
        var audit = AuditFor(j, SealedFullRun());
        DeviceJournalMerge.SetRegistrationNumber(j, DeviceMonitorRules.Identity(Stick()), "RN-FORGED");
        Assert.Equal(ControlStatus.Neconform, D05(Facts(StationPosture.Standalone, j, audit)).Status);
    }

    [Fact]
    public void D05_a_critical_alert_that_only_the_journal_holds_keeps_it_from_conform()
    {
        var j = JournalWithStick("RN-7");
        var audit = AuditFor(j, SealedFullRun());
        DeviceJournalMerge.AddAlert(j, new DeviceAlertRecord(T0.AddHours(2), DeviceMonitorRules.Identity(Stick()), "Fake Stick USB Device", DeviceClass.UsbStorage,
            DeviceAlertSeverity.Critical, "alertă adăugată doar în jurnal"));
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.DeVerificat, d.Status);
        Assert.Contains(d.Evidence, e => e.Contains("apare doar în jurnalul monitorului"));
        Assert.Contains("nu apare în jurnalul sigilat", d.Detail);
    }

    [Fact]
    public void D05_an_untrusted_journal_folder_keeps_it_from_conform_and_is_named()
    {
        var j = JournalWithStick("RN-7");
        var f = Facts(StationPosture.Standalone, j, AuditFor(j, SealedFullRun()));
        f.DeviceJournalUntrusted = "Folderul jurnalului monitorului aparține altui cont decât SYSTEM sau Administrators (S-1-5-21-1-2-3-1001); jurnalul nu este de încredere, contează jurnalul sigilat.";
        var d = D05(f);
        Assert.Equal(ControlStatus.DeVerificat, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("JURNALUL MONITORULUI NU ESTE DE ÎNCREDERE"));
    }

    [Fact]
    public void D05_names_a_bluetooth_radio_that_was_off_and_the_gaps_the_monitor_recorded_and_what_it_found_on_resume()
    {
        var j = JournalWithStick("RN-7");
        DeviceJournalMerge.NoteRadios(j, DeviceMonitorRules.BlindRadios([BluetoothRadioOff]), T0.AddHours(1));
        var baseAudit = AuditFor(j, SealedFullRun());
        var audit = new DeviceAuditReading(true, baseAudit.LastRegistration, baseAudit.CriticalAlerts, 0)
        {
            Runs = [SealedFullRun()],
            Devices = baseAudit.Devices,
            Gaps = [new SealedGap(T0.AddHours(5), T0.AddHours(6), "evenimente pierdute")],
            ObservedOnResume = [new DeviceAuditObservation(T0.AddHours(6), "SN|X|Y", "Fake Phone", "apărut")],
        };
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.DeVerificat, d.Status);                            // the lost hour is a gap
        Assert.Contains(d.Evidence, e => e.StartsWith("INTERVAL PIERDUT") && e.Contains("evenimente pierdute"));
        Assert.Contains(d.Evidence, e => e.StartsWith("observat la reluare") && e.Contains("Fake Phone"));
        Assert.Contains(d.Evidence, e => e.StartsWith("Bluetooth oprit – dispozitivele Bluetooth nu pot fi observate"));
        Assert.Contains("Bluetooth oprit", d.Detail);
    }

    // ---- review round 1: the journal against the sealed records (B1), and what keeps D05 from Conform ------------------------------------------

    [Fact]
    public void D05_a_row_re_classed_so_that_it_leaves_the_scope_is_not_conform()
    {
        var j = JournalWithStick("RN-7");
        var audit = AuditFor(j, SealedFullRun());
        j.Devices.Single(d => d.Class == DeviceClass.UsbStorage).Class = DeviceClass.Controller;   // "Hub": the stick would vanish from the report
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("alt tip"));
    }

    [Fact]
    public void D05_a_row_whose_dates_were_moved_before_the_period_is_not_conform()
    {
        var j = JournalWithStick("RN-7");
        var audit = AuditFor(j, SealedFullRun());
        var row = j.Devices.Single(d => d.Class == DeviceClass.UsbStorage);
        row.FirstSeenUtc = PeriodFrom.AddDays(-10);
        row.LastConnectedUtc = row.LastDisconnectedUtc = PeriodFrom.AddDays(-10);
        j.Alerts.Clear();                                                             // and its alert deleted from the journal
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("altă dată a primei observări"));
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("alerta critică sigilată") && e.Contains("nu are rând în perioadă"));
    }

    [Fact]
    public void D05_a_row_added_by_hand_is_not_conform()
    {
        var j = JournalWithStick("RN-7");
        var audit = AuditFor(j, SealedFullRun());
        j.Devices.Add(new DeviceJournalEntry { Identity = "SN|FAKE|ADDED", Class = DeviceClass.UsbStorage, Name = "Added stick", FirstSeenUtc = T0.AddHours(3), Present = true });
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("Added stick") && e.Contains("nu are înregistrarea primei observări"));
    }

    [Fact]
    public void D05_unreadable_sealed_records_keep_it_from_conform()
    {
        var j = JournalWithStick("RN-7");
        var a = AuditFor(j, SealedFullRun());
        var unreadable = new DeviceAuditReading(a.ChainValid, a.LastRegistration, a.CriticalAlerts, 2) { Runs = a.Runs, Devices = a.Devices, InfoAlerts = a.InfoAlerts };
        var d = D05(Facts(StationPosture.Standalone, j, unreadable));
        Assert.Equal(ControlStatus.DeVerificat, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("ÎNREGISTRĂRI SIGILATE NECITIBILE: 2"));
    }

    [Fact]
    public void D05_a_sealed_journal_reset_is_to_verify_not_a_tampering_finding()
    {
        // the damaged journal was set aside: the new journal is empty, the sealed records of before the reset are not expected in it
        var j = new DeviceJournal { Runs = [SealedFullRun()] };
        DeviceJournalMerge.Apply(j, [Mouse], NoMedia, "—", T0.AddHours(5), initial: true);
        var audit = AuditFor(j, SealedFullRun()) with { Resets = [new SealedJournalReset(T0.AddHours(5), "ab12cd", "jurnal deteriorat", "device_monitor.json.deteriorat-1")] };
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.DeVerificat, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("JURNAL RESETAT") && e.Contains("ab12cd"));
        Assert.DoesNotContain(d.Evidence, e => e.StartsWith("NECONFORM:"));
    }

    [Fact]
    public void D05_a_short_lost_interval_or_a_device_observed_on_resume_keeps_it_from_conform()
    {
        var j = JournalWithStick("RN-7");
        var lost = AuditFor(j, SealedFullRun()) with { Gaps = [new SealedGap(T0.AddHours(5), T0.AddHours(5).AddMinutes(1), "evenimente pierdute")] };
        Assert.Equal(ControlStatus.DeVerificat, D05(Facts(StationPosture.Standalone, j, lost)).Status);
        var resumed = AuditFor(j, SealedFullRun()) with { ObservedOnResume = [new DeviceAuditObservation(T0.AddHours(6), "SN|X|Y", "Fake Phone", "apărut")] };
        Assert.Equal(ControlStatus.DeVerificat, D05(Facts(StationPosture.Standalone, j, resumed)).Status);
    }

    [Fact]
    public void D05_a_volume_that_changed_is_to_verify_and_a_port_generated_clone_breaks_the_rule_on_an_isolated_station()
    {
        var j = JournalWithStick("RN-7");
        var stickId = OperationAudit.DeviceField(DeviceMonitorRules.Identity(Stick()));
        var volume = AuditFor(j, SealedFullRun()) with { InfoAlerts = [new DeviceAuditAlert(T0.AddHours(1), stickId, "Fake Stick USB Device", DeviceMonitorRules.VolumeChangedAlertText, "UsbStorage")] };
        var v = D05(Facts(StationPosture.Standalone, j, volume));
        Assert.Equal(ControlStatus.DeVerificat, v.Status);
        Assert.Contains(v.Evidence, e => e.StartsWith("VOLUM DIFERIT"));
        var clone = AuditFor(j, SealedFullRun()) with { CriticalAlerts = [new DeviceAuditAlert(T0.AddHours(1), stickId, "Fake Stick USB Device", DeviceMonitorRules.PortCloneAlertText, "UsbStorage")] };
        Assert.Equal(ControlStatus.Neconform, D05(Facts(StationPosture.Standalone, j, clone)).Status);
    }

    [Fact]
    public void D05_a_bluetooth_radio_that_was_never_switched_on_with_nothing_connected_through_it_can_be_conform()
    {
        var j = JournalWithStick("RN-7");
        DeviceJournalMerge.NoteRadios(j, DeviceMonitorRules.BlindRadios([BluetoothRadioOff]), T0.AddHours(1));
        var d = D05(Facts(StationPosture.Standalone, j, AuditFor(j, SealedFullRun())));
        Assert.Equal(ControlStatus.Conform, d.Status);
        Assert.Contains("Bluetooth oprit", d.Detail);
    }

    [Fact]
    public async Task The_monitor_seals_each_device_on_first_sight_once_and_the_radio_state_changes()
    {
        var f = new Fakes { Present = [Mouse, BluetoothRadioOff] };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        var mouse = Assert.Single(f.Events(OperationAudit.DeviceFirstSeen));
        Assert.Contains("identity=" + OperationAudit.DeviceField(DeviceMonitorRules.Identity(Mouse)), mouse.Detail);
        Assert.Contains("class=Mouse", mouse.Detail);
        Assert.Contains("state=neobservabil", Assert.Single(f.Events(OperationAudit.DeviceRadioState)).Detail);

        f.Present = [Mouse, BluetoothRadioOff, Stick(), UsbParent()];
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse, BluetoothRadioOff];
        f.Now = T0.AddMinutes(2);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse, BluetoothRadioOff, Stick(), UsbParent()];
        f.Now = T0.AddMinutes(3);
        await vm.RefreshAsync(initial: false);
        var all = f.Events(OperationAudit.DeviceFirstSeen);
        Assert.Equal(2, all.Count);                                                     // the stick once, however often it comes back
        Assert.Contains("class=UsbStorage", all[1].Detail);
        Assert.Contains("serial=FAKESN0001", all[1].Detail);
        Assert.Single(f.Events(OperationAudit.DeviceRadioState));                       // unchanged state: not sealed again

        f.Present = [Mouse, BluetoothRadioOff with { ConfigManagerErrorCode = 0 }, Stick(), UsbParent()];   // the radio is switched on
        f.Now = T0.AddMinutes(4);
        await vm.RefreshAsync(initial: false);
        Assert.Contains("state=observabil", f.Events(OperationAudit.DeviceRadioState)[1].Detail);
    }

    [Fact]
    public async Task A_first_seen_record_the_audit_refused_is_written_at_the_next_reading()
    {
        var f = new Fakes { Present = [Mouse], AuditRefusal = "disc plin" };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        Assert.Empty(f.Events(OperationAudit.DeviceFirstSeen));
        Assert.Contains("se reîncearcă", vm.Problem);
        f.AuditRefusal = null;
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        Assert.Single(f.Events(OperationAudit.DeviceFirstSeen));
    }

    [Fact]
    public async Task A_stick_back_with_another_volume_is_sealed_as_volum_diferit_and_keeps_its_row()
    {
        var f = new Fakes { Present = [Mouse] };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        f.Present = [Mouse, Stick(), UsbParent(), Volume];
        f.Media[Stick().InstanceId] = new MediumInfo("11112222", 16_000_000_000, "E:", "A");
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse];
        f.Media.Clear();
        f.Now = T0.AddMinutes(2);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse, Stick(), UsbParent(), Volume];
        f.Media[Stick().InstanceId] = new MediumInfo("99998888", 16_000_000_000, "E:", "B");
        f.Now = T0.AddMinutes(3);
        await vm.RefreshAsync(initial: false);
        Assert.Contains(f.Events(OperationAudit.DeviceAlertInfo), e => e.Detail.Contains(DeviceMonitorRules.VolumeChangedAlertText));
        Assert.Single(vm.Rows, r => r.Class == DeviceClass.UsbStorage);
    }

    [Fact]
    public void The_report_section_lists_type_state_user_registration_and_the_alerts_with_the_same_devices_as_D05()
    {
        var j = JournalWithStick("");
        var f = Facts(StationPosture.Standalone, j, AuditFor(j, SealedFullRun()));
        j.Devices.Add(new DeviceJournalEntry { Identity = "ID|ACPI\\BATTERY", Class = DeviceClass.Internal, Name = "Old internal battery row", Present = true, FirstSeenUtc = T0 });
        var r = ControlEvaluator.Evaluate(f);
        var lines = ControlReportPdf.DeviceSectionLines(r).Select(l => l.Text).ToList();
        Assert.Contains(lines, l => l.StartsWith("Medii de stocare și dispozitive conectate extern, văzute în perioadă: 3 (2 conectate acum) · încalcă regula: 1 · alerte: 1 (1 critice)"));
        Assert.Contains(lines, l => l.StartsWith("Acoperire: monitorul a rulat pe toată perioada"));
        Assert.Contains(lines, l => l.StartsWith("ALERTĂ CRITICĂ") && l.Contains("Fake Stick USB Device"));
        Assert.Contains(lines, l => l.StartsWith("ÎNCALCĂ REGULA: Stare: ") && l.Contains(" · Stick USB · ") && l.Contains("SN FAKESN0001") && l.Contains(DeviceMonitorReport.NoRegistration) && l.Contains("utilizator la conectare: LAB-PC\\user"));
        Assert.Contains(lines, l => l.StartsWith("Stare: conectat · Mouse extern · HID-compliant mouse") && l.Contains("ultima conectare") && l.Contains("VID/PID 046D:C077"));
        Assert.DoesNotContain(lines, l => l.Contains("Old internal battery row") || l.Contains("driver:"));
        // D05 lists the same devices
        var d05 = r.Checks.Single(c => c.Id == "D05");
        Assert.Equal(lines.Count(l => l.Contains(" · ") && !l.StartsWith("Medii de stocare") && !l.StartsWith("ALERTĂ") && !l.StartsWith("conectare")), d05.Evidence.Count(e => e.Contains(" · ") && !e.StartsWith("ALERTĂ")));
        Assert.Contains("3c. Dispozitive văzute de monitorul în timp real", ControlReportPdf.Corpus(r) + ControlReportPdf.DeviceSectionTitle);
        Assert.Contains("Fake Stick USB Device", ControlReportPdf.Corpus(r));
        var none = ControlReportPdf.DeviceSectionLines(ControlEvaluator.Evaluate(Facts(StationPosture.Standalone, null)));
        Assert.Contains("monitorul nu a rulat", Assert.Single(none).Text);
    }

    // ---- the signed media register switched off ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_signed_register_is_off_by_default_and_D04_then_does_not_apply_with_the_reason()
    {
        Assert.False(MediaRegisterFeature.DefaultEnabled);
        var f = Facts(StationPosture.Standalone, null);
        f.SignedMediaRegisterInUse = false;
        var d04 = ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "D04");
        Assert.Equal(ControlStatus.NuSeAplica, d04.Status);
        Assert.StartsWith("Nu se aplică: registrul semnat al mediilor nu este folosit pe această stație.", d04.Detail);
        // facts built without the switch keep evaluating D04 (the signed-register tests stay meaningful)
        Assert.NotEqual(ControlStatus.NuSeAplica, ControlEvaluator.Evaluate(Facts(StationPosture.Standalone, null)).Checks.Single(c => c.Id == "D04").Status);
    }

    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
        throw new DirectoryNotFoundException("LogAnalyzer.App");
    }

    [Fact]
    public void The_register_tab_is_gone_from_the_menu_and_the_monitor_page_is_present()
    {
        var main = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        Assert.DoesNotContain("Content=\"Registru medii\"", main);
        Assert.Contains($"Content=\"Monitor dispozitive\" GroupName=\"Sidebar\" Style=\"{{StaticResource SidebarTabStyle}}\" Command=\"{{Binding NavigateCommand}}\" CommandParameter=\"{RoleProfiles.TabMediaRegister}\"", main);
        Assert.Contains("MediaCenter.AlertIsCritical", main);
        foreach (var role in new[] { StationRole.Control })
        {
            var nav = RoleProfiles.For(role, new ClassifiedEditionProfile(), AppMode.AirGapped).Navigation;
            Assert.DoesNotContain(nav, n => n.Title == "Registru medii");
            Assert.Equal(RoleProfiles.TabMediaRegister, Assert.Single(nav, n => n.Title == "Monitor dispozitive").Tab);
        }
        var page = File.ReadAllText(Path.Combine(AppDir(), "Views", "MediaCenterView.xaml"));
        Assert.Contains("<TabItem Header=\"Monitor în timp real\"", page);
        Assert.True(page.IndexOf("<TabItem Header=\"Monitor în timp real\"", StringComparison.Ordinal) < page.IndexOf("<TabItem Header=\"Acum\"", StringComparison.Ordinal), "the monitor is the first tab");
        Assert.Contains("DataContext=\"{Binding Monitor}\"", page);
        Assert.Contains("Header=\"Nr. de înregistrare\" Binding=\"{Binding RegistrationNumber", page);
        Assert.Contains("<Ellipse", page);
        // the columns the owner asked for, and no driver column
        foreach (var header in new[] { "Stare", "Nume", "Tip", "Producător", "Model", "VID", "PID", "Serie", "Capacitate", "Literă", "Etichetă", "Utilizator", "Prima apariție", "Ultima conectare", "Ultima deconectare", "Nr. de înregistrare", "Registru", "Alertă" })
            Assert.Contains($"Header=\"{header}\"", page);
        // the columns in the owner's order, and no other data column (the last, headerless one is the „Istoric” button)
        var grid = page[page.IndexOf("x:Name=\"DevicesGrid\"", StringComparison.Ordinal)..];
        grid = grid[..grid.IndexOf("</DataGrid.Columns>", StringComparison.Ordinal)];
        var headers = System.Text.RegularExpressions.Regex.Matches(grid, "<DataGrid(?:Text|Template)Column Header=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["Stare", "Nume", "Tip", "Producător", "Model", "VID", "PID", "Serie", "Capacitate", "Literă", "Etichetă", "Utilizator", "Prima apariție", "Ultima conectare", "Ultima deconectare", "Nr. de înregistrare", "Registru", "Alertă", ""], headers);
        Assert.DoesNotContain("Header=\"Starea\"", page);
        Assert.DoesNotContain("Header=\"VID/PID\"", page);
        Assert.Contains("Content=\"Istoric\"", page);
        Assert.Contains("Content=\"Export CSV\"", page);
        Assert.Contains("{Binding SealingBanner}", page);
        Assert.DoesNotContain("Detalii driver", page);
        Assert.DoesNotContain("Header=\"Driver\"", page);
        Assert.Contains("{Binding RadioNotice}", page);
        Assert.Contains("{Binding SuppressedText}", page);
        // the register tabs stay in the code, switched off by the single flag
        Assert.Contains("<TabItem Header=\"Registrul de medii\" Visibility=\"{x:Static views:MediaRegisterUi.Visibility}\">", page);
        Assert.Contains("<TabItem Header=\"Registrul semnat\" Visibility=\"{x:Static views:MediaRegisterUi.Visibility}\">", page);
        Assert.Equal(System.Windows.Visibility.Collapsed, LogAnalyzer.UI.Views.MediaRegisterUi.Visibility);
    }

    // ---- review round 2 ---------------------------------------------------------------------------------------------------------------------

    private static readonly PnpSnapshotEntry UsbWifi = new(@"USB\VID_0BDA&PID_8812\FAKEWIFI0001", "Fake USB Wi-Fi Wireless Adapter", "Net", "Fake", @"USB\VID_0BDA&PID_8812", "rtwlanu", 0);

    private static DeviceJournal JournalWithWifi(out DeviceJournalEntry wifi)
    {
        var j = JournalWithStick("RN-7");
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver, UsbWifi], NoMedia, "—", T0.AddHours(3), initial: false);
        wifi = j.Devices.Single(d => d.Class == DeviceClass.WiFi);
        var text = DeviceMonitorRules.Decide(DeviceClass.WiFi, wifi.InstanceId, "", isolated: true).Reason;
        DeviceJournalMerge.AddAlert(j, new DeviceAlertRecord(T0.AddHours(3), wifi.Identity, wifi.Name, DeviceClass.WiFi, DeviceAlertSeverity.Critical, text));
        return j;
    }

    [Fact]
    public void D05_a_sealed_forbidden_class_alert_is_NECONFORM_even_when_the_journal_row_was_edited_to_look_disabled()
    {
        var j = JournalWithWifi(out var wifi);
        var audit = AuditFor(j, SealedFullRun());                                      // the sealed alert is there
        Assert.Equal(ControlStatus.Neconform, D05(Facts(StationPosture.Standalone, j, audit)).Status);   // the unedited row already breaks the rule
        wifi.ConfigManagerErrorCode = 22;                                              // the plain file is edited: "disabled, never active"
        wifi.ActiveSeenUtc = null;
        Assert.False(DeviceMonitorReport.ForbiddenSeen(wifi, isolated: true, PeriodFrom));
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("ÎNCALCĂ REGULA (jurnal sigilat)") && e.Contains("clasă interzisă"));
        Assert.Equal(ControlStatus.DeVerificat, D05(Facts(StationPosture.Internet, j, audit)).Status);   // an internet station: to verify, never conform
    }

    [Fact]
    public void D05_a_sealed_active_state_of_a_forbidden_class_is_NECONFORM_whatever_the_row_says()
    {
        // the adapter connected disabled (an information alert only), then was switched on: only the sealed state transition tells
        var j = JournalWithStick("RN-7");
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver, UsbWifi with { ConfigManagerErrorCode = 22 }], NoMedia, "—", T0.AddHours(3), initial: false);
        var wifi = j.Devices.Single(d => d.Class == DeviceClass.WiFi);
        var disabled = AuditFor(j, SealedFullRun()) with { States = [new SealedDeviceState(T0.AddHours(3), OperationAudit.DeviceField(wifi.Identity), "WiFi", 22, false)] };
        Assert.Equal(ControlStatus.Conform, D05(Facts(StationPosture.Standalone, j, disabled)).Status);   // disabled all along: what the policy asks
        var enabled = disabled with { States = [.. disabled.States, new SealedDeviceState(T0.AddHours(4), OperationAudit.DeviceField(wifi.Identity), "WiFi", 0, true)] };
        var d = D05(Facts(StationPosture.Standalone, j, enabled));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("ÎNCALCĂ REGULA (jurnal sigilat)") && e.Contains("activ"));
        // active before the period and still active at its start counts too
        var carried = disabled with { States = [new SealedDeviceState(PeriodFrom.AddDays(-3), OperationAudit.DeviceField(wifi.Identity), "WiFi", 0, true)] };
        Assert.Equal(ControlStatus.Neconform, D05(Facts(StationPosture.Standalone, j, carried)).Status);
    }

    private static DeviceJournal JournalWithStickRemovedInPeriod(out DeviceJournalEntry row, out string id)
    {
        var before = PeriodFrom.AddDays(-5);
        var j = new DeviceJournal { Runs = [SealedFullRun()] };
        DeviceJournalMerge.Apply(j, [Mouse], NoMedia, "—", before, initial: true);
        DeviceJournalMerge.Apply(j, [Mouse, Stick(), UsbParent()], NoMedia, @"LAB-PC\user", before.AddHours(1), initial: false);    // connected before the period
        id = DeviceMonitorRules.Identity(Stick());
        DeviceJournalMerge.SetRegistrationNumber(j, id, "RN-7");
        DeviceJournalMerge.Apply(j, [Mouse], NoMedia, "—", T0.AddHours(2), initial: false);                                           // removed during it
        row = j.Devices.Single(d => d.Class == DeviceClass.UsbStorage);
        return j;
    }

    [Fact]
    public void D05_a_stick_removed_in_the_period_whose_row_was_edited_to_absent_before_the_period_is_NECONFORM()
    {
        var j = JournalWithStickRemovedInPeriod(out var row, out var id);
        var sealedGone = new SealedPresence(T0.AddHours(2), OperationAudit.DeviceField(id), "UsbStorage", false, @"LAB-PC\user");
        var audit = AuditFor(j, SealedFullRun()) with { Presence = [sealedGone] };
        Assert.Equal(ControlStatus.Conform, D05(Facts(StationPosture.Standalone, j, audit)).Status);      // the honest row agrees with the sealed disconnect
        row.Present = false;
        row.LastConnectedUtc = row.LastDisconnectedUtc = PeriodFrom.AddDays(-4);                           // the edited row: gone before the period began
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("deconectare sigilată") && e.Contains("nu apare în perioadă"));
    }

    [Fact]
    public void D05_a_sealed_presence_at_run_start_against_a_row_that_claims_absence_is_NECONFORM()
    {
        var j = JournalWithStickRemovedInPeriod(out var row, out var id);
        var present = new SealedPresence(T0.AddHours(1), OperationAudit.DeviceField(id), "UsbStorage", true, @"LAB-PC\user");
        var audit = AuditFor(j, SealedFullRun()) with { Presence = [present] };                            // sealed: present when a run started in the period, no disconnect after
        row.Present = false;
        row.LastDisconnectedUtc = PeriodFrom.AddDays(-4);
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("prezent"));
    }

    [Fact]
    public void D05_a_sealed_disconnect_with_another_time_than_the_row_is_NECONFORM()
    {
        var j = JournalWithStickRemovedInPeriod(out var row, out var id);
        var audit = AuditFor(j, SealedFullRun()) with { Presence = [new SealedPresence(T0.AddHours(2), OperationAudit.DeviceField(id), "UsbStorage", false, "")] };
        row.LastDisconnectedUtc = T0.AddHours(-20);                                                         // still in the period, but not when it was sealed
        var d = D05(Facts(StationPosture.Standalone, j, audit));
        Assert.Equal(ControlStatus.Neconform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("NECONFORM:") && e.Contains("altă oră"));
    }

    [Fact]
    public void D05_a_journal_with_pending_seal_records_is_to_verify_never_conform_and_says_so()
    {
        var j = JournalWithStick("RN-7");
        Assert.Equal(ControlStatus.Conform, D05(StationPosture.Standalone, j).Status);
        j.PendingSeals.Add(new PendingSeal { Event = OperationAudit.DeviceAlertInfo, Detail = "identity=X; text=volum diferit" });
        var d = D05(StationPosture.Standalone, j);
        Assert.Equal(ControlStatus.DeVerificat, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("ÎNREGISTRĂRI ÎN AȘTEPTARE") && e.Contains("1"));
    }

    [Fact]
    public void D05_after_a_journal_reset_the_rebuilt_rows_keep_their_numbers_and_nothing_is_NECONFORM()
    {
        var j = JournalWithStick("RN-7");
        var audit = AuditFor(j, SealedFullRun()) with { Resets = [new SealedJournalReset(T0.AddHours(5), "ab12cd", "jurnal deteriorat", "device_monitor.json.deteriorat-1")] };
        var fresh = new DeviceJournal { Runs = [SealedFullRun()] };                                       // what the reset left
        Assert.Equal(ControlStatus.Neconform, D05(Facts(StationPosture.Standalone, fresh, audit)).Status);  // not rebuilt: rows are missing
        Assert.True(DeviceJournalRebuild.FromAudit(fresh, audit) > 0);
        var d = D05(Facts(StationPosture.Standalone, fresh, audit));
        Assert.Equal(ControlStatus.DeVerificat, d.Status);                                                  // the reset stays, to verify
        Assert.DoesNotContain(d.Evidence, e => e.StartsWith("NECONFORM:") || e.Contains("ÎNCALCĂ REGULA"));
        Assert.Equal("RN-7", fresh.Devices.Single(x => x.Class == DeviceClass.UsbStorage).RegistrationNumber);
        Assert.Contains(d.Evidence, e => e.StartsWith("JURNAL RESETAT"));
    }

    [Fact]
    public async Task The_monitor_seals_disconnections_the_set_present_at_start_and_the_problem_code_and_active_state()
    {
        var f = new Fakes { Present = [Mouse, UsbWifi], User = @"LAB-PC\user" };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        Assert.Equal(2, f.Events(OperationAudit.DevicePresent).Count);                        // the set present at the run start
        Assert.All(f.Events(OperationAudit.DevicePresent), e => Assert.Contains("mon=", e.Detail));
        var states = f.Events(OperationAudit.DeviceStateChanged);
        Assert.Contains(states, e => e.Detail.Contains("class=WiFi") && e.Detail.Contains("code=0") && e.Detail.Contains("active=da"));
        f.Present = [Mouse, UsbWifi with { ConfigManagerErrorCode = 22 }];                    // switched off
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        Assert.Contains(f.Events(OperationAudit.DeviceStateChanged), e => e.Detail.Contains("class=WiFi") && e.Detail.Contains("code=22") && e.Detail.Contains("active=nu"));
        var before = f.Events(OperationAudit.DeviceStateChanged).Count;
        f.Now = T0.AddMinutes(2);
        await vm.RefreshAsync(initial: false);
        Assert.Equal(before, f.Events(OperationAudit.DeviceStateChanged).Count);              // unchanged: not sealed again
        f.Present = [Mouse];
        f.User = @"LAB-PC\other";
        f.Now = T0.AddMinutes(3);
        await vm.RefreshAsync(initial: false);
        var gone = Assert.Single(f.Events(OperationAudit.DeviceDisconnected));
        Assert.Contains("class=WiFi", gone.Detail);
        Assert.Contains(@"user=LAB-PC\other", gone.Detail);
    }

    [Fact]
    public async Task A_refused_volume_info_alert_is_retried_at_the_next_reading()
    {
        var f = new Fakes { Present = [Mouse] };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        f.Present = [Mouse, Stick(), UsbParent(), Volume];
        f.Media[Stick().InstanceId] = new MediumInfo("11112222", 16_000_000_000, "E:", "A");
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse];
        f.Media.Clear();
        f.Now = T0.AddMinutes(2);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse, Stick(), UsbParent(), Volume];
        f.Media[Stick().InstanceId] = new MediumInfo("99998888", 16_000_000_000, "E:", "B");
        f.AuditRefusal = "disc plin";                                                          // the audit refuses while the same stick returns with another volume
        f.Now = T0.AddMinutes(3);
        await vm.RefreshAsync(initial: false);
        Assert.DoesNotContain(f.Events(OperationAudit.DeviceAlertInfo), e => e.Detail.Contains(DeviceMonitorRules.VolumeChangedAlertText));
        Assert.Contains(f.Saved!.PendingSeals, p => p.Event == OperationAudit.DeviceAlertInfo && p.Detail.Contains(DeviceMonitorRules.VolumeChangedAlertText));   // persisted, flagged pending
        f.AuditRefusal = null;
        f.Now = T0.AddMinutes(4);
        await vm.RefreshAsync(initial: false);
        Assert.Contains(f.Events(OperationAudit.DeviceAlertInfo), e => e.Detail.Contains(DeviceMonitorRules.VolumeChangedAlertText));
        Assert.Empty(vm.JournalCopy().PendingSeals);
    }

    [Fact]
    public async Task Pending_seal_records_survive_an_application_restart()
    {
        var journal = new DeviceJournal();
        journal.PendingSeals.Add(new PendingSeal { Event = OperationAudit.DeviceAlertInfo, Detail = OperationAudit.DeviceAlertCriticalDetail("SN||X", "UsbStorage", "Fake Stick", DeviceMonitorRules.VolumeChangedAlertText) });
        var f = new Fakes { Present = [Mouse] };
        using var vm = f.Create(isolated: true, journal);                                      // the new process reads the journal the old one saved
        await vm.RefreshAsync(initial: true);
        Assert.Contains(f.Events(OperationAudit.DeviceAlertInfo), e => e.Detail.Contains("identity=SN||X") && e.Detail.Contains(DeviceMonitorRules.VolumeChangedAlertText));
        Assert.Empty(vm.JournalCopy().PendingSeals);
    }

    [Fact]
    public async Task A_journal_saved_in_place_is_noted_in_the_sealed_audit_once_per_run()
    {
        var f = new Fakes { Present = [Mouse] };
        f.OnSave = DeviceJournalStore.NoteInPlaceSave;                                         // the save could not create its temporary file
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        f.Present = [Mouse, Stick(), UsbParent()];
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse];
        f.Now = T0.AddMinutes(2);
        await vm.RefreshAsync(initial: false);
        Assert.True(f.Saves >= 3);
        Assert.Single(f.Events(OperationAudit.DeviceJournalInPlaceSave));
    }

    [Fact]
    public async Task The_journal_of_a_real_monitor_run_agrees_with_the_audit_it_sealed_itself_no_false_tampering_finding()
    {
        var dir = Path.Combine(Path.GetTempPath(), "devmon_real_" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new LogAnalyzer.Dfir.Auth.AuthService(dir);
            var f = new Fakes { Present = [Mouse, UsbWifi with { ConfigManagerErrorCode = 22 }], User = @"LAB-PC\user", Now = DateTimeOffset.UtcNow };
            using var vm = new DeviceMonitorViewModel(() => f.Present.ToList(), _ => new Dictionary<string, MediumInfo>(f.Media, StringComparer.OrdinalIgnoreCase),
                (ev, detail) => { svc.AuditOperation(null, ev, detail); return null; }, new DeviceJournal(), j => null, isolated: true, () => f.Now, consoleUser: () => f.User);
            await vm.StartAsync(eventsAvailable: true);
            f.Present = [Mouse, UsbWifi with { ConfigManagerErrorCode = 22 }, Stick(), UsbParent()];
            f.Now = DateTimeOffset.UtcNow;
            await vm.RefreshAsync(initial: false);
            vm.Rows.Single(r => r.Class == DeviceClass.UsbStorage).RegistrationNumber = "RN-9";
            f.Present = [Mouse, UsbWifi with { ConfigManagerErrorCode = 22 }];                   // the stick is removed
            f.Now = DateTimeOffset.UtcNow;
            await vm.RefreshAsync(initial: false);
            vm.Stop();
            var verdict = svc.VerifyAuditSnapshot(out var bytes);
            Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, verdict.Status);
            var audit = OperationAudit.ReadDeviceMonitor(bytes, chainValid: true);
            Assert.Equal(0, audit.Unreadable);
            Assert.NotEmpty(audit.Presence);
            var now = DateTimeOffset.UtcNow;
            var facts = new StationFacts
            {
                Host = "LAB-PC", Posture = StationPosture.Standalone, IsAdministrator = true, CollectedUtc = now.AddHours(1), PeriodStartUtc = now.AddHours(-1), PeriodEndUtc = now.AddHours(1),
                DeviceMonitor = vm.JournalCopy(), DeviceAudit = audit,
            };
            var d = D05(facts);
            Assert.DoesNotContain(d.Evidence, e => e.StartsWith("NECONFORM:") || e.StartsWith("ÎNCALCĂ REGULA"));
            Assert.NotEqual(ControlStatus.Neconform, d.Status);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    // ---- columns, history, CSV export and the monitor without the right to seal -------------------------------------------------------------

    private const string KeyRefusal = "cheia de sigilare a jurnalului nu poate fi citită (porniți aplicația dintr-un cont de administrator Windows); nu s-a scris nimic";

    [Fact]
    public async Task The_row_has_the_page_columns_storage_fields_only_for_storage_and_split_VID_PID()
    {
        var f = new Fakes { Present = [Mouse], User = @"LAB-PC\user" };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        f.Media[Stick().InstanceId] = new MediumInfo("11112222", 14_900_000_000, "E:", "ETICHETA");
        f.Present = [Mouse, Stick(), UsbParent(), Phone];
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        var stick = vm.Rows.Single(r => r.Class == DeviceClass.UsbStorage);
        var phone = vm.Rows.Single(r => r.Class == DeviceClass.Phone);
        Assert.Equal(("04E8", "6860"), (phone.Vid, phone.Pid));
        var cells = stick.CsvCells();
        Assert.Equal(DeviceCsv.Headers.Count, cells.Count);
        Assert.Equal("Conectat", cells[0]);
        Assert.Equal("13,9 GB (14,9 GB pe etichetă)", cells[8]);
        Assert.Equal("E:", cells[9]);
        Assert.Equal("ETICHETA", cells[10]);
        Assert.Equal(@"LAB-PC\user", cells[11]);
        Assert.Equal("Neînregistrat", cells[16]);
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}$", cells[12]);
        Assert.Equal("", cells[14]);                                                  // never disconnected
        var mouse = vm.Rows.Single(r => r.Class == DeviceClass.Mouse);
        Assert.Equal(("046D", "C077"), (mouse.Vid, mouse.Pid));
        Assert.Equal("", mouse.CapacityText);
        Assert.Equal("", mouse.Letter);
        Assert.Equal("", mouse.Label);
        stick.RegistrationNumber = "RN-3";
        Assert.Equal("Înregistrat", stick.RegistryText);
        Assert.Equal("RN-3", stick.CsvCells()[15]);
    }

    [Fact]
    public async Task The_default_order_is_connected_first_then_the_latest_connection_first()
    {
        var f = new Fakes { Present = [Mouse] };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        f.Present = [Mouse, Stick(), UsbParent()];
        f.Now = T0.AddMinutes(1);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse, Stick(), UsbParent(), Phone];
        f.Now = T0.AddMinutes(2);
        await vm.RefreshAsync(initial: false);
        f.Present = [Mouse, Phone];                                                   // the stick is removed
        f.Now = T0.AddMinutes(3);
        await vm.RefreshAsync(initial: false);
        var order = vm.Rows.Select(r => r.Class).ToList();
        Assert.Equal(DeviceClass.UsbStorage, order[^1]);                              // disconnected last
        Assert.Equal(DeviceClass.Phone, order[0]);                                    // connected most recently first
    }

    [Fact]
    public async Task A_refusal_with_the_same_words_but_not_typed_as_key_unavailable_raises_no_banner()
    {
        var f = new Fakes { Present = [Stick(), UsbParent()], User = @"LAB-PC\user" };
        f.AuditRefusal = KeyRefusal;                                                  // the words alone are not a signal
        using var vm = f.Create(isolated: true);
        await vm.StartAsync(eventsAvailable: true);
        Assert.False(vm.SealingBlocked);
        Assert.Null(vm.JournalCopy().SealingRefused);
    }

    private static async Task<(DeviceMonitorViewModel Vm, DeviceJournal Journal, string Identity, Fakes F)> StickWithJournalNumber(string journalNumber, string? sealedNumber, bool chainValid = true)
    {
        var f = new Fakes { Present = [Stick(), UsbParent()], User = @"LAB-PC\user" };
        using (var first = f.Create(isolated: true)) await first.RefreshAsync(initial: true);
        var journal = f.Saved!.Copy();
        var entry = journal.Devices.Single(d => d.Class == DeviceClass.UsbStorage);
        entry.RegistrationNumber = journalNumber;                                     // what an editor of the plain file wrote
        var map = sealedNumber is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [OperationAudit.DeviceField(entry.Identity)] = sealedNumber };
        f.Reader = () => new DeviceAuditReading(chainValid, map, [], 0);
        var vm = f.Create(isolated: true, journal);
        return (vm, journal, entry.Identity, f);
    }

    [Fact]
    public async Task A_registration_number_only_the_journal_has_is_never_shown_as_registered_on_the_page_the_csv_or_the_report()
    {
        var (vm, journal, _, f) = await StickWithJournalNumber("RN-FAKE", sealedNumber: null);
        using (vm)
        {
            var row = vm.Rows.Single(r => r.Class == DeviceClass.UsbStorage);
            Assert.Equal("RN-FAKE", row.RegistrationNumber);                          // the cell shows the journal value ...
            Assert.Equal("Neconfirmat (nesigilat)", row.RegistryText);                // ... with its state visible
            Assert.True(row.NeedsRegistration);                                       // the red style follows the sealed state
            Assert.False(row.HasNoNumber);
            var cells = row.CsvCells();
            Assert.Equal("RN-FAKE", cells[15]);
            Assert.Equal("Neconfirmat (nesigilat)", cells[16]);
            Assert.DoesNotContain(cells, c => c == "Înregistrat");
            Assert.EndsWith("registru: Neconfirmat (nesigilat)", DeviceMonitorReport.Line(journal.Devices.Single(d => d.Class == DeviceClass.UsbStorage), f.Reader!()));
        }
    }

    [Fact]
    public async Task A_journal_number_that_differs_from_the_sealed_one_is_unconfirmed_and_an_equal_one_is_registered()
    {
        var (vm, _, _, _) = await StickWithJournalNumber("RN-9", sealedNumber: "RN-1");
        using (vm) Assert.Equal("Neconfirmat (nesigilat)", vm.Rows.Single(r => r.Class == DeviceClass.UsbStorage).RegistryText);
        var (vm2, _, _, _) = await StickWithJournalNumber("RN-1", sealedNumber: "RN-1");
        using (vm2)
        {
            var row = vm2.Rows.Single(r => r.Class == DeviceClass.UsbStorage);
            Assert.Equal("Înregistrat", row.RegistryText);
            Assert.False(row.NeedsRegistration);
        }
        var (vm3, _, _, _) = await StickWithJournalNumber("RN-1", sealedNumber: "RN-1", chainValid: false);      // a chain that does not verify proves nothing
        using (vm3) Assert.Equal("Neconfirmat (nesigilat)", vm3.Rows.Single(r => r.Class == DeviceClass.UsbStorage).RegistryText);
        var (vm4, _, _, _) = await StickWithJournalNumber("", sealedNumber: null);
        using (vm4)
        {
            var row = vm4.Rows.Single(r => r.Class == DeviceClass.UsbStorage);
            Assert.Equal("Neînregistrat", row.RegistryText);
            Assert.True(row.NeedsRegistration);
            Assert.True(row.HasNoNumber);
        }
    }

    [Fact]
    public async Task A_number_sealed_by_this_monitor_is_registered_until_the_sealed_audit_says_otherwise()
    {
        var f = new Fakes { Present = [Stick(), UsbParent()], User = @"LAB-PC\user" };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        var row = vm.Rows.Single(r => r.Class == DeviceClass.UsbStorage);
        row.RegistrationNumber = "RN-3";
        Assert.Equal("Înregistrat", row.RegistryText);
        f.AuditRefusal = "disc plin";
        row.RegistrationNumber = "RN-4";                                              // refused: not sealed, not changed
        Assert.Equal("RN-3", row.RegistrationNumber);
        Assert.Equal("Înregistrat", row.RegistryText);
    }

    [Fact]
    public async Task An_export_of_no_rows_seals_zero_rows_and_the_file_is_only_the_header()
    {
        var f = new Fakes { Present = [Mouse], User = @"LAB-PC\user" };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        var export = DeviceMonitorViewModel.PrepareCsv([]);
        Assert.Equal(0, export.Rows);
        Assert.Null(vm.SealCsvExport(export));
        Assert.Contains("rows=0", Assert.Single(f.Events(OperationAudit.DeviceExportCsv)).Detail);
        Assert.Contains("sha256=" + export.Sha256, f.Events(OperationAudit.DeviceExportCsv)[0].Detail);
        var text = new System.Text.UTF8Encoding(false).GetString(export.Bytes, 3, export.Bytes.Length - 3);
        Assert.Equal(DeviceCsv.Line(DeviceCsv.Headers) + "\r\n", text);
    }

    [Fact]
    public async Task A_refused_seal_because_the_key_cannot_be_read_raises_the_banner_and_D05_never_says_conform()
    {
        var f = new Fakes { Present = [Stick(), UsbParent()], User = @"LAB-PC\user" };
        f.AuditRefusal = KeyRefusal;
        f.KeyRefusal = true;
        using var vm = f.Create(isolated: true);
        Assert.False(vm.SealingBlocked);
        await vm.StartAsync(eventsAvailable: true);
        Assert.True(vm.SealingBlocked);
        Assert.Equal("Monitorul nu poate sigila evenimentele: porniți LogAnalyzer ca administrator. Până atunci D05 rămâne DE VERIFICAT.", vm.SealingBanner);
        var journal = vm.JournalCopy();
        Assert.NotNull(journal.SealingRefused);
        Assert.NotNull(f.Saved?.SealingRefused);                                      // saved, so a restart still knows
        var run = new MonitorRun { RunId = "r", StartUtc = PeriodFrom.AddHours(-1), LastBeatUtc = PeriodTo.AddHours(1), EndUtc = PeriodTo.AddHours(1) };
        journal.SealingRefused = new SealingRefusal { FirstUtc = PeriodFrom.AddHours(1), LastUtc = PeriodFrom.AddHours(2) };
        var d = D05(Facts(StationPosture.Standalone, journal, AuditFor(journal, run)));
        Assert.NotEqual(ControlStatus.Conform, d.Status);
        Assert.Contains(d.Evidence, e => e.StartsWith("MONITORUL A RULAT FĂRĂ DREPT DE SIGILARE"));
        // another refusal (disk full) is not the key: no banner
        var g = new Fakes { Present = [Mouse], AuditRefusal = "discul este plin" };
        using var vm2 = g.Create(isolated: true);
        await vm2.StartAsync(eventsAvailable: true);
        Assert.False(vm2.SealingBlocked);
        Assert.Null(vm2.JournalCopy().SealingRefused);
        // the right to seal is back: the banner goes
        f.AuditRefusal = null;
        f.Now = T0.AddMinutes(5);
        await vm.RefreshAsync(initial: false);
        Assert.False(vm.SealingBlocked);
    }

    [Fact]
    public async Task Export_CSV_prepares_the_visible_rows_with_a_BOM_and_seals_a_note_with_the_hash_and_no_path()
    {
        var f = new Fakes { Present = [Mouse, Stick(), UsbParent()], User = @"LAB-PC\user" };
        using var vm = f.Create(isolated: true);
        await vm.RefreshAsync(initial: true);
        var export = DeviceMonitorViewModel.PrepareCsv(vm.Rows.Take(1));
        Assert.Equal(1, export.Rows);
        Assert.Equal([0xEF, 0xBB, 0xBF], export.Bytes.Take(3).ToArray());
        var text = System.Text.Encoding.UTF8.GetString(export.Bytes, 3, export.Bytes.Length - 3);
        Assert.Equal(2, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);   // the header and the one row in view
        Assert.StartsWith("Stare;Nume;Tip;", text);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(export.Bytes)), export.Sha256);
        Assert.Null(vm.SealCsvExport(export));
        var note = Assert.Single(f.Events(OperationAudit.DeviceExportCsv));
        Assert.Contains("rows=1", note.Detail);
        Assert.Contains("sha256=" + export.Sha256, note.Detail);
        Assert.Contains("operator=", note.Detail);
        Assert.DoesNotContain(Path.GetTempPath(), note.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".csv", note.Detail, StringComparison.OrdinalIgnoreCase);
        // the sealed audit refuses the note: the caller writes nothing
        f.AuditRefusal = "audit indisponibil";
        Assert.Contains("audit indisponibil", vm.SealCsvExport(export)?.Reason);
    }

    [Fact]
    public async Task The_history_of_a_row_comes_from_the_sealed_audit_and_marks_pending_records()
    {
        var dir = Path.Combine(Path.GetTempPath(), "devhist_app_" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new LogAnalyzer.Dfir.Auth.AuthService(dir);
            var f = new Fakes { Present = [Mouse], User = @"LAB-PC\user", Now = DateTimeOffset.UtcNow };
            using var vm = new DeviceMonitorViewModel(() => f.Present.ToList(), _ => new Dictionary<string, MediumInfo>(),
                (ev, detail) => { if (f.AuditRefusal is { } r) return r; svc.AuditOperation(null, ev, detail); return null; }, new DeviceJournal(), j => null, isolated: true, () => f.Now,
                consoleUser: () => f.User, auditReader: () => { var v = svc.VerifyAuditSnapshot(out var b); return OperationAudit.ReadDeviceMonitor(b, v.Status == LogAnalyzer.Dfir.Case.ChainStatus.Valid); });
            await vm.RefreshAsync(initial: true);
            f.Present = [Mouse, Stick(), UsbParent()];
            f.Now = DateTimeOffset.UtcNow;
            await vm.RefreshAsync(initial: false);
            var stick = vm.Rows.Single(r => r.Class == DeviceClass.UsbStorage);
            stick.RegistrationNumber = "RN-4";
            f.Present = [Mouse];
            f.Now = DateTimeOffset.UtcNow;
            await vm.RefreshAsync(initial: false);
            f.AuditRefusal = "disc plin";                                              // the next records cannot be sealed: they stay pending
            f.Present = [Mouse, Stick(), UsbParent()];
            f.Now = DateTimeOffset.UtcNow;
            await vm.RefreshAsync(initial: false);
            var (events, notice) = vm.HistoryFor(stick);
            Assert.Equal("", notice);
            var sealedKinds = events.Where(e => !e.Pending).Select(e => e.Kind).ToList();
            Assert.Equal(DeviceHistory.FirstSeen, sealedKinds[0]);
            Assert.Contains(DeviceHistory.RegistrationChanged, sealedKinds);
            Assert.Contains(DeviceHistory.Disconnected, sealedKinds);
            Assert.Contains(events, e => e.Kind == DeviceHistory.RegistrationChanged && e.Details == "(gol) → RN-4");
            var times = events.Where(e => !e.Pending).Select(e => e.TimeUtc).ToList();
            Assert.Equal(times.OrderBy(t => t).ToList(), times);
            // the reconnection could not be sealed: it is listed after the sealed ones, marked
            Assert.Contains(events, e => e.Pending && e.Kind.Contains("în așteptare (nesigilat)"));
            Assert.All(events.Where(e => e.Pending), e => Assert.Contains("în așteptare (nesigilat)", e.Kind));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
