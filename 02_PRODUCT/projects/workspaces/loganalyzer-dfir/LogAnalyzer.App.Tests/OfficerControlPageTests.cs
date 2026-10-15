using System.IO;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Owner decisions 2026-10-16 on the control page: one button to sign as officer, the anchor code with a copy button, the previous code field and "Verifică raport".</summary>
public sealed class OfficerControlPageTests
{
    private static string View()
    {
        for (var d = new DirectoryInfo(System.AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return File.ReadAllText(Path.Combine(d.FullName, "LogAnalyzer.App", "Views", "StationControlView.xaml"));
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    [Fact]
    public void The_control_page_offers_signing_anchor_copy_export_registration_and_verification()
    {
        var xaml = View();
        foreach (var bind in new[] { "SignAsOfficerCommand", "CopyAnchorCodeCommand", "ExportAnchorCommand", "RegisterOfficerCommand", "VerifyReportCommand",
                                     "PreviousAnchorCode", "AnchorLine", "OfficerStatus", "VerifyFingerprint" })
            Assert.Contains("{Binding " + bind, xaml);
        Assert.Contains("Content=\"Semnează ca ofițer de securitate\"", xaml);
        Assert.Contains("Content=\"Copiază codul\"", xaml);
    }

    [Fact]
    public void Before_a_control_the_report_is_unsigned_and_has_no_anchor()
    {
        var vm = new StationControlViewModel();
        Assert.False(vm.HasAnchor);
        Assert.Equal("", vm.AnchorLine);
        Assert.StartsWith(OfficerSignature.UnsignedLabel, vm.OfficerStatus);
        Assert.Contains("procesul-verbal", vm.PreviousAnchorHelp);
        vm.CopyAnchorCodeCommand.Execute(null);
        Assert.Contains("Rulați întâi controlul", vm.Status);
        vm.SignAsOfficerCommand.Execute(null);
        Assert.Contains("Rulați întâi controlul", vm.Status);
    }
}
