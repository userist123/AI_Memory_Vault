using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Second-channel rules of the air-gap category: printing (D8), screenshots and clipboard / activity history (D9), virtual machines and a second operating
/// system (D12), archiver histories pointing at removable drives (D13) and per-application upload outliers (D14). Each rule says what the record shows and
/// what it does not; none of them claims that data left the station.
/// </summary>
public static partial class Wp14Rules
{
    private static string ScopeWord(Ctx c) => c.Scope.Network == NetworkCategory.StandalonePc ? "autonom" : "air-gapped";

    private static EvidenceRef FactRef(Ctx c, string locator, string description) => new(c.Facts?.EvidenceId ?? "", locator, description);

    /// <summary>A finding of the air-gap category that is not about a connection: no authorised-list basis exists for it, so the authorisation stays undefined.</summary>
    private static Finding Chan(Ctx c, string ruleId, string title, Severity sev, string subcategory, string channel, string observed, DateTimeOffset? first, DateTimeOffset? last,
        string who, string obj, string direction, string destination, string description, string reason, string[] alternatives, IReadOnlyList<EvidenceRef> evidence,
        IReadOnlyList<string> evidenceText, string mitre, SemanticType semantic, Confidence confidence = Confidence.Medium, Classification classification = Classification.Direct) => new()
    {
        FindingId = c.NextId(), RuleId = ruleId, Title = title, Severity = sev, Category = c.FindingCategory, Classification = classification, Confidence = confidence, MitreTechniqueId = mitre,
        FirstSeenUtc = first, LastSeenUtc = last ?? first, User = who.StartsWith("necunoscut", StringComparison.Ordinal) ? "" : who,
        Description = description, ClassificationReason = reason, SemanticType = semantic,
        SupportingEvidence = [.. evidence.Take(20)], AlternativeExplanations = [.. alternatives],
        AirGap = new AirGapDetail(subcategory, channel, AirGapAuthorization.Undefined, "profilul de proceduri nu conține o listă aprobată pentru acest canal: constatarea cere explicație", observed, first,
            who, obj, c.CaseClassText, "nu se aplică (nu este un mediu registrat)", direction, destination, [.. evidenceText.Take(5)]),
    };

    private static void Channels(Ctx c, List<Finding> o)
    {
        Archivers(c, o);
        OpticalFacts(c, o);
        MediaZoneRoundTrip(c, o);
        if (!c.AirGapScope) return;
        AdapterKindsFromFacts(c, o);
        BadUsb(c, o);
        Printing(c, o);
        ScreenChannels(c, o);
        VirtualChannels(c, o);
        UploadOutliers(c, o);
        CovertHardware(c, o);
    }

    // ---- D8: printing ----

    private static bool PrinterWritesFile(Ctx c, string printer, string driver, string port)
    {
        var l = c.Data.Channels;
        return l.PrintToFileDriverWords.Any(w => (printer + " " + driver).Contains(w, StringComparison.OrdinalIgnoreCase)) || l.PrintToFilePortWords.Any(w => port.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static string PrintUsers(List<TimelineEvent> items)
    {
        var users = items.Select(e => F(e, "Param3", "UserName", "User")).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return users.Count > 0 ? string.Join(", ", users) : UnknownWho;
    }

    private static void Printing(Ctx c, List<Finding> o)
    {
        const string Ch = "Microsoft-Windows-PrintService/Operational";
        var facts = c.Facts?.Print;
        // event 307 (document printed): Param2 document, Param3 user, Param4 computer, Param5 printer, Param6 port, Param7 bytes, Param8 pages
        var jobs = c.Events.Where(e => Ev(e, Ch, 307) && (c.Scope.PeriodFromUtc is null && c.Scope.PeriodToUtc is null || c.InPeriod(T(e)))).OrderBy(e => T(e)).ToList();
        foreach (var g in jobs.GroupBy(e => F(e, "Param5", "PrinterName", "Printer"), StringComparer.OrdinalIgnoreCase))
        {
            var items = g.ToList();
            var printer = g.Key.Length > 0 ? g.Key : "(imprimantă nespecificată)";
            var known = facts?.Printers.FirstOrDefault(p => p.Name.Equals(g.Key, StringComparison.OrdinalIgnoreCase));
            var port = F(items[0], "Param6", "PortName", "Port");
            bool toFile = PrinterWritesFile(c, printer, known?.Driver ?? "", known?.Port is { Length: > 0 } kp ? kp : port);
            bool network = known?.IsNetwork == true || port.StartsWith("\\\\", StringComparison.Ordinal) || port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) || port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase);
            int pages = items.Sum(e => int.TryParse(F(e, "Param8", "Pages", "TotalPages"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0);
            var docs = items.Take(6).Select(e => $"„{Shorten(F(e, "Param2", "DocumentName", "Document"))}” ({Time(T(e))}, utilizator {(F(e, "Param3", "UserName", "User") is { Length: > 0 } u ? u : "necunoscut")}, {F(e, "Param8", "Pages", "TotalPages") switch { "" => "pagini necunoscute", var p => p + " pagini" }})");
            var times = items.Select(T).Where(t => t is not null).Select(t => t!.Value).ToList();
            var kind = toFile ? "imprimantă virtuală care scrie un fișier (PDF/XPS): documentul nu iese pe hârtie, ci devine un fișier nou" : network ? "imprimantă de rețea" : "imprimantă locală";
            var desc = $"{LogAnalyzer.Dfir.Language.RoText.Count(items.Count, "document trimis", "documente trimise")} la imprimanta „{printer}” ({kind}) pe un sistem {ScopeWord(c)}, {Time(times.Count > 0 ? times[0] : null)} – {Time(times.Count > 0 ? times[^1] : null)}; {pages} pagini în total. " +
                       "Documente: " + string.Join("; ", docs) + (items.Count > 6 ? $" … (+{items.Count - 6})" : "") + ". " +
                       "Evenimentul 307 arată că un document a fost trimis la imprimantă; nu arată conținutul, nivelul de clasificare și nu dovedește că foaia a fost predată sau că ieșirea a fost înregistrată în registrul de tipărire.";
            o.Add(Chan(c, "AIRGAP-PRINT-JOB", $"Tipărire pe un sistem izolat: {LogAnalyzer.Dfir.Language.RoText.Count(items.Count, "document", "documente")} la „{printer}”", toFile || network ? Sev(c, Severity.Medium) : Sev(c, Severity.Low),
                "PRINT/SCAN", "PrintService/Operational 307", $"{LogAnalyzer.Dfir.Language.RoText.Count(items.Count, "document", "documente")} la „{printer}”", times.Count > 0 ? times[0] : null, times.Count > 0 ? times[^1] : null,
                PrintUsers(items),
                $"imprimanta „{printer}”", toFile ? "ieșire într-un fișier (imprimantă virtuală)" : "ieșire pe hârtie (document tipărit)", printer, desc,
                "Evenimente PrintService/Operational 307 (document tipărit) în perioada cazului, grupate pe imprimantă.",
                ["Tipărirea documentelor este o activitate normală a unei stații de lucru, dacă procedura o permite și foile sunt înregistrate.", "Un test de imprimantă sau o pagină de probă produce același eveniment.",
                 toFile ? "Imprimanta virtuală PDF este instalată implicit de Windows și este folosită și pentru a păstra o copie a unui document." : "Imprimanta poate fi cea aprobată a încăperii."],
                items.Select(e => Ref(e, $"307 {Shorten(F(e, "Param2", "DocumentName", "Document"))}")).ToList(), items.Select(e => $"{e.Source} {e.EventId} {e.Locator}".Trim()).ToList(), "", SemanticType.Observation));
        }
        if (facts is null) return;
        foreach (var p in facts.Printers.Where(p => p.IsNetwork))
            o.Add(Chan(c, "AIRGAP-PRINT-NETWORK-PRINTER", $"Imprimantă de rețea instalată pe un sistem izolat: {p.Name}", Sev(c, Severity.Medium), "PRINT/SCAN", "Imprimante instalate (spooler)",
                $"imprimanta „{p.Name}” (port {(p.Port.Length > 0 ? p.Port : "necunoscut")}) este de rețea", c.Facts!.CollectedUtc, null, "necunoscut: lista de imprimante nu conține contul", $"imprimanta „{p.Name}”", "necunoscută: lista nu arată ce s-a trimis", p.Port.Length > 0 ? p.Port : "port necunoscut",
                $"Pe sistemul {ScopeWord(c)} este instalată imprimanta „{p.Name}” cu portul „{p.Port}”, care indică o imprimantă din rețea (driver: {(p.Driver.Length > 0 ? p.Driver : "necunoscut")}). O imprimantă de rețea este o cale către o altă rețea; instalarea nu arată că s-a tipărit ceva.",
                "Imprimantă cu port de rețea (adresă IP, nume UNC sau WSD) în lista imprimantelor instalate.",
                ["Imprimanta poate fi dedicată, într-un segment aprobat al zonei, și fi permisă prin procedură.", "O imprimantă veche, rămasă instalată dintr-o configurare anterioară a stației, nu mai este folosită."],
                [FactRef(c, $"printers/{p.Name}", $"imprimantă {p.Name}, port {p.Port}")], [$"airgap_facts printers/{p.Name}"], "", SemanticType.Configuration));
        if (facts.OperationalLogEnabled == false && facts.Printers.Count > 0)
            o.Add(Chan(c, "AIRGAP-PRINT-UNLOGGED", "Jurnalul de tipărire este oprit pe un sistem izolat", Sev(c, Severity.Medium), "PRINT/SCAN", "PrintService/Operational (starea jurnalului)",
                $"jurnalul PrintService/Operational este oprit; {LogAnalyzer.Dfir.Language.RoText.Count(facts.Printers.Count, "imprimantă instalată", "imprimante instalate")}", c.Facts!.CollectedUtc, null, "necunoscut: starea jurnalului nu conține contul", "jurnalul PrintService/Operational", "necunoscută: nu se înregistrează ce se tipărește", "nu se aplică",
                $"Pe sistemul {ScopeWord(c)} sunt instalate {LogAnalyzer.Dfir.Language.RoText.Count(facts.Printers.Count, "imprimantă", "imprimante")}, dar jurnalul PrintService/Operational este oprit (este oprit implicit în Windows). Fără el nu se poate afla ce documente s-au tipărit, de cine și când; absența evenimentelor de tipărire nu dovedește că nu s-a tipărit.",
                "Valoarea Enabled a canalului PrintService/Operational este 0, iar stația are imprimante instalate.",
                ["Jurnalul este oprit implicit de Windows; organizația poate ține evidența tipăririi pe altă cale (server de tipărire, contor al imprimantei, registru de foi).", "Imprimantele pot fi virtuale (PDF) și nelăsate pentru tipărire reală."],
                [FactRef(c, "print/operationalLog", "PrintService/Operational: oprit")], ["airgap_facts print/operationalLog"], "", SemanticType.Configuration));
    }

    // ---- D9: screenshots, clipboard history, Windows activity history ----

    private static void ScreenChannels(Ctx c, List<Finding> o)
    {
        var s = c.Facts?.Screen;
        if (s is null) return;
        bool NoPeriod() => c.Scope.PeriodFromUtc is null && c.Scope.PeriodToUtc is null;
        var shots = s.Screenshots.Where(x => NoPeriod() || c.InPeriod(x.CreatedUtc)).OrderBy(x => x.CreatedUtc).ToList();
        var traces = s.SnippingTraces.Where(x => NoPeriod() || c.InPeriod(x.CreatedUtc)).OrderBy(x => x.CreatedUtc).ToList();
        if (shots.Count + traces.Count > 0)
        {
            var times = shots.Concat(traces).Select(x => x.CreatedUtc).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
            var names = shots.Take(6).Select(x => WinPath.GetFileName(x.Path));
            var desc = $"Pe sistemul {ScopeWord(c)} există {LogAnalyzer.Dfir.Language.RoText.Count(shots.Count, "fișier de captură de ecran", "fișiere de capturi de ecran")} în folderul Capturi de ecran" +
                       (shots.Count > 0 ? $" ({string.Join(", ", names)}{(shots.Count > 6 ? " …" : "")})" : "") + (traces.Count > 0 ? $" și {LogAnalyzer.Dfir.Language.RoText.Count(traces.Count, "urmă", "urme")} ale Snipping Tool / Screen Sketch" : "") +
                       $", în perioada cazului ({Time(times.Count > 0 ? times[0] : null)} – {Time(times.Count > 0 ? times[^1] : null)}). O captură de ecran este o copie a ceea ce se vedea pe monitor, adică un al doilea canal prin care conținutul clasificat poate ajunge într-un fișier neînregistrat. " +
                       "Fișierul arată că imaginea există, nu ce conține și nici că a fost scoasă din stație.";
            o.Add(Chan(c, "AIRGAP-SCREENSHOTS", $"Capturi de ecran pe un sistem izolat: {shots.Count + traces.Count}", Sev(c, Severity.Medium), "FILE TRANSFER", "Pictures\\Screenshots / Snipping Tool",
                $"{shots.Count} capturi de ecran, {traces.Count} urme de instrument de decupare", times.Count > 0 ? times[0] : null, times.Count > 0 ? times[^1] : null, "necunoscut: fișierul nu conține contul", "capturi de ecran", "necunoscută: fișierul arată crearea, nu transferul", "fișiere locale",
                desc, "Fișiere în Pictures\\Screenshots și foldere de lucru ale aplicațiilor de decupare, create în perioada cazului.",
                ["Capturile pot ilustra o procedură sau o eroare raportată (suport tehnic intern) și pot fi permise de procedură.", "Tasta Print Screen sau Win+Shift+S creează fișiere fără intenția de a le scoate din stație.", "Data fișierului poate fi modificată de copiere sau restaurare dintr-o copie de siguranță."],
                [.. shots.Take(10).Select(x => FactRef(c, $"screen/screenshots/{WinPath.GetFileName(x.Path)}", $"captură {WinPath.GetFileName(x.Path)}")), .. traces.Take(5).Select(x => FactRef(c, $"screen/snipping/{WinPath.GetFileName(x.Path)}", $"urmă {WinPath.GetFileName(x.Path)}"))],
                shots.Take(5).Select(x => $"airgap_facts screen/screenshots/{WinPath.GetFileName(x.Path)}").ToList(), "", SemanticType.Presence));
        }
        bool clip = s.ClipboardHistoryUser == 1 && s.ClipboardHistoryPolicy != 0;
        bool activity = s.ActivitiesCachePresent == true && s.ActivityFeedPolicy != 0;
        if (clip || activity)
        {
            var parts = new List<string>();
            if (clip) parts.Add("istoricul clipboard este pornit (ultimele elemente copiate sunt păstrate și pot fi sincronizate)");
            if (activity) parts.Add("istoricul de activitate Windows (Timeline, ActivitiesCache.db) există și nu este oprit prin politică (păstrează documentele și aplicațiile deschise)");
            o.Add(Chan(c, "AIRGAP-CLIPBOARD-TIMELINE", "Istoric clipboard sau de activitate pornit pe un sistem izolat", Sev(c, Severity.Low), "FILE TRANSFER", "Clipboard history / ActivitiesCache",
                string.Join("; ", parts), c.Facts!.CollectedUtc, null, "necunoscut: setarea nu conține contul", "istoric clipboard / activitate", "necunoscută: este o setare, nu un transfer", "date locale ale utilizatorului",
                $"Pe sistemul {ScopeWord(c)}: " + string.Join("; ", parts) + ". Aceste istorice păstrează pe disc fragmente din ce a fost afișat sau copiat, deci pot păstra text dintr-un document clasificat în afara registrelor. Setarea arată posibilitatea, nu că ceva a fost extras.",
                "EnableClipboardHistory = 1 (fără AllowClipboardHistory = 0) și / sau ActivitiesCache.db prezent fără EnableActivityFeed = 0.",
                ["Istoricul clipboard este oprit implicit; dacă este pornit, utilizatorul l-a pornit (Win+V).", "ActivitiesCache.db poate exista și golit sau fără intrări recente."],
                [FactRef(c, "screen/history", "istoric clipboard / Timeline")], ["airgap_facts screen/history"], "", SemanticType.Configuration));
        }
    }

    // ---- D12: virtual machines, second operating system ----

    private static void VirtualChannels(Ctx c, List<Finding> o)
    {
        var v = c.Facts?.Virtual;
        if (v is null) return;
        var present = new List<string>();
        if (v.HyperVMachines.Count > 0) present.Add($"{LogAnalyzer.Dfir.Language.RoText.Count(v.HyperVMachines.Count, "mașină virtuală Hyper-V", "mașini virtuale Hyper-V")} ({string.Join(", ", v.HyperVMachines.Take(5))})");
        if (v.WslDistributions.Count > 0) present.Add($"{LogAnalyzer.Dfir.Language.RoText.Count(v.WslDistributions.Count, "distribuție WSL", "distribuții WSL")} ({string.Join(", ", v.WslDistributions.Take(5))})");
        if (v.Hypervisors.Count > 0) present.Add($"produse de virtualizare instalate: {string.Join(", ", v.Hypervisors.Take(5))}");
        if (v.ContainerEngines.Count > 0) present.Add($"motoare de containere: {string.Join(", ", v.ContainerEngines.Take(5))}");
        if (v.Containers.Count > 0) present.Add($"{LogAnalyzer.Dfir.Language.RoText.Count(v.Containers.Count, "container Windows", "containere Windows")} ({string.Join(", ", v.Containers.Take(5))})");
        if (v.VirtualNetworks.Count > 0) present.Add($"rețele virtuale în interiorul stației: {string.Join("; ", v.VirtualNetworks.Take(5))}");
        if (v.BridgeDrivers.Count > 0) present.Add($"drivere de punte care pot lega o mașină virtuală la rețeaua fizică a stației: {string.Join(", ", v.BridgeDrivers.Take(5))}");
        if (present.Count > 0)
            o.Add(Chan(c, "AIRGAP-VM-PRESENT", "Virtualizare sau al doilea sistem de operare pe un sistem izolat", Sev(c, Severity.Medium), "NETWORK INTERFACE", "Hyper-V / WSL / VirtualBox / VMware / containere",
                string.Join("; ", present), c.Facts!.CollectedUtc, null, "necunoscut: inventarul nu conține contul", "mașini virtuale / WSL", "necunoscută: o mașină virtuală poate avea propria rețea și foldere partajate cu stația", "mașină virtuală",
                $"Pe sistemul {ScopeWord(c)}: {string.Join("; ", present)}. O mașină virtuală sau o distribuție WSL este un al doilea sistem care rulează în interiorul stației, cu propriul jurnal, propriul antivirus și, eventual, propria rețea sau foldere partajate; controalele și jurnalele stației nu îl acoperă. Prezența nu arată că a fost pornit sau folosit.",
                "Mașini Hyper-V, distribuții WSL (registrul Lxss), produse de virtualizare instalate (VirtualBox, VMware), motoare de containere (Docker, Podman) și containere Windows, comutatoare virtuale Hyper-V, adaptoare virtuale ale gazdei și drivere de punte, citite de pe stație.",
                ["Mașina virtuală poate servi unei sarcini aprobate (testare, mediu de lucru izolat) și figura în documentația stației.", "WSL și Hyper-V sunt caracteristici Windows pe care un administrator le poate fi activat pentru unelte de dezvoltare.", "Un produs de virtualizare poate fi rămas instalat fără mașini create."],
                [.. v.HyperVMachines.Take(5).Select(m => FactRef(c, $"virtual/hyperv/{m}", $"mașină virtuală {m}")), .. v.WslDistributions.Take(5).Select(m => FactRef(c, $"virtual/wsl/{m}", $"distribuție WSL {m}")), .. v.Hypervisors.Take(5).Select(m => FactRef(c, $"virtual/product/{m}", $"produs {m}")),
                    .. v.ContainerEngines.Take(5).Select(m => FactRef(c, $"virtual/container-engine/{m}", m)), .. v.Containers.Take(5).Select(m => FactRef(c, $"virtual/container/{m}", $"container {m}")),
                    .. v.VirtualNetworks.Take(5).Select(m => FactRef(c, $"virtual/network/{m}", m)), .. v.BridgeDrivers.Take(5).Select(m => FactRef(c, $"virtual/bridge/{m}", m))],
                present, "", SemanticType.Presence));
        var images = v.DiskImages.OrderByDescending(d => d.Size).ToList();
        if (images.Count > 0)
        {
            var total = images.Sum(d => d.Size);
            o.Add(Chan(c, "AIRGAP-VM-DISK-IMAGE", $"Imagini de disc virtual pe un sistem izolat: {images.Count}", Sev(c, Severity.Medium), "NETWORK INTERFACE", "Fișiere VHD / VHDX / VMDK / VDI",
                $"{images.Count} fișiere de disc virtual ({total / 1_000_000_000.0:0.#} GB)", images.Select(d => d.LastWriteUtc).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).Cast<DateTimeOffset?>().FirstOrDefault(), null,
                "necunoscut: fișierele nu conțin contul", "imagini de disc virtual", "necunoscută: un disc virtual poate fi copiat ca un singur fișier mare", "fișiere locale",
                $"Pe discurile fixe ale sistemului {ScopeWord(c)} au fost găsite {LogAnalyzer.Dfir.Language.RoText.Count(images.Count, "fișier de disc virtual", "fișiere de disc virtual")} ({string.Join(", ", images.Take(5).Select(d => WinPath.GetFileName(d.Path) + " " + (d.Size / 1_000_000.0).ToString("0", CultureInfo.InvariantCulture) + " MB"))}" +
                $"{(images.Count > 5 ? " …" : "")}){(v.DiskImageSearchTruncated ? "; căutarea a fost limitată (foldere obișnuite, adâncime și număr limitate), deci pot exista și altele" : "")}. Un disc virtual poate conține un sistem întreg sau un volum cu fișiere și poate fi mutat pe un stick ca un singur fișier; fișierul nu arată ce conține.",
                "Fișiere cu extensiile .vhd, .vhdx, .vmdk, .vdi, .qcow2, .avhdx în folderele obișnuite ale discurilor fixe (căutare limitată).",
                ["Un disc virtual poate fi copia de siguranță a stației sau o imagine de instalare aprobată.", "Windows creează fișiere .vhdx pentru unele caracteristici (WSL, Windows Sandbox, Hyper-V)."],
                images.Take(10).Select(d => FactRef(c, $"virtual/diskimage/{WinPath.GetFileName(d.Path)}", $"disc virtual {WinPath.GetFileName(d.Path)}, {d.Size} octeți")).ToList(),
                images.Take(5).Select(d => $"airgap_facts virtual/diskimage/{WinPath.GetFileName(d.Path)}").ToList(), "", SemanticType.Presence));
        }
        if (v.BootOsEntries is > 1)
            o.Add(Chan(c, "AIRGAP-MULTIBOOT", $"Mai multe sisteme de operare la pornire: {v.BootOsEntries}", Sev(c, Severity.High), "NETWORK INTERFACE", "BCD (bcdedit /enum all /v)",
                $"{v.BootOsEntries} intrări de pornire ({string.Join(", ", v.BootEntryNames.Take(5))})", c.Facts!.CollectedUtc, null, "necunoscut: configurația de pornire nu conține contul", "configurația de pornire (BCD)", "necunoscută: al doilea sistem nu este vizibil din primul", "al doilea sistem de operare",
                $"Configurația de pornire a sistemului {ScopeWord(c)} conține {v.BootOsEntries} intrări de sistem de operare: {string.Join("; ", v.BootEntryNames.Take(6))}. Dacă stația pornește în alt sistem, nimic din controalele, jurnalele și securitatea primului sistem nu mai este activ, iar discurile pot fi citite și copiate liber (inclusiv, dacă nu sunt cifrate, volumele celuilalt sistem).",
                "Mai mult de o intrare de încărcător de sistem (recunoscută după elementele osdevice / path, nu după titlul localizat al secțiunii) în ieșirea bcdedit /enum all /v; intrarea Windows Recovery Environment legată prin recoverysequence nu se numără.",
                ["O a doua intrare poate fi un mediu de recuperare Windows sau o versiune anterioară păstrată după o actualizare majoră.", "Stația poate fi o stație de dezvoltare cu două sisteme aprobate."],
                [FactRef(c, "virtual/bcd", "intrări de pornire BCD")], v.BootEntryNames.Take(5).ToList(), "", SemanticType.Configuration));
    }

    // ---- D13: archiver histories ----

    private static readonly Regex DriveRx = new(@"^\s*""?(?<d>[A-Za-z]):[\\/]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static void Archivers(Ctx c, List<Finding> o)
    {
        var f = c.Facts;
        if (f is null || f.Archivers.Count == 0) return;
        var removable = new HashSet<string>(f.RemovableLetters.Select(l => l.Trim().TrimEnd(':').ToUpperInvariant()).Where(l => l.Length == 1), StringComparer.OrdinalIgnoreCase);
        foreach (var m in ObservedUsb(c.Events)) if (m.Letter.Length > 0) removable.Add(m.Letter.ToUpperInvariant());
        if (removable.Count == 0) return;
        var hits = f.Archivers.Select(a => (A: a, M: DriveRx.Match(a.Value))).Where(x => x.M.Success && removable.Contains(x.M.Groups["d"].Value.ToUpperInvariant())).ToList();
        foreach (var g in hits.GroupBy(x => x.M.Groups["d"].Value.ToUpperInvariant()).OrderBy(g => g.Key))
        {
            var items = g.Select(x => x.A).ToList();
            var tools = items.Select(a => a.Tool).Distinct().ToList();
            o.Add(Chan(c, "MEDIA-ARCHIVER-REMOVABLE", $"Arhivator cu căi pe unitatea amovibilă {g.Key}:", Sev(c, Severity.Medium), "PORTABLE STORAGE", $"{string.Join(" / ", tools)} (istoric din registru)",
                $"{LogAnalyzer.Dfir.Language.RoText.Count(items.Count, "cale", "căi")} pe unitatea {g.Key}: în istoricul {string.Join(" / ", tools)}", null, null, "necunoscut: istoricul din registru nu are cont pe intrare (este al utilizatorului al cărui profil a fost citit)",
                $"arhive pe unitatea {g.Key}:", "posibilă scriere pe mediu (arhiva poate fi creată sau doar deschisă)", $"suport de stocare amovibil {g.Key}:",
                $"Istoricul {string.Join(" / ", tools)} conține {LogAnalyzer.Dfir.Language.RoText.Count(items.Count, "cale", "căi")} pe unitatea {g.Key}:, care este o unitate amovibilă pe această stație: {string.Join("; ", items.Take(6).Select(a => $"{a.List}: {Shorten(a.Value)}"))}{(items.Count > 6 ? " …" : "")}. " +
                "O arhivă (zip, rar, 7z) este felul obișnuit de a strânge multe fișiere într-unul singur înainte de a le muta pe un mediu. Istoricul arată că arhivatorul a lucrat cu acea cale; nu arată dacă arhiva a fost creată sau doar deschisă, ce conținea sau când (registrul nu păstrează ora fiecărei intrări).",
                "Intrări din istoricul WinRAR (ArcHistory, DialogEditHistory) sau 7-Zip (File Manager: FolderHistory, CopyHistory, PanelPath) cu litera unei unități amovibile.",
                ["Arhiva poate fi dintr-un mediu primit pe cale aprobată, deschis pentru citire.", "Litera unității amovibile poate fi reatribuită alt volum (MountedDevices păstrează ultima asociere).", "Istoricul poate data de dinainte ca stația să fie clasificată."],
                items.Take(10).Select(a => FactRef(c, $"archivers/{a.Tool}/{a.List}", $"{a.Tool} {a.List}: {Shorten(a.Value)}")).ToList(), items.Take(5).Select(a => $"airgap_facts archivers/{a.Tool}/{a.List}").ToList(), "", SemanticType.Presence));
        }
    }

    // ---- D14: per-application upload outliers (SRUM) ----

    private static void UploadOutliers(Ctx c, List<Finding> o)
    {
        var l = c.Data.Channels;
        var rows = c.Events.Where(e => e.Source == "SRUM" && T(e) is not null).Select(e => (E: e, App: e.Path, Day: DateOnly.FromDateTime(T(e)!.Value.UtcDateTime), Sent: long.TryParse(F(e, "BytesSent"), out var b) ? b : 0)).ToList();
        if (rows.Count == 0) return;
        var found = new List<(string App, DateOnly Day, long Bytes, double Median, int Days, List<TimelineEvent> Events)>();
        foreach (var app in rows.GroupBy(r => r.App, StringComparer.OrdinalIgnoreCase))
        {
            var perDay = app.GroupBy(r => r.Day).Select(d => (Day: d.Key, Bytes: d.Sum(x => x.Sent), Events: d.Select(x => x.E).ToList())).OrderBy(d => d.Day).ToList();
            var sorted = perDay.Select(d => (double)d.Bytes).OrderBy(x => x).ToList();
            double median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            foreach (var d in perDay)
            {
                if (d.Bytes < l.SrumUploadBytesPerDay) continue;
                bool enoughHistory = perDay.Count >= l.SrumMinDays;
                // with a short history the median says nothing, so only a clearly larger volume is reported (five times the threshold)
                if (enoughHistory ? d.Bytes <= median * l.SrumMedianFactor : d.Bytes < l.SrumUploadBytesPerDay * 5) continue;
                if (!(c.Scope.PeriodFromUtc is null && c.Scope.PeriodToUtc is null || c.InPeriod(new DateTimeOffset(d.Day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)) || c.InPeriod(new DateTimeOffset(d.Day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)))) continue;
                found.Add((app.Key, d.Day, d.Bytes, median, perDay.Count, d.Events));
            }
        }
        var top = found.OrderByDescending(x => x.Bytes).Take(l.SrumMaxFindings).ToList();
        foreach (var x in top)
        {
            var exe = WinPath.GetFileName(x.App.TrimEnd('\\'));
            var hist = x.Days >= l.SrumMinDays ? $"mediana zilelor aceleiași aplicații este {x.Median / 1_000_000.0:0.#} MB ({x.Days} zile de istoric)" : $"aplicația are doar {x.Days} zile de istoric în SRUM, deci o medie proprie nu poate fi calculată";
            var times = x.Events.Select(T).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
            o.Add(Chan(c, "AIRGAP-UPLOAD-OUTLIER", $"{exe}: {x.Bytes / 1_000_000.0:0.#} MB trimiși într-o zi ({x.Day:yyyy-MM-dd})", Sev(c, Severity.Medium), "NETWORK INTERFACE", "SRUM Network Data Usage",
                $"{x.Bytes / 1_000_000.0:0.#} MB trimiși de {exe} în ziua {x.Day:yyyy-MM-dd}", times.Count > 0 ? times[0] : null, times.Count > 0 ? times[^1] : null, WhoOf(x.Events), $"aplicația {exe}", "trimitere de date din stație către o rețea (SRUM nu arată destinația)", "necunoscută: SRUM nu păstrează adresele",
                $"{exe} a trimis {x.Bytes:N0} octeți ({x.Bytes / 1_000_000.0:0.#} MB) în ziua {x.Day:yyyy-MM-dd} (UTC), peste pragul de {l.SrumUploadBytesPerDay / 1_000_000} MB pe zi; {hist}. " +
                "SRUM numără octeții pe aplicație și pe interval de aproximativ o oră, pe toate interfețele (inclusiv rețeaua locală și bucla locală); nu arată destinația, protocolul sau conținutul.",
                "Octeții trimiși de o aplicație într-o zi, din SRUM, peste pragul din fișierul de date și peste mediana zilelor aceleiași aplicații înmulțită cu factorul din fișierul de date.",
                ["Actualizări Windows, antivirus sau copii de siguranță trimit volume mari legitim, mai ales în prima zi după o instalare.", "Sincronizarea unui folder cu un server aprobat din zona stației produce același volum.", "Volumul poate fi trafic pe o rețea locală aprobată, nu un trafic către exterior."],
                x.Events.OrderByDescending(e => long.TryParse(F(e, "BytesSent"), out var b) ? b : 0).Take(10).Select(e => Ref(e, $"SRUM {F(e, "BytesSent")} octeți trimiși")).ToList(), x.Events.Take(5).Select(e => $"{e.Source} {e.Locator}".Trim()).ToList(), "", SemanticType.Observation));
        }
        if (found.Count > top.Count)
            o.Add(Chan(c, "AIRGAP-UPLOAD-OUTLIER", $"Alte {found.Count - top.Count} zile cu volum mare de date trimise (nu au fost detaliate)", Sev(c, Severity.Low), "NETWORK INTERFACE", "SRUM Network Data Usage",
                $"{found.Count - top.Count} cazuri suplimentare peste prag", null, null, "necunoscut", "aplicații diverse", "necunoscută", "necunoscută",
                $"Numărul de cazuri (aplicație și zi) peste prag a depășit limita de {l.SrumMaxFindings} constatări detaliate; rămân {found.Count - top.Count} cazuri nedetaliate, cu volume mai mici decât cele raportate. Cele mai mici nu sunt neimportante: refaceți analiza cu un prag mai mare sau examinați SRUM direct.",
                "Limita de constatări a regulii a fost atinsă.", ["Mai multe aplicații legitime pot depăși pragul în aceeași zi (actualizări)."],
                found.Except(top).Take(10).SelectMany(x => x.Events.Take(1)).Select(e => Ref(e, "SRUM")).ToList(), [], "", SemanticType.Observation));
    }

}
