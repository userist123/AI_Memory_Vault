using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Microsoft.Data.Sqlite;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Windows Search index of Windows 11 (SQLite): <c>%ProgramData%\Microsoft\Search\Data\Applications\Windows\Windows.db</c>. One event per indexed item that has a path.
/// The property names are resolved through <c>SystemIndex_1_PropertyStore_Metadata</c> (the column ids differ from one machine to another, no number is fixed in code);
/// the values come from <c>SystemIndex_1_PropertyStore</c> in one ordered pass, so a database with millions of rows is never held in memory. Read from a working copy inside the
/// case (with -wal / -shm when they were collected); the stored evidence is never opened. The database is untrusted: no name from it enters SQL text, every value is capped
/// and type-checked, and System.Search.AutoSummary (a fragment of the file's content) stays in its own sensitive field, never in the summary.
/// </summary>
public sealed class WindowsSearchIndexParser : EvidenceParserBase
{
    public const string SourceTypeName = "windows_search_index";
    public const string EventSource = "WindowsSearch";
    public const string CollectedBy = "WindowsSearchIndexCollector";
    public const string DatabaseFileName = "Windows.db";
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Written by the collector into the evidence notes when Windows.db and its -wal come from one shadow copy.</summary>
    /// <summary>Written by the import when no Windows.db-wal was beside the imported Windows.db.</summary>
    public const string NoWalNote = "Lângă bază nu exista un jurnal Windows.db-wal.";

    public const string SameSnapshotNote ="aceeași pentru Windows.db, -wal și -shm";
    public const int MaxEvents = 500_000;
    public const int MaxTextChars = 2_048;
    public const int MaxAutoSummaryChars = 4_096;
    public const int MaxMetadataRows = 100_000;

    private const string ValueTable = "SystemIndex_1_PropertyStore";
    private const string MetaTable = "SystemIndex_1_PropertyStore_Metadata";

    private readonly int _maxEvents;

    public WindowsSearchIndexParser() : this(MaxEvents) { }

    /// <summary>Internal for the tests: a smaller event cap than <see cref="MaxEvents"/>.</summary>
    internal WindowsSearchIndexParser(int maxEvents) => _maxEvents = Math.Min(maxEvents, MaxEvents);

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "WindowsSearchIndexParser", Version = "1.0", Artifact = "Indexul Windows Search (Windows.db, SQLite, Windows 11)",
        SourceTypes = [SourceTypeName], FileNames = [DatabaseFileName], Fingerprints = ["sqlite"], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows 11 recent (SQLite pe o copie de lucru)",
        FormatVersions = ["Tabelele SystemIndex_1_PropertyStore și SystemIndex_1_PropertyStore_Metadata (Windows 11 26100)"],
        Limitations =
        [
            "Indexul acoperă doar locurile indexate (profiluri, Start Menu etc.); un fișier din afara lor nu apare.",
            "O intrare înseamnă că fișierul a existat la momentul indexării, nu că mai există sau că a fost deschis.",
            "Fișierele șterse pot rămâne în index până la următoarea trecere a serviciului de căutare.",
            "Windows.edb (ESE, Windows 10 și Windows 11 mai vechi) nu este citit de acest parser.",
            "Windows-gather.db este citit de WindowsSearchGatherParser; Windows-usn.db nu este citit.",
            $"Se citesc cel mult {MaxEvents} de intrări și {MaxTextChars} de caractere pe valoare text; baza nu poate depăși 2 GiB. Intrările fără cale nu produc eveniment.",
            "Fără -wal / -shm colectate lângă bază, ultimele modificări ale indexului pot lipsi.",
            "AutoSummary este un fragment din conținutul fișierului (probă sensibilă): rămâne în câmpul AutoSummary, nu în rezumat.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "WindowsSearchIndexParserTests: baze sintetice cu schema reală (id-uri de coloană diferite, toate tipurile de valoare, FILETIME ostil, plafoane, bază coruptă, Windows.db care nu este index, SHA-256 neschimbat); rulare pe un index real de Windows 11 26100 din laborator",
    };

    // Property names looked up in the metadata table; the index in this array is the slot used everywhere below.
    private static readonly string[] Names =
    [
        "System.ItemPathDisplay", "System.ItemUrl", "System.ItemFolderPathDisplay", "System.FileName", "System.DateModified", "System.DateCreated",
        "System.DateAccessed", "System.Search.GatherTime", "System.Size", "System.FileAttributes", "System.FileOwner", "System.ItemType",
        "System.IsFolder", "System.Search.Store", "System.Search.AutoSummary",
    ];
    private const int Path_ = 0, Url = 1, Folder = 2, FileName = 3, Modified = 4, Created = 5, Accessed = 6, Gather = 7, Size = 8, Attrs = 9, Owner = 10, ItemTypeI = 11, IsFolderI = 12, Store = 13, Summary = 14;

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var work = LogAnalyzer.Dfir.IO.WorkFolders.NewWorkDirectory(fullPath, "sqlite");
        try
        {
            var copy = SearchSqliteCopy.Prepare(Spec, item, fullPath, work, result);
            using var c = new SqliteConnection(SearchSqliteCopy.ConnectionString(copy));
            try
            {
                c.Open();
                SearchSqliteCopy.RegisterSchemaCollations(c);
                Read(c, item, sink, result, _maxEvents, ct);
            }
            catch (SqliteException ex)
            {
                // corrupt or incomplete database, another schema: a written gap, never an unchecked exception
                if (result.Records == 0) { result.Status = EvidenceStatus.Failed; result.Error = $"SQLite: {Clip(ex.Message)}"; }
                else result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap("Windows.db", result.Records == 0 ? EvidenceStatus.Failed : EvidenceStatus.Partial, $"baza SQLite nu a putut fi citită până la capăt: {Clip(ex.Message)}",
                    "Intrările rămase din index nu au fost citite", "Windows.db recolectat din nou (cu -wal și -shm), sau Windows-gather.db cu altă unealtă", "Posibil"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            LogAnalyzer.Dfir.IO.WorkFolders.TryDelete(work);
        }
    }

    private static readonly SearchSqliteSpec Spec = new(DatabaseFileName, [ValueTable, MetaTable], SameSnapshotNote, NoWalNote, CollectedBy, MaxFileBytes);

    private static void Read(SqliteConnection c, EvidenceItem item, IEventSink sink, ParseResult result, int maxEvents, CancellationToken ct)
    {
        // table names are constants; the lookup values are parameters
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ($a, $b)";
            cmd.Parameters.AddWithValue("$a", ValueTable);
            cmd.Parameters.AddWithValue("$b", MetaTable);
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (r.GetValue(0) is string s) tables.Add(s);
        }
        if (!tables.Contains(ValueTable) || !tables.Contains(MetaTable))
        {
            result.Status = EvidenceStatus.Failed;
            result.Error = "Baza nu are tabelele SystemIndex_1_PropertyStore și SystemIndex_1_PropertyStore_Metadata: nu este un index Windows Search.";
            result.Gaps.Add(new EvidenceGap("Windows.db", EvidenceStatus.Failed, "nu este indexul Windows Search (tabelele SystemIndex_1_* lipsesc)",
                "Fișierul nu a fost citit ca index", "Windows.db al indexului Windows Search din ProgramData\\Microsoft\\Search\\Data\\Applications\\Windows", "Nu"));
            return;
        }

        // column id -> slot, from the metadata table by name (the ids differ from one machine to another)
        var slotOf = new Dictionary<long, int>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT Id, Name FROM {MetaTable} ORDER BY Id LIMIT $n";
            cmd.Parameters.AddWithValue("$n", MaxMetadataRows);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (r.GetValue(0) is not long id || r.GetValue(1) is not string name) continue;
                var slot = Array.FindIndex(Names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (slot >= 0) slotOf[id] = slot;
            }
        }
        var haveSlots = slotOf.Values.ToHashSet();
        if (!haveSlots.Contains(Path_) && !haveSlots.Contains(Url))
        {
            result.Gaps.Add(new EvidenceGap("Windows.db", EvidenceStatus.Failed, "tabela de metadate nu are System.ItemPathDisplay nici System.ItemUrl",
                "Nicio intrare nu are cale, deci nu s-a produs niciun eveniment", "Windows.db recolectat din nou", "Posibil"));
            return;
        }

        var ids = slotOf.Keys.OrderBy(k => k).ToList();
        using var q = c.CreateCommand();
        var inList = new StringBuilder();
        for (int i = 0; i < ids.Count; i++)
        {
            inList.Append(i == 0 ? "$c" : ", $c").Append(i.ToString(CultureInfo.InvariantCulture));
            q.Parameters.AddWithValue("$c" + i.ToString(CultureInfo.InvariantCulture), ids[i]);
        }
        q.Parameters.AddWithValue("$t", MaxAutoSummaryChars + 1);
        q.Parameters.AddWithValue("$b", 2 * (MaxAutoSummaryChars + 1));
        // text and blob values are cut inside SQLite: a multi-megabyte value never reaches memory
        q.CommandText = $"SELECT WorkId, ColumnId, CASE typeof(Value) WHEN 'text' THEN substr(Value, 1, $t) WHEN 'blob' THEN substr(Value, 1, $b) ELSE Value END FROM {ValueTable} WHERE ColumnId IN ({inList}) ORDER BY WorkId, ColumnId";
        using var rd = q.ExecuteReader();

        var cur = new object?[Names.Length];
        long curWork = 0; bool have = false;
        while (rd.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (rd.GetValue(0) is not long work || rd.GetValue(1) is not long col || !slotOf.TryGetValue(col, out var slot)) continue;
            if (!have || work != curWork)
            {
                if (have && !Emit(curWork, cur, item, sink, result, maxEvents)) return;
                Array.Clear(cur);
                curWork = work; have = true;
            }
            var v = rd.GetValue(2);
            if (cur[slot] is null && v is not DBNull) cur[slot] = v;   // first value of a property wins
        }
        if (have) Emit(curWork, cur, item, sink, result, maxEvents);
    }

    /// <summary>False when the event cap was reached (a gap is written, reading stops).</summary>
    private static bool Emit(long workId, object?[] v, EvidenceItem item, IEventSink sink, ParseResult result, int maxEvents)
    {
        var path = Text(v[Path_], MaxTextChars);
        if (path.Length == 0) path = FromUrl(Text(v[Url], MaxTextChars));
        if (path.Length == 0) return true;
        if (result.Records >= maxEvents)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap("Windows.db", EvidenceStatus.Partial, $"indexul are mai mult de {maxEvents} de intrări cu cale; restul nu au fost citite",
                "Intrările peste plafon lipsesc din cronologie", "Windows.db cu o unealtă dedicată sau filtrat", "Posibil"));
            return false;
        }
        var safePath = Sane(path);
        var modified = Time(v[Modified]); var created = Time(v[Created]); var accessed = Time(v[Accessed]); var gather = Time(v[Gather]);
        Timestamp time; string semantics;
        if (modified.IsKnown) { time = modified; semantics = "file modified time held by the index (System.DateModified)"; }
        else if (gather.IsKnown) { time = gather; semantics = "index gather time (System.Search.GatherTime); no valid DateModified"; }
        else { time = Timestamp.Unknown(); semantics = "the index entry has no valid time (System.DateModified, System.Search.GatherTime)"; }
        var size = Number(v[Size]);
        var fileName = Sane(Text(v[FileName], MaxTextChars));
        if (fileName.Length == 0) fileName = Sane(WinPath.GetFileName(path));
        var inv = CultureInfo.InvariantCulture;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Path"] = safePath,
            ["Folder"] = Sane(Text(v[Folder], MaxTextChars)),
            ["FileName"] = fileName,
            ["Size"] = size?.ToString(inv) ?? "",
            ["Attributes"] = Number(v[Attrs])?.ToString(inv) ?? "",
            ["Owner"] = Sane(Text(v[Owner], MaxTextChars)),
            ["ItemType"] = Sane(Text(v[ItemTypeI], MaxTextChars)),
            ["IsFolder"] = Number(v[IsFolderI]) is { } f ? (f != 0 ? "da" : "nu") : "",
            ["Store"] = Sane(Text(v[Store], MaxTextChars)),
            ["DateCreated"] = created.UtcIso, ["DateModified"] = modified.UtcIso, ["DateAccessed"] = accessed.UtcIso, ["GatherTime"] = gather.UtcIso,
            ["WorkId"] = workId.ToString(inv),
        };
        // a fragment of the file's content: sensitive, only in its own field (no text and no hash of it in the summary)
        var auto = NotepadTabStateParser.ContentClean(Text(v[Summary], MaxAutoSummaryChars));
        if (auto.Length > 0)
        {
            fields["AutoSummary"] = auto;
            fields["AutoSummary Sensitivity"] = "Fragment din conținutul fișierului (System.Search.AutoSummary): poate conține date personale sau secrete ale utilizatorului analizat; tratați-l ca probă sensibilă; nu apare în rezumat.";
        }
        sink.Add(new TimelineEvent
        {
            Time = time, TimeSemantics = semantics, Source = EventSource, EvidenceId = item.EvidenceId, Path = safePath,
            Summary = $"Index Windows Search: {safePath} ({(size is { } sz ? sz.ToString(inv) + " octeți" : "mărime necunoscută")}, modificat {(modified.IsKnown ? modified.UtcIso : "oră necunoscută")})",
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
            Locator = $"{ValueTable} WorkId={workId.ToString(inv)}", Fields = fields,
        });
        result.Records++;
        return true;
    }

    /// <summary>
    /// Text for display: the Tag block (U+E0000-E007F, "ASCII smuggling"; a surrogate pair that <see cref="DefenderDetectionHistoryParser.Safe"/> does not see) is removed and
    /// named, then line breaks, tabs, bidi overrides and control characters are handled by <see cref="DefenderDetectionHistoryParser.Safe"/>.
    /// </summary>
    internal static string Sane(string s)
    {
        if (s.Length > 0 && s.Contains('\uDB40'))
        {
            var sb = new StringBuilder(s.Length);
            bool tag = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\uDB40' && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) && s[i + 1] <= '\uDC7F') { tag = true; i++; continue; }
                sb.Append(s[i]);
            }
            s = sb.Append(tag ? " [caractere Unicode ascunse eliminate: bloc Tag]" : "").ToString();
        }
        return DefenderDetectionHistoryParser.Safe(s);
    }

    /// <summary>"file:C:/x/y" (or file:///C:/x/y) as a Windows path; another scheme is kept as it is (it is only text).</summary>
    internal static string FromUrl(string url)
    {
        if (url.Length == 0) return "";
        if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return url;
        var p = url[5..].TrimStart('/');
        return p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':' ? p.Replace('/', '\\') : "\\\\" + p.Replace('/', '\\');
    }

    /// <summary>A text value of any stored type, capped (without cutting a surrogate pair) and cut at the first NUL. A BLOB is read as UTF-16LE.</summary>
    internal static string Text(object? v, int max)
    {
        string s = v switch
        {
            string t => t,
            byte[] b => Encoding.Unicode.GetString(b, 0, b.Length & ~1),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => "",
        };
        int nul = s.IndexOf('\0');
        if (nul >= 0) s = s[..nul];
        if (s.Length > max) { s = s[..max]; if (char.IsHighSurrogate(s[^1])) s = s[..^1]; }
        return s;
    }

    /// <summary>A non-negative number from INTEGER, REAL, TEXT or an 8-byte little-endian BLOB; anything else is null.</summary>
    internal static ulong? Number(object? v)
    {
        switch (v)
        {
            case long l: return l >= 0 ? (ulong)l : null;
            case double d: return d >= 0 && d < 1.8e19 && !double.IsNaN(d) ? (ulong)d : null;
            case string s: return ulong.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
            case byte[] { Length: 8 } b: return BinaryPrimitives.ReadUInt64LittleEndian(b);
            default: return null;
        }
    }

    /// <summary>A FILETIME: an 8-byte little-endian BLOB (any other length is ignored) or an INTEGER; zero, negative and beyond-calendar values are unknown.</summary>
    internal static Timestamp Time(object? v)
    {
        long ft;
        switch (v)
        {
            case byte[] { Length: 8 } b: ft = BinaryPrimitives.ReadInt64LittleEndian(b); break;
            case long l: ft = l; break;
            default: return Timestamp.Unknown();
        }
        return Timestamp.FromFileTime(ft, "FILETIME (100 ns since 1601 UTC)");
    }

    private static string Clip(string s) => s.Length > 300 ? s[..300] : s;
}
