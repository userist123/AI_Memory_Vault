using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>A parsed X.509 CRL. The signature is checked separately against the issuer certificate; nothing is fetched from anywhere.</summary>
public sealed class ParsedCrl
{
    public byte[] IssuerRaw { get; init; } = [];
    public DateTimeOffset ThisUpdate { get; init; }
    public DateTimeOffset? NextUpdate { get; init; }
    public HashSet<string> RevokedSerials { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsDelta { get; init; }
    internal byte[] Tbs { get; init; } = [];
    /// <summary>The DER encoding (a PEM input is converted), as stored in the CRL folder.</summary>
    public byte[] Der { get; init; } = [];
    internal string SignatureOid { get; init; } = "";
    internal byte[] Signature { get; init; } = [];

    private const string Rsa256 = "1.2.840.113549.1.1.11", Rsa384 = "1.2.840.113549.1.1.12", Rsa512 = "1.2.840.113549.1.1.13";
    private const string Ec256 = "1.2.840.10045.4.3.2", Ec384 = "1.2.840.10045.4.3.3", Ec512 = "1.2.840.10045.4.3.4";

    /// <summary>DER or PEM ("X509 CRL"). Throws <see cref="FormatException"/> with a readable reason when it is not a CRL.</summary>
    public static ParsedCrl Parse(byte[] data)
    {
        try { var der = Unpem(data); var crl = ParseDer(der); return crl.WithDer(der); }
        catch (Exception ex) when (ex is AsnContentException or InvalidOperationException or CryptographicException or ArgumentException or InvalidCastException)
        { throw new FormatException("fișierul nu este o listă de revocare (CRL) validă: " + ex.Message, ex); }
    }

    private static byte[] Unpem(byte[] data)
    {
        var text = System.Text.Encoding.ASCII.GetString(data);
        if (text.Contains("-----BEGIN", StringComparison.Ordinal) && PemEncoding.TryFind(text, out var f) && text[f.Label].Contains("CRL", StringComparison.Ordinal))
            return Convert.FromBase64String(text[f.Base64Data].ToString());
        return data;
    }

    private ParsedCrl WithDer(byte[] der) => new() { IssuerRaw = IssuerRaw, ThisUpdate = ThisUpdate, NextUpdate = NextUpdate, RevokedSerials = RevokedSerials, IsDelta = IsDelta, Tbs = Tbs, SignatureOid = SignatureOid, Signature = Signature, Der = der };

    private static DateTimeOffset ReadTime(AsnReader r) =>
        r.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) ? r.ReadUtcTime() : r.ReadGeneralizedTime();

    private static bool IsTime(AsnReader r) =>
        r.HasData && (r.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) || r.PeekTag().HasSameClassAndValue(Asn1Tag.GeneralizedTime));

    private static ParsedCrl ParseDer(byte[] der)
    {
        var outer = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
        var tbsEncoded = outer.ReadEncodedValue();
        var sigAlg = outer.ReadSequence();
        var oid = sigAlg.ReadObjectIdentifier();
        var signature = outer.ReadBitString(out _);
        outer.ThrowIfNotEmpty();

        var tbs = new AsnReader(tbsEncoded, AsnEncodingRules.DER).ReadSequence();
        if (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Integer)) tbs.ReadInteger();            // version
        tbs.ReadSequence();                                                                    // signature algorithm (must equal the outer one; the outer one is verified)
        var issuer = tbs.ReadEncodedValue().ToArray();
        var thisUpdate = ReadTime(tbs);
        DateTimeOffset? next = IsTime(tbs) ? ReadTime(tbs) : null;
        var revoked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
        {
            var list = tbs.ReadSequence();
            while (list.HasData)
            {
                var entry = list.ReadSequence();
                revoked.Add(SerialText(entry.ReadIntegerBytes().Span));
            }
        }
        bool delta = false;
        if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
        {
            var exts = tbs.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
            while (exts.HasData)
            {
                var ext = exts.ReadSequence();
                if (ext.ReadObjectIdentifier() == "2.5.29.27") delta = true;                   // deltaCRLIndicator
            }
        }
        return new ParsedCrl { IssuerRaw = issuer, ThisUpdate = thisUpdate, NextUpdate = next, RevokedSerials = revoked, IsDelta = delta, Tbs = tbsEncoded.ToArray(), SignatureOid = oid, Signature = signature };
    }

    /// <summary>Upper-case hex of a big-endian integer without leading zeros (the same form as <see cref="CertificateText.Serial"/>).</summary>
    internal static string SerialText(ReadOnlySpan<byte> bigEndian)
    {
        var hex = Convert.ToHexString(bigEndian).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    /// <summary>True only if this CRL was signed by <paramref name="issuer"/>'s key with a SHA-256/384/512 RSA PKCS#1 or ECDSA signature.</summary>
    public bool VerifySignature(X509Certificate2 issuer)
    {
        var hash = SignatureOid switch
        {
            Rsa256 or Ec256 => HashAlgorithmName.SHA256,
            Rsa384 or Ec384 => HashAlgorithmName.SHA384,
            Rsa512 or Ec512 => HashAlgorithmName.SHA512,
            _ => default,
        };
        if (hash == default) return false;                                                   // SHA-1, MD5, PSS: not accepted
        if (SignatureOid.StartsWith("1.2.840.113549.1.1.", StringComparison.Ordinal))
        {
            using var rsa = issuer.GetRSAPublicKey();
            return rsa is not null && rsa.VerifyData(Tbs, Signature, hash, RSASignaturePadding.Pkcs1);
        }
        using var ec = issuer.GetECDsaPublicKey();
        return ec is not null && ec.VerifyData(Tbs, Signature, hash, DSASignatureFormat.Rfc3279DerSequence);
    }
}

public sealed record CertValidation(bool Ok, string Reason, string Message, IReadOnlyList<string> Warnings)
{
    public static CertValidation Pass(IReadOnlyList<string> warnings) => new(true, "ok", "certificat valid", warnings);
    public static CertValidation Fail(string reason, string message, IReadOnlyList<string>? warnings = null) => new(false, reason, message, warnings ?? []);
}

/// <summary>
/// Offline certificate trust (decision 33): the chain must end at a CA certificate imported by the administrator (custom root trust, never the Windows
/// root store) and every certificate of the chain must be absent from the administrator-imported CRLs. Nothing is downloaded: chain building has
/// downloads switched off, and revocation is read from the imported CRLs (signature checked against the issuer, validity window checked). The .NET
/// X509 APIs do the chain, signature and time checks; this class adds only the offline CRL lookup.
/// </summary>
public sealed class CertificateTrust(string trustDir, string crlDir, Func<MissingCrlPolicy> missingCrl, Func<DateTimeOffset>? clock = null)
{
    private const string ClientAuth = "1.3.6.1.5.5.7.3.2", SmartCardLogon = "1.3.6.1.4.1.311.20.2.2";
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public string TrustDir { get; } = trustDir;
    public string CrlDir { get; } = crlDir;

    public static X509Certificate2 LoadCertificate(byte[] data)
    {
        var text = System.Text.Encoding.ASCII.GetString(data);
        if (text.Contains("-----BEGIN CERTIFICATE", StringComparison.Ordinal)) return X509Certificate2.CreateFromPem(text);
        return X509CertificateLoader.LoadCertificate(data);
    }

    public static bool IsSelfSigned(X509Certificate2 c) => c.SubjectName.RawData.AsSpan().SequenceEqual(c.IssuerName.RawData);

    public List<X509Certificate2> LoadAnchors()
    {
        var list = new List<X509Certificate2>();
        if (!Directory.Exists(TrustDir)) return list;
        foreach (var f in Directory.EnumerateFiles(TrustDir).Order(StringComparer.Ordinal))
        {
            try { list.Add(LoadCertificate(File.ReadAllBytes(f))); }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or FormatException) { }   // an unreadable file is not trusted
        }
        return list;
    }

    public List<ParsedCrl> LoadCrls()
    {
        var list = new List<ParsedCrl>();
        if (!Directory.Exists(CrlDir)) return list;
        foreach (var f in Directory.EnumerateFiles(CrlDir).Order(StringComparer.Ordinal))
        {
            try { list.Add(ParsedCrl.Parse(File.ReadAllBytes(f))); }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    public CertValidation Validate(X509Certificate2 leaf)
    {
        var now = _clock();
        var warnings = new List<string>();
        var imported = LoadAnchors();
        if (imported.Count == 0)
            return CertValidation.Fail("untrusted", "nu este importat niciun certificat al autorității de certificare (CA): administratorul trebuie să importe CA-ul organizației");

        // Anchors: self-signed roots, and imported certificates whose issuer is not imported (the top of what the administrator trusts).
        bool HasIssuerImported(X509Certificate2 c) => imported.Any(o => !ReferenceEquals(o, c) && o.SubjectName.RawData.AsSpan().SequenceEqual(c.IssuerName.RawData));
        using var chain = new X509Chain();
        var p = chain.ChainPolicy;
        p.TrustMode = X509ChainTrustMode.CustomRootTrust;
        p.RevocationMode = X509RevocationMode.NoCheck;                  // revocation is read from the imported CRLs below; no network in any edition
        p.DisableCertificateDownloads = true;
        p.VerificationTime = now.UtcDateTime;
        foreach (var c in imported)
        {
            if (IsSelfSigned(c) || !HasIssuerImported(c)) p.CustomTrustStore.Add(c); else p.ExtraStore.Add(c);
        }
        if (!chain.Build(leaf))
        {
            var st = chain.ChainStatus.Select(s => s.Status).Aggregate(X509ChainStatusFlags.NoError, (a, b) => a | b);
            if (st.HasFlag(X509ChainStatusFlags.NotTimeValid))
            {
                var bad = chain.ChainElements.Select(e => e.Certificate).FirstOrDefault(c => now < c.NotBefore || now > c.NotAfter) ?? leaf;
                return now < bad.NotBefore
                    ? CertValidation.Fail("not_yet_valid", $"certificatul „{bad.Subject}” nu este încă valabil (de la {bad.NotBefore:yyyy-MM-dd})")
                    : CertValidation.Fail("expired", $"certificatul „{bad.Subject}” a expirat la {bad.NotAfter:yyyy-MM-dd}");
            }
            if (st.HasFlag(X509ChainStatusFlags.UntrustedRoot) || st.HasFlag(X509ChainStatusFlags.PartialChain) || st.HasFlag(X509ChainStatusFlags.NotSignatureValid))
                return CertValidation.Fail("untrusted", "lanțul certificatului nu ajunge la un CA importat de administrator (lanț incomplet sau semnătură invalidă)");
            return CertValidation.Fail("untrusted", "lanț de certificate invalid: " + string.Join("; ", chain.ChainStatus.Select(s => s.Status.ToString())));
        }

        // Key usage / EKU of the leaf (only when the extensions are present).
        var ku = leaf.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (ku is not null && !ku.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
            return CertValidation.Fail("key_usage", "certificatul nu are utilizarea „semnătură digitală”");
        var eku = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (eku is not null && !eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => o.Value is ClientAuth or SmartCardLogon))
            return CertValidation.Fail("key_usage", "certificatul nu permite autentificarea clientului / smart card logon");

        // Offline revocation: every certificate that has an issuer in the chain.
        var crls = LoadCrls();
        var elements = chain.ChainElements.Select(e => e.Certificate).ToList();
        for (int i = 0; i < elements.Count - 1; i++)
        {
            var cert = elements[i]; var issuer = elements[i + 1];
            var mine = crls.Where(c => c.IssuerRaw.AsSpan().SequenceEqual(issuer.SubjectName.RawData) && !c.IsDelta && c.VerifySignature(issuer)).ToList();
            var current = mine.Where(c => c.ThisUpdate <= now && (c.NextUpdate is null || c.NextUpdate >= now)).ToList();
            if (mine.SelectMany(c => c.RevokedSerials).Contains(CertificateText.Serial(cert)))
                return CertValidation.Fail("revoked", $"certificatul „{cert.Subject}” (serie {CertificateText.Serial(cert)}) este revocat");
            if (current.Count == 0)
            {
                var why = mine.Count == 0 ? $"nu există nicio CRL importată pentru emitentul „{issuer.Subject}”" : $"CRL-ul emitentului „{issuer.Subject}” a expirat (nextUpdate {mine.Max(c => c.NextUpdate):yyyy-MM-dd HH:mm}Z)";
                if (missingCrl() == MissingCrlPolicy.Refuse)
                    return CertValidation.Fail("crl_missing", why + "; politica refuză autentificarea fără CRL curent", warnings);
                warnings.Add(why + "; politica permite autentificarea cu avertisment");
            }
        }
        return CertValidation.Pass(warnings);
    }
}
