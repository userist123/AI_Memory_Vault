using System.Windows;
using EventLogAnalyzer.App.ViewModels;
using EventLogAnalyzer.Core.KnowledgeBase;
using Microsoft.Win32;

namespace EventLogAnalyzer.App.Views;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel _dashboardViewModel;

    public MainWindow()
    {
        InitializeComponent();

        var kbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "event-knowledge-base.json");
        var knowledgeBase = EventKnowledgeBase.LoadFromFile(kbPath);

        _dashboardViewModel = new DashboardViewModel(knowledgeBase);
        DataContext = _dashboardViewModel;
    }

    private void OnSelectFolder(object sender, RoutedEventArgs e)
    {
        // WPF has no built-in folder picker pre-.NET 8's OpenFolderDialog;
        // using the modern one here (Windows 10 1809+).
        var dialog = new OpenFolderDialog
        {
            Title = "Select backup log folder (e.g. C:\\BACKUPLOGS\\2026\\08\\PC01)"
        };

        if (dialog.ShowDialog() == true && dialog.FolderName is { Length: > 0 } folder)
        {
            _dashboardViewModel.LoadFolder(folder);
        }
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        // MVP: single-frame dashboard is always visible; a full build swaps
        // the ScrollViewer's content for EventExplorerView / UserActivityView /
        // RegistryTreeView / RemediationView based on (sender as Button)?.Tag.
        // Omitted here to keep this representative sample focused - the
        // pattern is identical to DashboardViewModel's data binding above.
    }
}
