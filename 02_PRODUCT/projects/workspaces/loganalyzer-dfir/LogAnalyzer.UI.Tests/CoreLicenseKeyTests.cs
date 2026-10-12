using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;
using CoreLicenseService = LogAnalyzer.Core.Services.LicenseService;
using LicenseCheck = LogAnalyzer.Core.Services.LicenseCheck;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Security audit #3: the production licence is an ECDSA P-256 signature over (hardware id, expiry) made with an offline
    /// private key; the editions only hold the public key. The former salted-SHA-256 scheme ("KEY|yyyy-MM-dd", salt in public
    /// source) is no longer accepted.
    /// </summary>
    public class CoreLicenseKeyTests
    {
        private const string Hwid = "ABCD1234EFAB5678";
        private static readonly DateTime Expiry = new(2027, 12, 31);
        private static readonly DateTime Today = new(2026, 10, 4);

        private static (ECDsa Key, string PublicKey) NewKey()
        {
            var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (k, Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));
        }

        [Fact]
        public void Key_from_the_old_salted_scheme_is_rejected_even_though_it_matched_before()
        {
            var (key, pub) = NewKey();
            using var keyScope = key;
            var s = new CoreLicenseService([pub]);
            // SHA256("ABCD1234EFAB5678" + "20271231" + former public salt)[0..20]: the golden value the old scheme accepted.
            const string legacy = "DBAE98173265E73CEB9B|2027-12-31";
            Assert.Equal(LicenseCheck.LegacyScheme, s.Check(Hwid, legacy, Today, out _));
            Assert.False(s.VerifyLicenseString(Hwid, legacy, Today));
            Assert.Contains("schema veche", CoreLicenseService.Explain(LicenseCheck.LegacyScheme));
        }

        [Fact]
        public void Licence_signed_with_the_trusted_key_is_accepted_for_its_hardware_id()
        {
            var (key, pub) = NewKey();
            using var keyScope = key;
            var s = new CoreLicenseService([pub]);
            var lic = CoreLicenseService.Sign(key, Hwid, Expiry, "LIC-1");
            Assert.StartsWith("LA1.", lic);
            Assert.Equal(LicenseCheck.Valid, s.Check(Hwid, lic, Today, out var claims));
            Assert.Equal((Hwid, Expiry, "LIC-1"), (claims!.HardwareId, claims.ExpiresUtcDate, claims.LicenseId));
            Assert.True(s.VerifyLicenseString(Hwid.ToLowerInvariant(), "  " + lic + "\r\n", Today));
            // valid through the whole expiry day (UTC), not after it
            Assert.True(s.VerifyLicenseString(Hwid, lic, new DateTime(2027, 12, 31, 23, 59, 0)));
            Assert.Equal(LicenseCheck.Expired, s.Check(Hwid, lic, new DateTime(2028, 1, 1, 0, 0, 1), out _));
        }

        [Fact]
        public void Licence_signed_by_another_key_is_rejected()
        {
            var (trusted, pub) = NewKey();
            var (other, _) = NewKey();
            using var _a = trusted; using var _b = other;
            var s = new CoreLicenseService([pub]);
            Assert.Equal(LicenseCheck.BadSignature, s.Check(Hwid, CoreLicenseService.Sign(other, Hwid, Expiry), Today, out _));
        }

        [Fact]
        public void Hardware_id_and_expiry_are_enforced_and_tampering_breaks_the_signature()
        {
            var (key, pub) = NewKey();
            using var keyScope = key;
            var s = new CoreLicenseService([pub]);
            var lic = CoreLicenseService.Sign(key, Hwid, Expiry);
            Assert.Equal(LicenseCheck.WrongHardware, s.Check("0000000000000000", lic, Today, out _));
            Assert.Equal(LicenseCheck.Expired, s.Check(Hwid, lic, new DateTime(2028, 1, 2), out _));

            // Swap in a payload for another machine / a later date, keeping the signature: refused.
            var parts = lic.Split('.');
            var forgedPayload = CoreLicenseService.Sign(key, "0000000000000000", new DateTime(2099, 1, 1)).Split('.')[1];
            Assert.Equal(LicenseCheck.BadSignature, s.Check("0000000000000000", $"LA1.{forgedPayload}.{parts[2]}", Today, out _));
            var flipped = parts[2][..^2] + (parts[2][^2] == 'A' ? 'B' : 'A') + parts[2][^1];
            Assert.Equal(LicenseCheck.BadSignature, s.Check(Hwid, $"LA1.{parts[1]}.{flipped}", Today, out _));

            Assert.Equal(LicenseCheck.Malformed, s.Check(Hwid, "no-separator", Today, out _));
            Assert.Equal(LicenseCheck.Malformed, s.Check(Hwid, "", Today, out _));
            Assert.Equal(LicenseCheck.Malformed, s.Check(Hwid, "LA1.!!!.???", Today, out _));
        }

        [Fact]
        public void Without_a_compiled_in_public_key_every_licence_is_refused()
        {
            var (key, _) = NewKey();
            using var keyScope = key;
            var s = new CoreLicenseService(Array.Empty<string>());
            Assert.Equal(LicenseCheck.NoTrustedKey, s.Check(Hwid, CoreLicenseService.Sign(key, Hwid, Expiry), Today, out _));
            Assert.Contains("cheie publică", CoreLicenseService.Explain(LicenseCheck.NoTrustedKey));
        }

        [Fact]
        public void Saved_licence_file_is_checked_again_at_every_start()
        {
            var (key, pub) = NewKey();
            using var keyScope = key;
            var path = Path.Combine(Path.GetTempPath(), "la-lic-" + Guid.NewGuid().ToString("N") + ".lic");
            try
            {
                var s = new CoreLicenseService([pub], path, () => Hwid);
                Assert.False(s.IsActivated());
                Assert.False(s.ValidateAndSaveKey("DBAE98173265E73CEB9B|2027-12-31", out var why));
                Assert.Equal(LicenseCheck.LegacyScheme, why);
                Assert.False(File.Exists(path));
                Assert.True(s.ValidateAndSaveKey(CoreLicenseService.Sign(key, Hwid, DateTime.UtcNow.Date.AddYears(1)), out why));
                Assert.Equal(LicenseCheck.Valid, why);
                Assert.True(s.IsActivated());
                File.WriteAllText(path, "DBAE98173265E73CEB9B|2027-12-31"); // an old license.lic left next to the exe
                Assert.False(s.IsActivated());
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Local_hardware_id_has_the_format_shown_in_the_activation_window()
        {
            Assert.Matches("^[0-9A-F]{16}$", new CoreLicenseService(Array.Empty<string>()).GetHardwareId());
        }
    }
}
