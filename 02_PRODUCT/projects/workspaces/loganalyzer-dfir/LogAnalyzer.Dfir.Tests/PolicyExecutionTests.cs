using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Windows.Policy;
using Microsoft.Win32;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Settings kept in memory, with switches for the failure modes a real station can show.</summary>
public sealed class FakeSettings : ISettingProvider
{
    public Dictionary<string, SettingValue> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool WriteThrows { get; set; }
    public bool WriteIgnored { get; set; }
    public bool RereadThrows { get; set; }
    private bool _written;

    public bool Handles(SettingRef s) => s.Type == "registry";
    public SettingValue Read(SettingRef s)
    {
        if (RereadThrows && _written) throw new IOException("re-citire imposibilă");
        return Values.GetValueOrDefault(s.Display) ?? SettingValue.Absent;
    }
    public string? CannotWrite(SettingRef s) => s.ValueType == "multi_string" ? "multi_string nu se scrie" : null;
    public void Write(SettingRef s, string value)
    {
        if (WriteThrows) throw new UnauthorizedAccessException("acces refuzat");
        _written = true;
        if (!WriteIgnored) Values[s.Display] = new SettingValue(value, s.ValueType);
    }
    public void Delete(SettingRef s) => Values.Remove(s.Display);
}

[Collection("HKCU sandbox")] // share HKCU\Software\LogAnalyzerDfirTest; never run in parallel
public sealed class PolicyExecutionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_exec_" + Guid.NewGuid().ToString("N"));
    private readonly string _key = @"Software\LogAnalyzerDfirTest\" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\LogAnalyzerDfirTest"))
            if (parent is { SubKeyCount: 0, ValueCount: 0 }) { parent.Dispose(); Registry.CurrentUser.DeleteSubKey(@"Software\LogAnalyzerDfirTest", throwOnMissingSubKey: false); }
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Yaml(string version = "1.0.0") => $$"""
        policy:
          id: LA-EXEC-TEST
          version: '{{version}}'
          title: Test de execuție
          author: autor
        controls:
          - id: R1
            title: DWORD care lipsește
            detection: { type: registry, hive: HKCU, key: '{{_key}}', value: Enabled, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
          - id: R2
            title: Șir cu altă valoare
            detection: { type: registry, hive: HKCU, key: '{{_key}}', value: Mode, value_type: string }
            desired_state: { equals: 'strict' }
            remediation: set
            verification: reread
          - id: R3
            title: Listă (doar citită)
            detection: { type: registry, hive: HKCU, key: '{{_key}}', value: List, value_type: multi_string }
            desired_state: { equals: 'a b' }
            remediation: set
            verification: reread
          - id: R4
            title: Valoare care trebuie să lipsească
            detection: { type: registry, hive: HKCU, key: '{{_key}}', value: Legacy, value_type: dword }
            desired_state: { absent: 'true' }
            remediation: manual
            verification: reread
        """;

    /// <summary>Registers, validates, dry-runs and approves (by someone else) a policy.</summary>
    private (PolicyDocument, PolicyStore) Approved(PolicyExecutor x, string yaml)
    {
        var p = PolicyLoader.Parse(yaml);
        var store = new PolicyStore(Path.Combine(_dir, "store.json"));
        store.Register(p, "registrator");
        store.Validate(p, "registrator");
        var dry = x.Plan(p);
        store.MarkTested(p, "registrator", dry.Controls.Count - dry.Unreadable, dry.Unreadable);
        store.Approve(p, "aprobator", "schimbare aprobată pentru test");
        return (p, store);
    }

    private static string? RegQuery(string key, string value)
    {
        var psi = new ProcessStartInfo("reg.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "query", @"HKCU\" + key, "/v", value }) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var text = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        var m = Regex.Match(text, $@"^\s+{Regex.Escape(value)}\s+(REG_\w+)\s+(.*)$", RegexOptions.Multiline);
        return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value.Trim()}" : null;
    }

    [Fact]
    public void Approved_policy_is_applied_on_hkcu_verified_by_reg_and_rolled_back()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_key)) { k.SetValue("Mode", "lax"); k.SetValue("List", new[] { "x" }, RegistryValueKind.MultiString); }
        var x = new PolicyExecutor([new RegistrySettingProvider()], _dir);
        var (p, store) = Approved(x, Yaml());

        var plan = x.Plan(p);
        Assert.Equal((2, 1, 0, 1), (plan.ToChange, plan.Manual, plan.Unreadable, plan.Compliant));
        Assert.Equal(SettingValue.Absent, plan.Controls[0].Current);
        Assert.Equal(new SettingValue("lax", "string"), plan.Controls[1].Current);
        Assert.Contains("nu este scris", plan.Controls[2].Reason);

        var exec = x.Apply(p, plan, store, "operator", plan.Sha256);
        Assert.Equal(ExecutionStatus.Verified, exec.Status);
        Assert.False(exec.PolicyCompliantAfter); // R3 still needs a manual change
        Assert.Equal(new[] { ControlOutcome.Verified, ControlOutcome.Verified, ControlOutcome.ManualRequired, ControlOutcome.Compliant }, exec.Results.Select(r => r.Outcome));
        Assert.Equal(("operator", "aprobator", p.Sha256), (exec.Operator, exec.Approver, exec.PolicySha256));
        Assert.Equal("REG_DWORD 0x1", RegQuery(_key, "Enabled"));
        Assert.Equal("REG_SZ strict", RegQuery(_key, "Mode"));
        Assert.Equal(PolicyStatus.Deployed, store.Get(p)!.Status); // not VERIFIED: the policy is not fully compliant

        var audit = x.Audit.Read();
        Assert.Equal(2, audit.Count);
        Assert.Equal(("R2", "lax (string)", "set strict", "strict (string)", "Verified"), (audit[1].ControlId, audit[1].Before, audit[1].Action, audit[1].After, audit[1].Outcome));
        Assert.Empty(x.Audit.VerifyChain());
        Assert.Equal(exec, x.Load(exec.ExecutionId), new ExecutionComparer());

        var rb = x.Rollback(p, exec.ExecutionId, "operator");
        Assert.Equal(ExecutionStatus.Verified, rb.Status);
        Assert.All(rb.Results, r => Assert.Equal(ControlOutcome.RolledBack, r.Outcome));
        Assert.Null(RegQuery(_key, "Enabled"));
        Assert.Equal("REG_SZ lax", RegQuery(_key, "Mode"));
        Assert.Equal(4, x.Audit.Read().Count);
        Assert.Empty(x.Audit.VerifyChain());
    }

    private sealed class ExecutionComparer : IEqualityComparer<ExecutionRecord>
    {
        public bool Equals(ExecutionRecord? a, ExecutionRecord? b) =>
            a!.ExecutionId == b!.ExecutionId && a.PlanSha256 == b.PlanSha256 && a.Results.SequenceEqual(b.Results);
        public int GetHashCode(ExecutionRecord e) => e.ExecutionId.GetHashCode();
    }

    [Fact]
    public void Fully_compliant_after_apply_moves_the_policy_to_verified()
    {
        var fake = new FakeSettings();
        var yaml = Yaml().Split("  - id: R3")[0];
        var x = new PolicyExecutor([fake], _dir);
        var (p, store) = Approved(x, yaml);
        var plan = x.Plan(p);
        var exec = x.Apply(p, plan, store, "operator", plan.Sha256);
        Assert.True(exec.PolicyCompliantAfter);
        Assert.Equal(PolicyStatus.Verified, store.Get(p)!.Status);
        Assert.Equal(new[] { PolicyStatus.Draft, PolicyStatus.Validated, PolicyStatus.Tested, PolicyStatus.Approved, PolicyStatus.Deployed, PolicyStatus.Verified },
            store.Get(p)!.History.Select(h => h.To));
    }

    [Fact]
    public void Nothing_is_applied_before_approval()
    {
        var x = new PolicyExecutor([new FakeSettings()], _dir);
        var p = PolicyLoader.Parse(Yaml());
        var store = new PolicyStore(Path.Combine(_dir, "store.json"));
        store.Register(p, "registrator");
        var plan = x.Plan(p);
        Assert.Throws<PolicyLifecycleException>(() => x.Apply(p, plan, store, "operator", plan.Sha256)); // DRAFT
        store.Validate(p, "registrator");
        Assert.Throws<PolicyLifecycleException>(() => x.Apply(p, plan, store, "operator", plan.Sha256)); // VALIDATED
        store.MarkTested(p, "registrator", 4, 0);
        Assert.Throws<PolicyLifecycleException>(() => x.Apply(p, plan, store, "operator", plan.Sha256)); // TESTED
        Assert.Throws<PolicyLifecycleException>(() => store.Approve(p, "registrator", "eu")); // no self-approval
        Assert.Empty(x.Audit.Read());
    }

    [Fact]
    public void Changed_policy_tampered_plan_or_unconfirmed_plan_blocks_apply()
    {
        var fake = new FakeSettings();
        var x = new PolicyExecutor([fake], _dir);
        var (p, store) = Approved(x, Yaml());
        var plan = x.Plan(p);

        var edited = PolicyLoader.Parse(Yaml().Replace("equals: 'strict'", "equals: 'off'"));
        Assert.Throws<PolicyLifecycleException>(() => x.Apply(edited, plan, store, "operator", plan.Sha256)); // hash mismatch in store
        var forged = plan with { Controls = plan.Controls.Select(c => c with { DesiredValue = "0" }).ToList() };
        Assert.Throws<PolicyLifecycleException>(() => x.Apply(p, forged, store, "operator", plan.Sha256)); // plan changed after hashing
        Assert.Throws<PolicyLifecycleException>(() => x.Apply(p, plan, store, "operator", new string('0', 64))); // not what the operator saw
        Assert.Throws<PolicyLifecycleException>(() => x.Apply(p, plan, store, " ", plan.Sha256));
        Assert.Throws<PolicyLifecycleException>(() => new PolicyExecutor([fake], _dir, "ALTA-STATIE").Apply(p, plan, store, "operator", plan.Sha256));
        Assert.Empty(fake.Values);
        Assert.Empty(x.Audit.Read());
    }

    [Fact]
    public void Failed_write_unreadable_result_and_ignored_write_are_never_reported_as_verified()
    {
        foreach (var (mode, expected, status, deployed) in new[]
        {
            ("throws", ControlOutcome.Failed, ExecutionStatus.Failed, false),
            ("ignored", ControlOutcome.Failed, ExecutionStatus.Failed, true),
            ("reread", ControlOutcome.NotVerified, ExecutionStatus.NotVerified, true),
        })
        {
            var dir = Path.Combine(_dir, mode);
            var fake = new FakeSettings();
            var x = new PolicyExecutor([fake], dir);
            var p = PolicyLoader.Parse(Yaml());
            var store = new PolicyStore(Path.Combine(dir, "store.json"));
            store.Register(p, "registrator"); store.Validate(p, "registrator"); store.MarkTested(p, "registrator", 4, 0); store.Approve(p, "aprobator", "test");
            var plan = x.Plan(p);
            (fake.WriteThrows, fake.WriteIgnored, fake.RereadThrows) = (mode == "throws", mode == "ignored", mode == "reread");

            var exec = x.Apply(p, plan, store, "operator", plan.Sha256);
            Assert.Equal(expected, exec.Results[0].Outcome);
            Assert.Equal(status, exec.Status);
            Assert.False(exec.PolicyCompliantAfter);
            Assert.Equal(deployed ? PolicyStatus.Deployed : PolicyStatus.Approved, store.Get(p)!.Status);
            Assert.DoesNotContain(x.Audit.Read(), a => a.Outcome == "Verified");
        }
    }

    [Fact]
    public void A_setting_changed_after_the_plan_is_not_overwritten()
    {
        var fake = new FakeSettings();
        var x = new PolicyExecutor([fake], _dir);
        var (p, store) = Approved(x, Yaml());
        var plan = x.Plan(p);
        var r1 = p.Controls[0].Setting.Display;
        fake.Values[r1] = new SettingValue("7", "dword"); // someone else changed it meanwhile
        var exec = x.Apply(p, plan, store, "operator", plan.Sha256);
        Assert.Equal(ControlOutcome.Stale, exec.Results[0].Outcome);
        Assert.Equal("7", fake.Values[r1].Value);
        Assert.Equal(ControlOutcome.Verified, exec.Results[1].Outcome);

        fake.Values[p.Controls[1].Setting.Display] = new SettingValue("changed-again", "string");
        var rb = x.Rollback(p, exec.ExecutionId, "operator");
        Assert.Equal(ControlOutcome.Stale, rb.Results.Single().Outcome);
        Assert.Equal("changed-again", fake.Values[p.Controls[1].Setting.Display].Value);
    }

    [Fact]
    public void Edited_audit_line_or_execution_record_is_detected()
    {
        var x = new PolicyExecutor([new FakeSettings()], _dir);
        var (p, store) = Approved(x, Yaml());
        var plan = x.Plan(p);
        var exec = x.Apply(p, plan, store, "operator", plan.Sha256);

        var log = x.Audit.Path;
        var lines = File.ReadAllLines(log);
        lines[0] = lines[0].Replace("\"Operator\":\"operator\"", "\"Operator\":\"altcineva\"");
        File.WriteAllLines(log, lines);
        Assert.Equal([2], x.Audit.VerifyChain());

        var rec = Path.Combine(_dir, "executions", exec.ExecutionId + ".json");
        File.WriteAllText(rec, File.ReadAllText(rec).Replace("\"operator\"", "\"altcineva\""));
        Assert.Throws<PolicyLifecycleException>(() => x.Load(exec.ExecutionId));
        Assert.Throws<PolicyLifecycleException>(() => x.Rollback(p, exec.ExecutionId, "operator"));
    }

    // ---- providers against independent readers on this station ----

    [Fact]
    public void Account_policy_provider_matches_net_accounts()
    {
        var psi = new ProcessStartInfo("net.exe", "accounts") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = Process.Start(psi)!;
        var text = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        string Net(string label)
        {
            var m = Regex.Match(text, $@"^{Regex.Escape(label)}:?\s+(\S+)", RegexOptions.Multiline);
            Assert.True(m.Success, $"„{label}” lipsește din net accounts (altă limbă a sistemului?)");
            return m.Groups[1].Value switch { "Unlimited" => "-1", "Never" => "0", "None" => "0", var v => v };
        }
        var provider = new AccountPolicyProvider();
        string Ours(string name) => provider.Read(new SettingRef("secpol", "", "", name, "")).Value!;
        Assert.Equal(Net("Minimum password age (days)"), Ours("MinimumPasswordAge"));
        Assert.Equal(Net("Maximum password age (days)"), Ours("MaximumPasswordAge"));
        Assert.Equal(Net("Minimum password length"), Ours("MinimumPasswordLength"));
        Assert.Equal(Net("Length of password history maintained"), Ours("PasswordHistorySize"));
        Assert.Equal(Net("Lockout threshold"), Ours("LockoutBadCount"));
        Assert.Equal(Net("Lockout observation window (minutes)"), Ours("ResetLockoutCount"));
        if (Ours("LockoutBadCount") != "0") Assert.Equal(Net("Lockout duration (minutes)"), Ours("LockoutDuration"));
    }

    [Fact]
    public void Service_start_provider_matches_wmi()
    {
        var provider = new ServiceStartProvider();
        var map = new Dictionary<string, string> { ["Boot"] = "0", ["System"] = "1", ["Auto"] = "2", ["Manual"] = "3", ["Disabled"] = "4" };
        using var searcher = new ManagementObjectSearcher("SELECT Name, StartMode FROM Win32_Service");
        int compared = 0;
        foreach (ManagementObject s in searcher.Get())
        {
            if (!map.TryGetValue((string)s["StartMode"], out var expected)) continue; // WMI reports "Unknown" for a few
            Assert.Equal((string)s["Name"] + "=" + expected, (string)s["Name"] + "=" + provider.Read(new SettingRef("service", "", "", (string)s["Name"], "")).Value);
            compared++;
        }
        Assert.True(compared > 100, $"{compared} servicii comparate");
        Assert.Equal(SettingValue.Absent, provider.Read(new SettingRef("service", "", "", "NoSuchService_" + Guid.NewGuid().ToString("N"), "")));
    }

    [Fact]
    public void Audit_provider_without_elevation_reports_the_missing_privilege_and_the_control_is_unreadable()
    {
        if (WindowsSettingProvidersTestHook.Elevated) return; // the elevated path is covered by Audit_provider_matches_auditpol
        var x = new PolicyExecutor(WindowsSettingProviders.All(), _dir);
        var p = PolicyLoader.Parse("""
            policy: { id: A, version: '1', title: t, author: a }
            controls:
              - id: AUD
                title: Process Creation
                detection: { type: audit, name: Process Creation }
                desired_state: Success
                remediation: set
                verification: reread
            """);
        var c = x.Plan(p).Controls.Single();
        Assert.Equal((ControlState.Unreadable, PlannedAction.None), (c.State, c.Action));
        Assert.Contains("Win32Exception", c.ReadError);
        var ex = Assert.Throws<System.ComponentModel.Win32Exception>(() => new AuditSettingProvider().Read(p.Controls[0].Setting));
        Assert.Equal(1314, ex.NativeErrorCode); // ERROR_PRIVILEGE_NOT_HELD
    }

    [AdminFact]
    public void Audit_provider_matches_auditpol()
    {
        var psi = new ProcessStartInfo("auditpol.exe", "/get /category:* /r") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = Process.Start(psi)!;
        var rows = proc.StandardOutput.ReadToEnd().Split('\n').Skip(1).Select(l => l.Trim().Split(',')).Where(r => r.Length >= 5).ToList();
        proc.WaitForExit();
        var provider = new AuditSettingProvider();
        foreach (var r in rows)
            Assert.Equal(r[4], provider.Read(new SettingRef("audit", "", "", r[3].Trim('{', '}'), "")).Value);
        Assert.True(rows.Count > 40);
    }
}

internal static class WindowsSettingProvidersTestHook
{
    public static bool Elevated
    {
        get
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}
