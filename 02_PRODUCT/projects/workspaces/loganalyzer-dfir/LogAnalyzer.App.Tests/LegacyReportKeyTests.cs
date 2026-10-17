using System.IO;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Security audit #20: a legacy bare PKCS#8 report signing key still works, but never silently: every use says it is stored unencrypted and
/// names the re-protection command; <c>report-key-protect</c> rewrites it as a DPAPI (or passphrase) protected key file with the same key.
/// </summary>
public sealed class LegacyReportKeyTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private string Pdf()
    {
        var p = Path.Combine(_k.Dir, "raport.pdf");
        File.WriteAllBytes(p, System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n% test\n%%EOF\n"));
        return p;
    }

    private string BareKeyFile(out string pub)
    {
        var (priv, p) = ReportSignature.GenerateKey();
        pub = p;
        var f = Path.Combine(_k.Dir, "legacy.p8");
        File.WriteAllText(f, priv);
        return f;
    }

    [Fact]
    public void Inspect_flags_a_bare_key_and_not_a_protected_one()
    {
        var (priv, _) = ReportSignature.GenerateKey();
        Assert.True(ReportKeyProtection.IsLegacyBareKey(priv));
        Assert.False(ReportKeyProtection.IsLegacyBareKey(ReportKeyProtection.ProtectWithPassphrase(priv, "pw")));
        Assert.Contains("report-key-protect", ReportKeyProtection.LegacyBareKeyWarning);
    }

    [Fact]
    public void Signing_a_report_with_a_bare_key_still_signs_but_warns_and_offers_re_protection()
    {
        var key = BareKeyFile(out var pub);
        var pdf = Pdf();
        var line = new ReportSigningSettings { PrivateKeyPath = key, Signer = "Op" }.SignIfConfigured(pdf);
        Assert.Contains("Raport semnat digital", line);
        Assert.Contains(ReportKeyProtection.LegacyBareKeyWarning, line);
        Assert.Equal(ReportSignatureStatus.Valid, ReportSignature.Verify(pdf, [pub]).Status);
    }

    [Fact]
    public void Signing_with_a_protected_key_does_not_warn()
    {
        var (priv, _) = ReportSignature.GenerateKey();
        var f = Path.Combine(_k.Dir, "prot.p8");
        File.WriteAllText(f, ReportKeyProtection.ProtectForCurrentUser(priv));
        var line = new ReportSigningSettings { PrivateKeyPath = f, Signer = "Op" }.SignIfConfigured(Pdf());
        Assert.Contains("Raport semnat digital", line);
        Assert.DoesNotContain("NECRIPTAT", line);
    }

    [Fact]
    public void Cli_report_sign_warns_for_a_bare_key()
    {
        var key = BareKeyFile(out _);
        var o = new StringWriter();
        Assert.Equal(0, ReportCli.Sign(["report-sign", key, Pdf(), "Op"], o));
        Assert.Contains("report-key-protect", o.ToString());
    }

    [Fact]
    public void Report_key_protect_rewrites_the_bare_key_with_DPAPI_and_keeps_the_same_key()
    {
        var key = BareKeyFile(out var pub);
        var o = new StringWriter();
        Assert.Equal(0, ReportCli.Protect(["report-key-protect", key], o));
        var text = File.ReadAllText(key);
        Assert.StartsWith(ReportKeyProtection.DpapiPrefix, text);
        Assert.False(ReportKeyProtection.IsLegacyBareKey(text));
        using var k = ReportSignature.LoadPrivateKey(text);
        Assert.Equal(ReportSignature.KeyId(pub), ReportSignature.KeyId(System.Convert.ToBase64String(k.ExportSubjectPublicKeyInfo())));
        Assert.Contains(ReportSignature.KeyId(pub), o.ToString());
        Assert.DoesNotContain(Directory.GetFiles(_k.Dir), f => Path.GetFileName(f).StartsWith("legacy.p8.", StringComparison.Ordinal)); // no temporary copy left behind
    }

    [Fact]
    public void Report_key_protect_with_passphrase_uses_the_passphrase()
    {
        var key = BareKeyFile(out _);
        var o = new StringWriter();
        Assert.Equal(0, ReportCli.Protect(["report-key-protect", key, "--passphrase"], o, new StringReader("pw-1\npw-1\n")));
        var text = File.ReadAllText(key);
        Assert.True(ReportKeyProtection.NeedsPassphrase(text));
        using var _1 = ReportSignature.LoadPrivateKey(text, "pw-1");
    }

    [Fact]
    public void Report_key_protect_leaves_an_already_protected_key_and_refuses_garbage()
    {
        var (priv, _) = ReportSignature.GenerateKey();
        var f = Path.Combine(_k.Dir, "prot.p8");
        var protectedText = ReportKeyProtection.ProtectWithPassphrase(priv, "pw");
        File.WriteAllText(f, protectedText);
        Assert.Equal(0, ReportCli.Protect(["report-key-protect", f], new StringWriter()));
        Assert.Equal(protectedText, File.ReadAllText(f));

        var g = Path.Combine(_k.Dir, "garbage.p8");
        File.WriteAllText(g, "not a key");
        Assert.Equal(1, ReportCli.Protect(["report-key-protect", g], new StringWriter()));
        Assert.Equal("not a key", File.ReadAllText(g));
    }
}
