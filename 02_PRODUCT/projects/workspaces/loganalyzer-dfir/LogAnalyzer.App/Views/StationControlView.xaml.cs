using System.Windows.Controls;

namespace LogAnalyzer.UI.Views
{
    public partial class StationControlView : UserControl
    {
        public StationControlView()
        {
            InitializeComponent();
            // the missing-items line follows the edits: re-read after the cell value has been committed
            CorrectiveActionsGrid.CellEditEnding += (_, _) =>
                Dispatcher.BeginInvoke(new System.Action(() => (DataContext as ViewModels.StationControlViewModel)?.RefreshCorrectiveActionsMissing()), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
