using DiscUtils.Registry;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Services from the SYSTEM hive: synthetic exact values and the real hive against WMI Win32_Service (services.csv).</summary>
public sealed class ServicesParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_svc_" + Guid.NewGuid().ToString("N"));

    public ServicesParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-S", CaseId = "C", Source = "system_hive", SourceType = "system_hive", StoredPath = path };

    private string Hive()
    {
        var path = Path.Combine(_dir, "SYSTEM");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var hive = RegistryHive.Create(fs);
        hive.Root.CreateSubKey("Select").SetValue("Current", 2, RegistryValueType.Dword);
        hive.Root.CreateSubKey(@"ControlSet001\Services\Stale").SetValue("ImagePath", @"C:\old.exe", RegistryValueType.String);
        var svc = hive.Root.CreateSubKey(@"ControlSet002\Services");
        var evil = svc.CreateSubKey("CxUtilSvc");
        evil.SetValue("ImagePath", "\"C:\\ProgramData\\Conexant\\CxUtilSvc.exe\" -service", RegistryValueType.String);
        evil.SetValue("Type", 0x10, RegistryValueType.Dword);
        evil.SetValue("Start", 2, RegistryValueType.Dword);
        evil.SetValue("ObjectName", "LocalSystem", RegistryValueType.String);
        evil.SetValue("DisplayName", "CxUtilSvc Helper", RegistryValueType.String);
        var dll = svc.CreateSubKey("NetHelper");
        dll.SetValue("ImagePath", @"%SystemRoot%\system32\svchost.exe -k netsvcs -p", RegistryValueType.ExpandString);
        dll.SetValue("Type", 0x20, RegistryValueType.Dword);
        dll.SetValue("Start", 3, RegistryValueType.Dword);
        dll.CreateSubKey("Parameters").SetValue("ServiceDll", @"C:\Users\Public\nethelp.dll", RegistryValueType.ExpandString);
        var drv = svc.CreateSubKey("kdrv");
        drv.SetValue("ImagePath", @"\??\C:\Windows\Temp\kdrv.sys", RegistryValueType.ExpandString);
        drv.SetValue("Type", 1, RegistryValueType.Dword);
        drv.SetValue("Start", 1, RegistryValueType.Dword);
        var ok = svc.CreateSubKey("Spooler");
        ok.SetValue("ImagePath", @"%SystemRoot%\System32\spoolsv.exe", RegistryValueType.ExpandString);
        ok.SetValue("Type", 0x110, RegistryValueType.Dword);
        ok.SetValue("Start", 2, RegistryValueType.Dword);
        ok.SetValue("ObjectName", "LocalSystem", RegistryValueType.String);
        svc.CreateSubKey("NoImage").SetValue("Type", 0x10, RegistryValueType.Dword);
        return path;
    }

    [Fact]
    public void Services_of_the_current_control_set_are_read_with_their_configuration()
    {
        var p = Hive();
        var sink = new ListSink();
        var r = new ServicesParser().Parse(Item(p), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.DoesNotContain(sink.Events, e => e.Service == "Stale");           // ControlSet001 is not current
        var cx = Assert.Single(sink.Events, e => e.Service == "CxUtilSvc");
        Assert.Equal(@"C:\ProgramData\Conexant\CxUtilSvc.exe", cx.Path);
        Assert.Equal("Auto", cx.Fields["StartMode"]);
        Assert.Equal("Own Process", cx.Fields["ServiceType"]);
        Assert.Equal("LocalSystem", cx.User);
        Assert.Equal(@"SYSTEM\ControlSet002\Services\CxUtilSvc", cx.Locator);
        Assert.Contains("key last written", cx.TimeSemantics);
        var net = Assert.Single(sink.Events, e => e.Service == "NetHelper");
        Assert.Equal(@"%SystemRoot%\system32\svchost.exe -k netsvcs -p", net.Fields["ImagePath"]);   // not expanded
        Assert.Equal(@"C:\Users\Public\nethelp.dll", net.Fields["ServiceDll"]);
        Assert.Equal("Kernel Driver", Assert.Single(sink.Events, e => e.Service == "kdrv").Fields["ServiceType"]);
        Assert.Equal("System", Assert.Single(sink.Events, e => e.Service == "kdrv").Fields["StartMode"]);
        Assert.Equal("", Assert.Single(sink.Events, e => e.Service == "NoImage").Path);
    }

    [Fact]
    public void Services_and_drivers_from_user_writable_locations_are_findings()
    {
        var p = Hive();
        var sink = new ListSink();
        new ServicesParser().Parse(Item(p), p, sink, default);
        var f = Correlation.Run(sink.Events).Where(x => x.RuleId == "PERSIST-SERVICE-CONFIG").ToList();

        Assert.Equal(3, f.Count);
        Assert.Contains(f, x => x.File == @"C:\ProgramData\Conexant\CxUtilSvc.exe" && x.Severity == Severity.High && x.MitreTechniqueId == "T1543.003");
        Assert.Contains(f, x => x.File == @"C:\Users\Public\nethelp.dll");
        Assert.Contains(f, x => x.File == @"\??\C:\Windows\Temp\kdrv.sys");
        Assert.DoesNotContain(f, x => x.File.Contains("spoolsv"));
    }

    [Theory]
    [InlineData(@"%SystemRoot%\system32\svchost.exe -k x", @"C:\WINDOWS\system32\svchost.exe -k x")]
    [InlineData(@"\SystemRoot\System32\drivers\a.sys", @"C:\WINDOWS\System32\drivers\a.sys")]
    [InlineData(@"System32\drivers\a.sys", @"C:\WINDOWS\System32\drivers\a.sys")]
    [InlineData(@"\??\C:\x\a.sys", @"C:\x\a.sys")]
    [InlineData("\"C:\\Program Files\\a.exe\"", "\"C:\\Program Files\\a.exe\"")]
    [InlineData("\"%systemroot%\\system32\\wbengine.exe\"", "\"C:\\WINDOWS\\system32\\wbengine.exe\"")]
    [InlineData(@"%ProgramData%\Microsoft\x.exe", @"C:\ProgramData\Microsoft\x.exe")]
    public void Image_path_is_normalised_like_the_service_control_manager_shows_it(string image, string expected)
        => Assert.Equal(expected, ServicesParser.AsScmPath(image, @"C:\WINDOWS"));

    /// <summary>services.csv = WMI Win32_Service at 23:50:41; SYSTEM.hiv saved at 23:50:56 on the same station.</summary>
    [CorpusFact("servicesHive")]
    public void Real_services_match_wmi()
    {
        var hive = Corpus.File("servicesHive");
        var sink = new ListSink();
        Assert.Equal(EvidenceStatus.Success, new ServicesParser().Parse(Item(hive), hive, sink, default).Status);
        var byName = sink.Events.GroupBy(e => e.Service, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = CsvReader.ReadDicts(Path.Combine(Corpus.Root, Corpus.S("servicesHive", "servicesCsv"))).ToList();
        Assert.True(rows.Count > 300, $"{rows.Count} servicii în services.csv");
        var mismatch = new List<string>();
        int wmiUnknown = 0;
        foreach (var row in rows)
        {
            if (!byName.TryGetValue(row["Name"], out var e)) { mismatch.Add($"{row["Name"]}: lipsește din hive"); continue; }
            // WMI reports "Unknown" when it cannot read a protected service's configuration; the hive has the stored value.
            if (row["StartMode"] == "Unknown") { wmiUnknown++; Assert.NotEqual("", e.Fields["StartMode"]); }
            else if (!row["StartMode"].Equals(e.Fields["StartMode"], StringComparison.OrdinalIgnoreCase))
                mismatch.Add($"{row["Name"]}: StartMode WMI {row["StartMode"]} vs hive {e.Fields["StartMode"]}");
            if (row["StartName"].Length > 0 && !row["StartName"].Equals(e.User, StringComparison.OrdinalIgnoreCase))
                mismatch.Add($"{row["Name"]}: StartName WMI {row["StartName"]} vs hive {e.User}");
            if (row["PathName"].Length > 0 && !row["PathName"].Equals(ServicesParser.AsScmPath(e.Fields["ImagePath"], @"C:\WINDOWS"), StringComparison.OrdinalIgnoreCase))
                mismatch.Add($"{row["Name"]}: PathName WMI {row["PathName"]} vs hive {e.Fields["ImagePath"]}");
        }
        Assert.True(mismatch.Count == 0, $"{mismatch.Count} diferențe:{Environment.NewLine}{string.Join(Environment.NewLine, mismatch.Take(25))}");
        Assert.True(wmiUnknown < 10, $"{wmiUnknown} servicii cu StartMode Unknown în WMI");
    }
}
