using System.IO;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Response;
using LogAnalyzer.Dfir.Windows.Containment;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// WP9 review: the "Ridicare izolare" handler must not crash the screen when the case audit cannot be written after the process was
/// resumed; the operator is told that the resume happened and that the audit failed. Fakes only: nothing reaches the host.
/// </summary>
public sealed class ContainmentReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "la-contrel-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private sealed class Fw : IFirewallController
    {
        public Dictionary<string, FirewallRuleInfo> Rules { get; } = new();
        public void AddProgramBlockRule(string name, string programPath, FirewallDirection direction, string description) =>
            Rules[name] = new FirewallRuleInfo(name, programPath, direction, true, description);
        public bool RemoveRule(string name) => Rules.Remove(name);
        public IReadOnlyList<FirewallRuleInfo> ListContainmentRules() => Rules.Values.ToList();
    }

    private sealed class Table : IProcessTable
    {
        public Dictionary<int, ProcessObservation> Processes { get; } = new();
        public Dictionary<string, string> Hashes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ProcessObservation? Observe(int pid) => Processes.GetValueOrDefault(pid);
        public string? HashImage(string path) => Hashes.GetValueOrDefault(path);
    }

    [Fact]
    public void Audit_failure_after_the_resume_is_reported_and_does_not_throw()
    {
        Directory.CreateDirectory(_root);
        var program = Path.Combine(_root, "agent.exe");
        File.WriteAllText(program, "MZ test");
        var ws = CaseWorkspace.Create(Path.Combine(_root, "case"), new CaseInfo
        {
            CaseId = "CASE-REL", Name = "rel", CreatedAtUtc = DateTimeOffset.UtcNow, Timezone = "GTB Standard Time",
            Scope = new CaseScope
            {
                Purpose = "test", PeriodFromUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), PeriodToUtc = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
                SystemsInScope = ["TESTHOST"], Approver = "approver-1", LegalBasis = LegalBasis.Incident,
                Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
            },
        });
        var table = new Table();
        table.Processes[4242] = new ProcessObservation(4242, Start, program, false);
        table.Hashes[program] = "AA11";
        var resumed = new List<int>();
        var svc = new ProcessContainmentService(new Fw(), new ProcessScanner { RunDefenderScan = false }, ws,
            pid => table.Processes[pid] = table.Processes[pid] with { AllThreadsSuspended = true },
            pid => { resumed.Add(pid); table.Processes[pid] = table.Processes[pid] with { AllThreadsSuspended = false }; },
            "tester", table) { VerifyTimeout = TimeSpan.FromSeconds(5) };
        var inc = svc.Contain(program, 4242, "test", new ContainmentOptions(SuspendProcess: true, CopySampleIntoCase: false));
        Assert.Equal(ContainmentState.Contained, inc.State);

        foreach (var f in Directory.EnumerateFiles(Path.Combine(ws.Root, "Logs"))) File.SetAttributes(f, FileAttributes.ReadOnly);
        var status = ContainmentRelease.Run(svc, inc, "test");          // must not throw

        Assert.Equal(new[] { 4242 }, resumed);
        Assert.Contains("A FOST reluat", status);
        Assert.Contains("NU a putut fi scris", status);
        Assert.NotEqual(ContainmentState.Released, inc.State);
    }
}
