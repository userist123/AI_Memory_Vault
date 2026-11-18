using System.IO.Compression;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Persistence;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>TaskCache (registry) x ScheduledTask (XML) correlation: PERSIST-TASK-NO-SD, PERSIST-TASK-NO-XML and the TaskCache branch of PERSIST-TASK-CONFIG.</summary>
public sealed class TaskCacheCorrelationTests(ITestOutputHelper output)
{
    private const string Writable = @"C:\Users\u\AppData\Local\Vendor\upd.exe";
    private const string Benign = @"C:\Program Files\Vendor\upd.exe";

    private static TimelineEvent Cache(string uri, string command = Benign, string host = "", string hasSd = "true", string arguments = "", string actionType = "Exec",
                                       string lastRun = "", string dupPaths = "", string classId = "", string data = "") => new()
    {
        Time = Timestamp.FromUtc(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), "", "t"), Source = "TaskCache", EvidenceId = "EV-C", Host = host, Task = uri,
        Path = command.Trim('"'), Process = "x", Summary = "s", Locator = @"SOFTWARE\TaskCache\Tasks\" + Guid.NewGuid(),
        Fields = { ["TaskUri"] = uri, ["TreeHasSd"] = hasSd, ["ActionType"] = actionType, ["Command"] = command, ["Arguments"] = arguments, ["Author"] = "Fabrikam",
                   ["CacheRegisteredUtc"] = "2026-11-01T10:00:00.0000000Z", ["LastRunUtc"] = lastRun, ["TreeDuplicateIdPaths"] = dupPaths, ["ComClassId"] = classId, ["ComData"] = data },
    };

    private static TimelineEvent Xml(string uri, string command = Benign, string host = "", string arguments = "", string actionType = "", string classId = "", string data = "", string evidence = "EV-X") => new()
    {
        Time = Timestamp.FromUtc(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), "", "t"), Source = "ScheduledTask", EvidenceId = evidence, Host = host, Task = uri,
        Path = command.Trim('"'), Process = "x", Summary = "s", Locator = "xml",
        Fields = { ["Command"] = command, ["Arguments"] = arguments, ["Hidden"] = "false", ["Enabled"] = "true", ["ActionType"] = actionType, ["ComClassId"] = classId, ["ComData"] = data },
    };

    /// <summary>n cache tasks with matching XML on host h1, so the coverage of the host is high.</summary>
    private static IEnumerable<TimelineEvent> Matched(int n, string host = "h1") =>
        Enumerable.Range(0, n).SelectMany(i => new[] { Cache($@"\Lab\M{i}", host: host), Xml($@"\Lab\M{i}", host: host) });

    private static List<Finding> Rule(string id, params TimelineEvent[] ev) => Correlation.Run(ev).Where(f => f.RuleId == id).ToList();

    // ------------------------------------------------------------------ PERSIST-TASK-NO-SD

    [Fact]
    public void NoSd_fires_once_per_task_even_with_two_actions()
    {
        var f = Assert.Single(Rule("PERSIST-TASK-NO-SD", Cache(@"\Lab\Hidden", hasSd: "false", lastRun: "2026-11-02T00:00:00Z", dupPaths: @"\Lab\Other"),
                                                       Cache(@"\Lab\Hidden", @"C:\Windows\System32\cmd.exe", hasSd: "false", arguments: "/c ver")));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(Classification.Direct, f.Classification);
        Assert.Equal(Confidence.High, f.Confidence);
        Assert.Equal("T1053.005", f.MitreTechniqueId);
        Assert.Contains("ascunsă", f.Title);
        Assert.Contains(Benign, f.Description);
        Assert.Contains("cmd.exe /c ver", f.Description);
        Assert.Contains("înregistrării în cache", f.Description);
        Assert.Contains("Ultima pornire înregistrată în cache", f.Description);
        Assert.Contains(@"\Lab\Other", f.Description);
        Assert.Contains(f.MissingEvidence, m => m.Contains("4698") && m.Contains("106"));
        Assert.Contains(f.MissingEvidence, m => m.Contains("200/201"));
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("deteriorată"));
    }

    [Fact]
    public void NoSd_omits_last_run_when_absent_and_does_not_fire_when_sd_present_or_tree_missing()
    {
        var f = Assert.Single(Rule("PERSIST-TASK-NO-SD", Cache(@"\Lab\Hidden", hasSd: "false")));
        Assert.DoesNotContain("Ultima pornire", f.Description);
        Assert.DoesNotContain("Același Id", f.Description);
        Assert.Empty(Rule("PERSIST-TASK-NO-SD", Cache(@"\Lab\Visible", hasSd: "true")));
        Assert.Empty(Rule("PERSIST-TASK-NO-SD", Cache(@"\Lab\NoTree", hasSd: "")));
    }

    // ------------------------------------------------------------------ PERSIST-TASK-NO-XML

    [Fact]
    public void NoXml_fires_when_same_host_has_xml_for_other_tasks_only()
    {
        var f = Assert.Single(Rule("PERSIST-TASK-NO-XML", Matched(4).Append(Cache(@"\Lab\Extra", host: "h1")).ToArray()));   // 4 of 5 = 80%: per task
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("altă stație") && a.Contains("Host gol"));
        Assert.Equal(Confidence.Medium, f.Confidence);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("copiat"));
        Assert.Contains(f.MissingEvidence, m => m.Contains("necopiat") || m.Contains("nu au putut fi copiate"));
        Assert.Contains(@"\Lab\Extra", f.Title);
    }

    [Fact]
    public void NoXml_also_fires_when_sd_missing_and_does_not_when_no_xml_collected_or_xml_is_from_another_host()
    {
        Assert.Single(Rule("PERSIST-TASK-NO-XML", Cache(@"\Lab\Extra", hasSd: "false"), Xml(@"\Lab\Other")));
        Assert.Empty(Rule("PERSIST-TASK-NO-XML", Cache(@"\Lab\Extra")));                                                       // no XML events at all
        Assert.Empty(Rule("PERSIST-TASK-NO-XML", Cache(@"\Lab\Extra", host: "h1"), Xml(@"\Lab\Other", host: "h2")));          // XML of another host
        Assert.Empty(Rule("PERSIST-TASK-NO-XML", Cache(@"\Lab\Extra", host: "h1"), Xml(@"\Lab\Other", host: "")));            // one side empty: not the same host
        Assert.Empty(Rule("PERSIST-TASK-NO-XML", Cache(@"\Lab\Extra", host: "h1"), Xml(@"\Lab\Extra", host: "h1")));          // XML present
    }

    [Fact]
    public void NoXml_low_coverage_yields_one_low_summary_finding_with_at_most_ten_names()
    {
        var ev = new List<TimelineEvent> { Xml(@"\Lab\Present", host: "h1"), Cache(@"\Lab\Present", host: "h1") };
        for (int i = 0; i < 12; i++) ev.Add(Cache($@"\Lab\Gone{i:00}", host: "h1"));
        var f = Assert.Single(Rule("PERSIST-TASK-NO-XML", ev.ToArray()));   // 1 of 13 matched
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.StartsWith("12 activități", f.Title);
        Assert.Contains("Gone00", f.Description);
        Assert.Contains("Gone09", f.Description);
        Assert.DoesNotContain("Gone10", f.Description);
        Assert.DoesNotContain("Gone11", f.Description);
        Assert.Contains("copie incompletă", f.Description);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("Host gol"));
    }

    [Fact]
    public void NoXml_threshold_is_80_percent_per_host()
    {
        var below = Matched(3).Append(Cache(@"\Lab\X1", host: "h1")).Append(Cache(@"\Lab\X2", host: "h1")).ToArray();   // 3 of 5 = 60%
        var f = Assert.Single(Rule("PERSIST-TASK-NO-XML", below));
        Assert.Equal(Severity.Low, f.Severity);
        var atLeast = Matched(8).Append(Cache(@"\Lab\X1", host: "h1")).Append(Cache(@"\Lab\X2", host: "h1")).ToArray();   // 8 of 10 = 80%
        Assert.Equal(2, Rule("PERSIST-TASK-NO-XML", atLeast).Count);
        Assert.All(Rule("PERSIST-TASK-NO-XML", atLeast), x => Assert.Equal(Severity.Medium, x.Severity));
    }

    [Fact]
    public void NoXml_matches_case_insensitively_and_ignores_a_missing_leading_backslash()
    {
        Assert.Empty(Rule("PERSIST-TASK-NO-XML", Cache(@"Lab\Task", host: "H1"), Xml(@"\LAB\task", host: "h1"), Xml(@"\Lab\Z", host: "h1")));
        Assert.Empty(Rule("PERSIST-TASK-NO-XML", Cache(@"\Lab\Task"), Xml(@"Lab\TASK")));
    }

    [Fact]
    public void Tree_entry_without_a_Tasks_key_is_not_a_configured_task_for_NO_SD_or_NO_XML()
    {
        var orphan = Cache(@"\Lab\Orphan", "", hasSd: "false", actionType: "");
        orphan.Fields["Anomaly"] = "TreeIdWithoutTasksKey";
        Assert.Empty(Rule("PERSIST-TASK-NO-SD", orphan));
        Assert.Empty(Rule("PERSIST-TASK-NO-XML", orphan, Xml(@"\Lab\Other")));
    }

    // ------------------------------------------------------------------ PERSIST-TASK-CONFIG from the cache

    [Fact]
    public void Config_from_cache_when_only_the_registry_copy_exists()
    {
        var f = Assert.Single(Rule("PERSIST-TASK-CONFIG", Cache(@"\Lab\Updater", Writable)));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("cache-ul activităților planificate din registru (TaskCache)", f.ClassificationReason);
        Assert.Equal(Writable, f.File);
        Assert.DoesNotContain("S-1-5", f.PlainWhat);
    }

    [Fact]
    public void Config_from_cache_is_hidden_and_high_when_sd_missing_and_never_prints_a_sid()
    {
        var f = Assert.Single(Rule("PERSIST-TASK-CONFIG", Cache(@"\Lab\OneDrive Task-S-1-5-21-1-2-3-1001", Writable, hasSd: "false")));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("ascunsă", f.Title);
        Assert.DoesNotContain("S-1-5", f.PlainWhat);
    }

    [Fact]
    public void Config_with_xml_and_cache_for_the_same_command_yields_a_single_finding_from_the_xml()
    {
        var f = Assert.Single(Rule("PERSIST-TASK-CONFIG", Xml(@"\Lab\Updater", Writable, "h1"), Cache(@"Lab\updater", "\"" + Writable + "\"", "h1")));
        Assert.DoesNotContain("TaskCache", f.ClassificationReason);
    }

    [Fact]
    public void Config_from_cache_is_kept_when_xml_has_another_command_or_another_host_and_skips_benign_and_com_actions()
    {
        Assert.Single(Rule("PERSIST-TASK-CONFIG", Xml(@"\Lab\Updater", Benign, "h1"), Cache(@"\Lab\Updater", Writable, "h1")));
        Assert.Equal(2, Rule("PERSIST-TASK-CONFIG", Xml(@"\Lab\Updater", Writable, "h2"), Cache(@"\Lab\Updater", Writable, "h1")).Count);
        Assert.Empty(Rule("PERSIST-TASK-CONFIG", Cache(@"\Lab\Vendor", Benign)));
        Assert.Empty(Rule("PERSIST-TASK-CONFIG", Cache(@"\Lab\Com", "", actionType: "ComHandler")));
    }

    [Fact]
    public void Config_from_cache_is_not_suppressed_when_only_the_arguments_differ()
    {
        const string ps = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
        var f = Assert.Single(Rule("PERSIST-TASK-CONFIG",
            Xml(@"\Lab\Ps", ps, "h1", @"-File C:\Program Files\V\a.ps1"), Cache(@"\Lab\Ps", ps, "h1", arguments: @"-File C:\Users\Public\x.ps1")));
        Assert.Contains("TaskCache", f.ClassificationReason);
        Assert.Contains("x.ps1", f.Description);
    }

    [Fact]
    public void Config_from_cache_is_suppressed_when_arguments_are_equal_after_normalising()
    {
        const string ps = @"C:\Users\u\AppData\Local\V\p.exe";
        Assert.Single(Rule("PERSIST-TASK-CONFIG", Xml(@"\Lab\Ps", ps, "h1", @"-File x.ps1"), Cache(@"\Lab\Ps", ps, "h1", arguments: " \"-FILE X.PS1\" ")));
    }

    // ------------------------------------------------------------------ PERSIST-TASK-CACHE-MISMATCH

    [Fact]
    public void Mismatch_fires_once_per_task_when_the_actions_differ()
    {
        var f = Assert.Single(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\T", Benign, "h1", "/a"), Cache(@"\Lab\T", Benign, "h1", arguments: "/b"), Cache(@"\Lab\T", @"C:\x\y.exe", "h1")));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Classification.Correlated, f.Classification);
        Assert.Equal(Confidence.Medium, f.Confidence);
        Assert.Equal("T1053.005", f.MitreTechniqueId);
        Assert.Contains("/a", f.Description);
        Assert.Contains("/b", f.Description);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("decalaj de colectare"));
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("doar într-unul"));
        Assert.DoesNotContain("manipul", f.Description + f.PlainWhat + f.ClassificationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mismatch_fires_for_a_different_com_handler()
    {
        Assert.Single(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\C", "", "h1", actionType: "ComHandler", classId: "{AAA}", data: "d1"), Cache(@"\Lab\C", "", "h1", actionType: "ComHandler", classId: "{AAA}", data: "d2")));
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\C", "", "h1", actionType: "ComHandler", classId: "{aaa}", data: "D1"), Cache(@"\Lab\C", "", "h1", actionType: "ComHandler", classId: "{AAA}", data: "d1")));
    }

    [Fact]
    public void Mismatch_does_not_fire_when_equal_in_a_different_order_or_case_or_without_the_other_source()
    {
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH",
            Xml(@"\Lab\T", @"C:\A.EXE", "h1", "/X"), Xml(@"\Lab\T", @"C:\b.exe", "h1"),
            Cache(@"lab\t", @"c:\b.exe", "h1"), Cache(@"lab\t", "\"c:\\a.exe\"", "h1", arguments: "/x")));
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH", Cache(@"\Lab\T", Benign, "h1")));
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\T", Benign, "h1")));
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\T", Benign, "h1", "/a"), Cache(@"\Lab\T", Benign, "h2", arguments: "/b")));
    }

    [Fact]
    public void Mismatch_ignores_stale_tasks_keys_unreadable_blobs_and_a_second_copy_of_the_same_file()
    {
        // review round 2: these differences come from duplicates or a partial read, not from the task
        var stale = Cache(@"\Lab\T", @"C:\old\gone.exe", "h1", hasSd: "");            // a Tasks key without Tree entry, same URI
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\T", Benign, "h1"), Cache(@"\Lab\T", Benign, "h1"), stale));
        var partial = Cache(@"\Lab\T", Benign, "h1"); partial.Fields["Anomaly"] = "ActionsUnreadable";
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\T", Benign, "h1"), Xml(@"\Lab\T", @"C:\b.exe", "h1"), partial));
        var copy2 = Xml(@"\Lab\T", Benign, "h1", evidence: "EV-X2");      // the same task file collected twice
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\T", Benign, "h1"), copy2, Cache(@"\Lab\T", Benign, "h1")));
        var other = Xml(@"\Lab\T", @"C:\b.exe", "h1", evidence: "EV-X2");  // a second copy that differs, the first one equal: no finding
        Assert.Empty(Rule("PERSIST-TASK-CACHE-MISMATCH", Xml(@"\Lab\T", Benign, "h1"), other, Cache(@"\Lab\T", Benign, "h1")));
        Assert.Single(Rule("PERSIST-TASK-CACHE-MISMATCH", other, Cache(@"\Lab\T", Benign, "h1")));   // only the differing copy: a finding
    }

    // ------------------------------------------------------------------ SIDs in titles, Tasks key without Tree entry

    [Fact]
    public void Per_user_task_names_never_leak_a_sid_into_title_or_plain_text()
    {
        const string name = @"\Lab\OneDrive Task-S-1-5-21-1-2-3-1001";
        var ev = Matched(4).Concat([Cache(name, Writable, "h1", hasSd: "false"), Xml(@"\Lab\Other", Benign, "h1"), Xml(name, Benign, "h1", "/a"), Cache(name, Benign, "h1", arguments: "/b")]).ToArray();
        var found = Correlation.Run(ev).Where(x => x.RuleId.StartsWith("PERSIST-TASK-", StringComparison.Ordinal)).ToList();
        Assert.Contains(found, x => x.RuleId == "PERSIST-TASK-CONFIG");
        Assert.Contains(found, x => x.RuleId == "PERSIST-TASK-NO-SD");
        Assert.Contains(found, x => x.RuleId == "PERSIST-TASK-CACHE-MISMATCH");
        Assert.All(found, x => { Assert.DoesNotContain("S-1-5-21", x.Title); Assert.DoesNotContain("S-1-5-21", x.PlainWhat); });
        var noXml = Correlation.Run(Matched(4).Append(Cache(name, Benign, "h1")).ToArray()).Single(x => x.RuleId == "PERSIST-TASK-NO-XML");
        Assert.DoesNotContain("S-1-5-21", noXml.Title + noXml.PlainWhat);
    }

    [Fact]
    public void Tasks_key_without_a_tree_entry_is_excluded_from_NO_SD_and_NO_XML_current_behaviour()
    {
        // TreeHasSd is empty when the Tree entry is missing (the reader also flags the event with Anomaly=TasksKeyWithoutTreeEntry)
        var noTree = Cache(@"\Lab\NoTree", hasSd: "");
        noTree.Fields["Anomaly"] = "TasksKeyWithoutTreeEntry";
        Assert.Empty(Rule("PERSIST-TASK-NO-SD", noTree, Xml(@"\Lab\Other", host: "")));
        Assert.Empty(Rule("PERSIST-TASK-NO-XML", noTree, Xml(@"\Lab\Other", host: "")));
    }

    // ------------------------------------------------------------------ real lab data (skipped when absent)

    private const string LabHive = @"E:\LA-Lab\diag\taskcache\ref\SOFTWARE.hiv", LabTasks = @"E:\LA-Lab\diag\taskcache\ref\Tasks.zip";

    [LabFileFact(LabHive, LabTasks)]
    public void Real_clean_station_has_no_NO_SD_and_no_NO_XML_findings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ladfir_tcc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ZipFile.ExtractToDirectory(LabTasks, dir);
            var sink = new ListSink();
            new SoftwareHiveParser().Parse(new EvidenceItem { EvidenceId = "EV-H", CaseId = "C", Source = "software_hive", SourceType = "software_hive", StoredPath = LabHive }, LabHive, sink, default);
            int xmlFiles = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                xmlFiles++;
                new ScheduledTaskParser().Parse(new EvidenceItem { EvidenceId = "EV-X" + xmlFiles, CaseId = "C", Source = "task", SourceType = "task_xml", StoredPath = file }, file, sink, default);
            }
            var findings = Correlation.Run(sink.Events);
            var cache = sink.Events.Count(e => e.Source == "TaskCache");
            var xml = sink.Events.Count(e => e.Source == "ScheduledTask");
            var config = findings.Where(f => f.RuleId == "PERSIST-TASK-CONFIG").ToList();
            output.WriteLine($"files={xmlFiles} taskcache events={cache} xml events={xml}");
            foreach (var f in config) output.WriteLine($"CONFIG [{(f.ClassificationReason.Contains("TaskCache") ? "cache" : "xml")}] {f.Title}");
            foreach (var f in findings.Where(f => f.RuleId == "PERSIST-TASK-NO-XML")) output.WriteLine("NOXML " + f.Title);
            foreach (var f in findings.Where(f => f.RuleId == "PERSIST-TASK-CACHE-MISMATCH")) output.WriteLine("MISMATCH " + f.Title);
            output.WriteLine($"MISMATCH count={findings.Count(f => f.RuleId == "PERSIST-TASK-CACHE-MISMATCH")}");
            Assert.True(cache > 0 && xml > 0);
            Assert.Empty(findings.Where(f => f.RuleId == "PERSIST-TASK-NO-SD"));
            Assert.Empty(findings.Where(f => f.RuleId == "PERSIST-TASK-NO-XML"));
            Assert.Empty(findings.Where(f => f.RuleId == "PERSIST-TASK-CACHE-MISMATCH"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
