using System;
using System.Collections.Generic;

namespace LogAnalyzer.Core.Services.Connectivity
{
    /// <summary>How the application runs on this station.</summary>
    public enum AppMode
    {
        /// <summary>Isolated station: no feature may open a network connection.</summary>
        AirGapped,
        /// <summary>Connected station: live monitoring, syslog, domain and online integrations are allowed.</summary>
        Network
    }

    public enum ConnectivityState
    {
        /// <summary>Windows reports Internet connectivity on at least one interface.</summary>
        Internet,
        /// <summary>A network is connected (LAN / intranet) but Windows does not report Internet.</summary>
        LocalNetworkOnly,
        /// <summary>No connected network.</summary>
        NoNetwork,
        /// <summary>Connectivity could not be determined.</summary>
        Unknown
    }

    /// <summary>What the probe saw, and how it found out. Never produced by sending traffic.</summary>
    public sealed record ConnectivitySnapshot(
        ConnectivityState State,
        string Source,
        IReadOnlyList<string> Details,
        DateTime ObservedUtc)
    {
        public static ConnectivitySnapshot Unknown(string reason) =>
            new(ConnectivityState.Unknown, "none", new[] { reason }, DateTime.UtcNow);
    }

    public sealed record ModeDecision(AppMode Mode, bool IsOverride, string Reason, ConnectivitySnapshot Snapshot);

    public interface IConnectivityProbe
    {
        ConnectivitySnapshot Probe();
    }

    /// <summary>
    /// Legacy entry point kept for callers that only have an unauthenticated request (--mode=, LogAnalyzer.mode, detection).
    /// None of those can select Network any more: the connected mode comes only from a signed policy
    /// (<see cref="LogAnalyzer.Core.Services.Edition.EditionPolicyVerifier"/>). Everything here resolves to AirGapped.
    /// </summary>
    public static class OperatingModeResolver
    {
        public const string ArgumentPrefix = "--mode=";

        public static ModeDecision Resolve(IReadOnlyList<string> args, string? stationOverride, ConnectivitySnapshot snapshot)
        {
            foreach (var arg in args)
                if (arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
                    return new ModeDecision(AppMode.AirGapped, false,
                        $"Argumentul „{arg}” nu poate schimba modul: doar o politică semnată poate activa modul conectat.", snapshot);
            if (!string.IsNullOrWhiteSpace(stationOverride))
                return new ModeDecision(AppMode.AirGapped, false,
                    $"Fișierul de mod („{stationOverride.Trim()}”) nu poate schimba modul: doar o politică semnată poate activa modul conectat.", snapshot);
            return Detect(snapshot);
        }

        /// <summary>Detection is informational only: even a confirmed Internet connection never switches an isolated station to Network.</summary>
        public static ModeDecision Detect(ConnectivitySnapshot snapshot) =>
            new(AppMode.AirGapped, false, $"Fără politică semnată modul este AirGapped (conectivitate observată: {snapshot.State}, {snapshot.Source}).", snapshot);

        /// <summary>Accepts airgapped/air-gapped/offline, network/online and auto; used only to report what was requested.</summary>
        public static bool TryParseOverride(string value, out AppMode? mode)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "airgapped":
                case "air-gapped":
                case "offline":
                    mode = AppMode.AirGapped; return true;
                case "network":
                case "online":
                    mode = AppMode.Network; return true;
                case "auto":
                    mode = null; return true;
                default:
                    mode = null; return false;
            }
        }
    }

    /// <summary>The mode chosen at startup. Set once; never switched automatically while the application runs.</summary>
    public static class AppModeContext
    {
        private static ModeDecision? _current;

        public static ModeDecision Current =>
            _current ?? new ModeDecision(AppMode.AirGapped, false, "Mod neinițializat; implicit AirGapped.",
                ConnectivitySnapshot.Unknown("not initialized"));

        public static bool IsAirGapped => Current.Mode == AppMode.AirGapped;

        public static void Initialize(ModeDecision decision)
        {
            if (_current is not null && _current != decision)
                throw new InvalidOperationException("Modul de operare a fost deja stabilit pentru această sesiune.");
            _current = decision;
        }

        /// <summary>For tests only: the application itself never resets the mode.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static void ResetForTests() => _current = null;
    }

    /// <summary>
    /// Single gate for every feature that opens a network connection (listeners included).
    /// In AirGapped mode it refuses, so a station treated as isolated cannot be made to talk by a code path.
    /// </summary>
    public static class NetworkPolicy
    {
        public static void EnsureAllowed(string feature)
        {
            if (AppModeContext.IsAirGapped)
                throw new NetworkBlockedException(feature);
        }
    }

    public sealed class NetworkBlockedException : InvalidOperationException
    {
        public string Feature { get; }

        public NetworkBlockedException(string feature)
            : base($"„{feature}” folosește rețeaua și este blocat în modul AirGapped.") => Feature = feature;
    }
}
