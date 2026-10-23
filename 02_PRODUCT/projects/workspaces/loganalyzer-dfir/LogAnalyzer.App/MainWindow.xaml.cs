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
            Title += " — Ediția: " + LogAnalyzer.Core.Services.Edition.StationPostures.EditionLabel(LogAnalyzer.Core.Services.Edition.StationPostureContext.Current);
            // the media page is „alive” for the whole session: first inventory and live supervision start with the main window
            // start the device inventory and live supervision a few seconds after the window is up, so they do not compete with the first frame
            Loaded += async (_, _) => { await System.Threading.Tasks.Task.Delay(3000); viewModel.MediaCenter.Activate(); };
            // owner decision 2026-10-22: the three-screen guide opens once after the first sign-in (this window exists only after it); modeless, so nothing waits for it
            Loaded += (_, _) => Dispatcher.BeginInvoke(new System.Action(() => FirstRunGuideLauncher.ShowIfFirstTime()), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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