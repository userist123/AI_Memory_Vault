using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Response;
using LogAnalyzer.Dfir.Windows.Containment;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// R13.4: per-program containment is VERIFIED only after the firewall rules are re-read and the process is re-enumerated; the
/// process is identified by PID + start time + image SHA-256, so a reused PID is the original process ended, and the new one is
/// never touched. Fakes only: no firewall rule, suspension or resume reaches the host.
/// </summary>
public sealed class ResponseVerificationProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "la-respver-proc-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    /// <summary>Firewall whose re-read can disagree with what the action reported.</summary>
    private sealed class ScriptedFirewall : IFirewallController
    {
        public Dictionary<string, FirewallRuleInfo> Rules { get; } = new();
        public HashSet<FirewallDirection> SilentlyDropped { get; } = new();
        public bool RemoveIgnored { get; set; }
        public Exception? ListThrows { get; set; }

        public void AddProgramBlockRule(string name, string programPath, FirewallDirection direction, string description)
        {
            if (!SilentlyDropped.Contains(direction)) Rules[name] = new FirewallRuleInfo(name, programPath, direction, true, description);
        }

        public bool RemoveRule(string name) => !RemoveIgnored && Rules.Remove(name);

        public IReadOnlyList<FirewallRuleInfo> ListContainmentRules() => ListThrows is { } ex ? throw ex : Rules.Values.ToList();
    }

    /// <summary>Process list as re-enumerated; tests change it between the action and the re-check.</summary>
    private sealed class FakeProcessTable : IProcessTable
    {
        public Dictionary<int, ProcessObservation> Processes { get; } = new();
        public Dictionary<string, string> Hashes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Exception? Throws { get; set; }

        public ProcessObservation? Observe(int pid) => Throws is { } ex ? throw ex : Processes.GetValueOrDefault(pid);
        public string? HashImage(string path) => Hashes.GetValueOrDefault(path);
    }

    private CaseWorkspace NewCase() => CaseWorkspace.Create(Path.Combine(_root, "case-" + Guid.NewGuid().ToString("N")[..8]),
        new CaseInfo { CaseId = "CASE-RVP", Name = "rv-process", CreatedAtUtc = DateTimeOffset.UtcNow, Timezone = "GTB Standard Time", Scope = TestScopes.Valid() });

    private string Sample()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"agent_{Guid.NewGuid().ToString("N")[..6]}.exe");
        File.WriteAllText(path, "MZ program de test");
        return path;
    }

    private sealed record Rig(ProcessContainmentService Svc, ScriptedFirewall Fw, FakeProcessTable Table, CaseWorkspace Case, List<int> Suspended, List<int> Resumed, string Path);

    /// <summary>PID 4242 runs the sample; suspending it marks every thread suspended (unless <paramref name="suspendHasNoEffect"/>).</summary>
    private Rig NewRig(bool suspendHasNoEffect = false)
    {
        var fw = new ScriptedFirewall();
        var table = new FakeProcessTable();
        var path = Sample();
        table.Processes[4242] = new ProcessObservation(4242, Start, path, false);
        table.Hashes[path] = "AA11";
        var suspended = new List<int>();
        var resumed = new List<int>();
        var ws = NewCase();
        var svc = new ProcessContainmentService(fw, new ProcessScanner { RunDefenderScan = false }, ws,
            pid => { suspended.Add(pid); if (!suspendHasNoEffect) table.Processes[pid] = table.Processes[pid] with { AllThreadsSuspended = true }; },
            pid => { resumed.Add(pid); if (table.Processes.TryGetValue(pid, out var o)) table.Processes[pid] = o with { AllThreadsSuspended = false }; },
            "tester", table) { VerifyTimeout = TimeSpan.FromSeconds(5) };
        return new Rig(svc, fw, table, ws, suspended, resumed, path);
    }

    private static ContainmentOptions Suspend => new(SuspendProcess: true, CopySampleIntoCase: false);

    [Fact]
    public void Containment_is_contained_only_after_rules_and_process_are_rechecked_and_the_outcome_is_audited()
    {
        var r = NewRig();
        var inc = r.Svc.Contain(r.Path, 4242, "test", Suspend);
        Assert.Equal(ContainmentState.Contained, inc.State);
        Assert.Equal(ResponseStatus.Verified, inc.Verification!.Status);
        Assert.Equal(3, inc.Verification.Checks.Count);              // outbound rule, inbound rule, process
        Assert.Equal(new ResponseProcessIdentity(4242, Start, r.Path, "AA11"), inc.ProcessIdentity);
        Assert.Contains("containment.response.verify", File.ReadAllText(r.Case.AuditChainPath));
        Assert.Equal(ResponseStatus.Verified, r.Svc.LoadAll().Single().Verification!.Status);   // persisted with the incident
    }

    [Fact]
    public void Suspension_reported_but_threads_still_running_is_failed()
    {
        var r = NewRig(suspendHasNoEffect: true);
        var inc = r.Svc.Contain(r.Path, 4242, "test", Suspend);
        Assert.Equal(new[] { 4242 }, r.Suspended);
        Assert.True(inc.ProcessSuspended);                            // the call returned...
        Assert.Equal(ResponseStatus.Failed, inc.Verification!.Status); // ...but the re-enumeration shows it running
        Assert.Equal(ContainmentState.Failed, inc.State);
        Assert.Contains("nu toate firele", inc.Verification.Cause);
    }

    [Fact]
    public void One_of_two_rules_missing_on_reread_is_failed_with_the_missing_rule_named()
    {
        var r = NewRig();
        r.Fw.SilentlyDropped.Add(FirewallDirection.Inbound);
        var inc = r.Svc.Contain(r.Path, null, "test", new ContainmentOptions(false, false));
        Assert.Equal(2, inc.FirewallRules.Count);                     // both adds "succeeded"
        Assert.Equal(ContainmentState.Failed, inc.State);
        Assert.Contains("1 din 2", inc.Verification!.Cause);
        Assert.Contains("-in", inc.Verification.Cause);
        Assert.Contains(inc.Verification.Checks, c => c.Target.EndsWith("-in") && c.Status == ResponseStatus.Failed);
    }

    [Fact]
    public void Firewall_or_process_reread_that_throws_is_not_verified_with_the_cause_and_audited()
    {
        var r = NewRig();
        r.Fw.ListThrows = new UnauthorizedAccessException("COM: acces refuzat");
        var inc = r.Svc.Contain(r.Path, null, "test", new ContainmentOptions(false, false));
        Assert.Equal(ContainmentState.NotVerified, inc.State);
        Assert.Equal(ResponseStatus.NotVerified, inc.Verification!.Status);
        Assert.Contains("acces refuzat", inc.Verification.Cause);
        Assert.Contains("NOT_VERIFIED", File.ReadAllText(r.Case.AuditChainPath));

        var p = NewRig();
        var inc2 = p.Svc.Contain(p.Path, 4242, "test", Suspend);
        Assert.Equal(ContainmentState.Contained, inc2.State);
        p.Table.Throws = new TimeoutException("lista proceselor");
        var released = p.Svc.Release(inc2, "test");
        Assert.Empty(p.Resumed);                                      // unreadable list: the PID may be reused, nothing is resumed
        Assert.True(released.ProcessSuspended);
        Assert.Equal(ResponseStatus.NotVerified, released.ReleaseVerification!.Status);
        Assert.Contains("NU a fost reluat", released.ReleaseVerification.Cause);
        Assert.Contains(released.Actions, a => a.Action == "process.resume" && !a.Success && a.Detail.Contains("Verificați manual"));
        Assert.NotEqual(ContainmentState.Released, released.State);  // never claimed released without the re-check
    }

    [Fact]
    public void Unknown_identity_is_never_suspended_nor_resumed_even_after_pid_reuse()
    {
        // SYSTEM process: the image cannot be read (MainModule denied), so its identity cannot be established.
        var r = NewRig();
        r.Table.Processes[4242] = new ProcessObservation(4242, Start, null, false);
        var inc = r.Svc.Contain(r.Path, 4242, "test", Suspend);
        Assert.Empty(r.Suspended);                                    // refused: cannot confirm the PID is the program
        Assert.False(inc.ProcessSuspended);
        Assert.Null(inc.ProcessIdentity);
        Assert.Equal(ContainmentState.NotVerified, inc.State);
        Assert.Contains(inc.Verification!.Checks, c => c.Target == "proces PID 4242" && c.Status == ResponseStatus.Rejected && c.Observed.Contains("NU a fost atins"));
        Assert.Equal(2, inc.Verification.Checks.Count(c => c.Status == ResponseStatus.Verified));   // the rules are checked separately
        Assert.Contains(inc.Actions, a => a.Action == "process.suspend" && !a.Success && a.Detail.Contains("refuzată"));

        // An incident saved suspended without identity (before WP9), then the PID is reused: Release must not resume the new owner.
        var basis = r.Svc.Contain(r.Path, null, "test", new ContainmentOptions(false, false));
        var legacy = new ProcessIncident
        {
            IncidentId = basis.IncidentId, CaseId = basis.CaseId, ProgramPath = basis.ProgramPath, Pid = 4242, Trigger = "vechi",
            State = ContainmentState.Contained, ProcessSuspended = true, FirewallRules = basis.FirewallRules.ToList(),
        };
        r.Table.Processes[4242] = new ProcessObservation(4242, Start.AddMinutes(30), Path.Combine(_root, "svc.exe"), true);
        var released = r.Svc.Release(legacy, "test");
        Assert.Empty(r.Resumed);
        Assert.True(released.ProcessSuspended);
        Assert.Equal(ResponseStatus.NotVerified, released.ReleaseVerification!.Status);
        Assert.Contains("identitatea", released.ReleaseVerification.Cause);
        Assert.NotEqual(ContainmentState.Released, released.State);
    }

    [Fact]
    public void A_pid_that_runs_another_program_is_not_suspended_and_the_firewall_part_is_stated_separately()
    {
        var r = NewRig();
        var other = Path.Combine(_root, "unrelated.exe");
        r.Table.Processes[4242] = new ProcessObservation(4242, Start, other, false);   // stale suspect list: PID now runs another program
        r.Table.Hashes[other] = "DD44";
        var inc = r.Svc.Contain(r.Path, 4242, "test", Suspend);
        Assert.Empty(r.Suspended);
        Assert.NotEqual(ContainmentState.Contained, inc.State);
        Assert.Equal(ResponseStatus.NotVerified, inc.Verification!.Status);
        Assert.Contains("unrelated.exe", inc.Verification.Cause);
        Assert.NotEqual("IZOLAT (VERIFICAT)", inc.StateText);
        var summary = ContainmentText.Summary(inc);
        Assert.Contains("reguli firewall: 2 din 2 confirmate", summary);
        Assert.Contains("REJECTED", summary);

        // Same path, but the suspect's SHA-256 is known and the running image differs: refused as well.
        var s = NewRig();
        var inc2 = s.Svc.Contain(s.Path, 4242, "test", Suspend with { ExpectedSha256 = "FFFF" });
        Assert.Empty(s.Suspended);
        Assert.Contains("SHA-256", inc2.Verification!.Cause);

        var ok = NewRig();                                            // matching SHA-256: suspended and verified
        Assert.Equal(ContainmentState.Contained, ok.Svc.Contain(ok.Path, 4242, "test", Suspend with { ExpectedSha256 = "aa11" }).State);
        Assert.Equal(new[] { 4242 }, ok.Suspended);
    }

    [Fact]
    public void Failed_partial_containment_names_the_active_rules_and_automatic_containment_skips_it()
    {
        var r = NewRig();
        r.Fw.SilentlyDropped.Add(FirewallDirection.Inbound);
        var inc = r.Svc.Contain(r.Path, null, "test", new ContainmentOptions(false, false));
        Assert.Equal(ContainmentState.Failed, inc.State);
        var summary = ContainmentText.Summary(inc);
        Assert.Contains("reguli rămase ACTIVE", summary);
        Assert.Contains(inc.FirewallRules.First(n => n.EndsWith("-out")), summary);
        Assert.Contains(r.Path, ContainmentAutoPolicy.AlreadyHandled([inc]));   // not re-contained every tick

        var none = new ProcessIncident { IncidentId = "INC-9", CaseId = "C", ProgramPath = @"C:\x\y.exe", Trigger = "t", State = ContainmentState.Failed };
        var released = new ProcessIncident { IncidentId = "INC-8", CaseId = "C", ProgramPath = @"C:\x\z.exe", Trigger = "t", State = ContainmentState.Released };
        Assert.Empty(ContainmentAutoPolicy.AlreadyHandled([none, released]));   // nothing active: may be contained again
        Assert.Contains("nicio regulă activă", ContainmentText.Summary(none));
    }

    [Fact]
    public void Incident_saved_before_verification_is_not_shown_as_verified()
    {
        var old = new ProcessIncident { IncidentId = "INC-1", CaseId = "C", ProgramPath = @"C:\x\a.exe", Trigger = "t", State = ContainmentState.Contained };
        Assert.Equal("izolat (neverificat: incident înregistrat înainte de verificare)", old.StateText);
        Assert.Equal(old.StateText, ContainmentText.State(old));
        var oldReleased = new ProcessIncident { IncidentId = "INC-2", CaseId = "C", ProgramPath = @"C:\x\a.exe", Trigger = "t", State = ContainmentState.Released };
        Assert.DoesNotContain("(VERIFICAT)", oldReleased.StateText);

        var r = NewRig();
        var inc = r.Svc.Contain(r.Path, 4242, "test", Suspend);
        Assert.Equal("IZOLAT (VERIFICAT)", inc.StateText);
    }

    [Fact]
    public void Suspension_without_a_process_reader_is_not_verified()
    {
        var ws = NewCase();
        var svc = new ProcessContainmentService(new ScriptedFirewall(), new ProcessScanner { RunDefenderScan = false }, ws, _ => { }, _ => { }, "tester");
        var inc = svc.Contain(Sample(), 4242, "test", Suspend);
        Assert.Equal(ContainmentState.NotVerified, inc.State);
        Assert.Contains("cititor", inc.Verification!.Cause);
    }

    [Fact]
    public void Pid_reused_after_the_process_ended_counts_as_terminated_and_the_new_process_is_not_touched()
    {
        var r = NewRig();
        var inc = r.Svc.Contain(r.Path, 4242, "test", Suspend);
        Assert.Equal(ContainmentState.Contained, inc.State);

        // The contained process ended; Windows gave PID 4242 to another program (other start time, other image), which is running.
        var other = Path.Combine(_root, "other.exe");
        r.Table.Processes[4242] = new ProcessObservation(4242, Start.AddMinutes(9), other, false);
        r.Table.Hashes[other] = "BB22";
        Assert.Equal(ResponseStatus.Verified, ResponseVerifier.Verify("kill", "apply", [ProcessVerification.Terminated(inc.ProcessIdentity, 4242, r.Table)]).Status);

        var released = r.Svc.Release(inc, "test");
        Assert.Empty(r.Resumed);                                      // the new owner of the PID is never resumed
        Assert.Equal(ContainmentState.Released, released.State);
        Assert.Equal(ResponseStatus.Verified, released.ReleaseVerification!.Status);
        Assert.Contains(released.ReleaseVerification.Checks, c => c.Observed.Contains("altui proces"));
    }

    [Fact]
    public void Same_pid_and_start_but_other_image_hash_is_a_different_process()
    {
        var table = new FakeProcessTable();
        table.Processes[77] = new ProcessObservation(77, Start, @"C:\x\a.exe", false);
        table.Hashes[@"C:\x\a.exe"] = "CC33";
        var id = new ResponseProcessIdentity(77, Start, @"C:\x\a.exe", "AA11");
        Assert.Equal(ProcessVerification.Match.Reused, ProcessVerification.Compare(id, table.Observe(77), table).Match);
        Assert.Equal(ResponseStatus.Verified, ResponseVerifier.Verify("kill", "apply", [ProcessVerification.Terminated(id, 77, table)]).Status);

        table.Hashes[@"C:\x\a.exe"] = "AA11";                         // the same process still runs: the termination is FAILED
        Assert.Equal(ResponseStatus.Failed, ResponseVerifier.Verify("kill", "apply", [ProcessVerification.Terminated(id, 77, table)]).Status);

        table.Hashes.Clear();                                         // image unreadable: cannot tell -> NOT_VERIFIED
        Assert.Equal(ResponseStatus.NotVerified, ResponseVerifier.Verify("kill", "apply", [ProcessVerification.Terminated(id, 77, table)]).Status);
        Assert.Equal(ResponseStatus.NotVerified, ResponseVerifier.Verify("kill", "apply", [ProcessVerification.Terminated(null, 77, table)]).Status);
    }

    [Fact]
    public void Release_is_verified_and_a_rule_that_stays_is_failed_and_kept_for_a_new_attempt()
    {
        var r = NewRig();
        var inc = r.Svc.Contain(r.Path, 4242, "test", Suspend);
        r.Fw.RemoveIgnored = true;
        var stuck = r.Svc.Release(inc, "test");
        Assert.Equal(new[] { 4242 }, r.Resumed);                      // same identity: it is resumed
        Assert.Equal(ResponseStatus.Failed, stuck.ReleaseVerification!.Status);
        Assert.Equal(ContainmentState.Contained, stuck.State);        // still contained, as the re-read shows
        Assert.Equal(2, stuck.FirewallRules.Count);

        r.Fw.RemoveIgnored = false;
        var ok = r.Svc.Release(stuck, "test");
        Assert.Equal(ResponseStatus.Verified, ok.ReleaseVerification!.Status);
        Assert.Equal("rollback", ok.ReleaseVerification.Phase);
        Assert.Equal(ContainmentState.Released, ok.State);
        Assert.Empty(r.Fw.Rules);
    }

    [Fact]
    public void Audit_write_failure_during_a_response_is_surfaced()
    {
        var r = NewRig();
        var inc = r.Svc.Contain(r.Path, null, "test", new ContainmentOptions(false, false));
        foreach (var f in Directory.EnumerateFiles(Path.Combine(r.Case.Root, "Logs"))) File.SetAttributes(f, FileAttributes.ReadOnly);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => r.Svc.Release(inc, "test"));
    }
}
