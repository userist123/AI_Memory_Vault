using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Auth;

public enum SessionStatus { Active, Locked, Ended }

public sealed partial class AuthService
{
    // ───────────────────────── sessions ─────────────────────────

    private AuthSession NewSession(AuthAccount acc, SignInMode mode, string? thumb, string? reader)
    {
        var s = new AuthSession(acc.Account, acc.Person, acc.Role, acc.IsPrimaryAdmin, mode, thumb, reader, _clock());
        _sessions[s.Id] = s;
        return s;
    }

    private bool Valid(AuthSession? s) => s is not null && !s.Ended && !s.Locked && _sessions.TryGetValue(s.Id, out var x) && ReferenceEquals(x, s);

    public AuthResult AuthorizeAdministrator(AuthSession? s)
    {
        lock (_gate)
        {
            if (!Valid(s)) return AuthResult.Fail("not_signed_in", "nu există o sesiune autentificată activă");
            var acc = Find(s!.Account);
            if (acc is null || acc.Disabled) return AuthResult.Fail("not_signed_in", "contul nu mai este activ");
            return acc.Role == AuthRole.Administrator ? AuthResult.Success() : AuthResult.Fail("not_authorized", OperatorIdentity.AdministratorOnlyMessage);
        }
    }

    public void SignOut(AuthSession session)
    {
        lock (_gate)
        {
            if (session.Ended) return;
            session.Ended = true; session.LockReason = "signout";
            _sessions.Remove(session.Id);
            Audit("auth.signout", session.Account, session.Account, "ok", "signout", card: Short(session.CardThumbprint), reader: session.CardReader ?? "");
        }
    }

    /// <summary>
    /// Called periodically by the UI. Ends the session if the account or the card was disabled (immediate), applies role changes, locks on inactivity
    /// (all sessions) and, for a card session only, when the card is no longer in the reader. A password session never locks on card removal.
    /// </summary>
    public SessionStatus CheckSession(AuthSession session, ICardProvider? provider = null)
    {
        lock (_gate)
        {
            if (session.Ended) return SessionStatus.Ended;
            var acc = Find(session.Account);
            if (acc is null || acc.Disabled) return EndSession(session, "account_disabled");
            if (session.Mode == SignInMode.Card && acc.Cards.FirstOrDefault(c => c.Sha256Thumbprint == session.CardThumbprint) is not { Disabled: false }) return EndSession(session, "card_disabled");
            session.Role = acc.Role;
            if (session.Locked) return SessionStatus.Locked;
            var idle = TimeSpan.FromMinutes(Policy.IdleLockMinutes);
            if (_clock() - session.LastActivityUtc >= idle) return LockSession(session, "idle");
            if (session.Mode == SignInMode.Card && provider is not null && !SafePresent(provider, session.CardThumbprint!)) return LockSession(session, "card_removed");
            return SessionStatus.Active;
        }
    }

    private static bool SafePresent(ICardProvider p, string thumb)
    {
        try { return p.IsPresent(thumb); } catch (CardException) { return false; }
    }

    private SessionStatus EndSession(AuthSession s, string reason)
    {
        s.Ended = true; s.LockReason = reason; _sessions.Remove(s.Id);
        Audit("auth.session_ended", s.Account, s.Account, "ended", reason, card: Short(s.CardThumbprint), reader: s.CardReader ?? "");
        return SessionStatus.Ended;
    }

    private SessionStatus LockSession(AuthSession s, string reason)
    {
        s.Locked = true; s.LockReason = reason;
        Audit("auth.session_locked", s.Account, s.Account, "locked", reason, card: Short(s.CardThumbprint), reader: s.CardReader ?? "");
        return SessionStatus.Locked;
    }

    /// <summary>Unlocks with the same method as the session (card + PIN, or the primary administrator's password). Another account never unlocks it.</summary>
    public AuthResult Unlock(AuthSession session, ICardProvider? provider = null, string? password = null)
    {
        lock (_gate)
        {
            if (session.Ended) return AuthResult.Fail("ended", "sesiunea s-a încheiat; autentificați-vă din nou");
            if (!session.Locked) return AuthResult.Success();
            SignInResult r;
            if (session.Mode == SignInMode.Card)
            {
                if (provider is null) return AuthResult.Fail("no_card", "introduceți cardul");
                r = SignInWithCard(provider, session.CardThumbprint);
            }
            else r = SignInWithPassword(session.Account, password ?? "");
            if (!r.Ok) return AuthResult.Fail(r.Reason, r.Message);
            _sessions.Remove(r.Session!.Id); r.Session.Ended = true;      // the probe session is discarded; the original one is unlocked
            if (!SameName(r.Session.Account, session.Account)) return AuthResult.Fail("other_account", "altă persoană nu poate debloca sesiunea");
            session.Locked = false; session.LockReason = ""; session.Touch(_clock());
            Audit("auth.session_unlocked", session.Account, session.Account, "ok", "unlock", card: Short(session.CardThumbprint), reader: session.CardReader ?? "");
            return AuthResult.Success();
        }
    }

    // ───────────────────────── accounts, cards, roles ─────────────────────────

    private AuthResult Admin(AuthSession? actor, out AuthSession? s)
    {
        s = actor;
        if (!Valid(actor)) return AuthResult.Fail("not_signed_in", "nu există o sesiune autentificată activă");
        var acc = Find(actor!.Account);
        if (acc is null || acc.Disabled) return AuthResult.Fail("not_signed_in", "contul nu mai este activ");
        if (acc.Role != AuthRole.Administrator)
        {
            Audit("auth.denied", actor.Account, actor.Account, "refused", "not_authorized", "acțiune rezervată administratorului");
            return AuthResult.Fail("not_authorized", OperatorIdentity.AdministratorOnlyMessage);
        }
        return AuthResult.Success();
    }

    public AuthResult CreateAccount(AuthSession actor, string account, string person, AuthRole role)
    {
        lock (_gate)
        {
            var a = Admin(actor, out _); if (!a.Ok) return a;
            account = account.Trim();
            if (account.Length == 0 || account.Count(c => c == '\\') > 1 || account.StartsWith('\\') || account.EndsWith('\\')) return AuthResult.Fail("bad_account", "numele contului este invalid (DOMENIU\\utilizator sau utilizator)");
            var file = ReadAccounts() ?? new AccountsFile();
            if (file.Accounts.Any(x => SameName(x.Account, account))) return AuthResult.Fail("exists", "contul există deja");
            file.Accounts.Add(new AuthAccount { Account = account, Person = person.Trim(), Role = role, CreatedUtc = _clock() });
            WriteAccounts(file);
            Audit("auth.account_created", actor.Account, account, "ok", "created", $"rol={role}; autentificare numai cu card", accountsChanged: true);
            return AuthResult.Success();
        }
    }

    public AuthResult SetRole(AuthSession actor, string account, AuthRole role)
    {
        lock (_gate)
        {
            var a = Admin(actor, out _); if (!a.Ok) return a;
            var file = ReadAccounts(); var t = file?.Accounts.FirstOrDefault(x => SameName(x.Account, account));
            if (t is null) return AuthResult.Fail("not_found", "contul nu există");
            if (t.IsPrimaryAdmin) return AuthResult.Fail("primary_protected", "rolul administratorului principal nu se schimbă din aplicație");
            var old = t.Role; t.Role = role;
            WriteAccounts(file!);
            foreach (var s in _sessions.Values.Where(x => SameName(x.Account, t.Account))) s.Role = role;
            Audit("auth.role_changed", actor.Account, t.Account, "ok", "role_change", $"{old} -> {role}", accountsChanged: true);
            return AuthResult.Success();
        }
    }

    public AuthResult SetAccountDisabled(AuthSession actor, string account, bool disabled)
    {
        lock (_gate)
        {
            var a = Admin(actor, out _); if (!a.Ok) return a;
            var file = ReadAccounts(); var t = file?.Accounts.FirstOrDefault(x => SameName(x.Account, account));
            if (t is null) return AuthResult.Fail("not_found", "contul nu există");
            if (t.IsPrimaryAdmin) return AuthResult.Fail("primary_protected", "administratorul principal nu poate fi dezactivat din aplicație (accesul cu parolă trebuie să rămână disponibil)");
            t.Disabled = disabled;
            WriteAccounts(file!);
            foreach (var s in _sessions.Values.Where(x => SameName(x.Account, t.Account)).ToList()) if (disabled) EndSession(s, "account_disabled");
            Audit(disabled ? "auth.account_disabled" : "auth.account_enabled", actor.Account, t.Account, "ok", disabled ? "disabled" : "enabled", accountsChanged: true);
            return AuthResult.Success();
        }
    }

    public AuthResult SetCardDisabled(AuthSession actor, string account, string sha256Thumbprint, bool disabled)
    {
        lock (_gate)
        {
            var a = Admin(actor, out _); if (!a.Ok) return a;
            var file = ReadAccounts(); var t = file?.Accounts.FirstOrDefault(x => SameName(x.Account, account));
            var b = t?.Cards.FirstOrDefault(c => c.Sha256Thumbprint == sha256Thumbprint);
            if (t is null || b is null) return AuthResult.Fail("not_found", "contul sau cardul nu există");
            b.Disabled = disabled;
            WriteAccounts(file!);
            foreach (var s in _sessions.Values.Where(x => x.CardThumbprint == sha256Thumbprint).ToList()) if (disabled) EndSession(s, "card_disabled");
            Audit(disabled ? "auth.card_disabled" : "auth.card_enabled", actor.Account, t.Account, "ok", disabled ? "disabled" : "enabled", card: Short(sha256Thumbprint), accountsChanged: true);
            return AuthResult.Success();
        }
    }

    /// <summary>
    /// Binds a card certificate to an account (thumbprint, issuer, serial; never the key). The administrator may enrol any account; a signed-in user may
    /// enrol an additional card for their own account (replacement). The certificate must chain to an imported CA and the card must sign a challenge.
    /// </summary>
    public AuthResult EnrollCard(AuthSession actor, string account, CardCertificate card, ICardProvider provider)
    {
        lock (_gate)
        {
            if (!Valid(actor)) return AuthResult.Fail("not_signed_in", "nu există o sesiune autentificată activă");
            var self = SameName(actor.Account, account);
            if (!self)
            {
                var a = Admin(actor, out _); if (!a.Ok) return a;
            }
            var file = ReadAccounts(); var t = file?.Accounts.FirstOrDefault(x => SameName(x.Account, account));
            if (t is null) return AuthResult.Fail("not_found", "contul nu există");
            if (t.Disabled) return AuthResult.Fail("account_disabled", "contul este dezactivat");
            if (!card.HasPrivateKey) return AuthResult.Fail("no_private_key", "certificatul nu are cheie privată pe card: un badge doar cu UID nu poate fi înrolat");
            if (file!.Accounts.Any(x => x.Cards.Any(c => c.Sha256Thumbprint == card.Sha256Thumbprint)))
                return AuthResult.Fail("already_enrolled", "cardul este deja înrolat");
            var trust = Trust.Validate(card.Certificate);
            if (!trust.Ok) { Audit("auth.card_enrolled", actor.Account, t.Account, "refused", trust.Reason, trust.Message, Short(card.Sha256Thumbprint), card.Reader); return AuthResult.Fail(trust.Reason, trust.Message); }
            var sig = SignChallenge(provider, card, out var failure);
            if (sig is null) { Audit("auth.card_enrolled", actor.Account, t.Account, "refused", failure!.Value.Reason, failure.Value.Message, Short(card.Sha256Thumbprint), card.Reader); return AuthResult.Fail(failure.Value.Reason, failure.Value.Message); }
            t.Cards.Add(new CardBinding
            {
                Sha256Thumbprint = card.Sha256Thumbprint, Issuer = card.Certificate.Issuer, Serial = card.SerialHex, Subject = card.Certificate.Subject,
                EnrolledUtc = _clock(), EnrolledBy = actor.Account,
            });
            WriteAccounts(file);
            Audit("auth.card_enrolled", actor.Account, t.Account, "ok", "enrolled", $"subiect={card.Certificate.Subject}; serie={card.SerialHex}", Short(card.Sha256Thumbprint), card.Reader, accountsChanged: true);
            return AuthResult.Success(string.Join(" | ", trust.Warnings));
        }
    }

    // ───────────────────────── trust store, CRLs, policy ─────────────────────────

    public AuthResult ImportTrustCertificate(AuthSession actor, byte[] data, string sourceName)
    {
        lock (_gate)
        {
            var a = Admin(actor, out _); if (!a.Ok) return a;
            X509Certificate2 cert;
            try { cert = CertificateTrust.LoadCertificate(data); }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { return AuthResult.Fail("bad_file", "fișierul nu este un certificat X.509 (DER sau PEM)"); }
            var bc = cert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (bc is null || !bc.CertificateAuthority) return AuthResult.Fail("not_ca", "certificatul nu este de autoritate de certificare (CA); se importă numai certificate CA");
            var thumb = Convert.ToHexStringLower(SHA256.HashData(cert.RawData));
            Directory.CreateDirectory(TrustDir);
            File.WriteAllBytes(Path.Combine(TrustDir, thumb + ".cer"), cert.RawData);
            var warn = DateTimeOffset.UtcNow > cert.NotAfter ? " (atenție: certificatul CA a expirat)" : "";
            Audit("auth.trust_imported", actor.Account, "", "ok", "trust_import", $"subiect={cert.Subject}; sha256={thumb}; fișier={Path.GetFileName(sourceName)}{warn}");
            return AuthResult.Success(cert.Subject + warn);
        }
    }

    public AuthResult ImportCrl(AuthSession actor, byte[] data, string sourceName)
    {
        lock (_gate)
        {
            var a = Admin(actor, out _); if (!a.Ok) return a;
            ParsedCrl crl;
            try { crl = ParsedCrl.Parse(data); } catch (FormatException ex) { return AuthResult.Fail("bad_file", ex.Message); }
            if (crl.IsDelta) return AuthResult.Fail("delta", "CRL-urile delta nu sunt acceptate; importați CRL-ul complet");
            var issuer = Trust.LoadAnchors().FirstOrDefault(c => c.SubjectName.RawData.AsSpan().SequenceEqual(crl.IssuerRaw) && crl.VerifySignature(c));
            if (issuer is null) return AuthResult.Fail("untrusted_crl", "CRL-ul nu este semnat de niciun CA importat (importați mai întâi CA-ul emitent)");
            var name = "crl-" + Convert.ToHexStringLower(SHA256.HashData(crl.IssuerRaw))[..16] + ".crl";
            var path = Path.Combine(CrlDir, name);
            if (File.Exists(path))
            {
                try
                {
                    var existing = ParsedCrl.Parse(File.ReadAllBytes(path));
                    if (crl.ThisUpdate < existing.ThisUpdate) return AuthResult.Fail("older", $"CRL-ul este mai vechi (thisUpdate {crl.ThisUpdate:u}) decât cel instalat ({existing.ThisUpdate:u}); nu se înlocuiește");
                }
                catch (Exception ex) when (ex is FormatException or IOException) { }
            }
            Directory.CreateDirectory(CrlDir);
            File.WriteAllBytes(path, crl.Der);
            Audit("auth.crl_imported", actor.Account, "", "ok", "crl_import",
                $"emitent={issuer.Subject}; thisUpdate={crl.ThisUpdate:u}; nextUpdate={(crl.NextUpdate is { } n ? n.ToString("u") : "-")}; revocate={crl.RevokedSerials.Count}; fișier={Path.GetFileName(sourceName)}");
            var warn = crl.NextUpdate is { } nu && nu < _clock() ? "CRL-ul importat este deja expirat (nextUpdate în trecut)" : "";
            return AuthResult.Success(warn);
        }
    }

    public AuthResult SetPolicy(AuthSession actor, AuthPolicy policy)
    {
        lock (_gate)
        {
            var a = Admin(actor, out _); if (!a.Ok) return a;
            var old = Policy; var neu = policy.Normalised();
            Json.Write(PolicyPath, neu);
            Audit("auth.policy_changed", actor.Account, "", "ok", "policy", $"crl lipsă: {old.MissingCrl} -> {neu.MissingCrl}; prag blocare {old.LockoutThreshold} -> {neu.LockoutThreshold}; inactivitate {old.IdleLockMinutes} -> {neu.IdleLockMinutes} min");
            return AuthResult.Success();
        }
    }

    // ───────────────────────── primary administrator password ─────────────────────────

    public AuthResult ChangePassword(AuthSession session, string current, string next)
    {
        lock (_gate)
        {
            if (!Valid(session)) return AuthResult.Fail("not_signed_in", "nu există o sesiune autentificată activă");
            if (!session.IsPrimaryAdmin) return AuthResult.Fail("password_mode_not_allowed", "numai administratorul principal are parolă");
            var rec = ReadPassword();
            if (rec is null || !PasswordRules.Verify(rec, current ?? ""))
            {
                Audit("auth.password_changed", session.Account, session.Account, "refused", "wrong_current_password");
                return AuthResult.Fail("wrong_current_password", "parola curentă este incorectă");
            }
            var weak = PasswordRules.Check(next, session.Account);
            if (weak is not null) return AuthResult.Fail("weak_password", weak);
            var neu = PasswordRules.Create(session.Account, next, _clock());
            Json.Write(PasswordPath, neu);
            Audit("auth.password_changed", session.Account, session.Account, "ok", "changed");
            return AuthResult.Success();
        }
    }

    /// <summary>The primary administrator accepts accounts.json as it is after an outside edit (recorded with the old and new hash).</summary>
    public AuthResult AcceptCurrentAccountsState(AuthSession session)
    {
        lock (_gate)
        {
            if (!Valid(session)) return AuthResult.Fail("not_signed_in", "nu există o sesiune autentificată activă");
            if (!session.IsPrimaryAdmin) return AuthResult.Fail("not_authorized", "numai administratorul principal poate accepta starea curentă");
            if (AccountsIntegrityOk) return AuthResult.Success("nu era nimic de acceptat");
            var last = Json.ReadLines<AuthAuditEntry>(AuditPath).Select(e => e.AccountsSha256).LastOrDefault(s => s.Length > 0) ?? "(niciunul)";
            Audit("auth.integrity_accepted", session.Account, session.Account, "ok", "accepted", $"hash auditat {last}; hash curent {AccountsSha256}", accountsChanged: true);
            return AuthResult.Success();
        }
    }

    /// <summary>Accounts of the auth store that have no entry in the users register (clearances are entered there by the administrator).</summary>
    public IEnumerable<string> AccountsMissingFromRegister(LogAnalyzer.Dfir.Registers.UsersRegister register) =>
        Accounts().Where(a => !register.ForAccount(a.Account).Any()).Select(a => a.Account);
}
