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
        Assert.Contains("toate canalele de transfer aprobate în profilul de proceduri sunt de alt tip decât un suport de stocare", c.Detail);
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

    private static ControlCheck P28(List<string>? media, bool deny = false)
    {
        var f = new StationFacts { Posture = StationPosture.Standalone, IsAdministrator = true, ApprovedTransferMedia = media };
        f.Settings["Optical.WriterCount"] = "1"; f.Settings["Optical.WriterNames"] = "DVD-RW Test"; f.Settings["Optical.DriveCount"] = "1";
        f.Settings["Optical.DenyWrite"] = deny ? "1" : "0";
        return ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "P28");
    }

    [Fact]
    public void An_unblocked_disc_writer_is_non_conform_unless_the_profile_approves_a_disc_channel()
    {
        Assert.Equal(ControlStatus.Neconform, P28(null).Status);
        Assert.Equal(ControlStatus.Neconform, P28(["Stick USB marcat", "Procedură abcd"]).Status);   // "cd" inside a word is not a disc channel
        var approved = P28(["Export pe CD inscripționat aprobat"]);
        Assert.Equal(ControlStatus.DeVerificat, approved.Status);
        Assert.Contains("registrul mediilor", approved.Recommendation);
        Assert.Contains(approved.Evidence, e => e.StartsWith("Profil, canal aprobat: Export pe CD"));
        Assert.Equal(ControlStatus.Conform, P28(null, deny: true).Status);
    }
}