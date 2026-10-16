using System;
using System.Windows;
using LogAnalyzer.Connectors.Collection;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Policy;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.Infrastructure.Watchers;
using LogAnalyzer.Response.Collection;
using LogAnalyzer.Response.Policy;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Composition of the Network edition: an air-gapped LAN with domain controllers and servers, and no internet. It registers the internal-network
    /// capabilities (live event watchers, syslog, domain collection, response actions) and none of the AI layer. This executable contains no cloud
    /// client and no online downloader; the edition test keeps HTTP/web client code out of everything except LogAnalyzer.Connectors.
    /// </summary>
    public static class EditionComposition
    {
        public const string PublicKeyResource = SignedPolicyStartup.PublicKeyResource;

        /// <summary>The posture of this executable, fixed at compile time. A signed policy can only narrow the mode, never widen the posture.</summary>
        public const StationPosture Posture = StationPosture.IsolatedNetwork;

        public static ModeDecision DecideMode(string[] args, string baseDirectory) => DecideStartup(args, baseDirectory).Mode;

        /// <summary>
        /// Mode and station role. The Network edition runs in Network mode (LAN features on) by default, because it contains no internet code; a valid signed
        /// policy may narrow it to AirGapped (then NetworkPolicy blocks the LAN features too) and may set the station role. The command line, the
        /// LogAnalyzer.mode file and connectivity detection never choose anything.
        /// </summary>
        public static StartupDecision DecideStartup(string[] args, string baseDirectory)
        {
            var snapshot = new WindowsConnectivityProbe().Probe();
            var requested = EditionPolicyVerifier.RequestedOverride(args, null);
            var policy = SignedPolicyStartup.Load(typeof(EditionComposition).Assembly);
            var wanted = policy.Valid ? policy.Policy!.Mode : AppMode.Network;
            var effective = StationPostures.ClampMode(Posture, wanted);
            var note = string.IsNullOrWhiteSpace(requested) ? "" : $" Cererea operatorului „{requested}” a fost ignorată: modul vine doar din ediție și din politica semnată.";
            var reason = policy.Valid
                ? $"Ediția Rețea air-gap; politica semnată v{policy.Policy!.Version} (semnatar {policy.Policy.Signer}) stabilește modul {effective}.{note}"
                : $"Ediția Rețea air-gap: modul implicit este Network (rețea internă, fără internet; nu există cod de internet în aplicație). Politică semnată: {policy.Reason}{note}";
            var mode = new ModeDecision(effective, false, reason, snapshot);
            // The role decision reports the mode that is really in force, not the "no policy" fallback of the generic resolver.
            var role = StationRoleResolver.Decide(EditionKind.Unclassified, policy, mode, DateTimeOffset.UtcNow) with { Mode = effective };
            return new StartupDecision(mode, role, policy.Valid ? policy.Policy!.Version : 0);
        }

        public static void Register(IServiceCollection services)
        {
#if NETWORK_LOCAL_AI
            services.AddSingleton<IEditionProfile>(new NetworkEditionProfile(includesLocalAi: true));
#else
            services.AddSingleton<IEditionProfile>(new NetworkEditionProfile());
#endif
            services.AddSingleton<IFeatureViewFactory, NetworkFeatureViews>();
            services.AddSingleton<IHostDefense, HostDefense>();
            services.AddSingleton<ILiveEventSourceFactory, LiveEventSourceFactory>();
            services.AddSingleton<IConnectivityWatcher, NetworkChangeWatcher>();
            services.AddSingleton<IRegistryValueWriter, RegistryValueWriter>();
            services.AddSingleton<IAuditCollector, PowerShellAuditCollector>();
            // this edition ships the firewall runner and the collection script: their programs join the inventory A17 recognises (only from the sealed audit)
            LogAnalyzer.Response.ResponseProgramInventory.Register();
            services.AddSingleton<ISyslogReceiver, UdpSyslogReceiver>();
            services.AddSingleton<IAuditCollectionService, AuditCollectionService>();
            // N13: active, read-only domain-controller reachability (private addresses only; behind NetworkPolicy). Network edition only.
            services.AddSingleton<LogAnalyzer.Dfir.Windows.Audit.IDomainControllerProbe, LogAnalyzer.Connectors.Domain.DomainControllerProbe>();
        }
    }

    internal sealed class NetworkFeatureViews : IFeatureViewFactory
    {
        public object? CreateViewModel(string featureKey, object? context = null) => featureKey switch
        {
            FeatureKeys.Containment => new ContainmentViewModel(),
            FeatureKeys.DomainInvestigation => new DomainInvestigationViewModel(),
#if NETWORK_LOCAL_AI
            FeatureKeys.AiAnalysis when context is InvestigationViewModel inv => new AiAnalysisViewModel(inv),
#endif
            _ => null,
        };

        public FrameworkElement? CreateView(string featureKey, object viewModel) => featureKey switch
        {
            FeatureKeys.Containment => new ProcessContainmentView { DataContext = viewModel },
            FeatureKeys.DomainInvestigation => new DomainInvestigationView { DataContext = viewModel },
#if NETWORK_LOCAL_AI
            FeatureKeys.AiAnalysis => new AiAnalysisView { DataContext = viewModel },
#endif
            _ => null,
        };
    }
}
