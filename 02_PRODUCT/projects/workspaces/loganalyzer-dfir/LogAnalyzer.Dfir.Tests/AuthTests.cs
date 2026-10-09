using System.Text.Json;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP-AUTH (owner decision 33): card + PIN for users, account + password only for the primary administrator, offline trust, roles, audit.</summary>
public sealed class AuthTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private const string Pw = "Corect-Cal-Baterie-Agrafa-77";
    private const string Admin = @"CORP\admin";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-auth-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = T0;
    private readonly TestCa _ca;
    private readonly SoftwareCardProvider _card = new();

    public AuthTests()
    {
        Directory.CreateDirectory(_dir);
        _ca = new TestCa("Test Org CA", T0.AddYears(-2), T0.AddYears(5));
    }

    public void Dispose()
    {
        _ca.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private AuthService Svc(bool classified = false) => new(_dir, classified, () => _now);

    /// <summary>First run done, CA imported, an operator "ion" with an enrolled card; returns the admin (password) session.</summary>
    private (AuthService Svc, AuthSession Admin, X509Certificate2Holder Ion) Setup(bool classified = false, bool importCrl = true)
    {
        var svc = Svc(classified);
        Assert.True(svc.CreatePrimaryAdmin(Admin, "Administrator Principal", Pw).Ok);
        var admin = svc.SignInWithPassword(Admin, Pw).Session!;
        Assert.True(svc.ImportTrustCertificate(admin, _ca.PublicDer, "ca.cer").Ok);
        if (importCrl) Assert.True(svc.ImportCrl(admin, _ca.Crl(T0.AddDays(-1), T0.AddDays(30), 1), "ca.crl").Ok);
        Assert.True(svc.CreateAccount(admin, @"CORP\ion", "Ion Popescu", AuthRole.Operator).Ok);
        var ion = new X509Certificate2Holder(_ca.Issue("Ion Popescu", T0.AddYears(-1), T0.AddYears(2)));
        _card.Insert(ion.Cert);
        // enrolment needs a trust decision; without a CRL it is allowed under the "warn" policy, then the edition default is restored
        var edition = svc.Policy;
        if (!importCrl) svc.SetPolicy(admin, new AuthPolicy { MissingCrl = MissingCrlPolicy.Warn });
        var enrol = svc.EnrollCard(admin, @"CORP\ion", _card.Enumerate()[0], _card);
        Assert.True(enrol.Ok, enrol.Message);
        if (!importCrl) svc.SetPolicy(admin, edition);
        _card.ResetCalls();
        return (svc, admin, ion);
    }

    public sealed record X509Certificate2Holder(System.Security.Cryptography.X509Certificates.X509Certificate2 Cert);

    private string AuditText => File.ReadAllText(Path.Combine(_dir, AuthService.AuditFileName));

    // ───────────── primary administrator: password ─────────────

    [Theory]
    [InlineData("scurta123")]
    [InlineData(@"corp\admin")]
    [InlineData("admin")]
    [InlineData("loganalyzer1234")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    public void Weak_passwords_are_refused_at_first_run_and_nothing_is_created(string weak)
    {
        var svc = Svc();
        var r = svc.CreatePrimaryAdmin(Admin, "A", weak);
        Assert.False(r.Ok);
        Assert.Equal("weak_password", r.Reason);
        Assert.Equal(AuthSetupState.FirstRun, svc.SetupState);
        Assert.False(File.Exists(Path.Combine(_dir, AuthService.PasswordFileName)));
    }

    [Fact]
    public void Password_equal_to_the_account_name_is_refused_even_when_long()
    {
        var svc = Svc();
        Assert.Equal("weak_password", svc.CreatePrimaryAdmin("administratorul-principal-al-sistemului", "A", "administratorul-principal-al-sistemului").Reason);
    }

    [Fact]
    public void First_run_creates_the_primary_admin_with_a_pbkdf2_record_and_never_stores_the_password()
    {
        var svc = Svc();
        Assert.Equal(AuthSetupState.FirstRun, svc.SetupState);
        Assert.True(svc.CreatePrimaryAdmin(Admin, "Admin", Pw).Ok);
        Assert.Equal(AuthSetupState.Ready, svc.SetupState);
        Assert.False(svc.CreatePrimaryAdmin(@"CORP\other", "B", Pw).Ok);      // only once
        var rec = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, AuthService.PasswordFileName))).RootElement;
        Assert.Equal("PBKDF2-SHA256", rec.GetProperty("algorithm").GetString());
        Assert.True(rec.GetProperty("iterations").GetInt32() >= 600_000);
        Assert.DoesNotContain(Pw, File.ReadAllText(Path.Combine(_dir, AuthService.PasswordFileName)));
        Assert.DoesNotContain(Pw, AuditText);
        var acc = svc.PrimaryAdmin!;
        Assert.True(acc.IsPrimaryAdmin); Assert.Equal(AuthRole.Administrator, acc.Role);
    }

    [Fact]
    public void The_primary_admin_signs_in_with_account_and_password_and_wrong_credentials_are_generic()
    {
        var svc = Svc(); svc.CreatePrimaryAdmin(Admin, "Admin", Pw);
        var ok = svc.SignInWithPassword(Admin.ToUpperInvariant(), Pw);
        Assert.True(ok.Ok); Assert.Equal(SignInMode.Password, ok.Session!.Mode); Assert.True(ok.Session.IsAdministrator);
        var wrong = svc.SignInWithPassword(Admin, "gresita-gresita-gresita");
        var unknown = svc.SignInWithPassword(@"CORP\nimeni", Pw);
        Assert.Equal("invalid_credentials", wrong.Reason);
        Assert.Equal(wrong.Message, unknown.Message);                 // no account enumeration
    }

    [Fact]
    public void The_password_stays_valid_before_and_after_a_card_is_enrolled_for_the_primary_admin()
    {
        var svc = Svc(); svc.CreatePrimaryAdmin(Admin, "Admin", Pw);
        var s1 = svc.SignInWithPassword(Admin, Pw).Session!;
        svc.ImportTrustCertificate(s1, _ca.PublicDer, "ca.cer");
        svc.ImportCrl(s1, _ca.Crl(T0.AddDays(-1), T0.AddDays(30), 1), "a.crl");
        var cert = _ca.Issue("Admin", T0.AddYears(-1), T0.AddYears(1));
        _card.Insert(cert);
        Assert.True(svc.EnrollCard(s1, Admin, _card.Enumerate()[0], _card).Ok);
        // card works...
        var byCard = svc.SignInWithCard(_card);
        Assert.True(byCard.Ok, byCard.Message);
        Assert.Equal(Admin, byCard.Session!.Account); Assert.True(byCard.Session.IsAdministrator);
        // ...and the password is STILL valid, with no card in the slot at all
        _card.RemoveAll();
        var byPassword = svc.SignInWithPassword(Admin, Pw);
        Assert.True(byPassword.Ok, byPassword.Message);
        Assert.Equal(SignInMode.Password, byPassword.Session!.Mode);
    }

    [Fact]
    public void Password_mode_is_refused_for_every_other_account_including_administrators()
    {
        var (svc, admin, _) = Setup();
        Assert.True(svc.CreateAccount(admin, @"CORP\boss2", "Al doilea admin", AuthRole.Administrator).Ok);
        foreach (var a in new[] { @"CORP\ion", @"CORP\boss2" })
        {
            var r = svc.SignInWithPassword(a, Pw);
            Assert.False(r.Ok);
            Assert.Equal("password_mode_not_allowed", r.Reason);
            Assert.Null(r.Session);
        }
        Assert.Contains("password_mode_not_allowed", AuditText);
    }

    [Fact]
    public void Lockout_after_n_failures_is_time_limited_and_never_permanent()
    {
        var svc = Svc(); svc.CreatePrimaryAdmin(Admin, "Admin", Pw);
        for (int i = 0; i < 4; i++) Assert.Equal("invalid_credentials", svc.SignInWithPassword(Admin, "gresita-" + i + "-gresita-gresita").Reason);
        Assert.Equal("locked_out", svc.SignInWithPassword(Admin, "gresita-5-gresita-gresita").Reason);       // 5th failure locks
        Assert.Equal("locked_out", svc.SignInWithPassword(Admin, Pw).Reason);                                 // even the right password is refused while locked
        _now = _now.AddSeconds(31);
        Assert.True(svc.SignInWithPassword(Admin, Pw).Ok);                                                    // first lockout: 30 s, then it expires on its own
        // repeated lockouts back off but are capped: after 16 minutes the correct password always works
        for (int round = 0; round < 8; round++)
        {
            for (int i = 0; i < 5; i++) svc.SignInWithPassword(Admin, "gresita-gresita-gresita-" + i);
            _now = _now.AddSeconds(5);
        }
        Assert.Equal("locked_out", svc.SignInWithPassword(Admin, Pw).Reason);
        _now = _now.AddMinutes(16);
        Assert.True(svc.SignInWithPassword(Admin, Pw).Ok);
        Assert.Contains("auth.lockout", AuditText);
    }

    [Fact]
    public void Lockout_state_survives_a_restart_and_does_not_block_the_card_of_the_primary_admin()
    {
        var svc = Svc(); svc.CreatePrimaryAdmin(Admin, "Admin", Pw);
        var s = svc.SignInWithPassword(Admin, Pw).Session!;
        svc.ImportTrustCertificate(s, _ca.PublicDer, "ca.cer"); svc.ImportCrl(s, _ca.Crl(T0.AddDays(-1), T0.AddDays(30), 1), "a.crl");
        _card.Insert(_ca.Issue("Admin", T0.AddYears(-1), T0.AddYears(1)));
        Assert.True(svc.EnrollCard(s, Admin, _card.Enumerate()[0], _card).Ok);
        for (int i = 0; i < 5; i++) svc.SignInWithPassword(Admin, "gresita-gresita-gresita-" + i);
        var restarted = Svc();
        Assert.Equal("locked_out", restarted.SignInWithPassword(Admin, Pw).Reason);
        Assert.True(restarted.SignInWithCard(_card).Ok);                                                     // card stays usable during the password lockout
    }

    [Fact]
    public void Changing_the_password_needs_the_current_one_is_audited_and_the_old_one_stops_working()
    {
        var svc = Svc(); svc.CreatePrimaryAdmin(Admin, "Admin", Pw);
        var s = svc.SignInWithPassword(Admin, Pw).Session!;
        Assert.Equal("wrong_current_password", svc.ChangePassword(s, "nu-este-asta-deloc-1", "Parola-Noua-Foarte-Lunga-9").Reason);
        Assert.Equal("weak_password", svc.ChangePassword(s, Pw, "scurta").Reason);
        const string neu = "Parola-Noua-Foarte-Lunga-9";
        Assert.True(svc.ChangePassword(s, Pw, neu).Ok);
        Assert.False(svc.SignInWithPassword(Admin, Pw).Ok);
        Assert.True(svc.SignInWithPassword(Admin, neu).Ok);
        Assert.Contains("auth.password_changed", AuditText);
        Assert.DoesNotContain(neu, AuditText); Assert.DoesNotContain(Pw, AuditText);
    }

    [Fact]
    public void An_operator_account_cannot_change_a_password_because_only_the_primary_admin_has_one()
    {
        var (svc, _, _) = Setup();
        var ion = svc.SignInWithCard(_card).Session!;
        Assert.Equal("password_mode_not_allowed", svc.ChangePassword(ion, "x", "Parola-Noua-Foarte-Lunga-9").Reason);
    }

    [Fact]
    public void Offline_recovery_removing_the_password_file_lets_the_primary_admin_set_a_new_password_and_is_audited()
    {
        var svc = Svc(); svc.CreatePrimaryAdmin(Admin, "Admin", Pw);
        Assert.False(svc.RecoverPrimaryPassword("Parola-Noua-Foarte-Lunga-9").Ok);            // not in recovery
        File.Delete(Path.Combine(_dir, AuthService.PasswordFileName));
        Assert.Equal(AuthSetupState.PasswordRecovery, svc.SetupState);
        Assert.Equal("no_primary_password", svc.SignInWithPassword(Admin, Pw).Reason);
        Assert.Equal("weak_password", svc.RecoverPrimaryPassword("scurta").Reason);
        Assert.True(svc.RecoverPrimaryPassword("Parola-Noua-Foarte-Lunga-9").Ok);
        Assert.True(svc.SignInWithPassword(Admin, "Parola-Noua-Foarte-Lunga-9").Ok);
        Assert.Contains("auth.password_recovered", AuditText);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void Reinitialising_the_store_offline_is_detected_and_audited_and_the_old_audit_chain_is_kept()
    {
        var (svc, _, _) = Setup();
        File.Delete(Path.Combine(_dir, AuthService.AccountsFileName));
        File.Delete(Path.Combine(_dir, AuthService.PasswordFileName));
        var again = Svc();
        Assert.Equal(AuthSetupState.FirstRun, again.SetupState);
        Assert.True(again.CreatePrimaryAdmin(Admin, "Admin nou", Pw).Ok);
        Assert.Contains("auth.store_reinitialised", AuditText);
        Assert.Contains("auth.card_enrolled", AuditText);                                      // earlier history is still there
        Assert.Equal(ChainStatus.Valid, again.VerifyAudit().Status);
    }

    // ───────────── card sign-in ─────────────

    [Fact]
    public void A_card_with_a_trusted_certificate_signs_in_after_the_challenge_is_signed_and_verified()
    {
        var (svc, _, _) = Setup();
        var r = svc.SignInWithCard(_card);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(@"CORP\ion", r.Session!.Account);
        Assert.Equal(AuthRole.Operator, r.Session.Role); Assert.False(r.Session.IsAdministrator);
        Assert.Equal(SignInMode.Card, r.Session.Mode);
        Assert.Equal(1, _card.SignCalls);                                                     // exactly one PIN prompt
        Assert.Equal("Test Keyboard Reader 0", r.Session.CardReader);
    }

    [Fact]
    public void Ec_certificates_work_too()
    {
        var (svc, admin, _) = Setup();
        svc.CreateAccount(admin, "maria", "Maria", AuthRole.Operator);
        var ec = _ca.Issue("Maria", T0.AddYears(-1), T0.AddYears(1), ec: true);
        var p = new SoftwareCardProvider(); p.Insert(ec);
        Assert.True(svc.EnrollCard(admin, "maria", p.Enumerate()[0], p).Ok);
        Assert.Equal("maria", svc.SignInWithCard(p).Session!.Account);
    }

    [Fact]
    public void A_uid_only_badge_has_no_certificate_and_is_refused_with_a_clear_message()
    {
        var (svc, _, _) = Setup();
        _card.RemoveAll();
        var r = svc.SignInWithCard(_card);
        Assert.False(r.Ok); Assert.Equal("no_card", r.Reason); Assert.Contains("UID", r.Message);
    }

    [Fact]
    public void A_certificate_without_a_private_key_is_refused_and_cannot_be_enrolled()
    {
        var (svc, admin, _) = Setup();
        _card.RemoveAll();
        var pubOnly = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(_ca.Issue("Ion", T0.AddYears(-1), T0.AddYears(1)).RawData);
        _card.Insert(pubOnly, hasKey: false);
        var r = svc.SignInWithCard(_card);
        Assert.Equal("no_private_key", r.Reason);
        Assert.Equal("no_private_key", svc.EnrollCard(admin, @"CORP\ion", _card.Enumerate()[0], _card).Reason);
        Assert.Equal(0, _card.SignCalls);
    }

    [Fact]
    public void A_card_not_enrolled_for_any_account_is_refused_before_the_pin_prompt()
    {
        var (svc, _, _) = Setup();
        _card.RemoveAll();
        _card.Insert(_ca.Issue("Necunoscut", T0.AddYears(-1), T0.AddYears(1)));
        var r = svc.SignInWithCard(_card);
        Assert.Equal("unmapped_card", r.Reason);
        Assert.Equal(0, _card.SignCalls);
    }

    [Fact]
    public void A_certificate_from_an_unknown_ca_is_refused_and_so_is_everything_when_no_ca_is_imported()
    {
        var (svc, admin, _) = Setup();
        using var rogue = new TestCa("Rogue CA", T0.AddYears(-2), T0.AddYears(5));
        var cert = rogue.Issue("Ion", T0.AddYears(-1), T0.AddYears(1));
        var p = new SoftwareCardProvider(); p.Insert(cert);
        // not enrollable
        Assert.Equal("untrusted", svc.EnrollCard(admin, @"CORP\ion", p.Enumerate()[0], p).Reason);
        // with no CA imported at all nothing is trusted
        var fresh = new AuthService(Path.Combine(_dir, "empty"), false, () => _now);
        fresh.CreatePrimaryAdmin(Admin, "A", Pw);
        Assert.Equal("untrusted", fresh.Trust.Validate(cert).Reason);
        Assert.Contains("CA", fresh.Trust.Validate(cert).Message);
    }

    [Fact]
    public void Expired_and_not_yet_valid_certificates_are_refused_with_the_reason()
    {
        var (svc, admin, _) = Setup();
        _card.RemoveAll();
        svc.CreateAccount(admin, "vechi", "V", AuthRole.Operator);
        var old = _ca.Issue("Vechi", T0.AddYears(-2).AddDays(1), T0.AddDays(-1));
        var future = _ca.Issue("Viitor", T0.AddDays(5), T0.AddYears(1));
        var p = new SoftwareCardProvider(); p.Insert(old);
        Assert.Equal("expired", svc.EnrollCard(admin, "vechi", p.Enumerate()[0], p).Reason);
        p.RemoveAll(); p.Insert(future);
        Assert.Equal("not_yet_valid", svc.EnrollCard(admin, "vechi", p.Enumerate()[0], p).Reason);
    }

    [Fact]
    public void A_certificate_that_expires_after_enrolment_is_refused_at_sign_in()
    {
        var (svc, _, _) = Setup();
        Assert.True(svc.SignInWithCard(_card).Ok);
        _now = T0.AddYears(3);                                                                  // user cert valid until T0+2y
        var r = svc.SignInWithCard(_card);
        Assert.Equal("expired", r.Reason);
        Assert.Equal(1, _card.SignCalls);                                                       // no second PIN prompt
    }

    [Fact]
    public void A_revoked_certificate_is_refused_after_the_crl_is_imported()
    {
        var (svc, admin, ion) = Setup();
        Assert.True(svc.SignInWithCard(_card).Ok);
        var crl = _ca.Crl(T0.AddHours(-1), T0.AddDays(30), 2, ion.Cert);
        Assert.True(svc.ImportCrl(admin, crl, "revoke.crl").Ok);
        var r = svc.SignInWithCard(_card);
        Assert.False(r.Ok); Assert.Equal("revoked", r.Reason);
        Assert.Contains("auth.crl_imported", AuditText);
    }

    [Fact]
    public void Missing_crl_policy_defaults_to_refuse_on_the_classified_edition_and_warn_on_the_unclassified_one()
    {
        var (uncl, _, _) = Setup(classified: false, importCrl: false);
        Assert.Equal(MissingCrlPolicy.Warn, uncl.Policy.MissingCrl);
        var r = uncl.SignInWithCard(_card);
        Assert.True(r.Ok, r.Message);
        Assert.Contains(r.AllWarnings, w => w.Contains("CRL"));

        Directory.Delete(_dir, true); Directory.CreateDirectory(_dir); _card.RemoveAll();
        var (cls, _, _) = Setup(classified: true, importCrl: false);
        Assert.Equal(MissingCrlPolicy.Refuse, cls.Policy.MissingCrl);
        var c = cls.SignInWithCard(_card);
        Assert.False(c.Ok); Assert.Equal("crl_missing", c.Reason);
    }

    [Fact]
    public void The_missing_crl_policy_is_administrator_configurable_and_an_expired_crl_counts_as_missing()
    {
        var (svc, admin, _) = Setup(classified: true, importCrl: false);
        Assert.Equal("crl_missing", svc.SignInWithCard(_card).Reason);
        var policy = svc.Policy; policy.MissingCrl = MissingCrlPolicy.Warn;
        Assert.True(svc.SetPolicy(admin, policy).Ok);
        Assert.True(svc.SignInWithCard(_card).Ok);
        // CRL imported, later expired
        Assert.True(svc.ImportCrl(admin, _ca.Crl(T0.AddDays(-1), T0.AddDays(2), 1), "a.crl").Ok);
        Assert.Empty(svc.SignInWithCard(_card).AllWarnings);
        _now = T0.AddDays(3);
        var stale = svc.SignInWithCard(_card);
        Assert.True(stale.Ok); Assert.Contains(stale.AllWarnings, w => w.Contains("expirat"));
        policy.MissingCrl = MissingCrlPolicy.Refuse; svc.SetPolicy(admin, policy);
        Assert.Equal("crl_missing", svc.SignInWithCard(_card).Reason);
    }

    [Fact]
    public void Crl_import_refuses_unsigned_foreign_older_and_garbage_files()
    {
        var (svc, admin, _) = Setup(importCrl: false);
        using var rogue = new TestCa("Test Org CA", T0.AddYears(-2), T0.AddYears(5));         // same subject name, different key
        Assert.Equal("untrusted_crl", svc.ImportCrl(admin, rogue.Crl(T0.AddDays(-1), T0.AddDays(30), 1), "x.crl").Reason);
        Assert.Equal("bad_file", svc.ImportCrl(admin, [1, 2, 3, 4], "junk.crl").Reason);
        Assert.True(svc.ImportCrl(admin, _ca.Crl(T0.AddDays(-1), T0.AddDays(30), 5), "new.crl").Ok);
        var older = svc.ImportCrl(admin, _ca.Crl(T0.AddDays(-10), T0.AddDays(20), 4), "old.crl");
        Assert.Equal("older", older.Reason);                                                  // no roll-back to an older CRL
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "crl")));
    }

    [Fact]
    public void Trust_import_accepts_only_ca_certificates()
    {
        var (svc, admin, ion) = Setup();
        Assert.Equal("not_ca", svc.ImportTrustCertificate(admin, ion.Cert.RawData, "ion.cer").Reason);
        Assert.Equal("bad_file", svc.ImportTrustCertificate(admin, [9, 9, 9], "junk").Reason);
        var pem = "-----BEGIN CERTIFICATE-----\n" + Convert.ToBase64String(_ca.PublicDer, Base64FormattingOptions.InsertLineBreaks) + "\n-----END CERTIFICATE-----\n";
        Assert.True(svc.ImportTrustCertificate(admin, System.Text.Encoding.ASCII.GetBytes(pem), "ca.pem").Ok);   // PEM is accepted too
    }

    [Fact]
    public void A_card_that_signs_with_another_key_fails_the_signature_check()
    {
        var (svc, _, _) = Setup();
        _card.SignWithOther = _ca.Issue("Atacator", T0.AddYears(-1), T0.AddYears(1));
        var r = svc.SignInWithCard(_card);
        Assert.False(r.Ok); Assert.Equal("bad_signature", r.Reason); Assert.Null(r.Session);
    }

    [Fact]
    public void A_cancelled_or_wrong_pin_gives_no_session_and_the_pin_is_never_seen_or_logged()
    {
        var (svc, _, _) = Setup();
        _card.PinCancelled = true;
        var r = svc.SignInWithCard(_card);
        Assert.False(r.Ok); Assert.Equal("pin_cancelled", r.Reason);
        Assert.Contains("pin_cancelled", AuditText);
        // the API surface has no PIN parameter at all
        Assert.DoesNotContain(typeof(AuthService).GetMethods().SelectMany(m => m.GetParameters()), p => p.Name!.Contains("pin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(ICardProvider).GetMethods().SelectMany(m => m.GetParameters()), p => p.Name!.Contains("pin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Several_cards_per_user_and_disabling_a_card_or_a_user_is_immediate()
    {
        var (svc, admin, ion) = Setup();
        var second = _ca.Issue("Ion Popescu (inlocuire)", T0.AddYears(-1), T0.AddYears(2));
        var p2 = new SoftwareCardProvider(); p2.Insert(second);
        Assert.True(svc.EnrollCard(admin, @"CORP\ion", p2.Enumerate()[0], p2).Ok);
        Assert.Equal(2, svc.Find(@"CORP\ion")!.Cards.Count);
        Assert.True(svc.SignInWithCard(p2).Ok);

        var session = svc.SignInWithCard(_card).Session!;
        Assert.Equal(SessionStatus.Active, svc.CheckSession(session, _card));
        var thumb = svc.Find(@"CORP\ion")!.Cards[0].Sha256Thumbprint;
        Assert.True(svc.SetCardDisabled(admin, @"CORP\ion", thumb, true).Ok);
        Assert.Equal(SessionStatus.Ended, svc.CheckSession(session, _card));                   // the live session of that card ends at once
        Assert.Equal("card_disabled", svc.SignInWithCard(_card).Reason);
        Assert.True(svc.SignInWithCard(p2).Ok);                                                // the replacement card still works

        Assert.True(svc.SetAccountDisabled(admin, @"CORP\ion", true).Ok);
        Assert.Equal("account_disabled", svc.SignInWithCard(p2).Reason);
        Assert.True(svc.SetAccountDisabled(admin, @"CORP\ion", false).Ok);
        Assert.True(svc.SetCardDisabled(admin, @"CORP\ion", thumb, false).Ok);
        Assert.True(svc.SignInWithCard(_card).Ok);
    }

    [Fact]
    public void A_disabled_account_is_refused_before_any_pin_prompt()
    {
        var (svc, admin, _) = Setup();
        svc.SetAccountDisabled(admin, @"CORP\ion", true);
        var before = _card.SignCalls;
        Assert.Equal("account_disabled", svc.SignInWithCard(_card).Reason);
        Assert.Equal(before, _card.SignCalls);
    }

    [Fact]
    public void Enrolment_needs_possession_of_the_key_and_refuses_a_card_enrolled_twice()
    {
        var (svc, admin, ion) = Setup();
        Assert.Equal("already_enrolled", svc.EnrollCard(admin, @"CORP\ion", _card.Enumerate()[0], _card).Reason);
        svc.CreateAccount(admin, "maria", "Maria", AuthRole.Operator);
        var cert = _ca.Issue("Maria", T0.AddYears(-1), T0.AddYears(1));
        var p = new SoftwareCardProvider(); p.Insert(cert);
        p.SignWithOther = ion.Cert;                                                            // presents Maria's certificate but signs with another key
        Assert.Equal("bad_signature", svc.EnrollCard(admin, "maria", p.Enumerate()[0], p).Reason);
        Assert.Empty(svc.Find("maria")!.Cards);
    }

    // ───────────── roles ─────────────

    [Fact]
    public void An_operator_cannot_do_administrator_actions_and_each_refusal_is_audited()
    {
        var (svc, _, ion) = Setup();
        var op = svc.SignInWithCard(_card).Session!;
        Assert.Equal("not_authorized", svc.CreateAccount(op, "x", "X", AuthRole.Operator).Reason);
        Assert.Equal("not_authorized", svc.SetRole(op, @"CORP\ion", AuthRole.Administrator).Reason);
        Assert.Equal("not_authorized", svc.SetAccountDisabled(op, @"CORP\ion", true).Reason);
        Assert.Equal("not_authorized", svc.SetCardDisabled(op, @"CORP\ion", "00", true).Reason);
        Assert.Equal("not_authorized", svc.ImportTrustCertificate(op, _ca.PublicDer, "ca.cer").Reason);
        Assert.Equal("not_authorized", svc.ImportCrl(op, _ca.Crl(T0, T0.AddDays(5), 9), "a.crl").Reason);
        Assert.Equal("not_authorized", svc.SetPolicy(op, new AuthPolicy()).Reason);
        Assert.Equal("not_authorized", svc.AuthorizeAdministrator(op).Reason);
        var other = _ca.Issue("Altcineva", T0.AddYears(-1), T0.AddYears(1)); var p = new SoftwareCardProvider(); p.Insert(other);
        Assert.Equal("not_authorized", svc.EnrollCard(op, "altcineva", p.Enumerate()[0], p).Reason);   // may enrol only for self
        Assert.Contains("auth.denied", AuditText);
        Assert.False(svc.Find(@"CORP\ion")!.Role == AuthRole.Administrator);
    }

    [Fact]
    public void An_operator_may_enrol_an_additional_card_for_self_and_a_role_change_applies_to_the_live_session()
    {
        var (svc, admin, _) = Setup();
        var op = svc.SignInWithCard(_card).Session!;
        var spare = _ca.Issue("Ion rezerva", T0.AddYears(-1), T0.AddYears(1)); var p = new SoftwareCardProvider(); p.Insert(spare);
        Assert.True(svc.EnrollCard(op, @"CORP\ion", p.Enumerate()[0], p).Ok);
        Assert.True(svc.SetRole(admin, @"CORP\ion", AuthRole.Administrator).Ok);
        Assert.True(op.IsAdministrator);
        Assert.True(svc.AuthorizeAdministrator(op).Ok);
        Assert.True(svc.SetRole(admin, @"CORP\ion", AuthRole.Operator).Ok);
        Assert.False(op.IsAdministrator);
        Assert.Contains("auth.role_changed", AuditText);
    }

    [Fact]
    public void The_primary_admin_cannot_be_disabled_or_demoted_from_the_application()
    {
        var (svc, admin, _) = Setup();
        svc.CreateAccount(admin, @"CORP\boss2", "Boss2", AuthRole.Administrator);
        Assert.Equal("primary_protected", svc.SetAccountDisabled(admin, Admin, true).Reason);
        Assert.Equal("primary_protected", svc.SetRole(admin, Admin, AuthRole.Operator).Reason);
        Assert.True(svc.SignInWithPassword(Admin, Pw).Ok);
    }

    [Fact]
    public void A_signed_out_or_forged_session_has_no_rights()
    {
        var (svc, admin, _) = Setup();
        svc.SignOut(admin);
        Assert.Equal("not_signed_in", svc.CreateAccount(admin, "x", "X", AuthRole.Operator).Reason);
        Assert.Equal("not_signed_in", svc.AuthorizeAdministrator(null).Reason);
        Assert.Contains("auth.signout", AuditText);
    }

    // ───────────── session lock ─────────────

    [Fact]
    public void Card_removal_locks_a_card_session_and_the_same_card_unlocks_it()
    {
        var (svc, _, ion) = Setup();
        var s = svc.SignInWithCard(_card).Session!;
        Assert.Equal(SessionStatus.Active, svc.CheckSession(s, _card));
        _card.Remove(ion.Cert);
        Assert.Equal(SessionStatus.Locked, svc.CheckSession(s, _card));
        Assert.Equal("card_removed", s.LockReason);
        Assert.False(s.IsAdministrator);
        Assert.Equal("no_card", svc.Unlock(s, _card).Reason);                                  // still no card
        _card.Insert(ion.Cert);
        Assert.True(svc.Unlock(s, _card).Ok);
        Assert.Equal(SessionStatus.Active, svc.CheckSession(s, _card));
        Assert.Contains("auth.session_locked", AuditText); Assert.Contains("auth.session_unlocked", AuditText);
    }

    [Fact]
    public void A_password_session_is_never_locked_by_card_removal_only_by_inactivity()
    {
        var (svc, admin, _) = Setup();
        _card.RemoveAll();
        Assert.Equal(SessionStatus.Active, svc.CheckSession(admin, _card));                    // no card in the slot: the password session does not care
        Assert.Equal(SessionStatus.Active, svc.CheckSession(admin, null));
        _now = _now.AddMinutes(svc.Policy.IdleLockMinutes + 1);
        Assert.Equal(SessionStatus.Locked, svc.CheckSession(admin, _card));
        Assert.Equal("idle", admin.LockReason);
        Assert.Equal("invalid_credentials", svc.Unlock(admin, password: "gresita-gresita-gresita").Reason);
        Assert.True(svc.Unlock(admin, password: Pw).Ok);
        Assert.Equal(SessionStatus.Active, svc.CheckSession(admin, _card));
    }

    [Fact]
    public void Activity_postpones_the_idle_lock_and_it_applies_to_card_sessions_too()
    {
        var (svc, _, _) = Setup();
        var s = svc.SignInWithCard(_card).Session!;
        var idle = svc.Policy.IdleLockMinutes;
        _now = _now.AddMinutes(idle - 1); s.Touch(_now);
        _now = _now.AddMinutes(idle - 1);
        Assert.Equal(SessionStatus.Active, svc.CheckSession(s, _card));
        _now = _now.AddMinutes(2);
        Assert.Equal(SessionStatus.Locked, svc.CheckSession(s, _card));
    }

    [Fact]
    public void Another_persons_card_cannot_unlock_a_session()
    {
        var (svc, admin, _) = Setup();
        svc.CreateAccount(admin, "maria", "Maria", AuthRole.Operator);
        var m = _ca.Issue("Maria", T0.AddYears(-1), T0.AddYears(1)); var pm = new SoftwareCardProvider(); pm.Insert(m);
        Assert.True(svc.EnrollCard(admin, "maria", pm.Enumerate()[0], pm).Ok);
        var s = svc.SignInWithCard(_card).Session!;
        _now = _now.AddMinutes(60); svc.CheckSession(s, _card);
        Assert.True(s.Locked);
        Assert.False(svc.Unlock(s, pm).Ok);                                                    // Maria's card is not the session's card
        Assert.True(s.Locked);
    }

    // ───────────── audit and integrity ─────────────

    [Fact]
    public void The_auth_audit_is_hash_chained_records_every_event_kind_and_has_no_secrets()
    {
        var (svc, admin, ion) = Setup();
        svc.SignInWithPassword(Admin, "gresita-gresita-gresita");
        svc.SignInWithCard(_card);
        svc.SetRole(admin, @"CORP\ion", AuthRole.Administrator);
        svc.SetCardDisabled(admin, @"CORP\ion", svc.Find(@"CORP\ion")!.Cards[0].Sha256Thumbprint, true);
        var text = AuditText;
        foreach (var ev in new[] { "auth.primary_admin_created", "auth.password_set", "auth.signin", "auth.trust_imported", "auth.crl_imported", "auth.account_created",
                                   "auth.card_enrolled", "auth.role_changed", "auth.card_disabled" })
            Assert.Contains(ev, text);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
        // who, card thumbprint (prefix), reader and result are present on a card sign-in
        var line = File.ReadAllLines(Path.Combine(_dir, AuthService.AuditFileName)).Select(l => JsonDocument.Parse(l).RootElement)
            .First(e => e.GetProperty("event").GetString() == "auth.signin" && e.GetProperty("reason").GetString() == "card");
        Assert.Equal(@"CORP\ion", line.GetProperty("who").GetString());
        Assert.NotEmpty(line.GetProperty("cardThumbprint").GetString()!);
        Assert.Equal("Test Keyboard Reader 0", line.GetProperty("reader").GetString());
        Assert.Equal("ok", line.GetProperty("result").GetString());
        Assert.DoesNotContain(Pw, text);
        File.WriteAllText(Path.Combine(_dir, AuthService.AuditFileName), text.Replace("CORP\\\\ion", "CORP\\\\xxx"));
        Assert.Equal(ChainStatus.Broken, svc.VerifyAudit().Status);
    }

    [Fact]
    public void Editing_accounts_json_outside_the_app_blocks_cards_but_not_the_primary_admin_password_until_accepted()
    {
        var (svc, admin, _) = Setup();
        Assert.True(svc.AccountsIntegrityOk);
        var path = Path.Combine(_dir, AuthService.AccountsFileName);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"operator\"", "\"administrator\"").Replace("OPERATOR", "ADMINISTRATOR"));
        Assert.False(svc.AccountsIntegrityOk);
        Assert.Equal("integrity", svc.SignInWithCard(_card).Reason);
        var pw = svc.SignInWithPassword(Admin, Pw);
        Assert.True(pw.Ok); Assert.NotEmpty(pw.AllWarnings);                                    // the primary admin can always get in, with a warning
        Assert.True(svc.AcceptCurrentAccountsState(pw.Session!).Ok);
        Assert.True(svc.AccountsIntegrityOk);
        Assert.Contains("auth.integrity_accepted", AuditText);
        Assert.True(svc.SignInWithCard(_card).Ok);
    }

    [Fact]
    public void Accounts_missing_from_the_users_register_are_reported_for_the_administrator()
    {
        var (svc, _, _) = Setup();
        var reg = new UsersRegister();
        reg.SetCells([["Ion Popescu", @"CORP\ion", "", "", "", "", "", ""]]);
        Assert.Equal([Admin], svc.AccountsMissingFromRegister(reg).ToArray());
    }

    // ───────────── identity in custody / register gates ─────────────

    [Fact]
    public void The_signed_in_identity_replaces_the_os_account_in_the_case_custody_and_audit_chains()
    {
        var (svc, _, _) = Setup();
        var ion = svc.SignInWithCard(_card).Session!;
        using var _s = OperatorIdentity.Scope(ion, authenticationRequired: true);
        var ws = CaseWorkspace.Create(Path.Combine(_dir, "case"), new CaseInfo { CaseId = "C1", Name = "c", CreatedAtUtc = DateTimeOffset.UtcNow, Host = "H", Scope = TestScopes.Valid() });
        var p = Path.Combine(ws.RawDir("s"), "a.txt"); File.WriteAllText(p, "x");
        ws.RegisterStored(p, "a.txt", "s", "txt", TemporalType.Historical, "unit", "1");
        var custody = File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement).Select(e => e.GetProperty("who").GetString()).ToList();
        Assert.NotEmpty(custody);
        Assert.All(custody, w => Assert.Equal(@"CORP\ion", w));
        Assert.DoesNotContain(Environment.UserName, string.Join(" ", custody.Where(c => c != @"CORP\ion")));
        Assert.All(File.ReadAllLines(ws.AuditChainPath).Select(l => JsonDocument.Parse(l).RootElement.GetProperty("who").GetString()), w => Assert.Equal(@"CORP\ion", w));
    }

    private static UsersRegister OneUser() { var r = new UsersRegister(); r.SetCells([["Ion", @"CORP\ion", "", "", "", "", "", ""]]); return r; }

    [Fact]
    public void Registers_and_the_procedure_profile_can_be_saved_only_by_a_signed_in_administrator()
    {
        var (svc, admin, _) = Setup();
        var op = svc.SignInWithCard(_card).Session!;
        var reg = Path.Combine(_dir, "users.json");
        var prof = Path.Combine(_dir, "procedure_profile.json");
        using (OperatorIdentity.Scope(null, true))
        {
            Assert.Contains(RegisterStore.Save(OneUser(), reg, out _), i => i.IsError);
            Assert.Contains(ProfileStore.Save(new ProcedureProfile(), prof, out _), i => i.IsError);
        }
        using (OperatorIdentity.Scope(op, true))
        {
            Assert.Contains(RegisterStore.Save(OneUser(), reg, out _), i => i.Message.Contains("administratorul"));
            Assert.Contains(ProfileStore.Save(new ProcedureProfile(), prof, out _), i => i.IsError);
        }
        Assert.False(File.Exists(reg)); Assert.False(File.Exists(prof));
        using (OperatorIdentity.Scope(admin, true))
        {
            Assert.DoesNotContain(RegisterStore.Save(OneUser(), reg, out _), i => i.IsError);
            Assert.DoesNotContain(ProfileStore.Save(new ProcedureProfile(), prof, out _), i => i.IsError);
        }
        Assert.True(File.Exists(reg)); Assert.True(File.Exists(prof));
        var e = JsonDocument.Parse(File.ReadAllLines(RegisterStore.AuditPathFor(reg))[0]).RootElement;
        Assert.Equal(Admin, e.GetProperty("who").GetString());
        Assert.Contains("parolă", e.GetProperty("whoSource").GetString());
        // a locked administrator session loses the right at once
        _now = _now.AddMinutes(svc.Policy.IdleLockMinutes + 1); svc.CheckSession(admin, null);
        Assert.True(admin.Locked);
        using (OperatorIdentity.Scope(admin, true)) Assert.Contains(RegisterStore.Save(OneUser(), reg, out _, action: "again"), i => i.IsError);
    }

    [Fact]
    public void Without_required_authentication_headless_tools_behave_as_before()
    {
        using var _s = OperatorIdentity.Scope(null, authenticationRequired: false);
        var reg = Path.Combine(_dir, "users.json");
        Assert.DoesNotContain(RegisterStore.Save(OneUser(), reg, out _), i => i.IsError);
        Assert.Equal(Environment.UserName, OperatorIdentity.Who);
    }

    [Fact]
    public void Roles_for_edition_defaults()
    {
        Assert.Equal(MissingCrlPolicy.Refuse, AuthPolicy.DefaultFor(true).MissingCrl);
        Assert.Equal(MissingCrlPolicy.Warn, AuthPolicy.DefaultFor(false).MissingCrl);
        Assert.Equal(3, new AuthPolicy { LockoutThreshold = 0 }.Normalised().LockoutThreshold);
    }
}
