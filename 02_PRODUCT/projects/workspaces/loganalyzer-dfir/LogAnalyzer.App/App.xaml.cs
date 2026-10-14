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
            LogAnalyzer.Dfir.RegexDefaults.Apply();     // security audit #11: before the first regex of the process is built
            VerifyNativeLibrariesOrExit();
        }

        /// <summary>
        /// Security audit #5 + independent review #3: the executable (elevated, highestAvailable) loads SQLCipher / WPF / QuestPDF native libraries
        /// from its own folder; they must match the SHA-256 manifest EMBEDDED in this assembly at publish (eng/NativeLibraryManifest.targets), and
        /// no other DLL may be there. The embedded manifest is checked whenever it is present, single-file or not; only a Debug build falls back to
        /// the text file next to the exe. When a manifest was checked, the install folder must also be writable only by Administrators / SYSTEM:
        /// otherwise an elevated start is refused and a standard start warns. A development build run from bin (no manifest) is not checked.
        /// </summary>
        private static void VerifyNativeLibrariesOrExit()
        {
            string? embedded = null;
            using (var s = typeof(App).Assembly.GetManifestResourceStream(LogAnalyzer.Dfir.IO.NativeLibraryManifest.ResourceName))
                if (s is not null) using (var r = new System.IO.StreamReader(s)) embedded = r.ReadToEnd();
#pragma warning disable IL3000 // Location is empty exactly when running as a single-file bundle.
            bool singleFile = string.IsNullOrEmpty(typeof(App).Assembly.Location);
#pragma warning restore IL3000
#if DEBUG
            const bool debugBuild = true;
#else
            const bool debugBuild = false;
#endif
            var dir = AppContext.BaseDirectory;
            var check = LogAnalyzer.Dfir.IO.NativeLibraryManifest.VerifyInstall(dir, embedded, debugBuild, singleFile);
            if (!check.Ok)
            {
                NativeMessageBox(IntPtr.Zero, check.Message, "LogAnalyzer – biblioteci native neverificate", 0x10 /* MB_ICONERROR */);
                Environment.Exit(3);
            }
            if (!check.Checked) return;
            bool elevated;
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                elevated = new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            var folder = LogAnalyzer.Dfir.IO.NativeLibraryManifest.DecideInstallFolder(LogAnalyzer.Dfir.Auth.AuthStoreProtection.CheckDirectory(dir), elevated);
            if (folder.Verdict == LogAnalyzer.Dfir.IO.InstallFolderVerdict.RefuseElevated)
            {
                NativeMessageBox(IntPtr.Zero, folder.Message, "LogAnalyzer – folder de instalare neprotejat", 0x10 /* MB_ICONERROR */);
                Environment.Exit(4);
            }
            if (folder.Verdict == LogAnalyzer.Dfir.IO.InstallFolderVerdict.Warn)
                NativeMessageBox(IntPtr.Zero, folder.Message, "LogAnalyzer – folder de instalare neprotejat", 0x30 /* MB_ICONWARNING */);
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
        private static bool SignInBeforeMainWindow(string? debugLogPath)
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
        // Security audit #15: the log lives in %LOCALAPPDATA%\LogAnalyzer\logs and every line is redacted (StartupLog).
        private static void Step(string? path, string message) => StartupLog.Append(path, $"{StartupClock.ElapsedMilliseconds,7} ms  {message}");

        protected override void OnStartup(StartupEventArgs e)
        {
            // `--self-test`: prove the published executable runs on a bare machine, then exit without opening any window.
            if (LogAnalyzer.Dfir.Windows.Investigation.SelfTest.IsRequested(e.Args))
                Environment.Exit(LogAnalyzer.Dfir.Windows.Investigation.SelfTest.Execute(e.Args, AppContext.BaseDirectory));

            // Security audit #15: per-user log folder, never the (shared) folder of the executable; null when it cannot be created (no log).
            var logDir = StartupLog.EnsureDirectory();
            string? debugLogPath = StartupLog.PathIn(logDir, "startup_debug.log");
            string? crashLogPath = StartupLog.PathIn(logDir, "startup_crash.log");

            SplashWindow? splash = null;
            try
            {
                StartupLog.Append(debugLogPath, "OnStartup starting...\n", reset: true);
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

                // Security audit #15: known, recoverable failures are reported and the application continues; anything else is logged and
                // the application closes (fail closed) instead of running on in an unknown state.
                this.DispatcherUnhandledException += (sender, args) =>
                {
                    var decision = StartupLog.Classify(args.Exception);
                    Step(debugLogPath, $"Dispatcher unhandled exception ({decision}): {args.Exception}\n");
                    args.Handled = true;
                    if (decision == UnhandledDecision.ContinueWithMessage)
                    {
                        MessageBox.Show($"Operația nu a putut fi finalizată:\n{StartupLog.Redact(args.Exception.Message)}\n\nAplicația continuă; repetați operația după ce cauza este înlăturată.",
                            "LogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    try { MessageBox.Show(StartupLog.CloseMessage, "LogAnalyzer – eroare internă", MessageBoxButton.OK, MessageBoxImage.Error); }
                    finally { Shutdown(2); }
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
                // Independent review #5: the audit head was sealed but the record of it could not be appended (shown again at sign-in).
                if (AuthApp.Service.SealNotRecorded is { } sealIssue) Step(debugLogPath, $"Auth audit seal not recorded: {sealIssue}\n");
                // Security audit #18: the accepted signed policy's version goes into the hash-chained auth audit (anti-rollback mark).
                if (startup.PolicyVersion > 0)
                {
                    try { if (AuthApp.Service.RecordEditionPolicyVersion(startup.PolicyVersion)) Step(debugLogPath, $"Edition policy version {startup.PolicyVersion} recorded in the auth audit.\n"); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Step(debugLogPath, $"Edition policy version not recorded in the auth audit: {ex.GetType().Name}\n"); }
                }
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
                StartupLog.Append(crashLogPath, $"CRASH: {ex}", reset: true);
                MessageBox.Show($"Eroare critică la pornire:\n\n{StartupLog.Redact(ex.Message)}\n\nDetaliile tehnice sunt în %LOCALAPPDATA%\\LogAnalyzer\\logs\\startup_crash.log.",
                    "Eroare LogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Error);
                try { splash?.Close(); } catch {}
                this.Shutdown();
            }
        }
    }
}
