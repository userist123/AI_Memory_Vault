using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>
    /// Shows an element only above (parameter "1280") or only below (parameter "&lt;1280") a window width. WPF has no media queries; the header
    /// uses this to fold its auxiliary labels into icons on a narrow window (docs/design/DESIGN.md: the window fits whole at 1000 px).
    /// A value that is not a number, or a parameter that is not a threshold, shows the element: a layout rule must never hide a control by mistake.
    /// </summary>
    public sealed class WidthThresholdConverter : IValueConverter
    {
        /// <summary>The width from which the header shows its full labels.</summary>
        public const double Wide = 1280;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            IsVisible(value is double w ? w : double.NaN, parameter as string) ? Visibility.Visible : Visibility.Collapsed;

        internal static bool IsVisible(double width, string? parameter)
        {
            if (double.IsNaN(width) || string.IsNullOrWhiteSpace(parameter)) return true;
            bool below = parameter.StartsWith('<');
            if (!double.TryParse(below ? parameter[1..] : parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)) return true;
            return below ? width < threshold : width >= threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
