using System.Text;
using DiscUtils.Registry;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>USB storage from the SYSTEM hive: synthetic exact values and the real hive against Partition/Diagnostic 1006.</summary>
public sealed class UsbDevicesParserTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_usb_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime First = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Arrival = new(2026, 9, 19, 16, 2, 3, DateTimeKind.Utc);

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-U", CaseId = "C", Source = "system_hive", SourceType = "system_hive", StoredPath = path };

    private string Hive()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "SYSTEM");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var hive = RegistryHive.Create(fs);
        hive.Root.CreateSubKey("Select").SetValue("Current", 1, RegistryValueType.Dword);
        var inst = hive.Root.CreateSubKey(@"ControlSet001\Enum\USBSTOR\Disk&Ven_Kingston&Prod_DataTraveler_3.0&Rev_\E0D55EA574B119C128820337&0");
        inst.SetValue("FriendlyName", "Kingston DataTraveler 3.0 USB Device", RegistryValueType.String);
        var props = @"ControlSet001\Enum\USBSTOR\Disk&Ven_Kingston&Prod_DataTraveler_3.0&Rev_\E0D55EA574B119C128820337&0\Properties\{83da6326-97a6-4088-9453-a1923f573b29}";
        hive.Root.CreateSubKey(props + @"\0064").SetValue("", BitConverter.GetBytes(First.ToFileTimeUtc()), RegistryValueType.Binary);
        hive.Root.CreateSubKey(props + @"\0066").SetValue("", BitConverter.GetBytes(Arrival.ToFileTimeUtc()), RegistryValueType.Binary);
        hive.Root.CreateSubKey(@"ControlSet001\Enum\USB\VID_0951&PID_1666\E0D55EA574B119C128820337");
        hive.Root.CreateSubKey("MountedDevices").SetValue(@"\DosDevices\E:",
            Encoding.Unicode.GetBytes(@"_??_USBSTOR#Disk&Ven_Kingston&Prod_DataTraveler_3.0&Rev_#E0D55EA574B119C128820337&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}"), RegistryValueType.Binary);
        return path;
    }

    [Fact]
    public void Device_identity_times_vidpid_and_drive_letter_are_decoded()
    {
        var p = Hive();
        var sink = new ListSink();
        var r = new UsbDevicesParser().Parse(Item(p), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Equal(2, sink.Events.Count);                                       // 0064 and 0066; no 0067 recorded
        var arrival = Assert.Single(sink.Events, e => e.TimeSemantics.Contains("arrival"));
        Assert.Equal(new DateTimeOffset(Arrival), arrival.Time.Utc);
        Assert.Equal("Kingston", arrival.Fields["Vendor"]);
        Assert.Equal("DataTraveler 3.0", arrival.Fields["Product"]);
        Assert.Equal("E0D55EA574B119C128820337", arrival.Fields["Serial"]);
        Assert.Equal("VID_0951&PID_1666", arrival.Fields["VidPid"]);
        Assert.Equal("E:", arrival.Fields["DriveLetter"]);
        Assert.Equal(new DateTimeOffset(First), Assert.Single(sink.Events, e => e.TimeSemantics.Contains("first install")).Time.Utc);
    }

    [Fact]
    public void Missing_usbstor_is_reported_not_silently_empty()
    {
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, "SYSTEM");
        using (var fs = new FileStream(p, FileMode.Create, FileAccess.ReadWrite))
        using (var hive = RegistryHive.Create(fs))
            hive.Root.CreateSubKey("Select").SetValue("Current", 1, RegistryValueType.Dword);
        var r = new UsbDevicesParser().Parse(Item(p), p, new ListSink(), default);
        Assert.Equal(EvidenceStatus.Empty, r.Status);
        Assert.Contains(r.Gaps, g => g.Artifact == "USBSTOR");
    }

    /// <summary>
    /// Every USB disk the Partition/Diagnostic log saw (1006, ParentId USB\VID_…\serial) must be in the hive's USBSTOR with
    /// the same manufacturer and model; the hive's last arrival must not be earlier than the last 1006 for that serial
    /// beyond the tolerance printed below.
    /// </summary>
    [CorpusFact("usbHive")]
    public void Real_usb_devices_match_partition_diagnostic()
    {
        var hive = Corpus.File("usbHive");
        var sink = new ListSink();
        Assert.Equal(EvidenceStatus.Success, new UsbDevicesParser().Parse(Item(hive), hive, sink, default).Status);
        var devices = sink.Events.GroupBy(e => e.Fields["Serial"], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var log = Path.Combine(Corpus.Root, Corpus.S("usbHive", "partitionLog"));
        var logSink = new ListSink();
        new EvtxParser().Parse(new EvidenceItem { EvidenceId = "EV-P", CaseId = "C", Source = "p", SourceType = "evtx", StoredPath = log }, log, logSink, default);
        var seen = logSink.Events.Where(e => e.EventId == "1006" && e.Fields.GetValueOrDefault("ParentId", "").StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Fields["ParentId"][(e.Fields["ParentId"].LastIndexOf('\\') + 1)..], StringComparer.OrdinalIgnoreCase).ToList();
        Assert.NotEmpty(seen);

        var problems = new List<string>();
        var logStart = logSink.Events.Where(e => e.Time.Utc is not null).Min(e => e.Time.Utc!.Value);
        int matchedArrival = 0, outsideRetention = 0, mountedOnly = 0;
        foreach (var g in seen)
        {
            var lastLog = g.Max(e => e.Time.Utc!.Value);
            var any = g.First();
            if (!devices.TryGetValue(g.Key, out var dev)) { problems.Add($"{g.Key} ({any.Fields.GetValueOrDefault("Model")}): în jurnal, lipsește din USBSTOR"); continue; }
            var d = dev[0].Fields;
            if (!d["Vendor"].Equals(any.Fields.GetValueOrDefault("Manufacturer", "").Trim(), StringComparison.OrdinalIgnoreCase)
                || !d["Product"].Equals(any.Fields.GetValueOrDefault("Model", "").Trim(), StringComparison.OrdinalIgnoreCase))
                problems.Add($"{g.Key}: hive {d["Vendor"]}/{d["Product"]} vs jurnal {any.Fields.GetValueOrDefault("Manufacturer")}/{any.Fields.GetValueOrDefault("Model")}");
            var arrival = dev.FirstOrDefault(e => e.TimeSemantics.Contains("arrival"))?.Time.Utc;
            output.WriteLine($"{g.Key} {d["Vendor"]} {d["Product"]} [{d["Interface"]}]: ultima conectare hive {arrival:o}, 1006: {g.Count()} evenimente {g.Min(e => e.Time.Utc):o} – {lastLog:o}");
            // 1006 is also written on other disk refreshes, so 0066 need not be the last 1006; it must be one of them (±2 min),
            // unless it is older than the log's first event for any device (outside retention), or the device is known only
            // from MountedDevices (no time at all).
            if (d["Interface"] == "MountedDevices only") { mountedOnly++; continue; }
            if (arrival is null) { problems.Add($"{g.Key}: fără 0066 în hive"); continue; }
            if (arrival < logStart) { outsideRetention++; continue; }
            if (!g.Any(e => (e.Time.Utc!.Value - arrival.Value).Duration() <= TimeSpan.FromMinutes(2)))
                problems.Add($"{g.Key}: conectarea din hive {arrival:o} nu corespunde niciunui 1006 al dispozitivului");
            else matchedArrival++;
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        output.WriteLine($"ore 0066 potrivite cu 1006: {matchedArrival}; în afara retenției: {outsideRetention}; doar din MountedDevices: {mountedOnly}");
        Assert.True(matchedArrival >= 2, $"{matchedArrival} ore de conectare confirmate de jurnal");
    }
}
