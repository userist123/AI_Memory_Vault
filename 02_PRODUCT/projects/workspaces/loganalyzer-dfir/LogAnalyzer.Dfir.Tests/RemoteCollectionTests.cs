using System.Diagnostics;
using System.Text.Json;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Acquisition;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Tests that set the process-wide operating mode run alone.</summary>
[CollectionDefinition("AppMode", DisableParallelization = true)]
public sealed class AppModeCollection;

[Collection("AppMode")]
public sealed class RemoteCollectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ladfir_remote_" + Guid.NewGuid().ToString("N"));

    public RemoteCollectionTests()
    {
        // The whole remote path must work on an isolated station: the application never opens a connection for it.
        AppModeContext.ResetForTests();
        AppModeContext.Initialize(new ModeDecision(AppMode.AirGapped, true, "test", ConnectivitySnapshot.Unknown("test")));
    }

    public void Dispose()
    {
        AppModeContext.ResetForTests();
        foreach (var top in _shortRoots.Where(Directory.Exists))
        {
            foreach (var f in Directory.GetFiles(top, "*", SearchOption.AllDirectories)) { try { File.SetAttributes(f, FileAttributes.Normal); } catch (IOException) { } }
            Directory.Delete(top, true);
        }
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase() => CaseWorkspace.Create(Path.Combine(_root, "case"), new CaseInfo { CaseId = "CASE-REMOTE", Name = "remote", CreatedAtUtc = DateTimeOffset.UtcNow , Scope = TestScopes.Valid() });

    /// <summary>Windows PowerShell 5.1 as an operator starts it. PSModulePath is dropped so a PowerShell 7 parent (the CI
    /// runner's default shell) cannot hand it PS7-only module paths.</summary>
    private static ProcessStartInfo PowerShell(string file)
    {
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }) psi.ArgumentList.Add(a);
        psi.Environment.Remove("PSModulePath");
        return psi;
    }

    /// <summary>Runs the generated package with Windows PowerShell, as an operator would on the target, and returns the package folder.</summary>
    private string RunPackage(string script)
    {
        var dir = Path.Combine(_root, "target");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "collect.ps1");
        File.WriteAllText(file, script, new System.Text.UTF8Encoding(true));
        using var p = Process.Start(PowerShell(file))!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"exit {p.ExitCode}: {stderr}");
        return stdout.Trim().Split('\n').Last().Trim();
    }

    [Fact]
    public void Package_collects_on_this_station_hashes_on_target_and_imports_with_custody()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["evtx:Application", "hive:SYSTEM"]);
        var pkg = RunPackage(RemoteCollection.PackageScript(req, sha));

        var check = RemoteCollection.Verify(ws, pkg);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var m = check.Manifest!;
        Assert.Equal((req.RequestId, Environment.MachineName.ToUpperInvariant()), (m.RequestId, m.Host.ToUpperInvariant()));
        Assert.Equal("ok", m.Steps.Single(s => s.Artifact == "evtx:Application").Status);
        // Without elevation reg save cannot run; the package records that instead of hiding it.
        if (!m.Elevated) Assert.Equal("failed", m.Steps.Single(s => s.Artifact == "hive:SYSTEM").Status);

        var items = RemoteCollection.Import(ws, check);
        Assert.Equal(m.Files.Count + 1, items.Count);
        Assert.All(items.Skip(1), i => Assert.Equal(items[0].EvidenceId, i.ParentEvidenceId));
        Assert.All(items, i => Assert.Equal("remote:" + m.Host, i.Source));
        var evtx = items.Single(i => i.SourceType == "evtx");
        Assert.Equal(m.Files.Single(f => f.Path.EndsWith("Application.evtx")).Sha256, evtx.Sha256, StringComparer.OrdinalIgnoreCase);
        var custody = File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Contains(custody, c => c.GetProperty("action").GetString() == "remote-collected" && c.GetProperty("evidenceId").GetString() == evtx.EvidenceId);
        Assert.Contains(custody, c => c.GetProperty("action").GetString() == "authorized");
    }

    [Fact]
    public void A_log_that_does_not_exist_on_the_target_is_absent_not_a_failed_step()
    {
        // lab SRV01 2026-11-07: Sysmon is not installed, wevtutil exit 15007 was reported as „pas eșuat”
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["evtx:Microsoft-Windows-Sysmon/Operational"]);
        var check = RemoteCollection.Verify(ws, RunPackage(RemoteCollection.PackageScript(req, sha)));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var step = check.Manifest!.Steps.Single();
        Assert.Contains(step.Status, new[] { "ok", "absent" });   // ok where Sysmon is installed
        if (step.Status == "absent") Assert.Contains("nu exista pe statie", step.Error);
    }

    [Fact]
    public void Defender_history_files_in_a_package_are_typed_for_the_DetectionHistory_parser_only_with_a_GUID_name_and_matching_content()
    {
        const string id = "2169D50A-AB8F-4CC5-992A-DEAFEE4FAE82";
        var dir = Path.Combine(_root, "pkg", "files", RemoteCollection.DefenderHistoryFolder, "17");
        Directory.CreateDirectory(dir);
        var real = Path.Combine(dir, id);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Corpus", "DefenderDetectionHistory", id), real);
        var notGuid = Path.Combine(dir, "readme.txt"); File.WriteAllText(notGuid, "x");
        var junk = Path.Combine(dir, Guid.NewGuid().ToString().ToUpperInvariant()); File.WriteAllBytes(junk, new byte[64]);
        string Rel(string p) => Path.GetRelativePath(Path.Combine(_root, "pkg"), p).Replace('\\', '/');
        Assert.Equal(LogAnalyzer.Dfir.FileSystem.DefenderDetectionHistoryParser.SourceTypeName, RemoteCollection.ImportKind(Rel(real), real));
        Assert.Equal("file", RemoteCollection.ImportKind(Rel(notGuid), notGuid));
        Assert.Equal("file", RemoteCollection.ImportKind(Rel(junk), junk));
        // the same GUID file outside the Defender folder of a package is not taken for one
        Assert.Equal("file", RemoteCollection.ImportKind("files/other/" + id, real));
    }

    [Fact]
    public void Notepad_tabs_and_the_search_index_in_a_package_are_typed_only_in_their_own_folders_with_matching_content()
    {
        const string tab = "0e735e0a-8148-44fb-8cea-7e15f4caa644.bin";
        var pkg = Path.Combine(_root, "pkg2");
        var np = Path.Combine(pkg, "files", RemoteCollection.NotepadTabsFolder, "ana"); Directory.CreateDirectory(np);
        var realTab = Path.Combine(np, tab);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Corpus", "NotepadTabState", tab), realTab);
        var fakeTab = Path.Combine(np, Guid.NewGuid() + ".bin"); File.WriteAllBytes(fakeTab, new byte[64]);
        var si = Path.Combine(pkg, "files", RemoteCollection.SearchIndexFolder); Directory.CreateDirectory(si);
        var db = Path.Combine(si, "Windows.db");
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE t(x INTEGER)"; cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var notDb = Path.Combine(si, "other.db"); File.Copy(db, notDb);
        string Rel(string p) => Path.GetRelativePath(pkg, p).Replace('\\', '/');
        Assert.Equal(LogAnalyzer.Dfir.FileSystem.NotepadTabStateParser.SourceTypeName, RemoteCollection.ImportKind(Rel(realTab), realTab));
        Assert.Equal("file", RemoteCollection.ImportKind(Rel(fakeTab), fakeTab));
        Assert.Equal("file", RemoteCollection.ImportKind("files/other/" + tab, realTab));
        Assert.Equal(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser.SourceTypeName, RemoteCollection.ImportKind(Rel(db), db));
        Assert.Equal(WindowsSearchIndexCollector.WalSourceType, RemoteCollection.ImportKind("files/" + RemoteCollection.SearchIndexFolder + "/Windows.db-wal", db));
        Assert.Equal("file", RemoteCollection.ImportKind(Rel(notDb), notDb));
        Assert.Equal("file", RemoteCollection.ImportKind("files/elsewhere/Windows.db", db));
    }

    /// <summary>A package folder with a small Windows Search index (three rows that live only in the journal when <paramref name="withWal"/>), as the script would leave it.</summary>
    private string IndexPackage(RemoteCollectionRequest req, string requestSha, string stepStatus, bool withWal, string name, bool withGather = false)
    {
        var pkg = Path.Combine(_root, name);
        var dir = Path.Combine(pkg, "files", RemoteCollection.SearchIndexFolder); Directory.CreateDirectory(dir);
        var live = Path.Combine(_root, name + "_live.db");
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={live};Pooling=False"))
        {
            c.Open();
            void X(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            if (withWal) { X("PRAGMA journal_mode=WAL"); X("PRAGMA wal_autocheckpoint=0"); }
            X("CREATE TABLE SystemIndex_1_PropertyStore_Metadata (Id INTEGER PRIMARY KEY, UniqueKey TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, PropertyId INTEGER NOT NULL, VariantType INTEGER NOT NULL, StorageType INTEGER NOT NULL, MaxSize INTEGER, Flags INTEGER NOT NULL)");
            X("CREATE TABLE SystemIndex_1_PropertyStore (WorkId INTEGER NOT NULL, ColumnId INTEGER NOT NULL, Value BLOB NOT NULL, PRIMARY KEY (WorkId, ColumnId)) WITHOUT ROWID");
            X("INSERT INTO SystemIndex_1_PropertyStore_Metadata VALUES(7, 'k7', 'System.ItemPathDisplay', 1, 31, 11, NULL, 0)");
            for (int i = 1; i <= 3; i++) X($"INSERT INTO SystemIndex_1_PropertyStore VALUES({i}, 7, 'C:\\a\\f{i}.txt')");
            foreach (var n in withWal ? new[] { "", "-wal" } : [""])
            {
                using var src = new FileStream(live + n, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var dst = File.Create(Path.Combine(dir, "Windows.db" + n));
                src.CopyTo(dst);
            }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (withGather)
        {
            // a small Windows-gather.db: DocumentID 1 is also a WorkId of Windows.db, DocumentID 4 is not; no journal of its own beside it here
            var g = Path.Combine(dir, "Windows-gather.db");
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={g};Pooling=False"))
            {
                c.Open();
                void X(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
                X("CREATE TABLE SystemIndex_Gthr (ScopeID INTEGER NOT NULL, FileName TEXT, DocumentID INTEGER NOT NULL, LastModified BLOB, DeletedCount, TransactionFlags, CrawlNumberCrawled, Priority, AppOwnerId, StorageProviderId, UserData)");
                X("CREATE TABLE SystemIndex_GthrPth (Scope INTEGER, Parent INTEGER, Name TEXT)");
                X("INSERT INTO SystemIndex_GthrPth VALUES(2, 1, 'file:'), (3, 2, 'C:'), (4, 3, '/'), (5, 4, 'a/')");
                X("INSERT INTO SystemIndex_Gthr (ScopeID, FileName, DocumentID) VALUES (5, 'f1.txt', 1), (5, 'gone.txt', 4)");
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        string Sha(string p) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
        var files = Directory.GetFiles(dir).Select(p => new { path = Path.GetRelativePath(pkg, p).Replace('\\', '/'), size = new FileInfo(p).Length, sha256 = Sha(p) }).ToArray();
        var manifest = new
        {
            format = RemoteCollection.Format, requestId = req.RequestId, requestSha256 = requestSha, host = req.TargetHost, user = @"PC01\admin", elevated = true,
            collectedUtc = DateTimeOffset.UtcNow.ToString("o"),
            steps = new[] { new { artifact = "search-index", startUtc = "", endUtc = "", status = stepStatus, error = stepStatus == "ok" ? "" : "disc plin" } },
            files,
        };
        var mp = Path.Combine(pkg, "manifest.json");
        File.WriteAllText(mp, JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(pkg, "manifest.sha256"), Sha(mp) + "\n");
        return pkg;
    }

    [Fact]
    public void An_index_from_a_package_is_a_vouched_pair_only_when_its_step_ended_ok_and_never_meets_the_journal_of_another_import()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", ["search-index"]);
        ParseResult Parse(EvidenceItem i) =>
            new LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser().Parse(i, ws.FullPath(i.StoredPath), new ListSink(), default);

        // 1. step ok, database + journal: read whole (the rows are only in the journal), no gap
        var ok = RemoteCollection.Verify(ws, IndexPackage(req, sha, "ok", withWal: true, "pk_ok"));
        Assert.True(ok.Ok, string.Join("; ", ok.Problems));
        var first = RemoteCollection.Import(ws, ok);
        var db1 = first.Single(i => i.SourceType == LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser.SourceTypeName);
        var wal1 = first.Single(i => i.SourceType == WindowsSearchIndexCollector.WalSourceType);
        Assert.Equal(ws.FullPath(db1.StoredPath) + "-wal", ws.FullPath(wal1.StoredPath));
        var r1 = Parse(db1);
        Assert.Equal(3, r1.Records);
        Assert.Empty(r1.Gaps);

        // 2. a second package whose step failed after the database was copied (no journal): not vouched for, stored apart from the first
        //    import's journal, and the missing journal is a written gap
        var bad = RemoteCollection.Verify(ws, IndexPackage(req, sha, "failed", withWal: false, "pk_failed"));
        Assert.True(bad.Ok, string.Join("; ", bad.Problems));
        var db2 = RemoteCollection.Import(ws, bad).Single(i => i.SourceType == LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser.SourceTypeName);
        Assert.DoesNotContain(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser.SameSnapshotNote, db2.Notes);
        Assert.NotEqual(Path.GetDirectoryName(ws.FullPath(db1.StoredPath)), Path.GetDirectoryName(ws.FullPath(db2.StoredPath)));
        Assert.False(File.Exists(ws.FullPath(db2.StoredPath) + "-wal"));
        Assert.Contains(Parse(db2).Gaps, g => g.Artifact == "Windows.db-wal");
    }

    [Fact]
    public void The_search_index_step_copies_Windows_gather_db_from_the_same_shadow_copy_in_the_same_block_and_the_script_parses()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", ["search-index"]);
        var script = RemoteCollection.PackageScript(req, sha);
        int made = script.IndexOf($"Win32_Shadow{Destructive.Cut}Copy -MethodName Create", StringComparison.Ordinal);
        int gdb = script.IndexOf("$root + 'Windows-gather.db'", StringComparison.Ordinal);
        int gwal = script.IndexOf("$root + 'Windows-gather.db-wal'", StringComparison.Ordinal);
        int fin = script.IndexOf("Remove-CimInstance", StringComparison.Ordinal);
        Assert.True(made > 0 && made < gdb && gdb < fin && gwal < fin, "the gather copy must sit between the shadow copy creation and its deletion");
        // both files or neither: a failed copy removes the database it just wrote; a refused or missing gather is reported as text, never a failed step
        Assert.Contains("Remove-Item -LiteralPath $gdb, ($gdb + '-wal')", script);
        Assert.Contains("gather: absent", script);
        Assert.Contains("ReparsePoint", script);
        Assert.Contains("2GB", script);
        // PowerShell 5.1 itself must accept the generated script
        var file = Path.Combine(_root, "syntax", "collect.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, script);
        var check = Path.Combine(_root, "syntax", "check.ps1");
        File.WriteAllText(check, "param([string]$f) $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); if ($e.Count -gt 0) { $e | ForEach-Object { $_.Message }; exit 1 }");
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{check}\" \"{file}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.Environment.Remove("PSModulePath");
        using var pr = Process.Start(psi)!;
        var outText = pr.StandardOutput.ReadToEnd(); pr.StandardError.ReadToEnd(); pr.WaitForExit();
        Assert.True(pr.ExitCode == 0, outText);
    }

    [Fact]
    public void Gather_database_and_journal_of_a_package_are_typed_only_by_their_exact_path_and_content()
    {
        var pkg = Path.Combine(_root, "pkg3");
        var si = Path.Combine(pkg, "files", RemoteCollection.SearchIndexFolder); Directory.CreateDirectory(si);
        var db = Path.Combine(si, "Windows-gather.db");
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE t(x INTEGER)"; cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var rel = "files/" + RemoteCollection.SearchIndexFolder + "/Windows-gather.db";
        Assert.Equal(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.SourceTypeName, RemoteCollection.ImportKind(rel, db));
        Assert.Equal(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.WalSourceType, RemoteCollection.ImportKind(rel + "-wal", db));
        Assert.Equal("file", RemoteCollection.ImportKind("files/elsewhere/Windows-gather.db", db));
        var notSqlite = Path.Combine(si, "x.bin"); File.WriteAllBytes(notSqlite, new byte[64]);
        Assert.Equal("file", RemoteCollection.ImportKind(rel, notSqlite));
    }

    private List<TimelineEvent> ParseIndexPair(CaseWorkspace ws, EvidenceItem db, EvidenceItem gdb)
    {
        var events = new List<TimelineEvent>();
        foreach (var (i, parser) in new (EvidenceItem, LogAnalyzer.Dfir.Parsing.IEvidenceParser)[] { (db, new LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser()), (gdb, new LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser()) })
        {
            var sink = new ListSink();
            parser.Parse(i, ws.FullPath(i.StoredPath), sink, default);
            events.AddRange(sink.Events);
        }
        return events;
    }

    [Fact]
    public void A_package_with_an_ok_search_step_stores_gather_beside_Windows_db_with_a_common_moment_key_and_the_rule_finds_the_file_only_gather_knows()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", ["search-index"]);
        var check = RemoteCollection.Verify(ws, IndexPackage(req, sha, "ok", withWal: true, "pk_g_ok", withGather: true));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var items = RemoteCollection.Import(ws, check);
        var db = items.Single(i => i.SourceType == LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser.SourceTypeName);
        var gdb = items.Single(i => i.SourceType == LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.SourceTypeName);
        Assert.Equal(Path.GetDirectoryName(ws.FullPath(db.StoredPath)), Path.GetDirectoryName(ws.FullPath(gdb.StoredPath)));
        Assert.StartsWith("WindowsSearch_", Path.GetFileName(Path.GetDirectoryName(ws.FullPath(gdb.StoredPath))));
        Assert.Contains(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.GatherSameSnapshotNote, gdb.Notes);
        Assert.Contains(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser.SameSnapshotNote, db.Notes);
        var key = Path.GetFileName(Path.GetDirectoryName(ws.FullPath(gdb.StoredPath)))!["WindowsSearch_".Length..];
        Assert.EndsWith(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.SnapshotKeyPrefix + key, gdb.Notes);
        Assert.EndsWith(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.SnapshotKeyPrefix + key, db.Notes);

        var events = ParseIndexPair(ws, db, gdb);
        Assert.Contains(events, e => e.Source == "WindowsSearch");
        Assert.Contains(events, e => e.Source == "WindowsSearchGather");
        Assert.All(events, e => Assert.Equal(key, e.Fields[LogAnalyzer.Dfir.Analysis.Wp11Rules.SearchMomentField]));
        var found = LogAnalyzer.Dfir.Analysis.Wp11Rules.Run(events, [], () => "F-1").Where(f => f.RuleId == "WSEARCH-GATHER-ONLY").ToList();
        var one = Assert.Single(found);
        Assert.Contains("gone.txt", one.File);
    }

    [Fact]
    public void A_package_whose_search_step_did_not_end_ok_gets_no_same_snapshot_note_no_key_and_no_finding()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", ["search-index"]);
        var check = RemoteCollection.Verify(ws, IndexPackage(req, sha, "failed", withWal: false, "pk_g_failed", withGather: true));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var items = RemoteCollection.Import(ws, check);
        var db = items.Single(i => i.SourceType == LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchIndexParser.SourceTypeName);
        var gdb = items.Single(i => i.SourceType == LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.SourceTypeName);
        Assert.DoesNotContain(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.GatherSameSnapshotNote, gdb.Notes);
        Assert.DoesNotContain(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.SnapshotKeyPrefix, gdb.Notes);
        Assert.DoesNotContain(LogAnalyzer.Dfir.Windows.Parsers.WindowsSearchGatherParser.SnapshotKeyPrefix, db.Notes);
        var events = ParseIndexPair(ws, db, gdb);
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.False(e.Fields.ContainsKey(LogAnalyzer.Dfir.Analysis.Wp11Rules.SearchMomentField)));
        Assert.DoesNotContain(LogAnalyzer.Dfir.Analysis.Wp11Rules.Run(events, [], () => "F-1"), f => f.RuleId == "WSEARCH-GATHER-ONLY");
    }

    [Fact]
    public void A_gather_file_swapped_after_Verify_is_refused_at_import_and_nothing_stays_beside_Windows_db()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", ["search-index"]);
        var pkg = IndexPackage(req, sha, "ok", withWal: false, "pk_g_swap", withGather: true);
        var check = RemoteCollection.Verify(ws, pkg);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        File.AppendAllText(Path.Combine(pkg, "files", RemoteCollection.SearchIndexFolder, "Windows-gather.db"), "tampered");
        Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, check));
        // the folder of the pair is removed with everything in it
        var raw = ws.RawDir("remote:PC01");
        var left = Directory.Exists(raw) ? Directory.GetFiles(raw, "Windows*.db*", SearchOption.AllDirectories) : [];
        Assert.Empty(left);
    }

    [Fact]
    public void The_search_index_step_takes_database_and_journal_from_one_shadow_copy_and_always_deletes_it()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", ["search-index", "notepad-tabs"]);
        var script = RemoteCollection.PackageScript(req, sha);
        // not run here: it would create a shadow copy on the machine that runs the tests; the run is checked in the lab
        Assert.Contains($"Win32_Shadow{Destructive.Cut}Copy", script);
        Assert.Contains("Remove-CimInstance", script);
        Assert.Contains("finally", script);
        Assert.DoesNotContain("Stop-Service", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("esentutl", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.WindowsNotepad_8wekyb3d8bbwe", script);
    }

    [Fact]
    public void The_package_collects_the_Defender_history_folder_or_records_why_not()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["defender-history"]);
        var script = RemoteCollection.PackageScript(req, sha);
        Assert.Contains(@"Microsoft\Windows Defender\Scans\History\Service\DetectionHistory", script);
        var check = RemoteCollection.Verify(ws, RunPackage(script));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var step = check.Manifest!.Steps.Single();
        Assert.Contains(step.Status, new[] { "ok", "absent", "failed" });
        // the folder is readable by administrators only: without elevation the step fails and says why, it is never silently "ok" and empty
        if (!check.Manifest.Elevated) Assert.NotEqual("ok", step.Status);
        if (step.Status != "ok") Assert.False(string.IsNullOrWhiteSpace(step.Error));
        if (step.Status == "ok")
        {
            var items = RemoteCollection.Import(ws, check);
            Assert.All(items.Where(i => i.StoredPath.Contains(RemoteCollection.DefenderHistoryFolder, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(Path.GetFileName(i.StoredPath), out _)),
                i => Assert.Equal(LogAnalyzer.Dfir.FileSystem.DefenderDetectionHistoryParser.SourceTypeName, i.SourceType));
        }
    }

    // ---- remote-tool-logs ----

    private static string RtCorpus(string rel) => Path.Combine(AppContext.BaseDirectory, "Corpus", "RemoteToolLogs", rel);

    [Fact]
    public void Remote_tool_logs_are_a_known_artifact_and_the_script_parses_in_PowerShell_5_1()
    {
        Assert.Contains("remote-tool-logs", RemoteCollection.KnownArtifacts);
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", ["remote-tool-logs"]);
        var script = RemoteCollection.PackageScript(req, sha);
        Assert.Contains("connection_trace.txt", script);
        Assert.Contains("ad_svc.trace", script);
        Assert.Contains("Connections_incoming.txt", script);
        Assert.Contains("ReparsePoint", script);                       // no link followed, component by component
        Assert.Contains("16MB", script);
        Assert.Contains("64MB", script);
        Assert.Contains("ReadWrite,Delete", script);                   // shared read: the tool may hold its log open
        Assert.DoesNotContain("Invoke-WebRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stop-Service", script, StringComparison.OrdinalIgnoreCase);
        var file = Path.Combine(_root, "syntax_rt", "collect.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, script);
        var check = Path.Combine(_root, "syntax_rt", "check.ps1");
        File.WriteAllText(check, "param([string]$f) $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); if ($e.Count -gt 0) { $e | ForEach-Object { $_.Message }; exit 1 }");
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{check}\" \"{file}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.Environment.Remove("PSModulePath");
        using var pr = Process.Start(psi)!;
        var outText = pr.StandardOutput.ReadToEnd(); pr.StandardError.ReadToEnd(); pr.WaitForExit();
        Assert.True(pr.ExitCode == 0, outText);
    }

    [Fact]
    public void Remote_tool_logs_in_a_package_are_typed_only_in_their_folder_by_exact_name_and_content()
    {
        var pkg = Path.Combine(_root, "pkg_rt");
        var anydesk = Path.Combine(pkg, "files", RemoteCollection.RemoteToolLogsFolder, "Users", "ana", "AnyDesk"); Directory.CreateDirectory(anydesk);
        var tv = Path.Combine(pkg, "files", RemoteCollection.RemoteToolLogsFolder, "ProgramFiles", "TeamViewer"); Directory.CreateDirectory(tv);
        var ct = Path.Combine(anydesk, "connection_trace.txt"); File.Copy(RtCorpus(@"anydesk_TEST\connection_trace.txt"), ct);
        var ad = Path.Combine(anydesk, "ad.trace"); File.Copy(RtCorpus(@"anydesk_TEST\ad_svc.trace"), ad);
        var cin = Path.Combine(tv, "Connections_incoming.txt"); File.Copy(RtCorpus(@"teamviewer_TEST\Connections_incoming.txt"), cin);
        var fakeCt = Path.Combine(anydesk, "other"); Directory.CreateDirectory(fakeCt);
        var junk = Path.Combine(fakeCt, "connection_trace.txt"); File.WriteAllText(junk, "not a session log\r\n");
        var renamed = Path.Combine(anydesk, "connection_trace_copy.txt"); File.Copy(ct, renamed);
        string Rel(string p) => Path.GetRelativePath(pkg, p).Replace('\\', '/');
        Assert.Equal(LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskConnectionTrace, RemoteCollection.ImportKind(Rel(ct), ct));
        Assert.Equal(LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskTrace, RemoteCollection.ImportKind(Rel(ad), ad));
        Assert.Equal(LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.TeamViewerConnections, RemoteCollection.ImportKind(Rel(cin), cin));
        Assert.Equal("file", RemoteCollection.ImportKind(Rel(junk), junk));                        // right name, wrong content
        Assert.Equal("file", RemoteCollection.ImportKind(Rel(renamed), renamed));                  // right content, wrong name
        Assert.Equal("file", RemoteCollection.ImportKind("files/other/connection_trace.txt", ct)); // right file, outside the folder
        Assert.Equal("file", RemoteCollection.ImportKind("files/" + RemoteCollection.NotepadTabsFolder + "/ana/connection_trace.txt", ct));
        // a user profile named "Tasks": the session log is still a session log, never task XML (review finding 2026-11-11)
        Assert.Equal(LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskConnectionTrace,
            RemoteCollection.ImportKind("files/" + RemoteCollection.RemoteToolLogsFolder + "/Users/Tasks/AnyDesk/connection_trace.txt", ct));
    }

    private readonly List<string> _shortRoots = [];

    /// <summary>
    /// A station folder under a SHORT path (C:\Users\Public\lad_xxxxxxxx): the hard-link check of the package runs fsutil.exe, a native tool that fails on a path over
    /// 260 characters, and the test temp folder can be that long. Falls back to the test root when there is no public folder.
    /// </summary>
    private string ShortBase(string name)
    {
        var pub = Environment.GetEnvironmentVariable("PUBLIC");
        if (string.IsNullOrEmpty(pub) || !Directory.Exists(pub)) return Path.Combine(_root, name);
        var top = Path.Combine(pub, "lad_" + Guid.NewGuid().ToString("N")[..8]);
        _shortRoots.Add(top);
        return Path.Combine(top, name);
    }

    private string FakeStation(string name, bool shortPath = false)
    {
        var fake = shortPath ? ShortBase(name) : Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(fake, "Users"));
        Directory.CreateDirectory(Path.Combine(fake, "ProgramData"));
        Directory.CreateDirectory(Path.Combine(fake, "Program Files"));
        Directory.CreateDirectory(Path.Combine(fake, "Program Files (x86)"));
        return fake;
    }

    private string RunPackageOnFakeStation(string script, string fake)
    {
        var dir = Path.Combine(_root, "target_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "collect.ps1");
        File.WriteAllText(file, script, new System.Text.UTF8Encoding(true));
        var psi = PowerShell(file);
        // the script builds every path from these variables: it reads the synthetic station, never the real one
        psi.Environment["SystemDrive"] = fake;
        psi.Environment["ProgramData"] = Path.Combine(fake, "ProgramData");
        psi.Environment["ProgramFiles"] = Path.Combine(fake, "Program Files");
        psi.Environment["ProgramW6432"] = Path.Combine(fake, "Program Files");   // Windows PowerShell takes ProgramFiles from here
        psi.Environment["ProgramFiles(x86)"] = Path.Combine(fake, "Program Files (x86)");
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"exit {p.ExitCode}: {stderr}");
        return stdout.Trim().Split('\n').Last().Trim();
    }

    [Fact]
    public void The_package_script_collects_session_logs_from_a_synthetic_station_and_the_import_types_them()
    {
        var fake = FakeStation("station1");
        var ana = Path.Combine(fake, "Users", "ana", "AppData", "Roaming");
        Directory.CreateDirectory(Path.Combine(ana, "AnyDesk")); Directory.CreateDirectory(Path.Combine(ana, "TeamViewer"));
        File.Copy(RtCorpus(@"anydesk_TEST\connection_trace.txt"), Path.Combine(ana, "AnyDesk", "connection_trace.txt"));
        File.Copy(RtCorpus(@"anydesk_TEST\ad_svc.trace"), Path.Combine(ana, "AnyDesk", "ad.trace"));
        File.Copy(RtCorpus(@"teamviewer_TEST\Connections_incoming.txt"), Path.Combine(ana, "TeamViewer", "Connections.txt"));
        File.WriteAllBytes(Path.Combine(ana, "AnyDesk", "ad_svc.trace"), []);                               // not looked for in a profile
        Directory.CreateDirectory(Path.Combine(fake, "ProgramData", "AnyDesk"));
        File.Copy(RtCorpus(@"anydesk_TEST\ad_svc.trace"), Path.Combine(fake, "ProgramData", "AnyDesk", "ad_svc.trace"));
        File.WriteAllBytes(Path.Combine(fake, "ProgramData", "AnyDesk", "connection_trace.txt"), []);       // empty: skipped
        Directory.CreateDirectory(Path.Combine(fake, "Program Files", "TeamViewer"));
        File.Copy(RtCorpus(@"teamviewer_TEST\Connections_incoming.txt"), Path.Combine(fake, "Program Files", "TeamViewer", "Connections_incoming.txt"));

        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["remote-tool-logs"]);
        var pkg = RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha), fake);
        var check = RemoteCollection.Verify(ws, pkg);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var step = check.Manifest!.Steps.Single();
        Assert.Equal("ok", step.Status);
        var files = check.Manifest.Files.Select(f => f.Path).OrderBy(x => x).ToList();
        Assert.Equal(
        [
            "files/RemoteToolLogs/ProgramData/AnyDesk/ad_svc.trace",
            "files/RemoteToolLogs/ProgramFiles/TeamViewer/Connections_incoming.txt",
            "files/RemoteToolLogs/Users/ana/AnyDesk/ad.trace",
            "files/RemoteToolLogs/Users/ana/AnyDesk/connection_trace.txt",
            "files/RemoteToolLogs/Users/ana/TeamViewer/Connections.txt",
        ], files);

        var items = RemoteCollection.Import(ws, check).Where(i => i.SourceType != "remote-manifest").ToList();
        Assert.Equal(5, items.Count);
        Assert.Equal(2, items.Count(i => i.SourceType == LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskTrace));
        Assert.Single(items, i => i.SourceType == LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskConnectionTrace);
        Assert.Equal(2, items.Count(i => i.SourceType == LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.TeamViewerConnections));
        // the session logs carry the TARGET's zone from the manifest (here the fake station is this machine), not the case machine's by assumption
        Assert.Equal(TimeZoneInfo.Local.Id, check.Manifest.Timezone);
        Assert.All(items, i => Assert.Equal(check.Manifest.Timezone, i.Timezone));
    }

    [Fact]
    public void The_target_zone_survives_the_reimport_into_the_case_of_the_run()
    {
        // lab 2026-11-14: "Verifică și importă" puts the package in the live case; the run then re-imports the stored files into a NEW case
        // through InvestigationPipeline.Import. The zone from the manifest must travel with them; without it a session log stays unconverted.
        var fake = FakeStation("station-tz");
        var roaming = Path.Combine(fake, "Users", "ana", "AppData", "Roaming", "AnyDesk"); Directory.CreateDirectory(roaming);
        File.Copy(RtCorpus(@"anydesk_TEST\connection_trace.txt"), Path.Combine(roaming, "connection_trace.txt"));
        var live = NewCase();
        var (req, sha) = RemoteCollection.Authorize(live, Environment.MachineName, @"STATIE\operator", "investigație de test", ["remote-tool-logs"]);
        var check = RemoteCollection.Verify(live, RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha), fake));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var stored = RemoteCollection.Import(live, check).Single(i => i.SourceType == LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskConnectionTrace);
        var path = live.FullPath(stored.StoredPath);
        Assert.Equal(check.Manifest!.Timezone, stored.Timezone);

        CaseWorkspace Other(string name) => CaseWorkspace.Create(Path.Combine(_root, name), new CaseInfo { CaseId = "CASE-" + name, Name = name, CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var run = Other("case-run");
        var kept = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.Import(run, [path], new Dictionary<string, string> { [path] = stored.Timezone }).Single();
        Assert.Equal(LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskConnectionTrace, kept.SourceType);
        Assert.Equal(stored.Timezone, kept.Timezone);
        Assert.NotNull(LogAnalyzer.Dfir.RemoteTools.RemoteToolLogText.ZoneOf(kept).Info);
        // the same file imported by the operator with no known zone: unconverted, never this machine's zone by assumption
        var bare = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.Import(Other("case-bare"), [path]).Single();
        Assert.Equal("", bare.Timezone);
    }

    [Fact]
    public void Carried_types_and_origins_survive_the_reimport_of_files_renamed_by_the_live_case()
    {
        // lab 2026-11-16: the live case stored a second NTUSER.DAT as NTUSER_1.DAT; the run re-typed it by name as "file" and it was never parsed
        var dir = Path.Combine(_root, "renamed"); Directory.CreateDirectory(dir);
        var first = Path.Combine(dir, "NTUSER.DAT"); var second = Path.Combine(dir, "NTUSER_1.DAT");
        foreach (var f in new[] { first, second }) File.WriteAllBytes(f, [(byte)'r', (byte)'e', (byte)'g', (byte)'f', 0, 0, 0, 0]);
        CaseWorkspace Other(string name) => CaseWorkspace.Create(Path.Combine(_root, name), new CaseInfo { CaseId = "CASE-" + name, Name = name, CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var carried = new Dictionary<string, (string, string)>
        {
            [first] = ("ntuser_hive", "profil de origine: ana"), [second] = ("ntuser_hive", "profil de origine: bob"),
        };
        var items = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.Import(Other("case-carried"), [first, second], null, carried);
        Assert.All(items, i => Assert.Equal("ntuser_hive", i.SourceType));
        Assert.Contains(items, i => i.Notes.Contains("profil de origine: bob"));
        // without what the package import decided, the renamed copy is not a hive (the old behaviour, still right for an operator import)
        var bare = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.Import(Other("case-bare2"), [second]).Single();
        Assert.Equal("file", bare.SourceType);
    }

    [Fact]
    public void A_manifest_without_a_zone_leaves_session_log_times_unconverted()
    {
        // an older package (no "timezone") deserialises with Timezone null; the import then gives a session log no zone, never the analyst's
        var m = System.Text.Json.JsonSerializer.Deserialize<RemoteManifest>("{\"format\":\"x\",\"requestId\":\"r\",\"requestSha256\":\"s\",\"host\":\"h\",\"user\":\"u\",\"elevated\":false,\"collectedUtc\":\"c\",\"steps\":[],\"files\":[]}");
        Assert.NotNull(m);
        Assert.Null(m!.Timezone);
        var ws = NewCase();
        var item = ws.ImportFile(RtCorpus(@"anydesk_TEST\connection_trace.txt"), "remote:h", LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskConnectionTrace, TemporalType.Historical, "test", "1", timezone: "");
        Assert.Equal("", item.Timezone);
        Assert.Null(LogAnalyzer.Dfir.RemoteTools.RemoteToolLogText.ZoneOf(item).Info);
        // a zone id from the target longer than any Windows id is not looked up
        var odd = ws.ImportFile(RtCorpus(@"anydesk_TEST\connection_trace.txt"), "remote:h", LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.AnyDeskConnectionTrace, TemporalType.Historical, "test", "1", timezone: new string('x', 65));
        Assert.Null(LogAnalyzer.Dfir.RemoteTools.RemoteToolLogText.ZoneOf(odd).Info);
    }

    [Fact]
    public void The_package_script_does_not_follow_a_link_on_the_path_and_says_so()
    {
        var fake = FakeStation("station2");
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.Copy(RtCorpus(@"anydesk_TEST\connection_trace.txt"), Path.Combine(elsewhere, "connection_trace.txt"));
        var roaming = Path.Combine(fake, "Users", "bob", "AppData", "Roaming");
        Directory.CreateDirectory(roaming);
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(roaming, "AnyDesk")}\" \"{elsewhere}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var mk = Process.Start(psi)!) { mk.WaitForExit(10000); if (mk.ExitCode != 0) return; }   // junctions not available here

        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["remote-tool-logs"]);
        var pkg = RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha), fake);
        var check = RemoteCollection.Verify(ws, pkg);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var step = check.Manifest!.Steps.Single();
        Assert.NotEqual("ok", step.Status);
        Assert.Contains("legaturi neurmate: 1", step.Error);
        Assert.Empty(check.Manifest.Files);                                                         // the file behind the junction was not taken
        Directory.Delete(Path.Combine(roaming, "AnyDesk"));
    }

    [Fact]
    public void Tampered_missing_extra_unauthorized_or_foreign_packages_are_refused()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, "operator", "test", ["evtx:Application"]);
        var pkg = RunPackage(RemoteCollection.PackageScript(req, sha));
        Assert.True(RemoteCollection.Verify(ws, pkg).Ok);
        var evtx = Directory.GetFiles(pkg, "*.evtx", SearchOption.AllDirectories).Single();

        File.WriteAllText(Path.Combine(pkg, "files", "extra.txt"), "x");
        Assert.Contains(RemoteCollection.Verify(ws, pkg).Problems, p => p.Contains("extra.txt"));
        File.Delete(Path.Combine(pkg, "files", "extra.txt"));

        var bytes = File.ReadAllBytes(evtx);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(evtx, bytes);
        var bad = RemoteCollection.Verify(ws, pkg);
        Assert.False(bad.Ok);
        Assert.Contains(bad.Problems, p => p.Contains("SHA-256 diferit de cel calculat pe țintă"));
        Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, bad));

        File.Delete(evtx);
        Assert.Contains(RemoteCollection.Verify(ws, pkg).Problems, p => p.StartsWith("lipsește"));

        var manifest = Path.Combine(pkg, "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(Environment.MachineName, "ALTHOST", StringComparison.OrdinalIgnoreCase));
        var forged = RemoteCollection.Verify(ws, pkg).Problems;
        Assert.Contains(forged, p => p.Contains("modificat după colectare"));
        Assert.Contains(forged, p => p.Contains("autorizat pentru"));

        // A package from another case (its request is not here) is not imported.
        var other = CaseWorkspace.Create(Path.Combine(_root, "other"), new CaseInfo { CaseId = "CASE-OTHER", Name = "o", CreatedAtUtc = DateTimeOffset.UtcNow , Scope = TestScopes.Valid() });
        Assert.Contains(RemoteCollection.Verify(other, pkg).Problems, p => p.Contains("nu a fost autorizată aici"));
    }

    [Fact]
    public void Package_refuses_another_host_and_requests_are_validated()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "ALTA-STATIE", "operator", "test", ["evtx:Application"]);
        var script = RemoteCollection.PackageScript(req, sha);
        var file = Path.Combine(_root, "t2", "collect.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, script);
        using (var p = Process.Start(PowerShell(file))!) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); Assert.Equal(2, p.ExitCode); }
        Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(file)!)); // nothing collected

        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "host'; Remove-Item C:\\", "op", "j", ["evtx:Application"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "10.0.0.5", "op", "j", ["evtx:Application"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", " ", ["evtx:Application"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", "j", ["hive:SAM"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", "j", []));
    }

    [Fact]
    public void Package_contains_no_network_command_and_no_credential_store()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", RemoteCollection.KnownArtifacts);
        var script = RemoteCollection.PackageScript(req, sha);
        Assert.DoesNotContain("Get-FileHash", script, StringComparison.OrdinalIgnoreCase); // hashing must not depend on a module
        foreach (var forbidden in new[] { "Invoke-WebRequest", "Invoke-RestMethod", "Invoke-Command", "New-PSSession", "Enter-PSSession", "Start-BitsTransfer",
                                          "Net.WebClient", "Net.Sockets", "-ComputerName", "-CimSession", "New-CimSession", "HttpClient", "curl", "wget", "ftp", "Send-MailMessage", "\\\\\\\\", "Copy-Item -ToSession",
                                          "HKLM\\SAM", "HKLM\\SECURITY", "Cookies", "Login Data" })
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RemoteCollection.KnownArtifacts, a => a.Contains("SAM") || a.Contains("SECURITY"));
        Assert.True(AppModeContext.IsAirGapped);
    }

    // ---- user hives (optional artifact) ----

    private static byte[] SyntheticUsrClass()
    {
        var root = new RegfBuilder.Key("ROOT", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        var bag = root.Sub(@"Local Settings\Software\Microsoft\Windows\Shell\BagMRU");
        bag.Value("0", RegfBuilder.Binary, ShellItemBuilder.List(ShellItemBuilder.Root(ShellItemBuilder.ThisPc)));
        bag.Value("MRUListEx", RegfBuilder.Binary, ShellItemBuilder.Mru(0, 0xFFFFFFFF));
        return RegfBuilder.Build(root, "UsrClass.dat");
    }

    private static byte[] SyntheticNtUser()
    {
        var root = new RegfBuilder.Key("ROOT", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        root.Sub(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs").Value("MRUListEx", RegfBuilder.Binary, ShellItemBuilder.Mru(0xFFFFFFFF));
        return RegfBuilder.Build(root, "NTUSER.DAT");
    }

    [Fact]
    public void Every_file_kind_the_package_writes_is_read_by_a_parser_or_is_a_known_companion()
    {
        // audit 2026-11-16: "files/SYSTEM.hive" was typed "registry-hive" and "files/Tasks/..." "task-xml", types no parser reads: the SYSTEM / SOFTWARE hives
        // of every remote package were never parsed (found on a real SRV01 package: "niciun parser înregistrat"). Each kind must reach a parser now.
        var dir = Path.Combine(_root, "kinds"); Directory.CreateDirectory(dir);
        string Put(string name, byte[] bytes) { var p = Path.Combine(dir, name); File.WriteAllBytes(p, bytes); return p; }
        var hive = Put("SYSTEM.hive", SyntheticNtUser());
        var soft = Put("SOFTWARE.hive", SyntheticNtUser());
        var task = Put("task", System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Actions><Exec><Command>C:\\TEST\\a.exe</Command></Exec></Actions></Task>")).ToArray());
        var registry = LogAnalyzer.Dfir.Windows.Parsers.WindowsParsers.Registry;
        foreach (var (rel, full, expected) in new[]
        {
            ("files/SYSTEM.hive", hive, "system_hive"), ("files/SOFTWARE.hive", soft, "software_hive"), ("files/Tasks/Microsoft/Windows/TEST/task", task, "task_xml"),
        })
        {
            var kind = RemoteCollection.ImportKind(rel, full);
            Assert.Equal(expected, kind);
            var item = new EvidenceItem { EvidenceId = "EV-K", CaseId = "C", Source = "remote:test", SourceType = kind, StoredPath = full };
            Assert.NotEmpty(registry.Candidates(item));
        }
        // artifacts added to approach the live collection: each kind must be exactly the live collector's SourceType and reach a parser (or be a documented companion)
        var lnk = LnkParserTests.BuildLnk(@"C:\Users\u\Downloads\SETUP.EXE", "/x", @"C:\Users\u\Downloads", "ws-01");
        foreach (var (rel, name, bytes, expected) in new (string, string, byte[], string)[]
        {
            ("files/Amcache/Amcache.hve", "Amcache.hve", SyntheticNtUser(), "amcache"),
            ("files/Srum/SRUDB.dat", "SRUDB.dat", SyntheticSrudb(), "srum"),
            ("files/SetupApi/setupapi.dev.log", "setupapi.dev.log", SyntheticSetupApi(), LogAnalyzer.Dfir.Devices.SetupApiLogParser.SourceTypeName),
            ("files/SetupApi/setupapi.dev.20260101_120000.log", "setupapi.dev.20260101_120000.log", SyntheticSetupApi(), LogAnalyzer.Dfir.Devices.SetupApiLogParser.SourceTypeName),
            ("files/Firewall/pfirewall.log", "pfirewall.log", SyntheticFirewall(), LogAnalyzer.Dfir.Network.FirewallLogParser.SourceTypeName),
            ("files/Firewall/pfirewall.log.old", "pfirewall.log.old", SyntheticFirewall(), LogAnalyzer.Dfir.Network.FirewallLogParser.SourceTypeName),
            ("files/RecentItems/ana/Recent/a.lnk", "a.lnk", lnk, "lnk"),
            ("files/RecentItems/ana/AutomaticDestinations/f01b4d95cf55d32a.automaticDestinations-ms", "f01b4d95cf55d32a.automaticDestinations-ms", SyntheticAutoJumpList(lnk), "jumplist_auto"),
            ("files/RecentItems/ana/CustomDestinations/5f7b5f1e01b83767.customDestinations-ms", "5f7b5f1e01b83767.customDestinations-ms", NewSourcesRoutingTests.CustomDestinations(lnk), LogAnalyzer.Dfir.FileSystem.CustomJumpListParser.SourceTypeName),
        })
        {
            var newFull = Put(name, bytes);
            var newKind = RemoteCollection.ImportKind(rel, newFull);
            Assert.Equal(expected, newKind);
            var newItem = new EvidenceItem { EvidenceId = "EV-K", CaseId = "C", Source = "remote:test", SourceType = newKind, StoredPath = newFull };
            Assert.NotEmpty(registry.Candidates(newItem));
        }
        // the companions the live collectors also keep (no parser reads them on their own; AmcacheParser and SrumNetworkParser use them beside the database)
        foreach (var (rel, name, expected) in new[] { ("files/Amcache/Amcache.hve.LOG1", "Amcache.hve.LOG1", "amcache_log"), ("files/Amcache/Amcache.hve.LOG2", "Amcache.hve.LOG2", "amcache_log"),
                                                      ("files/Srum/SRU.chk", "SRU.chk", SrumCollector.SrumLogSourceType), ("files/Srum/SRU00001.log", "SRU00001.log", SrumCollector.SrumLogSourceType),
                                                      ("files/Srum/SRUDB.jfm", "SRUDB.jfm", SrumCollector.SrumLogSourceType), ("files/Srum/SRUtmp.jrs", "SRUtmp.jrs", SrumCollector.SrumLogSourceType) })
            Assert.Equal(expected, RemoteCollection.ImportKind(rel, Put(name, [1, 2, 3, 4, 5, 6, 7, 8])));
        // a .hive that is not SYSTEM / SOFTWARE, or not a regf file, stays "file"
        Assert.Equal("file", RemoteCollection.ImportKind("files/SAM.hive", Put("SAM.hive", SyntheticNtUser())));
        Assert.Equal("file", RemoteCollection.ImportKind("files/SYSTEM.hive", Put("not-a-hive", [1, 2, 3, 4])));
        // the operator import recognises the package names too (an analyst importing the files folder by hand)
        Assert.Equal("system_hive", LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.ImportType(hive));
        Assert.Equal("software_hive", LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.ImportType(soft));
    }

    private static void PutHives(string fake, string profile, bool ntuser = true, bool usrclass = true)
    {
        var home = Path.Combine(fake, "Users", profile);
        Directory.CreateDirectory(home);
        if (ntuser) File.WriteAllBytes(Path.Combine(home, "NTUSER.DAT"), SyntheticNtUser());
        if (!usrclass) return;
        var w = Path.Combine(home, "AppData", "Local", "Microsoft", "Windows");
        Directory.CreateDirectory(w);
        File.WriteAllBytes(Path.Combine(w, "UsrClass.dat"), SyntheticUsrClass());
    }

    [Fact]
    public void Authorize_refuses_an_unknown_artifact_accepts_user_hives_and_the_default_list_does_not_contain_them()
    {
        var ws = NewCase();
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", "j", ["user-hives-all"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", "j", ["user-hives", "hive:SAM"]));
        var (req, _) = RemoteCollection.Authorize(ws, "PC01", "op", "j", [RemoteCollection.UserHivesArtifact]);
        Assert.Equal(["user-hives"], req.Artifacts);
        Assert.DoesNotContain("user-hives", RemoteCollection.KnownArtifacts);
        Assert.Equal(["user-hives", "srum", "recent-items", "ps-transcripts"], RemoteCollection.OptionalArtifacts);
        Assert.DoesNotContain("user-hives", RemoteCollection.ArtifactsFor(false));
        Assert.Equal([.. RemoteCollection.KnownArtifacts, "user-hives"], RemoteCollection.ArtifactsFor(true));
        // a request edited to carry an unknown artifact still cannot generate a package
        Assert.Throws<ArgumentException>(() => RemoteCollection.PackageScript(req with { Artifacts = ["user-hives", "evil"] }, new string('A', 64)));
        var script = RemoteCollection.PackageScript(req, new string('A', 64));
        foreach (var forbidden in new[] { "Invoke-WebRequest", "Net.WebClient", "curl", "wget", "ftp", "HKLM\\SAM", "HKLM\\SECURITY", "Cookies", "Login Data" })
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_user_hives_step_collects_from_a_synthetic_station_refuses_links_and_oversized_files_and_the_import_types_them()
    {
        var fake = FakeStation("station-hives");
        PutHives(fake, "ana"); PutHives(fake, "bob");
        PutHives(fake, "dora", usrclass: false);
        File.WriteAllText(Path.Combine(fake, "Users", "dora", "NTUSER.DAT"), "nu este un hive de registru");          // right name, wrong content
        // carl: NTUSER.DAT is fine, UsrClass.dat is behind a junction on the path (refused and counted)
        PutHives(fake, "carl", usrclass: false);
        var elsewhere = Path.Combine(_root, "elsewhere-hives");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllBytes(Path.Combine(elsewhere, "UsrClass.dat"), SyntheticUsrClass());
        var carlLocal = Path.Combine(fake, "Users", "carl", "AppData", "Local", "Microsoft");
        Directory.CreateDirectory(carlLocal);
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(carlLocal, "Windows")}\" \"{elsewhere}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var mk = Process.Start(psi)!) { mk.WaitForExit(10000); if (mk.ExitCode != 0) return; }                  // junctions not available here
        // eve: NTUSER.DAT over the 512 MiB cap (never read), UsrClass.dat fine
        PutHives(fake, "eve", ntuser: false);
        using (var big = new FileStream(Path.Combine(fake, "Users", "eve", "NTUSER.DAT"), FileMode.CreateNew)) big.SetLength(513L * 1024 * 1024);

        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["user-hives"]);
        var pkg = RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha), fake);
        var check = RemoteCollection.Verify(ws, pkg);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var step = check.Manifest!.Steps.Single();
        Assert.NotEqual("ok", step.Status);
        Assert.Contains("legaturi neurmate: 1", step.Error);
        Assert.Contains("sarite (peste 512 MiB): 1", step.Error);
        Assert.Contains("hive-uri copiate: 7", step.Error);
        Assert.Equal(
        [
            "files/UserHives/ana/NTUSER.DAT", "files/UserHives/ana/UsrClass.dat",
            "files/UserHives/bob/NTUSER.DAT", "files/UserHives/bob/UsrClass.dat",
            "files/UserHives/carl/NTUSER.DAT",
            "files/UserHives/dora/NTUSER.DAT",
            "files/UserHives/eve/UsrClass.dat",
        ], check.Manifest.Files.Select(f => f.Path).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Assert.Empty(Directory.GetFiles(pkg, "*", SearchOption.AllDirectories).Where(f => new FileInfo(f).Length > 100 * 1024 * 1024));

        var items = RemoteCollection.Import(ws, check).Where(i => i.SourceType != "remote-manifest").ToList();
        Assert.Equal(7, items.Count);
        Assert.Equal(3, items.Count(i => i.SourceType == "ntuser_hive"));
        Assert.Equal(3, items.Count(i => i.SourceType == "usrclass_hive"));
        var notHive = Assert.Single(items, i => i.SourceType == "file");
        Assert.Contains("dora", notHive.Notes);
        foreach (var f in check.Manifest.Files)
        {
            var it = items.Single(i => i.Notes.Contains(f.Path, StringComparison.Ordinal));
            Assert.Equal(f.Sha256, it.Sha256, ignoreCase: true);                                   // SHA-256 at import equals the manifest's (the target's)
            Assert.Contains("profil de origine: " + f.Path.Split('/')[2], it.Notes);
        }

        // the ShellBags parser runs on the imported UsrClass.dat
        var uc = items.First(i => i.SourceType == "usrclass_hive" && i.Notes.Contains("/ana/"));
        var sink = new LogAnalyzer.Dfir.Model.ListSink();
        var parsed = new LogAnalyzer.Dfir.Windows.Parsers.ShellBagsParser().Parse(uc, ws.FullPath(uc.StoredPath), sink, default);
        Assert.Equal(EvidenceStatus.Success, parsed.Status);
        Assert.NotEmpty(sink.Events);
        Directory.Delete(Path.Combine(carlLocal, "Windows"));
    }

    /// <summary>Runs a PowerShell snippet (PS 5.1, as the package does) and returns its trimmed stdout.</summary>
    private string RunSnippet(string script)
    {
        var dir = Path.Combine(_root, "snip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "s.ps1");
        File.WriteAllText(file, script, new System.Text.UTF8Encoding(true));
        using var p = Process.Start(PowerShell(file))!;
        var stdout = p.StandardOutput.ReadToEnd(); var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"exit {p.ExitCode}: {stderr}");
        return stdout.Trim();
    }

    private static string UserHiveHelpers(string script)
    {
        const string begin = "# BEGIN user-hives helpers", end = "# END user-hives helpers";
        var i = script.IndexOf(begin, StringComparison.Ordinal); var j = script.IndexOf(end, StringComparison.Ordinal);
        Assert.True(i >= 0 && j > i, "the helper block is missing from the script");
        return script[i..j];
    }

    [Fact]
    public void The_sharing_violation_check_finds_the_IOException_inside_the_MethodInvocationException_and_esentutl_is_not_run()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "op", "j", ["user-hives"]);
        var helpers = UserHiveHelpers(RemoteCollection.PackageScript(req, sha));
        var locked = Path.Combine(_root, "locked.dat"); File.WriteAllBytes(locked, [1, 2, 3]);
        var free = Path.Combine(_root, "free.dat"); File.WriteAllBytes(free, [1, 2, 3]);
        var missing = Path.Combine(_root, "missing.dat");
        // same call as the step: New-Object wraps the failure; only the helper (not the esentutl branch) is run here
        string Probe(string path) => "try { $s = New-Object IO.FileStream('" + path + "', [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite,Delete'); $s.Dispose(); 'open' } catch { 'busy=' + $(Test-SharingViolation $_.Exception) + ' type=' + $_.Exception.GetType().Name }\n";
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var r = RunSnippet(helpers + Probe(locked) + Probe(missing) + Probe(free));
            var lines = r.Split('\n').Select(l => l.Trim()).ToArray();
            Assert.Equal("busy=True type=MethodInvocationException", lines[0]);   // the wrapper is what the catch block sees
            Assert.Equal("busy=False type=MethodInvocationException", lines[1]);  // a missing file is a plain failure, not a lock
            Assert.Equal("open", lines[2]);
        }
        Assert.DoesNotContain("esentutl", helpers, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class HardLinkFactAttribute : FactAttribute
    {
        public HardLinkFactAttribute()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ladfir_hl_probe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "a"), "x");
                if (!MakeHardLink(Path.Combine(dir, "b"), Path.Combine(dir, "a"))) Skip = "hard links cannot be created on this volume (mklink /H failed)";
            }
            finally { Directory.Delete(dir, true); }
        }
    }

    private static bool MakeHardLink(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var mk = Process.Start(psi)!;
        mk.StandardOutput.ReadToEnd(); mk.StandardError.ReadToEnd();
        mk.WaitForExit(10000);
        return mk.ExitCode == 0;
    }

    [HardLinkFact]
    public void The_user_hives_step_refuses_a_hard_linked_hive_and_counts_it()
    {
        var fake = FakeStation("station-hardlink");
        PutHives(fake, "ana", usrclass: false);
        var elsewhere = Path.Combine(_root, "elsewhere-hl");
        Directory.CreateDirectory(elsewhere);
        var original = Path.Combine(elsewhere, "someone-elses-hive.dat");
        File.WriteAllBytes(original, SyntheticNtUser());
        var fred = Path.Combine(fake, "Users", "fred"); Directory.CreateDirectory(fred);
        Assert.True(MakeHardLink(Path.Combine(fred, "NTUSER.DAT"), original));

        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["user-hives"]);
        var check = RemoteCollection.Verify(ws, RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha), fake));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var step = check.Manifest!.Steps.Single();
        Assert.NotEqual("ok", step.Status);
        Assert.Contains("legaturi neurmate: 1", step.Error);
        Assert.Equal(["files/UserHives/ana/NTUSER.DAT"], check.Manifest.Files.Select(f => f.Path).ToList());
    }

    private static void InjectFile(string pkg, string relative, byte[] bytes)
    {
        var full = Path.Combine(pkg, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        var mp = Path.Combine(pkg, "manifest.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(mp))!;
        var arr = node["files"] as System.Text.Json.Nodes.JsonArray ?? [];
        node["files"] = arr;
        arr.Add(new System.Text.Json.Nodes.JsonObject { ["path"] = relative, ["size"] = bytes.Length, ["sha256"] = Hashing.Sha256File(full) });
        File.WriteAllText(mp, node.ToJsonString(), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(pkg, "manifest.sha256"), Hashing.Sha256File(mp) + "\n");   // the sums recomputed too
    }

    [Fact]
    public void Verify_refuses_a_file_in_the_folder_of_an_artifact_the_request_did_not_ask_for()
    {
        var fake = FakeStation("station-bind");
        var ws = NewCase();
        // request WITHOUT user-hives: a hive injected under files/UserHives, manifest and its sum recomputed
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["remote-tool-logs"]);
        var pkg = RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha), fake);
        Assert.True(RemoteCollection.Verify(ws, pkg).Ok);
        InjectFile(pkg, "files/UserHives/mallory/NTUSER.DAT", SyntheticNtUser());
        var bad = RemoteCollection.Verify(ws, pkg);
        Assert.False(bad.Ok);
        Assert.Contains(bad.Problems, p => p.Contains("user-hives") && p.Contains("nu este în cererea autorizată"));
        Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, bad));

        // the same injection under a request that does carry user-hives is accepted
        var (req2, sha2) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["remote-tool-logs", "user-hives"]);
        var pkg2 = RunPackageOnFakeStation(RemoteCollection.PackageScript(req2, sha2), fake);
        InjectFile(pkg2, "files/UserHives/mallory/NTUSER.DAT", SyntheticNtUser());
        var good = RemoteCollection.Verify(ws, pkg2);
        Assert.True(good.Ok, string.Join("; ", good.Problems));
    }

    [Fact]
    public void UnauthorizedFile_maps_every_package_folder_to_its_artifact()
    {
        string[] all = [.. RemoteCollection.KnownArtifacts, .. RemoteCollection.OptionalArtifacts];
        foreach (var path in new[]
        {
            "files/Security.evtx", "files/Microsoft-Windows-PowerShell_Operational.evtx", "files/SYSTEM.hive", "files/SOFTWARE.hive", "files/Prefetch/A.pf", "files/Tasks/x/y",
            "files/DefenderDetectionHistory/a/b", "files/NotepadTabState/ana/x.bin", "files/RemoteToolLogs/Users/ana/AnyDesk/connection_trace.txt",
            "files/WindowsSearch/Windows.db", "files/UserHives/ana/NTUSER.DAT", "files/NTFS/$MFT",
            "files/Amcache/Amcache.hve", "files/Srum/SRUDB.dat", "files/SetupApi/setupapi.dev.log", "files/Firewall/pfirewall.log", "files/RecentItems/ana/Recent/a.lnk",
            "files/Microsoft-Windows-WinRM_Operational.evtx", "files/Microsoft-Windows-TerminalServices-RemoteConnectionManager_Operational.evtx",
        })
            Assert.Null(RemoteCollection.UnauthorizedFile(path, all));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/SAM.hive", all));          // not an artifact
        // review r2 2026-11-16: a non-canonical path judged by its first folder while the file read is elsewhere
        var prefetchOnly = new[] { "prefetch" };
        foreach (var bad in new[] { "files/Prefetch/../UserHives/mallory/NTUSER.DAT", @"files/Prefetch/..\UserHives\mallory\NTUSER.DAT", "files/Prefetch/./../UserHives/m/NTUSER.DAT",
                                    "files/Prefetch//x.pf", "files/./Prefetch/x.pf", "files/Prefetch/../x.hive" })
            Assert.NotNull(RemoteCollection.UnauthorizedFile(bad, prefetchOnly));
        Assert.Null(RemoteCollection.UnauthorizedFile("files/Prefetch/CMD.EXE-1234ABCD.pf", prefetchOnly));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/Other/x", all));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("elsewhere/x", all));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/Security.evtx", ["evtx:System"]));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/Prefetch/A.pf", ["tasks"]));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/NTFS/$MFT", ["prefetch"]));
        // each new folder is bound to its own artifact only
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/Amcache/Amcache.hve", ["srum", "setupapi", "firewall-log", "recent-items"]));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/Srum/SRUDB.dat", ["amcache", "setupapi", "firewall-log", "recent-items"]));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/SetupApi/setupapi.dev.log", ["amcache", "srum", "firewall-log", "recent-items"]));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/Firewall/pfirewall.log", ["amcache", "srum", "setupapi", "recent-items"]));
        Assert.NotNull(RemoteCollection.UnauthorizedFile("files/RecentItems/ana/Recent/a.lnk", ["amcache", "srum", "setupapi", "firewall-log", "user-hives"]));
    }

    [Fact]
    public void ImportKind_types_user_hives_only_by_exact_path_and_content()
    {
        var dir = Path.Combine(_root, "kind"); Directory.CreateDirectory(dir);
        var nt = Path.Combine(dir, "NTUSER.DAT"); File.WriteAllBytes(nt, SyntheticNtUser());
        var uc = Path.Combine(dir, "UsrClass.dat"); File.WriteAllBytes(uc, SyntheticUsrClass());
        var junk = Path.Combine(dir, "junk.dat"); File.WriteAllText(junk, "x");
        Assert.Equal("ntuser_hive", RemoteCollection.ImportKind("files/UserHives/ana/NTUSER.DAT", nt));
        Assert.Equal("usrclass_hive", RemoteCollection.ImportKind("files/UserHives/ana/UsrClass.dat", uc));
        Assert.Equal("ntuser_hive", RemoteCollection.ImportKind("files/UserHives/Tasks/NTUSER.DAT", nt));         // a profile named "Tasks"
        Assert.Equal("file", RemoteCollection.ImportKind("files/UserHives/ana/NTUSER.DAT", junk));               // not a hive
        Assert.Equal("file", RemoteCollection.ImportKind("files/UserHives/ana/UsrClass.dat", nt));               // content of the other kind
        Assert.Equal("file", RemoteCollection.ImportKind("files/Other/ana/NTUSER.DAT", nt));                     // outside the folder
        Assert.Equal("file", RemoteCollection.ImportKind("files/UserHives/NTUSER.DAT", nt));                     // no profile folder
        Assert.Equal("file", RemoteCollection.ImportKind("files/UserHives/ana/x/NTUSER.DAT", nt));               // too deep
        Assert.Equal("file", RemoteCollection.ImportKind("files/UserHives/ana/SYSTEM.hive", nt));                // another hive name in the folder
    }

    // ---- artifacts added to approach the live collection: amcache, setupapi, firewall-log (default); srum, recent-items (optional) ----

    private static byte[] SyntheticSrudb() { var b = new byte[4096]; BitConverter.GetBytes(0x89ABCDEFu).CopyTo(b, 4); return b; }   // the ESE header is what the fingerprint reads

    private static byte[] SyntheticSetupApi() => System.Text.Encoding.UTF8.GetBytes(
        "[Device Install Log]\r\n     OS Version = 10.0.19045\r\n>>>  [Device Install (Hardware initiated) - USB\\VID_0781&PID_5567\\4C530001]\r\n" +
        ">>>  Section start 2026/09/01 10:00:00.123\r\n<<<  [Exit status: SUCCESS]\r\n");

    private static byte[] SyntheticFirewall() => System.Text.Encoding.ASCII.GetBytes(
        "#Version: 1.5\r\n#Software: Microsoft Windows Firewall\r\n#Time Format: Local\r\n#Fields: date time action protocol src-ip dst-ip src-port dst-port size tcpflags tcpsyn tcpack tcpwin icmptype icmpcode info path\r\n\r\n" +
        "2026-09-01 10:00:00 ALLOW TCP 10.0.0.5 10.0.0.9 50000 443 0 - 0 0 0 - - - SEND\r\n");

    private static byte[] SyntheticAutoJumpList(byte[] lnk) => JumpListParserTests.BuildCfb(("1", lnk),
        ("DestList", JumpListParserTests.DestList((1, @"C:\Users\u\Downloads\SETUP.EXE", new DateTime(2026, 9, 19, 15, 1, 2, DateTimeKind.Utc), 3, false))));

    private static void Put(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }

    private static bool MakeJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var mk = Process.Start(psi)!;
        mk.StandardOutput.ReadToEnd(); mk.StandardError.ReadToEnd();
        mk.WaitForExit(10000);
        return mk.ExitCode == 0;
    }

    private static readonly string[] NewArtifacts = ["amcache", "setupapi", "firewall-log", "srum", "recent-items"];

    /// <summary>A synthetic station with every new artifact present.</summary>
    private string StationWithNewArtifacts(string name)
    {
        var fake = FakeStation(name, true);
        var win = Path.Combine(fake, "Windows");
        Put(Path.Combine(win, "appcompat", "Programs", "Amcache.hve"), SyntheticNtUser());
        Put(Path.Combine(win, "appcompat", "Programs", "Amcache.hve.LOG1"), [1, 2, 3, 4, 5, 6, 7, 8]);
        Put(Path.Combine(win, "appcompat", "Programs", "Amcache.hve.LOG2"), [8, 7, 6, 5, 4, 3, 2, 1]);
        Put(Path.Combine(win, "appcompat", "Programs", "Amcache.hve.bak"), [9, 9, 9, 9]);                       // not looked for
        Put(Path.Combine(win, "System32", "sru", "SRUDB.dat"), SyntheticSrudb());
        Put(Path.Combine(win, "System32", "sru", "SRU.chk"), [1, 2, 3, 4]);
        Put(Path.Combine(win, "System32", "sru", "SRU00001.log"), [1, 2, 3, 4]);
        Put(Path.Combine(win, "System32", "sru", "SRUtmp.jrs"), [1, 2, 3, 4]);
        Put(Path.Combine(win, "System32", "sru", "notes.txt"), [1, 2, 3, 4]);                                    // not an ESE file of SRUM
        Put(Path.Combine(win, "INF", "setupapi.dev.log"), SyntheticSetupApi());
        Put(Path.Combine(win, "INF", "setupapi.dev.20260101_120000.log"), SyntheticSetupApi());
        Put(Path.Combine(win, "INF", "setupapi.dev.log.bak"), SyntheticSetupApi());                              // not a setupapi.dev*.log name of the two real forms
        Put(Path.Combine(win, "INF", "setupapi.app.log"), SyntheticSetupApi());
        Put(Path.Combine(win, "System32", "LogFiles", "Firewall", "pfirewall.log"), SyntheticFirewall());
        Put(Path.Combine(win, "System32", "LogFiles", "Firewall", "pfirewall.log.old"), SyntheticFirewall());
        Put(Path.Combine(win, "System32", "LogFiles", "Firewall", "other.log"), SyntheticFirewall());
        var lnk = LnkParserTests.BuildLnk(@"C:\Users\u\Downloads\SETUP.EXE", "/x", @"C:\Users\u\Downloads", "ws-01");
        var recent = Path.Combine(fake, "Users", "ana", "AppData", "Roaming", "Microsoft", "Windows", "Recent");
        Put(Path.Combine(recent, "a.lnk"), lnk);
        Put(Path.Combine(recent, "empty.lnk"), []);                                                              // empty: skipped
        Put(Path.Combine(recent, "notes.txt"), [1, 2, 3]);                                                      // not a shortcut
        Put(Path.Combine(recent, "AutomaticDestinations", "f01b4d95cf55d32a.automaticDestinations-ms"), SyntheticAutoJumpList(lnk));
        Put(Path.Combine(recent, "CustomDestinations", "5f7b5f1e01b83767.customDestinations-ms"), NewSourcesRoutingTests.CustomDestinations(lnk));
        var bob = Path.Combine(fake, "Users", "bob", "AppData", "Roaming", "Microsoft", "Windows", "Recent");
        Put(Path.Combine(bob, "b.lnk"), lnk);
        Directory.CreateDirectory(Path.Combine(fake, "Users", "carl"));                                         // a profile without a Recent folder
        return fake;
    }

    private RemotePackageCheck RunNew(string fake, params string[] artifacts)
    {
        var ws = CaseWorkspace.Create(Path.Combine(_root, "case_" + Guid.NewGuid().ToString("N")), new CaseInfo { CaseId = "CASE-REMOTE", Name = "remote", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", artifacts);
        return RemoteCollection.Verify(ws, RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha, false), fake));
    }

    [Fact]
    public void Authorize_accepts_the_new_default_and_optional_artifacts_and_keeps_the_optional_ones_out_of_the_default_list()
    {
        var ws = NewCase();
        foreach (var a in new[] { "evtx:Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational", "evtx:Microsoft-Windows-WinRM/Operational", "amcache", "setupapi", "firewall-log" })
            Assert.Contains(a, RemoteCollection.KnownArtifacts);
        foreach (var a in new[] { "srum", "recent-items", "ps-transcripts" })
        {
            Assert.DoesNotContain(a, RemoteCollection.KnownArtifacts);
            Assert.DoesNotContain(a, RemoteCollection.ArtifactsFor(false));
            Assert.DoesNotContain(a, RemoteCollection.ArtifactsFor(true));
            Assert.Contains(a, RemoteCollection.ArtifactsFor(false, true));
            Assert.Contains(a, RemoteCollection.OptionalArtifacts);
        }
        Assert.Equal([.. RemoteCollection.KnownArtifacts, "srum", "recent-items", "ps-transcripts"], RemoteCollection.ArtifactsFor(false, true));
        Assert.Equal([.. RemoteCollection.KnownArtifacts, "user-hives", "srum", "recent-items", "ps-transcripts"], RemoteCollection.ArtifactsFor(true, true));
        var (req, _) = RemoteCollection.Authorize(ws, "PC01", "op", "j", ["srum", "recent-items", "amcache"]);
        Assert.Equal(["srum", "recent-items", "amcache"], req.Artifacts);
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", "j", ["srum-all"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.Authorize(ws, "PC01", "op", "j", ["recent-items", "hive:SAM"]));
        Assert.Throws<ArgumentException>(() => RemoteCollection.PackageScript(req with { Artifacts = ["srum", "evil"] }, new string('A', 64)));
        // a default package carries the code of the default steps only: no SRUM, no recent-items, no user-hives branch
        var (def, sha) = RemoteCollection.Authorize(ws, "PC01", "op", "j", RemoteCollection.KnownArtifacts);
        var script = RemoteCollection.PackageScript(def, sha);
        Assert.Contains("$a -eq 'amcache'", script); Assert.Contains("$a -eq 'setupapi'", script); Assert.Contains("$a -eq 'firewall-log'", script);
        Assert.DoesNotContain("$a -eq 'srum'", script); Assert.DoesNotContain("$a -eq 'recent-items'", script); Assert.DoesNotContain("$a -eq 'user-hives'", script);
        // a package without any of the steps that read held-open files has no shadow-copy copy at all
        var (rt, sha2) = RemoteCollection.Authorize(ws, "PC01", "op", "j", ["prefetch", "remote-tool-logs"]);
        Assert.DoesNotContain("esentutl", RemoteCollection.PackageScript(rt, sha2), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_new_steps_collect_a_synthetic_station_with_the_live_collectors_rules_and_the_import_types_them_like_the_live_collectors()
    {
        var fake = StationWithNewArtifacts("station-new");
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", NewArtifacts);
        var check = RemoteCollection.Verify(ws, RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha, false), fake));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        Assert.All(check.Manifest!.Steps, st => Assert.True(st.Status == "ok", $"{st.Artifact}: {st.Status} {st.Error}"));
        Assert.Equal(
        [
            "files/Amcache/Amcache.hve", "files/Amcache/Amcache.hve.LOG1", "files/Amcache/Amcache.hve.LOG2",
            "files/Firewall/pfirewall.log", "files/Firewall/pfirewall.log.old",
            "files/RecentItems/ana/AutomaticDestinations/f01b4d95cf55d32a.automaticDestinations-ms", "files/RecentItems/ana/CustomDestinations/5f7b5f1e01b83767.customDestinations-ms", "files/RecentItems/ana/Recent/a.lnk",
            "files/RecentItems/bob/Recent/b.lnk",
            "files/SetupApi/setupapi.dev.20260101_120000.log", "files/SetupApi/setupapi.dev.log",
            "files/Srum/SRU.chk", "files/Srum/SRU00001.log", "files/Srum/SRUDB.dat", "files/Srum/SRUtmp.jrs",
        ], check.Manifest.Files.Select(f => f.Path).OrderBy(x => x, StringComparer.Ordinal).ToList());
        // the firewall step says what it did not look for
        Assert.Contains("calea implicită", check.Manifest.Steps.Single(st => st.Artifact == "firewall-log").Error);

        var items = RemoteCollection.Import(ws, check).Where(i => i.SourceType != "remote-manifest").ToList();
        Assert.Equal(15, items.Count);
        string Kind(string tail) => items.Single(i => i.Notes.Contains(":files/" + tail + ";", StringComparison.Ordinal)).SourceType;
        Assert.Equal("amcache", Kind("Amcache/Amcache.hve"));
        Assert.Equal("amcache_log", Kind("Amcache/Amcache.hve.LOG1"));
        Assert.Equal("amcache_log", Kind("Amcache/Amcache.hve.LOG2"));
        Assert.Equal("srum", Kind("Srum/SRUDB.dat"));
        Assert.Equal(3, items.Count(i => i.SourceType == SrumCollector.SrumLogSourceType));
        Assert.Equal(LogAnalyzer.Dfir.Devices.SetupApiLogParser.SourceTypeName, Kind("SetupApi/setupapi.dev.log"));
        Assert.Equal(LogAnalyzer.Dfir.Devices.SetupApiLogParser.SourceTypeName, Kind("SetupApi/setupapi.dev.20260101_120000.log"));
        Assert.Equal(LogAnalyzer.Dfir.Network.FirewallLogParser.SourceTypeName, Kind("Firewall/pfirewall.log"));
        Assert.Equal(LogAnalyzer.Dfir.Network.FirewallLogParser.SourceTypeName, Kind("Firewall/pfirewall.log.old"));
        Assert.Equal(2, items.Count(i => i.SourceType == "lnk"));
        Assert.Equal("jumplist_auto", Kind("RecentItems/ana/AutomaticDestinations/f01b4d95cf55d32a.automaticDestinations-ms"));
        Assert.Equal(LogAnalyzer.Dfir.FileSystem.CustomJumpListParser.SourceTypeName, Kind("RecentItems/ana/CustomDestinations/5f7b5f1e01b83767.customDestinations-ms"));
        foreach (var f in check.Manifest.Files) Assert.Equal(f.Sha256, items.Single(i => i.Notes.Contains(":" + f.Path + ";", StringComparison.Ordinal)).Sha256, ignoreCase: true);
        // every typed item reaches a parser, except the companions
        var registry = LogAnalyzer.Dfir.Windows.Parsers.WindowsParsers.Registry;
        Assert.All(items.Where(i => i.SourceType is not ("amcache_log" or "srum_log")), i => Assert.NotEmpty(registry.Candidates(i)));
    }

    [Fact]
    public void A_SRUM_log_held_open_by_the_system_does_not_fail_the_step_when_the_database_is_copied()
    {
        // lab SRV01 2026-11-16: SRUDB.dat copied (shadow copy) but two ESE logs in use were not, and the whole step said "failed"
        var fake = StationWithNewArtifacts("station-srum-busy");
        var log = Path.Combine(fake, "Windows", "System32", "sru", "SRU00001.log");
        RemotePackageCheck check;
        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))   // held like the system holds its current ESE log
            check = RunNew(fake, "srum");
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var step = check.Manifest!.Steps.Single();
        Assert.Equal("ok", step.Status);
        Assert.Contains("SRU00001.log", step.Error);
        Assert.Contains("necopiate", step.Error);
        Assert.Contains(check.Manifest.Files, f => f.Path == "files/Srum/SRUDB.dat");
        Assert.DoesNotContain(check.Manifest.Files, f => f.Path.EndsWith("SRU00001.log", StringComparison.Ordinal));
    }

    [Fact]
    public void Recent_folders_without_any_shortcut_say_so_instead_of_a_missing_folder()
    {
        // lab SRV01 2026-11-16: every Recent folder existed but was empty, and the step said "folderul nu exista pe statie"
        var fake = FakeStation("station-recent-empty", true);
        Directory.CreateDirectory(Path.Combine(fake, "Users", "ana", "AppData", "Roaming", "Microsoft", "Windows", "Recent"));
        var step = RunNew(fake, "recent-items").Manifest!.Steps.Single();
        Assert.Equal("absent", step.Status);
        Assert.Contains("nu există fișiere recente", step.Error);
    }

    [Fact]
    public void The_transcript_step_reads_each_folder_and_file_once_refuses_links_on_the_policy_path_reports_every_limit_and_leaves_out_its_own_session()
    {
        var script = RemoteCollection.PackageScript(RemoteCollection.Authorize(NewCase(), "PC01", "op", "j", ["ps-transcripts"]).Item1, new string('A', 64));
        var branch = script[script.IndexOf("$a -eq 'ps-transcripts'", StringComparison.Ordinal)..];
        // each folder once (normalised full path, a folder inside a walked policy folder dropped) and each file once
        Assert.Contains("$seenDirs", branch); Assert.Contains("$walkRoots", branch); Assert.Contains("$seenFiles", branch); Assert.Contains("Get-NormPath", branch);
        // every component of the policy path is checked for a reparse point
        Assert.Contains("Get-LinkOnPath", branch);
        Assert.Contains("director de transcriere printr-o legătură (junction/symlink), necolectat", branch);
        // every limit is reported with its folder
        foreach (var limit in new[] { "limita de adâncime (8 niveluri)", "limita de 2000 de foldere parcurse", "limita de 400 de subfoldere de dată", "limita de 2000 de fișiere pe folder", "profiluri peste limita de 200" })
            Assert.Contains(limit, branch);
        // the own session: this process id and start time, the header read on 20 lines only
        Assert.Contains("[int]$PID", branch); Assert.Contains("GetCurrentProcess().StartTime", branch); Assert.Contains("-lt 20", branch);
        Assert.Contains("transcrierea propriei sesiuni de colectare, exclusă", branch);
        Assert.DoesNotContain("Invoke-Expression", branch);
    }

    [Fact]
    public void The_transcript_step_collects_documents_and_date_folders_once_and_counts_a_link_on_the_way()
    {
        var fake = FakeStation("station-pstr", true);
        var ana = Path.Combine(fake, "Users", "ana", "Documents");
        Put(Path.Combine(ana, "PowerShell_transcript.LAB.aaaa1111.20261118100000.txt"), [1, 2, 3]);
        Put(Path.Combine(ana, "20261118", "PowerShell_transcript.LAB.bbbb2222.20261118110000.txt"), [4, 5, 6]);
        Put(Path.Combine(ana, "notes.txt"), [7]);
        var elsewhere = Path.Combine(_root, "elsewhere-pstr");
        Put(Path.Combine(elsewhere, "PowerShell_transcript.LAB.cccc3333.20261118120000.txt"), [8, 9]);
        Directory.CreateDirectory(Path.Combine(fake, "Users", "bob"));
        Assert.True(MakeJunction(Path.Combine(fake, "Users", "bob", "Documents"), elsewhere), "a junction could not be created; this test needs one");
        try
        {
            var step = RunNew(fake, "ps-transcripts").Manifest!.Steps.Single();
            Assert.Contains("transcrieri copiate: 2", step.Error);
            Assert.Contains("legături neurmate: 1", step.Error);
        }
        finally { Directory.Delete(Path.Combine(fake, "Users", "bob", "Documents")); }
    }

    [Fact]
    public void An_empty_station_gives_absent_for_every_new_step_and_no_file()
    {
        var check = RunNew(FakeStation("station-nothing", true), NewArtifacts);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        Assert.Equal(5, check.Manifest!.Steps.Count);
        Assert.All(check.Manifest.Steps, st => Assert.Equal("absent", st.Status));
        Assert.Empty(check.Manifest.Files);
        // Amcache.hve missing but its logs present: absent too, the logs are not taken alone
        var fake = FakeStation("station-logs-only", true);
        Put(Path.Combine(fake, "Windows", "appcompat", "Programs", "Amcache.hve.LOG1"), [1, 2, 3, 4]);
        var lonely = RunNew(fake, "amcache");
        Assert.Equal("absent", lonely.Manifest!.Steps.Single().Status);
        Assert.Empty(lonely.Manifest.Files);
    }

    [Fact]
    public void A_link_on_the_way_is_refused_and_counted_by_every_new_step()
    {
        var fake = StationWithNewArtifacts("station-links");
        var elsewhere = Path.Combine(_root, "elsewhere-new");
        Put(Path.Combine(elsewhere, "setupapi.dev.log"), SyntheticSetupApi());
        Put(Path.Combine(elsewhere, "SRUDB.dat"), SyntheticSrudb());
        Put(Path.Combine(elsewhere, "pfirewall.log"), SyntheticFirewall());
        Put(Path.Combine(elsewhere, "Amcache.hve"), SyntheticNtUser());
        Put(Path.Combine(elsewhere, "x.customDestinations-ms"), NewSourcesRoutingTests.CustomDestinations(LnkParserTests.BuildLnk(@"C:\a.exe", "", "", "m")));
        // INF, sru, Firewall and Programs are replaced by junctions to a folder outside the station; carl's Recent is a junction
        foreach (var rel in new[] { @"Windows\INF", @"Windows\System32\sru", @"Windows\System32\LogFiles\Firewall", @"Windows\appcompat\Programs" })
            Directory.Delete(Path.Combine(fake, rel), true);
        foreach (var rel in new[] { @"Windows\INF", @"Windows\System32\sru", @"Windows\System32\LogFiles\Firewall", @"Windows\appcompat\Programs" })
            Assert.True(MakeJunction(Path.Combine(fake, rel), elsewhere), "a junction could not be created; this test needs one (review 2026-11-16: no silent pass)");
        var carlRecent = Path.Combine(fake, "Users", "carl", "AppData", "Roaming", "Microsoft", "Windows", "Recent");
        Directory.CreateDirectory(carlRecent);
        Put(Path.Combine(carlRecent, "c.lnk"), LnkParserTests.BuildLnk(@"C:\a.exe", "", "", "m"));
        Assert.True(MakeJunction(Path.Combine(carlRecent, "AutomaticDestinations"), elsewhere), "a junction could not be created; this test needs one");
        try
        {
            var check = RunNew(fake, NewArtifacts);
            Assert.True(check.Ok, string.Join("; ", check.Problems));
            foreach (var art in new[] { "amcache", "srum", "setupapi", "firewall-log" })
            {
                var st = check.Manifest!.Steps.Single(x => x.Artifact == art);
                Assert.NotEqual("ok", st.Status);
                Assert.Contains("legături neurmate: 1", st.Error);
            }
            var recentStep = check.Manifest!.Steps.Single(x => x.Artifact == "recent-items");
            Assert.NotEqual("ok", recentStep.Status);
            Assert.Contains("legături neurmate: 1", recentStep.Error);
            // nothing came from behind a link; carl's own shortcut was taken
            Assert.DoesNotContain(check.Manifest.Files, f => f.Path.StartsWith("files/Amcache/") || f.Path.StartsWith("files/Srum/") || f.Path.StartsWith("files/SetupApi/") || f.Path.StartsWith("files/Firewall/"));
            Assert.Contains(check.Manifest.Files, f => f.Path == "files/RecentItems/carl/Recent/c.lnk");
            Assert.DoesNotContain(check.Manifest.Files, f => f.Path.StartsWith("files/RecentItems/carl/AutomaticDestinations/"));
        }
        finally
        {
            Directory.Delete(Path.Combine(carlRecent, "AutomaticDestinations"));
            foreach (var rel in new[] { @"Windows\INF", @"Windows\System32\sru", @"Windows\System32\LogFiles\Firewall", @"Windows\appcompat\Programs" }) Directory.Delete(Path.Combine(fake, rel));
        }
    }

    [HardLinkFact]
    public void The_amcache_step_refuses_a_hard_linked_hive()
    {
        var fake = FakeStation("station-hl-amcache", true);
        var elsewhere = Path.Combine(_root, "elsewhere-amcache-hl");
        Put(Path.Combine(elsewhere, "other.dat"), SyntheticNtUser());
        Directory.CreateDirectory(Path.Combine(fake, "Windows", "appcompat", "Programs"));
        Assert.True(MakeHardLink(Path.Combine(fake, "Windows", "appcompat", "Programs", "Amcache.hve"), Path.Combine(elsewhere, "other.dat")));
        var check = RunNew(fake, "amcache");
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var st = check.Manifest!.Steps.Single();
        Assert.NotEqual("ok", st.Status);
        Assert.Contains("legături neurmate: 1", st.Error);
        Assert.Empty(check.Manifest.Files);
    }

    [Fact]
    public void Oversized_and_surplus_files_are_counted_not_copied()
    {
        var fake = FakeStation("station-caps", true);
        var recent = Path.Combine(fake, "Users", "ana", "AppData", "Roaming", "Microsoft", "Windows", "Recent");
        Directory.CreateDirectory(recent);
        File.WriteAllBytes(Path.Combine(recent, "ok.lnk"), LnkParserTests.BuildLnk(@"C:\a.exe", "", "", "m"));
        using (var big = new FileStream(Path.Combine(recent, "big.lnk"), FileMode.CreateNew)) big.SetLength(1024 * 1024 + 1);          // over the 1 MiB cap of a shortcut
        var inf = Path.Combine(fake, "Windows", "INF");
        Directory.CreateDirectory(inf);
        File.WriteAllBytes(Path.Combine(inf, "setupapi.dev.log"), SyntheticSetupApi());
        for (int i = 0; i < 22; i++) File.WriteAllBytes(Path.Combine(inf, $"setupapi.dev.2025{(i + 1):00}01_120000.log"), SyntheticSetupApi());
        var check = RunNew(fake, "recent-items", "setupapi");
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var rs = check.Manifest!.Steps.Single(x => x.Artifact == "recent-items");
        Assert.NotEqual("ok", rs.Status);
        Assert.Contains("sărite (peste limita de mărime sau de număr): 1", rs.Error);
        Assert.Equal(["files/RecentItems/ana/Recent/ok.lnk"], check.Manifest.Files.Select(f => f.Path).Where(p => p.StartsWith("files/RecentItems/")).ToList());
        // the live file first, then the newest rotated copies, 20 in all
        var setup = check.Manifest.Files.Where(f => f.Path.StartsWith("files/SetupApi/")).Select(f => f.Path).ToList();
        Assert.Equal(20, setup.Count);
        Assert.Contains("files/SetupApi/setupapi.dev.log", setup);
    }

    [Fact]
    public void Verify_refuses_a_file_in_the_folder_of_a_new_artifact_the_request_did_not_ask_for()
    {
        var fake = FakeStation("station-bind-new");
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["remote-tool-logs"]);
        var pkg = RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha, false), fake);
        Assert.True(RemoteCollection.Verify(ws, pkg).Ok);
        var lnk = LnkParserTests.BuildLnk(@"C:\a.exe", "", "", "m");
        foreach (var (rel, bytes, artifact) in new (string, byte[], string)[]
        {
            ("files/Amcache/Amcache.hve", SyntheticNtUser(), "amcache"), ("files/Srum/SRUDB.dat", SyntheticSrudb(), "srum"), ("files/SetupApi/setupapi.dev.log", SyntheticSetupApi(), "setupapi"),
            ("files/Firewall/pfirewall.log", SyntheticFirewall(), "firewall-log"), ("files/RecentItems/mallory/Recent/a.lnk", lnk, "recent-items"),
        })
        {
            var copy = Path.Combine(_root, "copy_" + artifact);
            Directory.CreateDirectory(copy);
            foreach (var f in Directory.GetFiles(pkg, "*", SearchOption.AllDirectories)) { var d = Path.Combine(copy, Path.GetRelativePath(pkg, f)); Directory.CreateDirectory(Path.GetDirectoryName(d)!); File.Copy(f, d); }
            InjectFile(copy, rel, bytes);
            var bad = RemoteCollection.Verify(ws, copy);
            Assert.False(bad.Ok);
            Assert.Contains(bad.Problems, p => p.Contains(artifact) && p.Contains("nu este în cererea autorizată"));
            Assert.Throws<InvalidOperationException>(() => RemoteCollection.Import(ws, bad));
        }
        // the same injection under a request that carries the artifact is accepted
        var (req2, sha2) = RemoteCollection.Authorize(ws, Environment.MachineName, @"STATIE\operator", "investigație de test", ["remote-tool-logs", "srum", "recent-items", "amcache", "setupapi", "firewall-log"]);
        var pkg2 = RunPackageOnFakeStation(RemoteCollection.PackageScript(req2, sha2, false), fake);
        InjectFile(pkg2, "files/Srum/SRUDB.dat", SyntheticSrudb());
        InjectFile(pkg2, "files/RecentItems/mallory/Recent/a.lnk", lnk);
        Assert.True(RemoteCollection.Verify(ws, pkg2).Ok);
    }

    [Fact]
    public void ImportKind_types_the_new_folders_only_by_exact_path_name_and_content()
    {
        var dir = Path.Combine(_root, "kind-new"); Directory.CreateDirectory(dir);
        string Write(string name, byte[] b) { var p = Path.Combine(dir, name); File.WriteAllBytes(p, b); return p; }
        var hive = Write("Amcache.hve", SyntheticNtUser());
        var srum = Write("SRUDB.dat", SyntheticSrudb());
        var setup = Write("setupapi.dev.log", SyntheticSetupApi());
        var fw = Write("pfirewall.log", SyntheticFirewall());
        var junk = Write("junk.bin", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18]);
        var lnk = Write("a.lnk", LnkParserTests.BuildLnk(@"C:\a.exe", "", "", "m"));
        var auto = Write("x.automaticDestinations-ms", SyntheticAutoJumpList(File.ReadAllBytes(lnk)));
        // wrong content under the right name stays "file"
        Assert.Equal("file", RemoteCollection.ImportKind("files/Amcache/Amcache.hve", junk));
        Assert.Equal("file", RemoteCollection.ImportKind("files/Srum/SRUDB.dat", junk));
        Assert.Equal("file", RemoteCollection.ImportKind("files/SetupApi/setupapi.dev.log", junk));
        Assert.Equal("file", RemoteCollection.ImportKind("files/Firewall/pfirewall.log", junk));
        Assert.Equal("file", RemoteCollection.ImportKind("files/RecentItems/ana/Recent/a.lnk", junk));
        Assert.Equal("file", RemoteCollection.ImportKind("files/RecentItems/ana/AutomaticDestinations/x.automaticDestinations-ms", junk));
        // right content under the wrong name, folder or depth stays "file"
        Assert.Equal("file", RemoteCollection.ImportKind("files/Amcache/SYSTEM.hive", hive));
        Assert.Equal("file", RemoteCollection.ImportKind("files/Amcache/x/Amcache.hve", hive));
        Assert.Equal("file", RemoteCollection.ImportKind("files/Srum/other.dat", srum));
        Assert.Equal("file", RemoteCollection.ImportKind("files/SetupApi/setupapi.app.log", setup));
        Assert.Equal("file", RemoteCollection.ImportKind("files/SetupApi/setupapi.dev.log.bak", setup));
        Assert.Equal("file", RemoteCollection.ImportKind("files/Firewall/other.log", fw));
        Assert.Equal("file", RemoteCollection.ImportKind("files/RecentItems/ana/Other/a.lnk", lnk));
        Assert.Equal("file", RemoteCollection.ImportKind("files/RecentItems/ana/Recent/a.txt", lnk));
        Assert.Equal("file", RemoteCollection.ImportKind("files/RecentItems/Recent/a.lnk", lnk));                         // no profile folder
        Assert.Equal("file", RemoteCollection.ImportKind("files/RecentItems/ana/Recent/x/a.lnk", lnk));                   // too deep
        Assert.Equal("file", RemoteCollection.ImportKind("files/RecentItems/ana/Recent/x.automaticDestinations-ms", auto)); // jump list in the shortcut folder
        Assert.Equal("file", RemoteCollection.ImportKind("files/Other/Amcache.hve", hive));                               // outside the folder
        // a profile named "Tasks": still a shortcut, never task XML
        Assert.Equal("lnk", RemoteCollection.ImportKind("files/RecentItems/Tasks/Recent/a.lnk", lnk));
        Assert.Equal("jumplist_auto", RemoteCollection.ImportKind("files/RecentItems/Tasks/AutomaticDestinations/x.automaticDestinations-ms", auto));
    }

    [Fact]
    public void The_reimport_into_the_case_of_the_run_keeps_the_type_of_every_new_kind_through_the_carried_types()
    {
        var fake = StationWithNewArtifacts("station-reimport");
        var live = NewCase();
        var (req, sha) = RemoteCollection.Authorize(live, Environment.MachineName, @"STATIE\operator", "investigație de test", NewArtifacts);
        var check = RemoteCollection.Verify(live, RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha, false), fake));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var stored = RemoteCollection.Import(live, check).Where(i => i.SourceType != "remote-manifest").ToList();
        var paths = stored.Select(i => live.FullPath(i.StoredPath)).ToList();
        var carried = stored.ToDictionary(i => live.FullPath(i.StoredPath), i => (i.SourceType, i.Notes), StringComparer.OrdinalIgnoreCase);
        var run = CaseWorkspace.Create(Path.Combine(_root, "case-run-new"), new CaseInfo { CaseId = "CASE-run-new", Name = "run", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var kept = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.Import(run, paths, null, carried);
        Assert.Equal(stored.Count, kept.Count);
        for (int i = 0; i < stored.Count; i++) Assert.Equal(stored[i].SourceType, kept[i].SourceType);
        // without the carried types the name alone decides: the text logs and the companions would be plain "file" (why the types travel)
        var bare = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.Import(run, paths.Where(p => Path.GetFileName(p).StartsWith("pfirewall", StringComparison.OrdinalIgnoreCase)).ToList());
        Assert.All(bare, b => Assert.Equal("file", b.SourceType));
    }

    [Fact]
    public void The_srum_step_creates_one_shadow_copy_for_the_whole_set_and_deletes_it_and_a_readable_set_needs_none()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "op", "j", ["srum"]);
        var script = RemoteCollection.PackageScript(req, sha);
        Assert.Contains($"$AllowShadow{Destructive.Cut}Copy = $true", script);
        Assert.Contains($"$AllowShadow{Destructive.Cut}Copy = $false", RemoteCollection.PackageScript(req, sha, false));
        // in the srum step: exactly one creation of a WMI shadow copy, and its deletion (CIM, then vssadmin by ID); the other steps of the package are not counted
        var from = script.IndexOf("$a -eq 'srum'", StringComparison.Ordinal);
        var step = script[from..script.IndexOf("} elseif ($a -eq", from + 10, StringComparison.Ordinal)];
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(step, "-MethodName Create").Count);
        Assert.Contains("Remove-CimInstance", step);
        Assert.Contains($"vss{Destructive.Cut}admin.exe delete sha{Destructive.Cut}dows", step);
        Assert.Contains("Get-VssLeft", step);                              // shadow copies counted before and after
        // the set is copied from the shadow copy as a whole: the direct copies are removed first, the files are listed IN the shadow copy
        Assert.True(step.IndexOf("Remove-Item -LiteralPath $dest -Recurse", StringComparison.Ordinal) < step.IndexOf("-MethodName Create", StringComparison.Ordinal));
        Assert.Contains("(New-Object IO.DirectoryInfo $shRoot).GetFiles()", step);

        // a set that can be read directly never creates a shadow copy, and the note says from which moment it comes
        var fake = StationWithNewArtifacts("station-srum-direct");
        var check = RunNew(fake, "srum");
        var st = check.Manifest!.Steps.Single();
        Assert.Equal("ok", st.Status);
        Assert.Contains("fără copie de umbră", st.Error);
        Assert.Contains("momentului", st.Error);
    }

    [Fact]
    public void The_srum_set_stays_together_with_its_original_names_in_the_live_case_and_in_the_case_of_the_run()
    {
        var fake = StationWithNewArtifacts("station-srum-set");
        var live = NewCase();
        var (req, sha) = RemoteCollection.Authorize(live, Environment.MachineName, @"STATIE\operator", "investigație de test", ["srum"]);
        var check = RemoteCollection.Verify(live, RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha, false), fake));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        var expected = new[] { "SRU.chk", "SRU00001.log", "SRUDB.dat", "SRUtmp.jrs" };
        var stored = RemoteCollection.Import(live, check).Where(i => i.SourceType != "remote-manifest").ToList();
        Assert.Equal(expected, stored.Select(i => Path.GetFileName(i.StoredPath)).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var liveDir = Path.GetDirectoryName(live.FullPath(stored[0].StoredPath))!;
        Assert.All(stored, i => Assert.Equal(liveDir, Path.GetDirectoryName(live.FullPath(i.StoredPath))));
        Assert.Matches(@"SRUM_[0-9a-f]{32}$", liveDir);
        var key = liveDir[^32..];
        Assert.All(stored, i => Assert.Contains(RemoteCollection.SrumSetKeyPrefix + key, i.Notes));
        Assert.Equal(1, stored.Count(i => i.SourceType == "srum"));
        Assert.Equal(3, stored.Count(i => i.SourceType == SrumCollector.SrumLogSourceType));

        var run = CaseWorkspace.Create(Path.Combine(_root, "case-run-srum"), new CaseInfo { CaseId = "CASE-run-srum", Name = "run", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var paths = stored.Select(i => live.FullPath(i.StoredPath)).ToList();
        var carried = stored.ToDictionary(i => live.FullPath(i.StoredPath), i => (i.SourceType, i.Notes), StringComparer.OrdinalIgnoreCase);
        var kept = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.Import(run, paths, null, carried);
        Assert.Equal(expected, kept.Select(i => Path.GetFileName(i.StoredPath)).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var runDir = Path.GetDirectoryName(run.FullPath(kept[0].StoredPath))!;
        Assert.All(kept, i => Assert.Equal(runDir, Path.GetDirectoryName(run.FullPath(i.StoredPath))));
        Assert.NotEqual(liveDir, runDir);
        Assert.Equal(stored.Select(i => i.SourceType).Order().ToArray(), kept.Select(i => i.SourceType).Order().ToArray());
        // what the parser is given: the path of the database, with the checked logs beside it (the real recovery needs a real ESE database; here the placement and the selection)
        var db = kept.Single(i => i.SourceType == "srum");
        Assert.Equal("SRUDB.dat", Path.GetFileName(run.FullPath(db.StoredPath)));
        var (logs, problem) = LogAnalyzer.Dfir.Windows.Parsers.SrumNetworkParser.VerifiedSrumLogs(runDir, default);
        Assert.Equal("", problem);
        Assert.Equal(["SRU.chk", "SRU00001.log", "SRUtmp.jrs"], logs.Select(l => Path.GetFileName(l.Path)).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.All(logs, l => Assert.Equal(runDir, Path.GetDirectoryName(l.Path)));
        // a set read directly is of one moment: no "different moments" note in either case
        Assert.All(stored, i => Assert.DoesNotContain(RemoteCollection.SrumMixedMomentNote, i.Notes));
        Assert.All(kept, i => Assert.DoesNotContain(RemoteCollection.SrumMixedMomentNote, i.Notes));
    }

    [Fact]
    public void A_srum_set_of_different_moments_says_so_in_the_notes_of_every_file_and_in_the_recovery_gap()
    {
        // residual of planner #4 item 3: the step-3 fact ("the logs are of another moment") was only in the manifest step note
        var fake = StationWithNewArtifacts("station-srum-mixed");
        var live = NewCase();
        var (req, sha) = RemoteCollection.Authorize(live, Environment.MachineName, @"STATIE\operator", "investigație de test", ["srum"]);
        var log = Path.Combine(fake, "Windows", "System32", "sru", "SRU00001.log");
        RemotePackageCheck check;
        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))   // a log held by the system and no shadow copy allowed: step 3
            check = RemoteCollection.Verify(live, RunPackageOnFakeStation(RemoteCollection.PackageScript(req, sha, false), fake));
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        Assert.Contains("din alt moment", check.Manifest!.Steps.Single().Error);
        var stored = RemoteCollection.Import(live, check).Where(i => i.SourceType != "remote-manifest").ToList();
        Assert.NotEmpty(stored);
        Assert.All(stored, i => Assert.Contains(RemoteCollection.SrumMixedMomentNote, i.Notes));
        // the set key stays the last segment, so the run still keeps the set together, and the fact travels with it
        var key = Path.GetDirectoryName(live.FullPath(stored[0].StoredPath))![^32..];
        Assert.All(stored, i => Assert.Equal(key, LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.SrumSetKey(i.Notes)));
        var run = CaseWorkspace.Create(Path.Combine(_root, "case-run-srum-mixed"), new CaseInfo { CaseId = "CASE-run-srum-mixed", Name = "run", CreatedAtUtc = DateTimeOffset.UtcNow, Scope = TestScopes.Valid() });
        var carried = stored.ToDictionary(i => live.FullPath(i.StoredPath), i => (i.SourceType, i.Notes), StringComparer.OrdinalIgnoreCase);
        var kept = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.Import(run, carried.Keys.ToList(), null, carried);
        Assert.Single(kept.Select(i => Path.GetDirectoryName(run.FullPath(i.StoredPath))).Distinct());
        Assert.All(kept, i => Assert.Contains(RemoteCollection.SrumMixedMomentNote, i.Notes));

        // the parser's recovery gap: the fact and a stronger likelihood only for such a set
        var mixedGap = LogAnalyzer.Dfir.Windows.Parsers.SrumNetworkParser.RecoveryGap("recuperată", kept.Single(i => i.SourceType == "srum").Notes);
        Assert.Contains("NU sunt din același moment", mixedGap.Reason);
        var oneMoment = LogAnalyzer.Dfir.Windows.Parsers.SrumNetworkParser.RecoveryGap("recuperată", "H:files/Srum/SRUDB.dat; cererea R");
        Assert.DoesNotContain("același moment", oneMoment.Reason);
        Assert.NotEqual(mixedGap, oneMoment);
    }

    [Fact]
    public void Only_one_ok_srum_step_with_the_step_1_or_step_2_sentence_counts_as_one_moment()
    {
        RemoteStep S(string status, string note, string art = "srum") => new(art, "t0", "t1", status, note);
        var direct = RemoteCollection.SrumDirectNotePrefix + ", în jurul momentului 2026-11-17T08:00:00Z";
        var shadow = RemoteCollection.SrumShadowNotePrefix + ", creată la t și ștearsă la final";
        Assert.Null(RemoteCollection.SrumSetMomentNote([S("ok", direct)]));
        Assert.Null(RemoteCollection.SrumSetMomentNote([S("ok", shadow), S("ok", "", "amcache")]));
        Assert.Equal(RemoteCollection.SrumMixedMomentNote, RemoteCollection.SrumSetMomentNote([S("ok", RemoteCollection.SrumMixedNotePrefix + " (x); ... din alt moment")]));
        // review LOW: a failed or unknown step is not proven to be a mix, only not proven to be one moment
        Assert.Equal(RemoteCollection.SrumUnprovenMomentNote, RemoteCollection.SrumSetMomentNote([S("failed", direct)]));
        Assert.Equal(RemoteCollection.SrumUnprovenMomentNote, RemoteCollection.SrumSetMomentNote([S("failed", RemoteCollection.SrumMixedNotePrefix)]));
        Assert.Equal(RemoteCollection.SrumUnprovenMomentNote, RemoteCollection.SrumSetMomentNote([S("ok", direct), S("ok", direct)]));
        Assert.Equal(RemoteCollection.SrumUnprovenMomentNote, RemoteCollection.SrumSetMomentNote([]));
        Assert.Equal(RemoteCollection.SrumUnprovenMomentNote, RemoteCollection.SrumSetMomentNote([S("ok", null!)]));
        Assert.Equal(RemoteCollection.SrumUnprovenMomentNote, RemoteCollection.SrumSetMomentNote([S("ok", "x " + direct)]));
        var unproven = LogAnalyzer.Dfir.Windows.Parsers.SrumNetworkParser.RecoveryGap("recuperată", "H:p; " + RemoteCollection.SrumUnprovenMomentNote + "; set SRUM: k");
        Assert.Contains("nu este dovedit", unproven.Reason);
        Assert.DoesNotContain("NU sunt din același moment", unproven.Reason);
        // the two sentences are the ones the package script writes
        var (req, sha) = RemoteCollection.Authorize(NewCase(), "PC01", "op", "j", ["srum"]);
        var script = RemoteCollection.PackageScript(req, sha);
        Assert.Contains("\"" + RemoteCollection.SrumDirectNotePrefix + ", în jurul momentului $momentUtc\"", script);
        Assert.Contains("\"" + RemoteCollection.SrumShadowNotePrefix + ", creată la $momentUtc", script);
        Assert.Contains("\"" + RemoteCollection.SrumMixedNotePrefix + " ($shadowErr)", script);
    }
}
