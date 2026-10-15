using System;
using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Owner decision 2026-10-16: officer-keygen and verify-report --officer-fingerprint (verification pinned to the fingerprint from the paper). Test keys only.</summary>
public sealed class OfficerKeyCliTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private string Pdf()
    {
        var p = Path.Combine(_k.Dir, "raport.pdf");
        File.WriteAllBytes(p, System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n% test report body\n%%EOF\n"));
        return p;
    }

    [Fact]
    public void Officer_keygen_writes_an_encrypted_key_prints_its_fingerprint_and_never_overwrites()
    {
        var priv = Path.Combine(_k.Dir, "usb", "ofiter.key");
        var o = new StringWriter();
        Assert.Equal(0, ReportCli.OfficerKeygen(["officer-keygen", priv], o, new StringReader("parola-ofiter\nparola-ofiter\n")));
        var text = File.ReadAllText(priv);
        Assert.StartsWith(OfficerKey.Prefix, text);
        var fp = Regex.Match(o.ToString(), "([0-9A-F]{4}-){7}[0-9A-F]{4}").Value;
        using (var k = OfficerKey.Load(text, "parola-ofiter")) Assert.Equal(OfficerKey.Fingerprint(OfficerKey.PublicKeyOf(k)), fp);
        Assert.Equal(1, ReportCli.OfficerKeygen(["officer-keygen", priv], new StringWriter(), new StringReader("parola-ofiter\nparola-ofiter\n")));
        Assert.Equal(1, ReportCli.OfficerKeygen(["officer-keygen", Path.Combine(_k.Dir, "b.key")], new StringWriter(), new StringReader("scurt\nscurt\n")));
        Assert.Equal(1, ReportCli.OfficerKeygen(["officer-keygen", Path.Combine(_k.Dir, "c.key")], new StringWriter(), new StringReader("parola-ofiter\nalta-parola\n")));
        Assert.False(File.Exists(Path.Combine(_k.Dir, "b.key")) || File.Exists(Path.Combine(_k.Dir, "c.key")));
    }

    [Fact]
    public void Verify_report_with_the_officer_fingerprint_is_valid_and_any_other_key_is_unknown()
    {
        var (file, pub) = OfficerKey.Generate("parola-ofiter");
        var fp = OfficerKey.Fingerprint(pub);
        var pdf = Pdf();
        using (var k = OfficerKey.Load(file, "parola-ofiter")) OfficerSignature.Sign(pdf, k, "Ofițer", "", DateTimeOffset.UtcNow);
        var v = new StringWriter();
        Assert.Equal(0, ReportCli.Verify(["verify-report", pdf, "--officer-fingerprint", fp], v));
        Assert.StartsWith("VALID", v.ToString());
        var (_, otherPub) = OfficerKey.Generate("alta-parola-2");
        Assert.Equal(3, ReportCli.Verify(["verify-report", pdf, "--officer-fingerprint", OfficerKey.Fingerprint(otherPub)], new StringWriter()));
        Assert.Equal(1, ReportCli.Verify(["verify-report", pdf, "--officer-fingerprint", "nu-e-amprenta"], new StringWriter()));
        File.AppendAllText(pdf, "x");
        Assert.Equal(2, ReportCli.Verify(["verify-report", pdf, "--officer-fingerprint", fp], new StringWriter()));
    }
}
