using System;
using System.Windows;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Connectors.Collection;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Policy;
using LogAnalyzer.Infrastructure.Watchers;
using LogAnalyzer.Response.Collection;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.Response.Policy;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Composition of the unclassified edition (P2/P3): every capability is registered. Whether the networked ones may run is
    /// decided by NetworkPolicy, which follows the signed edition policy (see EditionPolicyVerifier). This file exists only in
    /// the LogAnalyzer.App project; the classified executable has its own (ClassifiedComposition) and does not reference the
    /// assemblies used here.
    /// </summary>
    public static class EditionComposition
    {
        public const string PublicKeyResource = SignedPolicyStartup.PublicKeyResource;

        /// <summary>The posture of this executable, fixed at compile time. A signed policy only chooses the mode (StationPostures.ClampMode), never the posture.</summary>
        public const StationPosture Posture = StationPosture.Internet;

        /// <summary>
        /// Signed policy → mode. Missing / invalid / expired / foreign / rolled-back policy, or no embedded verification key,
        /// means AirGapped. The command line and the LogAnalyzer.mode file can no longer select Network.
        /// </summary>
        public static ModeDecision DecideMode(string[] args, string baseDirectory) => DecideStartup(args, baseDirectory).Mode;

        /// <summary>Mode and station role (WP18) from the same signed policy. The role never comes from anywhere else.</summary>
        public static StartupDecision DecideStartup(string[] args, string baseDirectory)
        {
            var modeFile = System.IO.Path.Combine(baseDirectory, "LogAnalyzer.mode");
            string? stationMode = null;
            try { stationMode = System.IO.File.Exists(modeFile) ? System.IO.File.ReadAllText(modeFile) : null; } catch (System.IO.IOException) { }
            var requested = EditionPolicyVerifier.RequestedOverride(args, stationMode);
            var snapshot = new WindowsConnectivityProbe().Probe();

            var result = SignedPolicyStartup.Load(typeof(EditionComposition).Assembly);
            var mode = EditionPolicyVerifier.Decide(result, snapshot, requested);
            var role = StationRoleResolver.Decide(EditionKind.Unclassified, result, mode, DateTimeOffset.UtcNow);
            return new StartupDecision(mode, role, result.Valid ? result.Policy!.Version : 0);
        }

        /// <summary>Anti-rollback mark (security audit #18); the logic is shared with the Network edition in SignedPolicyStartup.</summary>
        internal static long HighWater(string highWaterPath, string authAuditPath) => SignedPolicyStartup.HighWater(highWaterPath, authAuditPath);

        public static void Register(IServiceCollection services)
        {
            services.AddSingleton<IEditionProfile, UnclassifiedEditionProfile>();
            services.AddSingleton<IFeatureViewFactory, UnclassifiedFeatureViews>();
            services.AddSingleton<IHostDefense, HostDefense>();
            services.AddSingleton<ILiveEventSourceFactory, LiveEventSourceFactory>();
            services.AddSingleton<IConnectivityWatcher, NetworkChangeWatcher>();
            services.AddSingleton<IRegistryValueWriter, RegistryValueWriter>();
            services.AddSingleton<IAuditCollector, PowerShellAuditCollector>();
            // this edition ships the firewall runner and the collection script: their programs join the inventory A17 recognises (only from the sealed audit)
            LogAnalyzer.Response.ResponseProgramInventory.Register();
            services.AddSingleton<ISyslogReceiver, UdpSyslogReceiver>();
            services.AddSingleton<IAuditCollectionService, AuditCollectionService>();
        }
    }

    internal sealed class UnclassifiedFeatureViews : IFeatureViewFactory
    {
        public object? CreateViewModel(string featureKey, object? context = null) => featureKey switch
        {
            FeatureKeys.Containment => new ContainmentViewModel(),
            FeatureKeys.DomainInvestigation => new DomainInvestigationViewModel(),
            FeatureKeys.AiAnalysis when context is InvestigationViewModel inv => new AiAnalysisViewModel(inv),
            _ => null,
        };

        public FrameworkElement? CreateView(string featureKey, object viewModel) => featureKey switch
        {
            FeatureKeys.Containment => new ProcessContainmentView { DataContext = viewModel },
            FeatureKeys.DomainInvestigation => new DomainInvestigationView { DataContext = viewModel },
            FeatureKeys.AiAnalysis => new AiAnalysisView { DataContext = viewModel },
            _ => null,
        };
    }
}
