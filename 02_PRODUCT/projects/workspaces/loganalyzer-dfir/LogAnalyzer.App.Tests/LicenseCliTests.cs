using System;
using System.IO;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Security audit #3: the offline licence tooling (KeyGen license-keygen / license-sign) produces licences the editions accept.</summary>
public sealed class LicenseCliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-liccli-" + Guid.NewGuid().ToString("N"));
    private const string Hwid = "ABCD1234EFAB5678";

    public LicenseCliTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (string Priv, string Pub) Keygen()
    {
        var priv = Path.Combine(_dir, "licence.key");
        var pub = Path.Combine(_dir, "licence.pub");
        var o = new StringWriter();
        Assert.Equal(0, LicenseCli.Keygen(["license-keygen", priv, pub], o));
        return (priv, File.ReadAllText(pub).Trim());
    }

    [Fact]
    public void Keygen_writes_a_protected_private_key_prints_the_public_key_and_never_overwrites()
    {
        var (priv, pub) = Keygen();
        Assert.True(ReportKeyProtection.IsProtected(File.ReadAllText(priv)));
        Assert.DoesNotContain(pub, File.ReadAllText(priv), StringComparison.Ordinal);
        var o = new StringWriter();
        Assert.Equal(1, LicenseCli.Keygen(["license-keygen", priv], o));
        Assert.Contains("nu se suprascrie", o.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Signed_licence_is_accepted_by_the_edition_for_its_hardware_id_and_until_its_expiry_only()
    {
        var (priv, pub) = Keygen();
        var expires = DateTime.UtcNow.Date.AddYears(1);
        var outFile = Path.Combine(_dir, "license.lic");
        var o = new StringWriter();
        Assert.Equal(0, LicenseCli.Sign(["license-sign", priv, "--hardware-id", Hwid.ToLowerInvariant(), "--expires", expires.ToString("yyyy-MM-dd"), "--out", outFile], o));
        var lic = File.ReadAllText(outFile).Trim();
        Assert.Contains(lic, o.ToString(), StringComparison.Ordinal);

        var edition = new LicenseService([pub]);
        Assert.Equal(LicenseCheck.Valid, edition.Check(Hwid, lic, DateTime.UtcNow, out var claims));
        Assert.Equal(expires, claims!.ExpiresUtcDate);
        Assert.Equal(LicenseCheck.WrongHardware, edition.Check("0000000000000000", lic, DateTime.UtcNow, out _));
        Assert.Equal(LicenseCheck.Expired, edition.Check(Hwid, lic, expires.AddDays(1), out _));
        // another licence key does not vouch for it
        using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        Assert.Equal(LicenseCheck.BadSignature, new LicenseService([Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())]).Check(Hwid, lic, DateTime.UtcNow, out _));
    }

    [Theory]
    [InlineData("XYZ", "2030-01-01")]                 // not a hardware id
    [InlineData("ABCD1234EFAB5678", "2020-01-01")]    // already expired
    [InlineData("ABCD1234EFAB5678", "31.12.2030")]    // not yyyy-MM-dd
    public void Sign_refuses_bad_hardware_id_or_expiry(string hw, string expires)
    {
        var (priv, _) = Keygen();
        var o = new StringWriter();
        Assert.Equal(1, LicenseCli.Sign(["license-sign", priv, "--hardware-id", hw, "--expires", expires], o));
        Assert.DoesNotContain("LA1.", o.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_requires_both_hardware_id_and_expiry()
    {
        var (priv, _) = Keygen();
        Assert.Equal(1, LicenseCli.Sign(["license-sign", priv, "--hardware-id", Hwid], new StringWriter()));
        Assert.Equal(1, LicenseCli.Sign(["license-sign", priv, "--expires", "2030-01-01"], new StringWriter()));
    }
}
