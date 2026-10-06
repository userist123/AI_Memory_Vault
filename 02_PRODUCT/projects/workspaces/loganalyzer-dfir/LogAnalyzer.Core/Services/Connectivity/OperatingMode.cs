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
    /// Chooses the operating mode. Precedence: command line (--mode=), then the station's mode file, then detection.
    /// Detection maps only a confirmed Internet connection to Network; anything else, including Unknown, is AirGapped
    /// (fail closed: an isolated station must never start networked features because detection failed).
    /// </summary>
    public static class OperatingModeResolver
    {
        public const string ArgumentPrefix = "--mode=";

        public static ModeDecision Resolve(IReadOnlyList<string> args, string? stationOverride, ConnectivitySnapshot snapshot)
        {
            foreach (var arg in args)
            {
                if (arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg[ArgumentPrefix.Length..];
                    if (TryParseOverride(value, out var forced))
                        return forced is AppMode m
                            ? new ModeDecision(m, true, $"Mod forțat din linia de comandă ({arg}).", snapshot)
                            : Detect(snapshot);
                    return new ModeDecision(AppMode.AirGapped, true,
                        $"Argument necunoscut „{arg}”; se folosește modul sigur AirGapped.", snapshot);
                }
            }

            if (!string.IsNullOrWhiteSpace(stationOverride))
            {
                if (TryParseOverride(stationOverride.Trim(), out var forced))
                    return forced is AppMode m
                        ? new ModeDecision(m, true, $"Mod fixat pentru această stație (fișier de configurare: {stationOverride.Trim()}).", snapshot)
                        : Detect(snapshot);
                return new ModeDecision(AppMode.AirGapped, true,
                    $"Valoare necunoscută în fișierul de mod („{stationOverride.Trim()}”); se folosește modul sigur AirGapped.", snapshot);
            }

            return Detect(snapshot);
        }

        public static ModeDecision Detect(ConnectivitySnapshot snapshot) => snapshot.State switch
        {
            ConnectivityState.Internet => new ModeDecision(AppMode.Network, false,
                $"Windows raportează conexiune la Internet ({snapshot.Source}).", snapshot),
            ConnectivityState.LocalNetworkOnly => new ModeDecision(AppMode.AirGapped, false,
                $"Rețea locală fără Internet ({snapshot.Source}); stația este tratată ca izolată.", snapshot),
            ConnectivityState.NoNetwork => new ModeDecision(AppMode.AirGapped, false,
                $"Nicio rețea conectată ({snapshot.Source}).", snapshot),
            _ => new ModeDecision(AppMode.AirGapped, false,
                "Conectivitatea nu a putut fi determinată; se folosește modul sigur AirGapped.", snapshot)
        };

        /// <summary>Accepts airgapped/air-gapped/offline, network/online and auto. Auto yields a null mode.</summary>
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
