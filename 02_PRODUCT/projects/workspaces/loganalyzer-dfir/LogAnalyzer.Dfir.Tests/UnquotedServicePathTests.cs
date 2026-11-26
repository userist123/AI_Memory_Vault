using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #14 E6: SVC-UNQUOTED-PATH from the services of a synthetic SYSTEM hive (ServicesParser rows, then Correlation).</summary>
public sealed class UnquotedServicePathTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-e6-" + Guid.NewGuid().ToString("N"));
    public UnquotedServicePathTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private List<Finding> Run(params (string Name, string Image, int Start)[] services)
    {
        var root = new RegfBuilder.Key("ROOT", SyntheticCorpus.HiveWritten);
        root.Sub("Select").Value("Current", RegfBuilder.Dword, 1);
        foreach (var (name, image, start) in services)
        {
            var k = root.Sub(@"ControlSet001\Services\" + name);
            k.Value("ImagePath", RegfBuilder.String, image);
            k.Value("Type", RegfBuilder.Dword, 0x10);
            k.Value("Start", RegfBuilder.Dword, start);
        }
        var path = Path.Combine(_dir, "SYSTEM");
        File.WriteAllBytes(path, RegfBuilder.Build(root, "SYSTEM"));
        var sink = new ListSink();
        new ServicesParser().Parse(new EvidenceItem { EvidenceId = "EV-S", CaseId = "C", Source = "system_hive", SourceType = "system_hive", StoredPath = path }, path, sink, default);
        return Correlation.Run(sink.Events).Where(f => f.RuleId == "SVC-UNQUOTED-PATH").ToList();
    }

    [Fact]
    public void An_unquoted_path_with_a_space_before_exe_on_an_auto_or_manual_service_is_a_medium_finding_with_exact_locator_and_quote()
    {
        var f = Run(("VendorSvc", @"C:\Program Files\Vendor App\svc.exe -run", 2), ("VendorMan", @"%ProgramFiles%\Some Co\x.exe", 3));
        Assert.Equal(2, f.Count);
        var v = Assert.Single(f, x => x.Title.EndsWith("VendorSvc"));
        Assert.Equal(Severity.Medium, v.Severity);
        Assert.Equal(SemanticType.Configuration, v.SemanticType);
        Assert.Equal("T1574.009", v.MitreTechniqueId);
        var e = Assert.Single(v.SupportingEvidence);
        Assert.Equal(@"SYSTEM\ControlSet001\Services\VendorSvc", e.Locator);
        Assert.Equal(@"ImagePath = C:\Program Files\Vendor App\svc.exe -run", e.Quote);
        Assert.Contains(@"C:\Program.exe", v.Description);
        Assert.Contains(v.Limitations, l => l.Contains("ACL"));
        Assert.Contains(f, x => x.SupportingEvidence[0].Locator.EndsWith(@"\VendorMan"));

        // the manual verification gives sc.exe qc for the service and the reg query of the key
        var steps = ManualVerification.For(e, null, @"C:\Cazuri\C1");
        Assert.Contains(steps, s => s.Action == "sc.exe qc \"VendorSvc\"");
        Assert.Contains(steps, s => s.Action == @"reg query ""HKLM\SYSTEM\ControlSet001\Services\VendorSvc""");
    }

    [Fact]
    public void Quoted_system32_driver_no_space_arguments_only_and_disabled_services_give_nothing()
    {
        var f = Run(
            ("Quoted", "\"C:\\Program Files\\Vendor App\\svc.exe\" -run", 2),
            ("Drv1", @"System32\drivers\my drv.sys", 3),
            ("Drv2", @"\SystemRoot\System32\drivers\a b.sys", 3),
            ("Drv3", @"\??\C:\Program Files\Vendor\x.sys", 3),
            ("NoSpace", @"C:\Tools\svc.exe", 2),
            ("ArgsOnly", @"C:\Windows\System32\svchost.exe -k netsvcs -p", 2),
            ("Disabled", @"C:\Program Files\Vendor App\svc.exe", 4));
        Assert.Empty(f);
    }

    [Theory]
    [InlineData(@"C:\Program Files\A B\x.exe", true)]
    [InlineData("\"C:\\Program Files\\A B\\x.exe\"", false)]
    [InlineData(@"C:\A\x.exe /c a b", false)]
    [InlineData(@"C:\Program Files\A\x.dll", false)]
    [InlineData("", false)]
    public void IsUnquotedServicePath_cases(string image, bool expected) => Assert.Equal(expected, Correlation.IsUnquotedServicePath(image));
}
