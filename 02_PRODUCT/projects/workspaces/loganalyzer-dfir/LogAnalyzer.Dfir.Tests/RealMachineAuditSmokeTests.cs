using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Planner #12 B1: an OPT-IN smoke test of the live-audit pieces of planner #11 against the real machine it runs on. Skipped unless LA_REAL_MACHINE=1.
/// Read-only on the PC (the collectors only copy / query); the case folder is under %TEMP% and removed at the end, except the summary file
/// %TEMP%\la-real-audit-summary.txt. It asserts only robust things (no collector threw, every finding has a Locator, every stored copy matches its hash);
/// what it finds on this PC is printed, never asserted. Browser URLs are never written in full: counts and the top 5 domains only.
/// Run (elevated for the hive collectors):  dotnet test LogAnalyzer.Dfir.Tests --filter "FullyQualifiedName~RealMachineAuditSmokeTests"  with LA_REAL_MACHINE=1.
/// </summary>
public sealed class RealMachineAuditSmokeTests(ITestOutputHelper output)
{
    public sealed class RealMachineFactAttribute : FactAttribute
    {
        public RealMachineFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LA_REAL_MACHINE") != "1") Skip = "Real-machine smoke test not run (set LA_REAL_MACHINE=1; run elevated for the registry hives)";
        }
    }

    private static readonly Regex Url = new(@"[a-zA-Z][a-zA-Z0-9+.-]*://[^\s""'<>]+", RegexOptions.Compiled);
    private static string Mask(string? s) => Url.Replace(s ?? "", m => Uri.TryCreate(m.Value, UriKind.Absolute, out var u) ? $"{u.Scheme}://{u.Host}/…" : "<url>");
    private static string One(string? s, int max = 300) { var t = Mask(s).Replace("\r", " ").Replace("\n", " "); return t.Length <= max ? t : t[..max] + "…"; }

    [RealMachineFact]
    public void Live_audit_collectors_and_rules_run_on_this_machine()
    {
        Assert.True(OperatingSystem.IsWindows());
        var temp = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
        var caseRoot = Path.Combine(temp, "la-real-" + Guid.NewGuid().ToString("N"));
        var summaryPath = Path.Combine(temp, "la-real-audit-summary.txt");
        var sb = new StringBuilder();
        void Line(string s = "") { sb.AppendLine(s); output.WriteLine(s); }
        try
        {
            bool elevated;
            using (var id = WindowsIdentity.GetCurrent()) elevated = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            var v = TestScopes.Valid();
            var scope = new CaseScope
            {
                Purpose = "real-machine smoke test", PeriodFromUtc = DateTimeOffset.UtcNow.AddYears(-1), PeriodToUtc = DateTimeOffset.UtcNow.AddYears(1),
                SystemsInScope = [Environment.MachineName], Approver = v.Approver, LegalBasis = v.LegalBasis, Network = v.Network, Classification = v.Classification,
            };
            var ws = InvestigationPipeline.NewCase(caseRoot, "real-machine-smoke", scope);
            Line($"LogAnalyzer real-machine audit smoke, {DateTimeOffset.Now:O}; host {Environment.MachineName}; elevated={elevated}");
            Line($"Case folder (deleted at the end): {ws.Root}");

            // 2. the collectors, each on its own: one that throws is a failure of the test, one that is not available is recorded
            ICollector[] collectors = [new SoftwareHiveCollector(), new ExecutionArtifactsCollector(), new UserHistoryCollector(), new LiveStateCollector(), new LocalSecurityStateCollector()];
            Line(); Line("== COLLECTORS ==");
            foreach (var c in collectors)
            {
                if (c.RequiresElevation && !elevated) { Line($"[{c.Name}] not elevated: skipped (needs administrator)"); continue; }
                CollectorOutcome o = null!;
                var ex = Record.Exception(() => o = c.Collect(new CollectorContext { Case = ws, IsElevated = elevated }, default));
                Assert.True(ex is null, $"{c.Name} threw: {ex}");
                Line($"[{c.Name}] status={o.Status}; evidence={o.Evidence.Count}; errors/gaps={(o.Errors.Count == 0 ? "none" : string.Join(" | ", o.Errors.Select(e => One(e))))}");
                foreach (var e in o.Evidence)
                    Line($"    {e.EvidenceId} type={e.SourceType} size={e.Size} sha256={(e.Sha256.Length >= 16 ? e.Sha256[..16] : e.Sha256)}");
            }

            // 3. parse + analysis through the normal pipeline entry, on this case only (it holds only what the collectors above stored)
            Line(); Line("== PIPELINE (parse + rules, collect=false) ==");
            var res = new InvestigationPipeline().Run(ws, CollectionProfile.None, collect: false);
            Line($"state={res.State}; timeline rows={res.Timeline.Count}; findings={res.Findings.Count}; gaps={res.Gaps.Count}");
            foreach (var p in res.Parsing) Line($"  parse {p.EvidenceId} {p.Parser}: {p.Status}, records={p.Records}{(p.Error.Length > 0 ? ", error=" + One(p.Error) : "")}");
            foreach (var g in res.Gaps) Line($"  gap: {g.Artifact}: {g.Status}: {One(g.Reason)}");

            // browser: counts and top domains only
            var items = ws.LoadEvidence();
            var browserIds = items.Where(i => i.SourceType is "chromium_history" or "firefox_places").Select(i => i.EvidenceId).ToHashSet();
            var browserRows = res.Timeline.Where(t => browserIds.Contains(t.EvidenceId)).ToList();
            int downloads = browserRows.Count(t => t.Locator.StartsWith("downloads", StringComparison.OrdinalIgnoreCase) || t.Locator.StartsWith("moz_annos", StringComparison.OrdinalIgnoreCase));
            int visits = browserRows.Count - downloads;
            string Domain(TimelineEvent t)
            {
                if (t.Dns.Length > 0) return t.Dns;
                foreach (var v in new[] { t.Path }.Concat(t.Fields.Values))
                    if (v is not null && Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") return u.Host;
                return "";
            }
            var top = browserRows.Select(Domain).Where(d => d.Length > 0).GroupBy(d => d, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(5).ToList();
            Line($"  browser history: visits={visits}, downloads={downloads}; top domains: {(top.Count == 0 ? "none" : string.Join(", ", top.Select(g => $"{g.Key} ({g.Count()})")))}");

            // 4. findings per rule family
            Line(); Line("== FINDINGS BY RULE FAMILY ==");
            string Family(string ruleId) => ruleId.StartsWith("CFG-", StringComparison.Ordinal) ? "ConfigurationAudit (A5)"
                : ruleId.StartsWith("HOSTS-", StringComparison.Ordinal) || ruleId.StartsWith("PS-HISTORY", StringComparison.Ordinal) ? "Hosts / PS-HISTORY (A6)"
                : ruleId.StartsWith("LIVE-", StringComparison.Ordinal) ? "LiveStateAnalyzer (A8 and other LIVE-*)" : "other";
            var byId = items.ToDictionary(i => i.EvidenceId, StringComparer.Ordinal);
            foreach (var fam in new[] { "ConfigurationAudit (A5)", "Hosts / PS-HISTORY (A6)", "LiveStateAnalyzer (A8 and other LIVE-*)", "other" })
            {
                var list = res.Findings.Where(f => Family(f.RuleId) == fam).ToList();
                Line($"-- {fam}: {list.Count} finding(s)");
                foreach (var f in list)
                {
                    var r0 = f.SupportingEvidence.FirstOrDefault();
                    var step = r0 is null ? null : ManualVerification.For(r0, byId.GetValueOrDefault(r0.EvidenceId), ws.Root).FirstOrDefault();
                    Line($"   {f.RuleId} | {f.Severity} | {One(f.Title)}");
                    Line($"      locator={One(r0?.Locator)} | quote={One(r0?.Quote)} | timeSource={r0?.TimeSource}");
                    Line($"      verify={(step is null ? "(none)" : One(step.ToLine(), 400))}");
                }
            }

            // 5. robust assertions
            Assert.All(res.Findings, f =>
            {
                Assert.NotEmpty(f.SupportingEvidence);
                Assert.False(string.IsNullOrWhiteSpace(f.SupportingEvidence[0].Locator), $"finding {f.RuleId} has an empty Locator");
            });
            Line(); Line("== HASH CHECK ==");
            int checkedCount = 0;
            foreach (var e in items.Where(i => i.StoredPath.Length > 0))
            {
                Assert.True(ws.TryFullPath(e.StoredPath, out var full), $"{e.EvidenceId} stored path leaves the case");
                using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var actual = Convert.ToHexString(SHA256.HashData(fs));
                Assert.Equal(e.Sha256, actual, ignoreCase: true);
                checkedCount++;
            }
            Line($"{checkedCount} stored evidence file(s) match their recorded SHA-256");
        }
        finally
        {
            try { File.WriteAllText(summaryPath, sb.ToString(), Encoding.UTF8); output.WriteLine("Summary: " + summaryPath); } catch (IOException) { }
            try { if (Directory.Exists(caseRoot)) Directory.Delete(caseRoot, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { output.WriteLine("Case folder not fully deleted: " + e.Message); }
        }
    }
}
