using System.Buffers.Binary;
using System.Globalization;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Microsoft.Data.Sqlite;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Windows Search gatherer database of Windows 11 (SQLite): <c>%ProgramData%\Microsoft\Search\Data\Applications\Windows\Windows-gather.db</c>, beside Windows.db.
/// <c>SystemIndex_Gthr</c> holds one row per item the gatherer has seen (scope id, file name, DocumentID = the WorkId of Windows.db, last-modified FILETIME);
/// the folder part of the path is the chain of <c>SystemIndex_GthrPth</c> (Scope -> Parent, Name). One event per row. A row can outlive the file (observed in the lab:
/// after a delete the PropertyStore rows of Windows.db were gone, the gatherer row stayed), so a row means "the indexer knew this path", never "the file exists".
/// Read from a working copy inside the case (with -wal when it was collected); the stored evidence is never opened. The database is untrusted: no name from it
/// enters SQL text, names are cut inside SQLite, the path chain is walked with cycle and depth guards, every value is type-checked.
/// </summary>
public sealed class WindowsSearchGatherParser : EvidenceParserBase
{
    public const string SourceTypeName = "windows_search_gather";
    public const string EventSource = "WindowsSearchGather";
    public const string CollectedBy = WindowsSearchIndexParser.CollectedBy;
    public const string DatabaseFileName = "Windows-gather.db";
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>To be written into the evidence notes when Windows-gather.db and its -wal come from one shadow copy (the collector and the remote package do not copy Windows-gather.db yet).</summary>
    public const string GatherSameSnapshotNote = "aceeași pentru Windows-gather.db și -wal";
    /// <summary>Written by the import when no Windows-gather.db-wal was beside the imported Windows-gather.db.</summary>
    public const string NoWalNote = "Lângă bază nu exista un jurnal Windows-gather.db-wal.";

    public const int MaxEvents = 500_000;
    public const int MaxPathRows = 2_000_000;
    public const int MaxTextChars = 2_048;
    public const int MaxNameChars = 1_000;
    public const int MaxDepth = 256;
    /// <summary>Folder + file name, as for Windows.db (one event carries the path in Path, Url, Fields and Summary: a longer cap multiplies the output).</summary>
    public const int MaxPathChars = 2_048;
    /// <summary>Total characters of the folder names held in memory (the rows are capped one by one, the sum is capped here).</summary>
    public const long MaxPathTotalChars = 64_000_000;
    public const string IncompletePrefix = "[cale incompletă] ";

    private const string GatherTable = "SystemIndex_Gthr";
    private const string PathTable = "SystemIndex_GthrPth";

    private static readonly SearchSqliteSpec Spec = new(DatabaseFileName, [GatherTable, PathTable], GatherSameSnapshotNote, NoWalNote, CollectedBy, MaxFileBytes);

    private readonly int _maxEvents;
    private readonly int _maxPathRows;

    public WindowsSearchGatherParser() : this(MaxEvents, MaxPathRows) { }

    /// <summary>Internal for the tests: smaller caps than <see cref="MaxEvents"/> and <see cref="MaxPathRows"/>.</summary>
    internal WindowsSearchGatherParser(int maxEvents, int maxPathRows = MaxPathRows)
    {
        _maxEvents = Math.Min(maxEvents, MaxEvents);
        _maxPathRows = Math.Min(maxPathRows, MaxPathRows);
    }

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "WindowsSearchGatherParser", Version = "1.0", Artifact = "Baza gatherer a Windows Search (Windows-gather.db, SQLite, Windows 11)",
        SourceTypes = [SourceTypeName], FileNames = [DatabaseFileName], Fingerprints = ["sqlite"], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows 11 recent (SQLite pe o copie de lucru)",
        FormatVersions = ["Tabelele SystemIndex_Gthr și SystemIndex_GthrPth (Windows 11 26100)"],
        Limitations =
        [
            "Un rând înseamnă că indexatorul a cunoscut calea, nu că fișierul mai există, că a fost deschis sau că are acel conținut.",
            "Un rând poate rămâne și după ștergerea fișierului (observat în laborator); nu se știe cât timp rămâne.",
            "TransactionFlags este afișat ca valoare brută: semnificația biților nu este documentată și nu este interpretată.",
            "Ora este ultima modificare a fișierului înregistrată de gatherer, nu ora indexării sau a ștergerii.",
            "Windows-usn.db nu este citit; Windows.edb (ESE) nu este citit de acest parser.",
            "Calea se reconstituie din lanțul SystemIndex_GthrPth; dacă lanțul este întrerupt, ciclic sau mai adânc de 256 de niveluri, calea este marcată „[cale incompletă]”, câmpul PathComplete este „nu”.",
            $"Se citesc cel mult {MaxEvents} de rânduri, {MaxPathRows} de înregistrări de cale, {MaxNameChars} de caractere pe nume de folder și {MaxTextChars} pe nume de fișier; calea este plafonată la {MaxPathChars} de caractere; baza nu poate depăși 2 GiB.",
            "Fără -wal colectat lângă bază, ultimele modificări ale gatherer-ului pot lipsi.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "WindowsSearchGatherParserTests: baze sintetice cu schema reală (colație UNICODE_en-US_LINGUISTIC_IGNORECASE, WITHOUT ROWID), lanț de căi, cicluri, FILETIME ostil, nume de 5 MB, plafoane, bază coruptă, WAL, SHA-256 neschimbat; rulare pe o bază reală de Windows 11 26100 din laborator",
    };

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
                Read(c, item, sink, result, ct);
            }
            catch (SqliteException ex)
            {
                // corrupt or incomplete database, another schema: a written gap, never an unchecked exception
                if (result.Records == 0) { result.Status = EvidenceStatus.Failed; result.Error = $"SQLite: {Clip(ex.Message)}"; }
                else result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap(DatabaseFileName, result.Records == 0 ? EvidenceStatus.Failed : EvidenceStatus.Partial, $"baza SQLite nu a putut fi citită până la capăt: {Clip(ex.Message)}",
                    "Rândurile rămase din gatherer nu au fost citite", "Windows-gather.db recolectat din nou (cu -wal)", "Posibil"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            LogAnalyzer.Dfir.IO.WorkFolders.TryDelete(work);
        }
    }

    private void Read(SqliteConnection c, EvidenceItem item, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        // table names are constants; the lookup values are parameters
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ($a, $b)";
            cmd.Parameters.AddWithValue("$a", GatherTable);
            cmd.Parameters.AddWithValue("$b", PathTable);
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (r.GetValue(0) is string s) tables.Add(s);
        }
        if (!tables.Contains(GatherTable) || !tables.Contains(PathTable))
        {
            result.Status = EvidenceStatus.Failed;
            result.Error = "Baza nu are tabelele SystemIndex_Gthr și SystemIndex_GthrPth: nu este baza gather a Windows Search.";
            result.Gaps.Add(new EvidenceGap(DatabaseFileName, EvidenceStatus.Failed, "nu este baza gather a Windows Search (tabelele SystemIndex_Gthr și SystemIndex_GthrPth lipsesc)",
                "Fișierul nu a fost citit ca bază gather", "Windows-gather.db din ProgramData\\Microsoft\\Search\\Data\\Applications\\Windows", "Nu"));
            return;
        }

        var paths = LoadPaths(c, result, _maxPathRows, ct);
        var resolver = new PathResolver(paths);

        using var q = c.CreateCommand();
        q.Parameters.AddWithValue("$t", MaxTextChars);
        q.Parameters.AddWithValue("$lim", (long)_maxEvents + 1);   // one more than the cap, so reaching the cap is still seen and written as a gap
        // text and blob values are cut inside SQLite: a multi-megabyte value never reaches memory; LastModified is read only as an 8-byte blob
        q.CommandText =
            "SELECT ScopeID, substr(FileName, 1, $t), DocumentID, " +
            "CASE WHEN typeof(LastModified) = 'blob' AND length(LastModified) = 8 THEN LastModified END, " +
            Num("DeletedCount") + ", " + Num("TransactionFlags") + ", " + Num("CrawlNumberCrawled") + ", " + Num("Priority") + ", " + Num("AppOwnerId") + ", " +
            "substr(StorageProviderId, 1, $t), length(UserData) " +
            // "+DocumentID": a plain scan of the table and a temporary sort. Ordering through SystemIndex_Gthr_indexDocID makes SQLite look every row up in
            // the primary key (ScopeID, FileName COLLATE UNICODE_en-US_LINGUISTIC_IGNORECASE); the Windows collation is not available here, the stand-in
            // orders differently, and the lookup misses rows WITHOUT an error (lab, Windows 11 26100: 2 of 1,175 rows lost; 62 with an ordinal stand-in).
            $"FROM {GatherTable} ORDER BY +DocumentID LIMIT $lim";
        using var rd = q.ExecuteReader();
        while (rd.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (result.Records >= _maxEvents)
            {
                result.MalformedRecords++;
                result.Gaps.Add(new EvidenceGap(DatabaseFileName, EvidenceStatus.Partial, $"baza gather are mai mult de {_maxEvents} de rânduri; restul nu au fost citite",
                    "Rândurile peste plafon lipsesc din cronologie", "Windows-gather.db cu o unealtă dedicată sau filtrat", "Posibil"));
                return;
            }
            Emit(rd, resolver, item, sink, result);
        }
    }

    /// <summary>A column of unknown type read as something small: text and blobs are cut inside SQLite (an 8-byte blob is still a number).</summary>
    private static string Num(string column) =>
        $"CASE typeof({column}) WHEN 'text' THEN substr({column}, 1, 64) WHEN 'blob' THEN substr({column}, 1, 8) ELSE {column} END";

    private static Dictionary<long, (long Parent, string Name)> LoadPaths(SqliteConnection c, ParseResult result, int maxRows, CancellationToken ct)
    {
        var map = new Dictionary<long, (long, string)>();
        using var cmd = c.CreateCommand();
        cmd.Parameters.AddWithValue("$t", MaxNameChars);
        cmd.Parameters.AddWithValue("$n", (long)maxRows + 1);
        cmd.CommandText = $"SELECT Scope, Parent, substr(Name, 1, $t) FROM {PathTable} LIMIT $n";
        using var r = cmd.ExecuteReader();
        long rows = 0, chars = 0;
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (rows >= maxRows || chars > MaxPathTotalChars)
            {
                result.Gaps.Add(new EvidenceGap(DatabaseFileName, EvidenceStatus.Partial, rows >= maxRows
                        ? $"tabela SystemIndex_GthrPth are mai mult de {maxRows} de rânduri; restul nu au fost încărcate"
                        : "numele de foldere din SystemIndex_GthrPth depășesc plafonul de memorie; restul nu au fost încărcate",
                    "Căile care depind de rândurile neîncărcate sunt marcate „[cale incompletă]”", "Windows-gather.db cu o unealtă dedicată", "Posibil"));
                break;
            }
            rows++;
            if (ToLong(WindowsSearchIndexParser.Number(r.GetValue(0))) is not { } scope) continue;
            var parent = ToLong(WindowsSearchIndexParser.Number(r.GetValue(1))) ?? -1;
            var name = WindowsSearchIndexParser.Text(r.GetValue(2), MaxNameChars);
            chars += name.Length;
            map[scope] = (parent, name);
        }
        return map;
    }

    private static long? ToLong(ulong? v) => v is { } n && n <= long.MaxValue ? (long)n : null;

    /// <summary>
    /// Folder path of a scope, from the Parent chain. A missing parent, a cycle or a chain deeper than <see cref="MaxDepth"/> makes the path incomplete.
    /// Folder paths are memoised (within a memory budget) so the chain is not walked again for every row.
    /// </summary>
    private sealed class PathResolver(Dictionary<long, (long Parent, string Name)> map)
    {
        // Depth = number of folder levels from this node up to where its walk ended (root, missing parent or cycle).
        private readonly Dictionary<long, (string Path, bool Complete, int Depth)> _memo = [];
        private long _memoChars;
        private const long MemoBudget = 32_000_000;

        public (string Path, bool Complete) Folder(long scope)
        {
            if (_memo.TryGetValue(scope, out var m)) return (m.Path, m.Complete);
            return Walk(scope, useMemo: true) ?? Walk(scope, useMemo: false)!.Value;
        }

        /// <summary>
        /// Null when a memoised node would take the walk past <see cref="MaxDepth"/>: the caller walks again without the memo, so a deep chain is cut the same
        /// way whatever row was read first. A walk cut by depth memoises nothing (its result depends on where it started).
        /// </summary>
        private (string Path, bool Complete)? Walk(long scope, bool useMemo)
        {
            var chain = new List<long>();
            var seen = new HashSet<long>();
            var cur = scope;
            (string Path, bool Complete, int Depth) basis;
            bool depthCut = false;
            while (true)
            {
                if (useMemo && _memo.TryGetValue(cur, out var known))
                {
                    if (chain.Count + known.Depth > MaxDepth) return null;
                    basis = known; break;
                }
                if (!map.TryGetValue(cur, out _))
                {
                    // scope 1 has no row: it is the root (observed on Windows 11); any other missing scope breaks the chain
                    basis = ("", cur == 1, 0);
                    break;
                }
                if (!seen.Add(cur)) { basis = ("", false, 0); break; }
                if (chain.Count >= MaxDepth) { basis = ("", false, 0); depthCut = true; break; }
                chain.Add(cur);
                cur = map[cur].Parent;
            }
            var path = basis.Path; var complete = basis.Complete;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var name = map[chain[i]].Name;
                if (path.Length + name.Length > MaxPathChars) { path = (path + name)[..MaxPathChars]; complete = false; }
                else path += name;
                if (!depthCut && _memoChars + path.Length <= MemoBudget) { _memo[chain[i]] = (path, complete, basis.Depth + chain.Count - i); _memoChars += path.Length; }
            }
            return (path, complete);
        }
    }

    private static void Emit(SqliteDataReader rd, PathResolver resolver, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        var inv = CultureInfo.InvariantCulture;
        var scope = ToLong(WindowsSearchIndexParser.Number(rd.GetValue(0)));
        var fileName = WindowsSearchIndexParser.Text(rd.GetValue(1), MaxTextChars);
        var doc = WindowsSearchIndexParser.Number(rd.GetValue(2));
        var modified = TimeOf(rd.GetValue(3));

        var (folder, complete) = scope is { } sc ? resolver.Folder(sc) : ("", false);
        var rawUrl = folder + fileName;
        if (rawUrl.Length > MaxPathChars) { rawUrl = rawUrl[..MaxPathChars]; complete = false; }
        var winPath = ToWindowsPath(rawUrl, out var scopeGuid);
        var shown = complete ? winPath : IncompletePrefix + winPath;
        var safePath = WindowsSearchIndexParser.Sane(shown);

        var time = modified.IsKnown ? modified : Timestamp.Unknown();
        var semantics = modified.IsKnown ? "file last-modified time recorded by the indexer's gatherer (SystemIndex_Gthr.LastModified)"
                                         : "the gatherer row has no valid LastModified (SystemIndex_Gthr.LastModified)";
        var flags = WindowsSearchIndexParser.Number(rd.GetValue(5));
        var docText = doc?.ToString(inv) ?? "";
        var displayName = WindowsSearchIndexParser.Sane(fileName);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Path"] = safePath,
            ["Url"] = WindowsSearchIndexParser.Sane(rawUrl),
            ["FileName"] = displayName,
            ["ScopeGuid"] = scopeGuid,
            ["PathComplete"] = complete ? "da" : "nu",
            ["DocumentID"] = docText,
            ["LastModified"] = modified.UtcIso,
            ["DeletedCount"] = WindowsSearchIndexParser.Number(rd.GetValue(4))?.ToString(inv) ?? "",
            ["TransactionFlags"] = flags?.ToString(inv) ?? "",
            ["TransactionFlagsHex"] = flags is { } f ? "0x" + f.ToString("X", inv) : "",
            ["CrawlNumberCrawled"] = WindowsSearchIndexParser.Number(rd.GetValue(6))?.ToString(inv) ?? "",
            ["Priority"] = WindowsSearchIndexParser.Number(rd.GetValue(7))?.ToString(inv) ?? "",
            ["AppOwnerId"] = WindowsSearchIndexParser.Number(rd.GetValue(8))?.ToString(inv) ?? "",
            ["StorageProviderId"] = WindowsSearchIndexParser.Sane(WindowsSearchIndexParser.Text(rd.GetValue(9), MaxTextChars)),
            ["UserDataBytes"] = WindowsSearchIndexParser.Number(rd.GetValue(10))?.ToString(inv) ?? "",
            ["IsScopePattern"] = fileName.Contains('*') ? "da" : "nu",
        };
        sink.Add(new TimelineEvent
        {
            Time = time, TimeSemantics = semantics, Source = EventSource, EvidenceId = item.EvidenceId, Path = safePath,
            Summary = $"Gatherer Windows Search: {safePath} (DocumentID {(docText.Length > 0 ? docText : "necunoscut")}, modificat {(modified.IsKnown ? modified.UtcIso : "oră necunoscută")})",
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
            Locator = $"{GatherTable} DocumentID={(docText.Length > 0 ? docText : "?")}", Fields = fields,
        });
        result.Records++;
    }

    /// <summary>The LastModified value (already restricted to an 8-byte blob by the query) as a FILETIME little-endian; anything else is unknown.</summary>
    internal static Timestamp TimeOf(object? v) =>
        v is byte[] { Length: 8 } b
            ? Timestamp.FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(b), "FILETIME (100 ns since 1601 UTC)")
            : Timestamp.Unknown();

    /// <summary>
    /// "file:C:/[guid]/Users/x/f.txt" as the Windows path "C:\Users\x\f.txt": the "[guid]/" component right after the drive is the scope GUID and is
    /// taken out of the path. Any other scheme (winrt:, mapi:, iehistory: ...) stays text as it is; a file: URL without a GUID goes through <see cref="WindowsSearchIndexParser.FromUrl"/>.
    /// </summary>
    internal static string ToWindowsPath(string url, out string scopeGuid)
    {
        scopeGuid = "";
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = url[5..];
            if (rest.Length >= 2 && char.IsAsciiLetter(rest[0]) && rest[1] == ':')
            {
                var after = rest[2..];
                var body = after.StartsWith('/') ? after[1..] : after;
                if (body.Length >= 38 && body[0] == '[' && body[37] == ']' && (body.Length == 38 || body[38] == '/')
                    && Guid.TryParseExact(body.AsSpan(1, 36), "D", out _))
                {
                    scopeGuid = body.Substring(1, 36);
                    var tail = body.Length > 39 ? body[39..] : "";
                    return rest[..2] + "\\" + tail.Replace('/', '\\');
                }
            }
        }
        return WindowsSearchIndexParser.FromUrl(url);
    }

    private static string Clip(string s) => s.Length > 300 ? s[..300] : s;
}
