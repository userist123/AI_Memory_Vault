using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP3a: hash-chained audit and custody, custody context, evidence lifecycle, case scope.</summary>
public sealed class AuditChainCustodyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_wp3a_{Guid.NewGuid():N}");
    private static readonly string Zeros = new('0', 64);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase(string name = "case", Action<CaseInfo>? _ = null) =>
        CaseWorkspace.Create(Path.Combine(_root, name), new CaseInfo { CaseId = "CASE-W", Name = name, CreatedAtUtc = DateTimeOffset.UtcNow, Host = "AUDITED", Scope = TestScopes.Valid() });

    private EvidenceItem Add(CaseWorkspace ws, string name = "a.txt")
    {
        var p = Path.Combine(ws.RawDir("s"), name);
        File.WriteAllText(p, "x" + name);
        return ws.RegisterStored(p, name, "s", "txt", TemporalType.Historical, "unit", "1");
    }

    // ---- chain append + verify ----

    [Fact]
    public void New_case_chains_custody_and_audit_with_zero_first_prev_hash_and_verifies_valid()
    {
        var ws = NewCase();
        Add(ws); Add(ws, "b.txt");
        var lines = File.ReadAllLines(ws.CustodyJsonlPath);
        var first = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal(1, first.GetProperty("seq").GetInt64());
        Assert.Equal(Zeros, first.GetProperty("prevHash").GetString());
        var second = JsonDocument.Parse(lines[1]).RootElement;
        Assert.Equal(first.GetProperty("hash").GetString(), second.GetProperty("prevHash").GetString()); // only if >1 custody line
        var r = ws.VerifyChains();
        Assert.Equal(ChainStatus.Valid, r.Custody.Status);
        Assert.Equal(ChainStatus.Valid, r.Audit.Status);
        Assert.True(r.Custody.Entries >= 1);
        Assert.True(r.Audit.Entries >= 2); // case.created, evidence.registered
        Assert.True(File.Exists(Path.Combine(ws.Root, "Logs", "audit_chain.jsonl")));
    }

    [Fact]
    public void Legacy_csv_and_audit_log_are_still_written_unchanged()
    {
        var ws = NewCase();
        Add(ws);
        Assert.Contains("TimestampUtc,Who,Action", File.ReadAllText(ws.CustodyCsvPath));
        Assert.Contains("\tcase.created\t", File.ReadAllText(ws.AppAuditLogPath));
    }

    [Fact]
    public void Chain_survives_reopen_and_continues()
    {
        var ws = NewCase();
        Add(ws);
        var reopened = CaseWorkspace.Open(ws.Root);
        Add(reopened, "b.txt");
        var r = reopened.VerifyChains();
        Assert.Equal(ChainStatus.Valid, r.Custody.Status);
        Assert.Equal(ChainStatus.Valid, r.Audit.Status);
    }

    [Fact]
    public void Two_open_workspaces_on_the_same_case_interleave_without_forking_the_chain()
    {
        // e.g. the investigation pipeline and a later Open of the same case folder, both alive: each append must
        // continue from what is on disk, not from a head cached when the first instance was created.
        var a = NewCase();
        var b = CaseWorkspace.Open(a.Root);
        Add(a, "1.txt"); Add(b, "2.txt"); Add(a, "3.txt"); Add(b, "4.txt");
        var r = a.VerifyChains();
        Assert.Equal(ChainStatus.Valid, r.Custody.Status);
        Assert.Equal(ChainStatus.Valid, r.Audit.Status);
    }

    private static void Rewrite(string path, Func<List<string>, List<string>> f) => File.WriteAllLines(path, f(File.ReadAllLines(path).ToList()));

    [Fact]
    public void Edited_entry_is_detected_at_its_seq()
    {
        var ws = NewCase(); Add(ws); Add(ws, "b.txt");
        Rewrite(ws.CustodyJsonlPath, l => { l[1] = l[1].Replace("\"unit\"", "\"evil\""); return l; });
        var c = ws.VerifyChains().Custody;
        Assert.Equal(ChainStatus.Broken, c.Status);
        Assert.Equal(2, c.BrokenAtSeq);
        Assert.False(string.IsNullOrEmpty(c.Reason));
    }

    [Fact]
    public void Deleted_entry_is_detected()
    {
        var ws = NewCase(); Add(ws); Add(ws, "b.txt"); Add(ws, "c.txt");
        Rewrite(ws.CustodyJsonlPath, l => { l.RemoveAt(1); return l; });
        var c = ws.VerifyChains().Custody;
        Assert.Equal(ChainStatus.Broken, c.Status);
        Assert.Equal(2, c.BrokenAtSeq);
    }

    [Fact]
    public void Reordered_entries_are_detected()
    {
        var ws = NewCase(); Add(ws); Add(ws, "b.txt"); Add(ws, "c.txt");
        Rewrite(ws.CustodyJsonlPath, l => { (l[1], l[2]) = (l[2], l[1]); return l; });
        var c = ws.VerifyChains().Custody;
        Assert.Equal(ChainStatus.Broken, c.Status);
        Assert.Equal(2, c.BrokenAtSeq);
    }

    [Fact]
    public void Edited_audit_chain_entry_is_detected()
    {
        var ws = NewCase(); Add(ws);
        var path = Path.Combine(ws.Root, "Logs", "audit_chain.jsonl");
        Rewrite(path, l => { l[0] = l[0].Replace("case.created", "case.erased"); return l; });
        var a = ws.VerifyChains().Audit;
        Assert.Equal(ChainStatus.Broken, a.Status);
        Assert.Equal(1, a.BrokenAtSeq);
    }

    [Fact]
    public void Legacy_case_without_chain_fields_is_reported_unverifiable_not_valid_not_error()
    {
        var ws = NewCase(); Add(ws);
        // Simulate a pre-WP3 case: strip the chain fields from the custody log and drop the audit chain.
        Rewrite(ws.CustodyJsonlPath, l => l.Select(x =>
        {
            var o = System.Text.Json.Nodes.JsonNode.Parse(x)!.AsObject();
            o.Remove("seq"); o.Remove("prevHash"); o.Remove("hash");
            return o.ToJsonString();
        }).ToList());
        File.Delete(Path.Combine(ws.Root, "Logs", "audit_chain.jsonl"));
        var r = CaseWorkspace.Open(ws.Root).VerifyChains();
        Assert.Equal(ChainStatus.Legacy, r.Custody.Status);
        Assert.Contains("lanț neverificabil (caz creat înainte de WP3)", r.Custody.Message);
        Assert.NotEqual(ChainStatus.Valid, r.Custody.Status);
    }

    [Fact]
    public void Legacy_lines_then_new_chained_lines_verify_the_chained_part()
    {
        var ws = NewCase(); Add(ws);
        Rewrite(ws.CustodyJsonlPath, l => l.Select(x =>
        {
            var o = System.Text.Json.Nodes.JsonNode.Parse(x)!.AsObject();
            o.Remove("seq"); o.Remove("prevHash"); o.Remove("hash");
            return o.ToJsonString();
        }).ToList());
        var reopened = CaseWorkspace.Open(ws.Root);
        Add(reopened, "b.txt");
        var c = reopened.VerifyChains().Custody;
        Assert.Equal(ChainStatus.Valid, c.Status);
        Assert.True(c.LegacyLines >= 1);
        Assert.Contains("înainte de WP3", c.Message);
    }

    [Fact]
    public void Legacy_line_after_chained_lines_is_broken()
    {
        var ws = NewCase(); Add(ws);
        File.AppendAllText(ws.CustodyJsonlPath, "{\"who\":\"intruder\"}\n");
        Assert.Equal(ChainStatus.Broken, ws.VerifyChains().Custody.Status);
    }

    // ---- custody context ----

    [Fact]
    public void Collection_entries_carry_account_machine_source_and_medium_with_necunoscut_for_unknown()
    {
        var ws = NewCase();
        ws.SetCollectionContext(new CollectionContext("DOM\\collector", "FORENSIC-PC", "AUDITED", ""));
        Add(ws);
        var e = Json.ReadLines<CustodyEntry>(ws.CustodyJsonlPath).First(x => x.Action == "acquired");
        Assert.Equal("DOM\\collector", e.CollectorAccount);
        Assert.Equal("FORENSIC-PC", e.CollectorMachine);
        Assert.Equal("AUDITED", e.SourceSystem);
        Assert.Equal("necunoscut", e.MediaId);
    }

    [Fact]
    public void Removable_medium_id_is_recorded_when_known()
    {
        var ws = NewCase();
        ws.SetCollectionContext(new CollectionContext("a", "m", "s", "USB-SER-1234"));
        Add(ws);
        Assert.Equal("USB-SER-1234", Json.ReadLines<CustodyEntry>(ws.CustodyJsonlPath).First(x => x.Action == "acquired").MediaId);
    }

    [Fact]
    public void Admin_collector_on_the_audited_system_writes_a_warning_once_and_does_not_block()
    {
        var ws = NewCase();
        ws.IsLocalAdministrator = () => true;
        ws.SetCollectionContext(new CollectionContext("admin", "AUDITED", "AUDITED", ""));
        var ev = Add(ws); Add(ws, "b.txt");
        Assert.NotNull(ev);
        var warns = Json.ReadLines<CustodyEntry>(ws.CustodyJsonlPath).Where(x => x.Action == "warning.collector_admin").ToList();
        Assert.Single(warns);
        Assert.Contains("colectorul este administrator al sistemului auditat", warns[0].Transformation);
    }

    [Fact]
    public void Non_admin_or_different_machine_collector_writes_no_warning()
    {
        var ws = NewCase();
        ws.IsLocalAdministrator = () => false;
        ws.SetCollectionContext(new CollectionContext("u", "AUDITED", "AUDITED", ""));
        Add(ws);
        var ws2 = NewCase("case2");
        ws2.IsLocalAdministrator = () => true;
        ws2.SetCollectionContext(new CollectionContext("admin", "FORENSIC-PC", "AUDITED", ""));
        Add(ws2);
        Assert.DoesNotContain(Json.ReadLines<CustodyEntry>(ws.CustodyJsonlPath), x => x.Action == "warning.collector_admin");
        Assert.DoesNotContain(Json.ReadLines<CustodyEntry>(ws2.CustodyJsonlPath), x => x.Action == "warning.collector_admin");
    }

    // ---- lifecycle ----

    [Fact]
    public void New_evidence_is_acquired_and_old_json_without_state_defaults_to_acquired()
    {
        var ws = NewCase();
        Assert.Equal(EvidenceState.Acquired, Add(ws).State);
        var old = JsonSerializer.Deserialize<EvidenceItem>("{\"evidenceId\":\"EV-1\",\"caseId\":\"C\",\"source\":\"s\",\"sourceType\":\"t\"}", Json.Line)!;
        Assert.Equal(EvidenceState.Acquired, old.State);
    }

    [Fact]
    public void Forward_transitions_are_allowed_persisted_and_chained_in_custody()
    {
        var ws = NewCase(); var ev = Add(ws);
        foreach (var next in new[] { EvidenceState.Verified, EvidenceState.InAnalysis, EvidenceState.Archived })
        {
            var r = ws.TransitionState(ev.EvidenceId, next, "analyst");
            Assert.True(r.Ok, r.Reason);
            Assert.Equal(next, ws.LoadEvidence().Single().State);
        }
        var trans = Json.ReadLines<CustodyEntry>(ws.CustodyJsonlPath).Where(x => x.Action == "state.transition").Select(x => x.Transformation).ToList();
        Assert.Equal(["ACQUIRED->VERIFIED", "VERIFIED->IN_ANALYSIS", "IN_ANALYSIS->ARCHIVED"], trans);
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Custody.Status);
    }

    [Theory]
    [InlineData(EvidenceState.Acquired, EvidenceState.InAnalysis)]
    [InlineData(EvidenceState.Acquired, EvidenceState.Archived)]
    [InlineData(EvidenceState.Verified, EvidenceState.Acquired)]
    [InlineData(EvidenceState.Verified, EvidenceState.Archived)]
    [InlineData(EvidenceState.InAnalysis, EvidenceState.Verified)]
    [InlineData(EvidenceState.Archived, EvidenceState.InAnalysis)]
    [InlineData(EvidenceState.Acquired, EvidenceState.Acquired)]
    public void Invalid_transitions_are_refused_with_reason_and_not_written(EvidenceState from, EvidenceState to)
    {
        var ws = NewCase(); var ev = Add(ws);
        Advance(ws, ev.EvidenceId, from);
        var before = File.ReadAllLines(ws.CustodyJsonlPath).Length;
        var r = ws.TransitionState(ev.EvidenceId, to, "analyst");
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        Assert.Equal(from, ws.LoadEvidence().Single().State);
        Assert.Equal(before, File.ReadAllLines(ws.CustodyJsonlPath).Length);
    }

    [Fact]
    public void Disposed_cannot_be_reached_through_TransitionState_and_unknown_evidence_is_refused()
    {
        var ws = NewCase(); var ev = Add(ws);
        Assert.False(ws.TransitionState(ev.EvidenceId, EvidenceState.Disposed, "x").Ok);
        Assert.False(ws.TransitionState("EV-999999", EvidenceState.Verified, "x").Ok);
    }

    private static void Advance(CaseWorkspace ws, string id, EvidenceState to)
    {
        foreach (var s in new[] { EvidenceState.Verified, EvidenceState.InAnalysis, EvidenceState.Archived })
        {
            if (ws.LoadEvidence().Single(e => e.EvidenceId == id).State == to) return;
            Assert.True(ws.TransitionState(id, s, "setup").Ok);
        }
    }

    [Fact]
    public void Dispose_mark_requires_two_distinct_approvers_and_archived_state()
    {
        var ws = NewCase(); var ev = Add(ws);
        Assert.False(ws.MarkDisposed(ev.EvidenceId, "alice", "bob", "end of retention").Ok); // not archived yet
        Advance(ws, ev.EvidenceId, EvidenceState.Archived);
        Assert.False(ws.MarkDisposed(ev.EvidenceId, "alice", "alice", "r").Ok);
        Assert.False(ws.MarkDisposed(ev.EvidenceId, "alice", " ALICE ", "r").Ok);
        Assert.False(ws.MarkDisposed(ev.EvidenceId, "alice", "", "r").Ok);
        Assert.Equal(EvidenceState.Archived, ws.LoadEvidence().Single().State);
        var ok = ws.MarkDisposed(ev.EvidenceId, "alice", "bob", "end of retention");
        Assert.True(ok.Ok, ok.Reason);
        Assert.Equal(EvidenceState.Disposed, ws.LoadEvidence().Single().State);
        var e = Json.ReadLines<CustodyEntry>(ws.CustodyJsonlPath).Last(x => x.Action == "state.disposed");
        Assert.Contains("alice", e.Transformation); Assert.Contains("bob", e.Transformation);
        // No deletion: the stored file is still there.
        Assert.True(File.Exists(ws.FullPath(ev.StoredPath)));
    }

    [Fact]
    public void Legal_hold_blocks_disposal_and_release_allows_it()
    {
        var ws = NewCase(); var ev = Add(ws);
        Advance(ws, ev.EvidenceId, EvidenceState.Archived);
        ws.SetLegalHold(true, "litigation");
        Assert.True(CaseWorkspace.Open(ws.Root).Info.LegalHold);
        var r = ws.MarkDisposed(ev.EvidenceId, "alice", "bob", "x");
        Assert.False(r.Ok);
        Assert.Contains("LegalHold", r.Reason);
        Assert.Equal(EvidenceState.Archived, ws.LoadEvidence().Single().State);
        ws.SetLegalHold(false, "released");
        Assert.True(ws.MarkDisposed(ev.EvidenceId, "alice", "bob", "x").Ok);
    }

    [Fact]
    public void Disposed_is_terminal()
    {
        var ws = NewCase(); var ev = Add(ws);
        Advance(ws, ev.EvidenceId, EvidenceState.Archived);
        Assert.True(ws.MarkDisposed(ev.EvidenceId, "alice", "bob", "x").Ok);
        Assert.False(ws.TransitionState(ev.EvidenceId, EvidenceState.Verified, "x").Ok);
        Assert.False(ws.MarkDisposed(ev.EvidenceId, "alice", "bob", "x").Ok);
    }

    [Fact]
    public void Retention_is_empty_by_default_and_warns_only_when_exceeded_without_deleting()
    {
        var ws = NewCase(); var ev = Add(ws);
        var now = new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(ws.Info.RetentionUntilUtc);
        Assert.Null(ws.RetentionWarning(now));
        ws.SetRetention(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Null(ws.RetentionWarning(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)));
        var w = ws.RetentionWarning(now);
        Assert.NotNull(w);
        Assert.Contains("retenție", w);
        Assert.True(File.Exists(ws.FullPath(ev.StoredPath)));
        Assert.Equal(EvidenceState.Acquired, ws.LoadEvidence().Single().State);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), CaseWorkspace.Open(ws.Root).Info.RetentionUntilUtc);
    }

    // ---- scope ----

    [Fact]
    public void Incomplete_scope_is_refused_listing_every_missing_field_and_creates_nothing()
    {
        var dir = Path.Combine(_root, "refused");
        var ex = Assert.Throws<CaseScopeIncompleteException>(() => CaseWorkspace.Create(dir, new CaseInfo { CaseId = "C", Name = "n", CreatedAtUtc = DateTimeOffset.UtcNow }));
        foreach (var f in new[] { "purpose", "period.from", "period.to", "systems in scope", "approver", "legal basis", "system category (network)", "system category (classified/unclassified)" })
            Assert.Contains(f, ex.Missing);
        Assert.False(File.Exists(Path.Combine(dir, "case.json")));
    }

    [Fact]
    public void Partial_scope_lists_only_what_is_missing_and_period_must_be_ordered()
    {
        var s = TestScopes.Valid();
        var partial = new CaseScope { Purpose = s.Purpose, PeriodFromUtc = s.PeriodToUtc, PeriodToUtc = s.PeriodFromUtc, SystemsInScope = s.SystemsInScope, Approver = " ", LegalBasis = LegalBasis.Audit, Network = s.Network, Classification = s.Classification };
        var ex = Assert.Throws<CaseScopeIncompleteException>(() => CaseWorkspace.Create(Path.Combine(_root, "p"), new CaseInfo { CaseId = "C", Name = "n", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = partial }));
        Assert.Equal(2, ex.Missing.Count);
        Assert.Contains("approver", ex.Missing);
        Assert.Contains("period (to precedes from)", ex.Missing);
    }

    [Fact]
    public void Complete_scope_round_trips_through_case_json()
    {
        var ws = NewCase();
        var back = CaseWorkspace.Open(ws.Root).Info.Scope;
        Assert.Equal("test", back.Purpose);
        Assert.Equal(LegalBasis.Incident, back.LegalBasis);
        Assert.Equal(NetworkCategory.StandalonePc, back.Network);
        Assert.Equal(ClassificationLevel.Unclassified, back.Classification);
        Assert.Equal(["TESTHOST"], back.SystemsInScope);
        Assert.Empty(back.MissingFields());
        Assert.Null(CaseWorkspace.Open(ws.Root).ScopeNote);
    }

    [Fact]
    public void Provisional_scope_is_never_reported_as_confirmed_and_is_audited_on_create_and_open()
    {
        var root = Path.Combine(_root, "prov");
        var scope = TestScopes.Valid();
        var info = new CaseInfo { CaseId = "CASE-P", Name = "prov", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = new CaseScope
        {
            Purpose = scope.Purpose, PeriodFromUtc = scope.PeriodFromUtc, PeriodToUtc = scope.PeriodToUtc, SystemsInScope = scope.SystemsInScope,
            Approver = scope.Approver, LegalBasis = scope.LegalBasis, Network = scope.Network, Classification = scope.Classification, Provisional = true,
        } };
        var ws = CaseWorkspace.Create(root, info);
        Assert.Contains("scop provizoriu", ws.ScopeNote);
        Assert.Contains("case.scope_provisional", File.ReadAllText(ws.AppAuditLogPath));
        var reopened = CaseWorkspace.Open(root);
        Assert.True(reopened.Info.Scope.Provisional);
        Assert.Contains("scop provizoriu", reopened.ScopeNote);
    }

    [Fact]
    public void Old_case_json_without_scope_loads_with_empty_scope_and_a_note()
    {
        var ws = NewCase();
        var path = Path.Combine(ws.Root, "case.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node.Remove("scope"); node.Remove("legalHold"); node.Remove("retentionUntilUtc");
        File.WriteAllText(path, node.ToJsonString());
        var old = CaseWorkspace.Open(ws.Root);
        Assert.NotEmpty(old.Info.Scope.MissingFields());
        Assert.False(old.Info.LegalHold);
        Assert.Contains("scop incomplet (caz vechi)", old.ScopeNote);
        Add(old); // an old case remains usable
    }
}
