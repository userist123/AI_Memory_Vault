using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Windows.Containment;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.Response.Containment;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security audit #2: actions that change the station (policy approve/apply/rollback, program containment, station
/// isolation, audit-policy change) are refused in the core for a signed-in operator, and the account that approved a
/// policy cannot also apply it. A refused action writes nothing.
/// </summary>
public sealed class HostChangeRoleGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-hostgate-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AuthSession Operator(string account = "STATIE\\operator") =>
        new(account, "Operator Test", AuthRole.Operator, false, SignInMode.Card, null, null, Now);

    private static AuthSession Admin(string account) =>
        new(account, "Admin Test", AuthRole.Administrator, false, SignInMode.Card, null, null, Now);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private const string Yaml = """
        policy:
          id: LA-GATE-TEST
          version: '1.0.0'
          title: Test de autorizare
          author: autor
        controls:
          - id: R1
            title: DWORD care lipsește
            detection: { type: registry, hive: HKLM, key: 'Software\LaGateTest', value: Enabled, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
        """;

    private (PolicyExecutor X, FakeSettings Fake, PolicyDocument P, PolicyStore Store) Approved(string approver = "aprobator")
    {
        var fake = new FakeSettings();
        var x = new PolicyExecutor([fake], Path.Combine(_dir, "evidence"));
        var p = PolicyLoader.Parse(Yaml);
        var store = new PolicyStore(Path.Combine(_dir, "store.json"));
        store.Register(p, "registrator");
        store.Validate(p, "registrator");
        var dry = x.Plan(p);
        store.MarkTested(p, "registrator", dry.Controls.Count - dry.Unreadable, dry.Unreadable);
        store.Approve(p, approver, "aprobată pentru test");
        return (x, fake, p, store);
    }

    private static string[] Snapshot(string dir) => Directory.Exists(dir)
        ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => $"{f}|{new FileInfo(f).Length}").OrderBy(s => s).ToArray()
        : [];

    [Fact]
    public void Operator_session_cannot_apply_or_roll_back_a_policy_and_nothing_is_written()
    {
        var (x, fake, p, store) = Approved();
        var plan = x.Plan(p);
        var evidence = Path.Combine(_dir, "evidence");
        var before = Snapshot(evidence);
        using (OperatorIdentity.Scope(Operator(), authenticationRequired: true))
        {
            var ex = Assert.Throws<HostChangeNotAuthorizedException>(() => x.Apply(p, plan, store, "STATIE\\operator", plan.Sha256));
            Assert.Contains("administrator", ex.Message);
            Assert.Contains("Nu s-a schimbat nimic", ex.Message);
            Assert.Throws<HostChangeNotAuthorizedException>(() => x.Rollback(p, "orice", "STATIE\\operator"));
        }
        Assert.Empty(fake.Values);
        Assert.Equal(before, Snapshot(evidence));
        Assert.Equal(PolicyStatus.Approved, store.Get(p)!.Status);
    }

    [Fact]
    public void Nobody_signed_in_while_authentication_is_required_is_refused_too()
    {
        var (x, fake, p, store) = Approved();
        var plan = x.Plan(p);
        using (OperatorIdentity.Scope(null, authenticationRequired: true))
            Assert.Throws<HostChangeNotAuthorizedException>(() => x.Apply(p, plan, store, "cineva", plan.Sha256));
        Assert.Empty(fake.Values);
    }

    [Fact]
    public void Operator_session_cannot_approve_a_policy()
    {
        var fake = new FakeSettings();
        var x = new PolicyExecutor([fake], Path.Combine(_dir, "evidence"));
        var p = PolicyLoader.Parse(Yaml);
        var store = new PolicyStore(Path.Combine(_dir, "store.json"));
        store.Register(p, "registrator");
        store.Validate(p, "registrator");
        var dry = x.Plan(p);
        store.MarkTested(p, "registrator", dry.Controls.Count, 0);
        using (OperatorIdentity.Scope(Operator("STATIE\\aprobator"), authenticationRequired: true))
            Assert.Throws<HostChangeNotAuthorizedException>(() => store.Approve(p, "STATIE\\aprobator", "vreau"));
        Assert.Equal(PolicyStatus.Tested, store.Get(p)!.Status);
    }

    [Fact]
    public void The_account_that_approved_cannot_apply_and_nothing_is_written()
    {
        var (x, fake, p, store) = Approved(approver: "STATIE\\sef");
        var plan = x.Plan(p);
        var ex = Assert.Throws<PolicyLifecycleException>(() => x.Apply(p, plan, store, " statie\\SEF ", plan.Sha256));
        Assert.Contains("aprobat", ex.Message);
        Assert.Empty(fake.Values);
        Assert.False(File.Exists(x.Audit.Path));
    }

    [Fact]
    public void Administrator_other_than_the_approver_applies_the_policy()
    {
        // Review #2: with sign-in required the approval must be in the sealed auth audit, made by the signed-in approver.
        var auth = new AuthService(Path.Combine(_dir, "auth"));
        var fake = new FakeSettings();
        var x = new PolicyExecutor([fake], Path.Combine(_dir, "evidence"));
        var p = PolicyLoader.Parse(Yaml);
        var store = new PolicyStore(Path.Combine(_dir, "store.json"));
        using (OperatorIdentity.Scope(Admin("STATIE\\registrator"), authenticationRequired: true))
        using (PolicyApprovals.Scope(auth))
            store.Register(p, "STATIE\\registrator");      // sealed registration (review of the remediations #3)
        store.Validate(p, "registrator");
        var dry = x.Plan(p);
        store.MarkTested(p, "registrator", dry.Controls.Count - dry.Unreadable, dry.Unreadable);
        using (OperatorIdentity.Scope(Admin("STATIE\\sef"), authenticationRequired: true))
        using (PolicyApprovals.Scope(auth))
            store.Approve(p, "STATIE\\sef", "aprobată pentru test");
        var plan = x.Plan(p);
        using (OperatorIdentity.Scope(Admin("STATIE\\admin2"), authenticationRequired: true))
        using (PolicyApprovals.Scope(auth))
        {
            var exec = x.Apply(p, plan, store, "STATIE\\admin2", plan.Sha256);
            Assert.Equal(ExecutionStatus.Verified, exec.Status);
        }
        Assert.Equal("1", fake.Values.Single().Value.Value);
    }

    [Fact]
    public void Operator_session_cannot_contain_or_release_a_program_and_nothing_is_written()
    {
        var root = Path.Combine(_dir, "case");
        var ws = CaseWorkspace.Create(root, new CaseInfo { CaseId = "CASE-G", Name = "gate", CreatedAtUtc = Now, Timezone = "GTB Standard Time", Scope = TestScopes.Valid() });
        var fw = new FakeFirewall();
        var suspended = new List<int>();
        var svc = new ProcessContainmentService(fw, new ProcessScanner { RunDefenderScan = false }, ws, suspended.Add, _ => { }, "STATIE\\operator");
        var before = Snapshot(root);
        var program = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        using (OperatorIdentity.Scope(Operator(), authenticationRequired: true))
        {
            Assert.Throws<HostChangeNotAuthorizedException>(() => svc.Contain(program, 4242, "test", new ContainmentOptions(true, CopySampleIntoCase: true)));
            var fake = new ProcessIncident { IncidentId = "INC-X", CaseId = "CASE-G", Trigger = "test", ProgramPath = program, FirewallRules = { "rule-x" } };
            fw.Rules["rule-x"] = (program, FirewallDirection.Outbound);
            Assert.Throws<HostChangeNotAuthorizedException>(() => svc.Release(fake, "test"));
            Assert.True(fw.Rules.ContainsKey("rule-x"));
            fw.Rules.Clear();
        }
        Assert.Empty(fw.Rules);
        Assert.Empty(suspended);
        Assert.False(Directory.Exists(svc.IncidentsDir));
        Assert.Equal(before, Snapshot(root));
    }

    private sealed class CountingRunner : IFirewallCommandRunner
    {
        public int Calls;
        public CommandOutcome Netsh(string arguments) { Calls++; return new CommandOutcome(0, ""); }
    }

    [Fact]
    public void Operator_session_cannot_isolate_the_station_block_an_ioc_or_change_the_audit_policy()
    {
        var runner = new CountingRunner();
        using (OperatorIdentity.Scope(Operator(), authenticationRequired: true))
        {
            var iso = SystemDefenseExecutionService.IsolateHostFromNetwork(runner);
            Assert.Equal(DefenseActionResult.NotAuthorized, iso.Status);
            Assert.False(iso.Success);
            Assert.Contains("administrator", iso.Message);
            Assert.Equal(DefenseActionResult.NotAuthorized, SystemDefenseExecutionService.RestoreNetworkAccess(runner).Status);
            Assert.Equal(DefenseActionResult.NotAuthorized, SystemDefenseExecutionService.BlockMaliciousIoC("10.0.0.1", runner).Status);
            var (ok, detail) = AuditPolicyChange.EnableBlockedConnectionFailureAudit();
            Assert.False(ok);
            Assert.Contains("administrator", detail);
        }
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void Administrator_session_may_isolate()
    {
        var runner = new CountingRunner();
        using (OperatorIdentity.Scope(Admin("STATIE\\admin"), authenticationRequired: true))
            Assert.NotEqual(DefenseActionResult.NotAuthorized, SystemDefenseExecutionService.IsolateHostFromNetwork(runner).Status);
        Assert.True(runner.Calls > 0);
    }
}
