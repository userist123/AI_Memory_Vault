using System.Windows.Controls;

namespace LogAnalyzer.UI.Views
{
    public partial class ProcedureProfileView : UserControl
    {
        public ProcedureProfileView() => InitializeComponent();
            private void Grid_AutoGeneratingColumn(object sender, System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e) => ColumnLabels.OnAutoGeneratingColumn(sender, e);
    }
}
