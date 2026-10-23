using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security audit #4: the auth folder's DACL is checked at startup (sign-in refused when non-administrators can write, an administrator may continue
/// after an audited acknowledgement), and the head of the auth audit chain is sealed with an HMAC key, so a recomputed chain fails VerifyAudit.
/// </summary>
public sealed class AuthStoreProtectionTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private const string Pw = "Corect-Cal-Baterie-Agrafa-77";
    private const string Admin = @"CORP\admin";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-authacl-" + Guid.NewGuid().ToString("N"));
    private readonly TestCa _ca = new("Test Org CA", T0.AddYears(-2), T0.AddYears(5));
    private readonly SoftwareCardProvider _card = new();
    private StoreAclResult _acl = StoreAclResult.Ok;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private bool _keyCreated;

    public AuthStoreProtectionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _ca.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private AuthService Svc(bool keyed = true) => new(_dir, false, () => T0, new AuthStoreSecurity
    {
        CheckAcl = _ => _acl,
        ChainKey = _ => keyed ? (_key, _keyCreated) : (null, false),
    });

    private AuthService Plain() => new(_dir, false, () => T0, AuthStoreSecurity.None);

    private static readonly StoreAclResult Unprotected = new(false, [@"auth: BUILTIN\Users poate scrie"]);

    private string AuditPath => Path.Combine(_dir, AuthService.AuditFileName);

    // ───────────── folder DACL ─────────────

    [Fact]
    public void Owner_and_write_aces_outside_administrators_and_system_make_the_store_unprotected()
    {
        const string users = "S-1-5-32-545", admins = "S-1-5-32-544", system = "S-1-5-18", creatorOwner = "S-1-3-0", authUsers = "S-1-5-11";
        var read = (int)FileSystemRights.ReadAndExecute;
        Assert.Empty(AuthStoreProtection.Evaluate("auth", admins, [(admins, (int)FileSystemRights.FullControl), (system, (int)FileSystemRights.FullControl), (users, read), (creatorOwner, (int)FileSystemRights.FullControl)]));
        Assert.Contains(AuthStoreProtection.Evaluate("auth", admins, [(users, (int)FileSystemRights.CreateFiles)]), p => p.Contains("poate scrie"));
        Assert.Contains(AuthStoreProtection.Evaluate("auth", admins, [(authUsers, (int)FileSystemRights.Modify)]), p => p.Contains("poate scrie"));
        Assert.Contains(AuthStoreProtection.Evaluate("auth", admins, [(users, 0x40000000)]), p => p.Contains("poate scrie"));     // GENERIC_WRITE (inherit-only ACE)
        Assert.Contains(AuthStoreProtection.Evaluate("auth", users, []), p => p.Contains("proprietar"));
    }

    [Fact]
    public void Folder_where_users_can_write_is_reported_as_unprotected()
    {
        var d = new DirectoryInfo(Path.Combine(_dir, "realacl"));
        d.Create();
        var sec = d.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        d.SetAccessControl(sec);
        var r = AuthStoreProtection.CheckDirectory(d.FullName);
        Assert.False(r.Protected);
        Assert.Contains(StoreAclResult.StateName, r.Message);
        Assert.False(AuthStoreProtection.CheckDirectory(Path.Combine(_dir, "missing")).Protected);
    }

    [Fact]
    public void Unprotected_store_refuses_sign_in_until_an_administrator_acknowledges_and_the_acknowledgement_is_audited()
    {
        var svc = Svc();
        Assert.True(svc.CreatePrimaryAdmin(Admin, "Administrator Principal", Pw).Ok);
        _acl = Unprotected;
        Assert.False(svc.StoreProtection.Protected);

        var refused = svc.SignInWithPassword(Admin, Pw);
        Assert.False(refused.Ok);
        Assert.Equal("store_unprotected", refused.Reason);
        Assert.Contains(StoreAclResult.StateName, refused.Message);

        var ok = svc.SignInWithPassword(Admin, Pw, acknowledgeUnprotectedStore: true);
        Assert.True(ok.Ok, ok.Message);
        Assert.Contains(ok.AllWarnings, w => w.Contains(StoreAclResult.StateName));
        var audit = File.ReadAllText(AuditPath);
        Assert.Contains("auth.store_unprotected_ack", audit);
        Assert.Contains("store_unprotected", audit);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void Operator_card_sign_in_is_refused_on_an_unprotected_store_even_with_an_acknowledgement()
    {
        var svc = Svc();
        Assert.True(svc.CreatePrimaryAdmin(Admin, "Administrator Principal", Pw).Ok);
        var admin = svc.SignInWithPassword(Admin, Pw).Session!;
        Assert.True(svc.ImportTrustCertificate(admin, _ca.PublicDer, "ca.cer").Ok);
        Assert.True(svc.ImportCrl(admin, _ca.Crl(T0.AddDays(-1), T0.AddDays(30), 1), "ca.crl").Ok);
        Assert.True(svc.CreateAccount(admin, @"CORP\ion", "Ion Popescu", AuthRole.Operator).Ok);
        using var ion = _ca.Issue("Ion Popescu", T0.AddYears(-1), T0.AddYears(2));
        _card.Insert(ion);
        Assert.True(svc.EnrollCard(admin, @"CORP\ion", _card.Enumerate()[0], _card).Ok);
        _card.ResetCalls();

        _acl = Unprotected;
        var r = svc.SignInWithCard(_card, null, acknowledgeUnprotectedStore: true);
        Assert.False(r.Ok);
        Assert.Equal("store_unprotected", r.Reason);
        Assert.Equal(0, _card.SignCalls); // refused before the PIN prompt
        _acl = StoreAclResult.Ok;
        Assert.True(svc.SignInWithCard(_card).Ok);
    }

    // ───────────── sealed chain head ─────────────

    private void Activity(AuthService svc)
    {
        Assert.True(svc.CreatePrimaryAdmin(Admin, "Administrator Principal", Pw).Ok);
        Assert.False(svc.SignInWithPassword(Admin, "gresit-gresit-gresit").Ok);
        Assert.True(svc.SignInWithPassword(Admin, Pw).Ok);
    }

    /// <summary>What someone without the key can do: rewrite accounts.json and append a correctly recomputed chain entry.</summary>
    private void ForgeRecomputedEntry()
    {
        var copy = Path.Combine(_dir, "attacker.jsonl");
        File.Copy(AuditPath, copy);
        File.AppendAllText(Path.Combine(_dir, AuthService.AccountsFileName), " ");
        var sha = LogAnalyzer.Dfir.IO.Hashing.Sha256File(Path.Combine(_dir, AuthService.AccountsFileName));
        new HashChain(copy).Append(new AuthAuditEntry("auth.account_created", Admin, @"CORP\mallory", "", "", "ok", "", T0, "", sha));
        File.Copy(copy, AuditPath, overwrite: true);
        File.Delete(copy);
    }

    [Fact]
    public void Recomputed_chain_without_the_key_fails_VerifyAudit()
    {
        var svc = Svc();
        Activity(svc);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
        Assert.True(svc.AccountsIntegrityOk);

        ForgeRecomputedEntry();
        var view = Path.Combine(_dir, "unsealed-view.jsonl");
        File.Copy(AuditPath, view);
        Assert.Equal(ChainStatus.Valid, new HashChain(view).Verify().Status); // sanity: the plain chain alone does not see the forgery
        var v = svc.VerifyAudit();
        Assert.Equal(ChainStatus.Broken, v.Status);
        Assert.Contains("sigil", v.Reason);
        Assert.False(svc.AccountsIntegrityOk);   // the forged accounts hash is not trusted either
    }

    [Fact]
    public void Deleting_the_sealed_head_or_rewriting_the_chain_breaks_it()
    {
        var svc = Svc();
        Activity(svc);
        var head = HashChain.HeadPath(AuditPath);
        Assert.True(File.Exists(head));
        var saved = File.ReadAllText(head);
        File.Delete(head);
        Assert.Equal(ChainStatus.Broken, svc.VerifyAudit().Status);

        // a forged head without the key
        var o = JsonNode.Parse(saved)!.AsObject();
        o["mac"] = new string('0', 64);
        File.WriteAllText(head, o.ToJsonString());
        Assert.Equal(ChainStatus.Broken, svc.VerifyAudit().Status);

        File.WriteAllText(head, saved);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
        // truncating the last line leaves a valid shorter chain that no longer reaches the sealed head
        var lines = File.ReadAllLines(AuditPath);
        File.WriteAllLines(AuditPath, lines[..^1]);
        Assert.Equal(ChainStatus.Broken, svc.VerifyAudit().Status);
    }

    [Fact]
    public void Appends_by_other_writers_of_the_same_log_keep_the_seal()
    {
        var svc = Svc();
        Activity(svc);
        ClockMemory.RecordSeen(AuditPath, T0.AddMinutes(1), "test");      // writes through HashChain directly
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void An_existing_unsealed_chain_is_sealed_once_when_the_key_is_first_created_and_that_is_audited()
    {
        Activity(Plain());
        Assert.False(File.Exists(HashChain.HeadPath(AuditPath)));
        _keyCreated = true;
        var svc = Svc();
        _keyCreated = false;
        Assert.Contains("auth.chain_sealed", File.ReadAllText(AuditPath));
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
        Assert.True(svc.SignInWithPassword(Admin, Pw).Ok);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    /// <summary>
    /// Regression (2026-10-14): the first start after the key is created, by a process that may not write the existing audit file (a standard
    /// user; the file was written earlier by an administrator), threw from the AuthService constructor, so the application could not start.
    /// The constructor must survive; the write failure surfaces where it already did, at the first audited operation.
    /// </summary>
    [Fact]
    public void First_key_creation_by_a_process_that_cannot_write_the_audit_does_not_stop_startup()
    {
        Activity(Plain());
        var before = File.ReadAllText(AuditPath);
        File.SetAttributes(AuditPath, FileAttributes.ReadOnly);
        try
        {
            _keyCreated = true;
            var svc = Svc();
            _keyCreated = false;
            Assert.Equal(before, File.ReadAllText(AuditPath));
            Assert.Throws<UnauthorizedAccessException>(() => svc.SignInWithPassword(Admin, Pw));
        }
        finally { File.SetAttributes(AuditPath, FileAttributes.Normal); }
    }

    /// <summary>
    /// Independent review, finding 5: when the head was sealed but the auth.chain_sealed line could not be appended, the loss is not silent:
    /// the service keeps a state that the sign-in window shows and the startup log records.
    /// </summary>
    [Fact]
    public void A_seal_that_could_not_be_recorded_is_reported_not_silent()
    {
        Activity(Plain());
        File.SetAttributes(AuditPath, FileAttributes.ReadOnly);
        try
        {
            _keyCreated = true;
            var svc = Svc();
            _keyCreated = false;
            Assert.True(File.Exists(HashChain.HeadPath(AuditPath)));              // the head was sealed…
            Assert.DoesNotContain("auth.chain_sealed", File.ReadAllText(AuditPath));   // …but the record of it is missing
            Assert.NotNull(svc.SealNotRecorded);
            Assert.Contains("UnauthorizedAccessException", svc.SealNotRecorded);
            Assert.Equal("Sigilarea jurnalului nu a putut fi consemnată; verificați drepturile folderului.", AuthService.SealNotRecordedMessage);
        }
        finally { File.SetAttributes(AuditPath, FileAttributes.Normal); }
        Assert.Null(Svc().SealNotRecorded);   // a normal start has nothing to report
    }

    [Fact]
    public void Without_a_readable_key_nothing_is_appended_and_the_cause_is_given()
    {
        var svc = Svc();
        Activity(svc);
        var before = File.ReadAllText(AuditPath);
        var noKey = Svc(keyed: false);
        Assert.Throws<SealingKeyUnavailableException>(() => noKey.SignInWithPassword(Admin, Pw));
        Assert.Equal(before, File.ReadAllText(AuditPath));
        Assert.Equal(ChainStatus.Broken, noKey.VerifyAudit().Status);
        Assert.Contains("cheia", noKey.VerifyAudit().Reason);
    }
}
