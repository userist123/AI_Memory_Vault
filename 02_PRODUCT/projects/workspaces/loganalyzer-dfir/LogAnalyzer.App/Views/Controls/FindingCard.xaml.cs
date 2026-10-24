using System.Windows.Controls;

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>The finding card (WP6a, U4): title, human summary, severity, state, verification and the four actions. DataContext is a <c>FindingCardViewModel</c>.</summary>
    public partial class FindingCard : UserControl
    {
        public FindingCard() { InitializeComponent(); }
    }
}
