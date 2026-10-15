using System.Windows;
using LogAnalyzer.UI.ViewModels;

namespace LogAnalyzer.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            // the media page is „alive” for the whole session: first inventory and live supervision start with the main window
            // start the device inventory and live supervision a few seconds after the window is up, so they do not compete with the first frame
            Loaded += async (_, _) => { await System.Threading.Tasks.Task.Delay(3000); viewModel.MediaCenter.Activate(); };
            Closed += (_, _) => viewModel.MediaCenter.Dispose();
        }

        private void SimulateMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.DataContext = btn.DataContext;
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }
    }
}