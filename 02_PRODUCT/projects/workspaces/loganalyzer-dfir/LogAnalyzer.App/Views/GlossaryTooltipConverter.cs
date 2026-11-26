using System;
using System.Globalization;
using System.Windows.Data;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// WP6a for the generated „Avansat” menu: a glossary key (ModuleCatalog.PageEntry.Term) becomes the technical name with its explanation, the same text as
    /// <c>{views:Term key, Tooltip=True}</c>; no key gives no tooltip.
    /// </summary>
    public sealed class GlossaryTooltipConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string term && term.Length > 0 ? Glossary.Tooltip(term) : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
