using System.Windows.Controls;

namespace LogAnalyzer.UI.Views
{
    public partial class PolicyView : UserControl
    {
        public PolicyView() { InitializeComponent(); SimpleLevel.KeepFirstTabWhenSimple(this, PolicyTabs); }
    }
}
