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

        /// <summary>
        /// The mode badge of the main window. The runtime mode has two values (air-gapped or not); a not-air-gapped Arhipelag is on its closed LAN,
        /// never on the Internet (lab 2026-11-07: „Mod: conectat” on a closed network read as Internet access).
        /// </summary>
        public static string ModeBadge(StationPosture p, bool airGapped) =>
            airGapped || p == StationPosture.Standalone ? "Mod: izolat (fără rețea)" : p == StationPosture.IsolatedNetwork ? "Mod: rețea locală, fără Internet" : "Mod: conectat";

        /// <summary>One sentence: what the module is for (sign-in screen).</summary>
        public static string Purpose(StationPosture p) => p switch
        {
            StationPosture.Standalone => "Control de conformitate și investigație pe un calculator izolat, fără nicio conexiune de rețea.",
            StationPosture.IsolatedNetwork => "Control de conformitate și investigație pe stațiile unei rețele închise, cu domeniu, fără internet.",
            _ => "Control de conformitate și investigație pe stații conectate, cu interconectare aprobată.",
        };

        /// <summary>
        /// The notice shown before sign-in (plan R3, model: notice and consent). It states only what the application really does (sign-ins and actions go to its
        /// sealed audit). The unit's own approved wording, from its security officer, replaces it when the procedure profile carries one.
        /// </summary>
        public static string DefaultNotice(StationPosture p) =>
            (p == StationPosture.Internet ? "Acest sistem este destinat activității de securitate a organizației. " : "Acest sistem prelucrează informații clasificate. ") +
            "Accesul este permis numai persoanelor autorizate. Autentificările și acțiunile sunt înregistrate în jurnalul sigilat al aplicației și pot fi verificate la control.";

        /// <summary>One page of the main window: its tab, the modules that have it, whether it needs the network mode, and its variant (one tab, two pages by mode).</summary>
        /// <param name="Capability">The licence capability (<see cref="LicenseFeatures"/>) the page needs; null = no requirement.</param>
        /// <param name="Variant">"" for a page that is the same in every mode; "local" / "ad" for tab 8 (local accounts and USB when isolated, Active Directory on a network).</param>
        public sealed record PageEntry(string Key, int Tab, string Title, IReadOnlyList<StationPosture> Modules, bool NeedsNetworkMode, string Variant = "", string? Capability = null)
        {
            /// <summary>The tab as the text the navigation command takes as its parameter.</summary>
            public string TabText => Tab.ToString(System.Globalization.CultureInfo.InvariantCulture);
            /// <summary>Name announced by a screen reader for the page item.</summary>
            public override string ToString() => Title;

            /// <summary>The glossary key whose technical name and explanation are the entry's tooltip (WP6a); null = no glossary term.</summary>
            public string? Term { get; init; }
        }

        /// <summary>One section of the „Avansat” menu with the pages it lists now.</summary>
        public sealed record MenuSection(string Name, IReadOnlyList<PageEntry> Pages)
        {
            /// <summary>Name announced by a screen reader for the section (evaluation 2026-11-05: the record text with the list type was announced).</summary>
            public override string ToString() => Name;
        }

        private static readonly StationPosture[] All = [StationPosture.Standalone, StationPosture.IsolatedNetwork, StationPosture.Internet];
        private static readonly StationPosture[] Networked = [StationPosture.IsolatedNetwork, StationPosture.Internet];

        /// <summary>Every page of the main window, by tab (MainWindow.xaml TabControl order).</summary>
        public static IReadOnlyList<PageEntry> Pages { get; } =
        [
            new("0", 0, "Panou general", All, false),
            new("1", 1, "Jurnale Windows (EVTX)", All, false) { Term = "evtx" },
            new("2", 2, "Analiza registrului Windows", All, false),
            new("3", 3, "Amenințări și detecții", All, false),
            new("4", 4, "Cronologia evenimentelor", All, false),
            new("5", 5, "Analiza riscului", All, false),
            new("6", 6, "Firul atacului", All, false),
            // public actor profiles and indicator feeds come from outside: an isolated workstation never receives them
            new("7", 7, "Informații despre amenințări", Networked, false),
            new("8.local", 8, "Conturi locale și USB", All, false, "local"),
            new("8.ad", 8, "Active Directory", Networked, true, "ad", Capability: LicenseFeatures.AuthAd),
            new("9", 9, "Istoricul probelor", All, false, Capability: LicenseFeatures.CaseLocal) { Term = "chain_of_custody" },
            new("10", 10, "Tehnici de atac (MITRE ATT&CK)", All, false),
            new("11", 11, "Monitorizare în timp real", Networked, true, Capability: LicenseFeatures.CollectLan),
            new("12", 12, "Colectare date", All, false, Capability: LicenseFeatures.CaseLocal),
            // isolating a process acts on hosts of the network; the isolated workstation edition has no response actions (EditionFeature.HostResponse)
            new("13", 13, "Izolare procese suspecte", Networked, false, Capability: LicenseFeatures.ResponseHost),
            new("14", 14, "Control stație", All, false, Capability: LicenseFeatures.ControlStation),
            new("15", 15, "Investigație domeniu și e-mail", Networked, true, Capability: LicenseFeatures.CollectLan),
            new("16", 16, "Investigație completă (caz)", All, false, Capability: LicenseFeatures.CaseLocal),
            new("17", 17, "Politici", All, false),
            new("18", 18, "Profil de proceduri", All, false),
            new("19", 19, "Monitor dispozitive", All, false, Capability: LicenseFeatures.ControlStation),
            new("20", 20, "Registru utilizatori", All, false, Capability: LicenseFeatures.Registers),
            new("21", 21, "Autentificare și conturi", All, false),
            new("22", 22, "Acasă", All, false),
            new("23", 23, "Registrul de transferuri", All, false, Capability: LicenseFeatures.Registers),
        ];

        /// <summary>
        /// The „Avansat” menu layout, the single source of the sections and of the order inside them (the flow of a case). Page keys are those of <see cref="Pages"/>.
        /// Acasă (22) is not in it: it is the fixed first entry of the sidebar.
        /// </summary>
        private static readonly (string Section, string[] Keys)[] MenuLayout =
        [
            ("Examinare", ["0", "4", "1", "2", "3", "5", "6", "10", "8.local"]),
            ("Probe și colectare", ["16", "12", "9"]),
            ("Control stație", ["14", "19"]),
            ("Registre", ["23", "20"]),
            ("Rețea și domeniu", ["8.ad", "11", "13", "15"]),
            ("Amenințări", ["7"]),
            ("Administrare", ["17", "18", "21"]),
        ];

        /// <summary>The section names of the „Avansat” menu, in order.</summary>
        public static IEnumerable<string> MenuSectionNames => MenuLayout.Select(l => l.Section);

        /// <summary>The section of the „Avansat” menu a page key belongs to, or null (Acasă).</summary>
        public static string? SectionOf(string key) => MenuLayout.FirstOrDefault(l => l.Keys.Contains(key)).Section;

        /// <summary>
        /// The „Avansat” menu for a module and mode: the non-empty sections in order, each with the pages that are listed (<see cref="IsListed"/>) and are not
        /// already main pages of the role (<paramref name="roleMainTabs"/>). A section without pages is left out.
        /// </summary>
        public static IReadOnlyList<MenuSection> Menu(StationPosture module, AppMode mode, IReadOnlyList<int> roleMainTabs) =>
            Menu(module, mode, roleMainTabs, f => LicenseFeatures.DefaultFor(module).Contains(f));

        /// <summary>
        /// Same menu, but a page whose licence capability (<see cref="PageEntry.Capability"/>) <paramref name="has"/> denies is left out, as a page the module does not have.
        /// </summary>
        public static IReadOnlyList<MenuSection> Menu(StationPosture module, AppMode mode, IReadOnlyList<int> roleMainTabs, Func<string, bool> has)
        {
            var sections = new List<MenuSection>();
            foreach (var (name, keys) in MenuLayout)
            {
                var pages = keys.Select(Find).OfType<PageEntry>().Where(p => IsListed(p, module, mode) && IsLicensed(p, has) && !roleMainTabs.Contains(p.Tab)).ToList();
                if (pages.Count > 0) sections.Add(new MenuSection(name, pages));
            }
            return sections;
        }

        /// <summary>The licence grants the capability the page needs (a page without a requirement is always licensed).</summary>
        public static bool IsLicensed(PageEntry page, Func<string, bool> has) => page.Capability is null || has(page.Capability);

        /// <summary>
        /// The licence lets the tab open: true when some page of the tab needs no capability or has its capability granted; a tab no page declares is not decided here.
        /// </summary>
        public static bool IsTabLicensed(int tab, Func<string, bool> has)
        {
            var pages = Pages.Where(p => p.Tab == tab).ToList();
            return pages.Count == 0 || pages.Any(p => IsLicensed(p, has));
        }

        /// <summary>The tab may be opened in this module with these rights: the module has a page for it and the licence allows it. A tab outside the catalogue is not decided here.</summary>
        public static bool CanOpenTab(int tab, StationPosture module, Func<string, bool> has) =>
            !Pages.Any(p => p.Tab == tab) || Pages.Any(p => p.Tab == tab && Exists(p, module) && IsLicensed(p, has));

        /// <summary>
        /// Review of R2b: like <see cref="CanOpenTab(int, StationPosture, Func{string, bool})"/>, but only the page the tab SHOWS in this mode counts (tab 8 is local accounts
        /// when isolated and Active Directory on a network): an Arhipelag / Continent licence without auth.ad cannot open tab 8 in network mode through the local variant.
        /// </summary>
        public static bool CanOpenTab(int tab, StationPosture module, Func<string, bool> has, AppMode mode) =>
            !Pages.Any(p => p.Tab == tab) || Pages.Any(p => p.Tab == tab && Exists(p, module) && IsLicensed(p, has) && ShownIn(p, mode));

        private static bool ShownIn(PageEntry page, AppMode mode) => page.Variant switch
        {
            "local" => mode == AppMode.AirGapped,
            "ad" => mode != AppMode.AirGapped,
            _ => true,
        };

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
