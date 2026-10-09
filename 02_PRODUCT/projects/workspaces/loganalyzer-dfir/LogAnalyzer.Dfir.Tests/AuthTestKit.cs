using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Throw-away software certificates for the authentication tests: generated in memory, never written with a private key, never committed.</summary>
public sealed class TestCa : IDisposable
{
    public X509Certificate2 Cert { get; }
    private readonly RSA _key;
    private long _serial = 1000;

    public TestCa(string name, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        _key = RSA.Create(2048);
        var req = new CertificateRequest("CN=" + name, _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        Cert = req.CreateSelfSigned(notBefore, notAfter);
    }

    public byte[] PublicDer => Cert.RawData;

    /// <summary>A user certificate with a software private key (stands in for the key on the card).</summary>
    public X509Certificate2 Issue(string cn, DateTimeOffset notBefore, DateTimeOffset notAfter, bool ec = false, bool clientAuth = true, bool digitalSignature = true)
    {
        AsymmetricAlgorithm key = ec ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : RSA.Create(2048);
        var req = ec
            ? new CertificateRequest("CN=" + cn, (ECDsa)key, HashAlgorithmName.SHA256)
            : new CertificateRequest("CN=" + cn, (RSA)key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(digitalSignature ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment, true));
        if (clientAuth) req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2"), new Oid("1.3.6.1.4.1.311.20.2.2")], false));
        var serial = BitConverter.GetBytes(++_serial).Reverse().ToArray();
        using var pub = req.Create(Cert.SubjectName, X509SignatureGenerator.CreateForRSA(_key, RSASignaturePadding.Pkcs1), notBefore, notAfter, serial);
        return ec ? pub.CopyWithPrivateKey((ECDsa)key) : pub.CopyWithPrivateKey((RSA)key);
    }

    public byte[] Crl(DateTimeOffset thisUpdate, DateTimeOffset nextUpdate, long number, params X509Certificate2[] revoked)
    {
        var b = new CertificateRevocationListBuilder();
        foreach (var r in revoked) b.AddEntry(r, thisUpdate);
        var issuerWithKey = Cert;   // self-signed, still holds its in-memory key
        return b.Build(issuerWithKey, new BigInteger(number), nextUpdate, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, thisUpdate);
    }

    public void Dispose() { _key.Dispose(); Cert.Dispose(); }
}

/// <summary>A card in a "reader" made of software: it holds the certificates with their keys, can be removed, and counts the PIN prompts (signatures).</summary>
public sealed class SoftwareCardProvider : ICardProvider
{
    private readonly List<(X509Certificate2 Cert, bool HasKey)> _cards = [];
    public int SignCalls { get; private set; }
    /// <summary>The user cancels the PIN prompt / types a wrong PIN.</summary>
    public bool PinCancelled { get; set; }
    /// <summary>Sign with this certificate's key instead (a card that does not hold the key of the presented certificate).</summary>
    public X509Certificate2? SignWithOther { get; set; }

    public void ResetCalls() => SignCalls = 0;
    public void Insert(X509Certificate2 cert, bool hasKey = true) => _cards.Add((cert, hasKey));
    public void Remove(X509Certificate2 cert) => _cards.RemoveAll(c => c.Cert.Thumbprint == cert.Thumbprint);
    public void RemoveAll() => _cards.Clear();

    public IReadOnlyList<CardCertificate> Enumerate() => _cards.Select(c => new CardCertificate(c.Cert, "Test Keyboard Reader 0", "Software", c.HasKey)).ToList();

    public byte[] Sign(CardCertificate card, byte[] data)
    {
        SignCalls++;
        if (PinCancelled) throw new CardException("pin_cancelled", "PIN anulat");
        return CardSignature.Sign(SignWithOther ?? card.Certificate, data);
    }

    public bool IsPresent(string sha256Thumbprint) => _cards.Any(c => Convert.ToHexStringLower(SHA256.HashData(c.Cert.RawData)) == sha256Thumbprint);
}
