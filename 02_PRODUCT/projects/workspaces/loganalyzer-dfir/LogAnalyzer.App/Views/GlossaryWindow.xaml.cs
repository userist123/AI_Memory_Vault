using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.Views
{
    /// <summary>"Ce înseamnă?" (WP18 S3): the single glossary, searchable, for people who meet a technical word anywhere in the application.</summary>
    public partial class GlossaryWindow : Window
    {
        public GlossaryWindow(string? initialSearch = null)
        {
            InitializeComponent();
            SearchBox.Text = initialSearch ?? "";
            Refresh();
            SearchBox.Focus();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void Refresh()
        {
            var q = SearchBox.Text.Trim();
            Grid.ItemsSource = string.IsNullOrEmpty(q)
                ? Glossary.Terms.OrderBy(t => t.Romanian).ToList()
                : Glossary.Terms.Where(t => t.Romanian.Contains(q, System.StringComparison.OrdinalIgnoreCase) || t.Technical.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                                             || t.Explanation.Contains(q, System.StringComparison.OrdinalIgnoreCase) || t.English.Contains(q, System.StringComparison.OrdinalIgnoreCase))
                                .OrderBy(t => t.Romanian).ToList();
        }
    }
}
