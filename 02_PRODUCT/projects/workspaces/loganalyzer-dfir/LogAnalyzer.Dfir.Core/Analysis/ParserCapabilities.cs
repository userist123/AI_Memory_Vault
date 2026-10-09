namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// What a parser's output can and cannot establish (lessons learned §2). Data only: it is attached to the Limitations of
/// every finding that rests on that parser's rows, so "Amcache" can never read as "executed".
/// </summary>
public sealed record ParserCapabilities(
    string ParserId, IReadOnlyList<string> CanProve, IReadOnlyList<string> CannotProve,
    IReadOnlyList<string> Limitations, IReadOnlyList<string> CorrelationSources)
{
    /// <summary>
    /// Per-source detail for parsers that read many sources with one code path (EVTX: one entry per channel and event-id group). Additive:
    /// empty for parsers that read a single artifact. WP11 fills it for every channel the new rules read.
    /// </summary>
    public IReadOnlyList<ChannelCapability> Channels { get; init; } = [];

    public ChannelCapability? ChannelFor(string channel, int eventId) =>
        Channels.FirstOrDefault(c => c.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase) && c.EventIds.Contains(eventId));

    private static ParserCapabilities C(string id, string[] can, string[] cannot, string[] limits, string[] corr) => new(id, can, cannot, limits, corr);

    private static ChannelCapability Ch(string channel, int[] ids, string[] can, string[] cannot, string[] corr) => new(channel, ids, can, cannot, corr);

    /// <summary>What each EVTX channel/event group read by the WP11 rules can and cannot establish.</summary>
    private static readonly ChannelCapability[] EvtxChannels =
    [
        Ch("Security", [5140, 5145],
            ["Că un cont a accesat o partajare (5140) sau un obiect din ea (5145), cu adresa sursă și numele obiectului."],
            ["Ce s-a citit sau scris efectiv (5145 arată cererea de acces și masca, nu conținutul).", "Că accesul a reușit în toate cazurile (5145 se scrie pe fiecare cerere)."],
            ["4624 tip 3 (autentificarea), jurnalele stației sursă, System 7045 (serviciu creat după copierea unui executabil)."]),
        Ch("Security", [4670],
            ["Că permisiunile unui obiect au fost schimbate, cu descriptorul vechi și cel nou."],
            ["De ce a fost schimbat (moștenirea, instalatoarele și migrările produc același eveniment).", "Starea actuală a permisiunilor."],
            ["4656/4663 (acces la obiect), 5145, starea actuală (icacls / Get-Acl)."]),
        Ch("Security", [4720, 4722, 4724, 4738],
            ["Că un cont a fost creat (4720), activat (4722), i s-a resetat parola (4724) sau a fost modificat (4738), cu subiectul care a făcut-o."],
            ["Că contul a fost folosit după creare.", "Cine a cerut contul sau dacă modificarea a fost aprobată."],
            ["4624/4648 pentru folosire, 4728/4732/4756 pentru apartenență, controlerul de domeniu pentru conturile de domeniu."]),
        Ch("Security", [4728, 4732, 4756],
            ["Că un cont a fost adăugat într-un grup global/local/universal, cu SID-ul grupului."],
            ["Că apartenența a fost folosită.", "Cine a aprobat adăugarea."],
            ["4624 / 4672 (privilegii speciale la autentificare), jurnalul controlerului de domeniu."]),
        Ch("Security", [4624, 4648],
            ["O autentificare reușită (4624, tip și adresă sursă) sau folosirea explicită a credențialelor (4648)."],
            ["Activitatea din sesiune.", "Persoana din spatele contului.", "Adresa reală dacă există un intermediar."],
            ["TerminalServices, WinRM, 4688 în aceeași sesiune (LogonId), jurnale firewall."]),
        Ch("Security", [4688],
            ["Că un proces a fost creat, cu imaginea, linia de comandă (dacă auditarea liniei de comandă e activă) și LogonId."],
            ["Că procesul a terminat cu succes.", "Linia de comandă dacă auditarea ei nu e activă."],
            ["Sysmon 1, Prefetch, BAM, PowerShell 4104."]),
        Ch("System", [7036, 7040, 7045],
            ["Că un serviciu și-a schimbat starea (7036), tipul de pornire (7040) sau a fost instalat (7045), cu nume și cale."],
            ["Cine a făcut schimbarea.", "Că binarul serviciului este cel legitim.", "Textul stărilor este localizat: lista cuvintelor este în date."],
            ["Security 4697/4688, Prefetch pentru binar, Sysmon 1/4."]),
        Ch("System", [1074, 6006],
            ["Că sistemul a pornit o oprire/repornire (1074) sau că serviciul de jurnal a fost oprit curat (6006)."],
            ["Motivul opririi sau cine a cerut-o dincolo de câmpurile evenimentului."],
            ["System 6005/6008, Kernel-Power 41."]),
        Ch("System", [25, 33],
            ["Că volsnap a abandonat copiile (25) sau a șters cea mai veche copie pentru limita de spațiu (33)."],
            ["Că cineva a șters copiile intenționat (cauza este limita de spațiu sau o eroare de I/O)."],
            ["4688 / Sysmon 1 / PowerShell 4104 cu comenzi de ștergere, VSS 8193/8194."]),
        Ch("Application", [1033, 1034, 11707, 11724],
            ["Că MsiInstaller a instalat (11707/1033) sau a eliminat (11724/1034) un produs, cu numele produsului în mesaj."],
            ["Cine a instalat sau a eliminat produsul.", "Că produsul rula sau a rulat."],
            ["Amcache, System 7045 pentru servicii, Prefetch."]),
        Ch("Application", [8193, 8194],
            ["Că VSS a raportat o eroare."],
            ["Că a avut loc o ștergere de copii shadow (sunt erori, frecvente în rulările de backup)."],
            ["System volsnap 25/33, comenzi de ștergere în 4688 / Sysmon 1."]),
        Ch("Microsoft-Windows-Sysmon/Operational", [1, 4, 16, 22],
            ["Proces creat cu linie de comandă și hash-uri (1), starea serviciului Sysmon (4), schimbarea configurației (16), interogare DNS cu imaginea procesului (22)."],
            ["Ce s-a întâmplat în afara configurației active a Sysmon (filtrele pot omite evenimente).", "Că interogarea DNS a fost urmată de o conexiune."],
            ["Security 4688, DNS-Client 3006/3008, firewall 5156."]),
        Ch("Microsoft-Windows-WinRM/Operational", [6, 91, 142],
            ["Că o sesiune WSMan a fost creată (6 = pornită de această stație, 91 = shell creat pe această stație) sau o operație a eșuat (142)."],
            ["Comenzile rulate în sesiune.", "Identitatea sursei (câmpurile variază cu versiunea).", "Canalul poate lipsi pe unele sisteme."],
            ["Security 4624 tip 3, 4688 wsmprovhost.exe, PowerShell 4104."]),
        Ch("OpenSSH/Operational", [4],
            ["Că sshd a înregistrat un mesaj, inclusiv autentificări acceptate sau eșuate, cu utilizator și adresă (în text)."],
            ["Comenzile rulate în sesiune.", "Textul liber al mesajului depinde de versiunea OpenSSH."],
            ["Security 4624, 4688 pentru procesele copil ale sshd, authorized_keys."]),
        Ch("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", [21, 22, 25],
            ["Că o sesiune RDP s-a autentificat (21), a pornit shell-ul (22) sau s-a reconectat (25), cu adresa sursă."],
            ["Activitatea din sesiune.", "Adresa reală dacă există un gateway sau tunel."],
            ["Security 4624 tip 10, RemoteConnectionManager 1149."]),
        Ch("Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational", [1149],
            ["Că autentificarea la nivel de rețea RDP a reușit pentru un utilizator, cu adresa sursă."],
            ["Că s-a creat o sesiune interactivă (1149 precede autentificarea completă)."],
            ["Security 4624 tip 10, LocalSessionManager 21."]),
        Ch("Microsoft-Windows-DNS-Client/Operational", [3006, 3008, 3020],
            ["Că clientul DNS a interogat (3006), a primit răspuns (3008) sau a raportat detalii (3020) pentru un nume."],
            ["Procesul care a interogat (se deduce doar din PID, nesigur).", "Că a urmat o conexiune."],
            ["Sysmon 22 (cu imaginea), 4688 / Sysmon 1 pentru PID, firewall 5156."]),
        Ch("Microsoft-Windows-NetworkProfile/Operational", [10000, 10001, 4004],
            ["Că o rețea a fost conectată sau deconectată."],
            ["Serverele DNS sau schimbarea lor (evenimentul nu le conține)."],
            ["Chei NameServer din registru, DHCP-Client."]),
        Ch("Microsoft-Windows-PowerShell/Operational", [4104],
            ["Textul unui bloc de script PowerShell executat (dacă Script Block Logging e activ)."],
            ["Rezultatul execuției.", "Blocuri dezactivate prin politică."],
            ["Security 4688, Sysmon 1."]),
    ];

    public static IReadOnlyList<ParserCapabilities> All { get; } =
    [
        C("EvtxParser",
            ["Că Windows a înregistrat evenimentul, cu ora și câmpurile lui."],
            ["Că jurnalul e complet (înregistrări șterse sau suprascrise nu apar).", "Intenția sau identitatea reală din spatele unui cont."],
            ["Depinde de politica de audit activă la momentul evenimentului.", "Jurnalele circulare pierd evenimentele vechi."],
            ["Prefetch, SRUM, Amcache pentru aceeași activitate."]) with { Channels = EvtxChannels },
        C("PrefetchParser",
            ["Că executabilul a fost pornit (număr de rulări, ultimele rulări, fișiere referite în primele secunde)."],
            ["Cine a pornit programul.", "Că scripturile sau fișierele referite au fost executate sau citite integral.", "Că programul mai există pe disc."],
            ["Prefetch poate fi dezactivat (servere) sau golit; păstrează doar ultimele 8 rulări.", "Ora ultimei rulări e a procesului, nu a unei acțiuni a utilizatorului."],
            ["BAM, UserAssist, SRUM, EVTX 4688."]),
        C("AmcacheParser",
            ["Că o intrare pentru fișier a fost scrisă în Amcache (prezență/instalare, cu hash SHA-1 și editor)."],
            ["Că fișierul a fost executat.", "Ce utilizator l-a rulat sau când a rulat."],
            ["Ora intrării e ora scrierii în Amcache, nu a unei rulări.", "Hive-ul poate lipsi sau fi parțial actualizat."],
            ["Prefetch, BAM, EVTX 4688 pentru execuție."]),
        C("SystemHiveExecutionParser",
            ["BAM: ultima activitate de execuție per utilizator și cale.", "ShimCache: că o cale a fost văzută de subsistemul de compatibilitate."],
            ["ShimCache: că fișierul a fost executat (ora este a ultimei modificări a fișierului).", "BAM: rulări anterioare ultimei."],
            ["BAM păstrează o singură oră per cale și se curăță la repornire în anumite versiuni.", "ShimCache e scris în memorie și ajunge în hive la oprire."],
            ["Prefetch, Amcache, EVTX 4688."]),
        C("UserHiveParser",
            ["UserAssist: lansări din Explorer/meniul Start (GUI) de către acel utilizator."],
            ["Rulări din linia de comandă, servicii sau alte metode de pornire."],
            ["Numele sunt codate ROT13; contoarele pot fi resetate de utilizator."],
            ["Prefetch, BAM."]),
        C("SoftwareHiveParser",
            ["Ce configurație de pornire automată (Run/RunOnce, Winlogon) există în hive."],
            ["Că intrarea a rulat sau că a fost creată de un atacator.", "Cine a scris valoarea."],
            ["Ora este LastWriteTime al cheii, nu al valorii."],
            ["Prefetch, EVTX 7045/4697, TaskScheduler."]),
        C("ServicesParser",
            ["Ce servicii/drivere sunt configurate în hive-ul SYSTEM (cale, mod de pornire, cont)."],
            ["Că serviciul a pornit vreodată.", "Când a fost instalat (ora e a oricărei modificări a cheii)."],
            ["Un ControlSet inactiv poate diferi de cel curent."],
            ["EVTX System 7045/7036, Prefetch."]),
        C("UsbDevicesParser",
            ["Că un dispozitiv de stocare USB a fost recunoscut de sistem (identitate, ore de conectare/deconectare unde există)."],
            ["Că s-au copiat sau citit fișiere de pe/pe dispozitiv.", "Ce utilizator l-a folosit, dacă lipsesc alte surse."],
            ["MountedDevices păstrează identitatea, nu momentul."],
            ["LNK, JumpList, EVTX (Kernel-PnP), ShellBags."]),
        C("LnkParser",
            ["Că există un shortcut către o țintă, cu metadatele țintei la crearea/modificarea lui."],
            ["Că ținta a fost deschisă de utilizator.", "Că ținta mai există."],
            ["Datele sunt cele din antetul LNK, nu momentul deschiderii."],
            ["JumpList, Prefetch, Amcache."]),
        C("JumpListParser",
            ["Că o aplicație a înregistrat un element în lista sa recentă (cu ultima utilizare)."],
            ["Că documentul a fost deschis în mod demonstrat de utilizator.", "Conținutul elementului."],
            ["Înregistrările pot fi șterse de utilizator sau de curățare."],
            ["LNK, Prefetch."]),
        C("UsnJournalParser",
            ["Că s-a înregistrat o modificare NTFS (creare, ștergere, redenumire) pentru un nume de fișier."],
            ["Cine a făcut modificarea.", "Conținutul fișierului."],
            ["Jurnalul e circular; modificările vechi dispar.", "Exportul fsutil convertește ora locală în UTC cu fusul cazului."],
            ["MFT, Prefetch, EVTX."]),
        C("ScheduledTaskParser",
            ["Ce configurație are un task programat (acțiune, declanșatori, cont, ascuns/activ)."],
            ["Că taskul a rulat.", "Cine l-a creat (autor este scris de creator)."],
            ["Data de înregistrare e furnizată de autor și poate fi falsificată."],
            ["EVTX TaskScheduler 106/200/201, Security 4698, Prefetch."]),
        C("SrumNetworkParser",
            ["Că o aplicație a folosit rețeaua într-un interval orar, cu volume de octeți."],
            ["Destinația traficului.", "Conținutul transferului.", "Intenția."],
            ["Agregat orar (~1h), nu eveniment individual."],
            ["Prefetch, firewall, PCAPNG."]),
        C("PcapngParser",
            ["Ce pachete/fluxuri au fost capturate în interval, pe interfața capturată."],
            ["Ce s-a întâmplat în afara capturii sau pe alte interfețe.", "Conținutul traficului criptat."],
            ["Captura poate fi trunchiată sau filtrată."],
            ["Jurnale firewall, SRUM, DNS."]),
        C("BrowserHistoryParser",
            ["Că browserul a înregistrat o vizită sau o descărcare (adresă, oră)."],
            ["Că utilizatorul a văzut sau a deschis fișierul descărcat.", "Că descărcarea a fost rulată."],
            ["Istoricul poate fi șters sau profilul poate lipsi."],
            ["Prefetch, BAM, Zone.Identifier."]),
        C("FirefoxHistoryParser",
            ["Că Firefox a înregistrat o vizită sau o descărcare (adresă, oră)."],
            ["Că utilizatorul a văzut sau a deschis fișierul descărcat.", "Că descărcarea a fost rulată."],
            ["Istoricul poate fi șters sau profilul poate lipsi."],
            ["Prefetch, BAM, Zone.Identifier."]),
    ];

    private static readonly Dictionary<string, ParserCapabilities> ById = All.ToDictionary(c => c.ParserId, StringComparer.Ordinal);

    public static ParserCapabilities? For(string parserId) => ById.GetValueOrDefault(parserId);
}

/// <summary>What one source inside a multi-source parser (an EVTX channel and a group of event ids) can and cannot establish.</summary>
public sealed record ChannelCapability(
    string Channel, IReadOnlyList<int> EventIds, IReadOnlyList<string> CanProve, IReadOnlyList<string> CannotProve, IReadOnlyList<string> CorrelationSources);
