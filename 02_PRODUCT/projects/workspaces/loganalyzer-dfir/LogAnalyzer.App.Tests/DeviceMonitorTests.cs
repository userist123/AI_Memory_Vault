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
/// Owner decision 2026-10-16: the real-time device monitor in place of the signed media register (observe and alert only). Pure rules (snapshot diff, class,
/// driver state, journal merge and registration number by identity, alert severity per posture), the D05 check and report section, the monitor's view model
/// with fakes (no WPF window, no station access), and the switched-off register (D04 "Nu se aplică", the tabs gone from the menu). Serials are fake.
/// </summary>
public sealed class DeviceMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    // fake devices: the serials and ids are invented
    private static PnpSnapshotEntry Stick(string serial = "FAKESN0001", int code = 0) =>
        new($@"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.00\{serial}&0", "Fake Stick USB Device", "DiskDrive", "(Unități de disc standard)", @"USBSTOR\DiskFake____Stick_3.0", "disk", code);
    private static PnpSnapshotEntry UsbParent(string serial = "FAKESN0001") =>
        new($@"USB\VID_1234&PID_5678\{serial}", "USB Mass Storage Device", "USB", "Compatible USB storage device", @"USB\VID_1234&PID_5678&REV_0100", "USBSTOR", 0);
    private static readonly PnpSnapshotEntry Mouse = new(@"HID\VID_046D&PID_C077\7&1A2B3C4D&0&0000", "HID-compliant mouse", "Mouse", "Microsoft", @"HID\VID_046D&PID_C077", "mouhid", 0);
    private static readonly PnpSnapshotEntry Keyboard = new(@"HID\VID_046D&PID_C31C&MI_00\7&2B3C4D5E&0&0000", "HID Keyboard Device", "Keyboard", "(Tastaturi standard)", @"HID\VID_046D&PID_C31C&MI_00", "kbdhid", 0);
    private static readonly PnpSnapshotEntry Phone = new(@"USB\VID_04E8&PID_6860\FAKEPHONE01", "Galaxy (fake)", "WPD", "Samsung", @"USB\VID_04E8&PID_6860", "WUDFWpdMtp", 0);
    private static readonly PnpSnapshotEntry Wifi = new(@"PCI\VEN_8086&DEV_2723&SUBSYS_00848086\4&11111111&0&00E0", "Intel(R) Wi-Fi 6 AX200 160MHz", "Net", "Intel", @"PCI\VEN_8086&DEV_2723", "Netwtw10", 0);
    private static readonly PnpSnapshotEntry Ethernet = new(@"PCI\VEN_10EC&DEV_8168\4&22222222&0&00E4", "Realtek PCIe GbE Family Controller", "Net", "Realtek", @"PCI\VEN_10EC&DEV_8168", "rt640x64", 0);
    private static readonly PnpSnapshotEntry UsbEthernet = new(@"USB\VID_0BDA&PID_8153\FAKENIC0001", "Realtek USB GbE Family Controller", "Net", "Realtek", @"USB\VID_0BDA&PID_8153", "rtux64w10", 0);
    private static readonly PnpSnapshotEntry Printer = new(@"USBPRINT\FAKEPRINTER\7&33333333&0&USB001", "Fake LaserJet", "Printer", "Fake", @"USBPRINT\FAKEPRINTER", "", 0);
    private static readonly PnpSnapshotEntry NoDriver = new(@"USB\VID_ABCD&PID_0001\FAKEGADGET01", "Unknown device", "", "", @"USB\VID_ABCD&PID_0001", "", 28);
    private static readonly PnpSnapshotEntry Volume = new(@"STORAGE\VOLUME\_??_USBSTOR#DISK&VEN_FAKE#FAKESN0001&0", "Volume", "Volume", "Microsoft", @"STORAGE\Volume", "volume", 0);

    private static readonly IReadOnlyDictionary<string, SignedDriverInfo> NoDrivers = new Dictionary<string, SignedDriverInfo>();

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

    // ---- classes --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_class_is_recognised_and_duplicate_views_of_a_disk_are_skipped()
    {
        DeviceClass? C(PnpSnapshotEntry e) => DeviceMonitorRules.Classify(e.InstanceId, e.SetupClass, e.HardwareIds, e.Name);
        Assert.Equal(DeviceClass.UsbStorage, C(Stick()));
        Assert.Equal(DeviceClass.Controller, C(UsbParent()));
        Assert.Equal(DeviceClass.Mouse, C(Mouse));
        Assert.Equal(DeviceClass.Keyboard, C(Keyboard));
        Assert.Equal(DeviceClass.Phone, C(Phone));
        Assert.Equal(DeviceClass.WiFi, C(Wifi));
        Assert.Equal(DeviceClass.NetworkAdapter, C(Ethernet));
        Assert.Equal(DeviceClass.Printer, C(Printer));
        Assert.Equal(DeviceClass.Other, C(NoDriver));
        Assert.Null(C(Volume));
        Assert.Null(DeviceMonitorRules.Classify(@"SWD\WPDBUSENUM\_??_USBSTOR#DISK&VEN_FAKE#FAKESN0001&0", "WPD", "", "E:\\"));
        Assert.Equal(DeviceClass.Bluetooth, DeviceMonitorRules.Classify(@"BTHENUM\DEV_001122334455\8&1&0&BLUETOOTHDEVICE_001122334455", "Bluetooth", "", "Fake Headset"));
        Assert.Equal(DeviceClass.Camera, DeviceMonitorRules.Classify(@"USB\VID_046D&PID_085B&MI_00\7&44444444&0&0000", "Camera", "", "Fake Webcam"));
        Assert.Equal(DeviceClass.Scanner, DeviceMonitorRules.Classify(@"USB\VID_04B8&PID_0142\FAKESCAN01", "Image", "", "Fake Scanner"));
        Assert.Equal(DeviceClass.Audio, DeviceMonitorRules.Classify(@"HDAUDIO\FUNC_01&VEN_10EC\4&55555555&0&0001", "MEDIA", "", "Realtek High Definition Audio"));
        Assert.Equal(DeviceClass.Optical, DeviceMonitorRules.Classify(@"SCSI\CDROM&VEN_FAKE&PROD_DVD\4&66666666&0&000100", "CDROM", "", "Fake DVD"));
        Assert.Equal(DeviceClass.Internal, DeviceMonitorRules.Classify(@"ACPI\PNP0C0A\1", "Battery", "", "Microsoft ACPI-Compliant Control Method Battery"));
        Assert.Equal(DeviceClass.Internal, DeviceMonitorRules.Classify(@"SWD\PRINTENUM\{FAKE}", "PrintQueue", "", "Microsoft Print to PDF"));
        Assert.Equal(DeviceClass.Internal, DeviceMonitorRules.Classify(@"ROOT\VMS_MP\0000", "Net", "", "Hyper-V Virtual Ethernet Adapter"));
    }

    // ---- driver state ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_driver_state_comes_from_ConfigManagerErrorCode_and_the_signed_driver_data()
    {
        var signed = new SignedDriverInfo("10.0.26100.1", "Microsoft", "2026-01-15", true, "Microsoft Windows");
        Assert.Equal(DriverState.Missing, DeviceMonitorRules.Driver(28, signed).State);
        Assert.Contains("cod 28", DeviceMonitorRules.Driver(28, null).Text);
        var err = DeviceMonitorRules.Driver(43, signed);
        Assert.Equal(DriverState.Error, err.State);
        Assert.Contains("cod 43", err.Text);
        Assert.Equal(DriverState.Error, DeviceMonitorRules.Driver(22, null).State);
        var ok = DeviceMonitorRules.Driver(0, signed);
        Assert.Equal(DriverState.Installed, ok.State);
        Assert.Contains("Microsoft 10.0.26100.1", ok.Text);
        Assert.Contains("2026-01-15", ok.Text);
        Assert.Contains("semnat de Microsoft Windows", ok.Text);
        var unsigned = DeviceMonitorRules.Driver(0, signed with { IsSigned = false, Signer = "" });
        Assert.Equal(DriverState.Unsigned, unsigned.State);
        Assert.Contains("NESEMNAT", unsigned.Text);
        Assert.Equal(DriverState.Installed, DeviceMonitorRules.Driver(0, null).State);   // works, details not read
        Assert.Equal(DriverState.Unknown, DeviceMonitorRules.Driver(-1, null).State);
    }

    // ---- journal merge and registration number ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_snapshot_records_without_connections_then_arrivals_and_removals_are_dated()
    {
        var j = new DeviceJournal();
        Assert.Empty(DeviceJournalMerge.Apply(j, [Mouse, Keyboard], NoDrivers, T0, initial: true));
        Assert.Equal(2, j.Devices.Count);
        Assert.All(j.Devices, d => { Assert.True(d.Present); Assert.Equal(T0, d.FirstSeenUtc); });

        var t1 = T0.AddMinutes(5);
        var arrived = Assert.Single(DeviceJournalMerge.Apply(j, [Mouse, Keyboard, Stick(), UsbParent()], NoDrivers, t1, initial: false), c => c.Device.Class == DeviceClass.UsbStorage);
        Assert.True(arrived.Arrived);
        Assert.Equal(t1, arrived.Device.LastConnectedUtc);
        Assert.Equal("FAKESN0001", arrived.Device.Serial);

        var t2 = T0.AddMinutes(9);
        var gone = DeviceJournalMerge.Apply(j, [Mouse, Keyboard], NoDrivers, t2, initial: false);
        Assert.Equal(2, gone.Count);
        Assert.All(gone, c => { Assert.False(c.Arrived); Assert.False(c.Device.Present); Assert.Equal(t2, c.Device.LastDisconnectedUtc); });
        Assert.Equal(4, j.Devices.Count);                                       // the rows stay after disconnection
    }

    [Fact]
    public void The_registration_number_is_kept_by_device_identity_across_disconnection_and_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "la_devmon_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var j = new DeviceJournal();
            DeviceJournalMerge.Apply(j, [Stick()], NoDrivers, T0, initial: true);
            var id = DeviceMonitorRules.Identity(Stick());
            Assert.StartsWith("SN|", id);
            Assert.Equal("", DeviceJournalMerge.SetRegistrationNumber(j, id, "  RN-0042 "));
            Assert.Null(DeviceJournalMerge.SetRegistrationNumber(j, "ID|NOT-THERE", "X"));
            DeviceJournalMerge.Apply(j, [], NoDrivers, T0.AddMinutes(1), initial: false);   // unplugged
            LogAnalyzer.Dfir.IO.Json.Write(path, j);

            var reloaded = DeviceJournalStore.Load(path, out var problem);
            Assert.Equal("", problem);
            // plugged again after a restart; the id differs only in case: the same identity, the same number
            var again = Stick() with { InstanceId = Stick().InstanceId.ToLowerInvariant() };
            var change = Assert.Single(DeviceJournalMerge.Apply(reloaded, [again], NoDrivers, T0.AddHours(1), initial: false));
            Assert.Equal("RN-0042", change.Device.RegistrationNumber);
            Assert.Single(reloaded.Devices);
            // another stick (other serial) has its own identity and no number
            DeviceJournalMerge.Apply(reloaded, [again, Stick("FAKESN0002")], NoDrivers, T0.AddHours(2), initial: false);
            Assert.Equal("", reloaded.Find(DeviceMonitorRules.Identity(Stick("FAKESN0002")))!.RegistrationNumber);
            // without a serial the identity is the instance id
            Assert.Equal("ID|" + Mouse.InstanceId.ToUpperInvariant(), DeviceMonitorRules.Identity(Mouse));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_damaged_journal_is_set_aside_and_an_empty_one_is_started()
    {
        var path = Path.Combine(Path.GetTempPath(), "la_devmon_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var j = DeviceJournalStore.Load(path, out var problem);
            Assert.Empty(j.Devices);
            Assert.Contains("deteriorat", problem);
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
        Assert.Equal(DeviceAlertSeverity.Info, S(DeviceClass.UsbStorage, Stick().InstanceId, "RN-1"));
        var forbidden = forbiddenIsCritical ? DeviceAlertSeverity.Critical : DeviceAlertSeverity.Info;
        Assert.Equal(forbidden, S(DeviceClass.Phone, Phone.InstanceId, "RN-2"));
        Assert.Equal(forbidden, S(DeviceClass.Bluetooth, @"BTHENUM\DEV_001122334455\1"));
        Assert.Equal(forbidden, S(DeviceClass.WiFi, Wifi.InstanceId));
        Assert.Equal(forbidden, S(DeviceClass.NetworkAdapter, UsbEthernet.InstanceId));
        // a built-in wired adapter, a mouse or a printer is information everywhere
        Assert.Equal(DeviceAlertSeverity.Info, S(DeviceClass.NetworkAdapter, Ethernet.InstanceId));
        Assert.Equal(DeviceAlertSeverity.Info, S(DeviceClass.Mouse, Mouse.InstanceId));
        Assert.Equal(DeviceAlertSeverity.Info, S(DeviceClass.Printer, Printer.InstanceId));
        Assert.Contains("fără număr de înregistrare", DeviceMonitorRules.Decide(DeviceClass.UsbStorage, "", "", isolated).Reason);
    }

    // ---- the view model with fakes ----------------------------------------------------------------------------------------------------------

    private sealed class Fakes
    {
        public List<PnpSnapshotEntry> Present = [];
        public List<(string Ev, string Detail)> Audited = [];
        public string? AuditRefusal;
        public int Saves;
        public DeviceJournal? Saved;
        public DateTimeOffset Now = T0;

        public DeviceMonitorViewModel Create(bool isolated, DeviceJournal? journal = null) => new(
            () => Present.ToList(),
            _ => new Dictionary<string, SignedDriverInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [Stick().InstanceId] = new("10.0.26100.1", "Microsoft", "2026-01-15", true, "Microsoft Windows"),
            },
            (ev, detail) => { if (AuditRefusal is null) Audited.Add((ev, detail)); return AuditRefusal; },
            journal ?? new DeviceJournal(),
            j => { Saves++; Saved = j.Copy(); return null; },
            isolated, () => Now);
    }

    [Fact]
    public async Task The_monitor_shows_a_connection_immediately_alerts_and_keeps_the_row_red_after_disconnection()
    {
        var f = new Fakes { Present = [Mouse] };
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
        Assert.Equal("instalat", stickRow.DriverStateText);
        Assert.Contains("semnat de Microsoft Windows", stickRow.DriverText);
        Assert.Equal(2, alerts.Count);
        Assert.All(alerts, a => Assert.Equal(DeviceAlertSeverity.Critical, a.Severity));   // stick without number; phone on an isolated station
        Assert.Equal(2, vm.JournalCopy().Alerts.Count);

        f.Present = [Mouse];
        f.Now = T0.AddMinutes(2);
        await vm.RefreshAsync(initial: false);
        stickRow = Assert.Single(vm.Rows, r => r.Class == DeviceClass.UsbStorage);
        Assert.False(stickRow.Present);                                          // red circle, row kept
        Assert.Equal("deconectat", stickRow.StatusText);
        Assert.NotEqual("", stickRow.LastDisconnectedText);
        Assert.Contains("UTC", stickRow.LastDisconnectedText);
        Assert.True(f.Saves > 0);
        Assert.Equal(3, f.Saved!.Devices.Count);
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
        var (ev, detail) = Assert.Single(f.Audited);
        Assert.Equal(OperationAudit.DeviceRegistrationChanged, ev);
        Assert.Contains("old=(gol)", detail);
        Assert.Contains("new=RN-0042", detail);
        Assert.Contains("identity=" + DeviceMonitorRules.Identity(Stick()), detail);
        Assert.Equal("RN-0042", f.Saved!.Find(row.Identity)!.RegistrationNumber);
        Assert.True(f.Saves > saves);
        Assert.False(row.NeedsRegistration);

        row.RegistrationNumber = "RN-0043";
        Assert.Contains("old=RN-0042", f.Audited[1].Detail);
        Assert.Contains("new=RN-0043", f.Audited[1].Detail);

        // the audit cannot be written: the change is refused and the old value stays
        f.AuditRefusal = "disc plin";
        row.RegistrationNumber = "RN-9999";
        Assert.Equal("RN-0043", row.RegistrationNumber);
        Assert.Equal("RN-0043", vm.JournalCopy().Find(row.Identity)!.RegistrationNumber);
        Assert.Contains("disc plin", vm.Problem);
    }

    // ---- check D05 and the report section --------------------------------------------------------------------------------------------------

    private static DeviceJournal JournalWithStick(string number)
    {
        var j = new DeviceJournal();
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver], NoDrivers, T0, initial: true);
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver, Stick()], NoDrivers, T0.AddHours(1), initial: false);
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver], NoDrivers, T0.AddHours(2), initial: false);
        if (number.Length > 0) DeviceJournalMerge.SetRegistrationNumber(j, DeviceMonitorRules.Identity(Stick()), number);
        DeviceJournalMerge.AddAlert(j, new DeviceAlertRecord(T0.AddHours(1), DeviceMonitorRules.Identity(Stick()), "Fake Stick USB Device", DeviceClass.UsbStorage,
            DeviceAlertSeverity.Critical, "mediu de stocare fără număr de înregistrare"));
        return j;
    }

    private static StationFacts Facts(StationPosture posture, DeviceJournal? journal) => new()
    {
        Host = "LAB-PC", Posture = posture, IsAdministrator = true, CollectedUtc = T0.AddDays(1), PeriodStartUtc = T0.AddDays(-30), PeriodEndUtc = T0.AddDays(1),
        DeviceMonitor = journal,
    };

    private static ControlCheck D05(StationFacts f) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "D05");

    [Fact]
    public void D05_lists_every_device_seen_and_judges_storage_without_number_and_forbidden_classes_per_posture()
    {
        var bad = D05(Facts(StationPosture.Standalone, JournalWithStick("")));
        Assert.Equal(ControlStatus.Neconform, bad.Status);
        Assert.Contains(bad.Evidence, e => e.StartsWith("ÎNCALCĂ REGULA: ") && e.Contains("Stocare USB") && e.Contains("fără număr de înregistrare") && e.Contains("deconectat la 2026-10-16 10:00:00 UTC"));
        Assert.Contains(bad.Evidence, e => e.StartsWith("ALERTĂ CRITICĂ 2026-10-16 09:00:00 UTC"));
        Assert.Contains(bad.Evidence, e => e.Contains("Mouse") && e.Contains("conectat acum"));
        Assert.Contains(bad.Evidence, e => e.Contains("fără driver (cod 28"));
        Assert.Contains("1 fără număr de înregistrare", bad.Detail);
        Assert.Contains("Monitorul vede doar cât timp aplicația este pornită", bad.Detail);
        Assert.Equal(ControlStatus.DeVerificat, D05(Facts(StationPosture.Internet, JournalWithStick(""))).Status);
        var good = D05(Facts(StationPosture.Standalone, JournalWithStick("RN-7")));
        Assert.Equal(ControlStatus.Conform, good.Status);
        Assert.Contains(good.Evidence, e => e.Contains("nr. de înregistrare RN-7"));
        Assert.Equal(ControlStatus.Nedeterminat, D05(Facts(StationPosture.Standalone, null)).Status);
        Assert.Equal(ControlStatus.DeVerificat, D05(Facts(StationPosture.Standalone, new DeviceJournal())).Status);
        // a phone seen on an isolated station breaks the rule even with a number
        var j = JournalWithStick("RN-7");
        DeviceJournalMerge.Apply(j, [Mouse, NoDriver, Phone], NoDrivers, T0.AddHours(3), initial: false);
        Assert.Equal(ControlStatus.Neconform, D05(Facts(StationPosture.IsolatedNetwork, j)).Status);
        Assert.Equal(ControlStatus.Conform, D05(Facts(StationPosture.Internet, j)).Status);
    }

    [Fact]
    public void The_report_section_lists_class_driver_state_registration_and_the_alerts()
    {
        var r = ControlEvaluator.Evaluate(Facts(StationPosture.Standalone, JournalWithStick("")));
        var lines = ControlReportPdf.DeviceSectionLines(r).Select(l => l.Text).ToList();
        Assert.Contains(lines, l => l.StartsWith("Dispozitive văzute în perioadă: 3 (2 conectate acum) · încalcă regula: 1 · alerte: 1 (1 critice)"));
        Assert.Contains(lines, l => l.StartsWith("ALERTĂ CRITICĂ") && l.Contains("Fake Stick USB Device"));
        Assert.Contains(lines, l => l.StartsWith("ÎNCALCĂ REGULA: Stocare USB") && l.Contains("SN FAKESN0001") && l.Contains("driver: ") && l.Contains(DeviceMonitorReport.NoRegistration));
        Assert.Contains(lines, l => l.StartsWith("Mouse · HID-compliant mouse") && l.Contains("conectat acum") && l.Contains("VID/PID 046D:C077"));
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
        // the register tabs stay in the code, switched off by the single flag
        Assert.Contains("<TabItem Header=\"Registrul de medii\" Visibility=\"{x:Static views:MediaRegisterUi.Visibility}\">", page);
        Assert.Contains("<TabItem Header=\"Registrul semnat\" Visibility=\"{x:Static views:MediaRegisterUi.Visibility}\">", page);
        Assert.Equal(System.Windows.Visibility.Collapsed, LogAnalyzer.UI.Views.MediaRegisterUi.Visibility);
    }
}
