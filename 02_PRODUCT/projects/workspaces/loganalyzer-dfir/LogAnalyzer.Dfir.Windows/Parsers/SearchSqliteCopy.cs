using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Microsoft.Data.Sqlite;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// What differs between the Windows Search databases that share the working-copy logic (Windows.db, Windows-gather.db): the file name, the tables that must exist,
/// and the notes the collector / import writes into the evidence. All values are constants of the parser, never data from a database.
/// </summary>
internal sealed record SearchSqliteSpec(string DatabaseName, string[] RequiredTables, string SameSnapshotNote, string NoWalNote, string Collector, long MaxFileBytes);

/// <summary>
/// Working copy of a Windows Search SQLite database with its -wal: copy, journal decision, quick_check and the collations named in the schema. The stored
/// evidence is never opened. Shared by <see cref="WindowsSearchIndexParser"/> and <see cref="WindowsSearchGatherParser"/>.
/// </summary>
internal static class SearchSqliteCopy
{
    /// <summary>
    /// Copies the database into <paramref name="work"/> (and its -wal when it may be applied) and writes the gaps about the journal and the integrity check.
    /// Returns the path of the working copy.
    /// </summary>
    public static string Prepare(SearchSqliteSpec spec, EvidenceItem item, string fullPath, string work, ParseResult result)
    {
        var name = spec.DatabaseName;
        var copy = Path.Combine(work, name);
        File.Copy(fullPath, copy);
        File.SetAttributes(copy, FileAttributes.Normal);
        // Only -wal is placed beside the copy: SQLite rebuilds the -shm index from it. The collector takes the database and -wal from ONE snapshot;
        // for a pair from elsewhere (an import) the journal is applied only when the database alone is sound and the result still passes
        // quick_check with the required tables. That catches a journal of another database, NOT a journal of the same database from a later moment
        // (that one can rebuild a state that never existed and pass every check): the gap below says so whenever a journal is applied.
        SQLitePCL.Batteries_V2.Init();
        // The database alone only has to be sound: its tables may still live in the journal (a database never checkpointed).
        bool baseOk = QuickCheckOk(spec, copy, requireTables: false);
        // A pair is vouched for only when THIS application's collector copied it from one shadow copy (collector, action and note all match:
        // an imported file's notes hold the operator's path, which could contain the note text).
        bool samePair = item.Notes.Contains(spec.SameSnapshotNote, StringComparison.Ordinal) &&
            (item.Collector == spec.Collector && item.AcquisitionMethod == "acquired"
             // a verified remote package: its script takes both files from one shadow copy, or neither
             || item.Collector == LogAnalyzer.Dfir.Windows.Acquisition.RemoteCollection.CollectorName && item.AcquisitionMethod == "imported");
        var walPath = fullPath + "-wal";
        bool walThere = File.Exists(walPath);
        if (walThere && new FileInfo(walPath).Length > spec.MaxFileBytes)
        {
            walThere = false;
            result.Gaps.Add(new EvidenceGap(name + "-wal", EvidenceStatus.Partial, $"jurnalul WAL depășește {spec.MaxFileBytes:N0} octeți și nu a fost aplicat",
                "Ultimele modificări ale indexului, aflate doar în jurnal, pot lipsi", $"{name} citit fără jurnal", "Posibil"));
        }
        // a shadow copy can freeze a checkpoint half written: then the database alone fails quick_check and only database + journal is a valid
        // state, so a vouched-for pair gets its journal even when the database alone is not sound
        bool tryWal = walThere && (baseOk || samePair);
        bool applied = false;
        if (tryWal)
        {
            File.Copy(walPath, copy + "-wal"); File.SetAttributes(copy + "-wal", FileAttributes.Normal);
            if (QuickCheckOk(spec, copy))
            {
                applied = true;
                if (!samePair)
                    result.Gaps.Add(new EvidenceGap(name + "-wal", EvidenceStatus.Partial, "jurnalul WAL a fost aplicat, dar nu se știe dacă provine din același instantaneu cu baza: starea citită poate fi amestecată",
                        "Valorile modificate între cele două copii pot apărea într-o combinație care nu a existat", $"{name} și {name}-wal copiate din aceeași copie de umbră (colectorul aplicației face așa)", "Posibil"));
            }
            else
            {
                SqliteConnection.ClearAllPools();
                foreach (var f in new[] { copy, copy + "-wal", copy + "-shm" }) if (File.Exists(f)) File.Delete(f);
                File.Copy(fullPath, copy);
                File.SetAttributes(copy, FileAttributes.Normal);
                result.Gaps.Add(new EvidenceGap(name + "-wal", EvidenceStatus.Partial, "jurnalul WAL al indexului nu se potrivește cu baza și nu a fost aplicat",
                    "Ultimele modificări ale indexului, aflate doar în jurnal, pot lipsi", $"{name} și {name}-wal copiate din același instantaneu", "Posibil"));
            }
        }
        else if (!walThere && !samePair && item.AcquisitionMethod == "imported" && !item.Notes.Contains(spec.NoWalNote, StringComparison.Ordinal))
            result.Gaps.Add(new EvidenceGap(name + "-wal", EvidenceStatus.Partial, "baza a fost importată fără jurnalul WAL de lângă ea",
                "Ultimele modificări ale indexului, aflate doar în jurnal, pot lipsi", $"Importați {name} împreună cu {name}-wal din același folder", "Posibil"));
        // the read may still succeed on an unsound database (the damaged page off the query path): say so instead of staying silent
        if (!baseOk && !applied)
            result.Gaps.Add(new EvidenceGap(name, EvidenceStatus.Partial, "baza nu trece verificarea de integritate SQLite (quick_check)",
                "Unele intrări pot lipsi sau pot fi citite greșit", "O copie a indexului făcută din aceeași copie de umbră cu jurnalul", "Posibil"));
        return copy;
    }

    /// <summary>Connection string of a working copy (SQLite may write the copy when it replays the journal; the stored evidence is never opened).</summary>
    public static string ConnectionString(string copy) => $"Data Source={copy};Mode=ReadWrite;Pooling=False";

    /// <summary>
    /// True when the working copy with its -wal opens, passes PRAGMA quick_check and still has the required tables. A journal of another database
    /// can be internally valid and pass quick_check while replacing the schema, so the tables are checked too. Any SQLite error counts as "does not fit".
    /// </summary>
    private static bool QuickCheckOk(SearchSqliteSpec spec, string copy, bool requireTables = true)
    {
        try
        {
            using var c = new SqliteConnection(ConnectionString(copy));
            c.Open();
            RegisterSchemaCollations(c);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check(1)";
            if (cmd.ExecuteScalar() is not string s || !s.Equals("ok", StringComparison.OrdinalIgnoreCase)) return false;
            if (!requireTables) return true;
            // the table names are constants of the parser, passed as parameters
            var marks = string.Join(", ", spec.RequiredTables.Select((_, i) => "$t" + i));
            cmd.CommandText = $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ({marks})";
            for (int i = 0; i < spec.RequiredTables.Length; i++) cmd.Parameters.AddWithValue("$t" + i, spec.RequiredTables[i]);
            return cmd.ExecuteScalar() is long n && n == spec.RequiredTables.Length;
        }
        catch (SqliteException) { return false; }
        finally { SqliteConnection.ClearAllPools(); }
    }

    private static readonly System.Text.RegularExpressions.Regex CollateName =
        new(@"COLLATE\s+""?([A-Za-z0-9_\-]{1,64})""?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// The real databases declare Windows collations (lab, Windows 11 26100: COLLATE "UNICODE_en-US", "UNICODE_en-US_LINGUISTIC_IGNORECASE") that this SQLite does not
    /// have, and any read then fails with "no such collation sequence" on a sound database. The collations named in the schema are registered
    /// (OrdinalIgnoreCase when the name contains IGNORECASE, otherwise Ordinal; at most 16, names limited to letters, digits, '_' and '-') so the check and
    /// the read can run. The name is passed to the API, never into SQL text. Call it on every connection that reads or checks the copy.
    /// </summary>
    public static void RegisterSchemaCollations(SqliteConnection c)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT substr(sql, 1, 8192) FROM sqlite_master WHERE sql LIKE '%COLLATE%' LIMIT 256";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(0)) continue;
                try { foreach (System.Text.RegularExpressions.Match m in CollateName.Matches(r.GetString(0))) if (names.Count < 16) names.Add(m.Groups[1].Value); }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { }
            }
        }
        foreach (var n in names)
        {
            if (n.ToUpperInvariant() is "BINARY" or "NOCASE" or "RTRIM") continue;
            if (n.Contains("IGNORECASE", StringComparison.OrdinalIgnoreCase)) c.CreateCollation(n, (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            else c.CreateCollation(n, (a, b) => string.CompareOrdinal(a, b));
        }
    }
}
