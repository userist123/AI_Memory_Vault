using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests;

/// <summary>A [Fact] that runs only when LADFIR_LICENSE_FILE names a licence file on this station (the owner's issued licence; never in git).</summary>
public sealed class OwnerLicenseFactAttribute : FactAttribute
{
    public OwnerLicenseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LADFIR_LICENSE_FILE") is not { Length: > 0 } p || !File.Exists(p))
            Skip = "Licența proprietarului nu e indicată: setați LADFIR_LICENSE_FILE la fișierul license.lic emis pentru această stație.";
    }
}

/// <summary>
/// The licence issued for this station is accepted by the application as built: the public key compiled into LogAnalyzer.Core
/// (Licensing/license-public-keys.txt) verifies it against the station's real Hardware ID. Nothing is printed but the result.
/// </summary>
public sealed class OwnerLicenseLiveTests
{
    [OwnerLicenseFact]
    public void The_issued_licence_is_accepted_with_the_embedded_public_key_on_this_station()
    {
        Assert.NotEmpty(LicenseService.EmbeddedPublicKeys());
        var service = new LicenseService();
        var licence = File.ReadAllText(Environment.GetEnvironmentVariable("LADFIR_LICENSE_FILE")!).Trim();
        Assert.True(service.VerifyLicenseString(service.GetHardwareId(), licence, DateTime.UtcNow));
    }
}
