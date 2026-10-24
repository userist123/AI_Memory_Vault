using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>Verification of a card signature with the certificate's public key (RSA PKCS#1 v1.5 or ECDSA DER, SHA-256). BCL primitives only.</summary>
public static class CardSignature
{
    public static byte[] Sign(X509Certificate2 certWithKey, byte[] data)
    {
        using var rsa = certWithKey.GetRSAPrivateKey();
        if (rsa is not null) return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var ec = certWithKey.GetECDsaPrivateKey();
        if (ec is not null) return ec.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        throw new CardException("no_private_key", "certificatul nu are cheie privată RSA sau ECDSA");
    }

    public static bool Verify(X509Certificate2 cert, byte[] data, byte[] signature)
    {
        try
        {
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is not null) return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var ec = cert.GetECDsaPublicKey();
            if (ec is not null) return ec.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException) { }
        return false;
    }
}
