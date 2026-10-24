using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.ViewModels;

namespace LogAnalyzer.UI.Views
{
    public partial class MediaCenterView : UserControl
    {
        public MediaCenterView() { InitializeComponent(); SimpleLevel.KeepFirstTabWhenSimple(this, MainTabs); }

        /// <summary>„Istoric”: the sealed events of the row's device in a dialog.</summary>
        private void History_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not DeviceMonitorRow row || DevicesGrid.DataContext is not DeviceMonitorViewModel vm) return;
            var (events, notice) = vm.HistoryFor(row);
            new DeviceHistoryDialog(row.Name, row.ClassText, events, notice) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        /// <summary>„Export CSV”: the list in view, in the order the operator sorted it, to a file chosen in the save dialog.</summary>
        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (DevicesGrid.DataContext is not DeviceMonitorViewModel vm) return;
            var visible = CollectionViewSource.GetDefaultView(DevicesGrid.ItemsSource).Cast<object>().OfType<DeviceMonitorRow>().ToList();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"Dispozitive_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                Filter = "CSV (*.csv)|*.csv",
                DefaultExt = ".csv",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            var path = dlg.FileName;
            var export = DeviceMonitorViewModel.PrepareCsv(visible);
            try
            {
                // audit-first like every export: the intent, then the note of the export (operator, rows, SHA-256, no path), then the file, then its outcome
                vm.ExportNote = ExportAudit.Run(ExportAudit.LiveCaseId, "CSV dispozitive", export.Rows, path, () =>
                {
                    if (vm.SealCsvExport(export) is { } refused) throw new IOException("nota exportului nu a putut fi trecută în jurnalul de audit — " + refused);
                    File.WriteAllBytes(path, export.Bytes);
                }) ?? $"Exportat: {export.Rows} rânduri. Nota exportului (operator, număr de rânduri, SHA-256) este în jurnalul sigilat.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                vm.ExportNote = "Exportul nu s-a făcut: " + ex.Message;
            }
        }
    }
}
