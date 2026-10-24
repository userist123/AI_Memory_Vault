using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.UI.Views
{
    /// <summary>"Ce înseamnă?" (WP18 S3): the single glossary, searchable, for people who meet a technical word anywhere in the application.</summary>
    public partial class GlossaryWindow : Window
    {
        public GlossaryWindow(string? initialSearch = null)
        {
            InitializeComponent();
            DataContext = new LogAnalyzer.UI.ViewModels.GlossarySearch(initialSearch);
            SearchBox.Focus();
        }

        /// <summary>Reopens the three-screen guide: this window closes first (it is modal), then the guide opens beside the main window.</summary>
        private void Guide_Click(object sender, RoutedEventArgs e)
        {
            Close();
            LogAnalyzer.UI.ViewModels.FirstRunGuideLauncher.Show();
        }
    }
}
