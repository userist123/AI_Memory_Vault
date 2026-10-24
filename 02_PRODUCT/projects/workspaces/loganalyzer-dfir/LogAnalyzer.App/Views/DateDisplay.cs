using System;
using System.Globalization;
using System.Windows.Data;

namespace LogAnalyzer.UI.Views
{
    /// <summary>Display-only dates (row 14): the register stores "yyyy-MM-dd HH:mm"; the grid shows dd.MM.yyyy HH:mm. The stored value is never changed.</summary>
    public static class DateDisplay
    {
        private static readonly string[] Formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"];

        public static string Transfer(string stored)
        {
            var s = (stored ?? "").Trim();
            if (!DateTime.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return stored ?? "";
            return s.Length <= 10 ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : d.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }
    }

    public sealed class TransferDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => DateDisplay.Transfer(value as string ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
