using System.IO;
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
        foreach (var bind in new[] { "SignAsOfficerCommand", "CopyAnchorCodeCommand", "ExportAnchorCommand", "ManageOfficersCommand", "VerifySignedReportCommand",
                                     "ImportOfficerTrustCommand", "ShowStationAnchorHintCommand", "PreviousAnchorCode", "StationAnchorHint", "AnchorLine", "OfficerStatus", "VerifyFingerprint" })
            Assert.Contains("{Binding " + bind, xaml);
        Assert.Contains("Content=\"Semnează ca ofițer de securitate\"", xaml);
        Assert.Contains("Content=\"Copiază codul\"", xaml);
        Assert.DoesNotContain("parola", xaml, System.StringComparison.OrdinalIgnoreCase);   // no passphrase key file anywhere on the page
    }

    [Fact]
    public void Before_a_control_the_report_is_not_official_has_no_anchor_and_the_previous_code_is_empty()
    {
        var vm = new StationControlViewModel();
        Assert.False(vm.HasAnchor);
        Assert.Equal("", vm.AnchorLine);
        Assert.Equal("", vm.PreviousAnchorCode);                                           // never pre-filled from the station
        Assert.StartsWith(LogAnalyzer.Dfir.Windows.Audit.ControlReportPdf.OfficialLabel, vm.OfficerStatus);
        Assert.Contains("procesul-verbal", vm.PreviousAnchorHelp);
        vm.CopyAnchorCodeCommand.Execute(null);
        Assert.Contains("Rulați întâi controlul", vm.Status);
        vm.SignAsOfficerCommand.Execute(null);
        Assert.Contains("Rulați întâi controlul", vm.Status);
    }
}
