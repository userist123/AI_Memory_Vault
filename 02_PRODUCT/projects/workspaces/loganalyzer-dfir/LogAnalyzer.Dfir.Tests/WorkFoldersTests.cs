using System.Diagnostics;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security audit #14: no plaintext copy of evidence in %TEMP%. Parsers working on case evidence put their working copies in &lt;case&gt;\Work
/// (removed after success and after an exception); a repaired EVTX is kept in &lt;case&gt;\Derived, hashed and recorded in custody. Outside a case the
/// per-user work root is used and cleaned.
/// </summary>
public sealed class WorkFoldersTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_work_" + Guid.NewGuid().ToString("N"));

    public WorkFoldersTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private CaseWorkspace Case() => InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "work", TestScopes.Valid());

    /// <summary>Records the working folders that exist while the parser is emitting events (i.e. while its copy is open).</summary>
    private sealed class SpySink(Func<string[]> look) : IEventSink
    {
        public readonly List<TimelineEvent> Events = [];
        public string[]? SeenDuringParse;
        public void Add(TimelineEvent e) { SeenDuringParse ??= look(); Events.Add(e); }
    }

    private string History(string folder)
    {
        Directory.CreateDirectory(folder);
        var p = Path.Combine(folder, "History");
        using (var c = new SqliteConnection($"Data Source={p};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE urls(id INTEGER PRIMARY KEY, url TEXT, title TEXT, visit_count INTEGER, typed_count INTEGER, last_visit_time INTEGER, hidden INTEGER);
                CREATE TABLE visits(id INTEGER PRIMARY KEY, url INTEGER, visit_time INTEGER, from_visit INTEGER, transition INTEGER, visit_duration INTEGER);
                INSERT INTO urls VALUES (1, 'https://example.invalid/', 'x', 1, 0, 13370000000000000, 0);
                INSERT INTO visits VALUES (10, 1, 13370000000000000, 0, 805306368, 0);
                """;
            cmd.ExecuteNonQuery();
        }
        return p;
    }

    [Fact]
    public void Work_directory_for_case_evidence_is_inside_the_case_and_outside_it_is_the_user_work_root()
    {
        var ws = Case();
        var inCase = Path.Combine(ws.Root, "Raw", "src", "x.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(inCase)!);
        File.WriteAllText(inCase, "x");
        var w = WorkFolders.NewWorkDirectory(inCase, "t");
        Assert.StartsWith(Path.Combine(ws.Root, WorkFolders.CaseWorkFolder) + Path.DirectorySeparatorChar, w);
        Assert.True(WorkFolders.TryDelete(w));

        var userRoot = Path.Combine(_dir, "userwork");
        using (WorkFolders.UseUserWorkRoot(userRoot))
        {
            var o = WorkFolders.NewWorkDirectory(Path.Combine(_dir, "loose.bin"), "t");
            Assert.StartsWith(userRoot + Path.DirectorySeparatorChar, o);
            Assert.True(WorkFolders.TryDelete(o));
        }
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\') + @"\LogAnalyzer", w, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Chromium_history_in_a_case_is_copied_into_the_case_work_folder_and_removed_after_success()
    {
        var ws = Case();
        var ev = Assert.Single(InvestigationPipeline.Import(ws, [History(Path.Combine(_dir, "in"))]));
        var work = Path.Combine(ws.Root, WorkFolders.CaseWorkFolder);
        var sink = new SpySink(() => Directory.Exists(work) ? Directory.GetDirectories(work) : []);

        var r = new BrowserHistoryParser().Parse(ev, ws.FullPath(ev.StoredPath), sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        var during = Assert.Single(sink.SeenDuringParse!);
        Assert.StartsWith("sqlite_", Path.GetFileName(during));
        Assert.Empty(Directory.GetDirectories(work));
    }

    [Fact]
    public void Chromium_history_in_a_case_leaves_nothing_after_an_exception()
    {
        var ws = Case();
        var bad = Path.Combine(_dir, "bad", "History");
        Directory.CreateDirectory(Path.GetDirectoryName(bad)!);
        var bytes = new byte[8192];
        System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(bytes, 0);
        new Random(1).NextBytes(bytes.AsSpan(100));
        File.WriteAllBytes(bad, bytes);
        var ev = Assert.Single(InvestigationPipeline.Import(ws, [bad]));

        var r = new BrowserHistoryParser().Parse(ev, ws.FullPath(ev.StoredPath), new ListSink(), default);

        Assert.Equal(EvidenceStatus.Failed, r.Status);
        var work = Path.Combine(ws.Root, WorkFolders.CaseWorkFolder);
        Assert.True(!Directory.Exists(work) || Directory.GetFileSystemEntries(work).Length == 0);
    }

    [Fact]
    public void Outside_a_case_the_user_work_root_is_used_and_cleaned_after_success_and_exception()
    {
        var userRoot = Path.Combine(_dir, "userwork");
        using var _ = WorkFolders.UseUserWorkRoot(userRoot);
        var item = new EvidenceItem { EvidenceId = "EV-H", CaseId = "C", Source = "chromium_history", SourceType = "chromium_history", StoredPath = "History" };
        var good = History(Path.Combine(_dir, "loose"));
        var sink = new SpySink(() => Directory.Exists(userRoot) ? Directory.GetDirectories(userRoot) : []);
        Assert.Equal(EvidenceStatus.Success, new BrowserHistoryParser().Parse(item, good, sink, default).Status);
        Assert.Single(sink.SeenDuringParse!);
        Assert.Empty(Directory.GetFileSystemEntries(userRoot));

        var bad = Path.Combine(_dir, "loose2", "History");
        Directory.CreateDirectory(Path.GetDirectoryName(bad)!);
        File.WriteAllText(bad, "SQLite format 3\0 garbage garbage garbage");
        Assert.Equal(EvidenceStatus.Failed, new BrowserHistoryParser().Parse(item, bad, new ListSink(), default).Status);
        Assert.Empty(Directory.GetFileSystemEntries(userRoot));
    }

    private string CorruptEvtx()
    {
        var clean = Path.Combine(_dir, "system.evtx");
        using (var p = Process.Start(new ProcessStartInfo("wevtutil.exe", $"epl System \"{clean}\" /ow:true") { UseShellExecute = false, CreateNoWindow = true })!)
            p.WaitForExit(60_000);
        Assert.True(File.Exists(clean));
        Assert.True(new FileInfo(clean).Length > 4096 + 2 * 65536, "System log too small for the test");
        var corrupt = Path.Combine(_dir, "in_evtx", "system_corrupt.evtx");
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
        var b = File.ReadAllBytes(clean);
        for (int i = 0; i < 400; i++) b[4096 + 65536 + 512 + 3000 + i] = 0xCC;
        File.WriteAllBytes(corrupt, b);
        return corrupt;
    }

    [Fact]
    public void Repaired_evtx_of_case_evidence_is_derived_evidence_in_the_case_hashed_in_custody()
    {
        var ws = Case();
        var ev = Assert.Single(InvestigationPipeline.Import(ws, [CorruptEvtx()]));

        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        var derivedDir = Path.Combine(ws.Root, WorkFolders.CaseDerivedFolder, "evtx_repair");
        var repaired = Assert.Single(Directory.GetFiles(derivedDir));
        Assert.StartsWith(ev.EvidenceId + "_", Path.GetFileName(repaired));
        var rel = Path.GetRelativePath(ws.Root, repaired).Replace('\\', '/');
        var custody = File.ReadAllLines(ws.CustodyJsonlPath).Where(l => l.Contains("output.written") && l.Contains(rel)).ToList();
        Assert.NotEmpty(custody);
        Assert.Contains(Hashing.Sha256File(repaired), custody[^1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ev.EvidenceId, custody[^1]);
        var gap = Assert.Single(r.Gaps, g => g.Reason.Contains("RecordID"));
        Assert.Contains(rel, gap.Notes.Replace('\\', '/'));
        Assert.DoesNotContain(Path.GetTempPath(), gap.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repaired_evtx_outside_a_case_is_removed_after_parsing()
    {
        var userRoot = Path.Combine(_dir, "userwork_evtx");
        using var _ = WorkFolders.UseUserWorkRoot(userRoot);
        var corrupt = CorruptEvtx();
        var item = new EvidenceItem { EvidenceId = "EV-T", CaseId = "C", Source = "System", SourceType = "evtx", StoredPath = corrupt };
        var res = new EvtxParser().Parse(item, corrupt, new ListSink(), CancellationToken.None);
        Assert.Equal(EvidenceStatus.Partial, res.Status);
        Assert.True(res.Records > 0);
        Assert.True(!Directory.Exists(userRoot) || Directory.GetFileSystemEntries(userRoot).Length == 0);
    }
}
