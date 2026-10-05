using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public class InvestigationTests
{
    private static readonly DateTime T0 = new(2026, 9, 19, 14, 50, 0, DateTimeKind.Utc);

    private static TimelineEvent E(string source, string id, int minute, string ev = "EV-1", string locator = "", string process = "", string path = "",
                                   params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = source, EventId = id, EvidenceId = ev,
        Locator = locator.Length > 0 ? locator : $"EventRecordID={1000 + minute}", Process = process, Path = path, Summary = "t",
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public void Synthetic_incident_is_reconstructed_as_one_chain()
    {
        var events = new List<TimelineEvent>
        {
            E("Prefetch", "", 3, "EV-PF1", "pf", "SETUP.EXE", "", ("PrefetchHash", "C0F23475"), ("RunCount", "4"),
              ("ReferencedFiles", @"\VOLUME{x}\USERS\U\DOWNLOADS\TOOL_302044\SETUP.EXE|\VOLUME{x}\WINDOWS\SYSTEM32\NTDLL.DLL")),
            E("EventLog:Microsoft-Windows-Windows Defender/Operational", "1116", 14, "EV-DEF", "", "", "",
              ("Threat Name", "Behavior:Win32/GenCodeInjected.H"), ("Path", @"process:_pid:52196; file:_C:\Users\U\Downloads\TOOL_302044\NanAgent32.exe"), ("Action Name", "Not Applicable")),
            E("Prefetch", "", 19, "EV-PF2", "pf", "MSIEXEC.EXE", "", ("PrefetchHash", "8FFB1633"), ("RunCount", "9"),
              ("ReferencedFiles", @"\VOLUME{x}\WINDOWS\SYSTEM32\MSIEXEC.EXE|\VOLUME{x}\USERS\U\APPDATA\LOCAL\CONEXANT\CXUTILSVC HELPER\BOOTSTRAP_7D57.CMD")),
            E("SRUM", "", 25, "EV-SRUM", "row1", "msbuild.exe", @"\device\harddiskvolume3\windows\microsoft.net\framework\v4.0.30319\msbuild.exe",
              ("BytesSent", "7764746"), ("BytesRecvd", "100581358")),
            // benign noise: PowerShell's own policy probe and a Microsoft-owned writable path
            E("Prefetch", "", 200, "EV-PF3", "pf", "POWERSHELL.EXE", "", ("PrefetchHash", "1"), ("RunCount", "1"),
              ("ReferencedFiles", @"\VOLUME{x}\USERS\U\APPDATA\LOCAL\TEMP\__PSSCRIPTPOLICYTEST_ABC.PS1")),
        };
        var findings = Correlation.Run(events);

        Assert.Contains(findings, f => f.RuleId == "DEF-DETECTION" && f.File.EndsWith("NanAgent32.exe") && f.Title.Contains("GenCodeInjected"));
        Assert.Contains(findings, f => f.RuleId == "EXEC-SCRIPT-VIA-LOLBIN" && f.Process == "MSIEXEC.EXE");
        Assert.Contains(findings, f => f.RuleId == "NET-LOLBIN-TRAFFIC" && f.Process == "msbuild.exe");
        Assert.Contains(findings, f => f.RuleId == "EXEC-USERPATH-CORRELATED" && f.Process == "SETUP.EXE");
        Assert.DoesNotContain(findings, f => f.Process == "POWERSHELL.EXE");
        var chain = Assert.Single(findings, f => f.RuleId == "INCIDENT-CHAIN");
        Assert.Equal(Severity.Critical, chain.Severity);
        Assert.True(chain.Description.IndexOf("SETUP.EXE", StringComparison.Ordinal) < chain.Description.IndexOf("msbuild", StringComparison.Ordinal));
        Assert.All(findings, f => Assert.NotEmpty(f.SupportingEvidence));
    }

    [Fact]
    public void Cleared_logs_and_record_gaps_are_reported()
    {
        var events = new List<TimelineEvent>
        {
            E("EventLog:Security", "4624", 1, "EV-SEC", "EventRecordID=100"),
            E("EventLog:Security", "4624", 2, "EV-SEC", "EventRecordID=101"),
            E("EventLog:Security", "4624", 30, "EV-SEC", "EventRecordID=900"),
            E("EventLog:Security", "1102", 31, "EV-SEC", "EventRecordID=901", "", "", ("SubjectUserName", "ion")),
        };
        var f = Correlation.Run(events);
        Assert.Contains(f, x => x.RuleId == "LOG-TAMPER" && x.Description.Contains("ion"));
        var gap = Assert.Single(f, x => x.RuleId == "LOG-GAP");
        Assert.Contains("lipsesc 798", gap.Description);
        Assert.Equal(Classification.Candidate, gap.Classification);
    }

    [Fact]
    public void Exe_path_is_extracted_from_command_lines()
    {
        Assert.Equal(@"C:\Program Files\A B\x.exe", LiveStateCollector.ExePath("\"C:\\Program Files\\A B\\x.exe\" -k netsvcs"));
        Assert.Equal(@"C:\ProgramData\u\svc.exe", LiveStateCollector.ExePath(@"C:\ProgramData\u\svc.exe /run"));
    }

    [CorpusFact("defenderEvtx")]
    public void NanAgent_corpus_yields_the_known_incident_chain()
    {
        var root = Corpus.Root;
        var files = new[] { "Application", "Microsoft-Windows-Windows Defender%4Operational" }
            .Select(n => Path.Combine(root, "01_RAW_EVENTLOGS", n + ".evtx")).Where(File.Exists).ToList();
        files.AddRange(Directory.GetFiles(Path.Combine(root, "09_PREFETCH", "Prefetch"), "*.pf"));
        files.Add(Path.Combine(root, "07_EXECUTION", "SRUM", "SRUDB.dat"));
        var casesRoot = Path.Combine(Path.GetTempPath(), "la-inv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ws = InvestigationPipeline.NewCase(casesRoot, "regression");
            InvestigationPipeline.Import(ws, files);
            var r = new InvestigationPipeline().Run(ws, CollectionProfile.Standard, collect: false);

            Assert.Contains(r.Findings, f => f.RuleId == "DEF-DETECTION" && f.Description.Contains("NanAgent32.exe"));
            Assert.Contains(r.Findings, f => f.RuleId == "EXEC-SCRIPT-VIA-LOLBIN" && f.Description.Contains("BOOTSTRAP_7D57.CMD"));
            Assert.Contains(r.Findings, f => f.RuleId == "NET-LOLBIN-TRAFFIC" && f.Process == "msbuild.exe");
            var chain = r.Findings.First(f => f.RuleId == "INCIDENT-CHAIN" && f.FirstSeenUtc!.Value.UtcDateTime.Date == new DateTime(2026, 9, 19));
            Assert.Equal(Severity.Critical, chain.Severity);
            Assert.Contains("SETUP.EXE", chain.Description);
            Assert.Contains("NanAgent32.exe", chain.Description);
            Assert.Contains("msbuild.exe", chain.Description);
            Assert.True(File.Exists(r.TimelineCsv));

            // P1 provenance on the real case: every event and every finding reference carries the acquisition hash.
            var hashes = ws.LoadEvidence().ToDictionary(e => e.EvidenceId, e => e.Sha256);
            Assert.Empty(r.RejectedFindings);
            Assert.All(r.Timeline, e => { Assert.Equal(hashes[e.EvidenceId], e.SourceSha256); Assert.NotEmpty(e.ParserId); });
            Assert.All(r.Findings.SelectMany(f => f.SupportingEvidence), x => Assert.Equal(hashes[x.EvidenceId], x.Sha256));
            Assert.True(ReportIntegrity.Check(r).AllIntact);

            var pdf = Path.Combine(ws.Root, "raport.pdf");
            InvestigationReportPdf.Write(r, pdf, "test");
            Assert.True(new FileInfo(pdf).Length > 20_000);
        }
        finally
        {
            if (Directory.Exists(casesRoot))
            {
                foreach (var f in Directory.EnumerateFiles(casesRoot, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(casesRoot, true);
            }
        }
    }
}
