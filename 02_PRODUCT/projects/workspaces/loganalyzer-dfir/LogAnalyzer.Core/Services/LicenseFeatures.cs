using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Services.Edition;

namespace LogAnalyzer.Core.Services
{
    /// <summary>
    /// The fixed catalogue of licence capabilities (licence format LA2) and the base set of each module. A licence can only carry capabilities from
    /// this catalogue AND from the base set of the module it is issued for (it may carry fewer); anything else is refused.
    /// </summary>
    public static class LicenseFeatures
    {
        public const string ControlStation = "control.station";
        public const string CaseLocal = "case.local";
        public const string ReportPv = "report.pv";
        public const string Registers = "registers";
        public const string CollectLan = "collect.lan";
        public const string AuthAd = "auth.ad";
        public const string IntelOnline = "intel.online";
        public const string UpdateOnline = "update.online";
        public const string ResponseHost = "response.host";

        public const string ModuleInsula = "insula";
        public const string ModuleArhipelag = "arhipelag";
        public const string ModuleContinent = "continent";

        /// <summary>Every capability a licence may name, in catalogue order.</summary>
        public static IReadOnlyList<string> All { get; } =
            [ControlStation, CaseLocal, ReportPv, Registers, CollectLan, AuthAd, IntelOnline, UpdateOnline, ResponseHost];

        public static IReadOnlyList<string> Modules { get; } = [ModuleInsula, ModuleArhipelag, ModuleContinent];

        public static bool IsKnown(string? feature) => feature is not null && All.Contains(feature, StringComparer.Ordinal);

        public static bool IsKnownModule(string? module) => module is not null && Modules.Contains(module, StringComparer.Ordinal);

        /// <summary>The licence module id ("insula" | "arhipelag" | "continent") of a compiled posture; same mapping as <see cref="ModuleCatalog.ModuleName"/>.</summary>
        public static string ModuleId(StationPosture posture) => ModuleCatalog.ModuleName(posture).ToLowerInvariant();

        /// <summary>The posture of a module id, or null when the id is not a module.</summary>
        public static StationPosture? PostureOf(string? module) => module switch
        {
            ModuleInsula => StationPosture.Standalone,
            ModuleArhipelag => StationPosture.IsolatedNetwork,
            ModuleContinent => StationPosture.Internet,
            _ => null,
        };

        /// <summary>Base set of a module: insula the first four, arhipelag adds collect.lan, auth.ad and response.host, continent all. Empty for an unknown module.</summary>
        public static IReadOnlyList<string> DefaultFor(string? module) => module switch
        {
            ModuleInsula => [ControlStation, CaseLocal, ReportPv, Registers],
            ModuleArhipelag => [ControlStation, CaseLocal, ReportPv, Registers, CollectLan, AuthAd, ResponseHost],
            ModuleContinent => All,
            _ => [],
        };

        public static IReadOnlyList<string> DefaultFor(StationPosture posture) => DefaultFor(ModuleId(posture));

        /// <summary>What a licence for the module may carry (the base set of the module).</summary>
        public static bool IsAllowedFor(string module, string feature) => DefaultFor(module).Contains(feature, StringComparer.Ordinal);
    }
}
