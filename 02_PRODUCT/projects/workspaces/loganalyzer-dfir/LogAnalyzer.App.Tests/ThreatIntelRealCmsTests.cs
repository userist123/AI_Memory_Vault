using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using Xunit;
namespace LogAnalyzer.App.Tests;

/// <summary>
/// Master plan 6.12 with real cryptography: a package signed with a detached CMS (SignedCms) by an officer certificate issued by a test CA is
/// accepted by the application's own verifier, and the same package signed over other bytes is refused.
/// </summary>
public sealed class ThreatIntelRealCmsTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_ticms_" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void A_real_detached_cms_by_a_trusted_officer_is_accepted_and_a_signature_over_other_bytes_is_refused()
    {
        using var caKey = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=Test TI CA, O=Test", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        caReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caReq.PublicKey, false));
        using var ca = caReq.CreateSelfSigned(Now.AddYears(-1), Now.AddYears(5));
        using var officerKey = RSA.Create(2048);
        var req = new CertificateRequest("CN=Ofiter TI Test, O=Test", officerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));
        using var issued = req.Create(ca, Now.AddYears(-1), Now.AddYears(2), RandomNumberGenerator.GetBytes(8));
        using var officer = issued.CopyWithPrivateKey(officerKey);
        var crl = ParsedCrl.Parse(new CertificateRevocationListBuilder().Build(ca, 1, Now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, Now.AddDays(-1)));
        var trust = new QualifiedSignatureTrust([ca], [crl]);

        var src = Path.Combine(_dir, "pkg");
        Directory.CreateDirectory(Path.Combine(src, "ioc"));
        var ioc = "Type,Value,Context\nip,10.66.66.66,c2 test\n";
        File.WriteAllText(Path.Combine(src, "ioc", "cert.csv"), ioc);
        var json = JsonSerializer.SerializeToUtf8Bytes(new ThreatIntelManifest
        {
            Format = ThreatIntelManifest.FormatId, PackageId = "CERT-REAL", Version = 1, IssuedUtc = Now.AddMinutes(-1), Publisher = "CERT Test",
            Files = [new ThreatIntelFile("ioc/cert.csv", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ioc))))],
        });
        File.WriteAllBytes(Path.Combine(src, ThreatIntelManifest.FileName), json);
        var cms = new SignedCms(new ContentInfo(json), detached: true);
        cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, officer) { IncludeOption = X509IncludeOption.EndCertOnly });
        File.WriteAllBytes(Path.Combine(src, ThreatIntelManifest.SignatureName), cms.Encode());

        var trusted = new[] { OfficerFingerprint.Of(issued) };
        var store = new ThreatIntelStore(Path.Combine(_dir, "store"));
        var (check, staged) = store.Stage(src, "ana", SignedCmsVerifier.Instance, trust, trusted);
        Assert.True(check.Ok, check.Headline + " | " + string.Join(" | ", check.Lines));
        Assert.True(store.Approve(staged!.Id, "ioana", "pachet de test", SignedCmsVerifier.Instance, trust, trusted).Result.Ok);
        Assert.Equal(1, store.LoadActive(SignedCmsVerifier.Instance, trust, trusted).Version);

        // a signature over other bytes (the manifest edited after signing) is refused by the real verifier
        File.WriteAllBytes(Path.Combine(src, ThreatIntelManifest.FileName), JsonSerializer.SerializeToUtf8Bytes(new ThreatIntelManifest
        {
            Format = ThreatIntelManifest.FormatId, PackageId = "CERT-REAL", Version = 2, PreviousVersionSha256 = check.ManifestSha256, IssuedUtc = Now, Publisher = "CERT Test",
            Files = [new ThreatIntelFile("ioc/cert.csv", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ioc))))],
        }));
        Assert.Equal("semnătura nu corespunde manifestului", store.Stage(src, "ana", SignedCmsVerifier.Instance, trust, trusted).Check.Headline);
    }
}
