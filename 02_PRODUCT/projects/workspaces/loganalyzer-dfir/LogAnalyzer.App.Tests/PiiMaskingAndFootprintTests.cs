using System.IO;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Privacy;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>F10: PII masking is consistent (same value, same pseudonym) and complete for the known fields; the analysis footprint generalises paths. Synthetic names only.</summary>
public sealed class PiiMaskingAndFootprintTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { InvestigationReportPdf.TextObserver.Value = null; _k.Dispose(); }

    private static readonly string[] Originals = ["ana.pop", "WS-ALFA-07", "10.20.30.40", "ana.pop@example.org", "S-1-5-21-1111111111-2222222222-3333333333-1001", "FILESRV9", "ion.ion"];

    private static List<TimelineEvent> Timeline() =>
    [
        GapsTestKit.Ev("EventLog:Security", 0, "4624", path: "", user: "",
            f: [("TargetUserName", "ana.pop"), ("WorkstationName", "WS-ALFA-07"), ("IpAddress", "10.20.30.40"), ("TargetUserSid", "S-1-5-21-1111111111-2222222222-3333333333-1001")]),
        GapsTestKit.Ev("LNK", 5, path: @"C:\Users\ana.pop\Documents\plan.docx"),
        GapsTestKit.Ev("Prefetch", 6, path: @"\\FILESRV9\share\tool.exe", user: @"CORP\ion.ion"),
        GapsTestKit.Ev("BrowserDownload", 7, path: @"C:\Users\ion.ion\Downloads\x.zip", f: [("TabUrl", "mailto:ana.pop@example.org")]),
        GapsTestKit.Ev("EventLog:Security", 8, "4688", f: [("SubjectUserName", "SYSTEM"), ("NewProcessName", @"C:\Windows\System32\svchost.exe"), ("Computer", "WS-ALFA-07.corp.local")]),
    ];

    [Fact]
    public void Masking_is_consistent_the_same_value_always_gets_the_same_pseudonym()
    {
        var m = PiiMasker.FromTimeline(Timeline());
        var a = m.Mask("ana.pop logged on to WS-ALFA-07 from 10.20.30.40");
        var b = m.Mask(@"C:\Users\ana.pop\Desktop and WS-ALFA-07 again, 10.20.30.40 again, mail ana.pop@example.org");
        var user = m.User("ana.pop"); var host = m.Host("WS-ALFA-07");
        Assert.Contains(user, a); Assert.Contains(user, b);
        Assert.Contains(host, a); Assert.Contains(host, b);
        Assert.Equal(m.Mask("10.20.30.40"), m.Mask("10.20.30.40"));
        Assert.NotEqual(m.User("ana.pop"), m.User("ion.ion"));
        Assert.Equal(b, m.Mask(b));   // idempotent
    }

    [Fact]
    public void Every_known_field_of_every_row_is_masked_and_no_original_value_survives()
    {
        var events = Timeline();
        var m = PiiMasker.FromTimeline(events);
        var masked = events.Select(m.Mask).ToList();
        var all = string.Join("\n", masked.SelectMany(e => new[] { e.User, e.Host, e.Path, e.Summary, e.Process, e.RemoteIp }.Concat(e.Fields.Values)));
        foreach (var o in Originals) Assert.DoesNotContain(o, all, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(m.User("ana.pop"), masked[0].Fields["TargetUserName"]);
        Assert.StartsWith(@"C:\Users\" + m.User("ana.pop") + @"\Documents", masked[1].Path);
        Assert.StartsWith(@"\\" + m.Host("FILESRV9") + @"\share", masked[2].Path);
        Assert.Contains("SYSTEM", masked[4].Fields["SubjectUserName"]);                // built-in accounts are not personal data
        Assert.Contains(@"C:\Windows\System32\svchost.exe", masked[4].Fields["NewProcessName"]);
        Assert.Equal(m.Host("WS-ALFA-07"), masked[4].Fields["Computer"]);              // FQDN reduced to the same host pseudonym
        Assert.All(m.Mapping.Values, v => Assert.Matches(@"^(Utilizator|Statie|IP|SID|email|Serial|Domeniu)-\d+", v));
        // the mapping stays with the operator; it is not part of the masked output
        Assert.DoesNotContain("ana.pop", string.Join(" ", masked.Select(e => e.Summary)));
    }

    [Fact]
    public void Masked_investigation_report_prints_no_original_user_or_host_and_says_it_is_masked()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var r = new InvestigationResult { Case = ws };
        r.Timeline.AddRange(Timeline());
        r.Findings.Add(new Finding { FindingId = "F-0001", RuleId = "TEST", Title = "Logon ana.pop pe WS-ALFA-07", Description = @"Din 10.20.30.40, fișier C:\Users\ana.pop\Documents\plan.docx", Severity = Severity.Low });
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        var m = PiiMasker.FromTimeline(r.Timeline, ws.Info.Host, ws.Info.User);
        InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, "mascat.pdf"), "investigator", masker: m);
        foreach (var o in new[] { "ana.pop", "WS-ALFA-07", "10.20.30.40" }) Assert.DoesNotContain(o, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(m.User("ana.pop"), text);
        Assert.Contains("EXPORT CU DATE PERSONALE MASCATE", text);
    }

    [Fact]
    public void Footprint_lists_what_was_written_with_generalised_paths()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var fp = AnalysisFootprint.ForCase(ws);
        var text = string.Join("\n", fp.Lines());
        Assert.False(fp.LiveCollection);
        Assert.Contains("Nicio colectare live", text);
        Assert.Contains("Registry: doar citire", text);
        Assert.Contains(@"%TEMP%\LogAnalyzer\evtx_repair", text);
        Assert.Contains("Fișiere aduse de operator", text);
        Assert.Contains("%TEMP%", fp.CaseFolder);                                     // the test case lives under the temp folder
        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generalize_replaces_profile_roots_and_the_machine_name()
    {
        Assert.Equal(@"%USERPROFILE%\Desktop\x.txt", AnalysisFootprint.Generalize(@"D:\Users\someone\Desktop\x.txt"));
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "Cases", "CASE-" + Environment.MachineName + "-1");
        Assert.Equal(@"%LOCALAPPDATA%\LogAnalyzer\Cases\CASE-<stație>-1", AnalysisFootprint.Generalize(local));
    }

    [Fact]
    public void Investigation_report_has_the_footprint_section()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        InvestigationReportPdf.Write(new InvestigationResult { Case = ws }, Path.Combine(_k.Dir, "fp.pdf"), "investigator");
        Assert.Contains("Amprenta analizei pe țintă", text);
        Assert.Contains("Registry: doar citire", text);
    }
}
