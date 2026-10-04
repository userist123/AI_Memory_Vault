using System.Security.Principal;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Containment;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>A [Fact] that runs only in an elevated process (it changes real firewall state, then restores it).</summary>
public sealed class AdminFactAttribute : FactAttribute
{
    public AdminFactAttribute()
    {
        using var id = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator))
            Skip = "Necesită un proces rulat ca administrator.";
    }
}

public sealed class FakeFirewall : IFirewallController
{
    public Dictionary<string, (string Path, FirewallDirection Dir)> Rules { get; } = new();
    public bool Deny { get; set; }

    public void AddProgramBlockRule(string name, string programPath, FirewallDirection direction, string description)
    {
        if (Deny) throw new UnauthorizedAccessException("no admin");
        Rules[name] = (programPath, direction);
    }

    public bool RemoveRule(string name) => Rules.Remove(name);

    public IReadOnlyList<FirewallRuleInfo> ListContainmentRules() =>
        Rules.Select(r => new FirewallRuleInfo(r.Key, r.Value.Path, r.Value.Dir, true, "")).ToList();
}

public class ContainmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "la-contain-" + Guid.NewGuid().ToString("N"));
    private static readonly string Notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase() => CaseWorkspace.Create(Path.Combine(_root, "case"),
        new CaseInfo { CaseId = "CASE-C", Name = "containment", CreatedAtUtc = DateTimeOffset.UtcNow, Timezone = "GTB Standard Time" });

    /// <summary>An unsigned "program" in a user-writable folder (Temp), with a URL and an IP in its content.</summary>
    private string UnsignedSample()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "sample_agent.exe");
        File.WriteAllText(path, "MZ fake program http://evil-c2.example.xyz/beacon 185.220.101.47 WinHttpOpen InternetOpenA");
        return path;
    }

    [Fact]
    public void Windows_binaries_are_recognised_as_signed_and_a_plain_file_as_unsigned()
    {
        var sig = Authenticode.Verify(Notepad);
        Assert.True(sig.IsSigned);
        Assert.True(sig.IsValid, sig.Status);

        var unsigned = Authenticode.Verify(UnsignedSample());
        Assert.False(unsigned.IsSigned);
    }

    [Fact]
    public void Pe_header_of_a_system_binary_is_read()
    {
        var pe = PeReader.Read(Notepad);
        Assert.Equal("", pe.Error);
        Assert.Contains(pe.Machine, new[] { "x64", "ARM64", "x86" });
        Assert.Contains(pe.ImportedDlls, d => d.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase) || d.StartsWith("api-ms-win", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(pe.Sections);
    }

    [Fact]
    public void Location_and_address_classification()
    {
        Assert.True(SuspiciousProcessDetector.IsUserWritableLocation(Path.Combine(Path.GetTempPath(), "x.exe")));
        Assert.True(SuspiciousProcessDetector.IsUserWritableLocation(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Conexant", "a.exe")));
        Assert.False(SuspiciousProcessDetector.IsUserWritableLocation(Notepad));
        Assert.True(SuspiciousProcessDetector.IsPrivateOrLocal("192.168.1.10"));
        Assert.True(SuspiciousProcessDetector.IsPrivateOrLocal("10.0.0.1"));
        Assert.True(SuspiciousProcessDetector.IsPrivateOrLocal("::1"));
        Assert.False(SuspiciousProcessDetector.IsPrivateOrLocal("185.220.101.47"));
        Assert.Equal(@"\ProgramData\x\a.exe", BlockedConnectionLog.DeviceIndependentSuffix(@"C:\ProgramData\x\a.exe"));
    }

    [Fact]
    public void Scan_reports_identity_iocs_network_apis_and_reasons_without_touching_the_file()
    {
        var path = UnsignedSample();
        var before = File.GetLastWriteTimeUtc(path);
        var report = new ProcessScanner { RunDefenderScan = false }.Scan(path);

        Assert.Equal(64, report.File!.Sha256.Length);
        Assert.False(report.File.Signature.IsSigned);
        Assert.Contains(report.Iocs, i => i.Value.Contains("evil-c2.example.xyz"));
        Assert.Contains(report.Iocs, i => i.Value == "185.220.101.47");
        Assert.Contains("WinHttpOpen", report.NetworkApiReferences);
        Assert.Contains(report.SuspicionReasons, r => r.Contains("semnătură"));
        Assert.Contains(report.SuspicionReasons, r => r.Contains("orice utilizator poate scrie"));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Contain_blocks_only_the_program_records_everything_and_release_undoes_it()
    {
        var fw = new FakeFirewall();
        var ws = NewCase();
        var svc = new ProcessContainmentService(fw, new ProcessScanner { RunDefenderScan = false }, ws, operatorName: "tester");
        var path = UnsignedSample();

        var inc = svc.Contain(path, null, "test", new ContainmentOptions(SuspendProcess: false, CopySampleIntoCase: true));

        Assert.Equal(ContainmentState.Contained, inc.State);
        Assert.Equal(2, fw.Rules.Count);
        Assert.All(fw.Rules.Values, r => Assert.Equal(path, r.Path));
        Assert.Contains(fw.Rules.Values, r => r.Dir == FirewallDirection.Outbound);
        Assert.All(fw.Rules.Keys, k => Assert.StartsWith(WindowsFirewallController.RulePrefix, k));
        Assert.Single(inc.EvidenceIds);
        Assert.Contains(inc.Findings, f => f.RuleId == "CONTAIN-SUSPICIOUS-PROGRAM" && f.Classification == Classification.Direct);
        Assert.Contains("containment.firewall.block.outbound", File.ReadAllText(ws.AppAuditLogPath));

        var loaded = Assert.Single(svc.LoadAll());
        Assert.Equal(inc.IncidentId, loaded.IncidentId);
        Assert.Equal(inc.Scan!.File!.Sha256, loaded.Scan!.File!.Sha256);
        Assert.Equal(inc.Scan.Iocs.Count, loaded.Scan.Iocs.Count);

        svc.Release(inc, "fals pozitiv");
        Assert.Empty(fw.Rules);
        Assert.Equal(ContainmentState.Released, inc.State);
        Assert.Equal(ContainmentState.Released, svc.LoadAll()[0].State);
    }

    [Fact]
    public void Without_admin_rights_containment_is_reported_as_failed_not_silently_ok()
    {
        var svc = new ProcessContainmentService(new FakeFirewall { Deny = true }, new ProcessScanner { RunDefenderScan = false }, NewCase());
        var inc = svc.Contain(UnsignedSample(), null, "test", new ContainmentOptions(false, false));
        Assert.Equal(ContainmentState.Failed, inc.State);
        Assert.All(inc.Actions.Where(a => a.Action.StartsWith("firewall")), a => Assert.False(a.Success));
    }

    [Fact]
    public void Pdf_report_is_generated()
    {
        var svc = new ProcessContainmentService(new FakeFirewall(), new ProcessScanner { RunDefenderScan = false }, NewCase());
        var inc = svc.Contain(UnsignedSample(), null, "test", new ContainmentOptions(false, false));
        var pdf = Path.Combine(_root, "incident.pdf");
        IncidentPdfReport.Write(inc, pdf, "containment", Environment.MachineName);
        var head = new byte[5];
        using (var fs = File.OpenRead(pdf)) fs.ReadExactly(head);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
        Assert.True(new FileInfo(pdf).Length > 5_000);
    }

    [Fact]
    public void Trust_is_bound_to_the_file_hash_not_to_the_path()
    {
        var store = new TrustedProgramStore(Path.Combine(_root, "trusted.json"));
        var path = UnsignedSample();
        Assert.False(store.IsTrusted(path));
        store.Trust(path, "tool", "tester");
        Assert.True(new TrustedProgramStore(Path.Combine(_root, "trusted.json")).IsTrusted(path)); // persisted
        File.AppendAllText(path, " replaced by something else");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.False(store.IsTrusted(path));
        Assert.False(new SuspectProcess(1, "x", path, [], [], HighConfidence: true, Trusted: true).EligibleForAutoContainment);
        Assert.True(new SuspectProcess(1, "x", path, [], [], HighConfidence: true).EligibleForAutoContainment);
    }

    [AdminFact]
    public void Real_firewall_rule_is_created_and_removed()
    {
        var fw = new WindowsFirewallController();
        var name = WindowsFirewallController.RulePrefix + "UNITTEST-" + Guid.NewGuid().ToString("N")[..8];
        var program = Path.Combine(_root, "does-not-run.exe");
        try
        {
            fw.AddProgramBlockRule(name, program, FirewallDirection.Outbound, "LogAnalyzer unit test");
            Assert.Contains(fw.ListContainmentRules(), r => r.Name == name && r.ProgramPath.Equals(program, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            fw.RemoveRule(name);
        }
        Assert.DoesNotContain(fw.ListContainmentRules(), r => r.Name == name);
    }
}
