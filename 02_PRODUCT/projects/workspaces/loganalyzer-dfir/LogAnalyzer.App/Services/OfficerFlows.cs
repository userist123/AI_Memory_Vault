using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// The security officer's operations without UI (the view model asks for files and choices and calls these): sign a generated report with the qualified
    /// certificate (PAdES B-B, one incremental update), verify a signed report (ours or Adobe's), read the hashes of generated reports from the sealed audit,
    /// and find the qualified signing certificates on the inserted smart card.
    /// </summary>
    public static class OfficerFlows
    {
        public static IReadOnlyList<SecurityOfficerRow> Trusted(OfficerStoreData data) => OfficerRegistry.Trusted(data.Officers, SignedCmsVerifier.Instance, out _);

        /// <summary>The SHA-256 of every report the application generated, as recorded in the sealed audit ("report.generated").</summary>
        public static List<string> GeneratedReportHashes(string? auditPath) =>
            auditPath is null ? [] : LogAnalyzer.Dfir.Case.AuditAnchor.EventDetails(auditPath, OperationAudit.ReportGenerated)
                .Select(d => Regex.Match(d, @"sha256=([0-9A-Fa-f]{64})")).Where(m => m.Success).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToList();

        /// <summary>The certificates on the inserted card(s) usable for the officer's signature: key on the card, non-repudiation, valid now.</summary>
        public static List<X509Certificate2> CardSigningCertificates(ICardProvider cards, DateTimeOffset nowUtc) =>
            QualifiedSignatureTrust.SigningCandidates(cards.Enumerate().Select(c => c.Certificate), nowUtc).ToList();

        public static string SignedPathFor(string reportPdf, DateTimeOffset nowUtc) =>
            Path.Combine(Path.GetDirectoryName(reportPdf)!, Path.GetFileNameWithoutExtension(reportPdf) + $"_semnat_ofiter_{nowUtc:yyyyMMdd_HHmmss}.pdf");

        /// <summary>
        /// Signs <paramref name="pdfPath"/> (a report generated here) with <paramref name="cert"/> as a new file next to it, then runs the same verification as for an
        /// Adobe-signed file. Refuses a certificate that is not a trusted registered officer. The original file is not changed.
        /// </summary>
        public static (string SignedPath, OfficialVerification Verification) SignReport(string pdfPath, X509Certificate2 cert, IReadOnlyList<SecurityOfficerRow> trusted,
            IReadOnlyCollection<string> reportHashes, QualifiedSignatureTrust trust, string location, DateTimeOffset nowUtc)
        {
            var fp = OfficerFingerprint.Of(cert);
            if (!trusted.Any(t => OfficerFingerprint.Same(t.Fingerprint, fp)))
                throw new InvalidOperationException($"Certificatul „{OfficerFingerprint.CommonName(cert)}” (amprenta {fp}) nu aparține unui ofițer de securitate înregistrat. Înregistrați-l întâi.");
            var signed = Sign(File.ReadAllBytes(pdfPath), cert, location, "Raport de control semnat de ofiterul de securitate", nowUtc);
            var outPath = SignedPathFor(pdfPath, nowUtc);
            File.WriteAllBytes(outPath, signed);
            return (outPath, OfficialReportVerifier.Verify(signed, SignedCmsVerifier.Instance, reportHashes, trusted, null, trust));
        }

        public static byte[] Sign(byte[] pdf, X509Certificate2 cert, string location, string reason, DateTimeOffset nowUtc) =>
            PdfIncrementalSigner.Sign(pdf, new SignatureAppearance(OfficerFingerprint.CommonName(cert), cert.GetNameInfo(X509NameType.SimpleName, forIssuer: true), nowUtc, location, reason),
                                      new SignedCmsSigner(cert));

        /// <summary>A certificate from a file: .cer / .crt / .der / .pem, or the signer certificate of a signed PDF (e.g. a document the officer signed in Adobe).</summary>
        public static X509Certificate2 CertificateFromFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan().StartsWith("%PDF-"u8))
            {
                var pdf = PdfSignatureCheck.Check(bytes, SignedCmsVerifier.Instance);
                if (pdf.Problem is { } why || pdf.Signer is null) throw new InvalidOperationException("Din PDF nu se poate lua certificatul semnatarului: " + (pdf.Problem ?? "semnătură invalidă"));
                return pdf.Signer;
            }
            return CertificateTrust.LoadCertificate(bytes);
        }
    }
}
