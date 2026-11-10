using System.Diagnostics;
using System.Text.Json;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
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
    private string IndexPackage(RemoteCollectionRequest req, string requestSha, string stepStatus, bool withWal, string name)
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
    public void The_search_index_step_takes_database_and_journal_from_one_shadow_copy_and_always_deletes_it()
    {
        var ws = NewCase();
        var (req, sha) = RemoteCollection.Authorize(ws, "PC01", "operator", "test", ["search-index", "notepad-tabs"]);
        var script = RemoteCollection.PackageScript(req, sha);
        // not run here: it would create a shadow copy on the machine that runs the tests; the run is checked in the lab
        Assert.Contains("Win32_ShadowCopy", script);
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
}
