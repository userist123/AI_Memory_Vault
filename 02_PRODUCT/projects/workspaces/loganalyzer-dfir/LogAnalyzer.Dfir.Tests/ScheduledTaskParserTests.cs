using System.Text;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Persistence;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Scheduled task XML (System32\Tasks): synthetic exact values and the real corpus against schtasks /query /v.</summary>
public sealed class ScheduledTaskParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_tasks_" + Guid.NewGuid().ToString("N"));

    public ScheduledTaskParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-T", CaseId = "C", Source = "task", SourceType = "task_xml", StoredPath = path };

    private string Write(string name, string xml)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, xml, Encoding.Unicode);   // System32\Tasks files are UTF-16 LE with BOM
        return p;
    }

    private const string Malicious = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Date>2026-09-19T17:58:12.5+03:00</Date>
            <Author>MARIUS-PC\u</Author>
            <URI>\orchestratormaintain</URI>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
            <TimeTrigger><StartBoundary>2026-09-19T18:00:00</StartBoundary><Enabled>true</Enabled></TimeTrigger>
          </Triggers>
          <Principals><Principal id="Author"><UserId>S-1-5-21-1-2-3-1001</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
          <Settings><Hidden>true</Hidden><Enabled>true</Enabled></Settings>
          <Actions Context="Author">
            <Exec><Command>C:\Users\u\AppData\Local\Conexant\pro_cmd_core.exe</Command><Arguments>-run silent</Arguments><WorkingDirectory>C:\Users\u</WorkingDirectory></Exec>
            <ComHandler><ClassId>{12345678-1234-1234-1234-123456789ABC}</ClassId></ComHandler>
          </Actions>
        </Task>
        """;

    [Fact]
    public void Task_xml_is_recognised_by_content()
    {
        var p = Write("orchestratormaintain", Malicious);
        Assert.Equal("task_xml", EvidenceFingerprint.Detect(p));
        var notTask = Path.Combine(_dir, "x.xml");
        File.WriteAllText(notTask, "<?xml version=\"1.0\"?><root/>");
        Assert.NotEqual("task_xml", EvidenceFingerprint.Detect(notTask));
    }

    [Fact]
    public void Each_action_becomes_an_event_with_the_task_configuration()
    {
        var p = Write("orchestratormaintain", Malicious);
        var sink = new ListSink();
        var r = new ScheduledTaskParser().Parse(Item(p), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Equal(2, sink.Events.Count);
        var exec = sink.Events[0];
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 14, 58, 12, 500, TimeSpan.Zero), exec.Time.Utc);
        Assert.Equal("2026-09-19T17:58:12.5+03:00", exec.Time.Raw);
        Assert.Equal(@"C:\Users\u\AppData\Local\Conexant\pro_cmd_core.exe", exec.Path);
        Assert.Equal("pro_cmd_core.exe", exec.Process);
        Assert.Equal(exec.Path, exec.Fields["Command"]);
        Assert.Equal(@"\orchestratormaintain", exec.Task);
        Assert.Equal("-run silent", exec.Fields["Arguments"]);
        Assert.Equal("S-1-5-21-1-2-3-1001", exec.User);
        Assert.Equal("HighestAvailable", exec.Fields["RunLevel"]);
        Assert.Equal("true", exec.Fields["Hidden"]);
        Assert.Equal("LogonTrigger, TimeTrigger 2026-09-19T18:00:00", exec.Fields["Triggers"]);
        Assert.Equal(TemporalType.CurrentSnapshot, exec.TemporalType);
        Assert.Contains("registration", exec.TimeSemantics);
        Assert.Equal("Actions/Exec[1]", exec.Locator);
        Assert.Equal("{12345678-1234-1234-1234-123456789ABC}", sink.Events[1].Fields["ComClassId"]);
    }

    [Fact]
    public void Date_without_offset_is_kept_raw_and_not_converted()
    {
        var p = Write("t", Malicious.Replace("2026-09-19T17:58:12.5+03:00", "2026-09-19T17:58:12"));
        var sink = new ListSink();
        new ScheduledTaskParser().Parse(Item(p), p, sink, default);
        Assert.Null(sink.Events[0].Time.Utc);
        Assert.Equal("2026-09-19T17:58:12", sink.Events[0].Time.Raw);
    }

    [Fact]
    public void Task_without_actions_is_reported_and_broken_xml_fails()
    {
        var p = Write("empty", Malicious[..Malicious.IndexOf("<Actions", StringComparison.Ordinal)] + "</Task>");
        var sink = new ListSink();
        var r = new ScheduledTaskParser().Parse(Item(p), p, sink, default);
        Assert.Single(sink.Events);
        Assert.Equal("", sink.Events[0].Path);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("nicio acțiune"));

        var bad = Write("bad", Malicious[..200]);
        Assert.Equal(EvidenceStatus.Failed, new ScheduledTaskParser().Parse(Item(bad), bad, new ListSink(), default).Status);
    }

    [Fact]
    public void Task_running_from_user_writable_path_becomes_a_finding_with_evidence()
    {
        var bad = Write("orchestratormaintain", Malicious);
        var good = Write("vendor", Malicious.Replace(@"C:\Users\u\AppData\Local\Conexant\pro_cmd_core.exe", @"C:\Program Files\Vendor\upd.exe")
                                            .Replace(@"\orchestratormaintain", @"\vendor").Replace("<Hidden>true</Hidden>", "<Hidden>false</Hidden>"));
        var env = Write("envtask", Malicious.Replace(@"C:\Users\u\AppData\Local\Conexant\pro_cmd_core.exe", @"%LOCALAPPDATA%\x\run.exe")
                                           .Replace(@"\orchestratormaintain", @"\envtask"));
        var sink = new ListSink();
        foreach (var p in new[] { bad, good, env }) new ScheduledTaskParser().Parse(Item(p), p, sink, default);

        var findings = LogAnalyzer.Dfir.Analysis.Correlation.Run(sink.Events).Where(f => f.RuleId == "PERSIST-TASK-CONFIG").ToList();
        Assert.Equal(2, findings.Count);
        var f = Assert.Single(findings, x => x.File.EndsWith("pro_cmd_core.exe"));
        Assert.Equal(Severity.High, f.Severity);   // hidden + user-writable
        Assert.Equal("T1053.005", f.MitreTechniqueId);
        Assert.Contains("orchestratormaintain", f.Title);
        Assert.Equal("Actions/Exec[1]", Assert.Single(f.SupportingEvidence).Locator);
        Assert.Contains(findings, x => x.File == @"%LOCALAPPDATA%\x\run.exe");
        Assert.True(LogAnalyzer.Dfir.Analysis.Correlation.IsUserWritable(@"%TEMP%\a.exe"));
        Assert.False(LogAnalyzer.Dfir.Analysis.Correlation.IsUserWritable(@"%windir%\system32\a.exe"));
    }

    /// <summary>Every Exec action in the copied System32\Tasks must match "Task To Run" reported by schtasks on the station.</summary>
    [CorpusFact("tasks")]
    public void Real_tasks_match_schtasks_output()
    {
        var folder = Path.Combine(Corpus.Root, Corpus.S("tasks", "folder"));
        var csv = CsvReader.ReadDicts(Path.Combine(Corpus.Root, Corpus.S("tasks", "schtasksCsv")))
            .Where(d => d.TryGetValue("TaskName", out var n) && n != "TaskName")
            .GroupBy(d => d["TaskName"], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(d => d["Task To Run"].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        // schtasks prints the command without double quotes and "Multiple actions" for tasks with several actions.
        static string Unquote(string s) => s.Replace("\"", "");
        int files = 0, compared = 0, multi = 0;
        var mismatch = new List<string>();
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (EvidenceFingerprint.Detect(f) != "task_xml") continue;
            files++;
            var sink = new ListSink();
            var r = new ScheduledTaskParser().Parse(Item(f), f, sink, default);
            Assert.True(r.Status is EvidenceStatus.Success, $"{f}: {r.Status} {r.Error}");
            var execs = sink.Events.Where(e => e.Fields["ActionType"] == "Exec").ToList();
            if (execs.Count == 0 || !csv.TryGetValue(execs[0].Task, out var runs)) continue;
            if (sink.Events.Count > 1)
            {
                multi++;
                if (!runs.Contains("Multiple actions")) mismatch.Add($"{execs[0].Task}: {sink.Events.Count} acțiuni, schtasks „{string.Join(" | ", runs)}”");
                continue;
            }
            compared++;
            var expected = Unquote((execs[0].Fields["Command"] + " " + execs[0].Fields["Arguments"]).Trim());
            if (!runs.Contains(expected)) mismatch.Add($"{execs[0].Task}: parser „{expected}” vs schtasks „{string.Join(" | ", runs)}”");
        }
        Assert.True(files > 250, $"{files} fișiere de task");
        Assert.True(compared > 100, $"{compared} acțiuni comparate");
        Assert.True(multi > 0, "niciun task cu mai multe acțiuni în corpus");
        Assert.True(mismatch.Count == 0, string.Join(Environment.NewLine, mismatch.Take(20)));
    }
}
