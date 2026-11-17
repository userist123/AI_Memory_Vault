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

    [Fact]
    public void Two_app_instances_on_one_case_write_the_plain_audit_files_while_another_writer_holds_them()
    {
        // Lab 2026-11-16 (LAB-WS02): two app instances share the station's LIVE case; the second failed with "used by another process" on
        // LogAnalyzer_Audit.log, and the exception also lost the chained entry written after it. A writer holding the plain files (as the old
        // File.AppendAllText / CsvWriter of another instance did: write access, FileShare.Read) now only delays the append.
        var a = NewCase();
        var b = CaseWorkspace.Open(a.Root);
        Add(a);                                       // the custody CSV exists, with its header
        int auditBefore = File.ReadAllLines(a.AppAuditLogPath).Length;
        int custodyBefore = File.ReadAllLines(a.CustodyCsvPath).Length;
        var held = new[] { a.AppAuditLogPath, a.CustodyCsvPath, a.CollectionAuditPath }
            .Select(p => new FileStream(p, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read)).ToList();
        var release = Task.Run(async () => { await Task.Delay(300); held.ForEach(s => s.Dispose()); });
        const int n = 20;
        var now = DateTimeOffset.UtcNow;
        Parallel.For(0, 2, i =>
        {
            var ws = i == 0 ? a : b;
            for (int k = 0; k < n; k++)
            {
                ws.Audit("test.parallel", $"w{i} k{k}");
                ws.RecordCollection(new CollectionAuditRecord($"C{i}", now, now, EvidenceStatus.Success, 0, "src", "dst", 1, Zeros, "t", "1", "", ""));
                ws.Custody(new CustodyEntry(now, "u", "test.parallel", $"E{i}-{k}", "", "", Zeros, "t", "1", "", ""));
            }
        });
        release.Wait();

        var audit = File.ReadAllLines(a.AppAuditLogPath);
        Assert.Equal(n * 2, audit.Count(l => l.Contains("\ttest.parallel\t")));
        Assert.Equal(n * 2, audit.Count(l => l.Contains("\tcollector.finished\t")));
        Assert.All(audit.Skip(auditBefore), l => Assert.Equal(4, l.Split('\t').Length));          // no interleaved half-lines
        var collection = File.ReadAllLines(a.CollectionAuditPath);
        Assert.Single(collection, l => l.StartsWith("Collector,StartUTC", StringComparison.Ordinal)); // header once, by whichever writer came first
        Assert.Equal(n * 2 + 1, collection.Length);
        Assert.Equal(custodyBefore + n * 2, File.ReadAllLines(a.CustodyCsvPath).Length);
        var bytes = File.ReadAllBytes(a.CollectionAuditPath);
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, (byte)'C', ..]);                                     // the BOM only at the start
        Assert.Equal(1, Enumerable.Range(0, bytes.Length - 2).Count(j => bytes[j] == 0xEF && bytes[j + 1] == 0xBB && bytes[j + 2] == 0xBF));
        var r = a.VerifyChains();
        Assert.Equal(ChainStatus.Valid, r.Audit.Status);
        Assert.Equal(ChainStatus.Valid, r.Custody.Status);
    }

    [Fact]
    public void A_shared_append_fails_at_once_on_an_error_that_is_not_a_sharing_violation()
    {
        var dir = Path.Combine(_root, "ro");
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "x.log");
        File.WriteAllText(p, "");
        File.SetAttributes(p, FileAttributes.ReadOnly);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<UnauthorizedAccessException>(() => SharedAppend.WriteText(p, "y\n"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));   // no 15-second wait for an error a retry cannot fix
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

    // ---- shared chained append (two processes on one file) ----

    [Fact]
    public void Two_workspaces_on_one_case_give_distinct_evidence_ids()
    {
        var a = NewCase();
        var b = CaseWorkspace.Open(a.Root);
        var ids = new List<string>();
        for (int k = 0; k < 6; k++) ids.Add(Add(k % 2 == 0 ? a : b, $"f{k}.txt").EvidenceId);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids.OrderBy(x => x, StringComparer.Ordinal).ToList(), ids);
        Assert.Equal(ids, Json.ReadLines<EvidenceItem>(a.EvidenceIndexPath).Select(e => e.EvidenceId).ToList());
    }

    [Fact]
    public void A_state_change_in_one_workspace_never_drops_an_entry_another_workspace_appends_at_the_same_time()
    {
        // review 2026-11-17: the state change read the index, then replaced it with temp file + move; an entry appended in between was dropped and its
        // id issued again. Now the read and the rewrite happen under the index handle that also serialises the appends.
        var a = NewCase();
        var b = CaseWorkspace.Open(a.Root);
        var first = Enumerable.Range(0, 20).Select(k => Add(a, $"a{k}.txt").EvidenceId).ToList();
        var appended = new List<string>();
        var writer = Task.Run(() => { for (int k = 0; k < 40; k++) appended.Add(Add(b, $"b{k}.txt").EvidenceId); });
        var changer = Task.Run(() => { foreach (var id in first) Assert.True(a.TransitionState(id, EvidenceState.Verified, "t").Ok); });
        Task.WaitAll(writer, changer);
        var index = Json.ReadLines<EvidenceItem>(a.EvidenceIndexPath).ToList();
        Assert.Equal(60, index.Count);
        Assert.Equal(60, index.Select(e => e.EvidenceId).Distinct().Count());
        Assert.All(appended, id => Assert.Contains(index, e => e.EvidenceId == id));
        Assert.All(first, id => Assert.Equal(EvidenceState.Verified, index.Single(e => e.EvidenceId == id).State));
        Assert.False(File.Exists(a.EvidenceIndexPath + ".tmp"));
    }

    [Fact]
    public void A_refused_state_change_leaves_the_index_bytes_unchanged()
    {
        var a = NewCase();
        var id = Add(a, "x.txt").EvidenceId;
        var before = File.ReadAllBytes(a.EvidenceIndexPath);
        Assert.False(a.TransitionState(id, EvidenceState.Archived, "t").Ok);   // ACQUIRED -> ARCHIVED skips steps
        Assert.False(a.TransitionState("EV-999999", EvidenceState.Verified, "t").Ok);
        Assert.Equal(before, File.ReadAllBytes(a.EvidenceIndexPath));
    }

    [Fact]
    public void Two_policy_audit_logs_on_one_file_keep_one_chain()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "sub", "policy_audit.jsonl");
        var a = new LogAnalyzer.Dfir.Policy.PolicyAuditLog(path);
        var b = new LogAnalyzer.Dfir.Policy.PolicyAuditLog(path);
        for (int k = 0; k < 6; k++)
            (k % 2 == 0 ? a : b).Append(new LogAnalyzer.Dfir.Policy.PolicyAuditEntry(DateTimeOffset.UtcNow, "X", "k", "op", "ap", "P", "1", "s", "p", "st", "c", "set", null, "a", null, "ok", $"d{k}"));
        Assert.Empty(a.VerifyChain());
        Assert.Equal(6, a.Read().Count);
        Assert.Equal("", a.Read()[0].Prev);
    }

    [Fact]
    public void A_chained_append_waits_for_a_writer_that_holds_the_file()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "held.jsonl");
        File.WriteAllText(path, "first\n");
        foreach (var share in new[] { FileShare.Read, FileShare.None })
        {
            var held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, share);
            var release = Task.Run(async () => { await Task.Delay(300); held.Dispose(); });
            string? seen = null;
            SharedAppend.WriteAfterLastLine(path, last => { seen = last; return "next\n"; });
            release.Wait();
            Assert.NotNull(seen);
        }
        Assert.Equal(new[] { "first", "next", "next" }, File.ReadAllLines(path));
        Assert.False(File.ReadAllBytes(path) is [0xEF, 0xBB, 0xBF, ..]);
    }

    [Fact]
    public void A_chained_append_fails_at_once_on_an_error_that_is_not_a_sharing_violation()
    {
        var dir = Path.Combine(_root, "isdir");
        Directory.CreateDirectory(dir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.ThrowsAny<Exception>(() => SharedAppend.WriteAfterLastLine(dir, _ => "x"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public void A_chained_append_on_an_empty_file_sees_no_last_line_and_creates_the_folder()
    {
        var path = Path.Combine(_root, "new", "dir", "c.jsonl");
        string? seen = "unset";
        SharedAppend.WriteAfterLastLine(path, last => { seen = last; return "a\n"; });
        Assert.Null(seen);
        SharedAppend.WriteAfterLastLine(path, last => { seen = last; return "b\n"; });
        Assert.Equal("a", seen);
    }
}
