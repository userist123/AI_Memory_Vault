using System;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Notices computed once at startup and shown without blocking anything (finding B10): the station must run .NET 10.0.13 or newer (SignedCms
    /// denial-of-service fix). Local only: it reads the runtime version of the process; no network, no prompt that stops the start.
    /// </summary>
    public static class StartupNotices
    {
        public static string RuntimeWarning { get; private set; } = "";
        public static bool HasRuntimeWarning => RuntimeWarning.Length > 0;

        /// <summary>Computes the notice for <paramref name="runtime"/> (default: the running .NET) and returns it ("" = none).</summary>
        public static string Initialize(Version? runtime = null)
        {
            RuntimeWarning = RuntimeGuard.Warning(runtime ?? Environment.Version) is { } w ? "Atenție .NET: " + w + "." : "";
            return RuntimeWarning;
        }
    }
}
