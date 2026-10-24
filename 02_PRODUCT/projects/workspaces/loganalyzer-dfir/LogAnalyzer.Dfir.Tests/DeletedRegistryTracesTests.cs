using System.Text;
using DiscUtils.Registry;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// D18: deleted device keys and values recovered with the Registry library from the free cells of synthetic offline hives (SYSTEM, SOFTWARE, NTUSER),
/// the parsers that turn them into events, and the MEDIA-TRACES-DELETED finding with its "traces may have been cleaned" signal.
/// </summary>
public sealed class DeletedRegistryTracesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_del_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private const string Stor = @"ControlSet001\Enum\USBSTOR\Disk&Ven_Acme&Prod_Stick&Rev_1.00";
    private const string Mp2 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";
    private const string VolGuid = "{11111111-2222-3333-4444-555555555555}";
    private static readonly byte[] MountedF = Encoding.Unicode.GetBytes(@"_??_USBSTOR#Disk&Ven_Acme&Prod_Stick&Rev_1.00#AAA111&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}");
    private static EvidenceItem Item(string path, string type) => new() { EvidenceId = "EV-D", CaseId = "C", Source = type, SourceType = type, StoredPath = path };

    private string NewHive(string name, Action<RegistryHive> build, Action<RegistryHive>? delete = null)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var hive = RegistryHive.Create(fs);
        build(hive);
        delete?.Invoke(hive);
        return path;
    }

    private string SystemHive(bool deleteDevice = true) => NewHive("SYSTEM", h =>
    {
        h.Root.CreateSubKey("Select").SetValue("Current", 1, RegistryValueType.Dword);
        foreach (var s in new[] { "AAA111&0", "BBB222&0" })
            h.Root.CreateSubKey($@"{Stor}\{s}").SetValue("FriendlyName", "Acme Stick USB Device", RegistryValueType.String);
        h.Root.CreateSubKey(@"ControlSet001\Enum\USB\VID_1234&PID_5678\BBB222").SetValue("Service", "USBSTOR", RegistryValueType.String);
        h.Root.CreateSubKey(@"ControlSet001\Enum\Other\Keep\KEEP1").SetValue("x", "y", RegistryValueType.String);
        h.Root.CreateSubKey("MountedDevices").SetValue(@"\DosDevices\F:", MountedF, RegistryValueType.Binary);
    }, h =>
    {
        if (!deleteDevice) return;
        h.Root.OpenSubKey(Stor)!.DeleteSubKeyTree("BBB222&0");
        h.Root.OpenSubKey(@"ControlSet001\Enum\USB\VID_1234&PID_5678")!.DeleteSubKeyTree("BBB222");
        h.Root.OpenSubKey(@"ControlSet001\Enum\Other\Keep")!.DeleteSubKeyTree("KEEP1");
        h.Root.OpenSubKey("MountedDevices")!.DeleteValue(@"\DosDevices\F:");
    });

    [Fact]
    public void Deleted_usbstor_usb_and_mounteddevices_entries_are_recovered_with_serial_and_last_write()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-5);
        var list = DeletedRegistryTraces.Read(SystemHive(), "SYSTEM");

        var key = Assert.Single(list, e => e.Area == @"Enum\USBSTOR" && e.ValueName.Length == 0);
        Assert.EndsWith(@"Disk&Ven_Acme&Prod_Stick&Rev_1.00\BBB222&0", key.KeyPath);
        Assert.Equal("BBB222", key.Serial);
        Assert.NotNull(key.LastWriteUtc);
        Assert.InRange(key.LastWriteUtc!.Value, before, DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Contains(list, e => e.Area == @"Enum\USBSTOR" && e.ValueName == "FriendlyName");

        var usb = Assert.Single(list, e => e.Area == @"Enum\USB" && e.ValueName.Length == 0);
        Assert.Equal("BBB222", usb.Serial);

        var mounted = Assert.Single(list, e => e.Area == "MountedDevices");
        Assert.Equal(@"\DosDevices\F:", mounted.ValueName);
        Assert.Equal("AAA111", mounted.Serial);
    }

    [Fact]
    public void Keys_outside_the_watched_device_areas_are_not_reported()
    {
        var list = DeletedRegistryTraces.Read(SystemHive(), "SYSTEM");
        Assert.DoesNotContain(list, e => e.KeyPath.Contains("KEEP1"));
    }

    [Fact]
    public void A_hive_with_nothing_deleted_gives_no_entries_and_the_live_device_is_not_listed()
    {
        Assert.Empty(DeletedRegistryTraces.Read(SystemHive(deleteDevice: false), "SYSTEM"));
    }

    [Fact]
    public void Deleted_windows_portable_devices_and_emdmgmt_keys_are_recovered_from_SOFTWARE()
    {
        const string wpd = @"Microsoft\Windows Portable Devices\Devices\USBSTOR#Disk&Ven_Acme&Prod_Stick&Rev_1.00#CCC333&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}";
        const string emd = @"Microsoft\Windows NT\CurrentVersion\EMDMgmt\_??_USBSTOR#Disk&Ven_Acme&Prod_Stick&Rev_1.00#CCC333&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}STICKLABEL_123456";
        var path = NewHive("SOFTWARE", h =>
        {
            h.Root.CreateSubKey(wpd).SetValue("FriendlyName", "STICKLABEL", RegistryValueType.String);
            h.Root.CreateSubKey(emd).SetValue("LastUpdate", 5, RegistryValueType.Dword);
        }, h =>
        {
            h.Root.OpenSubKey(@"Microsoft\Windows Portable Devices\Devices")!.DeleteSubKeyTree(wpd[(wpd.LastIndexOf('\\') + 1)..]);
            h.Root.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion\EMDMgmt")!.DeleteSubKeyTree(emd[(emd.LastIndexOf('\\') + 1)..]);
        });

        var list = DeletedRegistryTraces.Read(path, "SOFTWARE");
        Assert.Contains(list, e => e.Area == "Windows Portable Devices" && e.Serial == "CCC333");
        Assert.Contains(list, e => e.Area == "EMDMgmt" && e.Serial == "CCC333");
    }

    [Fact]
    public void A_deleted_MountPoints2_volume_key_is_recovered_from_NTUSER_with_its_last_write()
    {
        var path = NewHive("NTUSER.DAT", h =>
        {
            h.Root.CreateSubKey($@"{Mp2}\{VolGuid}").SetValue("x", "y", RegistryValueType.String);
            h.Root.CreateSubKey($@"{Mp2}\{{99999999-2222-3333-4444-555555555555}}");
        }, h => h.Root.OpenSubKey(Mp2)!.DeleteSubKeyTree(VolGuid));

        var list = DeletedRegistryTraces.Read(path, "NTUSER");
        var k = Assert.Single(list, e => e.Area == "MountPoints2" && e.ValueName.Length == 0);
        Assert.Equal(VolGuid, k.VolumeGuid);
        Assert.NotNull(k.LastWriteUtc);
        Assert.DoesNotContain(list, e => e.VolumeGuid.StartsWith("{99999999", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_that_is_not_a_hive_is_an_error_not_an_empty_result()
    {
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, "junk");
        File.WriteAllBytes(p, new byte[4096]);
        Assert.ThrowsAny<Exception>(() => DeletedRegistryTraces.Read(p, "SYSTEM"));
    }

    // ---- the parsers emit the recovered entries ----

    [Fact]
    public void A_hive_our_reader_accepts_but_the_library_cannot_parse_still_parses_and_reports_a_gap_instead_of_failing()
    {
        // The hand-written corpus hive has no root nk the library accepts; our own reader reads it fully.
        Directory.CreateDirectory(_dir);
        Synthetic.SyntheticCorpus.Materialize(_dir);
        var p = Path.Combine(_dir, "registry", "SOFTWARE");
        var sink = new ListSink();
        var r = new SoftwareHiveParser().Parse(Item(p, "software_hive"), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Contains(sink.Events, e => e.Source == "RunKey");
        var gap = Assert.Single(r.Gaps, g => g.Artifact == "Chei șterse din registry");
        Assert.Contains("lipsa rezultatului nu înseamnă că nimic nu a fost șters", gap.Impact);
    }

    [Fact]
    public void The_usb_parser_emits_recovered_entries_next_to_the_live_devices()
    {
        var p = SystemHive();
        var sink = new ListSink();
        var r = new UsbDevicesParser().Parse(Item(p, "system_hive"), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Contains(sink.Events, e => e.Source == "USB" && e.Fields["Serial"] == "AAA111");
        var deleted = sink.Events.Where(e => e.Source == DeletedRegistryTraces.EventSource).ToList();
        Assert.Contains(deleted, e => e.Fields["Area"] == @"Enum\USBSTOR" && e.Fields["Serial"] == "BBB222" && e.Fields["Deleted"] == "key" && e.Time.Utc is not null);
        Assert.Contains(deleted, e => e.Fields["Area"] == "MountedDevices" && e.Fields["Deleted"] == "value");
    }

    [Fact]
    public void A_wiped_usbstor_still_yields_the_recovered_entries_and_the_existing_gap()
    {
        var path = NewHive("SYSTEM", h =>
        {
            h.Root.CreateSubKey("Select").SetValue("Current", 1, RegistryValueType.Dword);
            h.Root.CreateSubKey($@"{Stor}\ZZZ999&0").SetValue("FriendlyName", "Acme Stick USB Device", RegistryValueType.String);
        }, h => h.Root.OpenSubKey(@"ControlSet001\Enum")!.DeleteSubKeyTree("USBSTOR"));
        var sink = new ListSink();
        var r = new UsbDevicesParser().Parse(Item(path, "system_hive"), path, sink, default);

        Assert.Contains(r.Gaps, g => g.Artifact == "USBSTOR");
        Assert.Contains(sink.Events, e => e.Source == DeletedRegistryTraces.EventSource && e.Fields["Serial"] == "ZZZ999");
    }

    [Fact]
    public void The_user_hive_parser_emits_deleted_MountPoints2_keys()
    {
        var path = NewHive("NTUSER.DAT", h => h.Root.CreateSubKey($@"{Mp2}\{VolGuid}"), h => h.Root.OpenSubKey(Mp2)!.DeleteSubKeyTree(VolGuid));
        var sink = new ListSink();
        new UserHiveParser().Parse(Item(path, "ntuser_hive"), path, sink, default);
        Assert.Contains(sink.Events, e => e.Source == DeletedRegistryTraces.EventSource && e.Fields["Area"] == "MountPoints2" && e.Fields["VolumeGuid"] == VolGuid);
    }

    [Fact]
    public void The_software_hive_parser_emits_deleted_EMDMgmt_keys()
    {
        const string emd = @"Microsoft\Windows NT\CurrentVersion\EMDMgmt\_??_USBSTOR#Disk&Ven_Acme&Prod_Stick&Rev_1.00#CCC333&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}STICK_1";
        var path = NewHive("SOFTWARE", h => h.Root.CreateSubKey(emd), h => h.Root.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion\EMDMgmt")!.DeleteSubKeyTree(emd[(emd.LastIndexOf('\\') + 1)..]));
        var sink = new ListSink();
        new SoftwareHiveParser().Parse(Item(path, "software_hive"), path, sink, default);
        Assert.Contains(sink.Events, e => e.Source == DeletedRegistryTraces.EventSource && e.Fields["Area"] == "EMDMgmt" && e.Fields["Serial"] == "CCC333");
    }

    // ---- the finding and the signal ----

    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);
    private static int _n;

    private static TimelineEvent Row(string source, double minute, string eventId = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute).UtcDateTime, "", "test"), Source = source, EventId = eventId, EvidenceId = "EV-R",
        Locator = $"row={Interlocked.Increment(ref _n)}", Summary = source + " " + eventId, Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Part1006(string serial, double minute, string bus = "7") => Row("EventLog:Microsoft-Windows-Partition/Diagnostic", minute, "1006",
        ("SerialNumber", serial), ("BusType", bus), ("Capacity", "32000000000"), ("Manufacturer", "Acme"), ("Model", "Stick"));

    private static TimelineEvent UsbKey(string serial, string iface = "USBSTOR") => Row("USB", 0, "", ("Serial", serial), ("VidPid", "VID_1234&PID_5678"), ("FriendlyName", "Acme Stick"), ("Interface", iface));

    private static TimelineEvent Deleted(string area, string serial, string deletedKind = "key", string hive = "SYSTEM") => Row(DeletedRegistryTraces.EventSource, -30, "",
        ("Hive", hive), ("Area", area), ("KeyPath", @"ControlSet001\Enum\USBSTOR\X\" + serial), ("ValueName", ""), ("Serial", serial), ("VolumeGuid", ""), ("Deleted", deletedKind));

    private static List<Finding> Run(params TimelineEvent[] events)
    {
        int n = 0;
        var input = new Wp14Input { Scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified } };
        return Wp14Rules.Run(events, () => $"F-{++n:D4}", input).Where(f => f.RuleId == "MEDIA-TRACES-DELETED").ToList();
    }

    [Fact]
    public void A_logged_USB_disk_without_any_key_left_raises_the_cleaned_signal()
    {
        // The SYSTEM hive was read (another device is present) but AAA111, logged by Windows, has no key.
        var f = Assert.Single(Run(Part1006("AAA111", 0), Part1006("LIVE01", 1), UsbKey("LIVE01")));
        Assert.Equal("Urme de dispozitive șterse din registry", f.Title);
        Assert.Contains(Wp14Rules.TracesMayBeCleanedSignal, f.Signals!);
        Assert.Contains("AAA111", f.Description);
        Assert.DoesNotContain("LIVE01", f.Description);
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.Contains("dezinstalare", f.Description);
        Assert.Contains("nu arată cine a șters", f.Description);
        Assert.NotEmpty(f.AlternativeExplanations);
        Assert.Empty(AntiOverclaim.Violations(f));
    }

    [Fact]
    public void A_device_left_only_in_MountedDevices_counts_as_having_no_key()
    {
        var f = Assert.Single(Run(Part1006("AAA111", 0), UsbKey("AAA111", "MountedDevices only")));
        Assert.Contains(Wp14Rules.TracesMayBeCleanedSignal, f.Signals!);
    }

    [Fact]
    public void A_device_with_its_key_present_raises_no_finding()
    {
        Assert.Empty(Run(Part1006("LIVE01", 0), UsbKey("LIVE01")));
    }

    [Fact]
    public void Without_any_SYSTEM_hive_evidence_the_registry_is_silent_and_no_signal_is_raised()
    {
        Assert.Empty(Run(Part1006("AAA111", 0)));
    }

    [Fact]
    public void An_SD_card_in_1006_is_not_judged_by_the_usb_enum_keys()
    {
        Assert.Empty(Run(Part1006("SD0001", 0, bus: "12"), UsbKey("LIVE01")));
    }

    [Fact]
    public void Recovered_entries_alone_give_an_observed_finding_without_the_signal()
    {
        var f = Assert.Single(Run(Deleted(@"Enum\USBSTOR", "QQQ777"), UsbKey("LIVE01"), Part1006("LIVE01", 0)));
        Assert.Null(f.Signals);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Contains("QQQ777", f.Description);
        Assert.Contains("1 chei", f.Description);
        Assert.Equal(Severity.Low, f.Severity);
    }

    [Fact]
    public void A_recovered_entry_for_a_device_that_other_sources_name_is_correlated()
    {
        var f = Assert.Single(Run(Deleted(@"Enum\USBSTOR", "LIVE01"), UsbKey("LIVE01"), Part1006("LIVE01", 0)));
        Assert.Equal(Classification.Correlated, f.Classification);
    }

    [Fact]
    public void Nothing_deleted_and_nothing_missing_gives_no_finding()
    {
        Assert.Empty(Run(UsbKey("LIVE01"), Part1006("LIVE01", 0)));
    }

    [Fact]
    public void The_finding_cites_the_recovered_entries_and_the_1006_events()
    {
        var f = Assert.Single(Run(Deleted(@"Enum\USBSTOR", "AAA111"), Part1006("AAA111", 0), UsbKey("LIVE01")));
        Assert.Contains(f.SupportingEvidence, r => r.Description.Contains("1006"));
        Assert.True(f.SupportingEvidence.Count >= 2);
        Assert.NotNull(f.FirstSeenUtc);
    }
}
