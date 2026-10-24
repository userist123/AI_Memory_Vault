using System.Windows;
using LogAnalyzer.UI.ViewModels;

namespace LogAnalyzer.UI.Views
{
    /// <summary>The three-screen first-run guide. Modeless: it is shown with <c>Show()</c>, so the main window stays usable (and automatable) behind it.</summary>
    public partial class FirstRunGuideWindow : Window
    {
        private static FirstRunGuideWindow? _open;

        public FirstRunGuideWindow()
        {
            InitializeComponent();
            var vm = new FirstRunGuideViewModel();
            DataContext = vm;
            vm.CloseRequested += Close;
            Closing += (_, _) => { vm.CloseRequested -= Close; vm.Close(); };   // the X remembers it as seen, like Esc (no second Close while closing)
            Closed += (_, _) => { if (ReferenceEquals(_open, this)) _open = null; };
        }

        /// <summary>Opens the guide, or brings the open one to the front; owned by the main window when that is visible.</summary>
        public static void ShowModeless()
        {
            if (_open is { } existing) { existing.Activate(); return; }
            var window = new FirstRunGuideWindow();
            if (Application.Current?.MainWindow is { IsVisible: true } owner) window.Owner = owner;
            _open = window;
            window.Show();
        }
    }
}
