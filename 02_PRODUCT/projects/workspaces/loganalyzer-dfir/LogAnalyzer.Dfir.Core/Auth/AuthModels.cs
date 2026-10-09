using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>Global administrator and operator (decision 17 / 33). The primary administrator is an administrator that may also use a password.</summary>
public enum AuthRole { Operator, Administrator }

public enum SignInMode { Card, Password }

/// <summary>What to do when no current CRL is available for an issuer. Default: Refuse on the classified edition, Warn on the unclassified one.</summary>
public enum MissingCrlPolicy { Refuse, Warn }

/// <summary>Administrator-configurable authentication parameters (stored in policy.json, every change audited).</summary>
public sealed class AuthPolicy
{
    public MissingCrlPolicy MissingCrl { get; set; } = MissingCrlPolicy.Warn;
    /// <summary>Failed password attempts of the primary administrator before a time-limited lockout (3..20).</summary>
    public int LockoutThreshold { get; set; } = 5;
    /// <summary>Session lock after this many minutes without input (1..480).</summary>
    public int IdleLockMinutes { get; set; } = 10;

    public static AuthPolicy DefaultFor(bool classifiedEdition) => new() { MissingCrl = classifiedEdition ? MissingCrlPolicy.Refuse : MissingCrlPolicy.Warn };

    public AuthPolicy Normalised() => new()
    {
        MissingCrl = MissingCrl,
        LockoutThreshold = Math.Clamp(LockoutThreshold, 3, 20),
        IdleLockMinutes = Math.Clamp(IdleLockMinutes, 1, 480),
    };
}

/// <summary>A certificate bound to an account. Public data only: thumbprint, issuer, serial. The private key never leaves the card.</summary>
public sealed class CardBinding
{
    /// <summary>SHA-256 of the DER certificate, lower-case hex.</summary>
    public string Sha256Thumbprint { get; set; } = "";
    public string Issuer { get; set; } = "";
    /// <summary>Certificate serial number, upper-case hex without leading zeros.</summary>
    public string Serial { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTimeOffset EnrolledUtc { get; set; }
    public string EnrolledBy { get; set; } = "";
    public bool Disabled { get; set; }
}

public sealed class AuthAccount
{
    /// <summary>Sign-in name (DOMAIN\user or user); compared case-insensitively and in full. Matches an entry of the users register.</summary>
    public string Account { get; set; } = "";
    public string Person { get; set; } = "";
    public AuthRole Role { get; set; } = AuthRole.Operator;
    /// <summary>The one account that may sign in with a password (decision 33). It can never be disabled, demoted or deleted by the application.</summary>
    public bool IsPrimaryAdmin { get; set; }
    public bool Disabled { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public List<CardBinding> Cards { get; set; } = [];
}

public sealed class AccountsFile
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = "1.0";
    public List<AuthAccount> Accounts { get; set; } = [];
}

/// <summary>Password record of the primary administrator (PBKDF2-SHA256). Stored in its own file so the documented recovery can reset just it.</summary>
public sealed class PasswordRecord
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = "1.0";
    public string Account { get; set; } = "";
    public string Algorithm { get; set; } = "PBKDF2-SHA256";
    public int Iterations { get; set; }
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTimeOffset ChangedUtc { get; set; }
    public int FailedAttempts { get; set; }
    public int Lockouts { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
}

/// <summary>A certificate found on a card (or, in tests, a software stand-in).</summary>
public sealed record CardCertificate(X509Certificate2 Certificate, string Reader, string KeyProvider, bool HasPrivateKey)
{
    public string Sha256Thumbprint => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Certificate.RawData));
    public string SerialHex => CertificateText.Serial(Certificate);
}

public static class CertificateText
{
    public static string Serial(X509Certificate2 c) => c.SerialNumber.TrimStart('0').ToUpperInvariant();
}

public sealed class CardException(string reason, string message) : Exception(message)
{
    public string Reason { get; } = reason;
}

/// <summary>
/// The card behind an interface: the real implementation (Windows smart-card key storage provider, PC/SC reader in the keyboard) lives in
/// LogAnalyzer.Dfir.Windows; tests use software certificates. The PIN is requested by Windows / SafeNet and never reaches this code.
/// </summary>
public interface ICardProvider
{
    /// <summary>Certificates on inserted smart cards. A tag with no certificate (a UID-only RFID badge) produces nothing.</summary>
    IReadOnlyList<CardCertificate> Enumerate();
    /// <summary>Signs <paramref name="data"/> with the card key (SHA-256; RSA PKCS#1 v1.5 or ECDSA DER). Triggers the PIN prompt of Windows / SafeNet.</summary>
    byte[] Sign(CardCertificate card, byte[] data);
    /// <summary>Whether the card of this certificate is still in the reader.</summary>
    bool IsPresent(string sha256Thumbprint);
}

public sealed record AuthResult(bool Ok, string Reason, string Message)
{
    public static AuthResult Success(string message = "") => new(true, "ok", message);
    public static AuthResult Fail(string reason, string message) => new(false, reason, message);
}

public sealed record SignInResult(bool Ok, string Reason, string Message, AuthSession? Session = null, IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<CardCertificate>? Choices = null)
{
    public IReadOnlyList<string> AllWarnings => Warnings ?? [];
}

/// <summary>One signed-in user. Created only by <see cref="AuthService"/>.</summary>
public sealed class AuthSession
{
    internal AuthSession(string account, string person, AuthRole role, bool primary, SignInMode mode, string? cardThumbprint, string? reader, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); Account = account; Person = person; Role = role; IsPrimaryAdmin = primary; Mode = mode;
        CardThumbprint = cardThumbprint; CardReader = reader; SignedInUtc = now; LastActivityUtc = now;
    }

    public Guid Id { get; }
    public string Account { get; }
    public string Person { get; }
    public AuthRole Role { get; internal set; }
    public bool IsPrimaryAdmin { get; }
    public SignInMode Mode { get; }
    public string? CardThumbprint { get; }
    public string? CardReader { get; }
    public DateTimeOffset SignedInUtc { get; }
    public DateTimeOffset LastActivityUtc { get; private set; }
    public bool Locked { get; internal set; }
    public string LockReason { get; internal set; } = "";
    public bool Ended { get; internal set; }
    public bool IsAdministrator => Role == AuthRole.Administrator && !Locked && !Ended;
    /// <summary>"card ab12cd34ef56" / "parolă": how this session was authenticated (for the audit "who" source).</summary>
    public string Method => Mode == SignInMode.Card ? $"card {CardThumbprint?[..Math.Min(12, CardThumbprint.Length)]}" : "parolă";

    public void Touch(DateTimeOffset now) { if (!Locked && !Ended && now > LastActivityUtc) LastActivityUtc = now; }
}
