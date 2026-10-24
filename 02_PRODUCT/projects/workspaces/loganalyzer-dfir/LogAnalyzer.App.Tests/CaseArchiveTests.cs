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
