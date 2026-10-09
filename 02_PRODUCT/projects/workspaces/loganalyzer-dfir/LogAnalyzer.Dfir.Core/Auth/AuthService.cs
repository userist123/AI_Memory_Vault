using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Auth;

public enum AuthSetupState
{
    /// <summary>No account exists: the primary administrator is created with account + password.</summary>
    FirstRun,
    /// <summary>The accounts exist but the password file was removed (documented offline recovery): a new primary password is set.</summary>
    PasswordRecovery,
    Ready,
}

/// <summary>One line of the hash-chained authentication audit (who, card thumbprint, reader, result, reason). Never a password or PIN.</summary>
public sealed record AuthAuditEntry(string Event, string Who, string Account, string CardThumbprint, string Reader, string Result, string Reason,
    DateTimeOffset WhenUtc, string Detail, string AccountsSha256);

/// <summary>
/// Sign-in, roles, card enrolment and the administrator operations (decision 33).
/// Card: certificate on a contact smart card in the keyboard slot + PIN handled by Windows / SafeNet; the application verifies a signed challenge.
/// Password: ONLY the primary administrator, always allowed, also after a card is enrolled; time-limited lockout that always expires.
/// Every other account: card only. Everything is written to the hash-chained auth audit.
/// </summary>
public sealed partial class AuthService
{
    public const string AccountsFileName = "accounts.json", PasswordFileName = "primary_password.json", PolicyFileName = "policy.json", AuditFileName = "auth_audit.jsonl";
    public const string ChallengePrefix = "LogAnalyzer-card-signin-v1";

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _classified;
    private readonly Dictionary<Guid, AuthSession> _sessions = [];

    public AuthService(string? dir = null, bool classifiedEdition = false, Func<DateTimeOffset>? clock = null)
    {
        Dir = dir ?? DefaultDir;
        _classified = classifiedEdition;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Trust = new CertificateTrust(TrustDir, CrlDir, () => Policy.MissingCrl, _clock);
    }

    /// <summary>%PROGRAMDATA%\LogAnalyzer\auth</summary>
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "auth");

    public string Dir { get; }
    public string TrustDir => Path.Combine(Dir, "trust");
    public string CrlDir => Path.Combine(Dir, "crl");
    public string AccountsPath => Path.Combine(Dir, AccountsFileName);
    public string PasswordPath => Path.Combine(Dir, PasswordFileName);
    public string PolicyPath => Path.Combine(Dir, PolicyFileName);
    public string AuditPath => Path.Combine(Dir, AuditFileName);
    public CertificateTrust Trust { get; }

    // ───────────────────────── state ─────────────────────────

    public AuthPolicy Policy
    {
        get
        {
            try { if (File.Exists(PolicyPath)) return Json.Read<AuthPolicy>(PolicyPath).Normalised(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException) { }
            return AuthPolicy.DefaultFor(_classified);     // an unreadable policy file falls back to the edition default, never to a weaker value
        }
    }

    public AuthSetupState SetupState
    {
        get
        {
            var a = ReadAccounts();
            if (a is null || !a.Accounts.Any(x => x.IsPrimaryAdmin)) return AuthSetupState.FirstRun;
            return File.Exists(PasswordPath) ? AuthSetupState.Ready : AuthSetupState.PasswordRecovery;
        }
    }

    public IReadOnlyList<AuthAccount> Accounts() => ReadAccounts()?.Accounts ?? [];
    public AuthAccount? Find(string account) => ReadAccounts()?.Accounts.FirstOrDefault(a => SameName(a.Account, account));
    public AuthAccount? PrimaryAdmin => ReadAccounts()?.Accounts.FirstOrDefault(a => a.IsPrimaryAdmin);
    public ChainVerification VerifyAudit() => new HashChain(AuditPath).Verify();
    public string AccountsSha256 => File.Exists(AccountsPath) ? Hashing.Sha256File(AccountsPath) : "";

    /// <summary>
    /// False when accounts.json is not what the last audited change left (edited outside the application). Card sign-in is then refused; the primary
    /// administrator can still sign in with the password and accept the current state (audited).
    /// </summary>
    public bool AccountsIntegrityOk
    {
        get
        {
            if (!File.Exists(AccountsPath)) return true;
            var last = Json.ReadLines<AuthAuditEntry>(AuditPath).Select(e => e.AccountsSha256).LastOrDefault(s => s.Length > 0);
            return last is not null && string.Equals(last, AccountsSha256, StringComparison.Ordinal);
        }
    }

    public static bool SameName(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase) && a.Trim().Length > 0;

    private AccountsFile? ReadAccounts()
    {
        try { return File.Exists(AccountsPath) ? Json.Read<AccountsFile>(AccountsPath) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException) { return null; }
    }

    private PasswordRecord? ReadPassword()
    {
        try { return File.Exists(PasswordPath) ? Json.Read<PasswordRecord>(PasswordPath) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException) { return null; }
    }

    private void WriteAccounts(AccountsFile f) => Json.Write(AccountsPath, f);

    private void Audit(string ev, string who, string account, string result, string reason, string detail = "", string card = "", string reader = "", bool accountsChanged = false)
    {
        new HashChain(AuditPath).Append(new AuthAuditEntry(ev, who, account, card, reader, result, reason, _clock(), detail, accountsChanged ? AccountsSha256 : ""));
    }

    private static string Short(string? thumb) => thumb is null ? "" : thumb[..Math.Min(16, thumb.Length)];

    // ───────────────────────── first run / recovery ─────────────────────────

    /// <summary>First run only: creates the primary administrator with account + password.</summary>
    public AuthResult CreatePrimaryAdmin(string account, string person, string password)
    {
        lock (_gate)
        {
            if (SetupState != AuthSetupState.FirstRun) return AuthResult.Fail("already_initialised", "administratorul principal există deja");
            account = account.Trim();
            if (account.Length == 0 || account.Count(c => c == '\\') > 1) return AuthResult.Fail("bad_account", "numele contului este invalid (DOMENIU\\utilizator sau utilizator)");
            var weak = PasswordRules.Check(password, account);
            if (weak is not null) return AuthResult.Fail("weak_password", weak);
            bool reinit = File.Exists(AuditPath) && new FileInfo(AuditPath).Length > 0;
            var now = _clock();
            Directory.CreateDirectory(Dir);
            WriteAccounts(new AccountsFile { Accounts = [new AuthAccount { Account = account, Person = person.Trim(), Role = AuthRole.Administrator, IsPrimaryAdmin = true, CreatedUtc = now }] });
            Json.Write(PasswordPath, PasswordRules.Create(account, password, now));
            if (!File.Exists(PolicyPath)) Json.Write(PolicyPath, AuthPolicy.DefaultFor(_classified));
            if (reinit) Audit("auth.store_reinitialised", "(neautentificat)", account, "ok", "accounts.json absent; magazinul de autentificare a fost reinițializat offline", accountsChanged: true);
            Audit("auth.primary_admin_created", "(neautentificat)", account, "ok", "creat cu cont + parolă la prima rulare", accountsChanged: true);
            Audit("auth.password_set", "(neautentificat)", account, "ok", "parola administratorului principal setată (rămâne valabilă și după înrolarea unui card)");
            return AuthResult.Success();
        }
    }

    /// <summary>Documented recovery: primary_password.json was removed offline by an operating-system administrator; the primary administrator sets a new password.</summary>
    public AuthResult RecoverPrimaryPassword(string password)
    {
        lock (_gate)
        {
            if (SetupState != AuthSetupState.PasswordRecovery) return AuthResult.Fail("not_in_recovery", "recuperarea parolei nu este activă");
            var p = PrimaryAdmin!;
            var weak = PasswordRules.Check(password, p.Account);
            if (weak is not null) return AuthResult.Fail("weak_password", weak);
            Json.Write(PasswordPath, PasswordRules.Create(p.Account, password, _clock()));
            Audit("auth.password_recovered", "(neautentificat)", p.Account, "ok", "parolă nouă după ștergerea offline a primary_password.json (procedura documentată)");
            return AuthResult.Success();
        }
    }

    // ───────────────────────── password sign-in (primary administrator only) ─────────────────────────

    public SignInResult SignInWithPassword(string account, string password)
    {
        lock (_gate)
        {
            account = (account ?? "").Trim();
            var now = _clock();
            var acc = Find(account);
            if (acc is null)
            {
                PasswordRules.DummyVerify();
                Audit("auth.signin", "(neautentificat)", "(unknown) sha256:" + Short(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(account.ToLowerInvariant())))), "refused", "invalid_credentials", "mod=parolă");
                return new SignInResult(false, "invalid_credentials", "cont sau parolă incorecte");
            }
            if (!acc.IsPrimaryAdmin)
            {
                Audit("auth.signin", "(neautentificat)", acc.Account, "refused", "password_mode_not_allowed", "mod=parolă; doar administratorul principal poate folosi parola");
                return new SignInResult(false, "password_mode_not_allowed", "acest cont se autentifică numai cu card și PIN; parola este permisă doar administratorului principal");
            }
            var rec = ReadPassword();
            if (rec is null)
            {
                Audit("auth.signin", "(neautentificat)", acc.Account, "refused", "no_primary_password", "parola nu este setată (recuperare)");
                return new SignInResult(false, "no_primary_password", "parola administratorului principal nu este setată; urmați procedura de recuperare");
            }
            if (rec.LockedUntilUtc is { } until && now < until)
            {
                var wait = until - now;
                Audit("auth.signin", "(neautentificat)", acc.Account, "refused", "locked_out", $"blocat încă {Math.Ceiling(wait.TotalSeconds)} s");
                return new SignInResult(false, "locked_out", $"autentificarea cu parolă este blocată temporar încă {FormatWait(wait)} după prea multe încercări greșite. Blocarea expiră singură; cardul (dacă este înrolat) rămâne utilizabil.");
            }
            if (!PasswordRules.Verify(rec, password ?? ""))
            {
                rec.FailedAttempts++;
                var threshold = Policy.LockoutThreshold;
                if (rec.FailedAttempts >= threshold)
                {
                    rec.Lockouts++;
                    var delay = TimeSpan.FromSeconds(Math.Min(15 * 60, 30 * Math.Pow(2, Math.Min(rec.Lockouts - 1, 10))));
                    rec.LockedUntilUtc = now + delay;
                    rec.FailedAttempts = 0;
                    Json.Write(PasswordPath, rec);
                    Audit("auth.lockout", "(neautentificat)", acc.Account, "locked", "too_many_failures", $"blocat {delay.TotalSeconds:0} s (blocarea nr. {rec.Lockouts}); expiră singură");
                    return new SignInResult(false, "locked_out", $"prea multe încercări greșite: autentificarea cu parolă este blocată {FormatWait(delay)}. Blocarea expiră singură.");
                }
                Json.Write(PasswordPath, rec);
                Audit("auth.signin", "(neautentificat)", acc.Account, "failed", "invalid_credentials", $"mod=parolă; eșecuri consecutive {rec.FailedAttempts}/{threshold}");
                return new SignInResult(false, "invalid_credentials", "cont sau parolă incorecte");
            }
            if (acc.Disabled)       // cannot normally happen (the primary administrator cannot be disabled); a hand-edited file is refused, and the integrity check reports it
            {
                Audit("auth.signin", acc.Account, acc.Account, "refused", "account_disabled", "mod=parolă");
                return new SignInResult(false, "account_disabled", "contul este dezactivat");
            }
            rec.FailedAttempts = 0; rec.Lockouts = 0; rec.LockedUntilUtc = null;
            Json.Write(PasswordPath, rec);
            var warnings = new List<string>();
            if (!AccountsIntegrityOk) warnings.Add("accounts.json a fost modificat în afara aplicației; autentificarea cu card este refuzată până când administratorul principal acceptă starea curentă (Administrare)");
            var s = NewSession(acc, SignInMode.Password, null, null);
            Audit("auth.signin", acc.Account, acc.Account, "ok", "password", "mod=parolă");
            return new SignInResult(true, "ok", "autentificat cu parolă", s, warnings);
        }
    }

    private static string FormatWait(TimeSpan t) => t.TotalMinutes >= 1 ? $"{Math.Ceiling(t.TotalMinutes)} min" : $"{Math.Ceiling(t.TotalSeconds)} s";

    // ───────────────────────── card sign-in ─────────────────────────

    /// <summary>
    /// Card + PIN: lists certificates with a private key on inserted smart cards, validates the chain/revocation offline, asks the card to sign a random
    /// challenge (Windows / SafeNet shows the PIN prompt) and verifies the signature with the certificate's public key.
    /// </summary>
    public SignInResult SignInWithCard(ICardProvider provider, string? sha256Thumbprint = null)
    {
        lock (_gate)
        {
            var picked = SelectCard(provider, sha256Thumbprint, out var early);
            if (early is not null) return early;
            var (card, acc, binding) = picked!.Value;
            var v = VerifyCard(provider, card, acc, binding, "auth.signin");
            if (!v.Ok) return new SignInResult(false, v.Reason, v.Message, Warnings: v.Warnings);
            var s = NewSession(acc, SignInMode.Card, card.Sha256Thumbprint, card.Reader);
            Audit("auth.signin", acc.Account, acc.Account, "ok", "card", "mod=card+PIN", Short(card.Sha256Thumbprint), card.Reader);
            return new SignInResult(true, "ok", "autentificat cu card", s, v.Warnings);
        }
    }

    private (CardCertificate, AuthAccount, CardBinding)? SelectCard(ICardProvider provider, string? thumb, out SignInResult? early)
    {
        early = null;
        IReadOnlyList<CardCertificate> cards;
        try { cards = provider.Enumerate(); }
        catch (CardException ex) { early = Refuse("no_card", ex.Message, ""); return null; }
        if (cards.Count == 0) { early = Refuse("no_card", "nu s-a găsit niciun card cu certificat în cititor. Un badge RFID doar cu UID (fără cheie privată) nu este acceptat.", ""); return null; }
        var withKey = cards.Where(c => c.HasPrivateKey).ToList();
        if (withKey.Count == 0) { early = Refuse("no_private_key", "certificatul găsit nu are cheie privată pe card: un badge doar cu UID sau un certificat fără cheie nu este acceptat", ""); return null; }
        if (!AccountsIntegrityOk) { early = Refuse("integrity", "accounts.json a fost modificat în afara aplicației; autentificarea cu card este refuzată. Administratorul principal se poate autentifica cu parola și poate accepta starea curentă.", ""); return null; }
        var accounts = ReadAccounts()?.Accounts ?? [];
        var mapped = withKey.Select(c => (Card: c, Acc: accounts.FirstOrDefault(a => a.Cards.Any(b => b.Sha256Thumbprint == c.Sha256Thumbprint)))).Where(x => x.Acc is not null).ToList();
        if (thumb is not null) mapped = mapped.Where(x => x.Card.Sha256Thumbprint == thumb).ToList();
        if (mapped.Count == 0)
        {
            var first = (thumb is null ? withKey[0] : withKey.FirstOrDefault(c => c.Sha256Thumbprint == thumb) ?? withKey[0]);
            early = Refuse("unmapped_card", "cardul nu este înrolat pentru niciun cont; cereți administratorului să îl înroleze", Short(first.Sha256Thumbprint), first.Reader);
            return null;
        }
        if (mapped.Count > 1) { early = new SignInResult(false, "choose_card", "sunt mai multe carduri înrolate; alegeți unul", Choices: mapped.Select(m => m.Card).ToList()); return null; }
        var (card, acc) = mapped[0];
        return (card, acc!, acc!.Cards.First(b => b.Sha256Thumbprint == card.Sha256Thumbprint));
    }

    private SignInResult Refuse(string reason, string message, string thumb, string reader = "")
    {
        Audit("auth.signin", "(neautentificat)", "", "refused", reason, message, thumb, reader);
        return new SignInResult(false, reason, message);
    }

    private sealed record Verified(bool Ok, string Reason, string Message, IReadOnlyList<string> Warnings);

    /// <summary>Account/card flags, offline trust, then the signed challenge. The PIN prompt is shown only after the cheap checks pass.</summary>
    private Verified VerifyCard(ICardProvider provider, CardCertificate card, AuthAccount acc, CardBinding binding, string auditEvent)
    {
        Verified Deny(string reason, string message, IReadOnlyList<string>? w = null)
        {
            Audit(auditEvent, acc.Account, acc.Account, "refused", reason, message, Short(card.Sha256Thumbprint), card.Reader);
            return new Verified(false, reason, message, w ?? []);
        }
        if (acc.Disabled) return Deny("account_disabled", "contul este dezactivat");
        if (binding.Disabled) return Deny("card_disabled", "cardul este dezactivat");
        if (!string.Equals(binding.Serial, card.SerialHex, StringComparison.OrdinalIgnoreCase) || !string.Equals(binding.Issuer, card.Certificate.Issuer, StringComparison.Ordinal))
            return Deny("unmapped_card", "emitentul sau seria certificatului nu corespund înrolării");
        var trust = Trust.Validate(card.Certificate);
        if (!trust.Ok) return Deny(trust.Reason, trust.Message, trust.Warnings);
        var sig = SignChallenge(provider, card, out var failure);
        if (sig is null) return Deny(failure!.Value.Reason, failure.Value.Message, trust.Warnings);
        return new Verified(true, "ok", "", trust.Warnings);
    }

    /// <summary>Random challenge, signed by the card, verified with the public key of the certificate. Null with a reason on failure.</summary>
    private byte[]? SignChallenge(ICardProvider provider, CardCertificate card, out (string Reason, string Message)? failure)
    {
        failure = null;
        var challenge = Challenge(_clock());
        byte[] sig;
        try { sig = provider.Sign(card, challenge); }
        catch (CardException ex) { failure = (ex.Reason, ex.Message); return null; }
        catch (CryptographicException) { failure = ("sign_failed", "cardul nu a semnat (PIN greșit sau anulat, card scos sau blocat)"); return null; }
        if (!CardSignature.Verify(card.Certificate, challenge, sig)) { failure = ("bad_signature", "semnătura nu corespunde certificatului: cardul nu deține cheia privată a certificatului"); return null; }
        return sig;
    }

    internal static byte[] Challenge(DateTimeOffset now)
    {
        var prefix = Encoding.UTF8.GetBytes(ChallengePrefix + "\n");
        var nonce = RandomNumberGenerator.GetBytes(32);
        var ts = BitConverter.GetBytes(now.UtcTicks);
        return [.. prefix, .. nonce, .. ts];
    }
}
