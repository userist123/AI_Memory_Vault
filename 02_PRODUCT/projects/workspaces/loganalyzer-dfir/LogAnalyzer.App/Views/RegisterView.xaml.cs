using System.Windows.Controls;

namespace LogAnalyzer.UI.Views
{
    public partial class RegisterView : UserControl
    {
        public RegisterView() => InitializeComponent();
            private void Grid_AutoGeneratingColumn(object sender, System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e) => ColumnLabels.OnAutoGeneratingColumn(sender, e);
    }
}
