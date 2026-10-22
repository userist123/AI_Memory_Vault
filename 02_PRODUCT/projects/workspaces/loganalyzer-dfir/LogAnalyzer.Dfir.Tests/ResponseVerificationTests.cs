using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Response;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.Response.Containment;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// R22.6 / WP9: every controlled response is re-checked after it is applied, by re-reading the system (fakes here, nothing on the host
/// changes). Probes: apply "ok" but the re-read shows the old state -> FAILED; re-read throws / denied / times out -> NOT_VERIFIED with
/// the cause; partial application -> FAILED with the list; rollback re-checked too; every outcome audited, an audit failure surfaced.
/// </summary>
public sealed class ResponseVerificationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-respver-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    // ---------------------------------------------------------------- the shared verifier

    [Fact]
    public void Apply_reported_ok_but_reread_showing_the_old_state_is_failed_not_verified()
    {
        bool applied = false;                 // the "action" says it worked; the system still shows the old state
        var o = ResponseVerifier.Verify("test", "apply",
            [new ResponseProbe("regula A", "există", () => applied ? ResponseObservation.Holds("există") : ResponseObservation.Differs("nu există"))]);
        Assert.Equal(ResponseStatus.Failed, o.Status);
        Assert.False(o.IsSuccess);
        Assert.Contains("regula A", o.Cause);
    }

    public static TheoryData<string, string> ReaderFailures => new()
    {
        { "denied", "acces refuzat" },
        { "unsupported", "nesuportat" },
        { "io", "IOException" },
        { "null", "nu a întors nicio stare" },
        { "timeout", "timeout" },
    };

    [Theory]
    [MemberData(nameof(ReaderFailures))]
    public void Reread_that_throws_is_denied_unsupported_or_times_out_is_not_verified_with_the_cause(string mode, string cause)
    {
        Func<ResponseObservation> read = mode switch
        {
            "denied" => () => throw new UnauthorizedAccessException("fără drepturi"),
            "unsupported" => () => throw new NotSupportedException("API absent"),
            "io" => () => throw new IOException("disc"),
            "null" => () => null!,
            _ => () => { Thread.Sleep(3000); return ResponseObservation.Holds("prea târziu"); },
        };
        var o = ResponseVerifier.Verify("test", "apply", [new ResponseProbe("ținta", "aplicat", read)], TimeSpan.FromMilliseconds(200));
        Assert.Equal(ResponseStatus.NotVerified, o.Status);
        Assert.False(o.IsSuccess);
        Assert.Contains(cause, o.Checks.Single().Observed);
        Assert.Contains(cause, o.Cause);
    }

    [Fact]
    public void Partial_application_two_of_three_is_failed_with_the_list()
    {
        var present = new HashSet<string> { "R1", "R2" };
        var o = ResponseVerifier.Verify("test", "apply", new[] { "R1", "R2", "R3" }.Select(n =>
            new ResponseProbe(n, "există", () => present.Contains(n) ? ResponseObservation.Holds("există") : ResponseObservation.Differs("lipsește"))));
        Assert.Equal(ResponseStatus.Failed, o.Status);
        Assert.Contains("2 din 3", o.Cause);
        Assert.Contains("R3", o.Cause);
        Assert.DoesNotContain("R1 (", o.Cause);
        Assert.Equal(new[] { ResponseStatus.Verified, ResponseStatus.Verified, ResponseStatus.Failed }, o.Checks.Select(c => c.Status));
    }

    [Fact]
    public void A_response_with_nothing_to_recheck_is_not_verified()
    {
        var o = ResponseVerifier.Verify("test", "apply", []);
        Assert.Equal(ResponseStatus.NotVerified, o.Status);
        Assert.False(o.IsSuccess);
    }

    [Fact]
    public void Every_outcome_is_audited_and_an_audit_failure_is_surfaced_not_swallowed()
    {
        var lines = new List<(string A, string D)>();
        foreach (var status in new[] { ResponseStatus.Verified, ResponseStatus.NotVerified, ResponseStatus.Failed })
        {
            var o = ResponseVerifier.Aggregate("test", "apply", [new ResponseCheck("t", "e", "obs-" + status, status)]);
            ResponseVerifier.Record(o, (a, d) => lines.Add((a, d)));
            Assert.Null(o.AuditError);
        }
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(ResponseVerifier.AuditAction, l.A));
        Assert.Contains(lines, l => l.D.Contains("NOT_VERIFIED") && l.D.Contains("obs-NotVerified"));

        var ok = ResponseVerifier.Aggregate("test", "apply", [new ResponseCheck("t", "e", "o", ResponseStatus.Verified)]);
        Assert.True(ok.IsSuccess);
        ResponseVerifier.Record(ok, (_, _) => throw new IOException("disc plin"));
        Assert.False(ok.IsSuccess);                       // verified on the station, but unaudited: not a success
        Assert.Contains("disc plin", ok.AuditError);
        Assert.Contains("AUDIT NESCRIS", ok.StatusText);

        var none = ResponseVerifier.Record(ResponseVerifier.Aggregate("test", "apply", [new ResponseCheck("t", "e", "o", ResponseStatus.Verified)]), null);
        Assert.False(none.IsSuccess);
    }

    [Fact]
    public void Outcome_goes_to_the_hash_chained_case_audit()
    {
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case"),
            new CaseInfo { CaseId = "CASE-RV", Name = "rv", CreatedAtUtc = DateTimeOffset.UtcNow, Timezone = "GTB Standard Time", Scope = TestScopes.Valid() });
        var o = ResponseVerifier.Aggregate("izolare", "apply", [new ResponseCheck("regula X", "există", "nu există", ResponseStatus.Failed)]);
        ResponseVerifier.Record(o, ws.Audit);
        var chain = File.ReadAllText(ws.AuditChainPath);
        Assert.Contains(ResponseVerifier.AuditAction, chain);
        Assert.Contains("regula X", chain);
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Audit.Status);
    }

    // ---------------------------------------------------------------- station-wide firewall (isolation, IoC block)

    private sealed class ScriptedNetsh : IFirewallCommandRunner
    {
        private readonly HashSet<string> _rules = new();
        public int AddExit { get; set; }
        public bool AddCreatesRule { get; set; } = true;
        public bool DeleteRemovesRule { get; set; } = true;
        public int ShowExit { get; set; } = -100;   // -100 = answer from the rule set
        public List<string> Calls { get; } = new();

        public CommandOutcome Netsh(string arguments)
        {
            Calls.Add(arguments);
            var name = arguments.Split("name=\"")[1].Split('"')[0];
            if (arguments.Contains(" add rule ")) { if (AddExit == 0 && AddCreatesRule) _rules.Add(name); return new(AddExit, ""); }
            if (arguments.Contains(" delete rule ")) { if (DeleteRemovesRule) _rules.Remove(name); return new(0, "Ok."); }
            if (ShowExit != -100) return new(ShowExit, "Acces refuzat.");
            return _rules.Contains(name) ? new(0, name) : new(1, "Nicio regulă.");
        }
    }

    [Fact]
    public void Station_isolation_ok_but_rule_absent_on_reread_is_failed()
    {
        var r = SystemDefenseExecutionService.IsolateHostFromNetwork(new ScriptedNetsh { AddCreatesRule = false });
        Assert.Equal(DefenseActionResult.Failed, r.Status);
        Assert.False(r.Success);
        Assert.Equal(ResponseStatus.Failed, r.Verification!.Status);
    }

    [Fact]
    public void Station_reread_denied_is_not_verified_and_never_read_as_absent()
    {
        var fw = new ScriptedNetsh { ShowExit = 5 };
        var iso = SystemDefenseExecutionService.IsolateHostFromNetwork(fw);
        Assert.Equal(DefenseActionResult.NotVerified, iso.Status);
        Assert.False(iso.Success);
        Assert.Contains("5", iso.Verification!.Cause);

        // Rollback: a denied re-read must not be taken for "the rule is already gone".
        var restore = SystemDefenseExecutionService.RestoreNetworkAccess(fw);
        Assert.Equal(DefenseActionResult.NotVerified, restore.Status);
        Assert.False(restore.Success);
        Assert.Contains(fw.Calls, c => c.Contains(" delete rule "));
    }

    [Fact]
    public void Station_rollback_is_verified_by_reread_and_a_stuck_rule_is_failed()
    {
        var fw = new ScriptedNetsh();
        Assert.True(SystemDefenseExecutionService.IsolateHostFromNetwork(fw).Success);
        var ok = SystemDefenseExecutionService.RestoreNetworkAccess(fw);
        Assert.Equal(DefenseActionResult.Verified, ok.Status);
        Assert.Equal("rollback", ok.Verification!.Phase);
        Assert.Equal(ResponseStatus.Verified, ok.Verification.Status);

        var stuck = new ScriptedNetsh { DeleteRemovesRule = false };
        SystemDefenseExecutionService.IsolateHostFromNetwork(stuck);
        var bad = SystemDefenseExecutionService.RestoreNetworkAccess(stuck);
        Assert.Equal(DefenseActionResult.Failed, bad.Status);
        Assert.False(bad.Success);
    }

    [Fact]
    public void Station_result_is_audited_and_an_unaudited_success_is_not_a_success()
    {
        var lines = new List<string>();
        var r = SystemDefenseExecutionService.BlockMaliciousIoC("203.0.113.9", new ScriptedNetsh()).RecordTo((a, d) => lines.Add(a + " " + d));
        Assert.True(r.Success);
        Assert.Contains(lines, l => l.Contains("VERIFIED") && l.Contains("DFIR_BLOCK_IOC 203.0.113.9"));

        var rejected = SystemDefenseExecutionService.BlockMaliciousIoC("nu-e-ip", new ScriptedNetsh()).RecordTo((a, d) => lines.Add(a + " " + d));
        Assert.False(rejected.Success);
        Assert.Contains(lines, l => l.Contains("REJECTED"));

        var unaudited = SystemDefenseExecutionService.IsolateHostFromNetwork(new ScriptedNetsh()).RecordTo((_, _) => throw new UnauthorizedAccessException("jurnal blocat"));
        Assert.False(unaudited.Success);
        Assert.Contains("jurnal blocat", unaudited.Message);
        Assert.Contains("jurnal blocat", unaudited.Verification!.AuditError);
    }

    [Fact]
    public void A_verified_claim_without_recheck_evidence_is_not_accepted()
    {
        var forged = new DefenseActionResult { Success = true, Status = DefenseActionResult.Verified, Message = "gata" }.RecordTo((_, _) => { });
        Assert.False(forged.Success);
        Assert.Equal(DefenseActionResult.NotVerified, forged.Status);
    }

    // ---------------------------------------------------------------- audit-policy change

    [Fact]
    public void Audit_policy_change_is_verified_by_rereading_the_effective_policy()
    {
        var lines = new List<string>();
        var ok = AuditPolicyChange.EnableBlockedConnectionFailureAudit((a, d) => lines.Add(d), apply: () => (0, "ok"), reread: () => true);
        Assert.True(ok.Ok);
        Assert.Equal(ResponseStatus.Verified, ok.Verification.Status);

        var stale = AuditPolicyChange.EnableBlockedConnectionFailureAudit((a, d) => lines.Add(d), apply: () => (0, "ok"), reread: () => false);
        Assert.False(stale.Ok);
        Assert.Equal(ResponseStatus.Failed, stale.Verification.Status);

        var denied = AuditPolicyChange.EnableBlockedConnectionFailureAudit((a, d) => lines.Add(d), apply: () => (0, "ok"),
            reread: () => throw new System.ComponentModel.Win32Exception(5, "Access is denied"));
        Assert.False(denied.Ok);
        Assert.Equal(ResponseStatus.NotVerified, denied.Verification.Status);
        Assert.Contains("Access is denied", denied.Verification.Cause);

        var failed = AuditPolicyChange.EnableBlockedConnectionFailureAudit((a, d) => lines.Add(d), apply: () => (87, "parametru"), reread: () => false);
        Assert.Equal(ResponseStatus.Failed, failed.Verification.Status);

        Assert.Equal(4, lines.Count);   // every outcome, NOT_VERIFIED included
        var unaudited = AuditPolicyChange.EnableBlockedConnectionFailureAudit((_, _) => throw new IOException("jurnal"), apply: () => (0, ""), reread: () => true);
        Assert.False(unaudited.Ok);
        Assert.Contains("jurnal", unaudited.Detail);
    }

    // ---------------------------------------------------------------- policy execution (apply + rollback)

    private sealed class Settings : ISettingProvider
    {
        public Dictionary<string, SettingValue> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IgnoreWrites { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>A setting written after this is armed can no longer be read (the re-read after the write is denied).</summary>
        public bool ReadThrowsAfterWrite { get; set; }
        private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);
        public void ArmRereadFailure() { _written.Clear(); ReadThrowsAfterWrite = true; }
        public bool Handles(SettingRef s) => s.Type == "registry";
        public SettingValue Read(SettingRef s)
        {
            if (ReadThrowsAfterWrite && _written.Contains(s.Display)) throw new UnauthorizedAccessException("re-citire refuzată");
            return Values.GetValueOrDefault(s.Display) ?? SettingValue.Absent;
        }
        public string? CannotWrite(SettingRef s) => null;
        public void Write(SettingRef s, string value)
        {
            _written.Add(s.Display);
            if (!IgnoreWrites.Contains(s.Name)) Values[s.Display] = new SettingValue(value, s.ValueType);
        }
        public void Delete(SettingRef s) { _written.Add(s.Display); if (!IgnoreWrites.Contains(s.Name)) Values.Remove(s.Display); }
    }

    private const string ThreeControls = """
        policy:
          id: LA-RESPVER
          version: '1.0.0'
          title: Verificarea răspunsului
          author: autor
        controls:
          - id: R1
            title: Unu
            detection: { type: registry, hive: HKLM, key: 'Software\LaRespVer', value: A, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
          - id: R2
            title: Doi
            detection: { type: registry, hive: HKLM, key: 'Software\LaRespVer', value: B, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
          - id: R3
            title: Trei
            detection: { type: registry, hive: HKLM, key: 'Software\LaRespVer', value: C, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
        """;

    private (PolicyExecutor X, PolicyDocument P, PolicyStore S, PolicyPlan Plan) Ready(Settings fake, string name)
    {
        var dir = Path.Combine(_dir, name);
        var x = new PolicyExecutor([fake], dir, "STATIE-T");
        var p = PolicyLoader.Parse(ThreeControls);
        var store = new PolicyStore(Path.Combine(dir, "store.json"));
        store.Register(p, "registrator");
        store.Validate(p, "registrator");
        var dry = x.Plan(p);
        store.MarkTested(p, "registrator", dry.Controls.Count - dry.Unreadable, dry.Unreadable);
        store.Approve(p, "aprobator", "test");
        return (x, p, store, x.Plan(p));
    }

    [Fact]
    public void Policy_partial_application_two_of_three_is_failed_with_the_list_and_every_control_is_audited()
    {
        var fake = new Settings();
        fake.IgnoreWrites.Add("C");
        var (x, p, store, plan) = Ready(fake, "partial");
        var exec = x.Apply(p, plan, store, "operator", plan.Sha256);
        Assert.Equal(ExecutionStatus.Failed, exec.Status);
        var v = exec.Verification;
        Assert.Equal(ResponseStatus.Failed, v.Status);
        Assert.Contains("2 din 3", v.Cause);
        Assert.Contains(@"LaRespVer\C (", v.Cause);
        Assert.DoesNotContain(@"LaRespVer\A (", v.Cause);
        Assert.Equal(3, x.Audit.Read().Count);
        Assert.Empty(x.Audit.VerifyChain());
    }

    [Fact]
    public void Policy_reread_failure_is_not_verified_on_apply_and_on_rollback()
    {
        var fake = new Settings { ReadThrowsAfterWrite = true };
        var (x, p, store, plan) = Ready(fake, "reread");
        var exec = x.Apply(p, plan, store, "operator", plan.Sha256);
        Assert.Equal(ExecutionStatus.NotVerified, exec.Status);
        Assert.Equal(ResponseStatus.NotVerified, exec.Verification.Status);
        Assert.All(x.Audit.Read(), a => Assert.Equal("NotVerified", a.Outcome));   // NOT_VERIFIED is audited too

        // Rollback: the setting is restored, then the re-read is denied -> NOT_VERIFIED, never "rolled back".
        var fake2 = new Settings();
        var (x2, p2, store2, plan2) = Ready(fake2, "rb");
        var applied = x2.Apply(p2, plan2, store2, "operator", plan2.Sha256);
        Assert.Equal(ResponseStatus.Verified, applied.Verification.Status);
        fake2.ArmRereadFailure();   // the pre-read of the rollback works; the re-read after the restoring write is denied
        var rb = x2.Rollback(p2, applied.ExecutionId, "operator");
        Assert.Equal(ExecutionStatus.NotVerified, rb.Status);
        Assert.Equal(ResponseStatus.NotVerified, rb.Verification.Status);
        Assert.All(rb.Results, r => Assert.Equal(ControlOutcome.RollbackNotVerified, r.Outcome));
        Assert.Contains("re-citire refuzată", rb.Results[0].Detail);
        Assert.Contains(x2.Audit.Read(), a => a.Kind == "rollback" && a.Outcome == "RollbackNotVerified");
    }

    [Fact]
    public void Policy_rollback_is_verified_by_reread_and_an_ignored_restore_is_failed()
    {
        var fake = new Settings();
        var (x, p, store, plan) = Ready(fake, "rbok");
        var applied = x.Apply(p, plan, store, "operator", plan.Sha256);
        var rb = x.Rollback(p, applied.ExecutionId, "operator");
        Assert.Equal(ExecutionStatus.Verified, rb.Status);
        Assert.Equal(ResponseStatus.Verified, rb.Verification.Status);
        Assert.Equal("rollback", rb.Verification.Phase);

        var stuck = new Settings();
        var (y, q, store2, plan2) = Ready(stuck, "rbstuck");
        var applied2 = y.Apply(q, plan2, store2, "operator", plan2.Sha256);
        stuck.IgnoreWrites.Add("B");
        var rb2 = y.Rollback(q, applied2.ExecutionId, "operator");
        Assert.Equal(ExecutionStatus.Failed, rb2.Status);
        Assert.Equal(ResponseStatus.Failed, rb2.Verification.Status);
        Assert.Contains("2 din 3", rb2.Verification.Cause);
        Assert.Contains(y.Audit.Read(), a => a.Kind == "rollback" && a.Outcome == "RollbackFailed");
    }

    // ---------------------------------------------------------------- media sanitization

    [Fact]
    public async Task Sanitization_is_verified_only_by_its_read_back()
    {
        Directory.CreateDirectory(_dir);
        var f = Path.Combine(_dir, "wipe.bin");
        File.WriteAllBytes(f, Enumerable.Repeat((byte)0xAB, 4096).ToArray());
        var zero = await new MediaSanitizationEngine().SanitizeMediaAsync(f, SanitizationMethod.NistClearZero);
        Assert.Equal(ResponseStatus.Verified, zero.Verification.Status);

        File.WriteAllBytes(f, Enumerable.Repeat((byte)0xAB, 4096).ToArray());
        var random = await new MediaSanitizationEngine().SanitizeMediaAsync(f, SanitizationMethod.NistClearRandom);
        Assert.Equal(ResponseStatus.NotVerified, random.Verification.Status);

        var missing = await new MediaSanitizationEngine().SanitizeMediaAsync(Path.Combine(_dir, "nu-exista.bin"), SanitizationMethod.NistClearZero);
        Assert.Equal(ResponseStatus.Failed, missing.Verification.Status);
    }
}
