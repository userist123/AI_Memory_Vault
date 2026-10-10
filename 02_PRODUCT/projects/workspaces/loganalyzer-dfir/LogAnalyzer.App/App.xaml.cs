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

        /// <summary>First run creates the primary administrator; after an offline password recovery a new password is set; otherwise the normal sign-in.</summary>
        private static bool SignInBeforeMainWindow(string debugLogPath)
        {
            var mode = AuthApp.Service.SetupState switch
            {
                LogAnalyzer.Dfir.Auth.AuthSetupState.FirstRun => SignInWindowMode.FirstRun,
                LogAnalyzer.Dfir.Auth.AuthSetupState.PasswordRecovery => SignInWindowMode.Recovery,
                _ => SignInWindowMode.SignIn,
            };
            File.AppendAllText(debugLogPath, $"Auth setup state: {AuthApp.Service.SetupState}\n");
            var window = new SignInWindow(mode);
            if (window.ShowDialog() != true || window.Session is null) return false;
            AuthApp.SetSession(window.Session);
            return true;
        }

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
                var startup = EditionComposition.DecideStartup(e.Args, AppDomain.CurrentDomain.BaseDirectory);
                var decision = startup.Mode;
                AppModeContext.Initialize(decision);
                // Station role (WP18): from the same signed policy; CONTROL unless the policy says CSIRT. Never from the operator.
                LogAnalyzer.Core.Services.Edition.StationRoleContext.Initialize(startup.Role);
                File.AppendAllText(debugLogPath,
                    $"Mode: {decision.Mode} (override: {decision.IsOverride}) — {decision.Reason}\n" +
                    $"Connectivity: {decision.Snapshot.State} via {decision.Snapshot.Source}: {string.Join("; ", decision.Snapshot.Details)}\n" +
                    $"Station role: {startup.Role.EffectiveRole} — {startup.Role.Summary}\n");

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

                ServiceProvider.GetRequiredService<AuditLogService>().LogAction("station.role", LogAnalyzer.Core.Services.Edition.StationRoleContext.Current.Summary);
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

                // 2b. Autentificare (decizia 33): card + PIN pentru utilizatori; cont + parolă numai pentru administratorul principal.
                splash.Hide();
                AuthApp.Initialize();
                var signedIn = SignInBeforeMainWindow(debugLogPath);
                if (!signedIn)
                {
                    File.AppendAllText(debugLogPath, "Sign-in cancelled; shutting down.\n");
                    splash.Close();
                    this.Shutdown();
                    return;
                }
                splash.Show();
                // The auth audit records on which station role this session was opened (WP18).
                if (AuthApp.Session is { } openedSession)
                    AuthApp.Service.AuditSessionContext(openedSession, LogAnalyzer.Core.Services.Edition.StationRoleContext.Current.Summary);

                // 3. Afișăm fereastra principală
                File.AppendAllText(debugLogPath, "Resolving MainWindow...\n");
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                this.MainWindow = mainWindow;
                File.AppendAllText(debugLogPath, "Showing MainWindow...\n");
                mainWindow.Show();
                new SessionGuard(mainWindow).Start();      // lock on inactivity / card removal (card sessions only)

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
