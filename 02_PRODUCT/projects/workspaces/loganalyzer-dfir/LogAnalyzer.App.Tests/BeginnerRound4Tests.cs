using System.IO;
using System.Linq;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Real-PC findings, round 4 (2026-10-23): duplicate sentences, fixed language switch, plural, expanders, labels, Bluetooth maker.</summary>
[Collection(GlobalStateCollection.Name)]
public sealed class BeginnerRound4Tests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("LogAnalyzer.App not found above " + AppContext.BaseDirectory);
    }
    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));

    [Fact]
    public void Home_says_what_happens_once_the_hint_state_adds_nothing()
    {
        var vm = new StationControlViewModel();
        Assert.Equal("", vm.HomeStatus);
        Assert.Contains("Ce se întâmplă", HomeViewModel.WhatHappensQuickControlText);
    }

    [Fact]
    public void The_language_switch_is_left_of_nothing_that_appears_in_Expert()
    {
        var main = Src("MainWindow.xaml");
        Assert.True(main.IndexOf("Content=\"Colectează tot\"", StringComparison.Ordinal) < main.IndexOf("Nivel de limbaj: Simplu sau Expert", StringComparison.Ordinal),
            "Colectează tot must come before (left of) the language switch so showing it never moves the switch");
    }

    [Fact]
    public void Anchor_step_says_it_once_and_the_bottom_hint_is_short()
    {
        var view = Src("Views", "StationControlView.xaml");
        Assert.DoesNotContain("<TextBlock Text=\"{Binding PreviousAnchorHelp}\"", view);
        Assert.Contains("{Binding AnchorWhyText}", view);
        Assert.Contains("ToolTip=\"{Binding PreviousAnchorHelp}\"", view);
        Assert.Contains("Folosește codul propus de stație (nesigur)", view);
        Assert.True(StationControlViewModel.AnchorWhy.Length < 150);
        var vm = new StationControlViewModel();
        vm.FlowNextCommand.Execute(null);
        Assert.Equal(1, vm.FlowStepIndex);
        Assert.Equal("Lăsați gol dacă nu aveți procesul-verbal, apoi apăsați „Înainte”.", vm.Status);
    }

    [Fact]
    public void Investigation_in_Simplu_hides_the_collect_banner_and_the_case_buttons_until_a_result()
    {
        var inv = Src("Views", "InvestigationView.xaml");
        Assert.Contains("<ctrl:NoDataPanel HideInSimpleLevel=\"True\"/>", inv);
        var doc = XDocument.Parse(inv);
        var right = doc.Descendants().First(e => e.Name.LocalName == "StackPanel" && e.Elements().Any(c => (string?)c.Attribute("Content") == "Folder caz"));
        var xml = right.ToString();
        Assert.Contains("IsSimple", xml);
        Assert.Contains("Binding HasResult", xml);
    }

    [Theory]
    [InlineData(0, false, "0 de termeni")]
    [InlineData(1, true, "1 termen potrivit")]
    [InlineData(1, false, "1 termen")]
    [InlineData(2, true, "2 termeni potriviți")]
    [InlineData(19, true, "19 termeni potriviți")]
    [InlineData(20, true, "20 de termeni potriviți")]
    [InlineData(101, true, "101 termeni potriviți")]
    [InlineData(112, false, "112 termeni")]
    [InlineData(188, false, "188 de termeni")]
    public void Glossary_count_uses_the_Romanian_plural(int n, bool matching, string expected) => Assert.Equal(expected, GlossarySearch.TermCount(n, matching));

    [Theory]
    [InlineData("Views/RegisterView.xaml")]
    [InlineData("Views/ProcedureProfileView.xaml")]
    [InlineData("Views/PolicyView.xaml")]
    public void Avansat_expander_is_collapsed_and_sits_after_the_toolbar(string path)
    {
        var s = Src(path.Split('/'));
        var exp = s.IndexOf("<Expander Header=\"Avansat\" IsExpanded=\"False\"", StringComparison.Ordinal);
        Assert.True(exp > 0);
        Assert.True(exp > s.IndexOf("</WrapPanel>", StringComparison.Ordinal), "the expander must not sit directly above the toolbar it does not contain");
    }

    [Fact]
    public void Policies_and_users_have_one_primary_open_button()
    {
        var pol = Src("Views", "PolicyView.xaml");
        Assert.Equal(1, pol.Split("Content=\"Deschide fișier…\"").Length - 1);
        Assert.DoesNotContain("Content=\"Deschide fișier…\" Command=\"{Binding OpenFileCommand}\" Style=\"{StaticResource AccentButtonStyle}\"", pol);
        var reg = Src("Views", "RegisterView.xaml");
        Assert.Equal(1, reg.Split("Content=\"Preia de pe stație\"").Length - 1);
    }

    [Fact]
    public void Transfer_labels_use_the_same_wording()
    {
        var t = Src("Views", "TransferRegisterView.xaml");
        Assert.Contains("Text=\"Sursă (structura)\"", t);
        Assert.Contains("Text=\"Destinație (structura) *\"", t);
        Assert.DoesNotContain("structura / departamentul", t);
    }

    [Theory]
    [InlineData(DeviceClass.Bluetooth, "Microsoft", "", "—")]
    [InlineData(DeviceClass.BluetoothAdapter, "microsoft", "", "—")]
    [InlineData(DeviceClass.Bluetooth, "Microsoft", "05AC:0256", "Microsoft")]
    [InlineData(DeviceClass.Bluetooth, "Logitech", "", "Logitech")]
    [InlineData(DeviceClass.UsbStorage, "Microsoft", "", "Microsoft")]
    public void Bluetooth_driver_provider_is_not_shown_as_the_maker(DeviceClass cls, string maker, string vidPid, string expected)
        => Assert.Equal(expected, DeviceMonitorRow.ManufacturerDisplay(cls, maker, vidPid));
}
