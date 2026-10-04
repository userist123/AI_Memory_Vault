using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.UI.Views
{
    public partial class AlertDetailWindow : Window
    {
        private readonly DetectedIssue _issue;

        public AlertDetailWindow(DetectedIssue issue)
        {
            InitializeComponent();
            _issue = issue;

            TxtTitle.Text = issue.Title;
            TxtExplanation.Text = issue.Explanation;
            TxtSeverity.Text = issue.Severity.ToUpper();

            TxtMitre.Text = issue.HasMitreMapping ? $"MITRE: {issue.MitreTechniqueId}" : "MITRE: N/A";
            TxtMitreTactica.Text = $"Tactica: {(string.IsNullOrEmpty(issue.MitreTacticName) ? "-" : $"{issue.MitreTacticId} - {issue.MitreTacticName}")}";
            TxtMitreTehnica.Text = $"Tehnica: {(string.IsNullOrEmpty(issue.MitreTechniqueName) ? "-" : $"{issue.MitreTechniqueId} - {issue.MitreTechniqueName}")}";

            ChkVerified.IsChecked = issue.IsVerified;
            TxtNotes.Text = issue.AnalystNotes;

            CmbStatus.SelectedIndex = (int)issue.Status;
            CmbResolution.SelectedIndex = (int)issue.Resolution;

            if (issue.Severity.Equals("Critic", StringComparison.OrdinalIgnoreCase) || issue.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            {
                SeverityBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#450A0A"));
                TxtSeverity.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
            }
            else if (issue.Severity.Equals("Avertisment", StringComparison.OrdinalIgnoreCase) || issue.Severity.Equals("High", StringComparison.OrdinalIgnoreCase))
            {
                SeverityBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#422006"));
                TxtSeverity.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
            }
            else
            {
                SeverityBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                TxtSeverity.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF"));
            }

            var triggerEvent = issue.RelatedEvents?.LastOrDefault();
            TxtTriggerInfo.Text = triggerEvent != null ? triggerEvent.Message : "Nu exista date suplimentare / payload brut atasat.";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _issue.IsVerified = ChkVerified.IsChecked ?? false;
                _issue.AnalystNotes = TxtNotes.Text;

                if (CmbStatus.SelectedIndex >= 0)
                    _issue.Status = (AlertStatus)CmbStatus.SelectedIndex;

                if (CmbResolution.SelectedIndex >= 0)
                    _issue.Resolution = (AlertResolution)CmbResolution.SelectedIndex;

                DialogResult = true;
            }
            catch
            {
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try { DialogResult = false; }
            catch { Close(); }
        }
    }
}
