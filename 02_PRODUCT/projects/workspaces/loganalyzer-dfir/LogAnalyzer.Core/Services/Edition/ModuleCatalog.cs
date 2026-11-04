using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Services.Connectivity;

namespace LogAnalyzer.Core.Services.Edition
{
    /// <summary>
    /// The product modules (plan 2026-11-04, docs/dfir/PLAN_PRODUS_PROFESIONAL_2026-11-04.md section 2) and the single source of truth for which page of the
    /// main window exists in which module. A page a module can NEVER have is not listed at all (it is not shown disabled); a page the module has but that
    /// cannot work right now (for example a network page while a network station runs isolated) stays listed and says why.
    /// The module follows the compile-time posture (decision 13): Insula = Standalone, Arhipelag = IsolatedNetwork, Continent = Internet.
    /// </summary>
    public static class ModuleCatalog
    {
        public const string ProductName = "LogAnalyzer DFIR";

        public static string ModuleName(StationPosture p) => p switch
        {
            StationPosture.Standalone => "Insula",
            StationPosture.IsolatedNetwork => "Arhipelag",
            _ => "Continent",
        };

        public static string Descriptor(StationPosture p) => p switch
        {
            StationPosture.Standalone => "Stație izolată",
            StationPosture.IsolatedNetwork => "Rețea izolată",
            _ => "Rețea conectată",
        };

        /// <summary>"Insula — Stație izolată": the module name always shown with its descriptor.</summary>
        public static string DisplayName(StationPosture p) => $"{ModuleName(p)} — {Descriptor(p)}";

        /// <summary>One page of the main window: its tab, the modules that have it, whether it needs the network mode, and its variant (one tab, two pages by mode).</summary>
        /// <param name="Variant">"" for a page that is the same in every mode; "local" / "ad" for tab 8 (local accounts and USB when isolated, Active Directory on a network).</param>
        public sealed record PageEntry(string Key, int Tab, string Title, IReadOnlyList<StationPosture> Modules, bool NeedsNetworkMode, string Variant = "");

        private static readonly StationPosture[] All = [StationPosture.Standalone, StationPosture.IsolatedNetwork, StationPosture.Internet];
        private static readonly StationPosture[] Networked = [StationPosture.IsolatedNetwork, StationPosture.Internet];

        /// <summary>Every page of the main window, by tab (MainWindow.xaml TabControl order).</summary>
        public static IReadOnlyList<PageEntry> Pages { get; } =
        [
            new("0", 0, "Panou general", All, false),
            new("1", 1, "Jurnale Windows (EVTX)", All, false),
            new("2", 2, "Analiza registrului Windows", All, false),
            new("3", 3, "Amenințări și detecții", All, false),
            new("4", 4, "Cronologia evenimentelor", All, false),
            new("5", 5, "Analiza riscului", All, false),
            new("6", 6, "Firul atacului", All, false),
            // public actor profiles and indicator feeds come from outside: an isolated workstation never receives them
            new("7", 7, "Informații despre amenințări", Networked, false),
            new("8.local", 8, "Conturi locale și USB", All, false, "local"),
            new("8.ad", 8, "Active Directory", Networked, true, "ad"),
            new("9", 9, "Istoricul probelor", All, false),
            new("10", 10, "Tehnici de atac (MITRE ATT&CK)", All, false),
            new("11", 11, "Monitorizare în timp real", Networked, true),
            new("12", 12, "Colectare date", All, false),
            // isolating a process acts on hosts of the network; the isolated workstation edition has no response actions (EditionFeature.HostResponse)
            new("13", 13, "Izolare procese suspecte", Networked, false),
            new("14", 14, "Control stație", All, false),
            new("15", 15, "Investigație domeniu și e-mail", Networked, true),
            new("16", 16, "Investigație completă (caz)", All, false),
            new("17", 17, "Politici", All, false),
            new("18", 18, "Profil de proceduri", All, false),
            new("19", 19, "Monitor dispozitive", All, false),
            new("20", 20, "Registru utilizatori", All, false),
            new("21", 21, "Autentificare și conturi", All, false),
            new("22", 22, "Acasă", All, false),
            new("23", 23, "Registrul de transferuri", All, false),
        ];

        public static PageEntry? Find(string key) => Pages.FirstOrDefault(p => p.Key == key);

        /// <summary>The module can have the page at all (otherwise it is never shown, not even disabled).</summary>
        public static bool Exists(PageEntry page, StationPosture module) => page.Modules.Contains(module);

        /// <summary>
        /// The page is listed in the menu now: it exists in the module and, for a page that is one of two variants of a tab, it is the variant of the current
        /// mode (tab 8: local accounts when isolated, Active Directory on a network). In the menu, a network page of a network module whose station runs isolated
        /// is not listed either (the menu has no room for a reason); the role pages show it disabled with <see cref="UnavailableReason"/>.
        /// </summary>
        public static bool IsListed(PageEntry page, StationPosture module, AppMode mode)
        {
            if (!Exists(page, module)) return false;
            if (page.NeedsNetworkMode && mode == AppMode.AirGapped) return false;
            return page.Variant switch
            {
                "local" => mode == AppMode.AirGapped,
                "ad" => mode != AppMode.AirGapped,
                _ => true,
            };
        }

        /// <summary>Why a listed page cannot work right now, or empty.</summary>
        public static string UnavailableReason(PageEntry page, AppMode mode) =>
            page.NeedsNetworkMode && mode == AppMode.AirGapped ? RoleProfiles.NeedsNetworkReason : "";

        /// <summary>A tab some page of the module can show (used to keep hidden pages out of reach through shortcuts).</summary>
        public static bool TabExists(int tab, StationPosture module) => Pages.Any(p => p.Tab == tab && Exists(p, module));
    }
}
