using System;
using System.Windows.Data;
using System.Windows.Markup;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.Views
{
    /// <summary>Builds the binding to the text table; the text updates when the language changes (no restart).</summary>
    internal static class LocBinding
    {
        public static Binding Create(string key)
        {
            return new Binding("[" + key + "]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };
        }
    }

    /// <summary>
    /// XAML access to the resource layer (WP6b, U17): <c>{loc:T home.title}</c> is the text of that key in the current language, bound to the language
    /// setting. Identifiers (rule ids, event ids, channel names, file names, hashes) are data and never go through it.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class TExtension : MarkupExtension
    {
        public TExtension() { }
        public TExtension(string key) { Key = key; }

        [ConstructorArgument("key")]
        public string Key { get; set; } = "";

        public override object ProvideValue(IServiceProvider serviceProvider) => LocBinding.Create(Key).ProvideValue(serviceProvider);
    }
}

namespace LogAnalyzer.UI.Views
{
    /// <summary>Formats a localised pattern (<c>"Sesiune: {0}"</c> / <c>"Session: {0}"</c>) with a bound value; the pattern follows the language setting.</summary>
    public sealed class LocFormatConverter : System.Windows.Data.IMultiValueConverter
    {
        public static LocFormatConverter Instance { get; } = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Length == 0 || values[0] is not string pattern) return "";
            object? arg = values.Length > 1 && values[1] != System.Windows.DependencyProperty.UnsetValue ? values[1] : null;
            try { return string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, arg ?? ""); }
            catch (FormatException) { return pattern; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// <c>{loc:F key, Path=Entry.Title}</c>: the pattern of <c>key</c> (e.g. <c>"Sesiune: {0}"</c>) in the current language, filled with the bound value.
    /// The value (a name, a path, a date, a count) is data and is never translated.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class FExtension : MarkupExtension
    {
        public FExtension() { }
        public FExtension(string key) { Key = key; }

        [ConstructorArgument("key")]
        public string Key { get; set; } = "";
        public string Path { get; set; } = ".";

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var mb = new System.Windows.Data.MultiBinding { Converter = LocFormatConverter.Instance, Mode = System.Windows.Data.BindingMode.OneWay };
            mb.Bindings.Add(LocBinding.Create(Key));
            mb.Bindings.Add(new System.Windows.Data.Binding(Path) { Mode = System.Windows.Data.BindingMode.OneWay });
            return mb.ProvideValue(serviceProvider);
        }
    }
}
