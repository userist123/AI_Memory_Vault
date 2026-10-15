using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Owner decision 2026-10-16 (final): the officer signs the report in the application with the qualified certificate (PAdES B-B, one incremental update, CMS
/// with SignedCms) and the same verifier accepts our output and Adobe's. A software test CA, test certificates (with and without non-repudiation) and
/// test CRLs are made in memory; the smart card is replaced by a certificate with a software key through the same code path. Test material only.
/// </summary>
public sealed class OfficerQualifiedSignatureTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private sealed class Pki : IDisposable
    {
        public X509Certificate2 Ca = null!;
        public void Dispose() => Ca.Dispose();

        public static Pki Create()
        {
            var key = RSA.Create(2048);
            var req = new CertificateRequest("CN=Test QTSP CA, O=Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
            return new Pki { Ca = req.CreateSelfSigned(Now.AddDays(-30), Now.AddYears(5)) };
        }

        public X509Certificate2 Issue(string cn, bool nonRepudiation)
        {
            var key = RSA.Create(2048);
            var req = new CertificateRequest($"CN={cn}, O=Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509KeyUsageExtension(nonRepudiation ? X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.DigitalSignature, true));
            req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Ca, true, false));
            using var pub = req.Create(Ca, Now.AddDays(-10), Now.AddYears(2), RandomNumberGenerator.GetBytes(8));
            return pub.CopyWithPrivateKey(key);
        }

        public ParsedCrl Crl(params X509Certificate2[] revoked)
        {
            var b = new CertificateRevocationListBuilder();
            foreach (var r in revoked) b.AddEntry(r, Now.AddDays(-1));
            return ParsedCrl.Parse(b.Build(Ca, 1, Now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, Now.AddDays(-1)));
        }
    }

    private static byte[] Report(string host = "ST-1")
    {
        var path = Path.Combine(Path.GetTempPath(), "r_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var f = new StationFacts { Host = host, PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now, IsAdministrator = true };
            ControlReportPdf.Write(ControlEvaluator.Evaluate(f), path, "Inspector Test");
            return File.ReadAllBytes(path);
        }
        finally { File.Delete(path); }
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    private static OfficerStoreData FirstOfficer(X509Certificate2 cert)
    {
        var d = new OfficerStoreData();
        OfficerRegistry.RegisterFirst(d, OfficerRegistry.Identity(cert), Now);
        return d;
    }

    [Fact]
    public void A_report_signed_in_the_app_is_OFICIAL_only_with_the_fingerprint_from_paper_and_a_current_CRL()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var signed = OfficerFlows.Sign(original, officer, "ST-1", "Raport de control", Now);
        Assert.True(signed.AsSpan(0, original.Length).SequenceEqual(original));
        var trusted = OfficerFlows.Trusted(FirstOfficer(officer));
        var fp = OfficerFingerprint.Of(officer);
        var trust = new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]);

        var official = OfficialReportVerifier.Verify(signed, SignedCmsVerifier.Instance, [Sha(original)], trusted, fp, trust);
        Assert.Equal(OfficialVerdict.Oficial, official.Verdict);
        Assert.Equal("OFICIAL", official.VerdictWord);
        Assert.Equal(Sha(original), official.OriginalSha256);
        Assert.All(official.Lines, l => Assert.True(l.Passed));

        var stationOnly = OfficialReportVerifier.Verify(signed, SignedCmsVerifier.Instance, [Sha(original)], trusted, null, trust);
        Assert.Equal(OfficialVerdict.ValidNeoficial, stationOnly.Verdict);
        Assert.Contains("semnătură validă față de lista stației — confirmați amprenta de pe procesul-verbal", stationOnly.Headline);

        var noCrl = OfficialReportVerifier.Verify(signed, SignedCmsVerifier.Instance, [Sha(original)], trusted, fp, new QualifiedSignatureTrust([pki.Ca], []));
        Assert.Equal(OfficialVerdict.ValidNeoficial, noCrl.Verdict);
        Assert.Contains(noCrl.Lines, l => l.Passed is null && l.Text.Contains("nu se poate confirma revocarea") && l.Text.Contains("importați"));

        var noCa = OfficialReportVerifier.Verify(signed, SignedCmsVerifier.Instance, [Sha(original)], trusted, fp, QualifiedSignatureTrust.Empty);
        Assert.Equal(OfficialVerdict.ValidNeoficial, noCa.Verdict);
    }

    [Fact]
    public void The_CMS_is_CAdES_baseline_signing_certificate_v2_present_and_no_signing_time()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var signed = OfficerFlows.Sign(Report(), officer, "ST-1", "r", Now);
        var text = Encoding.Latin1.GetString(signed);
        int lt = text.LastIndexOf("/Contents <", StringComparison.Ordinal) + 11;
        var hex = text[lt..text.IndexOf('>', lt)].TrimEnd('0');
        if (hex.Length % 2 == 1) hex += "0";
        var cms = new SignedCms();
        cms.Decode(Convert.FromHexString(hex));
        var attrs = cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid.Value).ToList();
        Assert.Contains(SignedCmsSigner.SigningCertificateV2Oid, attrs);
        Assert.Contains("1.2.840.113549.1.9.4", attrs);          // message-digest
        Assert.DoesNotContain("1.2.840.113549.1.9.5", attrs);    // no signing-time (PAdES baseline)
        Assert.Equal("2.16.840.1.101.3.4.2.1", cms.SignerInfos[0].DigestAlgorithm.Value);
    }

    [Fact]
    public void Tampering_before_or_after_signing_and_reports_not_generated_here_are_INVALID()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var signed = OfficerFlows.Sign(original, officer, "ST-1", "r", Now);
        var trusted = OfficerFlows.Trusted(FirstOfficer(officer));
        var fp = OfficerFingerprint.Of(officer);
        var trust = new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]);
        OfficialVerification V(byte[] b, IReadOnlyCollection<string>? hashes = null) =>
            OfficialReportVerifier.Verify(b, SignedCmsVerifier.Instance, hashes ?? [Sha(original)], trusted, fp, trust);

        var changed = (byte[])signed.Clone(); changed[200] ^= 0x01;                            // one byte of the original part
        Assert.Equal(OfficialVerdict.Invalid, V(changed).Verdict);
        Assert.Contains("nu corespunde conținutului", V(changed).Headline);

        var later = signed.Concat(Encoding.ASCII.GetBytes("\n99 0 obj\n<<>>\nendobj\n%%EOF\n")).ToArray();   // an update after the signature
        Assert.Contains("modificat după semnare", V(later).Headline);

        // a different (edited) report signed properly is not one of the generated reports
        var edited = Report("ST-1-modificat");
        var signedEdited = OfficerFlows.Sign(edited, officer, "ST-1", "r", Now);
        var r = V(signedEdited);
        Assert.Equal(OfficialVerdict.Invalid, r.Verdict);
        Assert.Contains("modificat înainte de semnare", r.Headline);
    }

    [Fact]
    public void Authentication_certificates_unregistered_signers_wrong_paper_fingerprints_and_revoked_certificates_are_refused()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        using var authOnly = pki.Issue("Admin Autentificare", nonRepudiation: false);
        using var stranger = pki.Issue("Alt Semnatar", nonRepudiation: true);
        var original = Report();
        var trusted = OfficerFlows.Trusted(FirstOfficer(officer));
        var trust = new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]);

        Assert.Throws<InvalidOperationException>(() => OfficerRegistry.Identity(authOnly));
        var withAuth = OfficialReportVerifier.Verify(OfficerFlows.Sign(original, authOnly, "ST-1", "r", Now), SignedCmsVerifier.Instance, [Sha(original)], trusted, null, trust);
        Assert.Equal(OfficialVerdict.Invalid, withAuth.Verdict);
        Assert.Contains("nerepudiere", withAuth.Headline);

        var dir = _k.Dir; var pdf = Path.Combine(dir, "r.pdf"); File.WriteAllBytes(pdf, original);
        Assert.Throws<InvalidOperationException>(() => OfficerFlows.SignReport(pdf, stranger, trusted, [Sha(original)], trust, "ST-1", Now));

        var byStranger = OfficerFlows.Sign(original, stranger, "ST-1", "r", Now);
        Assert.Equal(OfficialVerdict.Invalid, OfficialReportVerifier.Verify(byStranger, SignedCmsVerifier.Instance, [Sha(original)], trusted, null, trust).Verdict);
        var wrongPaper = OfficialReportVerifier.Verify(OfficerFlows.Sign(original, officer, "ST-1", "r", Now), SignedCmsVerifier.Instance, [Sha(original)], trusted, OfficerFingerprint.Of(stranger), trust);
        Assert.Equal(OfficialVerdict.Invalid, wrongPaper.Verdict);
        Assert.Contains("nu este a ofițerului de pe procesul-verbal", wrongPaper.Headline);

        var revoked = OfficialReportVerifier.Verify(OfficerFlows.Sign(original, officer, "ST-1", "r", Now), SignedCmsVerifier.Instance, [Sha(original)], trusted,
                                                    OfficerFingerprint.Of(officer), new QualifiedSignatureTrust([pki.Ca], [pki.Crl(officer)]));
        Assert.Equal(OfficialVerdict.Invalid, revoked.Verdict);
        Assert.Contains("revocat", revoked.Headline);
    }

    [Fact]
    public void SignReport_writes_a_new_file_and_verifies_it_with_the_same_verifier()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var original = Report();
        var pdf = Path.Combine(_k.Dir, "Raport_control_ST-1.pdf"); File.WriteAllBytes(pdf, original);
        var (path, v) = OfficerFlows.SignReport(pdf, officer, OfficerFlows.Trusted(FirstOfficer(officer)), [Sha(original)], new QualifiedSignatureTrust([pki.Ca], [pki.Crl()]), "ST-1", Now);
        Assert.Equal(original, File.ReadAllBytes(pdf));                                     // the generated report is untouched
        Assert.True(File.Exists(path));
        Assert.Equal(OfficialVerdict.ValidNeoficial, v.Verdict);                            // no paper fingerprint at signing time
        Assert.Equal("Ofiter Test", v.SignerName);
    }

    [Fact]
    public void A_second_officer_is_trusted_only_through_a_request_signed_by_a_registered_officer()
    {
        using var pki = Pki.Create();
        using var first = pki.Issue("Ofiter Unu", nonRepudiation: true);
        using var second = pki.Issue("Ofiter Doi", nonRepudiation: true);
        using var third = pki.Issue("Ofiter Trei", nonRepudiation: true);
        var data = FirstOfficer(first);
        Assert.Throws<InvalidOperationException>(() => OfficerRegistry.RegisterFirst(data, OfficerRegistry.Identity(second), Now));

        var reqPath = Path.Combine(_k.Dir, "cerere.pdf");
        var identity = OfficerRegistry.Identity(second);
        OfficerRequestPdf.Write(reqPath, identity, "ST-1", Now);
        OfficerRegistry.AddRequest(data, identity, reqPath, Now);
        // signed by the candidate itself: refused
        var selfSigned = OfficerFlows.Sign(File.ReadAllBytes(reqPath), second, "ST-1", "aprobare", Now);
        Assert.Throws<InvalidOperationException>(() => OfficerRegistry.CompleteRequest(data, selfSigned, SignedCmsVerifier.Instance, Now));
        // signed by the registered officer: accepted
        var approved = OfficerFlows.Sign(File.ReadAllBytes(reqPath), first, "ST-1", "aprobare", Now);
        var row = OfficerRegistry.CompleteRequest(data, approved, SignedCmsVerifier.Instance, Now);
        Assert.Equal(OfficerRegistry.BasisApproved, row.Basis);
        Assert.Empty(data.Pending);
        Assert.Equal(2, OfficerFlows.Trusted(data).Count);

        // a row added by hand, reusing the approval of officer two for another certificate, is not trusted
        var forged = OfficerRegistry.Identity(third);
        forged.Basis = OfficerRegistry.BasisApproved; forged.ApprovedBy = row.ApprovedBy; forged.ApprovalPdf = row.ApprovalPdf;
        data.Officers.Add(forged);
        var t = OfficerRegistry.Trusted(data.Officers, SignedCmsVerifier.Instance, out var problems);
        Assert.Equal(2, t.Count);
        Assert.Single(problems);
    }

    [Fact]
    public void The_signer_certificate_can_be_taken_from_a_signed_pdf_or_a_certificate_file()
    {
        using var pki = Pki.Create();
        using var officer = pki.Issue("Ofiter Test", nonRepudiation: true);
        var pdf = Path.Combine(_k.Dir, "semnat.pdf");
        File.WriteAllBytes(pdf, OfficerFlows.Sign(Report(), officer, "ST-1", "r", Now));
        Assert.Equal(OfficerFingerprint.Of(officer), OfficerFingerprint.Of(OfficerFlows.CertificateFromFile(pdf)));
        var cer = Path.Combine(_k.Dir, "ofiter.cer");
        File.WriteAllBytes(cer, officer.RawData);
        Assert.Equal(OfficerFingerprint.Of(officer), OfficerFingerprint.Of(OfficerFlows.CertificateFromFile(cer)));
    }

    [Fact]
    public void Generated_report_hashes_come_from_the_sealed_audit()
    {
        var s = new AuthService(Path.Combine(_k.Dir, "auth"));
        s.AuditOperation(null, OperationAudit.ReportGenerated, OperationAudit.ReportGeneratedDetail("a.pdf", new string('a', 64)));
        s.AuditOperation(null, OperationAudit.ReportGenerated, OperationAudit.ReportGeneratedDetail("b.pdf", new string('B', 64)));
        Assert.Equal([new string('A', 64), new string('B', 64)], OfficerFlows.GeneratedReportHashes(s.AuditPath));
        Assert.Empty(OfficerFlows.GeneratedReportHashes(null));
    }

    [Fact]
    public void Qualified_CA_certificates_and_CRLs_are_imported_after_checking_what_they_are()
    {
        using var pki = Pki.Create();
        using var leaf = pki.Issue("Ofiter Test", nonRepudiation: true);
        var dir = Path.Combine(_k.Dir, "trust");
        var ca = Path.Combine(_k.Dir, "ca.cer"); File.WriteAllBytes(ca, pki.Ca.RawData);
        var crl = Path.Combine(_k.Dir, "ca.crl"); File.WriteAllBytes(crl, pki.Crl().Der);
        var notCa = Path.Combine(_k.Dir, "leaf.cer"); File.WriteAllBytes(notCa, leaf.RawData);
        Assert.Equal("certificat CA", QualifiedSignatureTrust.Import(ca, dir));
        Assert.Equal("CRL", QualifiedSignatureTrust.Import(crl, dir));
        Assert.Throws<FormatException>(() => QualifiedSignatureTrust.Import(notCa, dir));
        var t = QualifiedSignatureTrust.FromDirectory(dir);
        Assert.Single(t.Anchors); Assert.Single(t.Crls);
        Assert.Equal(QualifiedCheck.Ok, t.Validate(leaf, Now).Status);
    }
}
