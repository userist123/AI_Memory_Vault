using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>HG 585 ID-03: system-use notice at sign-in, explicit acceptance, and the sealed record of what was accepted.</summary>
public sealed class SignInNoticeTests : IDisposable
{
    private const string Account = "CORP\\admin", Password = "Parola-Foarte-Lunga-2026!";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_notice_" + Guid.NewGuid().ToString("N"));

    public SignInNoticeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private AuthService NewService(bool require)
    {
        var svc = new AuthService(Path.Combine(_dir, "auth"), requireNoticeAcceptance: require);
        Assert.True(svc.CreatePrimaryAdmin(Account, "Admin", Password).Ok);
        return svc;
    }

    private static string Hex(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private static AuthAuditEntry[] Lines(AuthService s, string ev) => Json.ReadLines<AuthAuditEntry>(s.AuditPath).Where(e => e.Event == ev).ToArray();

    [Fact]
    public void Service_refuses_password_sign_in_without_acceptance_and_audits_the_refusal()
    {
        var svc = NewService(require: true);
        var r = svc.SignInWithPassword(Account, Password);
        Assert.False(r.Ok);
        Assert.Equal(AuthService.NoticeNotAcceptedReason, r.Reason);
        Assert.Null(r.Session);
        var refusal = Assert.Single(Lines(svc, "auth.signin"));
        Assert.Equal("refused", refusal.Result);
        Assert.Equal(AuthService.NoticeNotAcceptedReason, refusal.Reason);
        Assert.Empty(Lines(svc, AuthService.NoticeAcceptedEvent));
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void Service_refuses_card_sign_in_without_acceptance_before_any_card_is_touched()
    {
        var svc = NewService(require: true);
        var r = svc.SignInWithCard(new ThrowingCards());
        Assert.Equal(AuthService.NoticeNotAcceptedReason, r.Reason);
    }

    private sealed class ThrowingCards : ICardProvider
    {
        public System.Collections.Generic.IReadOnlyList<CardCertificate> Enumerate() => throw new InvalidOperationException("must not be reached");
        public byte[] Sign(CardCertificate card, byte[] data) => throw new InvalidOperationException("must not be reached");
        public bool IsPresent(string sha256Thumbprint) => throw new InvalidOperationException("must not be reached");
    }

    [Fact]
    public void Empty_accepted_text_counts_as_not_accepted()
    {
        var svc = NewService(require: true);
        Assert.Equal(AuthService.NoticeNotAcceptedReason, svc.SignInWithPassword(Account, Password, notice: new AuthService.NoticeAcceptance("", "profil")).Reason);
    }

    [Fact]
    public void Successful_sign_in_writes_exactly_one_notice_accepted_line_with_the_hash_of_the_text()
    {
        var svc = NewService(require: true);
        const string text = "Sistem destinat activității de securitate.\nAccesul este permis numai persoanelor autorizate. ăîșțâ";
        var r = svc.SignInWithPassword(Account, Password, notice: new AuthService.NoticeAcceptance(text, "profil"));
        Assert.True(r.Ok);
        var line = Assert.Single(Lines(svc, AuthService.NoticeAcceptedEvent));
        Assert.Equal(Account, line.Account);
        Assert.Equal("ok", line.Result);
        Assert.Equal($"sha256={Hex(text)}; sursa=profil", line.Detail);
        Assert.DoesNotContain("securitate", line.Detail);
        // review L1: the sign-in line itself carries the acceptance, so a failed notice.accepted write cannot leave a sign-in without it
        var signin = Assert.Single(Lines(svc, "auth.signin"), e => e.Result == "ok");
        Assert.Contains($"sha256={Hex(text)}", signin.Detail);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void Whitespace_only_text_counts_as_not_accepted_and_the_refusal_keeps_only_a_hash_of_the_typed_name()
    {
        var svc = NewService(require: true);
        Assert.Equal(AuthService.NoticeNotAcceptedReason, svc.SignInWithPassword("Parola-Tastata-Gresit", Password, notice: new AuthService.NoticeAcceptance("  \n ", "profil")).Reason);
        var refusal = Assert.Single(Lines(svc, "auth.signin"));
        Assert.DoesNotContain("Parola-Tastata-Gresit", refusal.Account);
        Assert.Contains("sha256:", refusal.Account);
    }

    [Fact]
    public void Default_source_is_recorded_as_implicit_and_a_wrong_password_records_no_acceptance()
    {
        var svc = NewService(require: true);
        Assert.False(svc.SignInWithPassword(Account, "gresita-gresita-1", notice: new AuthService.NoticeAcceptance("t", "implicit")).Ok);
        Assert.Empty(Lines(svc, AuthService.NoticeAcceptedEvent));
        Assert.True(svc.SignInWithPassword(Account, Password, notice: new AuthService.NoticeAcceptance("t", "implicit")).Ok);
        Assert.EndsWith("sursa=implicit", Assert.Single(Lines(svc, AuthService.NoticeAcceptedEvent)).Detail);
    }

    [Fact]
    public void Unlock_does_not_ask_again_and_does_not_record_again()
    {
        var svc = NewService(require: true);
        var s = svc.SignInWithPassword(Account, Password, notice: new AuthService.NoticeAcceptance("t", "implicit")).Session!;
        s.Locked = true;
        Assert.True(svc.Unlock(s, null, Password).Ok);
        Assert.Single(Lines(svc, AuthService.NoticeAcceptedEvent));
    }

    [Fact]
    public void Without_the_requirement_existing_callers_keep_working_and_write_no_notice_line()
    {
        var svc = NewService(require: false);
        Assert.True(svc.SignInWithPassword(Account, Password).Ok);
        Assert.Empty(Lines(svc, AuthService.NoticeAcceptedEvent));
    }

    [Fact]
    public void Sign_in_buttons_stay_off_until_ticked_except_in_unlock()
    {
        Assert.False(SignInWindow.SignInEnabled(SignInWindowMode.SignIn, false));
        Assert.False(SignInWindow.SignInEnabled(SignInWindowMode.FirstRun, false));
        Assert.True(SignInWindow.SignInEnabled(SignInWindowMode.SignIn, true));
        Assert.True(SignInWindow.SignInEnabled(SignInWindowMode.Unlock, false));
    }

    [Fact]
    public void Profile_without_the_field_gives_the_default_text()
    {
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, "{\"schema_version\":\"1.0\",\"Name\":\"vechi\"}");
        var c = SignInNoticeResolver.Resolve(new ProfileProvider(path), "TEXT IMPLICIT");
        Assert.Equal("TEXT IMPLICIT", c.Text);
        Assert.Equal(SignInNoticeResolver.SourceDefault, c.Source);
        Assert.Null(c.Remark);
    }

    [Fact]
    public void Missing_profile_gives_the_default_without_remark_and_unreadable_profile_gives_it_with_a_remark()
    {
        var none = SignInNoticeResolver.Resolve(new ProfileProvider(Path.Combine(_dir, "absent.json")), "D");
        Assert.Equal(("D", null), (none.Text, none.Remark));
        var bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "{ nu este json");
        var c = SignInNoticeResolver.Resolve(new ProfileProvider(bad), "D");
        Assert.Equal("D", c.Text);
        Assert.Equal(SignInNoticeResolver.SourceDefault, c.Source);
        Assert.Equal(SignInNoticeResolver.UnreadableRemark, c.Remark);
    }

    [Fact]
    public void Profile_notice_is_used_trimmed_with_source_profil_and_round_trips_through_the_page()
    {
        var path = Path.Combine(_dir, "p.json");
        File.WriteAllText(path, ProfileStore.ToJson(new ProcedureProfile { Name = "n", SignInNotice = "  Text oficial aprobat.  " }));
        var c = SignInNoticeResolver.Resolve(new ProfileProvider(path), "D");
        Assert.Equal(("Text oficial aprobat.", SignInNoticeResolver.SourceProfile), (c.Text, c.Source));

        var vm = new ProcedureProfileViewModel(new ProfileProvider(path));
        Assert.Equal("  Text oficial aprobat.  ", vm.SignInNotice);   // loaded as stored; trimmed when saved (Pull)
        Assert.Equal("Text oficial aprobat.", ProfileImport.FromJson(File.ReadAllText(path)).Profile!.SignInNotice.Trim());
    }

    [Fact]
    public void Notice_longer_than_the_limit_is_a_profile_error()
    {
        var p = new ProcedureProfile { SignInNotice = new string('x', SignInNoticeResolver.MaxLength + 1) };
        Assert.Contains(ProfileOps.Validate(p), i => i.IsError && i.Message.Contains("notificării"));
        p.SignInNotice = new string('x', SignInNoticeResolver.MaxLength);
        Assert.DoesNotContain(ProfileOps.Validate(p), i => i.Message.Contains("notificării"));
    }
}
