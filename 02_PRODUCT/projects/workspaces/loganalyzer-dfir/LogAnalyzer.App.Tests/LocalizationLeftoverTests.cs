using System.IO;
using System.Linq;
using System.Xml.Linq;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Evaluation 2026-11-05: English left on screen („High”, „Removable Media”, „Show Calendar”) and unnamed fields in the scope dialog.</summary>
public sealed class LocalizationLeftoverTests
{
    private static string AppDir()
    {
        for (var d = new DirectoryInfo(System.AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return Path.Combine(d.FullName, "LogAnalyzer.App");
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    [Theory]
    [InlineData("High", "Ridicat")]
    [InlineData("critical", "Critic")]
    [InlineData("Informational", "Informativ")]
    public void The_english_severity_of_the_older_engines_is_shown_in_romanian(string raw, string start)
    {
        var text = (string)SeverityTextConverter.Instance.Convert(raw, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(start, text, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(raw, text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_severity_text_is_shown_as_written_not_dropped() =>
        Assert.Equal("Necunoscut", SeverityTextConverter.Instance.Convert("Necunoscut", typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("Removable Media", "Suporturi de stocare amovibile")]
    [InlineData("Credential Access", "Accesarea credențialelor")]
    [InlineData("Local Privilege Escalation", "Escaladarea privilegiilor locale")]
    public void The_english_category_of_the_older_engines_is_shown_in_romanian(string raw, string ro) =>
        Assert.Equal(ro, CategoryTextConverter.Instance.Convert(raw, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void No_older_page_binds_a_raw_severity_for_display()
    {
        foreach (var v in new[] { "AlertsAndRulesView", "DashboardView", "IncidentTimelineView", "KerberosAndLateralMovementView", "InvestigationView" })
            Assert.DoesNotContain("Binding Severity}", File.ReadAllText(Path.Combine(AppDir(), "Views", v + ".xaml")));
    }

    [Fact]
    public void Every_field_of_the_scope_dialog_has_a_romanian_name()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Views", "ScopeDialog.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var fields = doc.Descendants().Where(e => e.Name.LocalName is "TextBox" or "DatePicker" or "ComboBox" && e.Attribute(x + "Name") is not null).ToList();
        Assert.Equal(10, fields.Count);
        Assert.All(fields, f => Assert.False(string.IsNullOrWhiteSpace((string?)f.Attribute("AutomationProperties.Name")), (string?)f.Attribute(x + "Name")));
    }

    [Fact]
    public void A_recent_case_row_is_announced_by_its_title_not_by_the_record_and_its_path()
    {
        var row = new LogAnalyzer.Dfir.Case.RecentCaseView(new LogAnalyzer.Dfir.Case.RecentCase(@"C:\Users\user\AppData\Local\LogAnalyzer\Cases\CASE-1", "Caz test", System.DateTimeOffset.UtcNow, "Active"), true);
        Assert.StartsWith("Caz test, ", row.ToString());
        Assert.DoesNotContain("RecentCase", row.ToString());
        Assert.DoesNotContain(@"C:\Users", row.ToString());
    }

    [Fact]
    public void The_calendar_button_name_is_romanian() => Assert.Equal("Alege data din calendar", LogAnalyzer.UI.Services.DatePickerNames.CalendarButton);
}
