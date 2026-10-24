using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review of the remediations (findings 1-3): a process running as the same Windows user can rewrite store.json, execution records and their
/// .sha256 sidecars; only the keyed, sealed authentication audit is trusted. Registration, approval, application, rollback and retirement of a
/// policy are therefore recorded there, and rollback / approval / apply consult it instead of the per-user files.
/// </summary>
public sealed class PolicySealedLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-polseal-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private const string Yaml = """
        policy:
          id: LA-SEALED-TEST
          version: '1.0.0'
          title: Test de ciclu sigilat
          author: 'STATIE\autor'
        controls:
          - id: R1
            title: DWORD care lipsește
            detection: { type: registry, hive: HKLM, key: 'Software\LaSealedTest', value: Enabled, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
        """;

    private string StorePath => Path.Combine(_dir, "store.json");
    private string EvidenceDir => Path.Combine(_dir, "evidence");

    private static AuthSession Admin(string account) => new(account, "Admin Test", AuthRole.Administrator, false, SignInMode.Card, null, null, Now);

    private static T As<T>(string account, IPolicyApprovalLedger? ledger, Func<T> step)
    {
        using var _ = OperatorIdentity.Scope(Admin(account), authenticationRequired: true);
        using var __ = PolicyApprovals.Scope(ledger);
        return step();
    }

    /// <summary>Registered (sealed) by STATIE\registrator, validated and tested.</summary>
    private (PolicyExecutor X, FakeSettings Fake, PolicyDocument P, AuthService Auth) Tested()
    {
        var fake = new FakeSettings();
        var x = new PolicyExecutor([fake], EvidenceDir);
        var p = PolicyLoader.Parse(Yaml);
        var auth = new AuthService(Path.Combine(_dir, "auth"));
        As("STATIE\\registrator", auth, () =>
        {
            var store = new PolicyStore(StorePath);
            store.Register(p, "STATIE\\registrator");
            store.Validate(p, "STATIE\\registrator");
            var dry = x.Plan(p);
            return store.MarkTested(p, "STATIE\\registrator", dry.Controls.Count - dry.Unreadable, dry.Unreadable);
        });
        return (x, fake, p, auth);
    }

    private (PolicyExecutor X, FakeSettings Fake, PolicyDocument P, AuthService Auth, ExecutionRecord Exec) Applied()
    {
        var (x, fake, p, auth) = Tested();
        As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\sef", "revizuită"));
        var plan = x.Plan(p);
        var exec = As("STATIE\\admin2", auth, () => x.Apply(p, plan, new PolicyStore(StorePath), "STATIE\\admin2", plan.Sha256));
        Assert.Equal(ExecutionStatus.Verified, exec.Status);
        return (x, fake, p, auth, exec);
    }

    private static string[] Snapshot(string dir) => Directory.Exists(dir)
        ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => $"{f}|{new FileInfo(f).Length}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}").OrderBy(s => s).ToArray()
        : [];

    private static int AuditLines(AuthService auth, string ev) => File.ReadLines(auth.AuditPath).Count(l => l.Contains($"\"{ev}\""));

    // ---- finding 1: rollback ----

    [Fact]
    public void Apply_seals_the_execution_and_a_genuine_rollback_is_allowed_and_sealed()
    {
        var (x, fake, p, auth, exec) = Applied();
        Assert.Equal(1, AuditLines(auth, AuthService.PolicyAppliedEvent));
        var rb = As("STATIE\\admin2", auth, () => x.Rollback(p, exec.ExecutionId, "STATIE\\admin2", new PolicyStore(StorePath)));
        Assert.Equal(ExecutionStatus.Verified, rb.Status);
        Assert.Empty(fake.Values);                                    // the value did not exist before: deleted again
        Assert.Equal(1, AuditLines(auth, AuthService.PolicyRolledBackEvent));
        Assert.Equal(ChainStatus.Valid, auth.VerifyAudit().Status);
    }

    [Fact]
    public void A_forged_execution_record_with_a_recomputed_sidecar_is_refused_and_nothing_is_written()
    {
        var (x, fake, p, auth, exec) = Applied();
        // The attacker edits "Before" so that rollback would write a value of their choosing, and recomputes the .sha256 sidecar.
        var path = Path.Combine(EvidenceDir, "executions", exec.ExecutionId + ".json");
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        node["Results"]![0]!["Before"] = new JsonObject { ["Value"] = "0", ["Kind"] = "dword" };
        var forged = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, forged, new UTF8Encoding(false));
        File.WriteAllText(path + ".sha256", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(forged))) + "\n");
        Assert.Equal("0", x.Load(exec.ExecutionId).Results[0].Before!.Value); // the sidecar alone no longer catches it

        var before = Snapshot(EvidenceDir);
        var ex = Assert.Throws<PolicyLifecycleException>(() =>
            As("STATIE\\admin2", auth, () => x.Rollback(p, exec.ExecutionId, "STATIE\\admin2", new PolicyStore(StorePath))));
        Assert.Contains("jurnalul de autentificare", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nu s-a schimbat nimic", ex.Message);
        Assert.Equal("1", fake.Values.Single().Value.Value);
        Assert.Equal(before, Snapshot(EvidenceDir));
        Assert.Equal(0, AuditLines(auth, AuthService.PolicyRolledBackEvent));
    }

    [Fact]
    public void A_fabricated_execution_record_is_refused()
    {
        var (x, fake, p, auth, exec) = Applied();
        var path = Path.Combine(EvidenceDir, "executions", exec.ExecutionId + ".json");
        var fabricated = Path.Combine(EvidenceDir, "executions", "20990101T000000Z-deadbeef.json");
        var text = File.ReadAllText(path).Replace(exec.ExecutionId, "20990101T000000Z-deadbeef");
        File.WriteAllText(fabricated, text, new UTF8Encoding(false));
        File.WriteAllText(fabricated + ".sha256", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))) + "\n");
        Assert.Throws<PolicyLifecycleException>(() =>
            As("STATIE\\admin2", auth, () => x.Rollback(p, "20990101T000000Z-deadbeef", "STATIE\\admin2", new PolicyStore(StorePath))));
        Assert.Equal("1", fake.Values.Single().Value.Value);
    }

    [Fact]
    public void Rollback_needs_the_policy_still_approved_and_the_store()
    {
        var (x, fake, p, auth, exec) = Applied();
        // without the store the approval cannot be checked
        Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\admin2", auth, () => x.Rollback(p, exec.ExecutionId, "STATIE\\admin2")));
        As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Retire(p, "STATIE\\sef", "retrasă"));
        Assert.Throws<PolicyLifecycleException>(() =>
            As("STATIE\\admin2", auth, () => x.Rollback(p, exec.ExecutionId, "STATIE\\admin2", new PolicyStore(StorePath))));
        Assert.Equal("1", fake.Values.Single().Value.Value);
    }

    // ---- finding 2: retirement ----

    [Fact]
    public void Retirement_is_sealed_and_a_retired_policy_set_back_to_approved_in_the_store_is_refused()
    {
        var (x, fake, p, auth) = Tested();
        As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\sef", "revizuită"));
        As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Retire(p, "STATIE\\sef", "retrasă"));
        Assert.Equal(1, AuditLines(auth, AuthService.PolicyRetiredEvent));
        Assert.Empty(auth.Approvals(p.Id, p.Version, p.Sha256));      // the retirement cancels the earlier approval

        var records = JsonSerializer.Deserialize<List<PolicyRecord>>(File.ReadAllText(StorePath))!;
        records.Single().Status = PolicyStatus.Approved;
        File.WriteAllText(StorePath, JsonSerializer.Serialize(records));
        var plan = x.Plan(p);
        var ex = Assert.Throws<PolicyLifecycleException>(() =>
            As("STATIE\\admin2", auth, () => x.Apply(p, plan, new PolicyStore(StorePath), "STATIE\\admin2", plan.Sha256)));
        Assert.Contains("jurnalul de autentificare", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.Values);
    }

    // ---- finding 3: self-approval ----

    [Fact]
    public void The_author_who_rewrites_the_store_fields_still_cannot_approve_the_own_policy()
    {
        var (_, _, p, auth) = Tested();
        var records = JsonSerializer.Deserialize<List<PolicyRecord>>(File.ReadAllText(StorePath))!;
        var r = records.Single();
        var forged = new PolicyRecord { Id = r.Id, Version = r.Version, Sha256 = r.Sha256, Author = "STATIE\\altcineva", RegisteredBy = "STATIE\\altcineva", Status = r.Status, History = r.History };
        File.WriteAllText(StorePath, JsonSerializer.Serialize(new List<PolicyRecord> { forged }));
        var ex = Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\autor", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\autor", "a mea")));
        Assert.Contains("Auto-aprobarea", ex.Message);
        Assert.Empty(auth.Approvals(p.Id, p.Version, p.Sha256));
    }

    [Fact]
    public void The_registering_account_recorded_in_the_sealed_audit_cannot_approve_even_if_the_store_names_another()
    {
        var (_, _, p, auth) = Tested();
        Assert.Equal(1, AuditLines(auth, AuthService.PolicyRegisteredEvent));
        var records = JsonSerializer.Deserialize<List<PolicyRecord>>(File.ReadAllText(StorePath))!;
        var r = records.Single();
        var forged = new PolicyRecord { Id = r.Id, Version = r.Version, Sha256 = r.Sha256, Author = r.Author, RegisteredBy = "STATIE\\altcineva", Status = r.Status, History = r.History };
        File.WriteAllText(StorePath, JsonSerializer.Serialize(new List<PolicyRecord> { forged }));
        var ex = Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\registrator", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\registrator", "a mea")));
        Assert.Contains("Auto-aprobarea", ex.Message);
        Assert.Empty(auth.Approvals(p.Id, p.Version, p.Sha256));
    }

    [Fact]
    public void A_registration_missing_from_the_sealed_audit_cannot_be_approved()
    {
        var p = PolicyLoader.Parse(Yaml);
        var auth = new AuthService(Path.Combine(_dir, "auth"));
        var store = new PolicyStore(StorePath);                       // registered outside the application: no sealed registration
        store.Register(p, "STATIE\\registrator");
        store.Validate(p, "STATIE\\registrator");
        store.MarkTested(p, "STATIE\\registrator", 1, 0);
        var ex = Assert.Throws<PolicyLifecycleException>(() => As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\sef", "ok")));
        Assert.Contains("înregistr", ex.Message);
        Assert.Equal(PolicyStatus.Tested, new PolicyStore(StorePath).Get(p)!.Status);
        // Registering again from the application seals it; then another administrator may approve.
        As("STATIE\\registrator", auth, () => new PolicyStore(StorePath).Register(p, "STATIE\\registrator"));
        As("STATIE\\sef", auth, () => new PolicyStore(StorePath).Approve(p, "STATIE\\sef", "ok"));
        Assert.Single(auth.Approvals(p.Id, p.Version, p.Sha256));
    }
}
