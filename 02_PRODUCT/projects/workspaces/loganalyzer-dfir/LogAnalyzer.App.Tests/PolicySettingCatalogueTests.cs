using System.Text.RegularExpressions;
using System.Windows;
using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Policy;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Owner 2026-10-12: „și la politici la fel ca la restul, ce face fiecare politică în parte". Every setting the policy engine can name from a
/// closed list (account policy keys, audit subcategories, the services and registry values the explainer knows) has a catalogue entry with every
/// field filled, written in Romanian with comma-below diacritics; the Policies page shows it for the selected row and copies it.
/// </summary>
public sealed class PolicySettingCatalogueTests
{
    private static SettingRef Reg(string key, string name, string hive = "HKLM") => new("registry", hive, key, name, "dword");
    private static ControlPlan Row(SettingRef s, string desired, string? desiredValue, SettingValue? current, string? readError = null) =>
        new("C-1", "titlu", s, desired, desiredValue, current, readError, ControlState.NonCompliant, PlannedAction.Set, "", "high", "");

    [Fact]
    public void Every_account_policy_key_the_engine_reads_has_an_entry()
    {
        var missing = PolicyImport.SecpolNames.Where(n => PolicySettingCatalogue.Find(new SettingRef("secpol", "", "", n, "")) is null).ToList();
        Assert.True(missing.Count == 0, "secpol without an entry: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_audit_subcategory_has_an_entry_by_name_and_by_guid()
    {
        var missing = new List<string>();
        foreach (var (name, guid) in AuditSubcategories.ByName)
        {
            var byName = PolicySettingCatalogue.Find(new SettingRef("audit", "", "", name, ""));
            var byGuid = PolicySettingCatalogue.Find(new SettingRef("audit", "", "", "{" + guid + "}", ""));
            if (byName is null || byGuid is null || byName.Id != byGuid.Id) missing.Add(name);
        }
        Assert.True(missing.Count == 0, "audit subcategories without an entry: " + string.Join(", ", missing));
        Assert.Equal(AuditSubcategories.ByName.Count, PolicySettingCatalogue.All.Count(g => g.Id.StartsWith("audit:")));
    }

    [Fact]
    public void Every_known_service_has_an_entry_as_a_service_and_as_its_registry_start_value()
    {
        var missing = CodeExplainer.KnownServices.Where(n =>
            PolicySettingCatalogue.Find(new SettingRef("service", "", "", n, "")) is null
            || PolicySettingCatalogue.Find(Reg($@"SYSTEM\CurrentControlSet\Services\{n}", "Start"))?.Id != $"service:{n}").ToList();
        Assert.True(missing.Count == 0, "services without an entry: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_registry_value_the_explainer_knows_is_in_the_catalogue_or_listed_as_information_only()
    {
        var missing = new List<string>();
        foreach (var code in CodeExplainer.KnownRegistryCodes)
        {
            var cut = code.LastIndexOf('\\');
            if (PolicySettingCatalogue.InformationalOnly.ContainsKey(code)) continue;
            if (PolicySettingCatalogue.Find(Reg(code[..cut], code[(cut + 1)..])) is null) missing.Add(code);
        }
        Assert.True(missing.Count == 0, "registry codes neither in the catalogue nor information-only: " + string.Join(", ", missing));
        Assert.All(PolicySettingCatalogue.InformationalOnly, kv => Assert.Contains(kv.Key, CodeExplainer.KnownRegistryCodes));   // no stale exemption
    }

    [Fact]
    public void Real_policy_keys_find_their_entry()
    {
        Assert.Equal(@"registry:Policies\System\EnableLUA", PolicySettingCatalogue.Find(Reg(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA"))?.Id);
        Assert.Equal(@"registry:RemovableStorageDevices\{\Deny_Write", PolicySettingCatalogue.Find(Reg(@"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}", "Deny_Write"))?.Id);
        Assert.Equal(@"registry:RemovableStorageDevices\Deny_All", PolicySettingCatalogue.Find(Reg(@"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices", "Deny_All"))?.Id);
        Assert.Equal(@"registry:Firewall\EnableFirewall", PolicySettingCatalogue.Find(Reg(@"SOFTWARE\Policies\Microsoft\WindowsFirewall\PublicProfile", "EnableFirewall"))?.Id);
        Assert.Equal(@"registry:Terminal S\fDenyTSConnections", PolicySettingCatalogue.Find(Reg(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "fDenyTSConnections"))?.Id);
        Assert.Null(PolicySettingCatalogue.Find(Reg(@"SOFTWARE\Policies\Contoso\App", "EnableLUA")));   // same value name, unrelated key
    }

    [Fact]
    public void Every_entry_has_every_field_filled_and_a_unique_id()
    {
        foreach (var g in PolicySettingCatalogue.All)
            foreach (var (field, value) in new[] { ("Name", g.Name), ("What", g.What), ("Why", g.Why), ("Recommended", g.Recommended), ("Where", g.Where), ("ForIt", g.ForIt) })
                Assert.False(string.IsNullOrWhiteSpace(value), $"{g.Id}: {field} is empty");
        var dup = PolicySettingCatalogue.All.GroupBy(g => g.Id).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
        Assert.True(dup.Count == 0, "duplicate ids: " + string.Join(", ", dup));
        Assert.True(PolicySettingCatalogue.All.Count >= 7 + 59 + 16 + 40, $"catalogue has {PolicySettingCatalogue.All.Count} entries");
    }

    [Fact]
    public void Romanian_text_uses_comma_below_diacritics_and_never_the_cedilla_forms()
    {
        var cedilla = new Regex("[ŞşŢţ]");
        var diacritic = new Regex("[ăâîșțĂÂÎȘȚ]");
        foreach (var g in PolicySettingCatalogue.All)
        {
            var all = string.Join(" ", g.Name, g.What, g.Why, g.Recommended, g.Where, g.ForIt, string.Join(" ", g.Values.Values), g.AbsentMeaning);
            Assert.False(cedilla.IsMatch(all), $"{g.Id}: cedilla ş/ţ instead of ș/ț");
            Assert.True(diacritic.IsMatch(g.What + g.Why + g.Recommended), $"{g.Id}: Romanian text without diacritics");
        }
    }

    [Fact]
    public void Where_the_procedure_decides_the_recommendation_says_so_and_the_explanation_shows_the_policy_value()
    {
        var lockout = PolicySettingCatalogue.Find(new SettingRef("secpol", "", "", "LockoutDuration", ""))!;
        Assert.Contains(PolicySettingCatalogue.ProcedureValue, lockout.Recommended);

        var e = PolicySettingCatalogue.Explain(Row(new SettingRef("secpol", "", "", "LockoutBadCount", ""), "5", "5", new SettingValue("0", null)));
        Assert.True(e.InCatalogue);
        Assert.Contains("procedura dumneavoastră", e.Required);
        Assert.Contains(": 5", e.Required);
        Assert.Contains("nu se blochează niciodată", e.CurrentMeaning);
        Assert.Contains("Ce face:", e.ToPlainText());
        Assert.Contains("Pentru IT:", e.ToPlainText());
    }

    [Fact]
    public void Current_value_is_explained_for_audit_service_absent_and_unreadable()
    {
        var audit = PolicySettingCatalogue.Explain(Row(new SettingRef("audit", "", "", "Removable Storage", ""), "Success and Failure", "Success and Failure", new SettingValue("No Auditing", null)));
        Assert.Contains("nu se înregistrează nimic", audit.CurrentMeaning);
        Assert.Contains("și reușitele, și încercările eșuate", audit.Required);

        var svc = PolicySettingCatalogue.Explain(Row(new SettingRef("service", "", "", "bthserv", ""), "4", "4", SettingValue.Absent));
        Assert.Contains("nu există", svc.CurrentMeaning);

        var unreadable = PolicySettingCatalogue.Explain(Row(new SettingRef("audit", "", "", "Logon", ""), "Success", "Success", null, "acces refuzat"));
        Assert.Contains("nu a putut fi citită", unreadable.CurrentMeaning);
    }

    [Fact]
    public void A_registry_value_outside_the_catalogue_gets_an_honest_generic_explanation()
    {
        var e = PolicySettingCatalogue.Explain(Row(Reg(@"SOFTWARE\Policies\Contoso\App", "Mode"), "3", "3", new SettingValue("1", "dword")));
        Assert.False(e.InCatalogue);
        Assert.Contains("nu este în catalogul aplicației", e.Why);
        Assert.Contains(PolicySettingCatalogue.ProcedureValue, e.Recommended);
        Assert.Equal("Stația are acum 1.", e.CurrentMeaning);
        Assert.Contains(@"HKLM\SOFTWARE\Policies\Contoso\App\Mode", e.ForIt);
    }

    [Fact]
    public void The_row_tooltip_is_the_one_line_what_it_does()
    {
        var c = new PolicySettingOneLineConverter();
        var text = (string)c.Convert(Reg(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoAutorun"), typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("Oprirea comenzilor autorun.inf:", text);
    }

    [Fact]
    public void Selecting_a_plan_row_shows_the_explanation_and_copy_without_a_selection_says_what_to_do()
    {
        var vm = new PolicyViewModel();
        Assert.False(vm.HasSelectedSetting);
        vm.CopyExplanationCommand.Execute(null);
        Assert.Contains("Alegeți întâi", vm.Status);
        vm.SelectedPlanRow = Row(new SettingRef("secpol", "", "", "MinimumPasswordLength", ""), "14", "14", new SettingValue("8", null));
        Assert.True(vm.HasSelectedSetting);
        Assert.Equal("Lungimea minimă a parolei", vm.SelectedSettingExplanation!.Name);
        vm.SelectedPlanRow = null;
        Assert.False(vm.HasSelectedSetting);
    }

    [Fact]
    public void The_policies_page_loads_with_the_theme_and_shows_the_selected_explanation()
    {
        var shown = Wp18AccessibilityTests.OnSta(() =>
        {
            var vm = new PolicyViewModel();
            var row = Row(new SettingRef("audit", "", "", "Process Creation", ""), "Success", "Success", new SettingValue("No Auditing", null));
            vm.PlanRows.Add(row);
            vm.SelectedPlanRow = row;
            var view = new PolicyView { DataContext = vm };
            var host = new System.Windows.Controls.Border { Width = 1400, Child = view };
            host.Measure(new Size(1400, 1200));
            host.Arrange(new Rect(0, 0, 1400, 1200));
            host.UpdateLayout();
            return Texts(view).ToList();
        });
        Assert.Contains("Ce face", shown);
        Assert.Contains("De ce contează pe o stație izolată", shown);
        Assert.Contains(shown, t => t.StartsWith("Înregistrează în jurnalul de securitate fiecare program pornit"));
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TextBlock { Visibility: Visibility.Visible } tb && tb.ActualWidth > 0) yield return tb.Text;   // collapsed panels are not laid out
            foreach (var t in Texts(child)) yield return t;
        }
    }
}
