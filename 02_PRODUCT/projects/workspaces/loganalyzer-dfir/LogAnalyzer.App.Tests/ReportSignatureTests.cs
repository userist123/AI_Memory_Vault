using System.IO;
using System.Text.Json;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>F3: detached ECDSA P-256 signature of a report, verified as VALID / MODIFIED / UNKNOWN KEY, in the library and on the command line. Test keys only.</summary>
public sealed class ReportSignatureTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private string Pdf(string name = "raport.pdf")
    {
        var p = Path.Combine(_k.Dir, name);
        File.WriteAllBytes(p, System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n% test report body\n%%EOF\n"));
        return p;
    }

    [Fact]
    public void A_signed_report_verifies_VALID_with_the_trusted_key()
    {
        var (priv, pub) = ReportSignature.GenerateKey();
        var pdf = Pdf();
        using var key = ReportSignature.LoadPrivateKey(priv);
        var s = ReportSignature.Sign(pdf, key, "Operator Test");
        Assert.True(File.Exists(pdf + ".sig"));
        Assert.Equal(ReportSignature.Sha256File(pdf), s.Sha256);
        Assert.Equal(ReportSignature.KeyId(pub), s.KeyId);
        var r = ReportSignature.Verify(pdf, [pub]);
        Assert.Equal(ReportSignatureStatus.Valid, r.Status);
        Assert.Equal("VALID", r.Word);
        Assert.Contains("SEMNĂTURĂ VALIDĂ — fișierul nu a fost modificat de la semnare", r.Message);
        // the .sig is plain JSON with the SHA-256 and the signer, readable without the application
        var json = JsonDocument.Parse(File.ReadAllText(pdf + ".sig")).RootElement;
        Assert.Equal(s.Sha256, json.GetProperty("sha256").GetString());
        Assert.Equal("Operator Test", json.GetProperty("signer").GetString());
    }

    [Fact]
    public void One_changed_byte_is_MODIFIED()
    {
        var (priv, pub) = ReportSignature.GenerateKey();
        var pdf = Pdf();
        using (var key = ReportSignature.LoadPrivateKey(priv)) ReportSignature.Sign(pdf, key, "Operator Test");
        var bytes = File.ReadAllBytes(pdf); bytes[12] ^= 0x01; File.WriteAllBytes(pdf, bytes);
        var r = ReportSignature.Verify(pdf, [pub]);
        Assert.Equal(ReportSignatureStatus.Modified, r.Status);
        Assert.Equal("MODIFIED", r.Word);
    }

    [Fact]
    public void A_rewritten_signature_file_with_a_matching_hash_is_MODIFIED()
    {
        var (priv, pub) = ReportSignature.GenerateKey();
        var pdf = Pdf();
        using (var key = ReportSignature.LoadPrivateKey(priv)) ReportSignature.Sign(pdf, key, "Operator Test");
        File.AppendAllText(pdf, "extra");
        var sig = JsonSerializer.Deserialize<ReportSignatureFile>(File.ReadAllText(pdf + ".sig"))!;
        sig.Sha256 = ReportSignature.Sha256File(pdf);   // forger updates the hash but cannot re-sign
        File.WriteAllText(pdf + ".sig", JsonSerializer.Serialize(sig));
        Assert.Equal(ReportSignatureStatus.Modified, ReportSignature.Verify(pdf, [pub]).Status);
    }

    [Fact]
    public void A_signature_by_a_key_that_is_not_trusted_is_UNKNOWN_KEY()
    {
        var (priv, _) = ReportSignature.GenerateKey();
        var (_, otherPub) = ReportSignature.GenerateKey();
        var pdf = Pdf();
        using (var key = ReportSignature.LoadPrivateKey(priv)) ReportSignature.Sign(pdf, key, "Altcineva");
        var r = ReportSignature.Verify(pdf, [otherPub]);
        Assert.Equal(ReportSignatureStatus.UnknownKey, r.Status);
        Assert.Equal("UNKNOWN KEY", r.Word);
        Assert.Equal(ReportSignatureStatus.UnknownKey, ReportSignature.Verify(pdf, []).Status);
    }

    [Fact]
    public void A_report_without_a_signature_file_is_MISSING_never_valid()
    {
        var (_, pub) = ReportSignature.GenerateKey();
        Assert.Equal(ReportSignatureStatus.Missing, ReportSignature.Verify(Pdf(), [pub]).Status);
    }

    [Fact]
    public void Settings_sign_when_a_key_is_configured_and_say_so_when_not()
    {
        var pdf = Pdf();
        Assert.Contains("NU este semnat", new ReportSigningSettings().SignIfConfigured(pdf));
        Assert.False(File.Exists(pdf + ".sig"));
        var (priv, pub) = ReportSignature.GenerateKey();
        var keyFile = Path.Combine(_k.Dir, "key.p8");
        File.WriteAllText(keyFile, priv);
        var settings = new ReportSigningSettings { PrivateKeyPath = keyFile, Signer = "Operator Test", TrustedPublicKeys = [pub] };
        var path = Path.Combine(_k.Dir, "report_signing.json");
        settings.Save(path);
        var loaded = ReportSigningSettings.Load(path);
        Assert.Equal(keyFile, loaded.PrivateKeyPath);
        Assert.Contains("Raport semnat digital", loaded.SignIfConfigured(pdf));
        Assert.Equal(ReportSignatureStatus.Valid, ReportSignature.Verify(pdf, loaded.TrustedPublicKeys).Status);
        Assert.DoesNotContain(priv, File.ReadAllText(path));   // the settings hold the path, never the private key
    }

    [Fact]
    public void Cli_keygen_sign_and_verify_report_print_VALID_MODIFIED_UNKNOWN_KEY_with_exit_codes()
    {
        var keyDir = Path.Combine(_k.Dir, "keys");
        var priv = Path.Combine(keyDir, "report.p8");
        var pubFile = Path.Combine(keyDir, "report.pub");
        var o = new StringWriter();
        Assert.Equal(0, ReportCli.Keygen(["report-keygen", priv, pubFile], o));
        Assert.True(File.Exists(priv) && File.Exists(pubFile));
        Assert.Equal(1, ReportCli.Keygen(["report-keygen", priv], new StringWriter()));   // never overwrites
        var pdf = Pdf();
        Assert.Equal(0, ReportCli.Sign(["report-sign", priv, pdf, "Operator Test"], new StringWriter()));

        var v = new StringWriter();
        Assert.Equal(0, ReportCli.Verify(["verify-report", pdf, pubFile], v));
        Assert.StartsWith("VALID", v.ToString());

        var (_, stranger) = ReportSignature.GenerateKey();
        var u = new StringWriter();
        Assert.Equal(3, ReportCli.Verify(["verify-report", pdf, stranger], u));
        Assert.StartsWith("UNKNOWN KEY", u.ToString());

        File.AppendAllText(pdf, "x");
        var m = new StringWriter();
        Assert.Equal(2, ReportCli.Verify(["verify-report", pdf, File.ReadAllText(pubFile)], m));
        Assert.StartsWith("MODIFIED", m.ToString());
    }

    [Fact]
    public void Cli_keygen_refuses_to_write_the_private_key_inside_a_git_repository()
    {
        var repo = Path.Combine(_k.Dir, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var o = new StringWriter();
        Assert.Equal(1, ReportCli.Keygen(["report-keygen", Path.Combine(repo, "sub", "k.p8")], o));
        Assert.False(File.Exists(Path.Combine(repo, "sub", "k.p8")));
        Assert.Contains("depozit git", o.ToString());
    }

    [Fact]
    public void App_verify_function_uses_the_trusted_keys_configured_on_the_page()
    {
        var (priv, pub) = ReportSignature.GenerateKey();
        var pdf = Pdf();
        using (var key = ReportSignature.LoadPrivateKey(priv)) ReportSignature.Sign(pdf, key, "Operator Test");
        var vm = new LogAnalyzer.UI.ViewModels.InvestigationViewModel { TrustedKeysText = "" };
        Assert.StartsWith("UNKNOWN KEY", vm.VerifyReportFile(pdf));
        vm.TrustedKeysText = pub;
        Assert.StartsWith("VALID", vm.VerifyReportFile(pdf));
    }
}
