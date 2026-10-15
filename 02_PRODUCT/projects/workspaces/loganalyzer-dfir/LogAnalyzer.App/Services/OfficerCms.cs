using System;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// CAdES detached signature for PAdES B-B with SignedCms (System.Security.Cryptography.Pkcs from the WPF shared framework, no package): SHA-256, the
    /// signer certificate only, signed attributes content-type and message-digest (added by .NET) plus ESS signing-certificate-v2 (RFC 5035). No signing-time
    /// attribute (PAdES baseline; the claimed time is /M in the PDF). With a smart-card certificate the key never leaves the card: Windows / SafeNet asks for
    /// the PIN while signing; the application never sees it.
    /// </summary>
    // linked into LogAnalyzer.KeyGen as well: internal there, so a project that references both does not see two public types
#if LOGANALYZER_KEYGEN
    internal
#else
    public
#endif
    sealed class SignedCmsSigner(X509Certificate2 certWithKey) : ICmsDetachedSigner
    {
        public const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

        public byte[] Sign(byte[] content)
        {
            var cms = new SignedCms(new ContentInfo(content), detached: true);
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certWithKey)
            {
                DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),   // SHA-256
                IncludeOption = X509IncludeOption.EndCertOnly,
            };
            signer.SignedAttributes.Add(new AsnEncodedData(new Oid(SigningCertificateV2Oid), SigningCertificateV2(certWithKey)));
            cms.ComputeSignature(signer, silent: false);
            return cms.Encode();
        }

        /// <summary>SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 }; ESSCertIDv2 = { certHash (SHA-256, default algorithm omitted), issuerSerial }.</summary>
        public static byte[] SigningCertificateV2(X509Certificate2 cert)
        {
            var w = new AsnWriter(AsnEncodingRules.DER);
            using (w.PushSequence())
            using (w.PushSequence())
            using (w.PushSequence())
            {
                w.WriteOctetString(SHA256.HashData(cert.RawData));
                using (w.PushSequence())
                {
                    using (w.PushSequence())
                    using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true)))
                        w.WriteEncodedValue(cert.IssuerName.RawData);
                    w.WriteInteger(cert.SerialNumberBytes.Span);
                }
            }
            return w.Encode();
        }
    }

    /// <summary>Detached CMS verification with SignedCms: exactly one signer, its certificate present, signature and message digest checked over the content.</summary>
    // linked into LogAnalyzer.KeyGen as well: internal there, so a project that references both does not see two public types
#if LOGANALYZER_KEYGEN
    internal
#else
    public
#endif
    sealed class SignedCmsVerifier : ICmsDetachedVerifier
    {
        public static SignedCmsVerifier Instance { get; } = new();

        public CmsResult Verify(byte[] cms, byte[] content)
        {
            try
            {
                var signed = new SignedCms(new ContentInfo(content), detached: true);
                signed.Decode(cms);
                if (signed.SignerInfos.Count != 1) return new CmsResult(false, null, "semnătura trebuie să aibă exact un semnatar");
                var si = signed.SignerInfos[0];
                if (si.Certificate is null) return new CmsResult(false, null, "semnătura nu conține certificatul semnatarului");
                si.CheckSignature(verifySignatureOnly: true);
                return new CmsResult(true, X509CertificateLoader.LoadCertificate(si.Certificate.RawData), "");
            }
            catch (CryptographicException ex) { return new CmsResult(false, null, ex.Message); }
        }
    }
}
