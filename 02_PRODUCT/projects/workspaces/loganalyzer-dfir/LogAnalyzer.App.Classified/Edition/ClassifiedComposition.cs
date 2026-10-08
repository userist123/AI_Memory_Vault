using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Composition of the classified edition (P1). It registers only "not available" stand-ins for the capabilities that are
    /// not compiled into this executable: this project references neither LogAnalyzer.Connectors, LogAnalyzer.Response nor
    /// LogAnalyzer.Ai. A test inspects the build output to keep it that way.
    /// </summary>
    public static class EditionComposition
    {
        /// <summary>The classified edition has no mode to choose: no network code is compiled in, so the mode is AirGapped.</summary>
        public static ModeDecision DecideMode(string[] args, string baseDirectory) =>
            new(AppMode.AirGapped, false, "Ediția clasificată (P1): codul de rețea nu este inclus în aplicație; modul nu poate fi schimbat.",
                ConnectivitySnapshot.Unknown("edition without network code"));

        public static void Register(IServiceCollection services)
        {
#if CLASSIFIED_LOCAL_AI
            // Explicit compile-time option (-p:ClassifiedIncludeLocalAi=true), OFF by default; requires owner approval.
            services.AddSingleton<IEditionProfile>(new ClassifiedEditionProfile(new[] { EditionFeature.AiAnalysis }));
            services.AddSingleton<IFeatureViewFactory, ClassifiedLocalAiViews>();
#else
            services.AddSingleton<IEditionProfile>(new ClassifiedEditionProfile());
            services.AddSingleton<IFeatureViewFactory, NoFeatureViews>();
#endif
            services.AddSingleton<IHostDefense, UnavailableHostDefense>();
            services.AddSingleton<ILiveEventSourceFactory, NoLiveEventSourceFactory>();
            services.AddSingleton<IConnectivityWatcher, NoConnectivityWatcher>();
            services.AddSingleton<IAuditCollector, UnavailableAuditCollector>();
            services.AddSingleton<ISyslogReceiver, UnavailableSyslogReceiver>();
            services.AddSingleton<IAuditCollectionService, AuditCollectionService>();
        }
    }
}
