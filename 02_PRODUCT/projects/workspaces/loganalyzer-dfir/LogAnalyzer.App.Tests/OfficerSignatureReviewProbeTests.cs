using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.KeyGen;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Security review of PR #289: every probe of the reviewer reproduced as a regression test (test material only).</summary>
public sealed partial class OfficerQualifiedSignatureTests
{
    private static readonly SignatureAppearance Look = new("Ofiter Test", "Test QTSP CA", Now, "ST-1", "Raport de control");

    private static int ContentStreamOf(byte[] pdf, int pageIndex)
    {
        var st = PdfStructure.Parse(pdf);
        return int.Parse(Regex.Match(st.Dict(st.Pages[pageIndex]), @"/Contents\s+(\d+)\s+0\s+R").Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Probe 1: the administrator appends an UNSIGNED incremental update that replaces a page content stream, then the officer signs (Adobe signs any file).</summary>
    private static byte[] WithAdministratorUpdate(byte[] original)
    {
        var st = PdfStructure.Parse(original);
        int obj = ContentStreamOf(original, 0);
        const string content = "BT /F4 24 Tf 72 700 Td (RAPORT FALS - TOTUL CONFORM) Tj ET";
        var sb = new StringBuilder();
        long off = original.Length;
        sb.Append(CultureInfo.InvariantCulture, $"{obj} 0 obj\n<</Length {content.Length}>>\nstream\n{content}\nendstream\nendobj\n");
        long xref = off + sb.Length;
        sb.Append(CultureInfo.InvariantCulture, $"xref\n{obj} 1\n{off:D10} 00000 n \ntrailer\n<</Size {st.Size} /Root {st.Root} 0 R /Prev {st.StartXref}>>\nstartxref\n{xref}\n%%EOF\n");
        return [.. original, .. Encoding.ASCII.GetBytes(sb.ToString())];
    }

    [Fact]
    public void Probe1_an_unsigned_update_between_the_report_and_the_signature_is_INVALID()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var signed = PdfIncrementalSigner.Sign(WithAdministratorUpdate(original), Look, new SignedCmsSigner(officer), allowPriorUpdates: true, extraObjects: null);
        var v = OfficialReportVerifier.Verify(signed, SignedCmsVerifier.Instance, [Sha(original)], OfficerFlows.Trusted(FirstOfficer(officer)), OfficerFingerprint.Of(officer),
                                              new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]));
        Assert.Equal(OfficialVerdict.Invalid, v.Verdict);
        Assert.Contains("modificat înainte de semnare", v.Headline);
        Assert.DoesNotContain(v.Lines, l => l.Passed == true && l.Text.Contains("Raportul original este exact cel generat"));
    }

    [Fact]
    public void Probe1_an_original_object_replaced_inside_the_signature_update_is_INVALID()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var trusted = OfficerFlows.Trusted(FirstOfficer(officer));
        var trust = new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]);
        const string fake = "BT /F4 24 Tf 72 700 Td (RAPORT FALS) Tj ET";
        var replacedContent = new Dictionary<int, string> { [ContentStreamOf(original, 0)] = $"<</Length {fake.Length}>>\nstream\n{fake}\nendstream" };
        var s1 = PdfIncrementalSigner.Sign(original, Look, new SignedCmsSigner(officer), allowPriorUpdates: false, extraObjects: replacedContent);
        var v1 = OfficialReportVerifier.Verify(s1, SignedCmsVerifier.Instance, [Sha(original)], trusted, OfficerFingerprint.Of(officer), trust);
        Assert.Equal(OfficialVerdict.Invalid, v1.Verdict);
        Assert.Contains("modificat înainte de semnare", v1.Headline);

        // the first page dictionary rewritten with another content stream and size
        var st = PdfStructure.Parse(original);
        var page = st.Dict(st.Pages[0]).Replace("/MediaBox [0 0 595 842]", "/MediaBox [0 0 300 300]");
        var s2 = PdfIncrementalSigner.Sign(original, Look, new SignedCmsSigner(officer), false, new Dictionary<int, string> { [st.Pages[0]] = page });
        Assert.Equal(OfficialVerdict.Invalid, OfficialReportVerifier.Verify(s2, SignedCmsVerifier.Instance, [Sha(original)], trusted, OfficerFingerprint.Of(officer), trust).Verdict);

        // a new annotation (not a signature widget) drawn over the page
        var overlay = new Dictionary<int, string> { [st.Size + 10] = "<</Type /Annot /Subtype /FreeText /Rect [0 0 595 842] /Contents (TOTUL CONFORM)>>" };
        var s3 = PdfIncrementalSigner.Sign(original, Look, new SignedCmsSigner(officer), false, overlay);
        Assert.Equal(OfficialVerdict.Invalid, OfficialReportVerifier.Verify(s3, SignedCmsVerifier.Instance, [Sha(original)], trusted, OfficerFingerprint.Of(officer), trust).Verdict);

        // our own signature update stays OFICIAL
        var ok = OfficerFlows.Sign(original, officer, "ST-1", "r", Now);
        Assert.Equal(OfficialVerdict.Oficial, OfficialReportVerifier.Verify(ok, SignedCmsVerifier.Instance, [Sha(original)], trusted, OfficerFingerprint.Of(officer), trust).Verdict);
    }

    /// <summary>A minimal file shaped like a signed PDF, with the given /M date and /Contents hex: parsing must stay total.</summary>
    private static byte[] Synthetic(string m, string hex)
    {
        const string ph = "[0 ********** ********** **********]";
        var head = $"%PDF-1.7\n1 0 obj\n<</Type /Sig /SubFilter /ETSI.CAdES.detached /ByteRange {ph} /M ({m}) /Contents ";
        var tail = " >>\nendobj\n%%EOF\n";
        int lt = Encoding.ASCII.GetByteCount(head), gt = lt + hex.Length + 2;
        var total = gt + Encoding.ASCII.GetByteCount(tail);
        var br = $"[0 {lt,-10} {gt,-10} {total - gt,-10}]";
        return Encoding.ASCII.GetBytes(head.Replace(ph, br) + "<" + hex + ">" + tail);
    }

    [Fact]
    public void Probe3_malformed_signed_files_and_planted_approvals_never_throw()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var trusted = OfficerFlows.Trusted(FirstOfficer(officer));
        var trust = new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]);
        var inputs = new List<byte[]>
        {
            Synthetic("D:20261016090000+99'00'", "3003020105"),
            Synthetic("D:20261016090000+00'00'", "300302010"),        // odd number of hex digits
            Synthetic("D:99999999999999", "3082FFFF"),                // length beyond the buffer
            Synthetic("D:2026", ""),
        };
        var signed = OfficerFlows.Sign(Report(), officer, "ST-1", "r", Now);
        var rng = new Random(289);
        for (int i = 0; i < 60; i++)
        {
            var b = (byte[])signed.Clone();
            for (int k = 0; k < 4; k++) b[rng.Next(b.Length)] = (byte)rng.Next(256);
            inputs.Add(b);
            inputs.Add(signed.AsSpan(0, rng.Next(signed.Length)).ToArray());
        }
        foreach (var b in inputs)
        {
            var v = OfficialReportVerifier.Verify(b, SignedCmsVerifier.Instance, [Sha(signed)], trusted, OfficerFingerprint.Of(officer), trust);
            Assert.NotEqual(OfficialVerdict.Oficial, v.Verdict);
        }

        // a planted approval file must not break reading the officer list
        var data = FirstOfficer(officer);
        using var other = pki.Issue("Alt Ofiter", nonRepudiation: true);
        var forged = OfficerRegistry.Identity(other);
        forged.Basis = OfficerRegistry.BasisApproved; forged.ApprovedBy = data.Officers[0].Fingerprint;
        forged.ApprovalPdf = Convert.ToBase64String(Synthetic("D:20261016090000+99'00'", "300302010"));
        data.Officers.Add(forged);
        var t = OfficerRegistry.Trusted(data.Officers, SignedCmsVerifier.Instance, out var problems);
        Assert.Single(t);
        Assert.Single(problems);
    }

    [Fact]
    public void Probe4_changing_the_zero_padding_after_the_CMS_is_INVALID()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var signed = OfficerFlows.Sign(original, officer, "ST-1", "r", Now);
        var text = Encoding.Latin1.GetString(signed);
        int contentsEnd = text.IndexOf('>', text.LastIndexOf("/Contents <", StringComparison.Ordinal));
        var padded = (byte[])signed.Clone();
        padded[contentsEnd - 1] = (byte)'1';                     // last padding digit
        var v = OfficialReportVerifier.Verify(padded, SignedCmsVerifier.Instance, [Sha(original)], OfficerFlows.Trusted(FirstOfficer(officer)), OfficerFingerprint.Of(officer),
                                              new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]));
        Assert.Equal(OfficialVerdict.Invalid, v.Verdict);
        Assert.Contains("umplutur", v.Headline);
    }

    [Fact]
    public void Probe5_the_label_says_qualified_only_when_the_certificate_declares_it()
    {
        using var pki = Pki.Create();
        using var advanced = pki.Issue("Ofiter Avansat", nonRepudiation: true);
        using var qualified = pki.Issue("Ofiter Calificat", nonRepudiation: true, qcStatements: true);
        var original = Report();
        var trust = new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]);
        var a = OfficialReportVerifier.Verify(OfficerFlows.Sign(original, advanced, "ST-1", "r", Now), SignedCmsVerifier.Instance, [Sha(original)],
                                              OfficerFlows.Trusted(FirstOfficer(advanced)), OfficerFingerprint.Of(advanced), trust);
        Assert.Contains("semnătură electronică avansată (certificatul nu declară că e calificat)", a.QualifiedLabel);
        Assert.Contains(a.QualifiedLabel, a.ToPlainText());
        var q = OfficialReportVerifier.Verify(OfficerFlows.Sign(original, qualified, "ST-1", "r", Now), SignedCmsVerifier.Instance, [Sha(original)],
                                              OfficerFlows.Trusted(FirstOfficer(qualified)), OfficerFingerprint.Of(qualified), trust);
        Assert.StartsWith("semnătură electronică calificată", q.QualifiedLabel);
    }

    [Fact]
    public void Probe6_an_old_runtime_is_named_in_the_verdict_without_failing()
    {
        Assert.Contains("10.0.13", RuntimeGuard.Warning(new Version(10, 0, 9)));
        Assert.Null(RuntimeGuard.Warning(new Version(10, 0, 13)));
        Assert.Null(RuntimeGuard.Warning(new Version(11, 0, 0)));
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var v = OfficialReportVerifier.Verify(OfficerFlows.Sign(original, officer, "ST-1", "r", Now), SignedCmsVerifier.Instance, [Sha(original)],
                                              OfficerFlows.Trusted(FirstOfficer(officer)), OfficerFingerprint.Of(officer), new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]),
                                              runtime: new Version(10, 0, 9));
        Assert.Equal(OfficialVerdict.Oficial, v.Verdict);
        Assert.Contains(v.Warnings, w => w.Contains("actualizați .NET la 10.0.13"));
        Assert.Contains("actualizați .NET la 10.0.13", v.ToPlainText());
    }

    [Fact]
    public void Probe2_station_list_officers_are_labelled_declared_not_confirmed()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var signed = OfficerFlows.Sign(original, officer, "ST-1", "r", Now);
        var trust = new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]);
        var stationOnly = OfficialReportVerifier.Verify(signed, SignedCmsVerifier.Instance, [Sha(original)], OfficerFlows.Trusted(FirstOfficer(officer)), null, trust);
        Assert.Contains(stationOnly.Lines, l => l.Text.Contains("declarat pe stație (neconfirmat)"));
        var r = LogAnalyzer.Dfir.Windows.Audit.ControlEvaluator.Evaluate(new LogAnalyzer.Dfir.Windows.Audit.StationFacts { Host = "ST-1", PeriodStartUtc = Now.AddDays(-1), PeriodEndUtc = Now });
        r.RegisteredOfficers = OfficerFlows.Trusted(FirstOfficer(officer));
        Assert.Contains(LogAnalyzer.Dfir.Windows.Audit.ControlReportPdf.OfficerSectionLines(r), l => l.Contains("declarat pe stație (neconfirmat)"));
    }

    [Fact]
    public void KeyGen_verify_report_checks_a_card_signed_pdf_against_the_paper_fingerprint_and_report_hash()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var pdf = Path.Combine(_k.Dir, "semnat.pdf"); File.WriteAllBytes(pdf, OfficerFlows.Sign(original, officer, "ST-1", "r", Now));
        var ca = Path.Combine(_k.Dir, "ca.cer"); File.WriteAllBytes(ca, pki.Ca.RawData);
        var crl = Path.Combine(_k.Dir, "ca.crl"); File.WriteAllBytes(crl, pki.Crl().Der);
        var o = new StringWriter();
        Assert.Equal(0, ReportCli.Verify(["verify-report", pdf, "--officer-fingerprint", OfficerFingerprint.Of(officer), "--report-sha256", Sha(original), "--ca", ca, "--crl", crl], o));
        Assert.StartsWith("OFICIAL", o.ToString());
        using var other = pki.Issue("Alt", nonRepudiation: true);
        Assert.Equal(2, ReportCli.Verify(["verify-report", pdf, "--officer-fingerprint", OfficerFingerprint.Of(other), "--report-sha256", Sha(original), "--ca", ca, "--crl", crl], new StringWriter()));
        Assert.Equal(1, ReportCli.Verify(["verify-report", pdf, "--officer-fingerprint", "nu-e-amprenta", "--report-sha256", Sha(original)], new StringWriter()));
        Assert.Equal(5, ReportCli.Verify(["verify-report", pdf, "--officer-fingerprint", OfficerFingerprint.Of(officer), "--report-sha256", Sha(original)], new StringWriter()));   // no CA / CRL given: valid but not official
    }
}
