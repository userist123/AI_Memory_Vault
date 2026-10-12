using System.IO;
using System.IO.Compression;
using System.Text;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Second independent review: duplicate archive entries, honest wording, strict re-hash, case id sanitising, key passphrase confirmation, one expected hash. Synthetic data only.</summary>
public sealed class ReviewFix2ArchiveTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private (ReportSigningSettings Settings, string Pub) Signing()
    {
        var (priv, pub) = ReportSignature.GenerateKey();
        var keyFile = Path.Combine(_k.Dir, "keys", Guid.NewGuid().ToString("N") + ".p8");
        Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
        File.WriteAllText(keyFile, priv);
        return (new ReportSigningSettings { PrivateKeyPath = keyFile, Signer = "Laborator Test", TrustedPublicKeys = [pub] }, pub);
    }

    private (CaseWorkspace Ws, string Zip, CaseArchiveResult Result) Make(ReportSigningSettings? signing = null)
    {
        var ws = _k.Case(_k.File("in/a.bin", "alpha"), _k.File("in/b.bin", "beta"));
        var zip = Path.Combine(_k.Dir, "out", Guid.NewGuid().ToString("N")[..6] + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        return (ws, zip, CaseArchive.Export(ws, zip, signing: signing));
    }

    // ---- duplicate entries ----

    [Theory]
    [InlineData("same")]
    [InlineData("upper")]
    [InlineData("backslash")]
    public void A_signed_archive_with_an_appended_duplicate_entry_is_refused_at_verify_and_import(string variant)
    {
        var (settings, pub) = Signing();
        var (_, zip, _) = Make(settings);
        string original;
        using (var z = ZipFile.OpenRead(zip)) original = z.Entries.First(e => e.FullName.StartsWith("case/Raw/", StringComparison.Ordinal) && !e.FullName.EndsWith('/')).FullName;
        var dup = variant switch { "upper" => original.ToUpperInvariant(), "backslash" => original.Replace('/', '\\'), _ => original };
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            using var w = new StreamWriter(z.CreateEntry(dup).Open(), new UTF8Encoding(false));
            w.Write("planted");
        }
        File.Delete(zip + ".sha256");
        var check = CaseArchive.Verify(zip, null, [pub]);
        Assert.False(check.Ok);
        Assert.Contains(check.Problems, p => p.Contains("dublat", StringComparison.OrdinalIgnoreCase));
        var (ws, c2) = CaseArchive.Import(zip, Path.Combine(_k.Dir, "imported"), null, [pub]);
        Assert.Null(ws);
        Assert.False(c2.Ok);
    }

    // ---- wording ----

    [Fact]
    public void Only_a_verified_archive_says_verified()
    {
        var (_, zip, res) = Make();
        var none = CaseArchive.Verify(zip);
        Assert.True(none.Ok);
        Assert.Contains("Arhivă integră, dar neautentificată (nimeni nu garantează pentru ea)", none.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Arhivă verificată", none.Summary, StringComparison.Ordinal);
        var good = CaseArchive.Verify(zip, res.ManifestSha256);
        Assert.StartsWith("Arhivă verificată", good.Summary, StringComparison.Ordinal);
        var (ws, imported) = CaseArchive.Import(zip, Path.Combine(_k.Dir, "imp"), importAsUnverified: true);
        Assert.NotNull(ws);
        Assert.Contains("Importată ca NEVERIFICATĂ", imported.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Arhivă verificată", imported.Summary, StringComparison.Ordinal);
    }

    // ---- strict re-hash ----

    [Fact]
    public void A_file_that_disappears_before_the_re_hash_fails_the_import()
    {
        var (_, zip, res) = Make();
        var dest = Path.Combine(_k.Dir, "imported");
        var (ws, check) = CaseArchive.ImportCore(zip, dest, res.ManifestSha256, null, false, folder =>
        {
            var victim = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).First(f => f.Contains("Raw", StringComparison.Ordinal));
            File.SetAttributes(victim, FileAttributes.Normal);
            File.Delete(victim);
        });
        Assert.Null(ws);
        Assert.False(check.Ok);
        Assert.Contains(check.Problems, p => p.StartsWith("LIPSĂ după extragere", StringComparison.Ordinal));
        Assert.False(Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any());
    }

    // ---- case id ----

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..\\evil")]
    public void A_case_id_that_is_a_dot_name_or_has_a_path_separator_is_refused(string caseId)
    {
        var ws = _k.Case(_k.File("in/a.bin", "alpha"));
        var zip = Path.Combine(_k.Dir, "out", Guid.NewGuid().ToString("N")[..6] + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        CaseArchive.Export(ws.Root, caseId, zip);
        var root = Path.Combine(_k.Dir, "imports-root");
        Directory.CreateDirectory(root);
        var (imported, check) = CaseArchive.Import(zip, root, importAsUnverified: true);
        Assert.Null(imported);
        Assert.False(check.Ok);
        Assert.Contains(check.Problems, p => p.Contains("identificator", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        Assert.False(Directory.Exists(Path.Combine(_k.Dir, "evil")));
    }

    // ---- KeyGen ----

    [Fact]
    public void Passphrase_keygen_asks_twice_and_refuses_a_mismatch()
    {
        var old = Environment.GetEnvironmentVariable(ReportCli.PassphraseEnvironmentVariable);
        Environment.SetEnvironmentVariable(ReportCli.PassphraseEnvironmentVariable, null);
        try
        {
            var o = new StringWriter();
            var bad = Path.Combine(_k.Dir, "k1", "a.key");
            Assert.Equal(1, ReportCli.Keygen(["report-keygen", bad, "--passphrase"], o, new StringReader("prima-parola\nalta-parola\n")));
            Assert.False(File.Exists(bad));
            Assert.Contains("nu coincid", o.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prima-parola", o.ToString(), StringComparison.Ordinal);
            var good = Path.Combine(_k.Dir, "k2", "b.key");
            Assert.Equal(0, ReportCli.Keygen(["report-keygen", good, "--passphrase"], new StringWriter(), new StringReader("aceeasi-parola\naceeasi-parola\n")));
            Assert.True(File.Exists(good));
        }
        finally { Environment.SetEnvironmentVariable(ReportCli.PassphraseEnvironmentVariable, old); }
    }

    [Fact]
    public void Verify_archive_refuses_several_expected_hashes()
    {
        var (_, zip, res) = Make();
        var o = new StringWriter();
        Assert.Equal(1, ReportCli.VerifyArchive(["verify-archive", zip, res.ManifestSha256, new string('a', 64)], o));
        Assert.Contains("o singură valoare", o.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, ReportCli.VerifyArchive(["verify-archive", zip, res.ManifestSha256], new StringWriter()));
    }

    // ---- masked signer ----

    [Fact]
    public void A_person_display_name_signer_label_is_masked_and_an_organisation_label_is_kept()
    {
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel { SignerName = "Ion Popescu" };
        var masker = new LogAnalyzer.Dfir.Privacy.PiiMasker();
        var signer = vm.MaskedSigner(masker);
        Assert.StartsWith("Utilizator-", signer, StringComparison.Ordinal);
        Assert.DoesNotContain("Popescu", signer, StringComparison.Ordinal);
        vm.SignerName = "Laborator DFIR";
        Assert.Equal("Laborator DFIR", vm.MaskedSigner(masker));
    }
}
