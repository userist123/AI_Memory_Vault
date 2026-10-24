using System.IO;
using System.Security.Cryptography;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Security audit #18: the edition-policy anti-rollback high-water mark is also kept in the hash-chained auth audit. The effective mark is the
/// maximum of the plain file and the audit, so deleting or lowering the file does not re-enable an older signed policy.
/// </summary>
public sealed class EditionPolicyHighWaterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_hw_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);

    public EditionPolicyHighWaterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private AuthService Auth() => new(Path.Combine(_dir, "auth"));
    private string HighWaterFile => Path.Combine(_dir, "policy.highwater");

    [Fact]
    public void Recording_a_version_writes_one_audit_entry_only_when_it_raises_the_mark()
    {
        var auth = Auth();
        Assert.True(auth.RecordEditionPolicyVersion(3));
        Assert.False(auth.RecordEditionPolicyVersion(3));
        Assert.False(auth.RecordEditionPolicyVersion(2));
        Assert.True(auth.RecordEditionPolicyVersion(5));
        Assert.Equal(5, AuthService.EditionPolicyHighWater(auth.AuditPath));
        Assert.Equal(ChainStatus.Valid, auth.VerifyAudit().Status);
    }

    [Fact]
    public void Missing_high_water_file_is_treated_as_the_maximum_in_the_audit()
    {
        var auth = Auth();
        auth.RecordEditionPolicyVersion(5);
        Assert.False(File.Exists(HighWaterFile));
        Assert.Equal(5, EditionComposition.HighWater(HighWaterFile, auth.AuditPath));

        File.WriteAllText(HighWaterFile, "2");                 // lowered by hand
        Assert.Equal(5, EditionComposition.HighWater(HighWaterFile, auth.AuditPath));
        File.WriteAllText(HighWaterFile, "7");                 // file ahead of the audit (audit not writable at that start)
        Assert.Equal(7, EditionComposition.HighWater(HighWaterFile, auth.AuditPath));
        Assert.Equal(0, EditionComposition.HighWater(Path.Combine(_dir, "none"), Path.Combine(_dir, "no_audit.jsonl")));
    }

    [Fact]
    public void An_older_signed_policy_is_refused_after_the_high_water_file_is_deleted()
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = Convert.ToBase64String(k.ExportSubjectPublicKeyInfo());
        var auth = Auth();
        auth.RecordEditionPolicyVersion(5);
        File.WriteAllText(HighWaterFile, "5");
        File.Delete(HighWaterFile);

        var hw = EditionComposition.HighWater(HighWaterFile, auth.AuditPath);
        var older = EditionPolicyVerifier.Verify(EditionPolicy.Sign(k, AppMode.Network, 4, "2026-10-01", null, "*", "owner"), pub, "PC1", Now, hw);
        Assert.False(older.Valid);
        var current = EditionPolicyVerifier.Verify(EditionPolicy.Sign(k, AppMode.Network, 5, "2026-10-01", null, "*", "owner"), pub, "PC1", Now, hw);
        Assert.True(current.Valid, current.Reason);
    }

    [Fact]
    public void An_unreadable_audit_line_does_not_lower_the_mark()
    {
        var auth = Auth();
        auth.RecordEditionPolicyVersion(6);
        File.AppendAllText(auth.AuditPath, "{not json\n");
        Assert.Equal(6, AuthService.EditionPolicyHighWater(auth.AuditPath));
    }
}
