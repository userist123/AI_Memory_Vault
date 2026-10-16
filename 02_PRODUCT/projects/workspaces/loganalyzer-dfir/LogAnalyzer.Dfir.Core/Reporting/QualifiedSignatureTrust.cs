using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Reporting;

public enum QualifiedCheck { Ok, Unconfirmed, Failed }

public sealed record QualifiedValidation(QualifiedCheck Status, string Message);

/// <summary>
/// Offline checks of the security officer's qualified signature certificate: non-repudiation key usage, validity at the signing time, chain to a qualified
/// CA certificate the officer imported (custom root trust, never the Windows root store, downloads off) and revocation in imported CRLs (signature of the CRL
/// checked against the issuer). Separate from the sign-in trust (<see cref="CertificateTrust"/>), which requires client-authentication certificates.
/// A missing CA or a missing / outdated CRL is reported as "cannot confirm", never as valid.
/// </summary>
public sealed class QualifiedSignatureTrust(IReadOnlyList<X509Certificate2> anchors, IReadOnlyList<ParsedCrl> crls)
{
    public static QualifiedSignatureTrust Empty { get; } = new([], []);

    public IReadOnlyList<X509Certificate2> Anchors { get; } = anchors;
    public IReadOnlyList<ParsedCrl> Crls { get; } = crls;

    /// <summary>%PROGRAMDATA%\LogAnalyzer\officer_trust\{ca,crl}: the qualified CA certificates and CRLs imported on this station.</summary>
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "officer_trust");

    public static QualifiedSignatureTrust FromDirectory(string? dir = null)
    {
        dir ??= DefaultDir;
        var t = new CertificateTrust(Path.Combine(dir, "ca"), Path.Combine(dir, "crl"), () => MissingCrlPolicy.Refuse);
        return new QualifiedSignatureTrust(t.LoadAnchors(), t.LoadCrls());
    }

    /// <summary>
    /// Copies a CA certificate or a CRL file into the trust folder after checking what it is. Returns "certificat CA" or "CRL"; throws <see cref="FormatException"/> otherwise.
    /// </summary>
    public static string Import(string file, string? dir = null)
    {
        dir ??= DefaultDir;
        var data = File.ReadAllBytes(file);
        try
        {
            var crl = ParsedCrl.Parse(data);
            Directory.CreateDirectory(Path.Combine(dir, "crl"));
            File.WriteAllBytes(Path.Combine(dir, "crl", Convert.ToHexStringLower(SHA256.HashData(crl.Der))[..16] + ".crl"), crl.Der);
            return "CRL";
        }
        catch (FormatException) { }
        X509Certificate2 cert;
        try { cert = CertificateTrust.LoadCertificate(data); }
        catch (CryptographicException ex) { throw new FormatException("fișierul nu este nici certificat, nici listă de revocare (CRL): " + ex.Message, ex); }
        using (cert)
        {
            var bc = cert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (bc is null || !bc.CertificateAuthority) throw new FormatException($"„{cert.Subject}” nu este un certificat de autoritate de certificare (CA).");
            Directory.CreateDirectory(Path.Combine(dir, "ca"));
            File.WriteAllBytes(Path.Combine(dir, "ca", Convert.ToHexStringLower(SHA256.HashData(cert.RawData))[..16] + ".cer"), cert.RawData);
        }
        return "certificat CA";
    }

    /// <summary>
    /// What the certificate declares about itself (ETSI EN 319 412-5 QcStatements, OID 1.3.6.1.5.5.7.1.3): "calificată" only with QcCompliance (0.4.0.1862.1.1)
    /// and QcSSCD (0.4.0.1862.1.4); otherwise "avansată". The label does not change the verdict; it keeps the wording honest.
    /// </summary>
    public static string QualifiedLabel(X509Certificate2 cert)
    {
        var ids = new HashSet<string>();
        try
        {
            var ext = cert.Extensions.Cast<X509Extension>().FirstOrDefault(e => e.Oid?.Value == "1.3.6.1.5.5.7.1.3");
            if (ext is not null)
            {
                var r = new System.Formats.Asn1.AsnReader(ext.RawData, System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence();
                while (r.HasData) { var st = r.ReadSequence(); ids.Add(st.ReadObjectIdentifier()); }
            }
        }
        catch (System.Formats.Asn1.AsnContentException) { ids.Clear(); }
        return ids.Contains("0.4.0.1862.1.1") && ids.Contains("0.4.0.1862.1.4")
            ? "semnătură electronică calificată (certificatul declară QcCompliance și dispozitiv calificat QcSSCD)"
            : "semnătură electronică avansată (certificatul nu declară că e calificat)";
    }

    public static bool HasNonRepudiation(X509Certificate2 cert) =>
        cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is { } ku && ku.KeyUsages.HasFlag(X509KeyUsageFlags.NonRepudiation);

    /// <summary>The certificates usable for the officer's signature: private key available, non-repudiation, valid now.</summary>
    public static IEnumerable<X509Certificate2> SigningCandidates(IEnumerable<X509Certificate2> certs, DateTimeOffset nowUtc) =>
        certs.Where(c => c.HasPrivateKey && HasNonRepudiation(c) && c.NotBefore.ToUniversalTime() <= nowUtc.UtcDateTime && c.NotAfter.ToUniversalTime() >= nowUtc.UtcDateTime);

    public QualifiedValidation Validate(X509Certificate2 leaf, DateTimeOffset signedUtc)
    {
        if (!HasNonRepudiation(leaf))
            return new(QualifiedCheck.Failed, "certificatul nu are utilizarea „nerepudiere” (non-repudiation): nu este un certificat de semnătură calificată");
        var at = signedUtc.UtcDateTime;
        if (at < leaf.NotBefore.ToUniversalTime() || at > leaf.NotAfter.ToUniversalTime())
            return new(QualifiedCheck.Failed, $"certificatul nu era valabil la data semnării ({signedUtc:yyyy-MM-dd HH:mm} UTC; valabil {leaf.NotBefore:yyyy-MM-dd} – {leaf.NotAfter:yyyy-MM-dd})");
        if (Anchors.Count == 0)
            return new(QualifiedCheck.Unconfirmed, "nu este importat niciun certificat al autorității de certificare calificate (CA), deci lanțul certificatului nu se poate confirma");

        bool HasIssuerImported(X509Certificate2 c) => Anchors.Any(o => !ReferenceEquals(o, c) && o.SubjectName.RawData.AsSpan().SequenceEqual(c.IssuerName.RawData));
        using var chain = new X509Chain();
        var p = chain.ChainPolicy;
        p.TrustMode = X509ChainTrustMode.CustomRootTrust;
        p.RevocationMode = X509RevocationMode.NoCheck;   // revocation from the imported CRLs below; nothing is downloaded
        p.DisableCertificateDownloads = true;
        p.VerificationTime = at;
        foreach (var c in Anchors) { if (CertificateTrust.IsSelfSigned(c) || !HasIssuerImported(c)) p.CustomTrustStore.Add(c); else p.ExtraStore.Add(c); }
        if (!chain.Build(leaf))
            return new(QualifiedCheck.Failed, "lanțul certificatului nu ajunge la un CA calificat importat (" + string.Join(", ", chain.ChainStatus.Select(s => s.Status.ToString())) + ")");

        var elements = chain.ChainElements.Select(e => e.Certificate).ToList();
        var unconfirmed = new List<string>();
        for (int i = 0; i < elements.Count - 1; i++)
        {
            var cert = elements[i]; var issuer = elements[i + 1];
            var mine = Crls.Where(c => c.IssuerRaw.AsSpan().SequenceEqual(issuer.SubjectName.RawData) && !c.IsDelta && c.VerifySignature(issuer)).ToList();
            if (mine.SelectMany(c => c.RevokedSerials).Contains(CertificateText.Serial(cert)))
                return new(QualifiedCheck.Failed, $"certificatul „{cert.Subject}” (serie {CertificateText.Serial(cert)}) este revocat în lista de revocare importată");
            // a CRL that does not list the certificate and was in force at the signing time (or was issued after it) shows it was not revoked when it signed
            if (!mine.Any(c => c.ThisUpdate >= signedUtc || (c.ThisUpdate <= signedUtc && (c.NextUpdate is null || c.NextUpdate >= signedUtc))))
                unconfirmed.Add(mine.Count == 0 ? $"nu există nicio listă de revocare (CRL) importată pentru emitentul „{issuer.Subject}”"
                                                : $"lista de revocare (CRL) a emitentului „{issuer.Subject}” expirase la data semnăturii (valabilă până la {mine.Max(c => c.NextUpdate):yyyy-MM-dd HH:mm} UTC)");
        }
        if (unconfirmed.Count > 0) return new(QualifiedCheck.Unconfirmed, "nu se poate confirma revocarea: " + string.Join("; ", unconfirmed));
        return new(QualifiedCheck.Ok, "lanț până la un CA calificat importat, nerevocat la data semnării");
    }
}
