using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Real-PC walkthrough round 3 and the independent review (2026-10-22): the device list opens on the external devices, connected or disconnected (owner decision 2026-10-22), one physical
/// device is one row, the risk page shows no score and no green „normal” without an analysis, the transfer register's first columns keep a readable width,
/// and the control collector reads the monitor's journal through its safe path. Neutral fixtures: FAKE serials.
/// </summary>
public sealed class UiWalkthroughRound3Tests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "LogAnalyzer.App");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("LogAnalyzer.App not found above " + AppContext.BaseDirectory);
    }

    private static string ProjectDir() => Path.GetDirectoryName(AppDir())!;

    private static PnpSnapshotEntry Stick(string serial = "FAKESN0301") =>
        new($@"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\{serial}&0", "Fake Stick USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskFake____Stick", "disk", 0);

    private static readonly PnpSnapshotEntry Mouse = new(@"HID\VID_046D&PID_C077\7&1A2B3C4D&0&0000", "HID-compliant mouse", "Mouse", "Microsoft", @"HID\VID_046D&PID_C077", "mouhid", 0);

    private sealed class Station
    {
        public List<PnpSnapshotEntry> Present = [];
        public DateTimeOffset Now = T0;

        public DeviceMonitorViewModel Create(DeviceJournal? journal = null) => new(
            () => Present.ToList(), _ => new Dictionary<string, MediumInfo>(), (_, _) => null, journal ?? new DeviceJournal(), _ => null, isolated: false, () => Now);
    }

    [Fact]
    public async Task The_list_opens_on_the_external_devices_connected_or_disconnected_and_hides_internal_nodes()
    {
        var internalNode = new PnpSnapshotEntry(@"ACPI\VEN_FAKE&DEV_0001", "Fake internal sensor", "System", "FAKE", @"ACPI\VEN_FAKE&DEV_0001", "fakesys", 0);
        var s = new Station { Present = [Mouse, Stick(), internalNode] };
        using var vm = s.Create();
        Assert.Equal(DeviceMonitorViewModel.FilterAll, vm.Filter);  // owner decision 2026-10-22: connected AND disconnected external devices
        await vm.RefreshAsync(initial: true);
        Assert.Equal(2, vm.Rows.Count);                            // the internal node never appears
        s.Present = [Mouse, internalNode];                          // the stick is removed
        s.Now = T0.AddMinutes(5);
        await vm.RefreshAsync(initial: false);
        Assert.Equal(2, vm.Rows.Count);                            // the disconnected stick stays in the default list
        Assert.Contains(vm.Rows, r => r.Class == DeviceClass.UsbStorage && !r.Present);
        Assert.Contains(vm.Rows, r => r.Class == DeviceClass.Mouse && r.Present);
        Assert.DoesNotContain(vm.Rows, r => r.Name.Contains("internal sensor"));
        vm.Filter = DeviceMonitorViewModel.FilterConnected;         // the filter list is kept
        var row = Assert.Single(vm.Rows);
        Assert.Equal(DeviceClass.Mouse, row.Class);
    }

    [Fact]
    public async Task A_registered_stick_with_a_placeholder_serial_replaced_by_another_is_unregistered_with_a_sealed_critical_alert()
    {
        var sealedRecords = new List<(string Event, string Detail)>();
        var stick = new PnpSnapshotEntry(@"USBSTOR\Disk&Ven_Generic&Prod_Flash&Rev_1.00\000000000000&0", "Generic Flash USB Device", "DiskDrive", "(Standard disk drives)", @"USBSTOR\DiskGeneric_Flash", "disk", 0, "{CCCCCCCC-0000-0000-0000-00000000B001}");
        var medium = new MediumInfo("AAAA0001", 16_000_000_000, "E:", "x");
        var present = new List<PnpSnapshotEntry> { stick };
        var now = T0;
        using var vm = new DeviceMonitorViewModel(() => present.ToList(), _ => new Dictionary<string, MediumInfo> { [stick.InstanceId] = medium }, (ev, detail) => { sealedRecords.Add((ev, detail)); return null; },
            new DeviceJournal(), _ => null, isolated: false, () => now);
        await vm.RefreshAsync(initial: true);
        var row = Assert.Single(vm.Rows);
        row.RegistrationNumber = "NR-7/2026";
        Assert.False(row.HasNoNumber);
        present.Clear(); now = T0.AddMinutes(5);
        await vm.RefreshAsync(initial: false);
        present.Add(stick); medium = new MediumInfo("BBBB0002", 64_000_000_000, "E:", "y"); now = T0.AddMinutes(10);       // another stick, same model and placeholder serial
        await vm.RefreshAsync(initial: false);
        Assert.True(row.HasNoNumber, string.Join(" // ", sealedRecords.Select(r => r.Event + ":" + r.Detail)));                              // shown, counted and filtered as unregistered
        Assert.True(row.NeedsRegistration);
        Assert.Equal(RegistryState.NotRegistered, row.Registry);
        Assert.True(sealedRecords.Any(r => r.Event == "device.alert_critical" && r.Detail.Contains("nu corespunde mediului înregistrat")), string.Join(" // ", sealedRecords.Where(r => r.Event.Contains("alert")).Select(r => r.Event + ":" + r.Detail)));
    }

    [Fact]
    public async Task A_known_row_whose_kind_changes_with_the_classifier_seals_one_device_reclassified_note()
    {
        var sealedRecords = new List<(string Event, string Detail)>();
        const string box = "{CCCCCCCC-0000-0000-0000-00000000A003}";
        PnpSnapshotEntry[] razer =
        [
            new(@"USB\VID_1532&PID_0042\FAKERZ0002", "USB Composite Device", "USB", "(Standard USB Host Controller)", @"USB\VID_1532&PID_0042", "usbccgp", 0, box),
            new(@"HID\VID_1532&PID_0042&MI_00\8&AAA&0&0000", "HID-compliant mouse", "Mouse", "Microsoft", @"HID\VID_1532&PID_0042&MI_00", "mouhid", 0, box),
            new(@"HID\VID_1532&PID_0042&MI_01\8&BBB&0&0000", "HID Keyboard Device", "Keyboard", "(Standard keyboards)", @"HID\VID_1532&PID_0042&MI_01", "kbdhid", 0, box),
        ];
        var identity = DeviceMonitorRules.Identity(razer[0]);
        var journal = new DeviceJournal
        {
            Devices = [new DeviceJournalEntry { Identity = identity, InstanceId = razer[0].InstanceId, Class = DeviceClass.Keyboard, Name = "HID-compliant mouse", FirstSeenUtc = T0.AddDays(-3), ClassifierVersion = 0 }],
        };
        // the sealed audit (never the journal) holds the old kind and the old classifier version
        var first = new SealedDevice(LogAnalyzer.Dfir.Auth.OperationAudit.DeviceField(identity), "Keyboard", "", T0.AddDays(-3), T0.AddDays(-3), "HID-compliant mouse", 1);
        var audit = new DeviceAuditReading(true, new Dictionary<string, string>(), [], 0) { Devices = [first] };
        using var vm = new DeviceMonitorViewModel(() => razer, _ => new Dictionary<string, MediumInfo>(), (ev, detail) => { sealedRecords.Add((ev, detail)); return null; }, journal, _ => null, isolated: false, () => T0,
            auditReader: () => audit);
        await vm.RefreshAsync(initial: true);
        var note = Assert.Single(sealedRecords, r => r.Event == "device.reclassified");
        Assert.Contains("class_old=Keyboard", note.Detail);
        Assert.Contains("class_new=Mouse", note.Detail);
        Assert.Contains("classifier_old=1; classifier_new=" + DeviceScope.ClassifierVersion, note.Detail);
        Assert.Contains("operator=application", note.Detail);
        await vm.RefreshAsync(initial: false);
        Assert.Single(sealedRecords, r => r.Event == "device.reclassified");     // once
    }

    [Fact]
    public void Rows_of_one_device_left_by_an_older_version_are_one_row_with_the_earliest_first_sight()
    {
        DeviceJournalEntry Row(string identity, DateTimeOffset first) => new()
        {
            Identity = identity, InstanceId = @"USBSTOR\Disk&Ven_Fake&Prod_Stick&Rev_1.00\FAKESN0302&0", Class = DeviceClass.UsbStorage, Name = "Fake Stick USB Device",
            Manufacturer = "FAKE", VidPid = "Fake Stick", Serial = "FAKESN0302", CapacityBytes = 16_000_000_000, FirstSeenUtc = first, LastConnectedUtc = first,
            LastDisconnectedUtc = first.AddHours(1), DuplicateSerial = true,
        };
        var journal = new DeviceJournal { Devices = [Row("SN|FAKE STICK|FAKESN0302|DUP|1", T0.AddDays(-1)), Row("SN|FAKE STICK|FAKESN0302", T0.AddDays(-3))] };
        DeviceJournalMerge.RecomputeDuplicateSerials(journal);
        DeviceJournalMerge.MarkSuperseded(journal);                // what every reading of the journal does
        using var vm = new Station().Create(journal);
        vm.Filter = DeviceMonitorViewModel.FilterAll;
        var row = Assert.Single(vm.Rows);
        Assert.Equal("SN|FAKE STICK|FAKESN0302", row.Identity);
        Assert.Equal(T0.AddDays(-3), row.FirstSeenUtc);
        Assert.Equal(T0.AddDays(-1), row.LastConnectedUtc);        // the latest connection of the two rows
        Assert.Equal("", row.AlertText);                           // the old clone mark of the stick's own nodes is gone
    }

    [Fact]
    public void The_monitor_page_and_the_guide_say_that_the_list_shows_connected_and_disconnected_external_devices_by_default()
    {
        var page = File.ReadAllText(Path.Combine(AppDir(), "Views", "MediaCenterView.xaml"));
        Assert.Contains("Lista arată implicit dispozitivele care au fost conectate la stație ca medii de stocare sau dispozitive externe, conectate sau deconectate", page);
        var guide = File.ReadAllText(Path.Combine(ProjectDir(), "docs", "utilizare", "GHID_DE_UTILIZARE.md"));
        Assert.Contains("**Lista arată implicit dispozitivele conectate sau deconectate care au fost conectate la stație ca medii de stocare sau dispozitive externe**", guide);
        Assert.Contains("un mouse cu colecții de tastatură pentru macro-uri este „Mouse extern”", guide);
    }

    [Fact]
    public void Without_an_analysis_of_this_session_the_risk_page_shows_no_score_and_no_green_normal()
    {
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        Assert.Contains("bool evidence = eventsForAnalysis.Count > 0 && announce;", vm);
        Assert.Contains("AiRiskScore = evidence ? explainableRisk.TotalScore : 0;", vm);
        Assert.Contains("AiRiskColor = evidence ? explainableRisk.LevelColor : NeutralRiskColor;", vm);
        Assert.Contains("AiExecutiveSummary = evidence ? explainableRisk.ExecutiveSummaryRo", vm);
        Assert.Contains("foreach (var factor in explainableRisk.Factors.Where(_ => evidence))", vm);
        Assert.Equal("Nu există date analizate.", MainViewModel.NoDataSummary);
        Assert.Equal("#94a3b8", MainViewModel.NeutralRiskColor);
        // the green „normal” summary of the engine is never assigned unconditionally
        Assert.DoesNotMatch(@"AiExecutiveSummary = explainableRisk\.ExecutiveSummaryRo;", vm);
        Assert.DoesNotMatch(@"AiRiskColor = explainableRisk\.LevelColor;", vm);
    }

    [Fact]
    public void Every_transfer_register_column_has_a_minimum_width_and_the_grid_scrolls_sideways()
    {
        var page = File.ReadAllText(Path.Combine(AppDir(), "Views", "TransferRegisterView.xaml"));
        var start = page.IndexOf("<DataGrid ItemsSource=\"{Binding Transfers}\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var grid = page[start..page.IndexOf("</DataGrid.Columns>", start, StringComparison.Ordinal)];
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", grid[..grid.IndexOf('>')]);
        var columns = Regex.Matches(grid, "<DataGrid(?:Text|Template)Column Header=\"([^\"]*)\"[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(9, columns.Count);
        foreach (var c in columns)
        {
            var header = Regex.Match(c, "Header=\"([^\"]*)\"").Groups[1].Value;
            var min = Regex.Match(c, "MinWidth=\"(\\d+)\"");
            Assert.True(min.Success, $"column „{header}” has no MinWidth (it collapsed to two characters on the station)");
            Assert.True(int.Parse(min.Groups[1].Value) >= Math.Max(header.Length * 7 + 20, 60), $"column „{header}” MinWidth {min.Groups[1].Value} is too small");
        }
        // „Data” shows a date and a time („2026-10-22 14:33”)
        Assert.Matches("Header=\"Data\"[^>]*MinWidth=\"1[4-9]\\d\"", grid);
    }

    [Fact]
    public void The_control_collector_reads_the_monitor_journal_through_its_safe_path()
    {
        var src = File.ReadAllText(Path.Combine(ProjectDir(), "LogAnalyzer.Dfir.Windows", "Audit", "StationFacts.cs"));
        var tryAt = src.IndexOf("Try(facts, \"Jurnalul monitorului de dispozitive\", () =>", StringComparison.Ordinal);
        var loadAt = src.IndexOf("DeviceJournalStore.Load(journalPath", StringComparison.Ordinal);
        Assert.True(tryAt >= 0, "the journal is not read through Try");
        Assert.True(loadAt > tryAt && loadAt - tryAt < 600, "the Load call is not inside the Try block");
        Assert.Single(Regex.Matches(src, @"DeviceJournalStore\.Load\("));
    }
}
