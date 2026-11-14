using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Parsers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Windows.db and Windows-gather.db copied from ONE shadow copy: the collector (fake shadow copy), the moment key in the notes, and the rule
/// WSEARCH-GATHER-ONLY. Threat model: the notes of an imported file hold the operator's path (a forged key or note), two databases from different moments
/// would give hundreds of false "deleted", a gatherer row can be planted, Windows.db read incompletely would make rows look missing.
/// </summary>
public sealed class WindowsSearchGatherPairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_wsgp_" + Guid.NewGuid().ToString("N"));
    public WindowsSearchGatherPairTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(_dir, true); }

    // ---------------------------------------------------------------- collector

    /// <summary>A shadow copy for the tests: Create freezes the index folder into a mirror folder.</summary>
    private sealed class FakeSnapshot(string mirrorRoot, string indexDir, bool fail = false, string? missingInSnapshot = null) : WindowsSearchIndexCollector.ISnapshot
    {
        public List<string> Deleted { get; } = [];
        public (string Id, string DeviceObject) Create(string volumeRoot)
        {
            if (fail) throw new InvalidOperationException("Win32_ShadowCopy.Create a întors 5");
            var frozen = mirrorRoot.TrimEnd('\\') + indexDir[(Path.GetPathRoot(indexDir)!.Length - 1)..];
            Directory.CreateDirectory(frozen);
            foreach (var f in Directory.GetFiles(indexDir))
                if (!Path.GetFileName(f).Equals(missingInSnapshot, StringComparison.OrdinalIgnoreCase)) File.Copy(f, Path.Combine(frozen, Path.GetFileName(f)));
            return ("{SNAP-7}", mirrorRoot);
        }
        public void Delete(string id) => Deleted.Add(id);
    }

    /// <summary>An index folder: the collector only copies bytes, so the files need no valid content.</summary>
    private string IndexFolder(string name, params string[] files)
    {
        var d = Path.Combine(_dir, name); Directory.CreateDirectory(d);
        foreach (var f in files) File.WriteAllText(Path.Combine(d, f), "x:" + f);
        return d;
    }

    private (CollectorOutcome O, CaseWorkspace Ws) Collect(WindowsSearchIndexCollector.ISnapshot snap, string indexDir)
    {
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case_" + Guid.NewGuid().ToString("N")),
            new CaseInfo { CaseId = "C-WSG", Name = "wsg", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        return (new WindowsSearchIndexCollector(snap, indexDir).Collect(new CollectorContext { Case = ws, IsElevated = true }, default), ws);
    }

    private static string Key => WindowsSearchGatherParser.SnapshotKeyPrefix + "{SNAP-7}";

    [Fact]
    public void The_collector_copies_gather_with_its_journal_from_the_same_shadow_copy_with_notes_and_the_common_key()
    {
        var dir = IndexFolder("live1", "Windows.db", "Windows.db-wal", "Windows.db-shm", "Windows-gather.db", "Windows-gather.db-wal", "Windows-gather.db-shm");
        var snap = new FakeSnapshot(Path.Combine(_dir, "snap1"), dir);
        var (o, _) = Collect(snap, dir);
        Assert.Equal(["{SNAP-7}"], snap.Deleted);
        var db = Assert.Single(o.Evidence, e => e.SourceType == WindowsSearchIndexParser.SourceTypeName);
        var gdb = Assert.Single(o.Evidence, e => e.SourceType == WindowsSearchGatherParser.SourceTypeName);
        Assert.Single(o.Evidence, e => e.SourceType == WindowsSearchGatherParser.WalSourceType);
        Assert.Single(o.Evidence, e => e.SourceType == WindowsSearchGatherParser.ShmSourceType);
        Assert.Contains($"Copiat din copia de umbră {{SNAP-7}}, {WindowsSearchGatherParser.GatherSameSnapshotNote}", gdb.Notes);
        Assert.Contains($"Copiat din copia de umbră {{SNAP-7}}, {WindowsSearchIndexParser.SameSnapshotNote}", db.Notes);
        Assert.Contains(Key, gdb.Notes); Assert.Contains(Key, db.Notes);
        // the key of both databases is the same one and is read back only for a vouched-for pair of this collector
        Assert.Equal("{SNAP-7}", SearchSqliteCopy.SnapshotKey(new SearchSqliteSpec("Windows-gather.db", [], WindowsSearchGatherParser.GatherSameSnapshotNote, "", WindowsSearchGatherParser.CollectedBy, 1), gdb, gdb.StoredPath));
        Assert.Equal("{SNAP-7}", SearchSqliteCopy.SnapshotKey(new SearchSqliteSpec("Windows.db", [], WindowsSearchIndexParser.SameSnapshotNote, "", WindowsSearchIndexParser.CollectedBy, 1), db, db.StoredPath));
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void Without_a_shadow_copy_gather_is_not_copied_at_all_and_says_so()
    {
        var dir = IndexFolder("live2", "Windows.db", "Windows-gather.db", "Windows-gather.db-wal");
        var (o, _) = Collect(new FakeSnapshot(Path.Combine(_dir, "snap2"), dir, fail: true), dir);
        Assert.DoesNotContain(o.Evidence, e => e.SourceType is WindowsSearchGatherParser.SourceTypeName or WindowsSearchGatherParser.WalSourceType or WindowsSearchGatherParser.ShmSourceType);
        Assert.Contains(o.Errors, e => e.StartsWith("Windows-gather.db: necopiat") && e.Contains("copie de umbră comună"));
        Assert.Contains(o.Errors, e => e.StartsWith("Windows-gather.db-wal: necopiat"));
        // the database copied without a shadow copy carries no key
        Assert.All(o.Evidence, e => Assert.DoesNotContain(WindowsSearchGatherParser.SnapshotKeyPrefix, e.Notes));
    }

    [Fact]
    public void A_missing_gather_does_not_break_the_step_and_adds_no_error()
    {
        var dir = IndexFolder("live3", "Windows.db", "Windows.db-wal");
        var snap = new FakeSnapshot(Path.Combine(_dir, "snap3"), dir);
        var (o, _) = Collect(snap, dir);
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Empty(o.Errors);
        Assert.DoesNotContain(o.Evidence, e => e.SourceType == WindowsSearchGatherParser.SourceTypeName);
        Assert.Contains(o.Evidence, e => e.SourceType == WindowsSearchIndexParser.SourceTypeName);
    }

    [Fact]
    public void Gather_over_the_size_cap_is_noted_and_not_copied_while_the_index_is()
    {
        var dir = IndexFolder("live4", "Windows.db", "Windows-gather.db");
        var o = new CollectorOutcome();
        var plan = WindowsSearchIndexCollector.Plan(dir, 8, o);   // 8 bytes: both files are "x:Windows..." and over the cap
        Assert.Empty(plan);
        Assert.Contains(o.Errors, e => e.StartsWith("Windows-gather.db:") && e.Contains("peste plafonul"));
        var o2 = new CollectorOutcome();
        Assert.Equal(["Windows.db", "Windows-gather.db"], WindowsSearchIndexCollector.Plan(dir, 1024, o2).Select(p => p.FileName).ToArray());
    }

    // ---------------------------------------------------------------- the key helper

    private static readonly SearchSqliteSpec GatherSpec = new("Windows-gather.db", [], WindowsSearchGatherParser.GatherSameSnapshotNote, "", WindowsSearchGatherParser.CollectedBy, 1);

    private static EvidenceItem Item(string notes, string collector, string method) =>
        new() { EvidenceId = "E", CaseId = "C", Source = "s", SourceType = WindowsSearchGatherParser.SourceTypeName, StoredPath = "p", Notes = notes, Collector = collector, AcquisitionMethod = method };

    [Fact]
    public void The_key_is_read_only_from_a_vouched_pair_once_at_the_end_and_for_an_import_only_when_it_names_its_own_folder()
    {
        string Same = WindowsSearchGatherParser.GatherSameSnapshotNote;
        var guid = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(_dir, "WindowsSearch_" + guid, "Windows-gather.db");
        string K(string id) => WindowsSearchGatherParser.SnapshotKeyPrefix + id;
        // collector: accepted
        Assert.Equal("{AB-12345678}", SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"x. Copiat din copia de umbră {{AB-12345678}}, {Same}. {K("{AB-12345678}")}.", WindowsSearchGatherParser.CollectedBy, "acquired"), "z"));
        // a note text without the collector / method of this application: refused (an operator-supplied path could contain it)
        Assert.Null(SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"{Same}. {K("{AB-12345678}")}.", "Import", "imported"), folder));
        Assert.Null(SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"{Same}. {K("{AB-12345678}")}.", WindowsSearchGatherParser.CollectedBy, "imported"), "z"));
        // no same-snapshot note: refused even with a key
        Assert.Null(SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"x. {K("{AB-12345678}")}.", WindowsSearchGatherParser.CollectedBy, "acquired"), "z"));
        // the key twice (one forged in the path), or not at the end: refused
        Assert.Null(SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"{Same}. {K("{AB-12345678}")}. {K("{AB-12345678}")}.", WindowsSearchGatherParser.CollectedBy, "acquired"), "z"));
        Assert.Null(SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"{Same}. {K("{AB-12345678}")}. altceva", WindowsSearchGatherParser.CollectedBy, "acquired"), "z"));
        // remote package import: the key must equal the id of the folder the application created
        Assert.Equal(guid, SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"PC01:files/x; c; copia de umbră a pachetului, {Same}; {K(guid)}", RemoteCollection.CollectorName, "imported"), folder));
        Assert.Null(SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"PC01:files/x; c; copia de umbră a pachetului, {Same}; {K("0123456789abcdef0123456789abcdef")}", RemoteCollection.CollectorName, "imported"), folder));
        Assert.Null(SearchSqliteCopy.SnapshotKey(GatherSpec, Item($"PC01:files/x; c; copia de umbră a pachetului, {Same}; {K(guid)}", RemoteCollection.CollectorName, "imported"), Path.Combine(_dir, "altfolder", "Windows-gather.db")));
    }

    // ---------------------------------------------------------------- the rule

    private static readonly DateTime T0 = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
    private int _n;

    private TimelineEvent Store(string work, string key, string host = "WS01", bool incomplete = false)
    {
        var f = new List<(string, string)> { ("WorkId", work) };
        if (key.Length > 0) f.Add((Wp11Rules.SearchMomentField, key));
        if (incomplete) f.Add((Wp11Rules.SearchIncompleteField, "da"));
        return Make("WindowsSearch", @"C:\LA\store-" + work + ".txt", host, f);
    }

    private TimelineEvent Gather(string doc, string key, string path = @"C:\LA\TEST-LA-gather-sters.txt", string host = "WS01", string scopePattern = "nu")
    {
        var f = new List<(string, string)> { ("DocumentID", doc), ("IsScopePattern", scopePattern), ("LastModified", "2026-11-10T07:55:00Z") };
        if (key.Length > 0) f.Add((Wp11Rules.SearchMomentField, key));
        return Make("WindowsSearchGather", path, host, f);
    }

    private TimelineEvent Make(string source, string path, string host, List<(string, string)> f) => new()
    {
        Time = Timestamp.FromUtc(T0, "", "test"), Source = source, EvidenceId = source == "WindowsSearch" ? "EV-S" : "EV-G", Locator = "row=" + ++_n, Summary = "t", Path = path, Host = host,
        Fields = f.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase),
    };

    private static List<Finding> Rule(params TimelineEvent[] events) => W11.Run(events).Where(f => f.RuleId == "WSEARCH-GATHER-ONLY").ToList();

    [Fact]
    public void A_gather_row_missing_from_the_store_of_the_same_moment_gives_exactly_one_finding_for_the_experiment_data()
    {
        // lab experiment: gather knows 1811 (kept file) and 1827 (TEST-LA-gather-sters.txt, deleted); the store has only 1811
        var found = Rule(Store("1811", "{S1}"), Gather("1811", "{S1}", @"C:\LA\TEST-LA-gather-pastrat.txt"), Gather("1827", "{S1}"));
        var f = Assert.Single(found);
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal("", f.MitreTechniqueId);
        Assert.Contains("TEST-LA-gather-sters.txt", f.File);
        Assert.Contains("TEST-LA-gather-sters.txt", f.Description);
        Assert.Contains("posibil șterse sau mutate", f.Description);
        Assert.Contains("$MFT", f.Description); Assert.Contains("$UsnJrnl", f.Description); Assert.Contains("Coșul de reciclare", f.Description);
        Assert.Contains("1827", Assert.Single(f.SupportingEvidence).ToString());   // the row is named in the evidence reference
    }

    [Fact]
    public void A_gather_row_present_in_the_store_gives_nothing()
        => Assert.Empty(Rule(Store("1811", "{S1}"), Gather("1811", "{S1}")));

    [Fact]
    public void Different_keys_give_nothing_because_the_two_databases_are_from_different_moments()
        => Assert.Empty(Rule(Store("1811", "{S1}"), Gather("1827", "{S2}")));

    [Fact]
    public void Without_a_key_on_either_side_nothing_is_emitted()
    {
        Assert.Empty(Rule(Store("1811", ""), Gather("1827", "")));
        Assert.Empty(Rule(Store("1811", "{S1}"), Gather("1827", "")));
        Assert.Empty(Rule(Store("1811", ""), Gather("1827", "{S1}")));
    }

    [Fact]
    public void Gather_alone_or_the_store_alone_gives_nothing()
    {
        Assert.Empty(Rule(Gather("1827", "{S1}")));
        Assert.Empty(Rule(Store("1811", "{S1}")));
    }

    [Fact]
    public void The_same_key_on_another_host_is_another_moment()
        => Assert.Empty(Rule(Store("1811", "{S1}", host: "WS01"), Gather("1827", "{S1}", host: "WS02")));

    [Fact]
    public void A_scope_pattern_row_gives_nothing()
        => Assert.Empty(Rule(Store("1811", "{S1}"), Gather("1827", "{S1}", @"C:\LA\*", scopePattern: "da")));

    [Fact]
    public void A_store_read_incompletely_gives_nothing_because_the_comparison_would_be_false()
        => Assert.Empty(Rule(Store("1811", "{S1}", incomplete: true), Store("1812", "{S1}"), Gather("1827", "{S1}")));

    [Fact]
    public void Many_missing_rows_are_one_finding_that_lists_the_first_and_counts_the_rest()
    {
        // lab 2026-11-10 (a real station): 14 separate findings for ordinary churn -> one finding per moment
        var events = new List<TimelineEvent> { Store("1", "{S1}") };
        for (int i = 0; i < 30; i++) events.Add(Gather((100 + i).ToString(), "{S1}", $@"C:\LA\gone{i}.txt"));
        var f = Assert.Single(Rule(events.ToArray()));
        Assert.Equal(Severity.Low, f.Severity);
        Assert.StartsWith("30 de fișiere cunoscute", f.Title);
        Assert.Equal(Wp11Rules.SearchGatherListed, System.Text.RegularExpressions.Regex.Matches(f.Description, @"C:\\LA\\gone\d+\.txt").Count);
        Assert.Contains("și încă 15", f.Description);
        Assert.DoesNotContain("scanner", f.Description, StringComparison.OrdinalIgnoreCase);
        // another moment is another finding
        events.Add(Store("1", "{S2}")); events.Add(Gather("500", "{S2}", @"C:\LA\other.txt"));
        Assert.Equal(2, Rule(events.ToArray()).Count);
    }

    // ---------------------------------------------------------------- the parsers stamp the key and flag incomplete reads

    private static EvidenceItem StoreItem(string path, string notes, string collector, string method) =>
        new() { EvidenceId = "EV-W", CaseId = "C", Source = "s", SourceType = WindowsSearchIndexParser.SourceTypeName, StoredPath = path, Notes = notes, Collector = collector, AcquisitionMethod = method };

    private string StoreDb(string name, int rows)
    {
        var d = Path.Combine(_dir, name); Directory.CreateDirectory(d);
        var p = Path.Combine(d, "Windows.db");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            void X(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            X("CREATE TABLE SystemIndex_1_PropertyStore_Metadata (Id INTEGER PRIMARY KEY, UniqueKey TEXT, Name TEXT, PropertyId INTEGER, VariantType INTEGER, StorageType INTEGER, MaxSize INTEGER, Flags INTEGER)");
            X("CREATE TABLE SystemIndex_1_PropertyStore (WorkId INTEGER, ColumnId INTEGER, Value BLOB, PRIMARY KEY (WorkId, ColumnId)) WITHOUT ROWID");
            X("INSERT INTO SystemIndex_1_PropertyStore_Metadata VALUES(7, 'k7', 'System.ItemPathDisplay', 1, 31, 11, NULL, 0)");
            for (int i = 1; i <= rows; i++) X($"INSERT INTO SystemIndex_1_PropertyStore VALUES({i}, 7, 'C:\\a\\f{i}.txt')");
        }
        SqliteConnection.ClearAllPools();
        return p;
    }

    [Fact]
    public void The_index_parser_stamps_the_key_for_a_vouched_pair_and_marks_a_capped_read_incomplete()
    {
        var db = StoreDb("st1", 5);
        var notes = $"Copiat din copia de umbră {{SNAP-7}}, {WindowsSearchIndexParser.SameSnapshotNote}. {Key}.";
        var item = StoreItem(db, notes, WindowsSearchIndexParser.CollectedBy, "acquired");

        var sink = new ListSink();
        var r = new WindowsSearchIndexParser().Parse(item, db, sink, default);
        Assert.Equal(5, r.Records);
        Assert.All(sink.Events, e => { Assert.Equal("{SNAP-7}", e.Fields[Wp11Rules.SearchMomentField]); Assert.False(e.Fields.ContainsKey(Wp11Rules.SearchIncompleteField)); });

        // cap reached: a gap is written and one event carries the incomplete flag, so the rule stays silent
        var capped = new ListSink();
        var r2 = new WindowsSearchIndexParser(3).Parse(item, db, capped, default);
        Assert.NotEmpty(r2.Gaps);
        Assert.Contains(capped.Events, e => e.Fields.ContainsKey(Wp11Rules.SearchIncompleteField));
        var gather = Gather("99", "{SNAP-7}");
        Assert.Empty(W11.Run(capped.Events.Concat([gather])).Where(f => f.RuleId == "WSEARCH-GATHER-ONLY"));

        // an item without the key (or not vouched for) gets no key on its events
        var plain = new ListSink();
        new WindowsSearchIndexParser().Parse(StoreItem(db, "orice", "Import", "imported"), db, plain, default);
        Assert.NotEmpty(plain.Events);
        Assert.All(plain.Events, e => Assert.False(e.Fields.ContainsKey(Wp11Rules.SearchMomentField)));
    }

    [Theory]
    [InlineData("Windows.db-wal", "Windows.db")]
    [InlineData("Windows-gather.db-wal", "Windows-gather.db")]
    public void A_database_whose_journal_was_not_copied_keeps_neither_the_same_snapshot_note_nor_the_moment_key(string missing, string database)
    {
        // review r1 MEDIUM: the journal existed but the copy from the shadow copy failed; the database alone is older than the pair and comparing it
        // with the other database would report every file indexed since the last checkpoint as missing
        var dir = IndexFolder("live-nowal-" + missing, "Windows.db", "Windows.db-wal", "Windows-gather.db", "Windows-gather.db-wal");
        var (o, _) = Collect(new FakeSnapshot(Path.Combine(_dir, "snap-nowal-" + missing), dir, missingInSnapshot: missing), dir);
        Assert.Contains(o.Errors, e => e.StartsWith(missing + ":"));
        var type = database == "Windows.db" ? WindowsSearchIndexParser.SourceTypeName : WindowsSearchGatherParser.SourceTypeName;
        var broken = Assert.Single(o.Evidence, e => e.SourceType == type);
        Assert.DoesNotContain(Key, broken.Notes);
        Assert.DoesNotContain(WindowsSearchIndexParser.SameSnapshotNote, broken.Notes);
        Assert.DoesNotContain(WindowsSearchGatherParser.GatherSameSnapshotNote, broken.Notes);
        Assert.Contains("Jurnalul WAL nu a putut fi copiat", broken.Notes);
        Assert.Null(SearchSqliteCopy.SnapshotKey(new SearchSqliteSpec(database, [], database == "Windows.db" ? WindowsSearchIndexParser.SameSnapshotNote : WindowsSearchGatherParser.GatherSameSnapshotNote, "", WindowsSearchIndexParser.CollectedBy, 1), broken, broken.StoredPath));
        // the other database, copied with its journal, keeps its key
        var other = Assert.Single(o.Evidence, e => e.SourceType == (type == WindowsSearchIndexParser.SourceTypeName ? WindowsSearchGatherParser.SourceTypeName : WindowsSearchIndexParser.SourceTypeName));
        Assert.Contains(Key, other.Notes);
    }

    [Fact]
    public void A_store_with_a_workid_without_a_path_is_marked_incomplete_so_its_gatherer_row_is_not_reported_missing()
    {
        // review r1 LOW: a WorkId whose properties have no path gives no event; its gatherer row would look missing
        var db = StoreDb("st-nopath", 3);
        using (var c = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO SystemIndex_1_PropertyStore_Metadata VALUES(8, 'k8', 'System.Size', 1, 21, 11, NULL, 0); INSERT INTO SystemIndex_1_PropertyStore VALUES(99, 8, 51);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var notes = $"Copiat din copia de umbră {{SNAP-7}}, {WindowsSearchIndexParser.SameSnapshotNote}. {Key}.";
        var sink = new ListSink();
        var r = new WindowsSearchIndexParser().Parse(StoreItem(db, notes, WindowsSearchIndexParser.CollectedBy, "acquired"), db, sink, default);
        Assert.Equal(3, r.Records);
        Assert.Contains(sink.Events, e => e.Fields.ContainsKey(Wp11Rules.SearchIncompleteField));
        Assert.Empty(W11.Run(sink.Events.Concat([Gather("99", "{SNAP-7}")])).Where(f => f.RuleId == "WSEARCH-GATHER-ONLY"));
    }
}