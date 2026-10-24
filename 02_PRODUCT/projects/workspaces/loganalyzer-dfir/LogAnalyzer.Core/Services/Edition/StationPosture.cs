using LogAnalyzer.Core.Services.Connectivity;

namespace LogAnalyzer.Core.Services.Edition
{
    /// <summary>
    /// The network posture of the station an edition is built for. It is fixed at COMPILE time by the edition's composition
    /// (Standalone executable -> Standalone, Network executable -> IsolatedNetwork, Internet executable -> Internet) and
    /// is never read from a file, an argument or a policy.
    /// </summary>
    public enum StationPosture
    {
        /// <summary>Air-gapped single station: no network, no internet, no wireless. The edition contains no network code.</summary>
        Standalone,
        /// <summary>Air-gapped LAN (domain controllers, servers, domains): internal network yes, internet never.</summary>
        IsolatedNetwork,
        /// <summary>Connected station with internet access: hardening rules apply, isolation rules do not.</summary>
        Internet,
    }

    /// <summary>The posture of this process, set once at startup from the edition's composition. Defaults to the strictest posture (fail closed).</summary>
    public static class StationPostureContext
    {
        private static StationPosture? _current;
        public static StationPosture Current => _current ?? StationPosture.Standalone;

        public static void Initialize(StationPosture posture)
        {
            if (_current is not null && _current != posture)
                throw new System.InvalidOperationException("Postura ediției a fost deja stabilită pentru această sesiune.");
            _current = posture;
        }

        /// <summary>For tests only: the application itself never resets the posture.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static void ResetForTests() => _current = null;
    }

    public static class StationPostures
    {
        /// <summary>Romanian label shown in the window title, the UI and the reports.</summary>
        public static string EditionLabel(StationPosture p) => p switch
        {
            StationPosture.Standalone => "Standalone",
            StationPosture.IsolatedNetwork => "Rețea air-gap",
            _ => "Internet",
        };

        /// <summary>
        /// Interaction with the signed edition policy: the compile-time posture ALWAYS wins and is never changed by a policy. A policy
        /// only chooses the runtime <see cref="AppMode"/>, and only inside what the posture permits: a Standalone edition is always
        /// AirGapped (a "connected" policy is ignored); an IsolatedNetwork edition may be narrowed to AirGapped by a policy but is never
        /// widened beyond the LAN (there is no internet code in it); the Internet edition follows the policy.
        /// </summary>
        public static AppMode ClampMode(StationPosture compiled, AppMode requested) =>
            compiled == StationPosture.Standalone ? AppMode.AirGapped : requested;
    }
}
