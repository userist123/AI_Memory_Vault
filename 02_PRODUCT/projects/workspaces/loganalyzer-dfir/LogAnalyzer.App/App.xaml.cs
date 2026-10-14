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
        /// <summary>Runs before WPF creates the application object, i.e. before any of the application's own native libraries is loaded.</summary>
        static App()
        {
            VerifyNativeLibrariesOrExit();
        }

        /// <summary>
        /// Security audit #5: the published single-file executable (elevated, highestAvailable) loads SQLCipher / WPF / QuestPDF native libraries from
        /// its own folder; they must match the SHA-256 manifest written at publish, and no other DLL may be there. Otherwise the app does not start.
        /// Development and test builds (assemblies on disk, no manifest) are not checked.
        /// </summary>
        private static void VerifyNativeLibrariesOrExit()
        {
#pragma warning disable IL3000 // Location is empty exactly when running as a single-file bundle: that is what is tested here.
            if (!string.IsNullOrEmpty(typeof(App).Assembly.Location)) return;
#pragma warning restore IL3000
            var check = LogAnalyzer.Dfir.IO.NativeLibraryManifest.Verify(AppContext.BaseDirectory);
            if (check.Ok) return;
            NativeMessageBox(IntPtr.Zero, check.Message, "LogAnalyzer – biblioteci native neverificate", 0x10 /* MB_ICONERROR */);
            Environment.Exit(3);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int NativeMessageBox(IntPtr owner, string text, string caption, uint type);

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

        /// <summary>
        /// The detail window's „Ce înseamnă” tab lives in Core, which cannot see the control-check guides (Dfir.Windows): the guide of a station,
        /// domain or mailbox check (what, why, how, result, what to do) is plugged in here (senior review E2/E3).
        /// </summary>
        public static void RegisterMeaningExplainers() =>
            LogAnalyzer.Core.Services.Details.ItemMeaning.RegisterExplainer(nameof(LogAnalyzer.Dfir.Windows.Audit.ControlCheck), item =>
                item is LogAnalyzer.Dfir.Windows.Audit.ControlCheck check
                    ? LogAnalyzer.Dfir.Windows.Audit.CheckGuides.Explain(check).ToPlainText().Split(Environment.NewLine)
                    : Array.Empty<string>());

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
            Step(debugLogPath, $"Auth setup state: {AuthApp.Service.SetupState}\n");
            var window = new SignInWindow(mode);
            if (window.ShowDialog() != true || window.Session is null) return false;
            AuthApp.SetSession(window.Session);
            return true;
        }

        /// <summary>The application's services, exactly as startup registers them (also used by the startup timing test).</summary>
        public static ServiceCollection BuildServices() => BuildServices(null);

        /// <summary>
        /// Test seam (internal, visible only to LogAnalyzer.App.Tests): <paramref name="applicationDataRoot"/> replaces %LOCALAPPDATA%\LogAnalyzer so the
        /// startup test does not write the machine's database or chain of custody. Null (the only value production passes) keeps the fixed folder.
        /// </summary>
        internal static ServiceCollection BuildServices(string? applicationDataRoot)
        {
            var services = new ServiceCollection();
            
            // Servicii Core (Utilitare)
            services.AddSingleton<AuditLogService>();
            services.AddSingleton<PluginManagerService>();
            services.AddSingleton<KnowledgeBaseService>();
            services.AddSingleton<LogAnalyzer.Core.Services.LicenseService>();

            var applicationDataPath = applicationDataRoot ?? Path.Combine(
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
            if (applicationDataRoot is null) services.AddSingleton<IDatabaseService, DatabaseService>();
            else services.AddSingleton<IDatabaseService>(_ => new DatabaseService(applicationDataRoot));
            EditionComposition.Register(services);
            
            // Componentele MVVM și Ferestrele din UI
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();
            services.AddTransient<ActivationWindow>();
            return services;
        }

        // Startup timing (owner 2026-10-11: "the other app feels faster"): every step of startup_debug.log carries the milliseconds since start,
        // so slow steps are measured, not guessed. The sign-in and licence dialogs show as gaps that are the user's time, not the application's.
        private static readonly System.Diagnostics.Stopwatch StartupClock = System.Diagnostics.Stopwatch.StartNew();
        private static void Step(string path, string message)
        {
            try { File.AppendAllText(path, $"{StartupClock.ElapsedMilliseconds,7} ms  {message}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
                LogAnalyzer.UI.Services.DataPackTrust.Apply();                                         // keys for offline detection data packs

                // Operating mode: decided by the edition. The unclassified edition takes it only from the signed policy
                // (fail closed to AirGapped); --mode= and LogAnalyzer.mode are ignored. The classified edition is always AirGapped.
                var startup = EditionComposition.DecideStartup(e.Args, AppDomain.CurrentDomain.BaseDirectory);
                var decision = startup.Mode;
                AppModeContext.Initialize(decision);
                // Station role (WP18): from the same signed policy; CONTROL unless the policy says CSIRT. Never from the operator.
                LogAnalyzer.Core.Services.Edition.StationRoleContext.Initialize(startup.Role);
                Step(debugLogPath,
                    $"Mode: {decision.Mode} (override: {decision.IsOverride}) — {decision.Reason}\n" +
                    $"Connectivity: {decision.Snapshot.State} via {decision.Snapshot.Source}: {string.Join("; ", decision.Snapshot.Details)}\n" +
                    $"Station role: {startup.Role.EffectiveRole} — {startup.Role.Summary}\n");

                this.DispatcherUnhandledException += (sender, args) =>
                {
                    Step(debugLogPath, $"Dispatcher unhandled exception: {args.Exception}\n");
                    MessageBox.Show($"Eroare critică internă:\n{args.Exception.Message}", "Crash", MessageBoxButton.OK, MessageBoxImage.Error);
                    args.Handled = true;
                };

                // Double-click on a row of ANY grid opens the full detail of that item (event, artifact, incident…).
                EventManager.RegisterClassHandler(typeof(System.Windows.Controls.DataGridRow), System.Windows.Controls.Control.MouseDoubleClickEvent,
                    new System.Windows.Input.MouseButtonEventHandler(OnGridRowDoubleClick));
                RegisterMeaningExplainers();

                var services = BuildServices();

                Step(debugLogPath, "Building ServiceProvider...\n");
                ServiceProvider = services.BuildServiceProvider();
                Step(debugLogPath, "ServiceProvider built. Initializing Database...\n");

                ServiceProvider.GetRequiredService<AuditLogService>().LogAction("station.role", LogAnalyzer.Core.Services.Edition.StationRoleContext.Current.Summary);
                var dbService = ServiceProvider.GetRequiredService<IDatabaseService>();
                dbService.InitializeDatabase();
                Step(debugLogPath, "Database initialized.\n");

                this.ShutdownMode = ShutdownMode.OnMainWindowClose;

                Step(debugLogPath, "Showing SplashWindow...\n");
                splash = new SplashWindow();
                splash.Show();
                Step(debugLogPath, "SplashWindow shown.\n");

                // 2. Verificăm licența
                var licenseService = ServiceProvider.GetRequiredService<LogAnalyzer.Core.Services.LicenseService>();
                Step(debugLogPath, $"Verifying license... (IsActivated: {licenseService.IsActivated()})\n");
                if (!licenseService.IsActivated())
                {
                    Step(debugLogPath, "License is not activated. Showing ActivationWindow...\n");
                    var activationWindow = ServiceProvider.GetRequiredService<ActivationWindow>();
                    bool? activated = activationWindow.ShowDialog();
                    Step(debugLogPath, $"ActivationWindow ShowDialog returned: {activated}\n");
                    if (activated != true)
                    {
                        Step(debugLogPath, "Shutdown called due to license failure.\n");
                        splash.Close();
                        this.Shutdown();
                        return;
                    }
                }

                // 2b. Autentificare (decizia 33): card + PIN pentru utilizatori; cont + parolă numai pentru administratorul principal.
                splash.Hide();
                AuthApp.Initialize();
                // The application's own time memory: compare the clock with the last recorded time BEFORE sign-in writes its own entries,
                // then record "clock.seen" in the chained auth audit.
                try
                {
                    var clock = LogAnalyzer.Dfir.Case.ClockMemory.Startup(AuthApp.Service.AuditPath, DateTimeOffset.UtcNow, LogAnalyzer.Dfir.Windows.Audit.StationFactCollector.ReadInstallDate());
                    Step(debugLogPath, $"Clock memory: {clock.State} — {clock.Message}\n");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Step(debugLogPath, $"Clock memory unavailable: {ex.Message}\n"); }
                var signedIn = SignInBeforeMainWindow(debugLogPath);
                if (!signedIn)
                {
                    Step(debugLogPath, "Sign-in cancelled; shutting down.\n");
                    splash.Close();
                    this.Shutdown();
                    return;
                }
                splash.Show();
                // The auth audit records on which station role this session was opened (WP18).
                if (AuthApp.Session is { } openedSession)
                    AuthApp.Service.AuditSessionContext(openedSession, LogAnalyzer.Core.Services.Edition.StationRoleContext.Current.Summary);

                // 3. Afișăm fereastra principală
                Step(debugLogPath, "Resolving MainWindow...\n");
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                this.MainWindow = mainWindow;
                Step(debugLogPath, "Showing MainWindow...\n");
                mainWindow.ContentRendered += (_, _) => Step(debugLogPath, "MainWindow first frame rendered.\n");
                mainWindow.Show();
                new SessionGuard(mainWindow).Start();      // lock on inactivity / card removal (card sessions only)

                // --tab=<n> opens a given tab at startup (e.g. 13 = "Izolare procese suspecte").
                var tabArg = Array.Find(e.Args, a => a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase));
                if (tabArg is not null && int.TryParse(tabArg[6..], out var tab) && mainWindow.DataContext is MainViewModel mvm)
                    mvm.SelectedTabIndex = tab;
                Step(debugLogPath, "MainWindow shown. Closing SplashWindow...\n");
                splash.Close();
                Step(debugLogPath, "Startup complete.\n");
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
