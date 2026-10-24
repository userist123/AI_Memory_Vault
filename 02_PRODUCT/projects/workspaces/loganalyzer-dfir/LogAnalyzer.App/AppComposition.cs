using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Infrastructure;
using LogAnalyzer.Infrastructure.Parsers;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Services;
using LogAnalyzer.UI.Views;

namespace LogAnalyzer.UI
{
    /// <summary>
    /// The application's composition (service registrations, detail-window explainers, the built service provider), kept out of <see cref="App"/>
    /// so that code and tests can reach it without touching the <see cref="App"/> type: <see cref="App"/>'s static constructor is the startup
    /// security gate (native-library manifest, install folder) and must run only in the real executable, never inside a test host, where a
    /// Release build without the embedded manifest would show a modal box and block the test run. Startup calls these exactly as before.
    /// </summary>
    public static class AppComposition
    {
        /// <summary>The service provider built at startup; null until <see cref="App"/> builds it.</summary>
        public static IServiceProvider? ServiceProvider { get; internal set; }

        /// <summary>
        /// The detail window's „Ce înseamnă” tab lives in Core, which cannot see the control-check guides (Dfir.Windows): the guide of a station,
        /// domain or mailbox check (what, why, how, result, what to do) is plugged in here (senior review E2/E3).
        /// </summary>
        public static void RegisterMeaningExplainers() =>
            LogAnalyzer.Core.Services.Details.ItemMeaning.RegisterExplainer(nameof(LogAnalyzer.Dfir.Windows.Audit.ControlCheck), item =>
                item is LogAnalyzer.Dfir.Windows.Audit.ControlCheck check
                    ? LogAnalyzer.Dfir.Windows.Audit.CheckGuides.Explain(check).ToPlainText().Split(Environment.NewLine)
                    : Array.Empty<string>());

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
    }
}
