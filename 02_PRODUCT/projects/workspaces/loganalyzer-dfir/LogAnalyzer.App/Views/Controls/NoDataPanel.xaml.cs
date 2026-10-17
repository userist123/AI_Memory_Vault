using System.Windows;
using System.Windows.Controls;

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>
    /// G3: „Nu există încă date — Colectează acum”, with the single „Colectează tot” command; hidden once an analysis result exists, and on a page that
    /// loads its own data (the older pages under "Avansat") also hidden while <see cref="PageItemCount"/> is above zero.
    /// </summary>
    public partial class NoDataPanel : UserControl
    {
        public static readonly DependencyProperty PageItemCountProperty = DependencyProperty.Register(nameof(PageItemCount), typeof(int), typeof(NoDataPanel),
            new PropertyMetadata(0, (d, e) => d.SetValue(HasPageDataPropertyKey, (int)e.NewValue > 0)));

        private static readonly DependencyPropertyKey HasPageDataPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasPageData), typeof(bool), typeof(NoDataPanel), new PropertyMetadata(false));
        public static readonly DependencyProperty HasPageDataProperty = HasPageDataPropertyKey.DependencyProperty;

        /// <summary>Items the page itself holds (for example the loaded events); 0 = the page has nothing of its own.</summary>
        public int PageItemCount { get => (int)GetValue(PageItemCountProperty); set => SetValue(PageItemCountProperty, value); }

        /// <summary>True while <see cref="PageItemCount"/> is above zero.</summary>
        public bool HasPageData => (bool)GetValue(HasPageDataProperty);

        public NoDataPanel() { InitializeComponent(); }
    }
}
