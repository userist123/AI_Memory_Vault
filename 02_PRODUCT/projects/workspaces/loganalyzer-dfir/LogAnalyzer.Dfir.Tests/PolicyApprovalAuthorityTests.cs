using System.Text.Json;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Independent review, finding 2: with sign-in required, the acting account of approve / apply is the signed-in account (a typed name cannot
/// replace it), and the approval that authorises apply is the one recorded in the sealed authentication audit, not the per-user store.json.
/// </summary>
public sealed class PolicyApprovalAuthorityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-polauth-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static AuthSession Admin(string account) => new(account, "Admin Test", AuthRole.Administrator, false, SignInMode.Card, null, null, Now);

    private const string Yaml = """
        policy:
          id: LA-APPROVAL-TEST
          version: '1.0.0'
          title: Test de aprobare
          author: autor
        controls:
          - id: R1
            title: DWORD care lipsește
            detection: { type: registry, hive: HKLM, key: 'Software\LaApprovalTest', value: Enabled, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
        """;

    private string StorePath => Path.Combine(_dir, "store.json");

    /// <summary>Registered, validated and tested (no sign-in needed for those steps); not approved.</summary>
    private (PolicyExecutor X, FakeSettings Fake, PolicyDocument P, AuthService Auth) Tested()
    {
        var fake = new FakeSettings();
        var x = new PolicyExecutor([fake], Path.Combine(_dir, "evidence"));
        var p = PolicyLoader.Parse(Yaml);
        var store = new PolicyStore(StorePath);
        store.Register(p, "registrator");
        store.Validate(p, "registrator");
        var dry = x.Plan(p);
        store.MarkTested(p, "registrator", dry.Controls.Count - dry.Unreadable, dry.Unreadable);
        return (x, fake, p, new AuthService(Path.Combine(_dir, "auth")));
    }

    private static T As<T>(string account, IPolicyApprovalLedger? ledger, Func<T> step)
    {
        using var _ = OperatorIdentity.Scope(Admin(account), authenticationRequired: true);
        using var __ = PolicyApprovals.Scope(ledger);
        return step();
    }

    [Fact]
    public void Two_accounts_approve_then_apply_and_the_approval_is_in_the_sealed_audit()
    {
        var (x, fake, p, auth) = Tested();
        As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\sef", "revizuită"));
        Assert.Single(auth.Approvals(p.Id, p.Version, p.Sha256), a => a.Approver == "STATIE\\sef");
        Assert.Equal(ChainStatus.Valid, auth.VerifyAudit().Status);
        var store = new PolicyStore(StorePath);
        var plan = x.Plan(p);
        var exec = As("STATIE\\admin2", auth, () => x.Apply(p, plan, store, "STATIE\\admin2", plan.Sha256));
        Assert.Equal(ExecutionStatus.Verified, exec.Status);
        Assert.Equal("STATIE\\sef", exec.Approver);
        Assert.Equal("1", fake.Values.Single().Value.Value);
    }

    [Fact]
    public void A_forged_store_approval_naming_another_account_is_refused_at_apply()
    {
        var (x, fake, p, auth) = Tested();
        // Edit the per-user store directly: APPROVED by another account, nothing in the sealed audit.
        var records = JsonSerializer.Deserialize<List<PolicyRecord>>(File.ReadAllText(StorePath))!;
        var r = records.Single();
        r.History.Add(new PolicyTransition(PolicyStatus.Tested, PolicyStatus.Approved, "STATIE\\sef", Now, "falsificat", p.Sha256));
        r.Status = PolicyStatus.Approved;
        File.WriteAllText(StorePath, JsonSerializer.Serialize(records));
        var store = new PolicyStore(StorePath);
        var plan = x.Plan(p);
        var ex = Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\admin2", auth, () => x.Apply(p, plan, store, "STATIE\\admin2", plan.Sha256)));
        Assert.Contains("jurnalul de autentificare", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.Values);
        Assert.False(File.Exists(x.Audit.Path));
    }

    [Fact]
    public void A_store_approver_that_differs_from_the_sealed_audit_is_refused()
    {
        var (x, fake, p, auth) = Tested();
        As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\sef", "revizuită"));
        // Rewrite the approver in the store to the account that will apply: the audit still names the real approver.
        var records = JsonSerializer.Deserialize<List<PolicyRecord>>(File.ReadAllText(StorePath))!;
        var h = records.Single().History;
        h[^1] = h[^1] with { Operator = "STATIE\\altul" };
        File.WriteAllText(StorePath, JsonSerializer.Serialize(records));
        var store = new PolicyStore(StorePath);
        var plan = x.Plan(p);
        Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\sef", auth, () => x.Apply(p, plan, store, "STATIE\\sef", plan.Sha256)));
        Assert.Empty(fake.Values);
    }

    [Fact]
    public void The_same_signed_in_account_cannot_approve_and_apply()
    {
        var (x, fake, p, auth) = Tested();
        As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\sef", "revizuită"));
        var store = new PolicyStore(StorePath);
        var plan = x.Plan(p);
        // The typed name cannot stand in for the signed-in account.
        Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\sef", auth, () => x.Apply(p, plan, store, "STATIE\\admin2", plan.Sha256)));
        var ex = Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\sef", auth, () => x.Apply(p, plan, store, "", plan.Sha256)));
        Assert.Contains("aprobat", ex.Message);
        Assert.Empty(fake.Values);
    }

    [Fact]
    public void Approval_needs_the_signed_in_account_and_the_sealed_audit()
    {
        var (_, _, p, auth) = Tested();
        // a typed approver other than the signed-in account
        Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\altul", "x")));
        // no ledger registered while sign-in is required
        Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\sef", null, () => new PolicyStore(StorePath).Approve(p, "STATIE\\sef", "x")));
        Assert.Equal(PolicyStatus.Tested, new PolicyStore(StorePath).Get(p)!.Status);
        Assert.Empty(auth.Approvals(p.Id, p.Version, p.Sha256));
    }
}
