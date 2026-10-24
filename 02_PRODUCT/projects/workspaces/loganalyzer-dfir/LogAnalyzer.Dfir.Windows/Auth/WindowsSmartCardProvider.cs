using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Windows.Auth;

/// <summary>
/// The contact smart card in the keyboard's smart-card slot (decision 33), through Windows smart-card support only: the SafeNet minidriver / smart-card
/// key storage provider and the built-in PC/SC reader. Certificates come from the CURRENT USER "My" store (the Windows Certificate Propagation service
/// puts the certificates of an inserted card there); only certificates whose private key lives on a smart card are offered. The PIN is requested by
/// Windows / SafeNet while the key signs; this class never receives, stores or logs it, and changes nothing on the host.
/// </summary>
public sealed class WindowsSmartCardProvider : ICardProvider
{
    public IReadOnlyList<CardCertificate> Enumerate()
    {
        var list = new List<CardCertificate>();
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var cert in store.Certificates)
            {
                var info = Describe(cert);
                if (info is null) { cert.Dispose(); continue; }
                list.Add(new CardCertificate(cert, info.Value.Reader, info.Value.Provider, HasPrivateKey: true));
            }
        }
        catch (CryptographicException ex)
        {
            throw new CardException("no_card", "magazinul de certificate nu poate fi citit: " + ex.Message);
        }
        return list;
    }

    /// <summary>Signs with the card key; Windows / SafeNet shows the PIN prompt. A cancelled or wrong PIN surfaces as a CryptographicException (mapped by the caller).</summary>
    public byte[] Sign(CardCertificate card, byte[] data) => CardSignature.Sign(card.Certificate, data);

    public bool IsPresent(string sha256Thumbprint)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var cert in store.Certificates)
            {
                using (cert)
                {
                    if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(cert.RawData)), sha256Thumbprint, StringComparison.Ordinal)) continue;
                    return Describe(cert) is not null;      // the key must still be reachable on a card in a reader
                }
            }
        }
        catch (CryptographicException) { }
        return false;
    }

    private const string ReaderProperty = "SmartCardReader";      // NCRYPT_READER_PROPERTY

    /// <summary>Reader and provider when the certificate's private key is on a smart card that is in a reader now; null otherwise.</summary>
    private static (string Reader, string Provider)? Describe(X509Certificate2 cert)
    {
        if (!cert.HasPrivateKey) return null;
        try
        {
            using var rsa = cert.GetRSAPrivateKey();
            if (rsa is RSACng rc) return FromCng(rc.Key);
            if (rsa is RSACryptoServiceProvider csp) return FromCsp(csp.CspKeyContainerInfo);
            using var ec = cert.GetECDsaPrivateKey();
            if (ec is ECDsaCng ecc) return FromCng(ecc.Key);
        }
        catch (CryptographicException) { }       // card removed, key not reachable
        return null;
    }

    private static (string, string)? FromCng(CngKey key)
    {
        var provider = key.Provider?.Provider ?? "";
        string? reader = null;
        try
        {
            if (key.HasProperty(ReaderProperty, CngPropertyOptions.None))
                reader = Encoding.Unicode.GetString(key.GetProperty(ReaderProperty, CngPropertyOptions.None).GetValue() ?? []).TrimEnd('\0');
        }
        catch (CryptographicException) { return null; }
        bool smartProvider = provider.Contains("Smart Card", StringComparison.OrdinalIgnoreCase) || provider.Contains("SafeNet", StringComparison.OrdinalIgnoreCase) ||
                             provider.Contains("eToken", StringComparison.OrdinalIgnoreCase);
        if (reader is null && !smartProvider) return null;      // a software key: not a card
        return (string.IsNullOrWhiteSpace(reader) ? "(cititor necunoscut)" : reader, provider);
    }

    private static (string, string)? FromCsp(CspKeyContainerInfo info)
    {
        if (!info.Removable) return null;                      // a CAPI key on a removable device = a token / smart card
        var name = info.KeyContainerName ?? "";
        var reader = name.StartsWith(@"\\.\", StringComparison.Ordinal) && name.LastIndexOf('\\') > 3 ? name[4..name.LastIndexOf('\\')] : "(cititor necunoscut)";
        return (reader, info.ProviderName ?? "");
    }
}
