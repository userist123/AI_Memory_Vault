using System.Security.Cryptography;
using System.Text;
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
/// Windows Search index (Windows 11, SQLite): synthetic databases with the real schema. The threat model (the database comes from the analysed station and can be
/// edited by an attacker): P1 corrupt / incomplete / other schema, P2 size caps and streaming, P3 unexpected value types and hostile FILETIME, P4 hidden characters in
/// paths, P5 AutoSummary never in the summary, P6 no name from the database in SQL text, P7 the stored evidence is never opened or changed, P8 another Windows.db.
/// </summary>
public sealed class WindowsSearchIndexParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_wsi_" + Guid.NewGuid().ToString("N"));
    public WindowsSearchIndexParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(_dir, true); }

    private static readonly string[] AllNames =
    [
        "System.ItemPathDisplay", "System.ItemUrl", "System.ItemFolderPathDisplay", "System.FileName", "System.DateModified", "System.DateCreated", "System.DateAccessed",
        "System.Search.GatherTime", "System.Size", "System.FileAttributes", "System.FileOwner", "System.ItemType", "System.IsFolder", "System.Search.Store", "System.Search.AutoSummary",
    ];

    /// <summary>Column ids by name: a different numbering per "machine" (offset and stride), as on real stations.</summary>
    private sealed class Layout(long offset, long stride, bool reverse = false)
    {
        public long Id(string name) => offset + stride * (reverse ? AllNames.Length - 1 - Array.IndexOf(AllNames, name) : Array.IndexOf(AllNames, name));
    }

    private static void Exec(SqliteConnection c, string sql, params (string N, object V)[] p)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static void Schema(SqliteConnection c, Layout l, IEnumerable<string>? names = null)
    {
        Exec(c, "CREATE TABLE SystemIndex_1_PropertyStore (WorkId INTEGER, ColumnId INTEGER, Value BLOB, PRIMARY KEY (WorkId, ColumnId)) WITHOUT ROWID");
        Exec(c, "CREATE TABLE SystemIndex_1_PropertyStore_Metadata (Id INTEGER PRIMARY KEY, UniqueKey TEXT, Name TEXT, PropertyId INTEGER, VariantType INTEGER, StorageType INTEGER, MaxSize INTEGER, Flags INTEGER)");
        foreach (var n in names ?? AllNames)
            Exec(c, "INSERT INTO SystemIndex_1_PropertyStore_Metadata (Id, UniqueKey, Name) VALUES ($i, $k, $n)", ("$i", l.Id(n)), ("$k", "k" + n), ("$n", n));
    }

    private static void Put(SqliteConnection c, Layout l, long work, string name, object value) =>
        Exec(c, "INSERT OR REPLACE INTO SystemIndex_1_PropertyStore (WorkId, ColumnId, Value) VALUES ($w, $c, $v)", ("$w", work), ("$c", l.Id(name)), ("$v", value));

    private static byte[] Ft(DateTime utc) => BitConverter.GetBytes(utc.ToFileTimeUtc());
    private static readonly DateTime Mod = new(2026, 11, 8, 10, 30, 0, DateTimeKind.Utc), Gat = new(2026, 11, 9, 1, 2, 3, DateTimeKind.Utc);

    private string MakeDb(string name, Layout l, Action<SqliteConnection> fill, IEnumerable<string>? names = null)
    {
        var p = Path.Combine(_dir, name);
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            Schema(c, l, names);
            using var tx = c.BeginTransaction();
            fill(c);
            tx.Commit();
        }
        SqliteConnection.ClearAllPools();
        return p;
    }

    private string Sub(string name) { var d = Path.Combine(_dir, name); Directory.CreateDirectory(d); return Path.Combine(d, "Windows.db"); }

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-W", CaseId = "C", Source = "test", SourceType = WindowsSearchIndexParser.SourceTypeName, StoredPath = path };

    private static (ParseResult R, ListSink S) Run(string path, int? cap = null)
    {
        var sink = new ListSink();
        IEvidenceParser p = cap is { } n ? new WindowsSearchIndexParser(n) : new WindowsSearchIndexParser();
        return (p.Parse(Item(path), path, sink, default), sink);
    }

    private static string Sha(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));

    private static void File1(SqliteConnection c, Layout l, long w, string path, DateTime? mod = null, object? size = null)
    {
        Put(c, l, w, "System.ItemPathDisplay", path);
        if (mod is { } m) Put(c, l, w, "System.DateModified", Ft(m));
        if (size is not null) Put(c, l, w, "System.Size", size);
    }

    // ---- the happy path and the column ids ----

    [Theory]
    [InlineData(10, 1, false)]
    [InlineData(5000, 7, true)]
    [InlineData(1, 3, false)]
    public void An_indexed_file_gives_one_event_with_all_fields_whatever_the_column_ids(long offset, long stride, bool reverse)
    {
        var l = new Layout(offset, stride, reverse);
        var db = MakeDb("Windows.db", l, c =>
        {
            Put(c, l, 1811, "System.ItemPathDisplay", @"C:\Users\ana\Documents\TEST-raport.txt");
            Put(c, l, 1811, "System.ItemFolderPathDisplay", @"C:\Users\ana\Documents");
            Put(c, l, 1811, "System.FileName", "TEST-raport.txt");
            Put(c, l, 1811, "System.DateModified", Ft(Mod)); Put(c, l, 1811, "System.DateCreated", Ft(Mod.AddDays(-1)));
            Put(c, l, 1811, "System.DateAccessed", Ft(Mod.AddHours(1))); Put(c, l, 1811, "System.Search.GatherTime", Ft(Gat));
            Put(c, l, 1811, "System.Size", 1234L); Put(c, l, 1811, "System.FileAttributes", 32L);
            Put(c, l, 1811, "System.FileOwner", @"LAB\ana"); Put(c, l, 1811, "System.ItemType", ".txt");
            Put(c, l, 1811, "System.IsFolder", 0L); Put(c, l, 1811, "System.Search.Store", "file");
        });
        var (r, s) = Run(db);
        Assert.Equal(1, r.Records);
        Assert.Empty(r.Gaps);
        var e = Assert.Single(s.Events);
        Assert.Equal(WindowsSearchIndexParser.EventSource, e.Source);
        Assert.Equal(@"C:\Users\ana\Documents\TEST-raport.txt", e.Path);
        Assert.Equal("1811", e.Fields["WorkId"]);
        Assert.Equal(@"C:\Users\ana\Documents", e.Fields["Folder"]);
        Assert.Equal("TEST-raport.txt", e.Fields["FileName"]);
        Assert.Equal("1234", e.Fields["Size"]);
        Assert.Equal("32", e.Fields["Attributes"]);
        Assert.Equal(@"LAB\ana", e.Fields["Owner"]);
        Assert.Equal(".txt", e.Fields["ItemType"]);
        Assert.Equal("nu", e.Fields["IsFolder"]);
        Assert.Equal("file", e.Fields["Store"]);
        Assert.Equal("2026-11-08T10:30:00.000Z", e.Fields["DateModified"]);
        Assert.Equal("2026-11-07T10:30:00.000Z", e.Fields["DateCreated"]);
        Assert.Equal("2026-11-08T11:30:00.000Z", e.Fields["DateAccessed"]);
        Assert.Equal("2026-11-09T01:02:03.000Z", e.Fields["GatherTime"]);
        Assert.Equal(Mod, e.Time.Utc!.Value.UtcDateTime);
        Assert.Contains("System.DateModified", e.TimeSemantics);
        Assert.Equal(@"Index Windows Search: C:\Users\ana\Documents\TEST-raport.txt (1234 octeți, modificat 2026-11-08T10:30:00.000Z)", e.Summary);
        Assert.Equal("SystemIndex_1_PropertyStore WorkId=1811", e.Locator);
    }

    [Fact]
    public void Time_is_DateModified_then_GatherTime_then_unknown_and_hostile_FILETIME_is_ignored()
    {
        var l = new Layout(100, 2);
        var db = MakeDb("Windows.db", l, c =>
        {
            File1(c, l, 1, @"C:\a\mod.txt", Mod); Put(c, l, 1, "System.Search.GatherTime", Ft(Gat));
            File1(c, l, 2, @"C:\a\gather.txt"); Put(c, l, 2, "System.Search.GatherTime", Ft(Gat));
            File1(c, l, 3, @"C:\a\none.txt");
            // P3: negative, zero, beyond the calendar, 4 and 16 bytes, text, real: none is a time; GatherTime is used when valid
            File1(c, l, 4, @"C:\a\neg.txt"); Put(c, l, 4, "System.DateModified", BitConverter.GetBytes(-5L)); Put(c, l, 4, "System.Search.GatherTime", Ft(Gat));
            File1(c, l, 5, @"C:\a\zero.txt"); Put(c, l, 5, "System.DateModified", BitConverter.GetBytes(0L));
            File1(c, l, 6, @"C:\a\big.txt"); Put(c, l, 6, "System.DateModified", BitConverter.GetBytes(long.MaxValue));
            File1(c, l, 7, @"C:\a\short.txt"); Put(c, l, 7, "System.DateModified", new byte[] { 1, 2, 3, 4 });
            File1(c, l, 8, @"C:\a\long.txt"); Put(c, l, 8, "System.DateModified", new byte[16]);
            File1(c, l, 9, @"C:\a\text.txt"); Put(c, l, 9, "System.DateModified", "2026-01-01"); Put(c, l, 9, "System.DateCreated", 3.5);
            File1(c, l, 10, @"C:\a\ulong.txt"); Put(c, l, 10, "System.DateModified", BitConverter.GetBytes(ulong.MaxValue));
        });
        var (r, s) = Run(db);
        Assert.Equal(10, r.Records);
        var by = s.Events.ToDictionary(e => e.Fields["WorkId"]);
        Assert.Equal(Mod, by["1"].Time.Utc!.Value.UtcDateTime);
        Assert.Equal(Gat, by["2"].Time.Utc!.Value.UtcDateTime);
        Assert.Contains("GatherTime", by["2"].TimeSemantics);
        Assert.False(by["3"].Time.IsKnown);
        Assert.Contains("no valid time", by["3"].TimeSemantics);
        Assert.Contains("necunoscută", by["3"].Summary);
        Assert.Equal(Gat, by["4"].Time.Utc!.Value.UtcDateTime);
        foreach (var w in new[] { "5", "6", "7", "8", "9", "10" }) { Assert.False(by[w].Time.IsKnown, w); Assert.Equal("", by[w].Fields["DateModified"]); }
        Assert.Equal("", by["9"].Fields["DateCreated"]);
    }

    [Fact]
    public void Values_of_every_storage_type_are_read_and_a_wrong_type_is_ignored_never_an_exception()
    {
        var l = new Layout(40, 3);
        var db = MakeDb("Windows.db", l, c =>
        {
            // path as UTF-16LE BLOB (VT 31 may come that way), with a trailing NUL
            Put(c, l, 1, "System.ItemPathDisplay", Encoding.Unicode.GetBytes("C:\\Users\\ana\\blob.txt\0"));
            Put(c, l, 1, "System.Size", BitConverter.GetBytes(77UL)); Put(c, l, 1, "System.FileAttributes", 2.0);
            Put(c, l, 1, "System.Search.Store", 5L);                       // INTEGER where TEXT is expected
            Put(c, l, 1, "System.IsFolder", "1");                          // TEXT where INTEGER is expected: parsed as a number
            // no ItemPathDisplay: the file URL is the path
            Put(c, l, 2, "System.ItemUrl", "file:C:/Users/ana/Downloads/AnyDesk.exe");
            Put(c, l, 2, "System.Size", "4096"); Put(c, l, 2, "System.FileOwner", new byte[] { 0xFF, 0xFE, 0x41 });   // odd-length BLOB
            Put(c, l, 3, "System.ItemUrl", "file:///C:/Users/ana/Desktop/x.pdf"); Put(c, l, 3, "System.Size", -4L);
            Put(c, l, 4, "System.ItemUrl", "iehistory://{x}/https://example.org/"); Put(c, l, 4, "System.Size", new byte[3]);
            Put(c, l, 5, "System.ItemUrl", "file://LAB-SRV/share/f.doc"); Put(c, l, 5, "System.Size", 1e30);
            Put(c, l, 6, "System.ItemPathDisplay", 12345L);               // a number as path: kept as text
            Put(c, l, 7, "System.ItemType", ".txt");                       // no path at all: no event
            Put(c, l, 8, "System.ItemPathDisplay", "");                   // empty path: no event
        });
        var (r, s) = Run(db);
        Assert.Equal(6, r.Records);
        var by = s.Events.ToDictionary(e => e.Fields["WorkId"]);
        Assert.Equal(@"C:\Users\ana\blob.txt", by["1"].Path);
        Assert.Equal("77", by["1"].Fields["Size"]);
        Assert.Equal("2", by["1"].Fields["Attributes"]);
        Assert.Equal("5", by["1"].Fields["Store"]);
        Assert.Equal("da", by["1"].Fields["IsFolder"]);
        Assert.Equal(@"C:\Users\ana\Downloads\AnyDesk.exe", by["2"].Path);
        Assert.Equal("4096", by["2"].Fields["Size"]);
        Assert.Equal(@"C:\Users\ana\Desktop\x.pdf", by["3"].Path);
        Assert.Equal("", by["3"].Fields["Size"]);        // negative: not a size
        Assert.Contains("mărime necunoscută", by["3"].Summary);
        Assert.Equal("iehistory://{x}/https://example.org/", by["4"].Path);
        Assert.Equal("", by["4"].Fields["Size"]);        // 3-byte BLOB
        Assert.Equal(@"\\LAB-SRV\share\f.doc", by["5"].Path);
        Assert.Equal("", by["5"].Fields["Size"]);        // beyond 64 bits
        Assert.Equal("12345", by["6"].Path);
    }

    [Fact]
    public void Text_values_are_capped_at_2048_characters_without_cutting_a_surrogate_pair()
    {
        var l = new Layout(1, 1);
        var longPath = "C:\\" + new string('a', 5000);
        var pairs = "C:\\" + string.Concat(Enumerable.Repeat("\U0001F600", 2000));   // 4000 UTF-16 units
        var db = MakeDb("Windows.db", l, c =>
        {
            Put(c, l, 1, "System.ItemPathDisplay", longPath);
            Put(c, l, 2, "System.ItemPathDisplay", pairs);
            Put(c, l, 3, "System.ItemPathDisplay", @"C:\x.txt"); Put(c, l, 3, "System.FileOwner", new string('o', 100_000));
        });
        var (r, s) = Run(db);
        Assert.Equal(3, r.Records);
        var by = s.Events.ToDictionary(e => e.Fields["WorkId"]);
        Assert.Equal(WindowsSearchIndexParser.MaxTextChars, by["1"].Path.Length);
        Assert.True(by["2"].Path.Length <= WindowsSearchIndexParser.MaxTextChars);
        Assert.False(char.IsHighSurrogate(by["2"].Path[^1]));
        Assert.Equal(WindowsSearchIndexParser.MaxTextChars, by["3"].Fields["Owner"].Length);
    }

    // ---- P2: caps and streaming ----

    [Fact]
    public void The_event_cap_stops_reading_and_writes_a_gap_without_an_exception()
    {
        var l = new Layout(1, 1);
        var db = MakeDb("Windows.db", l, c => { for (int i = 1; i <= 25; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod); });
        var (r, s) = Run(db, cap: 10);
        Assert.Equal(10, r.Records);
        Assert.Equal(10, s.Events.Count);
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        var gap = Assert.Single(r.Gaps);
        Assert.Contains("10", gap.Reason);
        Assert.Equal(500_000, WindowsSearchIndexParser.MaxEvents);
        Assert.Equal(2L * 1024 * 1024 * 1024, new WindowsSearchIndexParser().Descriptor.MaxInputBytes);
    }

    [Fact]
    public void Huge_values_in_the_database_are_cut_inside_SQLite_and_many_rows_stream()
    {
        var l = new Layout(1, 1);
        var db = MakeDb("Windows.db", l, c =>
        {
            File1(c, l, 1, @"C:\a\big.txt", Mod);
            Put(c, l, 1, "System.Search.AutoSummary", new string('z', 3_000_000));
            Put(c, l, 1, "System.FileOwner", new byte[6_000_000]);
            for (int i = 2; i <= 3000; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i);
        });
        var (r, s) = Run(db);
        Assert.Equal(3000, r.Records);
        var big = s.Events.First(e => e.Fields["WorkId"] == "1");
        Assert.Equal(WindowsSearchIndexParser.MaxAutoSummaryChars, big.Fields["AutoSummary"].Length);
        Assert.True(big.Fields["Owner"].Length <= WindowsSearchIndexParser.MaxTextChars);
    }

    // ---- P4 / P5: hidden characters, AutoSummary ----

    [Fact]
    public void A_hostile_path_is_sanitised_in_Path_Fields_and_Summary()
    {
        var l = new Layout(1, 1);
        var evil = "C:\\Users\\ana\\gpj.\u202Etxt\r\n[FAKE] eveniment fals\u0007\tx\U000E0041";
        var db = MakeDb("Windows.db", l, c => { Put(c, l, 1, "System.ItemPathDisplay", evil); Put(c, l, 1, "System.FileOwner", "a\u202Eb\r\nc"); Put(c, l, 1, "System.FileName", "n\u202Em\nx"); });
        var (_, s) = Run(db);
        var e = Assert.Single(s.Events);
        foreach (var text in new[] { e.Path, e.Summary, e.Fields["Path"], e.Fields["Owner"], e.Fields["FileName"] })
        {
            Assert.DoesNotContain('\u202E', text);
            Assert.DoesNotContain('\r', text);
            Assert.DoesNotContain('\n', text);
            Assert.DoesNotContain('\u0007', text);
            Assert.DoesNotContain(text, ch => char.IsSurrogate(ch));
        }
        Assert.Contains("U+202E", e.Path);   // the removal is named, not silent
    }

    [Fact]
    public void AutoSummary_is_in_its_own_sensitive_field_only_never_in_the_summary_or_a_hash()
    {
        var l = new Layout(1, 1);
        var secret = "parola=SuperSecret123 \u202E\U000E0041 sfarsit";
        var db = MakeDb("Windows.db", l, c => { File1(c, l, 1, @"C:\Users\ana\Documents\note.txt", Mod, 10L); Put(c, l, 1, "System.Search.AutoSummary", secret); File1(c, l, 2, @"C:\Users\ana\Documents\gol.txt", Mod); });
        var (_, s) = Run(db);
        var e = s.Events.Single(x => x.Fields["WorkId"] == "1");
        Assert.StartsWith("parola=SuperSecret123", e.Fields["AutoSummary"]);
        Assert.DoesNotContain('\u202E', e.Fields["AutoSummary"]);
        Assert.DoesNotContain(e.Fields["AutoSummary"], ch => char.IsSurrogate(ch));
        Assert.Contains("probă sensibilă", e.Fields["AutoSummary Sensitivity"]);
        Assert.Contains("nu apare în rezumat", e.Fields["AutoSummary Sensitivity"]);
        foreach (var (_, v) in e.Fields.Where(f => f.Key != "AutoSummary")) Assert.DoesNotContain("SuperSecret", v);
        Assert.DoesNotContain("SuperSecret", e.Summary);
        Assert.DoesNotContain("SuperSecret", e.Path + e.Locator + e.Notes + e.TimeSemantics);
        Assert.DoesNotContain("AutoSummary", e.Summary);
        Assert.DoesNotContain("SHA", e.Summary, StringComparison.OrdinalIgnoreCase);
        var none = s.Events.Single(x => x.Fields["WorkId"] == "2");
        Assert.False(none.Fields.ContainsKey("AutoSummary"));
        Assert.False(none.Fields.ContainsKey("AutoSummary Sensitivity"));
    }

    // ---- P1: corrupt, incomplete, other schema ----

    [Fact]
    public void A_file_that_is_not_a_database_and_a_truncated_database_give_a_failure_not_an_exception()
    {
        var junk = Sub("junk");
        File.WriteAllBytes(junk, Encoding.ASCII.GetBytes("this is not an sqlite database at all, just text " + new string('x', 5000)));
        var (r, s) = Run(junk);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Empty(s.Events);
        Assert.NotEmpty(r.Gaps);

        var l = new Layout(1, 1);
        var good = MakeDb("good.db", l, c => { for (int i = 1; i <= 800; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i); });
        var bytes = File.ReadAllBytes(good);
        foreach (var keep in new[] { 100, 4096, bytes.Length / 2, bytes.Length - 1000 })
        {
            var cut = Sub("cut" + keep);
            File.WriteAllBytes(cut, bytes[..keep]);
            var (r2, s2) = Run(cut);                       // whatever SQLite makes of it: never an unchecked exception
            Assert.True(r2.Status is EvidenceStatus.Failed or EvidenceStatus.Partial or EvidenceStatus.Success or EvidenceStatus.Empty, r2.Status.ToString());
            Assert.True(s2.Events.Count <= 800);
        }
    }

    [Fact]
    public void A_corrupted_page_in_the_middle_keeps_what_was_read_and_never_throws()
    {
        var l = new Layout(1, 1);
        var good = MakeDb("good.db", l, c => { for (int i = 1; i <= 3000; i++) { File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i); Put(c, l, i, "System.FileOwner", new string('o', 300)); } });
        var bytes = File.ReadAllBytes(good);
        for (int i = 4096 * 20; i < 4096 * 30 && i < bytes.Length; i++) bytes[i] = (byte)(i * 31);
        var bad = Sub("bad");
        File.WriteAllBytes(bad, bytes);
        var (r, s) = Run(bad);
        Assert.True(r.Status is EvidenceStatus.Failed or EvidenceStatus.Partial or EvidenceStatus.Success, r.Status.ToString());
        if (r.Status != EvidenceStatus.Success) Assert.True(r.Gaps.Count > 0 || r.Error.Length > 0);
        Assert.True(s.Events.Count <= 3000);
    }

    [Fact]
    public void Missing_tables_missing_columns_and_a_metadata_table_without_paths_give_a_gap()
    {
        var oneTable = Sub("t1");
        using (var c = new SqliteConnection($"Data Source={oneTable};Pooling=False")) { c.Open(); Exec(c, "CREATE TABLE SystemIndex_1_PropertyStore (WorkId INTEGER, ColumnId INTEGER, Value BLOB, PRIMARY KEY (WorkId, ColumnId)) WITHOUT ROWID"); }
        SqliteConnection.ClearAllPools();
        var (r1, s1) = Run(oneTable);
        Assert.Equal(EvidenceStatus.Failed, r1.Status);
        Assert.Empty(s1.Events);
        Assert.Contains("nu este un index", r1.Error);

        var wrongCols = Sub("t2");
        using (var c = new SqliteConnection($"Data Source={wrongCols};Pooling=False"))
        {
            c.Open();
            Exec(c, "CREATE TABLE SystemIndex_1_PropertyStore (A INTEGER, B INTEGER, C BLOB)");
            Exec(c, "CREATE TABLE SystemIndex_1_PropertyStore_Metadata (Id INTEGER PRIMARY KEY, Name TEXT)");
            Exec(c, "INSERT INTO SystemIndex_1_PropertyStore_Metadata VALUES (1, 'System.ItemPathDisplay')");
        }
        SqliteConnection.ClearAllPools();
        var (r2, s2) = Run(wrongCols);
        Assert.Equal(EvidenceStatus.Failed, r2.Status);
        Assert.Empty(s2.Events);
        Assert.NotEmpty(r2.Gaps);

        var noPaths = MakeDb("t3.db", new Layout(1, 1), c => { }, names: ["System.Size", "System.FileName"]);
        var (r3, s3) = Run(noPaths);
        Assert.Empty(s3.Events);
        Assert.Contains(r3.Gaps, g => g.Reason.Contains("System.ItemPathDisplay"));
    }

    // ---- P6: no SQL injection ----

    [Fact]
    public void Names_and_values_from_the_database_never_become_SQL_text()
    {
        var l = new Layout(1, 1);
        var db = MakeDb("Windows.db", l, c =>
        {
            File1(c, l, 1, @"C:\a\ok.txt", Mod, 5L);
            Put(c, l, 1, "System.ItemType", "x'); DROP TABLE SystemIndex_1_PropertyStore; --");
            Put(c, l, 1, "System.FileName", "\"; DELETE FROM SystemIndex_1_PropertyStore; --");
            Exec(c, "INSERT INTO SystemIndex_1_PropertyStore_Metadata (Id, Name) VALUES (9001, 'System.Size''; DROP TABLE SystemIndex_1_PropertyStore; --')");
            Exec(c, "INSERT INTO SystemIndex_1_PropertyStore_Metadata (Id, Name) VALUES (9002, 'x\" OR 1=1; --')");
            Exec(c, "INSERT INTO SystemIndex_1_PropertyStore (WorkId, ColumnId, Value) VALUES (2, 9001, 'C:\\a\\evil.txt')");
            Exec(c, "INSERT INTO SystemIndex_1_PropertyStore (WorkId, ColumnId, Value) VALUES (2, 9002, 'C:\\a\\evil2.txt')");
        });
        var before = Sha(db);
        var (r, s) = Run(db);
        Assert.Equal(1, r.Records);                       // WorkId 2 has no known property: no event
        Assert.Equal(before, Sha(db));
        using (var c = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT count(*) FROM SystemIndex_1_PropertyStore";
            Assert.Equal(7L, (long)cmd.ExecuteScalar()!);
        }
        SqliteConnection.ClearAllPools();
        Assert.StartsWith("x'); DROP", Assert.Single(s.Events).Fields["ItemType"]);
    }

    // ---- P7: the stored evidence is not opened or changed ----

    [Fact]
    public void The_stored_evidence_keeps_its_SHA256_and_its_folder_gets_no_new_file_even_when_read_only()
    {
        var l = new Layout(1, 1);
        var db = MakeDb("Windows.db", l, c => { for (int i = 1; i <= 50; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i); });
        File.SetAttributes(db, FileAttributes.ReadOnly);
        var before = Sha(db);
        var names = Directory.EnumerateFileSystemEntries(_dir).Select(Path.GetFileName).OrderBy(x => x).ToList();
        var (r, _) = Run(db);
        Assert.Equal(50, r.Records);
        Assert.Equal(before, Sha(db));
        Assert.Equal(names, Directory.EnumerateFileSystemEntries(_dir).Select(Path.GetFileName).OrderBy(x => x).ToList());
        Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(db) & FileAttributes.ReadOnly);
    }

    private static void CopyShared(string from, string to)
    {
        using var f = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var t = new FileStream(to, FileMode.Create, FileAccess.Write);
        f.CopyTo(t);
    }

    [Fact]
    public void The_wal_and_shm_beside_the_database_are_applied_on_the_working_copy_only()
    {
        var live = Path.Combine(_dir, "live.db");
        var ev = Path.Combine(_dir, "ev"); Directory.CreateDirectory(ev);
        var l = new Layout(1, 1);
        using (var c = new SqliteConnection($"Data Source={live};Pooling=False"))
        {
            c.Open();
            Exec(c, "PRAGMA journal_mode=WAL"); Exec(c, "PRAGMA wal_autocheckpoint=0");
            Schema(c, l);
            for (int i = 1; i <= 20; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i);
            Assert.True(File.Exists(live + "-wal"));
            CopyShared(live, Path.Combine(ev, "Windows.db"));
            CopyShared(live + "-wal", Path.Combine(ev, "Windows.db-wal"));
            CopyShared(live + "-shm", Path.Combine(ev, "Windows.db-shm"));
        }
        SqliteConnection.ClearAllPools();
        var stored = Path.Combine(ev, "Windows.db");
        var shaDb = Sha(stored); var shaWal = Sha(stored + "-wal");
        var files = Directory.EnumerateFileSystemEntries(ev).Select(Path.GetFileName).OrderBy(x => x).ToList();
        var (r, _) = Run(stored);
        Assert.Equal(20, r.Records);                       // the rows exist only in the -wal
        Assert.Equal(shaDb, Sha(stored)); Assert.Equal(shaWal, Sha(stored + "-wal"));
        Assert.Equal(files, Directory.EnumerateFileSystemEntries(ev).Select(Path.GetFileName).OrderBy(x => x).ToList());
    }

    /// <summary>A shadow copy for the tests: Create freezes the index folder as it is at that moment into a mirror folder.</summary>
    private sealed class FakeSnapshot(string mirrorRoot, string indexDir, bool fail = false, bool failDelete = false) : WindowsSearchIndexCollector.ISnapshot
    {
        public List<string> Deleted { get; } = [];
        public (string Id, string DeviceObject) Create(string volumeRoot)
        {
            if (fail) throw new InvalidOperationException("Win32_ShadowCopy.Create a întors 5");
            var frozen = mirrorRoot.TrimEnd('\\') + indexDir[(Path.GetPathRoot(indexDir)!.Length - 1)..];
            Directory.CreateDirectory(frozen);
            foreach (var f in Directory.GetFiles(indexDir)) CopyShared(f, Path.Combine(frozen, Path.GetFileName(f)));
            return ("{SNAP-1}", mirrorRoot);
        }
        public void Delete(string id) { if (failDelete) throw new InvalidOperationException("acces refuzat"); Deleted.Add(id); }
    }

    private (CollectorOutcome O, LogAnalyzer.Dfir.Case.CaseWorkspace Ws) CollectWith(WindowsSearchIndexCollector.ISnapshot snap, string indexDir)
    {
        var ws = LogAnalyzer.Dfir.Case.CaseWorkspace.Create(Path.Combine(_dir, "case_" + Guid.NewGuid().ToString("N")),
            new CaseInfo { CaseId = "C-WSI", Name = "wsi", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var o = new WindowsSearchIndexCollector(snap, indexDir).Collect(new CollectorContext { Case = ws, IsElevated = true }, default);
        return (o, ws);
    }

    private string LiveIndex(string name, out SqliteConnection open)
    {
        var dir = Path.Combine(_dir, name); Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "Windows.db");
        var l = new Layout(1, 1);
        open = new SqliteConnection($"Data Source={db};Pooling=False");
        open.Open();
        Exec(open, "PRAGMA journal_mode=WAL"); Exec(open, "PRAGMA wal_autocheckpoint=0");
        Schema(open, l);
        for (int i = 1; i <= 7; i++) File1(open, l, i, $@"C:\a\f{i}.txt", Mod, (long)i);
        return dir;
    }

    [Fact]
    public void The_collector_takes_the_database_and_its_wal_from_one_shadow_copy_and_deletes_it()
    {
        var dir = LiveIndex("live1", out var open);
        using (open)
        {
            var snap = new FakeSnapshot(Path.Combine(_dir, "snap1"), dir);
            var (o, ws) = CollectWith(snap, dir);
            Assert.Equal(["{SNAP-1}"], snap.Deleted);
            var db = Assert.Single(o.Evidence, e => e.SourceType == WindowsSearchIndexParser.SourceTypeName);
            Assert.Contains(o.Evidence, e => e.SourceType == WindowsSearchIndexCollector.WalSourceType);
            Assert.Contains(WindowsSearchIndexParser.SameSnapshotNote, db.Notes);
            Assert.All(o.Evidence, e => Assert.Contains("{SNAP-1}", e.Notes));
            // the pair from one snapshot is read whole, without the "possibly mixed" gap
            var (r, _) = Run(ws.FullPath(db.StoredPath));
            var stored = ws.FullPath(db.StoredPath);
            var sink = new ListSink();
            var res = new WindowsSearchIndexParser().Parse(db, stored, sink, default);
            Assert.Equal(7, res.Records);
            Assert.DoesNotContain(res.Gaps, g => g.Artifact == "Windows.db-wal");
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Without_a_common_shadow_copy_the_wal_and_shm_are_never_collected()
    {
        var dir = LiveIndex("live2", out var open);
        using (open)
        {
            var (o, _) = CollectWith(new FakeSnapshot(Path.Combine(_dir, "snap2"), dir, fail: true), dir);
            Assert.DoesNotContain(o.Evidence, e => e.SourceType is WindowsSearchIndexCollector.WalSourceType or WindowsSearchIndexCollector.ShmSourceType);
            Assert.Contains(o.Errors, e => e.Contains("copia de umbră comună nu a putut fi creată"));
            Assert.Contains(o.Errors, e => e.StartsWith("Windows.db-wal: necopiat"));
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void A_shadow_copy_that_cannot_be_deleted_is_reported_with_the_command_to_remove_it()
    {
        var dir = LiveIndex("live3", out var open);
        using (open)
        {
            var (o, _) = CollectWith(new FakeSnapshot(Path.Combine(_dir, "snap3"), dir, failDelete: true), dir);
            Assert.Contains(o.Errors, e => e.Contains("{SNAP-1}") && e.Contains("vssadmin delete shadows"));
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void A_wal_of_the_same_database_from_a_later_moment_is_applied_with_a_mixed_state_gap_when_the_pair_is_not_vouched_for()
    {
        // review r1: database copied, then UPDATE + checkpoint TRUNCATE + UPDATE, then the -wal copied: quick_check passes, state never existed
        var live = Path.Combine(_dir, "mix.db");
        var ev = Path.Combine(_dir, "mixev"); Directory.CreateDirectory(ev);
        var l = new Layout(1, 1);
        using (var c = new SqliteConnection($"Data Source={live};Pooling=False"))
        {
            c.Open();
            Exec(c, "PRAGMA journal_mode=WAL"); Exec(c, "PRAGMA wal_autocheckpoint=0");
            Schema(c, l);
            for (int i = 1; i <= 3; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i);
            Exec(c, "PRAGMA wal_checkpoint(TRUNCATE)");
            CopyShared(live, Path.Combine(ev, "Windows.db"));
            for (int i = 4; i <= 6; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i);
            Exec(c, "PRAGMA wal_checkpoint(TRUNCATE)");
            for (int i = 7; i <= 8; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i);
            CopyShared(live + "-wal", Path.Combine(ev, "Windows.db-wal"));
        }
        SqliteConnection.ClearAllPools();
        var (r, _) = Run(Path.Combine(ev, "Windows.db"));
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows.db-wal" && g.Reason.Contains("amestecată"));
    }

    [Fact]
    public void An_imported_index_brings_its_wal_into_the_same_folder_and_the_pair_is_marked_as_possibly_mixed()
    {
        var dir = LiveIndex("imp1", out var open);
        using (open)
        {
            var ws = LogAnalyzer.Dfir.Case.CaseWorkspace.Create(Path.Combine(_dir, "case_imp1"),
                new CaseInfo { CaseId = "C-IMP", Name = "imp", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
            var src = Path.Combine(_dir, "imp1src"); Directory.CreateDirectory(src);
            CopyShared(Path.Combine(dir, "Windows.db"), Path.Combine(src, "Windows.db"));
            CopyShared(Path.Combine(dir, "Windows.db-wal"), Path.Combine(src, "Windows.db-wal"));
            // the operator selects both files: the journal is taken with the database, not imported twice
            var items = InvestigationPipeline.Import(ws, [Path.Combine(src, "Windows.db"), Path.Combine(src, "Windows.db-wal")]);
            Assert.Equal(2, items.Count);
            var db = Assert.Single(items, i => i.SourceType == WindowsSearchIndexParser.SourceTypeName);
            var wal = Assert.Single(items, i => i.SourceType == WindowsSearchIndexCollector.WalSourceType);
            Assert.Equal(Path.GetDirectoryName(ws.FullPath(db.StoredPath)), Path.GetDirectoryName(ws.FullPath(wal.StoredPath)));
            var res = new WindowsSearchIndexParser().Parse(db, ws.FullPath(db.StoredPath), new ListSink(), default);
            Assert.Equal(7, res.Records);                                                   // the rows live only in the journal
            Assert.Contains(res.Gaps, g => g.Artifact == "Windows.db-wal" && g.Reason.Contains("amestecată"));
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void An_imported_index_without_a_wal_says_so_in_its_notes_and_gets_no_missing_journal_gap()
    {
        var l = new Layout(1, 1);
        var src = Path.Combine(_dir, "imp2src"); Directory.CreateDirectory(src);
        var db = Path.Combine(src, "Windows.db");
        using (var c = new SqliteConnection($"Data Source={db};Pooling=False")) { c.Open(); Schema(c, l); File1(c, l, 1, @"C:\a\x.txt", Mod, 1L); }
        SqliteConnection.ClearAllPools();
        var ws = LogAnalyzer.Dfir.Case.CaseWorkspace.Create(Path.Combine(_dir, "case_imp2"),
            new CaseInfo { CaseId = "C-IMP2", Name = "imp2", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var item = Assert.Single(InvestigationPipeline.Import(ws, [db]));
        Assert.Contains(WindowsSearchIndexParser.NoWalNote, item.Notes);
        var res = new WindowsSearchIndexParser().Parse(item, ws.FullPath(item.StoredPath), new ListSink(), default);
        Assert.Equal(1, res.Records);
        Assert.DoesNotContain(res.Gaps, g => g.Artifact == "Windows.db-wal");
    }

    [Fact]
    public void A_Windows_collation_in_the_schema_does_not_make_a_sound_index_look_damaged()
    {
        // lab, real package from one shadow copy: CatalogStorageManager uses COLLATE "UNICODE_en-US"; quick_check failed with
        // "no such collation sequence", the journal was dropped and the database was reported as unsound
        var dir = Path.Combine(_dir, "coll"); Directory.CreateDirectory(dir);
        var live = Path.Combine(dir, "Windows.db");
        var ev = Path.Combine(_dir, "collev"); Directory.CreateDirectory(ev);
        var l = new Layout(1, 1);
        using (var c = new SqliteConnection($"Data Source={live};Pooling=False"))
        {
            c.Open();
            c.CreateCollation("UNICODE_en-US", (a, b) => string.CompareOrdinal(a, b));
            Exec(c, "PRAGMA journal_mode=WAL"); Exec(c, "PRAGMA wal_autocheckpoint=0");
            Schema(c, l);
            Exec(c, "CREATE TABLE CatalogStorageManager (CatalogName TEXT DEFAULT NULL, CatalogID INTEGER PRIMARY KEY, UNIQUE (CatalogName COLLATE \"UNICODE_en-US\" ASC, CatalogID ASC))");
            Exec(c, "INSERT INTO CatalogStorageManager VALUES('SystemIndex', 1)");
            for (int i = 1; i <= 4; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i);
            CopyShared(live, Path.Combine(ev, "Windows.db"));
            CopyShared(live + "-wal", Path.Combine(ev, "Windows.db-wal"));
        }
        SqliteConnection.ClearAllPools();
        var (r, _) = Run(Path.Combine(ev, "Windows.db"));
        Assert.Equal(4, r.Records);
        Assert.DoesNotContain(r.Gaps, g => g.Artifact == "Windows.db");
        Assert.DoesNotContain(r.Gaps, g => g.Reason.Contains("nu se potrivește"));
    }

    [Fact]
    public void A_database_that_fails_quick_check_is_never_read_silently()
    {
        var l = new Layout(1, 1);
        var path = Sub("unsound");
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open(); Schema(c, l);
            Exec(c, "CREATE TABLE pad(x BLOB)"); Exec(c, "CREATE INDEX pad_i ON pad(x)");
            for (int i = 0; i < 400; i++) Exec(c, "INSERT INTO pad VALUES(randomblob(900))");
            File1(c, l, 1, @"C:\a\x.txt", Mod, 1L);
        }
        SqliteConnection.ClearAllPools();
        // damage pages in the middle of the file (the padding index), leaving the header and the schema page intact
        var bytes = File.ReadAllBytes(path);
        for (int i = bytes.Length / 2; i < bytes.Length / 2 + 8192 && i < bytes.Length; i++) bytes[i] = 0x5A;
        File.WriteAllBytes(path, bytes);
        var (r, _) = Run(path);
        Assert.True(r.Gaps.Any(g => g.Artifact == "Windows.db"), "an unsound database must leave a written gap");
    }

    [Fact]
    public void A_wal_that_does_not_fit_the_database_is_dropped_with_a_gap_and_the_database_is_still_read()
    {
        // the collector copies Windows.db and -wal through two VSS snapshots: a journal of another state must not break the read
        var l = new Layout(1, 1);
        var ev = Path.Combine(_dir, "ev2"); Directory.CreateDirectory(ev);
        var a = Path.Combine(_dir, "a.db");
        using (var c = new SqliteConnection($"Data Source={a};Pooling=False"))
        {
            c.Open(); Schema(c, l);
            for (int i = 1; i <= 5; i++) File1(c, l, i, $@"C:\a\f{i}.txt", Mod, (long)i);
        }
        var b = Path.Combine(_dir, "b.db");
        using (var c = new SqliteConnection($"Data Source={b};Pooling=False"))
        {
            c.Open();
            Exec(c, "PRAGMA journal_mode=WAL"); Exec(c, "PRAGMA wal_autocheckpoint=0");
            Exec(c, "CREATE TABLE other(x INTEGER)");
            for (int i = 0; i < 200; i++) Exec(c, $"INSERT INTO other VALUES({i})");
            CopyShared(b + "-wal", Path.Combine(ev, "Windows.db-wal"));
        }
        SqliteConnection.ClearAllPools();
        File.Copy(a, Path.Combine(ev, "Windows.db"));
        var (r, _) = Run(Path.Combine(ev, "Windows.db"));
        Assert.Equal(5, r.Records);
        Assert.Contains(r.Gaps, g => g.Artifact == "Windows.db-wal");
    }

    // ---- P8 and the recognition of the file ----

    [Fact]
    public void Another_Windows_db_is_not_read_as_an_index()
    {
        var p = Sub("other");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            Exec(c, "CREATE TABLE notes (id INTEGER PRIMARY KEY, path TEXT)");
            Exec(c, "INSERT INTO notes VALUES (1, 'C:\\secret.txt')");
        }
        SqliteConnection.ClearAllPools();
        var (r, s) = Run(p);
        Assert.Equal(EvidenceStatus.Failed, r.Status);
        Assert.Empty(s.Events);
        Assert.Equal(0, r.Records);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("nu este indexul Windows Search"));
    }

    [Fact]
    public void ImportType_types_only_a_Windows_db_with_an_sqlite_header_and_leaves_Chromium_history_alone()
    {
        var l = new Layout(1, 1);
        var idx = MakeDb("Windows.db", l, c => File1(c, l, 1, @"C:\a\x.txt", Mod));
        Assert.Equal(WindowsSearchIndexParser.SourceTypeName, InvestigationPipeline.ImportType(idx));
        Assert.Equal("sqlite", EvidenceFingerprint.Detect(idx));

        var hist = Path.Combine(Path.GetDirectoryName(Sub("h"))!, "History");
        File.Copy(idx, hist);
        Assert.Equal("chromium_history", InvestigationPipeline.ImportType(hist));

        var notSqlite = Sub("n");
        File.WriteAllText(notSqlite, "plain text");
        Assert.NotEqual(WindowsSearchIndexParser.SourceTypeName, InvestigationPipeline.ImportType(notSqlite));
        var other = Path.Combine(Path.GetDirectoryName(Sub("o"))!, "Windows-gather.db");
        File.Copy(idx, other);
        Assert.NotEqual(WindowsSearchIndexParser.SourceTypeName, InvestigationPipeline.ImportType(other));

        // the registry routes the typed file to this parser; the Chromium history item is never offered to it
        var reg = WindowsParsers.Registry;
        var item = Item(idx);
        Assert.Contains(reg.Candidates(item), p => p.Descriptor.ParserId == "WindowsSearchIndexParser");
        Assert.Equal(["WindowsSearchIndexParser"], reg.Select(item, idx).Parsers.Select(p => p.Descriptor.ParserId));
        var histItem = new EvidenceItem { EvidenceId = "EV-H", CaseId = "C", Source = "t", SourceType = "chromium_history", StoredPath = hist };
        Assert.DoesNotContain(reg.Candidates(histItem), p => p.Descriptor.ParserId == "WindowsSearchIndexParser");
    }

    [Fact]
    public void The_descriptor_states_the_limitations()
    {
        var d = new WindowsSearchIndexParser().Descriptor;
        var all = string.Join("\n", d.Limitations);
        Assert.Contains("locurile indexate", all);
        Assert.Contains("nu că mai există", all);
        Assert.Contains("Fișierele șterse", all);
        Assert.Contains("Windows.edb", all);
        Assert.Contains("Windows-gather.db", all);
        Assert.NotNull(d.Capabilities);
    }

    // ---- the collector's plan (the VSS copy itself needs a station) ----

    [Fact]
    public void The_collector_plans_the_database_with_its_companions_and_notes_edb_as_evidence()
    {
        var dir = Path.Combine(_dir, "col"); Directory.CreateDirectory(dir);
        var o = new CollectorOutcome();
        Assert.Empty(WindowsSearchIndexCollector.Plan(dir, 1000, o));   // empty folder: nothing, and not an error
        Assert.Empty(o.Errors);
        foreach (var n in new[] { "Windows.db", "Windows.db-wal", "Windows.db-shm", "Windows.edb" }) File.WriteAllBytes(Path.Combine(dir, n), new byte[10]);
        var plan = WindowsSearchIndexCollector.Plan(dir, 1000, o);
        Assert.Equal(["Windows.db", "Windows.db-wal", "Windows.db-shm", "Windows.edb"], plan.Select(p => p.FileName));
        Assert.Equal(WindowsSearchIndexParser.SourceTypeName, plan[0].SourceType);
        Assert.Equal(WindowsSearchIndexCollector.EdbSourceType, plan[3].SourceType);
        Assert.Contains("nu există parser", plan[3].Note);
        Assert.Empty(o.Errors);
        Assert.True(new WindowsSearchIndexCollector().RequiresElevation);
    }

    [Fact]
    public void The_collector_does_not_copy_a_file_over_the_cap_and_does_not_take_companions_without_a_database()
    {
        var dir = Path.Combine(_dir, "col2"); Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "Windows.db"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(dir, "Windows.db-wal"), new byte[10]);
        var o = new CollectorOutcome();
        Assert.Empty(WindowsSearchIndexCollector.Plan(dir, 1000, o));   // the database is over the cap: the companions are useless alone
        Assert.Contains(o.Errors, e => e.Contains("Windows.db") && e.Contains("plafon"));
        Assert.Equal(2L * 1024 * 1024 * 1024, WindowsSearchIndexCollector.MaxFileBytes);

        var dir3 = Path.Combine(_dir, "col3"); Directory.CreateDirectory(dir3);
        File.WriteAllBytes(Path.Combine(dir3, "Windows.db-wal"), new byte[10]);
        var o3 = new CollectorOutcome();
        Assert.Empty(WindowsSearchIndexCollector.Plan(dir3, 1000, o3));
        Assert.Empty(o3.Errors);
    }

    [Fact]
    public void The_collector_does_not_follow_a_junction_to_the_index_folder()
    {
        var real = Path.Combine(_dir, "real"); Directory.CreateDirectory(real);
        File.WriteAllBytes(Path.Combine(real, "Windows.db"), new byte[10]);
        var link = Path.Combine(_dir, "link");
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{real}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!)
        { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
        Assert.True(Directory.Exists(link), "mklink /J did not create the junction");
        var o = new CollectorOutcome();
        Assert.Empty(WindowsSearchIndexCollector.Plan(link, 1000, o));
        Assert.Contains(o.Errors, e => e.Contains("nu este urmată"));
        Directory.Delete(link);
    }
}
