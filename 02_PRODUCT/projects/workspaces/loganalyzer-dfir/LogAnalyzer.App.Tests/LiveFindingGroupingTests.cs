using System.IO;
using System.Net;
using System.Text.Json;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #12 B2: repeated findings are grouped (one per executable) and the live / stored duplicates of one task, Run value or Startup entry are merged. Fake data only.</summary>
public sealed class LiveFindingGroupingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-b2-" + Guid.NewGuid().ToString("N"));
    public LiveFindingGroupingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const string Py = @"C:\Users\ana\AppData\Local\Programs\Python\python.exe";
    private const string Mcp = @"C:\Users\ana\AppData\Roaming\mcp\server.exe";

    private static LiveStateCollector.ProcessRow Proc(int pid, string path) =>
        new(pid, 4, Path.GetFileName(path), path, $"\"{path}\" --worker {pid}", "NESEMNAT", true);

    private List<Finding> Analyze(List<LiveStateCollector.ProcessRow> procs, List<TcpConnection>? conns = null,
        List<LiveStateCollector.TaskRow>? tasks = null, List<LiveStateCollector.AutorunRow>? runs = null)
    {
        var snap = new LiveStateCollector.Snapshot(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), "PC-TEST", procs, conns ?? [], [], tasks ?? [], runs ?? [], []);
        var path = Path.Combine(_dir, "live.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snap));
        var item = new EvidenceItem { EvidenceId = "E-0007", CaseId = "C", Source = "live system", SourceType = "live_snapshot" };
        return LiveStateAnalyzer.Analyze(item, path, 0).ToList();
    }

    [Fact]
    public void Six_processes_of_the_same_unsigned_exe_are_one_finding_with_six_refs_and_the_count_in_the_title()
    {
        var fs = Analyze(Enumerable.Range(100, 6).Select(i => Proc(i, Py)).ToList());
        var f = Assert.Single(fs);
        Assert.Equal("LIVE-UNSIGNED-USERPATH", f.RuleId);
        Assert.Equal(6, f.SupportingEvidence.Count);
        Assert.EndsWith("python.exe (6 procese)", f.Title);
        Assert.Equal(6, f.SupportingEvidence.Select(e => e.Locator).Distinct().Count());
        Assert.Empty(f.Limitations);
    }

    [Fact]
    public void More_than_ten_processes_cap_the_refs_and_say_so()
    {
        var f = Assert.Single(Analyze(Enumerable.Range(100, 13).Select(i => Proc(i, Py)).ToList()));
        Assert.Equal(LiveStateAnalyzer.MaxProcessRefs, f.SupportingEvidence.Count);
        Assert.Contains("(13 procese)", f.Title);
        Assert.Contains("și încă 3 procese", f.Description);
        Assert.Contains(f.Limitations, l => l.Contains("încă 3 procese"));
    }

    [Fact]
    public void Net_and_plain_processes_of_the_same_exe_give_only_the_net_finding()
    {
        var conns = new List<TcpConnection> { new(102, new IPEndPoint(IPAddress.Parse("192.168.1.5"), 50000), new IPEndPoint(IPAddress.Parse("8.8.8.8"), 443), "ESTABLISHED") };
        var f = Assert.Single(Analyze([Proc(100, Py), Proc(101, Py), Proc(102, Py)], conns));
        Assert.Equal("LIVE-UNSIGNED-USERPATH-NET", f.RuleId);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(3, f.SupportingEvidence.Count);
        Assert.Contains("8.8.8.8:443", f.Ip);
    }

    [Fact]
    public void Two_different_exes_are_two_findings_and_ids_are_sequential()
    {
        var fs = Analyze([Proc(1, Py), Proc(2, Mcp), Proc(3, Py)]);
        Assert.Equal(2, fs.Count);
        Assert.Equal(["F-0001", "F-0002"], fs.Select(f => f.FindingId));
        Assert.DoesNotContain("procese", fs.Single(f => f.Process == "server.exe").Title);
    }

    // ---- merge of live and stored findings ----

    private static EvidenceRef Ref(string id, string locator) => new(id, locator, "test");

    private static Finding Live(string id, string rule, string title, string file, string locator) =>
        new() { FindingId = id, RuleId = rule, Title = title, File = file, Description = "live", SupportingEvidence = [Ref("E-LIVE", locator)] };

    private static Finding Stored(string id, string rule, string title, string file, string description) =>
        new() { FindingId = id, RuleId = rule, Title = title, File = file, Description = description, SupportingEvidence = [Ref("E-XML", "task xml")] };

    private const string RunPrefix = "Pornire automată dintr-o locație în care utilizatorii pot scrie: ";
    private const string Exe = @"C:\Users\ana\AppData\Local\App\app.exe";

    [Fact]
    public void Task_in_live_snapshot_and_task_xml_is_one_finding_keeping_both_evidence_refs()
    {
        var live = Live("F-0200", "LIVE-TASK-USERPATH", "x: Update", $"\"{Exe}\" /silent", @"Tasks[\Lenovo\Vantage\Schedule\Update]");
        var stored = Stored("F-0010", "PERSIST-TASK-CONFIG", "y: Update", Exe, $@"Lenovo\Vantage\Schedule\Update rulează {Exe} /silent ca ana");
        var r = LiveFindingMerger.Merge([stored, live]);
        var f = Assert.Single(r.Findings);
        Assert.Equal(1, r.Merged);
        Assert.Equal("PERSIST-TASK-CONFIG", f.RuleId);
        Assert.Equal("F-0010", f.FindingId);
        Assert.Equal(["E-XML", "E-LIVE"], f.SupportingEvidence.Select(e => e.EvidenceId));
        Assert.Contains(f.Limitations, l => l.Contains("LIVE-TASK-USERPATH") && l.Contains("F-0200"));
    }

    [Fact]
    public void Task_named_differently_is_merged_by_unique_leaf_and_same_program_only()
    {
        var live = Live("F-0200", "LIVE-TASK-USERPATH", "x", Exe, @"Tasks[\Vendor\Sub\Update]");
        var stored = Stored("F-0010", "PERSIST-TASK-CONFIG", "y", Exe, $"Update rulează {Exe} ca ana");
        Assert.Single(LiveFindingMerger.Merge([stored, live]).Findings);
        var otherExe = Stored("F-0011", "PERSIST-TASK-CONFIG", "y", @"C:\Users\ana\AppData\Local\Other\o.exe", @"Update rulează C:\Users\ana\AppData\Local\Other\o.exe ca ana");
        Assert.Equal(2, LiveFindingMerger.Merge([otherExe, live]).Findings.Count);
    }

    [Fact]
    public void Ambiguous_task_names_keep_both_findings()
    {
        var live = Live("F-0200", "LIVE-TASK-USERPATH", "x", Exe, @"Tasks[\Update]");
        var s1 = Stored("F-0010", "PERSIST-TASK-CONFIG", "y", Exe, $@"A\Update rulează {Exe} ca ana");
        var s2 = Stored("F-0011", "PERSIST-TASK-CONFIG", "y", Exe, $@"B\Update rulează {Exe} ca ana");
        var r = LiveFindingMerger.Merge([s1, s2, live]);
        Assert.Equal(3, r.Findings.Count);
        Assert.Equal(0, r.Merged);
    }

    [Fact]
    public void Run_value_in_live_autoruns_and_hive_is_one_finding()
    {
        var live = Live("F-0300", "LIVE-AUTORUN-USERPATH", RunPrefix + "MyApp", $"\"{Exe}\" --boot", "Autoruns[MyApp]");
        var stored = Stored("F-0020", "PERSIST-RUNKEY-USERPATH", RunPrefix + "MyApp", Exe, $@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run: MyApp = {Exe} --boot");
        var r = LiveFindingMerger.Merge([live, stored]);
        var f = Assert.Single(r.Findings);
        Assert.Equal("PERSIST-RUNKEY-USERPATH", f.RuleId);
        Assert.Equal(2, f.SupportingEvidence.Count);
        // another value name or another program: nothing merges
        Assert.Equal(2, LiveFindingMerger.Merge([Live("F-0301", "LIVE-AUTORUN-USERPATH", RunPrefix + "Other", Exe, "Autoruns[Other]"), stored]).Findings.Count);
    }

    [Fact]
    public void Startup_entry_in_live_snapshot_and_stored_folder_is_one_finding()
    {
        const string lnk = @"C:\Users\ana\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Tool.lnk";
        var live = Live("F-0400", "LIVE-STARTUP-LNK", "Intrare în folderul Startup (utilizatorul ana): Tool.lnk", lnk, $"Startup[{lnk}]");
        var stored = Stored("F-0030", "PERSIST-STARTUP-FOLDER", "Intrare în folderul Startup: Tool.lnk", "", $"În folderul Startup al profilului „ana” se află „Tool.lnk” ({lnk}), care pornește");
        var f = Assert.Single(LiveFindingMerger.Merge([live, stored]).Findings);
        Assert.Equal("PERSIST-STARTUP-FOLDER", f.RuleId);
        Assert.Equal(2, f.SupportingEvidence.Count);
    }
}
