using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.Core.Services.Details;
using Microsoft.Win32;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// Full detail of any item (event, registry artifact, timeline row, incident, finding…): every field, what it
    /// means, related items and the raw form, with copy and PDF export.
    /// </summary>
    public partial class GenericDetailWindow : Window
    {
        private readonly DetailSheet _sheet;
        private readonly Action<object>? _escalateAction;
        private readonly object _item;

        public GenericDetailWindow(object item, Action<object>? escalateAction)
        {
            InitializeComponent();
            _item = item;
            _escalateAction = escalateAction;
            _sheet = DetailSheetBuilder.Build(item);
            DataContext = _sheet;
            Title = _sheet.Title;
            FieldsGrid.ItemsSource = _sheet.Fields;
            MeaningBox.Text = _sheet.Meaning.Count > 0
                ? string.Join(Environment.NewLine + Environment.NewLine, _sheet.Meaning)
                : "Nu există o explicație predefinită pentru acest tip de element. Consultați câmpurile și forma brută.";
            RelatedGrid.ItemsSource = _sheet.Related;
            RelatedTab.Header = $"Corelate ({_sheet.Related.Count})";
            RawBox.Text = _sheet.Raw;
            EscalateButton.Visibility = escalateAction is null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Opens the detail of an item over the main window.</summary>
        public static void ShowFor(object item, Action<object>? escalate = null)
        {
            var w = new GenericDetailWindow(item, escalate) { Owner = Application.Current?.MainWindow };
            w.Show();
        }

        private void Related_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RelatedGrid.SelectedItem is RelatedItem r) ShowFor(r.Source, _escalateAction);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(_sheet.ToPlainText());
            StatusText.Text = "Copiat în clipboard.";
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var safe = new string(_sheet.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            var dlg = new SaveFileDialog { FileName = (safe.Length > 80 ? safe[..80] : safe) + ".pdf", Filter = "PDF (*.pdf)|*.pdf" };
            if (dlg.ShowDialog(this) != true) return;
            DetailSheetPdf.Write(_sheet, dlg.FileName);
            StatusText.Text = "PDF salvat: " + dlg.FileName;
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }

        private void Escalate_Click(object sender, RoutedEventArgs e)
        {
            Close();
            _escalateAction?.Invoke(_item);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
