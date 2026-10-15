using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Round-2 security review of PR #289: probes P7–P12 (P12 is in LogAnalyzer.Dfir.Tests) as regression tests. Test material only.</summary>
public sealed partial class OfficerQualifiedSignatureTests
{
    private sealed record Setup(Pki Pki, System.Security.Cryptography.X509Certificates.X509Certificate2 Officer, byte[] Original) : IDisposable
    {
        public void Dispose() { Officer.Dispose(); Pki.Dispose(); }
    }

    private static Setup NewSetup()
    {
        var pki = Pki.Create();
        return new Setup(pki, pki.Issue("Ofiter Test", nonRepudiation: true), Report());
    }

    private static OfficialVerification VerifyWithPaper(Setup s, byte[] signed) =>
        OfficialReportVerifier.Verify(signed, SignedCmsVerifier.Instance, [Sha(s.Original)], OfficerFlows.Trusted(FirstOfficer(s.Officer)), OfficerFingerprint.Of(s.Officer),
                                      new QualifiedSignatureTrust([s.Pki.Ca], [s.Pki.Crl()]));

    private static byte[] SignWith(Setup s, IReadOnlyDictionary<int, string>? extra = null, Func<int, string, string>? rewrite = null, IReadOnlyList<(int, string?)>? raw = null) =>
        PdfIncrementalSigner.Sign(s.Original, Look, new SignedCmsSigner(s.Officer), allowPriorUpdates: false, extraObjects: extra, rewrite: rewrite, raw: raw);

    private static void AssertModified(OfficialVerification v)
    {
        Assert.Equal(OfficialVerdict.Invalid, v.Verdict);
        Assert.Contains("modificat înainte de semnare", v.Headline);
    }

    [Fact]
    public void P8_an_object_header_with_a_comment_between_its_tokens_is_still_seen_and_refused()
    {
        using var s = NewSetup();
        int cs = ContentStreamOf(s.Original, 0);
        const string fake = "BT /F4 24 Tf 72 700 Td (RAPORT FALS) Tj ET";
        var raw = new List<(int, string?)> { (cs, $"{cs} 0 %c\nobj\n<</Length {fake.Length}>>\nstream\n{fake}\nendstream\nendobj\n") };
        AssertModified(VerifyWithPaper(s, SignWith(s, raw: raw)));
    }

    [Fact]
    public void P9_a_free_entry_in_the_update_xref_is_refused()
    {
        using var s = NewSetup();
        AssertModified(VerifyWithPaper(s, SignWith(s, raw: [(ContentStreamOf(s.Original, 0), null)])));
    }

    [Theory]
    [InlineData("/A <</S /Java#53cript /JS (app.alert(1))>>")]
    [InlineData("/AA <</O <</S /JavaScript /J#53 (app.alert(1))>>>>")]
    [InlineData("/A <</S /URI /URI (http://exemplu.invalid)>>")]
    public void P10_P11_actions_and_links_in_the_widget_are_refused_even_with_escaped_names(string action)
    {
        using var s = NewSetup();
        var signed = SignWith(s, rewrite: (num, body) => body.Contains("/Subtype /Widget") ? body[..body.LastIndexOf(">>", StringComparison.Ordinal)] + " " + action + ">>" : body);
        AssertModified(VerifyWithPaper(s, signed));
    }

    [Fact]
    public void P11_XFA_in_the_form_is_refused()
    {
        using var s = NewSetup();
        var st = PdfStructure.Parse(s.Original, 2);
        var field = SignatureField.Find(st)!.Value;
        var signed = SignWith(s, extra: new Dictionary<int, string> { [field.Form] = $"<</Fields [{field.Widget} 0 R] /SigFlags 3 /XFA [(preamble) (x)]>>" });
        AssertModified(VerifyWithPaper(s, signed));
    }

    [Fact]
    public void P7_a_second_widget_is_refused()
    {
        using var s = NewSetup();
        var st = PdfStructure.Parse(s.Original, 2);
        int extra = st.Size + 10;
        var signed = SignWith(s,
            extra: new Dictionary<int, string> { [extra] = $"<</Type /Annot /Subtype /Widget /FT /Sig /Rect [0 0 10 10] /P {st.Pages[^1]} 0 R>>" },
            rewrite: (num, body) => num == st.Pages[^1] ? Regex.Replace(body, @"/Annots \[(\d+) 0 R\]", m => $"/Annots [{m.Groups[1].Value} 0 R {extra} 0 R]") : body);
        AssertModified(VerifyWithPaper(s, signed));
    }

    [Fact]
    public void A_widget_covering_the_page_or_a_rotating_appearance_matrix_is_refused()
    {
        using var s = NewSetup();
        AssertModified(VerifyWithPaper(s, SignWith(s, rewrite: (n, b) => b.Contains("/Subtype /Widget") ? Regex.Replace(b, @"/Rect \[[^\]]*\]", "/Rect [0 0 595 842]") : b)));
        AssertModified(VerifyWithPaper(s, SignWith(s, rewrite: (n, b) => b.Contains("/Subtype /Form") ? b.Replace("/BBox", "/Matrix [0 1 -1 0 0 0] /BBox") : b)));
    }

    [Fact]
    public void Our_own_update_holds_exactly_the_whitelisted_objects_and_leaves_every_original_byte_untouched()
    {
        using var s = NewSetup();
        var signed = OfficerFlows.Sign(s.Original, s.Officer, "ST-1", "r", Now);
        Assert.True(signed.AsSpan(0, s.Original.Length).SequenceEqual(s.Original));
        var inspection = PdfUpdateInspector.Inspect(signed, s.Original.Length);
        Assert.Null(inspection.Problem);
        Assert.Equal(["appearance", "font", "sig", "widget"], inspection.Roles.Values.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(OfficialVerdict.Oficial, VerifyWithPaper(s, signed).Verdict);
    }

    [Fact]
    public void A_signature_dictionary_ending_with_its_hex_Contents_is_read_correctly()
    {
        const string d = "<</Type /Sig /Filter /Adobe.PPKLite /Contents <00AB>>>";
        Assert.Equal(d, PdfStructure.LeadingDict(d + " trailing", 0));
        var bytes = Encoding.ASCII.GetBytes("7 0 obj\n" + d + "\nendobj\n");
        var o = new PdfParser(bytes, 0, bytes.Length).ReadIndirect();
        Assert.Equal(7, o.Num);
        Assert.Equal([0x00, 0xAB], Assert.IsType<PdfString>(((PdfDict)o.Value)["Contents"]).Bytes);
    }

    [Fact]
    public void Names_are_decoded_and_comments_skipped_by_the_parser()
    {
        var bytes = Encoding.ASCII.GetBytes("<</S /Java#53cript % comentariu\n /K [1 0 R (a\\)b) <41 42>]>>");
        var d = Assert.IsType<PdfDict>(new PdfParser(bytes, 0, bytes.Length).ReadValue());
        Assert.Equal("JavaScript", d.NameOf("S"));
        var arr = Assert.IsType<PdfArray>(d["K"]);
        Assert.IsType<PdfRef>(arr.Items[0]);
        Assert.Equal("a)b", Encoding.ASCII.GetString(Assert.IsType<PdfString>(arr.Items[1]).Bytes));
        Assert.Equal("AB", Encoding.ASCII.GetString(Assert.IsType<PdfString>(arr.Items[2]).Bytes));
    }

    [Fact]
    public void The_certificate_line_uses_the_same_qualified_or_advanced_label()
    {
        using var s = NewSetup();
        var v = VerifyWithPaper(s, OfficerFlows.Sign(s.Original, s.Officer, "ST-1", "r", Now));
        Assert.Contains(v.Lines, l => l.Text.Contains("Certificatul semnatarului") && l.Text.Contains(v.QualifiedLabel));
        Assert.DoesNotContain(v.Lines, l => l.Text.Contains("este de semnătură calificată (nerepudiere)"));
    }
}
