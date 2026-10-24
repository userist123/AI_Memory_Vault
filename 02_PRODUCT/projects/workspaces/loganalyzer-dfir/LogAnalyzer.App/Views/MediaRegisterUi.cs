using System.Windows;
using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// Owner decision 2026-10-16: the parts of the media page that belong to the media register (its two tabs, the "Față de registru" columns, the "Adaugă în
    /// registru" button) are visible only while <see cref="MediaRegisterFeature.Enabled"/> is on. Used from XAML with x:Static, so a DataGrid column (which is
    /// not in the visual tree and cannot bind) follows the same switch.
    /// </summary>
    public static class MediaRegisterUi
    {
        public static Visibility Visibility => MediaRegisterFeature.Enabled ? Visibility.Visible : Visibility.Collapsed;
    }
}
