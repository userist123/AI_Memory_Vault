using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>F4: verifiable case archive — manifest with every file's SHA-256, the manifest's own SHA-256, README with manual commands, verification on import.</summary>
public sealed class CaseArchiveTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private (CaseWorkspace Ws, CaseArchiveResult Archive) Make()
    {
        var ws = _k.Case(_k.File("in/a.bin", "alpha"), _k.File("in/b.bin", "beta"));
        var zip = Path.Combine(_k.Dir, "out", "caz.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        return (ws, CaseArchive.Export(ws, zip));
    }

    [Fact]
    public void Export_lists_every_case_file_with_its_SHA256_and_verifies()
    {
        var (ws, a) = Make();
        var check = CaseArchive.Verify(a.ZipPath);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        Assert.Equal(a.ManifestSha256, check.ManifestSha256);
        var caseFiles = Directory.EnumerateFiles(ws.Root, "*", SearchOption.AllDirectories).Count();
        Assert.True(a.FileCount >= caseFiles - 1);   // the audit line written after the export is not inside the archive
        Assert.Contains(check.Manifest!.Files, f => f.Path.StartsWith("case/Raw/", StringComparison.Ordinal));
        Assert.Contains("case.archive_done", File.ReadAllText(ws.AppAuditLogPath));
        Assert.Contains(a.ZipSha256, File.ReadAllText(CaseArchive.SidecarPath(a.ZipPath)));
    }

    [Fact]
    public void An_archive_waits_for_another_instance_writing_a_shared_case_log_and_never_zips_half_a_line()
    {
        // planner #7, 2026-11-17: the archive opened every file with FileShare.ReadWrite|Delete and could zip (and sign) a line another instance was writing
        var ws = _k.Case(_k.File("in/a.bin", "alpha"));
        var log = ws.AppAuditLogPath;
        var writer = new FileStream(log, FileMode.Open, FileAccess.Write, FileShare.Read);   // what SharedAppend holds while it appends
        writer.Seek(0, SeekOrigin.End);
        writer.Write(System.Text.Encoding.UTF8.GetBytes("2026-11-17T00:00:00.000Z\tx\tpartial"));
        writer.Flush();
        var finish = Task.Run(() =>
        {
            Thread.Sleep(400);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("-line-complete\n"));
            writer.Dispose();
        });
        var zip = Path.Combine(_k.Dir, "out", "caz.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        var a = CaseArchive.Export(ws.Root, ws.Info.CaseId, zip);
        finish.Wait();
        using var z = ZipFile.OpenRead(a.ZipPath);
        using var r = new StreamReader(z.GetEntry("case/Logs/LogAnalyzer_Audit.log")!.Open());
        var archived = r.ReadToEnd();
        Assert.EndsWith("partial-line-complete\n", archived);
        Assert.True(CaseArchive.Verify(a.ZipPath).Ok);
    }

    [Fact]
    public void A_sealed_log_archived_while_another_instance_appends_keeps_its_head_from_the_same_moment()
    {
        // review 2026-11-17: the log and its .head were zipped one after the other; an append + head move in between gave a head past the log ("rescris")
        var ws = _k.Case(_k.File("in/a.bin", "alpha"));
        var log = Path.Combine(ws.Root, "Logs", "sealed_test.jsonl");
        var key = RandomNumberGenerator.GetBytes(32);
        HashChain.RegisterHeadSeal(log, key);
        var chain = new HashChain(log);
        chain.Append(new { Event = "first" });
        var done = false;
        var writer = Task.Run(() => { for (int i = 0; i < 150; i++) new HashChain(log).Append(new { Event = "e", I = i }); Volatile.Write(ref done, true); });
        int exports = 0;
        while (!Volatile.Read(ref done) || exports < 2)
        {
            var zip = Path.Combine(_k.Dir, "out", $"caz{exports}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
            var a = CaseArchive.Export(ws.Root, ws.Info.CaseId, zip);
            var dest = Path.Combine(_k.Dir, $"x{exports}");
            ZipFile.ExtractToDirectory(a.ZipPath, dest);
            var copy = Path.Combine(dest, "case", "Logs", "sealed_test.jsonl");
            HashChain.RegisterHeadSeal(copy, key);
            var v = new HashChain(copy).Verify();
            Assert.True(v.Status != ChainStatus.Broken, v.Message);
            exports++;
        }
        writer.Wait();
    }

    [Fact]
    public void A_file_time_the_zip_format_cannot_hold_is_clamped_instead_of_failing_the_export()
    {
        // a file removed after the listing reads as 1601-01-01 (File.GetLastWriteTime of a missing file)
        Assert.Equal(new DateTime(1980, 1, 1), CaseArchive.ZipTime(File.GetLastWriteTime(Path.Combine(_k.Dir, "no-such-file"))).Date);
        Assert.Equal(2107, CaseArchive.ZipTime(new DateTime(2200, 1, 1)).Year);
        var t = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Local);
        Assert.Equal(t, CaseArchive.ZipTime(t));
        Assert.True(CaseArchive.IsTransientHelper(@"C:\c\Evidence\evidence_index.jsonl.tmp"));
        Assert.True(CaseArchive.IsTransientHelper(@"C:\c\Logs\workflow.jsonl.LOCK"));
        Assert.False(CaseArchive.IsTransientHelper(@"C:\c\Logs\audit_chain.jsonl"));
    }

    [Theory]
    [InlineData("Logs/LogAnalyzer_Audit.log", true)]
    [InlineData("Logs/audit_chain.jsonl", true)]
    [InlineData("Logs/chain_of_custody.jsonl.head", true)]
    [InlineData("Evidence/evidence_index.jsonl", true)]
    [InlineData("EVIDENCE/Evidence_Index.jsonl", true)]
    [InlineData("media_live.jsonl", true)]
    [InlineData("Evidence/evidence_index.jsonl.tmp", false)]
    [InlineData("Logs/workflow.jsonl.lock", false)]
    [InlineData("Logs/audit_chain.jsonl.head.tmp", false)]
    [InlineData("Raw/media_live.jsonl", false)]
    [InlineData("Evidence/other.jsonl", false)]
    [InlineData("Raw/Security.evtx", false)]
    [InlineData("Logs/sub/x.log", false)]
    public void Only_the_shared_case_logs_are_read_with_writers_excluded(string rel, bool shared)
    {
        var root = Path.Combine(_k.Dir, "caseroot");
        Assert.Equal(shared, CaseArchive.IsSharedCaseLog(root, Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void Manual_verification_without_the_application_matches_the_manifest()
    {
        var (_, a) = Make();
        var dest = Path.Combine(_k.Dir, "verificare");
        ZipFile.ExtractToDirectory(a.ZipPath, dest);
        // what Get-FileHash / certutil compute: plain SHA-256 of the extracted bytes
        var manifestBytes = File.ReadAllBytes(Path.Combine(dest, "manifest.json"));
        Assert.Equal(a.ManifestSha256, Convert.ToHexString(SHA256.HashData(manifestBytes)));
        Assert.StartsWith(a.ManifestSha256, File.ReadAllText(Path.Combine(dest, "manifest.sha256")));
        var m = JsonSerializer.Deserialize<CaseArchiveManifest>(manifestBytes)!;
        foreach (var f in m.Files)
            Assert.Equal(f.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dest, f.Path.Replace('/', Path.DirectorySeparatorChar))))));
        var readme = File.ReadAllText(Path.Combine(dest, "README.txt"));
        Assert.Contains("Get-FileHash", readme);
        Assert.Contains("certutil -hashfile manifest.json SHA256", readme);
        Assert.Contains(a.ManifestSha256, readme);
        Assert.Contains("ConvertFrom-Json", readme);
    }

    private static void Rewrite(string zip, string entry, string content)
    {
        using var z = ZipFile.Open(zip, ZipArchiveMode.Update);
        z.GetEntry(entry)!.Delete();
        using var w = new StreamWriter(z.CreateEntry(entry).Open());
        w.Write(content);
    }

    [Fact]
    public void A_changed_file_inside_the_archive_is_reported_and_import_refuses_it()
    {
        var (_, a) = Make();
        var m = CaseArchive.Verify(a.ZipPath).Manifest!;
        var target = m.Files.First(f => f.Path.StartsWith("case/Raw/", StringComparison.Ordinal)).Path;
        Rewrite(a.ZipPath, target, "tampered");
        var check = CaseArchive.Verify(a.ZipPath, a.ManifestSha256);
        Assert.False(check.Ok);
        Assert.Contains(check.Problems, p => p == "MODIFICAT: " + target);
        var (ws, c2) = CaseArchive.Import(a.ZipPath, Path.Combine(_k.Dir, "imported"));
        Assert.Null(ws);
        Assert.False(c2.Ok);
        Assert.False(Directory.Exists(Path.Combine(_k.Dir, "imported")) && Directory.EnumerateFileSystemEntries(Path.Combine(_k.Dir, "imported")).Any());
    }

    [Fact]
    public void An_added_file_or_a_replaced_manifest_is_reported()
    {
        var (_, a) = Make();
        using (var z = ZipFile.Open(a.ZipPath, ZipArchiveMode.Update))
        using (var w = new StreamWriter(z.CreateEntry("case/Raw/planted.txt").Open())) w.Write("x");
        Assert.Contains(CaseArchive.Verify(a.ZipPath, a.ManifestSha256).Problems, p => p.Contains("ÎN PLUS") && p.Contains("planted.txt"));

        var (_, b) = Make();
        Rewrite(b.ZipPath, "manifest.json", "{\"schema\":\"" + CaseArchive.Schema + "\",\"case_id\":\"X\",\"files\":[]}");
        var check = CaseArchive.Verify(b.ZipPath, b.ManifestSha256);
        Assert.False(check.Ok);
        Assert.Contains(check.Problems, p => p.Contains("manifest.sha256"));
    }

    [Fact]
    public void A_verified_archive_imports_as_a_case_that_opens_and_rechecks_valid()
    {
        var (ws, a) = Make();
        var (imported, check) = CaseArchive.Import(a.ZipPath, Path.Combine(_k.Dir, "imported"), a.ManifestSha256);
        Assert.True(check.Ok);
        Assert.NotNull(imported);
        Assert.Equal(ws.Info.CaseId, imported!.Info.CaseId);
        Assert.Equal(ws.LoadEvidence().Count, imported.LoadEvidence().Count);
        Assert.Equal(0, imported.LastRecheck!.ModifiedCount + imported.LastRecheck.MissingCount);
        Assert.Contains("case.imported_archive", File.ReadAllText(imported.AppAuditLogPath));
    }

    [Fact]
    public void Cli_verify_archive_prints_VERIFIED_or_NOT_VERIFIED()
    {
        var (_, a) = Make();
        var o = new StringWriter();
        Assert.Equal(0, ReportCli.VerifyArchive(["verify-archive", a.ZipPath, a.ManifestSha256], o));
        Assert.StartsWith("VERIFIED", o.ToString());
        Rewrite(a.ZipPath, "manifest.sha256", "00  manifest.json");
        var bad = new StringWriter();
        Assert.Equal(2, ReportCli.VerifyArchive(["verify-archive", a.ZipPath], bad));
        Assert.StartsWith("NOT VERIFIED", bad.ToString());
    }

    [Fact]
    public void The_archive_cannot_be_written_inside_the_case_it_archives()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        Assert.Throws<ArgumentException>(() => CaseArchive.Export(ws, Path.Combine(ws.Root, "self.zip")));
    }
}
