using System;
using Xunit;
using CoreLicenseService = LogAnalyzer.Core.Services.LicenseService;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// The license generators (LicenseManager, KeyGen) and the editions' activation share the Core LicenseService
    /// ("KEY|yyyy-MM-dd" scheme). These tests pin the key format so already-issued licenses keep working.
    /// </summary>
    public class CoreLicenseKeyTests
    {
        private const string Hwid = "ABCD1234EFAB5678";
        private static readonly DateTime Expiry = new(2027, 12, 31);

        [Fact]
        public void Key_matches_independently_computed_golden_value()
        {
            // SHA256("ABCD1234EFAB5678" + "20271231" + salt)[0..20], computed outside the code base.
            Assert.Equal("DBAE98173265E73CEB9B", new CoreLicenseService().GenerateKey(Hwid, Expiry));
            Assert.Equal("DBAE98173265E73CEB9B|2027-12-31", new CoreLicenseService().BuildLicenseString(Hwid, Expiry));
        }

        [Fact]
        public void Generated_license_is_accepted_for_its_hardware_id()
        {
            var s = new CoreLicenseService();
            Assert.True(s.VerifyLicenseString(Hwid, s.BuildLicenseString(Hwid, Expiry), new DateTime(2026, 10, 4)));
            Assert.True(s.VerifyLicenseString(Hwid.ToLowerInvariant(), " " + s.BuildLicenseString(Hwid, Expiry).ToLowerInvariant() + " ", new DateTime(2026, 10, 4)));
        }

        [Fact]
        public void License_is_rejected_for_another_machine_after_expiry_or_when_tampered()
        {
            var s = new CoreLicenseService();
            var lic = s.BuildLicenseString(Hwid, Expiry);
            Assert.False(s.VerifyLicenseString("0000000000000000", lic, new DateTime(2026, 10, 4)));
            Assert.False(s.VerifyLicenseString(Hwid, lic, new DateTime(2028, 1, 1)));
            Assert.False(s.VerifyLicenseString(Hwid, lic.Replace("|2027-12-31", "|2030-12-31"), new DateTime(2026, 10, 4)));
            Assert.False(s.VerifyLicenseString(Hwid, "X" + lic[1..], new DateTime(2026, 10, 4)));
            Assert.False(s.VerifyLicenseString(Hwid, "no-separator", new DateTime(2026, 10, 4)));
            Assert.False(s.VerifyLicenseString(Hwid, "", new DateTime(2026, 10, 4)));
        }

        [Fact]
        public void Local_hardware_id_has_the_format_shown_in_the_activation_window()
        {
            Assert.Matches("^[0-9A-F]{16}$", new CoreLicenseService().GetHardwareId());
        }
    }
}
