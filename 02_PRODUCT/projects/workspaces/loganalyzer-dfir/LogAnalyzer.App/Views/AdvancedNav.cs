using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// The „Avansat” menu (row 20 of the 2026-10-22 beginner walkthrough): it lists only the pages that are not already under „Pagini principale” for this station's role,
    /// so no page appears twice in the sidebar.
    /// </summary>
    public static class AdvancedNav
    {
        public static bool IsShown(IReadOnlyList<NavigationEntry> roleNavigation, int tab) => !roleNavigation.Any(n => n.Tab == tab);


        /// <summary>
        /// Simplu hides the „Avansat” menu; if the page open when the level moves to Simplu is one of its pages (not Acasă, not a main page of the role),
        /// the person lands on Acasă so no hidden page stays selected. Expert, and any main page, stay where they are.
        /// </summary>
        public static int TabAfterLevelChange(IReadOnlyList<NavigationEntry> roleNavigation, int selectedTab, bool simple) =>
            simple && selectedTab != RoleProfiles.TabHome && IsShown(roleNavigation, selectedTab) ? RoleProfiles.TabHome : selectedTab;
    }

    /// <summary>
    /// Visibility of an „Avansat” entry: <c>Binding RoleNavigation</c>, <c>ConverterParameter</c> = the page key of <see cref="ModuleCatalog"/> (the tab, or "8.local" / "8.ad").
    /// The catalog decides whether the module has the page at all and which variant the mode shows; the role decides whether it is already a main page.
    /// An unknown key is never shown (fail closed), so a page missing from the catalog cannot leak into a module.
    /// </summary>
    public sealed class AdvancedNavVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            IsVisible(value as IReadOnlyList<NavigationEntry>, parameter as string ?? "", StationPostureContext.Current,
                      AppModeContext.IsAirGapped ? AppMode.AirGapped : AppMode.Network) ? Visibility.Visible : Visibility.Collapsed;

        public static bool IsVisible(IReadOnlyList<NavigationEntry>? roleNavigation, string key, StationPosture module, AppMode mode)
        {
            if (ModuleCatalog.Find(key) is not { } page || !ModuleCatalog.IsListed(page, module, mode)) return false;
            return roleNavigation is null || AdvancedNav.IsShown(roleNavigation, page.Tab);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
