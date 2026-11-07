using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// P14 (research 2026-11-07, plan P2.8): the verdict on unblocked USB storage follows the procedure profile. Before, both branches were DE VERIFICAT and the
/// check could never say NECONFORM.
/// </summary>
public sealed class UsbBlockingByProfileTests
{
    private static ControlCheck P14(bool blocked, List<string>? media)
    {
        var f = new StationFacts { Posture = StationPosture.Standalone, IsAdministrator = true, ApprovedTransferMedia = media };
        f.Settings["USBSTOR.Start"] = blocked ? "4" : "3";
        return ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "P14");
    }

    [Fact]
    public void Unblocked_USB_when_the_profile_approves_no_removable_channel_is_non_conform()
    {
        var c = P14(false, ["Imprimantă dedicată hârtie", "CD inscripționat aprobat"]);
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("nu aprobă niciun canal de transfer pe USB", c.Detail);
        Assert.Contains(c.Evidence, e => e == "Profil, canal aprobat: CD inscripționat aprobat");
    }

    [Fact]
    public void Unblocked_USB_with_an_approved_stick_channel_leaves_the_media_register_to_decide()
    {
        var c = P14(false, ["Transfer clasificat Stick USB marcat"]);
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("D01–D04", c.Detail);
    }

    [Fact]
    public void No_profile_channels_is_to_be_checked_and_blocked_USB_is_conform_whatever_the_profile()
    {
        Assert.Equal(ControlStatus.DeVerificat, P14(false, null).Status);
        Assert.Contains("nu definește canalele", P14(false, null).Detail);
        Assert.Equal(ControlStatus.Conform, P14(true, null).Status);
        Assert.Equal(ControlStatus.Conform, P14(true, ["Imprimantă"]).Status);
    }
}
