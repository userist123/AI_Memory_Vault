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
    }

    /// <summary>
    /// Visibility of an „Avansat” entry: <c>Binding RoleNavigation</c>, <c>ConverterParameter</c> = the tab, optionally <c>;air</c> / <c>;net</c> for the entries that exist in one
    /// operating mode only (the mode is fixed at start-up, like the view model's IsAirGappedMode / IsNetworkMode).
    /// </summary>
    public sealed class AdvancedNavVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parts = (parameter as string ?? "").Split(';');
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tab)) return Visibility.Visible;
            if (parts.Length > 1 && ((parts[1] == "air") != AppModeContext.IsAirGapped)) return Visibility.Collapsed;
            return value is IReadOnlyList<NavigationEntry> nav && !AdvancedNav.IsShown(nav, tab) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
