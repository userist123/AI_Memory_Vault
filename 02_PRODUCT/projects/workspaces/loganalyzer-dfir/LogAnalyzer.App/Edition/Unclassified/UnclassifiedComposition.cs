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
        public const string PublicKeyResource = "edition-policy-public-key.b64";

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

            var highWaterPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(EditionPolicyVerifier.DefaultPolicyPath)!, "policy.highwater");
            long highWater = 0;
            try { if (System.IO.File.Exists(highWaterPath)) long.TryParse(System.IO.File.ReadAllText(highWaterPath).Trim(), out highWater); } catch (System.IO.IOException) { }

            string? policyText = null;
            try { if (System.IO.File.Exists(EditionPolicyVerifier.DefaultPolicyPath)) policyText = System.IO.File.ReadAllText(EditionPolicyVerifier.DefaultPolicyPath); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }

            string? key = null;
            using (var st = typeof(EditionComposition).Assembly.GetManifestResourceStream(PublicKeyResource))
                if (st is not null) { using var r = new System.IO.StreamReader(st); key = r.ReadToEnd(); }

            var result = EditionPolicyVerifier.Verify(policyText, key, Environment.MachineName, DateTimeOffset.UtcNow, highWater);
            if (result.Valid && result.Policy!.Version > highWater)
            {
                // Best effort (needs write access to the policy directory, i.e. administrators): later, older policies are refused.
                try { System.IO.File.WriteAllText(highWaterPath, result.Policy.Version.ToString()); } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            }
            var mode = EditionPolicyVerifier.Decide(result, snapshot, requested);
            var role = StationRoleResolver.Decide(EditionKind.Unclassified, result, mode, DateTimeOffset.UtcNow);
            return new StartupDecision(mode, role);
        }

        public static void Register(IServiceCollection services)
        {
            services.AddSingleton<IEditionProfile, UnclassifiedEditionProfile>();
            services.AddSingleton<IFeatureViewFactory, UnclassifiedFeatureViews>();
            services.AddSingleton<IHostDefense, HostDefense>();
            services.AddSingleton<ILiveEventSourceFactory, LiveEventSourceFactory>();
            services.AddSingleton<IConnectivityWatcher, NetworkChangeWatcher>();
            services.AddSingleton<IRegistryValueWriter, RegistryValueWriter>();
            services.AddSingleton<IAuditCollector, PowerShellAuditCollector>();
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
