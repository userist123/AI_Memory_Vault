using System.IO;
using System.Linq;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>The optional "user-hives" artifact of the remote collection package: the checkbox of the "Colectare la distanță" tab and the list it sends to Authorize.</summary>
[Collection(GlobalStateCollection.Name)]
public sealed class RemoteUserHivesOptionTests
{
    private const string Label = "Include hive-urile utilizatorilor (NTUSER.DAT, UsrClass.dat: istoric personal)";

    private static string AppFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var f = Path.Combine([dir.FullName, "LogAnalyzer.App", .. parts]);
            if (File.Exists(f)) return File.ReadAllText(f);
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(string.Join("/", parts) + " not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void The_checkbox_exists_is_unticked_by_default_and_carries_an_automation_name()
    {
        var doc = XDocument.Parse(AppFile("Views", "InvestigationView.xaml"));
        var box = doc.Descendants().Single(e => e.Name.LocalName == "CheckBox" && (string?)e.Attribute("Content") == Label);
        Assert.Equal(Label, (string?)box.Attributes().Single(a => a.Name.LocalName == "AutomationProperties.Name").Value);
        Assert.Equal("{Binding RemoteIncludeUserHives}", (string?)box.Attribute("IsChecked"));
        // it sits in the "Colectare la distanță" tab
        var tab = box.Ancestors().First(e => e.Name.LocalName == "TabItem");
        Assert.Equal("Colectare la distanță", (string?)tab.Attribute("Header"));

        var vm = new InvestigationViewModel();
        Assert.False(vm.RemoteIncludeUserHives);
    }

    [Fact]
    public void The_list_sent_to_Authorize_differs_with_the_checkbox_and_the_status_says_whether_the_hives_were_included()
    {
        var vm = new InvestigationViewModel();
        var off = InvestigationViewModel.RemoteArtifactList(vm.RemoteIncludeUserHives);
        Assert.DoesNotContain("user-hives", off);
        Assert.Equal(RemoteCollection.KnownArtifacts, off);

        vm.RemoteIncludeUserHives = true;
        var on = InvestigationViewModel.RemoteArtifactList(vm.RemoteIncludeUserHives);
        Assert.Contains("user-hives", on);
        Assert.Equal([.. RemoteCollection.KnownArtifacts, "user-hives"], on);

        Assert.Contains("NU sunt incluse", InvestigationViewModel.RemoteGeneratedStatus("R1", "PC01", "c.ps1", false));
        var yes = InvestigationViewModel.RemoteGeneratedStatus("R1", "PC01", "c.ps1", true);
        Assert.Contains("SUNT incluse", yes);
        Assert.DoesNotContain("NU sunt incluse", yes);
    }

    [Fact]
    public void GenerateRemotePackage_takes_its_list_from_the_checkbox_state()
    {
        var src = AppFile("ViewModels", "InvestigationViewModel.cs");
        Assert.Contains("RemoteArtifactList(includeHives)", src);
        Assert.Contains("var includeHives = RemoteIncludeUserHives;", src);
        Assert.Contains("EndRemoteRequest();", src);
        Assert.DoesNotContain("RemoteCollection.KnownArtifacts);", src.Substring(src.IndexOf("private void GenerateRemotePackage", StringComparison.Ordinal), 900));
    }

    private const string HistoryLabel = "Include istoricul personal (SRUM, fișiere recente)";

    [Fact]
    public void The_personal_history_checkbox_exists_in_the_tab_is_unticked_by_default_and_carries_an_automation_name()
    {
        var doc = XDocument.Parse(AppFile("Views", "InvestigationView.xaml"));
        var box = doc.Descendants().Single(e => e.Name.LocalName == "CheckBox" && (string?)e.Attribute("Content") == HistoryLabel);
        Assert.Equal(HistoryLabel, (string?)box.Attributes().Single(a => a.Name.LocalName == "AutomationProperties.Name").Value);
        Assert.Equal("{Binding RemoteIncludePersonalHistory}", (string?)box.Attribute("IsChecked"));
        Assert.Equal("Colectare la distanță", (string?)box.Ancestors().First(e => e.Name.LocalName == "TabItem").Attribute("Header"));
        Assert.False(new InvestigationViewModel().RemoteIncludePersonalHistory);
    }

    [Fact]
    public void The_personal_history_box_adds_SRUM_and_recent_items_to_the_list_only_when_ticked_and_the_status_says_so()
    {
        var vm = new InvestigationViewModel();
        var off = InvestigationViewModel.RemoteWithPersonalHistory(InvestigationViewModel.RemoteArtifactList(vm.RemoteIncludeUserHives), vm.RemoteIncludePersonalHistory);
        Assert.DoesNotContain("srum", off);
        Assert.DoesNotContain("recent-items", off);
        Assert.Equal(RemoteCollection.KnownArtifacts, off);
        Assert.Contains("amcache", off); Assert.Contains("setupapi", off); Assert.Contains("firewall-log", off);   // the new defaults are in without any box

        vm.RemoteIncludePersonalHistory = true;
        var on = InvestigationViewModel.RemoteWithPersonalHistory(InvestigationViewModel.RemoteArtifactList(vm.RemoteIncludeUserHives), vm.RemoteIncludePersonalHistory);
        Assert.Equal([.. RemoteCollection.KnownArtifacts, "srum", "recent-items"], on);
        vm.RemoteIncludeUserHives = true;
        Assert.Equal([.. RemoteCollection.KnownArtifacts, "user-hives", "srum", "recent-items"],
            InvestigationViewModel.RemoteWithPersonalHistory(InvestigationViewModel.RemoteArtifactList(vm.RemoteIncludeUserHives), vm.RemoteIncludePersonalHistory));

        Assert.Contains("nu este inclus", InvestigationViewModel.RemoteGeneratedStatus("R1", "PC01", "c.ps1", false));
        var yes = InvestigationViewModel.RemoteGeneratedStatus("R1", "PC01", "c.ps1", false, true);
        Assert.Contains("ESTE inclus", yes);
        Assert.DoesNotContain("nu este inclus", yes);
    }

    [Fact]
    public void GenerateRemotePackage_takes_the_personal_history_from_the_box_and_the_choice_goes_back_to_unticked_after_each_request()
    {
        var src = AppFile("ViewModels", "InvestigationViewModel.cs");
        var body = src.Substring(src.IndexOf("private void GenerateRemotePackage", StringComparison.Ordinal), 1400);
        Assert.Contains("var includeHistory = RemoteIncludePersonalHistory;", body);
        Assert.Contains("RemoteWithPersonalHistory(RemoteArtifactList(includeHives), includeHistory)", body);
        Assert.True(body.IndexOf("EndRemoteRequest();", StringComparison.Ordinal) > body.IndexOf("RemoteCollection.Authorize", StringComparison.Ordinal));
        var vm = new InvestigationViewModel { RemoteIncludeUserHives = true, RemoteIncludePersonalHistory = true };
        vm.EndRemoteRequest();
        Assert.False(vm.RemoteIncludeUserHives);
        Assert.False(vm.RemoteIncludePersonalHistory);
    }

    [Fact]
    public void The_choice_applies_to_one_request_only_and_goes_back_to_unticked()
    {
        var vm = new InvestigationViewModel();
        vm.RemoteIncludeUserHives = true;
        vm.EndRemoteRequest();
        Assert.False(vm.RemoteIncludeUserHives);
        Assert.DoesNotContain("user-hives", InvestigationViewModel.RemoteArtifactList(vm.RemoteIncludeUserHives));
    }
}
