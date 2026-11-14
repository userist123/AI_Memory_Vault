using System.Security.Cryptography;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Windows-gather.db of Windows Search (Windows 11, SQLite): synthetic databases with the real schema (collation UNICODE_en-US_LINGUISTIC_IGNORECASE, WITHOUT ROWID).
/// Threat model (the database comes from the analysed station): P1 path chain, P2 FILETIME, P3 cycles and deep chains, P4 missing parent, P5 hidden characters,
/// P6 huge names, P7 event cap, P8 corrupt / other database, P9 journal (own, foreign), P10 stored evidence unchanged, P11 other schemes, P12 scope patterns.
/// </summary>
public sealed class WindowsSearchGatherParserTests : IDisposable
{
    private const string Guid1 = "c59dc150-2d86-41f5-a432-c8cb4a9dc215";
    private const string CollationName = "UNICODE_en-US_LINGUISTIC_IGNORECASE";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_wsg_" + Guid.NewGuid().ToString("N"));
    public WindowsSearchGatherParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(_dir, true); }

    private static void Exec(SqliteConnection c, string sql, params (string N, object? V)[] p)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>The real schema of Windows 11 26100 (columns and keys as found in the lab), with the Windows collation registered on the building connection.</summary>
    private static void Schema(SqliteConnection c, Comparison<string>? collation = null)
    {
        c.CreateCollation(CollationName, collation ?? ((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)));
        Exec(c, "CREATE TABLE SystemIndex_Gthr (ScopeID UNSIGNEDLONG_INTEGER NOT NULL, FileName LONGTEXT_TEXT(512), DocumentID UNSIGNEDLONG_INTEGER NOT NULL, SDID, UserData LONGBINARY_BLOB, " +
                  "AppOwnerId, LastModified BINARY_BLOB(8), RequiredSIDs, DeletedCount, TransactionFlags, TransactionExtendedFlags, CrawlNumberCrawled, StartAddressIdentifier, Priority, RunTime, " +
                  "FailureUpdateAttempts, ClientID, LastRequestedRunTime, StorageProviderId LONGTEXT_TEXT(130), CalculatedPropertyFlags, " +
                  $"PRIMARY KEY (ScopeID ASC, FileName COLLATE \"{CollationName}\" ASC)) WITHOUT ROWID");
        Exec(c, "CREATE INDEX SystemIndex_Gthr_indexDocID ON SystemIndex_Gthr (DocumentID)");
        Exec(c, "CREATE TABLE SystemIndex_GthrPth (Scope UNSIGNEDLONG_INTEGER, Parent UNSIGNEDLONG_INTEGER, Name LONGTEXT_TEXT(1000), PRIMARY KEY (Scope)) WITHOUT ROWID");
        Exec(c, "CREATE TABLE SystemIndex_GthrAppOwner (OwnerId INTEGER PRIMARY KEY, UserSid, AppUserModelId)");
    }

    private static void Pth(SqliteConnection c, object scope, object? parent, object? name) =>
        Exec(c, "INSERT INTO SystemIndex_GthrPth (Scope, Parent, Name) VALUES ($s, $p, $n)", ("$s", scope), ("$p", parent), ("$n", name));

    private static void Row(SqliteConnection c, object scope, object? fileName, long doc, object? lastModified = null, object? deleted = null, object? flags = null)
        => Exec(c, "INSERT INTO SystemIndex_Gthr (ScopeID, FileName, DocumentID, LastModified, DeletedCount, TransactionFlags) VALUES ($s, $f, $d, $m, $x, $t)",
            ("$s", scope), ("$f", fileName), ("$d", doc), ("$m", lastModified), ("$x", deleted), ("$t", flags));

    /// <summary>The path chain observed in the lab: file: / C: / "/" / [guid]/ / Users/ / it.admin/ / Documents/.</summary>
    private static void RealChain(SqliteConnection c)
    {
        Pth(c, 2, 1, "file:"); Pth(c, 3, 2, "C:"); Pth(c, 4, 3, "/"); Pth(c, 5, 4, $"[{Guid1}]/");
        Pth(c, 11, 5, "Users/"); Pth(c, 16, 11, "it.admin/"); Pth(c, 17, 16, "Documents/");
        Pth(c, 9, 1, "winrt:"); Pth(c, 10, 9, "//");
    }

    private string Make(string sub, Action<SqliteConnection> fill, bool chain = true, Comparison<string>? collation = null)
    {
        var d = Path.Combine(_dir, sub); Directory.CreateDirectory(d);
        var p = Path.Combine(d, "Windows-gather.db");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            Schema(c, collation);
            using var tx = c.BeginTransaction();
            if (chain) RealChain(c);
            fill(c);
            tx.Commit();
        }
        SqliteConnection.ClearAllPools();
        return p;
    }

    private static EvidenceItem Item(string path, string notes = "", string collector = "", string method = "") =>
        new() { EvidenceId = "EV-G", CaseId = "C", Source = "test", SourceType = WindowsSearchGatherParser.SourceTypeName, StoredPath = path, Notes = notes, Collector = collector, AcquisitionMethod = method };

    private static (ParseResult R, ListSink S) Run(string path, EvidenceItem? item = null, WindowsSearchGatherParser? parser = null)
    {
        var sink = new ListSink();
        IEvidenceParser p = parser ?? new WindowsSearchGatherParser();
        return (p.Parse(item ?? Item(path), path, sink, default), sink);
    }

    private static string Sha(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));

    private static void CopyShared(string from, string to)
    {
        using var f = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var t = new FileStream(to, FileMode.Create, FileAccess.Write);
        f.CopyTo(t);
    }

    private static readonly byte[] RealFiletime = Convert.FromHexString("df71f25a9270dd01");

    // ---- the Windows collation is not available: the read must not depend on the primary key order ----

    [Fact]
    public void Rows_ordered_by_a_collation_this_reader_does_not_have_are_all_read()
    {
        // built with a collation that orders names in reverse: the stand-in (OrdinalIgnoreCase) disagrees with the stored order, as the real
        // UNICODE_en-US_LINGUISTIC_IGNORECASE does (lab: 2 of 1,175 rows silently lost when the read went through SystemIndex_Gthr_indexDocID)
        var db = Make("coll", c =>
        {
            for (int i = 0; i < 200; i++) Row(c, 17, $"f{i:D3}_{(char)('a' + i % 26)}.txt", 1000 + (i * 37) % 200, RealFiletime);
        }, collation: (a, b) => string.CompareOrdinal(b, a));
        var (r, s) = Run(db);
        Assert.Equal(200, r.Records);
        Assert.Equal(200, s.Events.Select(e => e.Fields["DocumentID"]).Distinct().Count());
        // still in DocumentID order
        var docs = s.Events.Select(e => long.Parse(e.Fields["DocumentID"], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(docs.OrderBy(x => x), docs);
    }

    // ---- P1, P2: the real row ----

    [Fact]
    public void The_real_row_gives_the_reconstructed_path_the_scope_guid_and_the_little_endian_filetime()
    {
        var db = Make("real", c => Row(c, 17, "TEST-LA-raport-index.txt", 1811, RealFiletime, 0L, 2147483749L));
        var (r, s) = Run(db);
        Assert.Equal(1, r.Records);
        Assert.Empty(r.Gaps);
        var e = Assert.Single(s.Events);
        Assert.Equal(WindowsSearchGatherParser.EventSource, e.Source);
        Assert.Equal(@"C:\Users\it.admin\Documents\TEST-LA-raport-index.txt", e.Path);
        Assert.Equal(@"C:\Users\it.admin\Documents\TEST-LA-raport-index.txt", e.Fields["Path"]);
        Assert.Equal($"file:C:/[{Guid1}]/Users/it.admin/Documents/TEST-LA-raport-index.txt", e.Fields["Url"]);
        Assert.Equal(Guid1, e.Fields["ScopeGuid"]);
        Assert.Equal("da", e.Fields["PathComplete"]);
        Assert.Equal("TEST-LA-raport-index.txt", e.Fields["FileName"]);
        Assert.Equal("1811", e.Fields["DocumentID"]);
        Assert.Equal("2026-11-09T21:36:18.435Z", e.Fields["LastModified"]);
        Assert.True(e.Time.IsKnown);
        Assert.Equal("2026-11-09T21:36:18.435Z", e.Time.UtcIso);
        Assert.Equal("2147483749", e.Fields["TransactionFlags"]);
        Assert.Equal("0x80000065", e.Fields["TransactionFlagsHex"]);
        Assert.Equal("0", e.Fields["DeletedCount"]);
        Assert.Equal("SystemIndex_Gthr DocumentID=1811", e.Locator);
        Assert.Equal(TemporalType.Historical, e.TemporalType);
        Assert.Equal(Classification.Direct, e.Classification);
        Assert.Equal(Confidence.Medium, e.Confidence);
        Assert.Contains("Gatherer Windows Search", e.Summary);
        Assert.Contains("DocumentID 1811", e.Summary);
        Assert.Equal("nu", e.Fields["IsScopePattern"]);
    }

    [Fact]
    public void A_hostile_or_wrong_typed_filetime_is_an_unknown_time_never_an_exception()
    {
        var neg = BitConverter.GetBytes(-5L);
        var beyond = BitConverter.GetBytes(long.MaxValue);
        var db = Make("ft", c =>
        {
            Row(c, 17, "zero", 1, new byte[8]);
            Row(c, 17, "negative", 2, neg);
            Row(c, 17, "beyond", 3, beyond);
            Row(c, 17, "four", 4, new byte[] { 1, 2, 3, 4 });
            Row(c, 17, "text", 5, "2026-11-09");
            Row(c, 17, "int", 6, 133_000_000_000_000_000L);
            Row(c, 17, "null", 7, null);
            Row(c, 17, "good", 8, RealFiletime);
        });
        var (r, s) = Run(db);
        Assert.Equal(8, r.Records);
        foreach (var name in new[] { "zero", "negative", "beyond", "four", "text", "int", "null" })
            Assert.False(s.Events.Single(e => e.Fields["FileName"] == name).Time.IsKnown, name);
        Assert.True(s.Events.Single(e => e.Fields["FileName"] == "good").Time.IsKnown);
        Assert.Contains("oră necunoscută", s.Events.Single(e => e.Fields["FileName"] == "zero").Summary);
    }

    [Fact]
    public void Wrong_value_types_in_any_column_do_not_break_the_read()
    {
        var db = Make("types", c =>
        {
            Row(c, "not-a-number", "t.txt", 1, RealFiletime, "x", "y");
            Row(c, 17, new byte[] { 0x41, 0x00, 0x42, 0x00 }, 2, RealFiletime, new byte[] { 1, 2 }, 3.5);
            Row(c, 17, 12345L, 3, RealFiletime, new string('9', 5000), -1L);
            Row(c, 17, "", 4);
        });
        var (r, s) = Run(db);
        Assert.Equal(4, r.Records);
        Assert.Equal("nu", s.Events.Single(e => e.Fields["DocumentID"] == "1").Fields["PathComplete"]);
        Assert.Equal("12345", s.Events.Single(e => e.Fields["DocumentID"] == "3").Fields["FileName"]);
        Assert.Equal("", s.Events.Single(e => e.Fields["DocumentID"] == "3").Fields["DeletedCount"]);   // 5000 digits are cut to 64 and do not fit a number
    }

    // ---- P3, P4: chains ----

    [Fact]
    public void A_cycle_in_the_path_table_and_a_chain_of_300_levels_end_with_an_incomplete_path()
    {
        var db = Make("cycle", c =>
        {
            Pth(c, 20, 21, "a/"); Pth(c, 21, 20, "b/");
            Pth(c, 100, 1, "root/");
            for (int i = 101; i < 400; i++) Pth(c, i, i - 1, $"d{i}/");
            Pth(c, 500, 500, "self/");
            Row(c, 20, "in-cycle.txt", 1);
            Row(c, 399, "deep.txt", 2);
            Row(c, 100, "shallow.txt", 3);
            Row(c, 500, "self.txt", 4);
        });
        var (r, s) = Run(db);
        Assert.Equal(4, r.Records);
        foreach (var d in new[] { "1", "2", "4" })
        {
            var e = s.Events.Single(x => x.Fields["DocumentID"] == d);
            Assert.Equal("nu", e.Fields["PathComplete"]);
            Assert.StartsWith(WindowsSearchGatherParser.IncompletePrefix, e.Path);
        }
        var ok = s.Events.Single(x => x.Fields["DocumentID"] == "3");
        Assert.Equal("da", ok.Fields["PathComplete"]);
        Assert.Equal("root/shallow.txt", ok.Path);   // no known scheme: kept as text
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_folder_inside_a_too_deep_chain_gets_the_same_path_whatever_row_is_read_first(bool deepFirst)
    {
        // 300 levels: a row at the bottom is cut by depth; a row at level 100 has a complete path and must keep it even when read after the deep one
        var db = Make("order" + deepFirst, c =>
        {
            Pth(c, 100, 1, "root/");
            for (int i = 101; i < 400; i++) Pth(c, i, i - 1, $"d{i}/");
            Row(c, 399, "deep.txt", deepFirst ? 1 : 2);
            Row(c, 200, "mid.txt", deepFirst ? 2 : 1);
        });
        var (r, s) = Run(db);
        Assert.Equal(2, r.Records);
        var mid = s.Events.Single(x => x.Fields["FileName"] == "mid.txt");
        Assert.Equal("da", mid.Fields["PathComplete"]);
        Assert.StartsWith("root/d101/", mid.Path);
        Assert.EndsWith("d200/mid.txt", mid.Path);
        Assert.Equal("nu", s.Events.Single(x => x.Fields["FileName"] == "deep.txt").Fields["PathComplete"]);
    }

    [Fact]
    public void Every_event_stays_small_even_for_long_folder_names_in_deep_chains()
    {
        var db = Make("amp", c =>
        {
            Pth(c, 100, 1, "root/");
            for (int i = 101; i < 350; i++) Pth(c, i, i - 1, new string('n', 120) + "/");
            for (int k = 0; k < 20; k++) Row(c, 349, $"f{k}.txt", 10 + k);
        });
        var (r, s) = Run(db);
        Assert.Equal(20, r.Records);
        foreach (var e in s.Events)
        {
            Assert.True(e.Path.Length <= WindowsSearchGatherParser.MaxPathChars + WindowsSearchGatherParser.IncompletePrefix.Length);
            Assert.True(e.Summary.Length <= 3 * WindowsSearchGatherParser.MaxPathChars);
            Assert.True(e.Fields.Values.Sum(v => v.Length) <= 4 * WindowsSearchGatherParser.MaxPathChars + 2_048);
        }
    }

    [Fact]
    public void A_missing_parent_gives_an_incomplete_path_marked_in_the_text_and_in_the_field()
    {
        var db = Make("missing", c => { Pth(c, 50, 77, "orphan/"); Row(c, 50, "x.txt", 1); Row(c, 999, "noscope.txt", 2); });
        var (_, s) = Run(db);
        var e = s.Events.Single(x => x.Fields["DocumentID"] == "1");
        Assert.Equal("nu", e.Fields["PathComplete"]);
        Assert.StartsWith("[cale incompletă]", e.Path);
        Assert.Contains("orphan", e.Path);
        Assert.Equal("nu", s.Events.Single(x => x.Fields["DocumentID"] == "2").Fields["PathComplete"]);
    }

    // ---- P5, P6: hostile text ----

    [Fact]
    public void Bidi_controls_tabs_newlines_and_tag_block_characters_are_made_safe()
    {
        var db = Make("hidden", c =>
        {
            Pth(c, 60, 17, "evil\u202Efolder\t/");
            Row(c, 60, "a\nb\u202Etxt.exe\uDB40\uDC41", 1);
        });
        var (_, s) = Run(db);
        var e = Assert.Single(s.Events);
        foreach (var text in new[] { e.Path, e.Fields["Path"], e.Fields["FileName"], e.Fields["Url"], e.Summary })
        {
            Assert.DoesNotContain('\u202E', text);
            Assert.DoesNotContain('\t', text);
            Assert.DoesNotContain('\n', text);
            Assert.DoesNotContain('\uDB40', text);
        }
        Assert.Contains("bloc Tag", e.Fields["FileName"]);
    }

    [Fact]
    public void A_name_of_five_megabytes_is_cut_inside_sqlite()
    {
        var db = Make("huge", c =>
        {
            Exec(c, "INSERT INTO SystemIndex_GthrPth (Scope, Parent, Name) VALUES (70, 17, replace(hex(zeroblob(2500000)), '0', 'F'))");
            Exec(c, "INSERT INTO SystemIndex_Gthr (ScopeID, FileName, DocumentID) VALUES (70, replace(hex(zeroblob(2500000)), '0', 'E'), 1)");
        });
        var (r, s) = Run(db);
        Assert.Equal(1, r.Records);
        var e = Assert.Single(s.Events);
        Assert.True(e.Fields["FileName"].Length <= WindowsSearchGatherParser.MaxTextChars);
        Assert.True(e.Path.Length <= WindowsSearchGatherParser.MaxPathChars + 200);
        Assert.True(e.Path.Length < 10_000);
    }

    [Fact]
    public void A_path_longer_than_the_cap_is_cut_and_marked_incomplete()
    {
        var db = Make("longpath", c =>
        {
            Pth(c, 100, 17, new string('a', 1000));
            for (int i = 101; i < 160; i++) Pth(c, i, i - 1, new string('b', 1000));
            Row(c, 159, new string('c', 2000), 1);
        });
        var (_, s) = Run(db);
        var e = Assert.Single(s.Events);
        Assert.Equal("nu", e.Fields["PathComplete"]);
        Assert.True(e.Path.Length <= WindowsSearchGatherParser.MaxPathChars + 64);
    }

    // ---- P7: caps ----

    [Fact]
    public void The_event_cap_stops_the_read_with_a_partial_gap()
    {
        var db = Make("cap", c => { for (int i = 1; i <= 10; i++) Row(c, 17, $"f{i}.txt", i); });
        var (r, s) = Run(db, parser: new WindowsSearchGatherParser(3));
        Assert.Equal(3, r.Records);
        Assert.Equal(3, s.Events.Count);
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows-gather.db" && g.Status == EvidenceStatus.Partial && g.Reason.Contains("mai mult de 3"));
    }

    [Fact]
    public void Too_many_path_rows_leave_a_gap_and_the_paths_that_depend_on_them_incomplete()
    {
        var db = Make("pathcap", c => Row(c, 17, "x.txt", 1));
        var (r, s) = Run(db, parser: new WindowsSearchGatherParser(100, 3));
        Assert.Equal(1, r.Records);
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows-gather.db" && g.Status == EvidenceStatus.Partial && g.Reason.Contains("SystemIndex_GthrPth"));
        Assert.Equal("nu", Assert.Single(s.Events).Fields["PathComplete"]);
    }

    // ---- P8: damaged and foreign databases ----

    [Fact]
    public void A_corrupt_file_is_a_failed_result_with_a_gap_never_an_exception()
    {
        var d = Path.Combine(_dir, "bad"); Directory.CreateDirectory(d);
        var p = Path.Combine(d, "Windows-gather.db");
        File.WriteAllBytes(p, Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 + 7)).ToArray());
        var (r, s) = Run(p);
        Assert.Empty(s.Events);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows-gather.db");
    }

    [Fact]
    public void A_damaged_page_leaves_a_written_gap()
    {
        var path = Make("unsound", c =>
        {
            Exec(c, "CREATE TABLE pad(x BLOB)"); Exec(c, "CREATE INDEX pad_i ON pad(x)");
            for (int i = 0; i < 400; i++) Exec(c, "INSERT INTO pad VALUES(randomblob(900))");
            Row(c, 17, "x.txt", 1);
        });
        var bytes = File.ReadAllBytes(path);
        for (int i = bytes.Length / 2; i < bytes.Length / 2 + 8192 && i < bytes.Length; i++) bytes[i] = 0x5A;
        File.WriteAllBytes(path, bytes);
        var (r, _) = Run(path);
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows-gather.db");
    }

    [Fact]
    public void A_sqlite_file_without_the_gather_tables_is_failed_as_not_a_gather_database()
    {
        var d = Path.Combine(_dir, "other"); Directory.CreateDirectory(d);
        var p = Path.Combine(d, "Windows-gather.db");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False")) { c.Open(); Exec(c, "CREATE TABLE something(x INTEGER)"); }
        SqliteConnection.ClearAllPools();
        var (r, s) = Run(p);
        Assert.Empty(s.Events);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains(r.Gaps, g => g.Status == EvidenceStatus.Failed && g.Reason.Contains("nu este baza gather"));
    }

    [Fact]
    public void A_gather_table_with_another_schema_is_a_gap_not_an_exception()
    {
        var d = Path.Combine(_dir, "schema"); Directory.CreateDirectory(d);
        var p = Path.Combine(d, "Windows-gather.db");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            Exec(c, "CREATE TABLE SystemIndex_Gthr (Foo INTEGER)");
            Exec(c, "CREATE TABLE SystemIndex_GthrPth (Scope INTEGER, Parent INTEGER, Name TEXT)");
        }
        SqliteConnection.ClearAllPools();
        var (r, _) = Run(p);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows-gather.db");
    }

    [Fact]
    public void A_hostile_collation_name_is_registered_by_api_never_in_sql_and_the_read_works()
    {
        var d = Path.Combine(_dir, "coll"); Directory.CreateDirectory(d);
        var p = Path.Combine(d, "Windows-gather.db");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            c.CreateCollation("WEIRD-name_1", (a, b) => string.CompareOrdinal(a, b));
            Exec(c, "CREATE TABLE SystemIndex_Gthr (ScopeID INTEGER NOT NULL, FileName TEXT COLLATE \"WEIRD-name_1\", DocumentID INTEGER NOT NULL, LastModified BLOB, DeletedCount, TransactionFlags, CrawlNumberCrawled, Priority, AppOwnerId, StorageProviderId, UserData)");
            Exec(c, "CREATE TABLE SystemIndex_GthrPth (Scope INTEGER PRIMARY KEY, Parent INTEGER, Name TEXT)");
            Exec(c, "INSERT INTO SystemIndex_Gthr (ScopeID, FileName, DocumentID) VALUES (1, 'x', 1)");
        }
        SqliteConnection.ClearAllPools();
        var (r, _) = Run(p);
        Assert.Equal(1, r.Records);
    }

    // ---- P9: the journal ----

    /// <summary>A live gather database in WAL mode (never checkpointed) copied with its -wal into a folder: the rows live only in the journal.</summary>
    private string WalPair(string sub, int rows)
    {
        var live = Path.Combine(_dir, sub + "_live.db");
        var ev = Path.Combine(_dir, sub); Directory.CreateDirectory(ev);
        using (var c = new SqliteConnection($"Data Source={live};Pooling=False"))
        {
            c.Open();
            Exec(c, "PRAGMA journal_mode=WAL"); Exec(c, "PRAGMA wal_autocheckpoint=0");
            Schema(c);
            RealChain(c);
            for (int i = 1; i <= rows; i++) Row(c, 17, $"f{i}.txt", i, RealFiletime);
            Assert.True(File.Exists(live + "-wal"));
            CopyShared(live, Path.Combine(ev, "Windows-gather.db"));
            CopyShared(live + "-wal", Path.Combine(ev, "Windows-gather.db-wal"));
        }
        SqliteConnection.ClearAllPools();
        return Path.Combine(ev, "Windows-gather.db");
    }

    [Fact]
    public void A_row_that_lives_only_in_the_wal_appears_when_the_pair_is_vouched_for()
    {
        var db = WalPair("walv", 6);
        var item = Item(db, notes: "Instantaneu " + WindowsSearchGatherParser.GatherSameSnapshotNote, collector: WindowsSearchGatherParser.CollectedBy, method: "acquired");
        var shaDb = Sha(db); var shaWal = Sha(db + "-wal");
        var (r, _) = Run(db, item);
        Assert.Equal(6, r.Records);
        Assert.DoesNotContain(r.Gaps, g => g.Artifact == "Windows-gather.db-wal");
        Assert.Equal(shaDb, Sha(db)); Assert.Equal(shaWal, Sha(db + "-wal"));
    }

    [Fact]
    public void A_wal_beside_a_sound_database_is_applied_with_a_mixed_state_gap_when_the_pair_is_not_vouched_for()
    {
        var db = WalPair("waln", 4);
        var (r, _) = Run(db);
        Assert.Equal(4, r.Records);
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows-gather.db-wal" && g.Reason.Contains("amestecată"));
    }

    [Fact]
    public void A_wal_of_another_database_is_not_applied_and_leaves_a_gap()
    {
        var ev = Path.Combine(_dir, "foreign"); Directory.CreateDirectory(ev);
        var good = Make("foreign_src", c => { for (int i = 1; i <= 5; i++) Row(c, 17, $"f{i}.txt", i); });
        File.Copy(good, Path.Combine(ev, "Windows-gather.db"));
        var b = Path.Combine(_dir, "b.db");
        using (var c = new SqliteConnection($"Data Source={b};Pooling=False"))
        {
            c.Open();
            Exec(c, "PRAGMA journal_mode=WAL"); Exec(c, "PRAGMA wal_autocheckpoint=0");
            Exec(c, "CREATE TABLE other(x INTEGER)");
            for (int i = 0; i < 200; i++) Exec(c, $"INSERT INTO other VALUES({i})");
            CopyShared(b + "-wal", Path.Combine(ev, "Windows-gather.db-wal"));
        }
        SqliteConnection.ClearAllPools();
        var (r, _) = Run(Path.Combine(ev, "Windows-gather.db"));
        Assert.Equal(5, r.Records);
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows-gather.db-wal");
    }

    [Fact]
    public void An_imported_database_without_a_wal_gets_the_missing_journal_gap_unless_the_notes_say_so()
    {
        var db = Make("nowal", c => Row(c, 17, "x.txt", 1));
        var (r1, _) = Run(db, Item(db, method: "imported"));
        Assert.Contains(r1.Gaps, g => g.Artifact == "Windows-gather.db-wal" && g.Reason.Contains("fără jurnalul"));
        var (r2, _) = Run(db, Item(db, notes: WindowsSearchGatherParser.NoWalNote, method: "imported"));
        Assert.DoesNotContain(r2.Gaps, g => g.Artifact == "Windows-gather.db-wal");
    }

    // ---- P10, P11, P12 ----

    [Fact]
    public void The_stored_evidence_is_never_changed_and_the_working_copy_is_gone()
    {
        var db = Make("sha", c => Row(c, 17, "x.txt", 1, RealFiletime));
        var sha = Sha(db);
        var files = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(db)!).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Run(db);
        Assert.Equal(sha, Sha(db));
        Assert.Equal(files, Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(db)!).Select(Path.GetFileName).OrderBy(x => x).ToList());
    }

    [Fact]
    public void Other_schemes_stay_text_and_a_scope_pattern_is_flagged()
    {
        var db = Make("schemes", c =>
        {
            Row(c, 10, "Microsoft.WindowsCalculator_8wekyb3d8bbwe", 1);
            Pth(c, 80, 1, "mapi:"); Pth(c, 81, 80, "//");
            Row(c, 81, "{S-1-5-21-1}/Inbox/42", 2);
            Pth(c, 90, 17, "labadmin/AppData/");
            Row(c, 90, "*", 3);
        });
        var (_, s) = Run(db);
        var w = s.Events.Single(e => e.Fields["DocumentID"] == "1");
        Assert.Equal("winrt://Microsoft.WindowsCalculator_8wekyb3d8bbwe", w.Path);
        Assert.Equal("", w.Fields["ScopeGuid"]);
        Assert.Equal("da", w.Fields["PathComplete"]);
        Assert.Equal("mapi://{S-1-5-21-1}/Inbox/42", s.Events.Single(e => e.Fields["DocumentID"] == "2").Path);
        var p = s.Events.Single(e => e.Fields["DocumentID"] == "3");
        Assert.Equal("da", p.Fields["IsScopePattern"]);
        Assert.EndsWith(@"labadmin\AppData\*", p.Path);
    }

    [Theory]
    [InlineData("file:C:/[c59dc150-2d86-41f5-a432-c8cb4a9dc215]/Users/x/f.txt", @"C:\Users\x\f.txt", "c59dc150-2d86-41f5-a432-c8cb4a9dc215")]
    [InlineData("file:C:/Users/x/f.txt", @"C:\Users\x\f.txt", "")]
    [InlineData("file:C:/[not-a-guid]/Users/x", @"C:\[not-a-guid]\Users\x", "")]
    [InlineData("winrt://App_1", "winrt://App_1", "")]
    [InlineData("iehistory://{abc}/x", "iehistory://{abc}/x", "")]
    [InlineData("file:C:/[c59dc150-2d86-41f5-a432-c8cb4a9dc215]", @"C:\", "c59dc150-2d86-41f5-a432-c8cb4a9dc215")]
    public void The_url_conversion_removes_only_a_guid_component_right_after_the_drive(string url, string expected, string guid)
    {
        Assert.Equal(expected, WindowsSearchGatherParser.ToWindowsPath(url, out var g));
        Assert.Equal(guid, g);
    }

    // ---- registration, recognition and import ----

    [Fact]
    public void The_file_is_recognised_registered_and_described()
    {
        var db = Make("reg", c => Row(c, 17, "x.txt", 1));
        Assert.Equal(WindowsSearchGatherParser.SourceTypeName, InvestigationPipeline.ImportType(db));
        Assert.Equal("sqlite", EvidenceFingerprint.Detect(db));
        var notSqlite = Path.Combine(_dir, "n", "Windows-gather.db"); Directory.CreateDirectory(Path.GetDirectoryName(notSqlite)!);
        File.WriteAllText(notSqlite, "plain text");
        Assert.Equal("file", InvestigationPipeline.ImportType(notSqlite));

        var item = Item(db);
        var reg = WindowsParsers.Registry;
        Assert.Equal(["WindowsSearchGatherParser"], reg.Select(item, db).Parsers.Select(p => p.Descriptor.ParserId));
        var d = new WindowsSearchGatherParser().Descriptor;
        var all = string.Join("\n", d.Limitations);
        Assert.Contains("nu că fișierul mai există", all);
        Assert.Contains("ștergerea fișierului", all);
        Assert.Contains("TransactionFlags", all);
        Assert.Contains("Windows-usn.db", all);
        Assert.Equal(["Windows-gather.db"], d.FileNames);
        Assert.NotNull(d.Capabilities);
    }

    [Fact]
    public void An_imported_gather_database_brings_its_wal_into_one_folder_and_is_parsed()
    {
        var live = WalPair("imp", 3);
        var src = Path.Combine(_dir, "impsrc"); Directory.CreateDirectory(src);
        CopyShared(live, Path.Combine(src, "Windows-gather.db"));
        CopyShared(live + "-wal", Path.Combine(src, "Windows-gather.db-wal"));
        var ws = LogAnalyzer.Dfir.Case.CaseWorkspace.Create(Path.Combine(_dir, "case_imp"),
            new CaseInfo { CaseId = "C-IMPG", Name = "impg", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var items = InvestigationPipeline.Import(ws, [Path.Combine(src, "Windows-gather.db"), Path.Combine(src, "Windows-gather.db-wal")]);
        Assert.Equal(2, items.Count);
        var db = Assert.Single(items, i => i.SourceType == WindowsSearchGatherParser.SourceTypeName);
        var wal = Assert.Single(items, i => i.SourceType == WindowsSearchIndexCollector.WalSourceType);
        Assert.Equal(Path.GetDirectoryName(ws.FullPath(db.StoredPath)), Path.GetDirectoryName(ws.FullPath(wal.StoredPath)));
        Assert.EndsWith("Windows-gather.db-wal", wal.StoredPath);
        var res = new WindowsSearchGatherParser().Parse(db, ws.FullPath(db.StoredPath), new ListSink(), default);
        Assert.Equal(3, res.Records);
        Assert.Contains(res.Gaps, g => g.Artifact == "Windows-gather.db-wal" && g.Reason.Contains("amestecată"));

        // alone: the note says there was no journal
        var alone = Make("impalone", c => Row(c, 17, "x.txt", 1));
        var ws2 = LogAnalyzer.Dfir.Case.CaseWorkspace.Create(Path.Combine(_dir, "case_imp2"),
            new CaseInfo { CaseId = "C-IMPG2", Name = "impg2", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var one = Assert.Single(InvestigationPipeline.Import(ws2, [alone]));
        Assert.Contains(WindowsSearchGatherParser.NoWalNote, one.Notes);
    }
}
