using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Graph;
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
        files.Add(Path.Combine(root, "21_BROWSER", "Chrome", "Default", "History"));
        files.Add(Path.Combine(root, "25_TARGET_MSBUILD", "Conexant_CxUtilSvcHelper_COPY", "CxUtilSvc Helper", "NetworkMonitor.targets"));
        var casesRoot = Path.Combine(Path.GetTempPath(), "la-inv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ws = InvestigationPipeline.NewCase(casesRoot, "regression");
            InvestigationPipeline.Import(ws, files);
            // The investigation's IOC inventory as a case rule list (Rules/ioc), next to the rules shipped with the application.
            Directory.CreateDirectory(Path.Combine(ws.Root, "Rules", "ioc"));
            File.Copy(Path.Combine(root, "29_IOC", "IOC_INVENTORY.csv"), Path.Combine(ws.Root, "Rules", "ioc", "IOC_INVENTORY.csv"));
            var r = new InvestigationPipeline().Run(ws, CollectionProfile.Standard, collect: false);

            Assert.Contains(r.Findings, f => f.RuleId == "DEF-DETECTION" && f.Description.Contains("NanAgent32.exe"));
            Assert.Contains(r.Findings, f => f.RuleId == "EXEC-SCRIPT-VIA-LOLBIN" && f.Description.Contains("BOOTSTRAP_7D57.CMD"));
            Assert.Contains(r.Findings, f => f.RuleId == "NET-LOLBIN-TRAFFIC" && f.Process == "msbuild.exe");
            var chain = r.Findings.First(f => f.RuleId == "INCIDENT-CHAIN" && f.FirstSeenUtc!.Value.UtcDateTime.Date == new DateTime(2026, 9, 19));
            Assert.Equal(Severity.Critical, chain.Severity);
            Assert.Contains("SETUP.EXE", chain.Description);
            Assert.Contains("NanAgent32.exe", chain.Description);
            Assert.Contains("msbuild.exe", chain.Description);
            // Initial access: the archive downloaded from tzd4is.cyou, then SETUP.EXE run from its extracted folder.
            var access = Assert.Single(r.Findings, f => f.RuleId == "DOWNLOAD-THEN-EXEC" && f.File.Contains("302044"));
            Assert.Equal(Confidence.High, access.Confidence);
            Assert.Contains("tzd4is.cyou", access.Description);
            Assert.Contains("SETUP.EXE", access.Description);
            Assert.Contains("302044.zip", chain.Description);

            // P4 graph on the real case: direct edges where the evidence shows them, correlation only as derivation.
            var g = r.Graph!;
            Assert.All(g.Relationships, x => Assert.True(x.EvidenceId.Length > 0 && x.Locator.Length > 0 || x.Derivation.Length > 0));
            var domain = g.Find("Domain", "tzd4is.cyou")!;
            Assert.Contains(g.Edges(domain.Id), x => x.Type == RelationType.Downloaded && x.TargetEntity.Contains("302044.ZIP") && x.Classification == Classification.Direct && x.Reason.Contains("tab"));
            Assert.Contains(g.Relationships, x => x.Type == RelationType.Downloaded && x.SourceEntity == "Domain:130af8a83568f840a6ae55fd.192169503.com" && x.TargetEntity.Contains("302044.ZIP"));
            Assert.Contains(g.Relationships, x => x.Type == RelationType.Detected && x.TargetEntity.Contains("NANAGENT32.EXE"));
            Assert.Contains(g.Relationships, x => x.Type == RelationType.Loaded && x.TargetEntity.Contains("BOOTSTRAP_7D57.CMD"));
            Assert.Contains(g.Relationships, x => x.Type == RelationType.DerivedFrom && x.SourceEntity.EndsWith(@"\DOWNLOADS\SAMFW_FRP_TOOL_V5.9_SETUP_DOWNLOAD_LATES_ARCHIVE_FILE_302044\SETUP.EXE") && x.Classification == Classification.Correlated);
            Assert.True(File.Exists(Path.Combine(ws.Root, "Analysis", "graph.json")));

            // P5 detection on the real case: Sigma (Defender), IOC path in Prefetch, hash IOC and YARA on the MSBuild loader.
            Assert.Contains(r.Detections, d => d.Kind == LogAnalyzer.Dfir.Detection.DetectionKind.Sigma && d.RuleId.Contains("8c3e2b4a") && d.EvidenceId.Length > 0);
            Assert.Contains(r.Detections, d => d.Kind == LogAnalyzer.Dfir.Detection.DetectionKind.Ioc && d.Match.Contains("BOOTSTRAP_7D57.CMD"));
            Assert.Contains(r.Detections, d => d.Kind == LogAnalyzer.Dfir.Detection.DetectionKind.Hash && d.Match.Contains("NetworkMonitor.targets"));
            Assert.Contains(r.Detections, d => d.Kind == LogAnalyzer.Dfir.Detection.DetectionKind.Yara && d.RuleId == "YARA:MSBuild_PropertyFunction_EntityObfuscation");
            Assert.All(r.Detections, d => Assert.True(d.RuleSha256.Length == 64 && d.EvidenceId.Length > 0));
            Assert.DoesNotContain(r.Gaps, g => g.Artifact.StartsWith("Regulă"));
            Assert.True(File.Exists(Path.Combine(ws.Root, "Analysis", "rules.json")));
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
