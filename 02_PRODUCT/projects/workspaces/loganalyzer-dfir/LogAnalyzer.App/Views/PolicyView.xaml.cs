using System.Windows.Controls;

namespace LogAnalyzer.UI.Views
{
    public partial class PolicyView : UserControl
    {
        public PolicyView() { InitializeComponent(); SimpleLevel.KeepFirstTabWhenSimple(this, PolicyTabs); }

        /// <summary>Master plan 6.12: the package store is read when the section is opened, not at start-up (it re-verifies the active package).</summary>
        private void ThreatIntelExpanded(object sender, System.Windows.RoutedEventArgs e) => (DataContext as ViewModels.PolicyViewModel)?.ThreatIntel.Refresh();
    }
}
