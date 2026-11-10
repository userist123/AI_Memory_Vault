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
            // plan 2026-11-04: product, module and descriptor ("LogAnalyzer DFIR · Insula — Stație izolată")
            Title = LogAnalyzer.Core.Services.Edition.ModuleCatalog.ProductName + " · " + LogAnalyzer.Core.Services.Edition.ModuleCatalog.DisplayName(LogAnalyzer.Core.Services.Edition.StationPostureContext.Current);
            // the media page is „alive” for the whole session: first inventory and live supervision start with the main window
            // start the device inventory and live supervision a few seconds after the window is up, so they do not compete with the first frame
            Loaded += async (_, _) => { await System.Threading.Tasks.Task.Delay(3000); viewModel.MediaCenter.Activate(); };
            // owner decision 2026-10-22: the three-screen guide opens once after the first sign-in (this window exists only after it); modeless, so nothing waits for it
            Loaded += (_, _) => Dispatcher.BeginInvoke(new System.Action(() => FirstRunGuideLauncher.ShowIfFirstTime()), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Closed += (_, _) => viewModel.MediaCenter.Dispose();

            // Lab 2026-11-10 (Insula on a VM): a sidebar item only navigated on a click. Checked from the keyboard (arrow keys inside the radio group)
            // or by a screen reader (UI Automation Select), the item showed as selected while the page stayed the old one. A checked item now opens its
            // page, and a page opened from elsewhere (a Home card, a search result) checks its own item, so the menu and the content never disagree.
            AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler((_, e) =>
            {
                if (_syncingSidebar || e.OriginalSource is not System.Windows.Controls.RadioButton { GroupName: SidebarGroup } rb) return;
                if (SidebarTab(rb) is not { } tab || tab == viewModel.SelectedTabIndex) return;
                if (!viewModel.OpenTab(tab)) SyncSidebar(viewModel.SelectedTabIndex);   // refused (licence / module): the menu goes back to the open page
            }));
            viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex)) SyncSidebar(viewModel.SelectedTabIndex); };
        }

        private const string SidebarGroup = "Sidebar";
        private bool _syncingSidebar;

        /// <summary>The tab a sidebar item opens (its CommandParameter), or null.</summary>
        internal static int? SidebarTab(System.Windows.Controls.RadioButton rb) =>
            int.TryParse(rb.CommandParameter?.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;

        /// <summary>Checks the sidebar item of the open page; when the page has no visible item (it lives in the collapsed "Avansat" list), no item stays checked.</summary>
        private void SyncSidebar(int tab)
        {
            if (_syncingSidebar) return;
            _syncingSidebar = true;
            try
            {
                var items = new System.Collections.Generic.List<System.Windows.Controls.RadioButton>();
                Collect(this, items);
                var match = items.Find(r => SidebarTab(r) == tab);
                if (match is not null) { if (match.IsChecked != true) match.IsChecked = true; }
                else foreach (var r in items) if (r.IsChecked == true) r.IsChecked = false;
            }
            finally { _syncingSidebar = false; }
        }

        private static void Collect(DependencyObject root, System.Collections.Generic.List<System.Windows.Controls.RadioButton> into)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is System.Windows.Controls.RadioButton { GroupName: SidebarGroup } rb) into.Add(rb);
                else Collect(c, into);
            }
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