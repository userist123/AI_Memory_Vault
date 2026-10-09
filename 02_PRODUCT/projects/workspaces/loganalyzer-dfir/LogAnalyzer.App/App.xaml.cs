using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Infrastructure;
using LogAnalyzer.Infrastructure.Parsers;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.Views;

namespace LogAnalyzer.UI
{
    public partial class App : Application
    {
        private static void OnGridRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.Handled || sender is not System.Windows.Controls.DataGridRow { Item: { } item } row) return;
            if (item == System.Windows.Data.CollectionView.NewItemPlaceholder) return;
            if (System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(row) is not System.Windows.Controls.DataGrid grid) return;
            // Grids with their own double-click command, alerts (opened on selection) and the detail window's own grids are left alone.
            if (grid.InputBindings.OfType<System.Windows.Input.MouseBinding>().Any(b => b.MouseAction == System.Windows.Input.MouseAction.LeftDoubleClick)) return;
            if (item is LogAnalyzer.Core.Models.DetectedIssue || Window.GetWindow(grid) is GenericDetailWindow) return;
            GenericDetailWindow.ShowFor(item);
            e.Handled = true;
        }

        public static IServiceProvider? ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // `--self-test`: prove the published executable runs on a bare machine, then exit without opening any window.
            if (LogAnalyzer.Dfir.Windows.Investigation.SelfTest.IsRequested(e.Args))
                Environment.Exit(LogAnalyzer.Dfir.Windows.Investigation.SelfTest.Execute(e.Args, AppContext.BaseDirectory));

            string debugLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_debug.log");
            string crashLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_crash.log");

            SplashWindow? splash = null;
            try
            {
                File.WriteAllText(debugLogPath, "OnStartup starting...\n");
                base.OnStartup(e);
                LogAnalyzer.UI.Services.LiveCase.ScopePrompt = LogAnalyzer.UI.Views.ScopeDialog.Ask;   // owner decision 28

                // Operating mode: decided by the edition. The unclassified edition takes it only from the signed policy
                // (fail closed to AirGapped); --mode= and LogAnalyzer.mode are ignored. The classified edition is always AirGapped.
                var decision = EditionComposition.DecideMode(e.Args, AppDomain.CurrentDomain.BaseDirectory);
                AppModeContext.Initialize(decision);
                File.AppendAllText(debugLogPath,
                    $"Mode: {decision.Mode} (override: {decision.IsOverride}) — {decision.Reason}\n" +
                    $"Connectivity: {decision.Snapshot.State} via {decision.Snapshot.Source}: {string.Join("; ", decision.Snapshot.Details)}\n");

                this.DispatcherUnhandledException += (sender, args) =>
                {
                    File.AppendAllText(debugLogPath, $"Dispatcher unhandled exception: {args.Exception}\n");
                    MessageBox.Show($"Eroare critică internă:\n{args.Exception.Message}", "Crash", MessageBoxButton.OK, MessageBoxImage.Error);
                    args.Handled = true;
                };

                // Double-click on a row of ANY grid opens the full detail of that item (event, artifact, incident…).
                EventManager.RegisterClassHandler(typeof(System.Windows.Controls.DataGridRow), System.Windows.Controls.Control.MouseDoubleClickEvent,
                    new System.Windows.Input.MouseButtonEventHandler(OnGridRowDoubleClick));

                var services = new ServiceCollection();
                
                // Servicii Core (Utilitare)
                services.AddSingleton<AuditLogService>();
                services.AddSingleton<PluginManagerService>();
                services.AddSingleton<KnowledgeBaseService>();
                services.AddSingleton<LogAnalyzer.Core.Services.LicenseService>();

                var applicationDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LogAnalyzer");
                Directory.CreateDirectory(applicationDataPath);
                services.AddSingleton<SecurePathService>();
                services.AddSingleton(sp => new ChainOfCustodyService(Path.Combine(applicationDataPath, "chain-of-custody.ndjson")));
                services.AddSingleton<EvidenceIntakeService>();
                // Motoarele din Infrastructure
                services.AddSingleton<IEventParser, EvtxParser>();
                services.AddSingleton<IAnalysisEngine, AnalysisEngine>();
                services.AddSingleton<IRegistryParser, OfflineRegistryParser>();
                services.AddSingleton<IDatabaseService, DatabaseService>();
                EditionComposition.Register(services);
                
                // Componentele MVVM și Ferestrele din UI
                services.AddTransient<MainViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<ActivationWindow>();

                File.AppendAllText(debugLogPath, "Building ServiceProvider...\n");
                ServiceProvider = services.BuildServiceProvider();
                File.AppendAllText(debugLogPath, "ServiceProvider built. Initializing Database...\n");

                var dbService = ServiceProvider.GetRequiredService<IDatabaseService>();
                dbService.InitializeDatabase();
                File.AppendAllText(debugLogPath, "Database initialized.\n");

                this.ShutdownMode = ShutdownMode.OnMainWindowClose;

                File.AppendAllText(debugLogPath, "Showing SplashWindow...\n");
                splash = new SplashWindow();
                splash.Show();
                File.AppendAllText(debugLogPath, "SplashWindow shown.\n");

                // 2. Verificăm licența
                var licenseService = ServiceProvider.GetRequiredService<LogAnalyzer.Core.Services.LicenseService>();
                File.AppendAllText(debugLogPath, $"Verifying license... (IsActivated: {licenseService.IsActivated()})\n");
                if (!licenseService.IsActivated())
                {
                    File.AppendAllText(debugLogPath, "License is not activated. Showing ActivationWindow...\n");
                    var activationWindow = ServiceProvider.GetRequiredService<ActivationWindow>();
                    bool? activated = activationWindow.ShowDialog();
                    File.AppendAllText(debugLogPath, $"ActivationWindow ShowDialog returned: {activated}\n");
                    if (activated != true)
                    {
                        File.AppendAllText(debugLogPath, "Shutdown called due to license failure.\n");
                        splash.Close();
                        this.Shutdown();
                        return;
                    }
                }

                // 3. Afișăm fereastra principală
                File.AppendAllText(debugLogPath, "Resolving MainWindow...\n");
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                this.MainWindow = mainWindow;
                File.AppendAllText(debugLogPath, "Showing MainWindow...\n");
                mainWindow.Show();

                // --tab=<n> opens a given tab at startup (e.g. 13 = "Izolare procese suspecte").
                var tabArg = Array.Find(e.Args, a => a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase));
                if (tabArg is not null && int.TryParse(tabArg[6..], out var tab) && mainWindow.DataContext is MainViewModel mvm)
                    mvm.SelectedTabIndex = tab;
                File.AppendAllText(debugLogPath, "MainWindow shown. Closing SplashWindow...\n");
                splash.Close();
                File.AppendAllText(debugLogPath, "Startup complete.\n");
            }
            catch (Exception ex)
            {
                File.WriteAllText(crashLogPath, $"CRASH: {ex.ToString()}");
                MessageBox.Show($"Eroare critică la pornire:\n\n{ex.Message}\n\n{ex.StackTrace}", "Eroare LogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Error);
                try { splash?.Close(); } catch {}
                this.Shutdown();
            }
        }
    }
}
