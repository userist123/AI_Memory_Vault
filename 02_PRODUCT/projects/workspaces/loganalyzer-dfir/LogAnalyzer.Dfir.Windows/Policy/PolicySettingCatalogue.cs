using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Windows.Policy;

/// <summary>
/// One policy setting in plain words, for a controller who is not an IT specialist (owner request 2026-10-12: „și la politici la fel ca la restul,
/// ce face fiecare politică în parte"). <see cref="Name"/> = numele pe înțeles, <see cref="What"/> = ce face, <see cref="Why"/> = de ce contează
/// pe o stație izolată clasificată, <see cref="Recommended"/> = valoarea recomandată (or, where the organisation decides, the pointer to the
/// procedure), <see cref="Where"/> = where it is set in Group Policy, <see cref="ForIt"/> = the technical location for IT.
/// <see cref="Values"/> maps a value as the engine reads it to its meaning; the key "*" is a general note (unit, range) for any other value.
/// Meanings are documented Microsoft semantics; none of them asserts wrongdoing.
/// </summary>
public sealed record PolicySettingGuide(
    string Id, string Name, string What, string Why, string Recommended, string Where, string ForIt,
    IReadOnlyDictionary<string, string> Values, string AbsentMeaning);

/// <summary>The explanation panel of one plan row: the catalogue entry plus what the opened policy requires and what the current value means.</summary>
public sealed record PolicySettingExplanation(
    string Setting, string Name, string What, string Why, string Recommended, string Required, string CurrentMeaning, string Where, string ForIt,
    bool InCatalogue)
{
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Name} ({Setting})");
        sb.AppendLine("Ce face: " + What);
        sb.AppendLine("De ce contează pe o stație izolată: " + Why);
        sb.AppendLine("Valoarea recomandată: " + Recommended);
        sb.AppendLine(Required);
        sb.AppendLine("Ce înseamnă valoarea de acum: " + CurrentMeaning);
        sb.AppendLine("Unde se setează: " + Where);
        sb.Append("Pentru IT: " + ForIt);
        return sb.ToString();
    }
}

/// <summary>
/// The catalogue of the settings a policy plan can show: every account-policy key the engine reads (<see cref="PolicyImport.SecpolNames"/>),
/// every advanced-audit subcategory (<see cref="AuditSubcategories.ByName"/>), the known services and the registry values that GPOs commonly set
/// on an isolated station (security options, device installation, removable storage, AutoPlay, BitLocker, firewall, Defender, PowerShell, Recall).
/// A registry value or service not in the catalogue gets an honest generic explanation (<see cref="PolicySettingExplanation.InCatalogue"/> false),
/// never a guessed meaning.
/// </summary>
public static class PolicySettingCatalogue
{
    /// <summary>The words used wherever the organisation's procedure, not Microsoft, decides the value.</summary>
    public const string ProcedureValue = "valoarea cerută de procedura dumneavoastră (profilul de proceduri)";

    private const string NotAppliedHere = "Se folosește în rețele cu domeniu sau pe servere; pe o stație izolată, fără domeniu, de obicei nu produce evenimente.";
    private const string AdvancedAudit = @"Computer Configuration\Policies\Windows Settings\Security Settings\Advanced Audit Policy Configuration\Audit Policies";
    private const string SecurityOptions = @"Computer Configuration\Windows Settings\Security Settings\Local Policies\Security Options";
    private const string AdminTemplates = @"Computer Configuration\Administrative Templates";

    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    private static Dictionary<string, string> V(params (string Value, string Meaning)[] pairs) =>
        pairs.ToDictionary(p => p.Value, p => p.Meaning, StringComparer.OrdinalIgnoreCase);

    private static string Proc(string? reference = null) =>
        reference is null ? $"Nu există o valoare universală: se aplică {ProcedureValue}." : $"Se aplică {ProcedureValue}. Ca reper, {reference}";

    // ---- account policy (secpol) -----------------------------------------------------------------------------------------------------------

    private static PolicySettingGuide Secpol(string name, string display, string what, string why, string recommended, string group, string gpoName,
                                              Dictionary<string, string> values) =>
        new($"secpol:{name}", display, what, why, recommended,
            $"„{@"Computer Configuration\Windows Settings\Security Settings\Account Policies"}\\{group}\\{gpoName}”; pe stația fără domeniu, în „Politica de securitate locală” (secpol.msc), la aceeași cale.",
            $"Comanda „net accounts”; în șablonul de securitate (.inf), secțiunea [System Access], cheia {name}. Valoarea se păstrează în baza conturilor locale (SAM), nu într-o valoare de registru care se editează direct.",
            values, "");

    private static readonly PolicySettingGuide[] SecpolGuides =
    [
        Secpol("MinimumPasswordLength", "Lungimea minimă a parolei", "Stabilește câte caractere trebuie să aibă cel puțin o parolă nouă.",
            "Parolele scurte se ghicesc ușor de cine are acces la tastatura stației; pe o stație izolată parola este principala barieră.",
            Proc("linia de bază de securitate Microsoft cere cel puțin 14 caractere."), "Password Policy", "Minimum password length",
            V(("0", "parola poate lipsi cu totul"), ("*", "numărul este în caractere"))),
        Secpol("MaximumPasswordAge", "Durata maximă a parolei", "Stabilește după câte zile parola expiră și trebuie schimbată.",
            "Limitează cât timp poate fi folosită o parolă aflată de altcineva.",
            Proc(), "Password Policy", "Maximum password age",
            V(("-1", "parola nu expiră niciodată"), ("0", "parola nu expiră niciodată"), ("*", "numărul este în zile"))),
        Secpol("MinimumPasswordAge", "Durata minimă a parolei", "Stabilește câte zile trebuie să treacă până când parola poate fi schimbată din nou.",
            "Împiedică schimbarea rapidă, de mai multe ori la rând, ca utilizatorul să revină la parola veche.",
            Proc("linia de bază de securitate Microsoft cere 1 zi."), "Password Policy", "Minimum password age",
            V(("0", "parola se poate schimba imediat, oricât de des"), ("*", "numărul este în zile"))),
        Secpol("PasswordHistorySize", "Istoricul parolelor", "Stabilește câte parole vechi ține minte Windows, ca să nu poată fi refolosite.",
            "Fără istoric, utilizatorul poate reveni mereu la aceeași parolă, chiar după o compromitere.",
            Proc("linia de bază de securitate Microsoft cere 24 de parole ținute minte."), "Password Policy", "Enforce password history",
            V(("0", "parolele vechi pot fi refolosite"), ("*", "numărul este de parole ținute minte"))),
        Secpol("LockoutBadCount", "Pragul de blocare a contului", "Stabilește după câte parole greșite la rând se blochează contul.",
            "Fără blocare, cineva cu acces la stație poate încerca parole la nesfârșit.",
            Proc("linia de bază de securitate Microsoft cere blocarea după cel mult 10 încercări greșite."), "Account Lockout Policy", "Account lockout threshold",
            V(("0", "contul nu se blochează niciodată, oricâte parole greșite se încearcă"), ("*", "numărul este de încercări greșite"))),
        Secpol("LockoutDuration", "Durata blocării contului", "Stabilește câte minute rămâne blocat un cont după prea multe parole greșite.",
            "Încetinește ghicirea parolei; o blocare până la intervenția administratorului lasă și o urmă verificabilă.",
            Proc(), "Account Lockout Policy", "Account lockout duration",
            V(("-1", "contul rămâne blocat până îl deblochează un administrator"), ("0", "contul rămâne blocat până îl deblochează un administrator"), ("*", "numărul este în minute"))),
        Secpol("ResetLockoutCount", "Resetarea numărătorii de parole greșite", "Stabilește după câte minute fără încercări greșite numărătoarea se întoarce la zero.",
            "Stabilește fereastra de timp în care se adună încercările care duc la blocare.",
            Proc(), "Account Lockout Policy", "Reset account lockout counter after",
            V(("*", "numărul este în minute"))),
    ];

    // ---- advanced audit (audit) ------------------------------------------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string> AuditValues = V(
        ("No Auditing", "nu se înregistrează nimic din această categorie"),
        ("Success", "se înregistrează doar acțiunile reușite"),
        ("Failure", "se înregistrează doar încercările eșuate"),
        ("Success and Failure", "se înregistrează și reușitele, și încercările eșuate"));

    /// <summary>The value the Microsoft security baseline for Windows 11 sets, where it sets one.</summary>
    private static readonly Dictionary<string, string> AuditBaseline = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Credential Validation"] = "Success and Failure", ["Security Group Management"] = "Success", ["User Account Management"] = "Success and Failure",
        ["Plug and Play Events"] = "Success", ["Process Creation"] = "Success", ["Account Lockout"] = "Failure", ["Group Membership"] = "Success",
        ["Logoff"] = "Success", ["Logon"] = "Success and Failure", ["Other Logon/Logoff Events"] = "Success and Failure", ["Special Logon"] = "Success",
        ["Detailed File Share"] = "Failure", ["File Share"] = "Success and Failure", ["Other Object Access Events"] = "Success and Failure",
        ["Removable Storage"] = "Success and Failure", ["Audit Policy Change"] = "Success", ["Authentication Policy Change"] = "Success",
        ["MPSSVC Rule-Level Policy Change"] = "Success and Failure", ["Other Policy Change Events"] = "Failure", ["Sensitive Privilege Use"] = "Success and Failure",
        ["Other System Events"] = "Success and Failure", ["Security State Change"] = "Success", ["Security System Extension"] = "Success",
        ["System Integrity"] = "Success and Failure",
    };

    // Name as auditpol lists it -> (category in the Group Policy editor, name in the editor when it differs, what it records, why it matters here).
    private static readonly (string Name, string? Category, string? GpoName, string What, string Why)[] AuditRows =
    [
        ("Security State Change", "System", null, "pornirea și oprirea stației și schimbarea orei sistemului.", "Arată când a fost pornită stația și dacă cineva a dat ceasul înapoi ca să încurce cronologia."),
        ("Security System Extension", "System", null, "încărcarea de componente noi în sistemul de securitate Windows și instalarea de servicii noi.", "Un program rău-intenționat se poate instala ca serviciu ca să pornească odată cu stația."),
        ("System Integrity", "System", null, "problemele auditului însuși: evenimente pierdute, jurnal plin, componente de sistem cu semnătură invalidă.", "Arată dacă jurnalul de securitate a pierdut evenimente sau dacă au fost modificate componente protejate."),
        ("IPsec Driver", "System", null, "activitatea componentei IPsec, care criptează traficul de rețea.", "Pe o stație fără rețea de obicei nu produce evenimente; contează doar dacă stația folosește IPsec."),
        ("Other System Events", "System", null, "pornirea și oprirea firewall-ului Windows și operațiile cu fișierele cheilor de criptare.", "Oprirea firewall-ului sau probleme cu cheile de criptare pot fi semne de intervenție."),
        ("Logon", "Logon/Logoff", null, "fiecare intrare în cont pe stație (cine, când, cum: la tastatură sau de la distanță) și încercările eșuate.", "Este baza oricărui control: arată cine a lucrat pe stație și dacă cineva a încercat parole."),
        ("Logoff", "Logon/Logoff", null, "ieșirile din sesiune.", "Împreună cu intrările în cont arată cât a durat fiecare sesiune de lucru."),
        ("Account Lockout", "Logon/Logoff", null, "încercările de intrare respinse pentru că contul era blocat.", "Arată încercări repetate de ghicire a parolei."),
        ("IPsec Main Mode", "Logon/Logoff", null, "negocierea inițială a conexiunilor IPsec (criptarea traficului de rețea).", NotAppliedHere),
        ("IPsec Quick Mode", "Logon/Logoff", null, "negocierea rapidă a conexiunilor IPsec.", NotAppliedHere),
        ("IPsec Extended Mode", "Logon/Logoff", null, "negocierea extinsă a conexiunilor IPsec.", NotAppliedHere),
        ("Special Logon", "Logon/Logoff", null, "intrările în cont cu drepturi de administrator sau echivalente.", "Arată cine a lucrat cu drepturi depline pe stație."),
        ("Other Logon/Logoff Events", "Logon/Logoff", null, "blocarea și deblocarea ecranului, pornirea economizorului de ecran, reconectarea sesiunilor.", "Arată dacă stația a fost lăsată deschisă și cine a deblocat-o."),
        ("Network Policy Server", "Logon/Logoff", null, "cererile de acces procesate de un server de politici de rețea (NPS).", NotAppliedHere),
        ("User / Device Claims", "Logon/Logoff", null, "atributele utilizatorului și ale dispozitivului folosite de controlul dinamic al accesului din domeniu.", NotAppliedHere),
        ("Group Membership", "Logon/Logoff", null, "grupurile din care face parte utilizatorul la fiecare intrare în cont.", "Arată ce drepturi avea efectiv persoana în acea sesiune, de exemplu dacă era administrator."),
        ("Access Rights", null, null, "o categorie listată de comanda auditpol pe versiunile recente de Windows, pentru care Microsoft nu publică încă o descriere detaliată; aplicația nu îi presupune conținutul.", "Se configurează doar dacă procedura o cere explicit."),
        ("File System", "Object Access", null, "accesul la fișierele și folderele marcate special pentru audit (lista de audit a fiecărui folder).", "Arată cine a deschis, a modificat sau a șters documentele protejate; fără marcarea folderelor nu se înregistrează nimic."),
        ("Registry", "Object Access", null, "accesul la cheile de registru marcate special pentru audit.", "Arată schimbări în setările sensibile ale stației."),
        ("Kernel Object", "Object Access", null, "accesul la obiecte interne ale sistemului de operare.", "Produce multe evenimente cu valoare mică pentru un control; de obicei rămâne oprită."),
        ("SAM", "Object Access", null, "accesul la baza de date a conturilor locale (SAM).", "Poate arăta încercări de a citi lista conturilor."),
        ("Certification Services", "Object Access", null, "operațiile unui server de certificate (AD CS).", NotAppliedHere),
        ("Application Generated", "Object Access", null, "evenimentele de audit trimise de aplicații prin interfața de audit Windows.", "Depinde de aplicațiile instalate; puține aplicații o folosesc."),
        ("Handle Manipulation", "Object Access", null, "deschiderea și închiderea accesului la obiectele marcate pentru audit.", "Completează auditul fișierelor, dar produce foarte multe evenimente."),
        ("File Share", "Object Access", null, "accesul la folderele partajate în rețea.", "Pe o stație izolată nu ar trebui să existe acces din rețea; orice astfel de eveniment este semnificativ."),
        ("Filtering Platform Packet Drop", "Object Access", null, "pachetele de rețea blocate de platforma de filtrare Windows (firewall).", "Arată încercări de conectare blocate; produce mult volum."),
        ("Filtering Platform Connection", "Object Access", null, "conexiunile de rețea permise sau blocate de firewall.", "Pe o stație izolată orice conexiune de rețea este neașteptată."),
        ("Other Object Access Events", "Object Access", null, "crearea, modificarea și ștergerea activităților planificate și alte operații cu obiecte.", "O activitate planificată poate porni un program pe ascuns, la o anumită oră."),
        ("Detailed File Share", "Object Access", null, "fiecare fișier accesat printr-un folder partajat în rețea.", "Arată exact ce fișiere au fost citite sau scrise din rețea."),
        ("Removable Storage", "Object Access", null, "fiecare fișier citit sau scris pe stick-uri și discuri externe.", "Pe o stație izolată este principala cale de a scoate sau de a aduce date; fără acest audit copierile nu lasă urme."),
        ("Central Policy Staging", "Object Access", null, "diferențele dintre politica centrală de acces aplicată și una propusă (testare în domeniu).", NotAppliedHere),
        ("Sensitive Privilege Use", "Privilege Use", null, "folosirea drepturilor sensibile: preluarea fișierelor altcuiva, depanarea programelor, lucrul ca parte a sistemului.", "Aceste drepturi permit ocolirea protecțiilor; folosirea lor trebuie să lase urme."),
        ("Non Sensitive Privilege Use", "Privilege Use", null, "folosirea drepturilor obișnuite (de exemplu schimbarea fusului orar).", "Produce foarte multe evenimente cu valoare mică; de obicei rămâne oprită."),
        ("Other Privilege Use Events", "Privilege Use", null, "nimic în prezent: Microsoft o păstrează rezervată.", "Nu produce evenimente; valoarea ei nu schimbă ce se vede la control."),
        ("Process Creation", "Detailed Tracking", null, "fiecare program pornit pe stație, cu contul care l-a pornit.", "Arată ce programe au rulat; fără ea nu se poate reconstitui ce s-a făcut pe stație."),
        ("Process Termination", "Detailed Tracking", null, "oprirea programelor.", "Arată cât timp a rulat un program."),
        ("DPAPI Activity", "Detailed Tracking", null, "folosirea protecției de date Windows (DPAPI), care criptează parolele salvate și cheile.", "Poate arăta încercări de a extrage parole salvate."),
        ("RPC Events", "Detailed Tracking", null, "conexiunile de apel la distanță (RPC) primite de stație.", "Pe o stație izolată conexiunile din afară nu ar trebui să existe."),
        ("Plug and Play Events", "Detailed Tracking", "Audit PNP Activity", "conectarea dispozitivelor externe (stick-uri, telefoane, discuri) și instalarea driverelor lor.", "Arată ce dispozitive au fost conectate și când, chiar dacă nu s-a copiat nimic."),
        ("Token Right Adjusted Events", "Detailed Tracking", "Audit Token Right Adjusted", "schimbările drepturilor unui program în timp ce rulează.", "Poate arăta un program care își mărește drepturile."),
        ("Audit Policy Change", "Policy Change", null, "schimbările politicii de audit înseși.", "Cine oprește auditul înainte de a face ceva își ascunde urmele; această schimbare trebuie să rămână vizibilă."),
        ("Authentication Policy Change", "Policy Change", null, "schimbările regulilor de autentificare, de exemplu drepturile de intrare acordate conturilor.", "Arată dacă cineva și-a acordat dreptul de a intra pe stație."),
        ("Authorization Policy Change", "Policy Change", null, "schimbările drepturilor de utilizator (de exemplu cine poate prelua fișiere) și ale permisiunilor.", "Arată dacă cineva și-a acordat drepturi suplimentare."),
        ("MPSSVC Rule-Level Policy Change", "Policy Change", null, "schimbările regulilor firewall-ului Windows.", "O regulă nouă poate deschide stația către rețea."),
        ("Filtering Platform Policy Change", "Policy Change", null, "schimbările platformei de filtrare Windows (filtre de rețea, IPsec).", "Arată schimbări ale filtrării traficului de rețea."),
        ("Other Policy Change Events", "Policy Change", null, "alte schimbări de politică: recuperarea datelor criptate, actualizarea politicii de grup locale, operații ale componentei de criptare.", "Eșecurile pot arăta probleme la aplicarea politicii de securitate."),
        ("User Account Management", "Account Management", null, "crearea, ștergerea, activarea, dezactivarea conturilor și schimbarea parolelor lor.", "Un cont nou sau reactivat pe o stație izolată trebuie să corespundă unei aprobări."),
        ("Computer Account Management", "Account Management", null, "schimbările conturilor de calculator din domeniu.", NotAppliedHere),
        ("Security Group Management", "Account Management", null, "schimbările în grupurile de securitate, de exemplu adăugarea cuiva în grupul administratorilor.", "Arată cine a primit drepturi de administrator și când."),
        ("Distribution Group Management", "Account Management", null, "schimbările grupurilor de e-mail din domeniu.", NotAppliedHere),
        ("Application Group Management", "Account Management", null, "schimbările grupurilor de aplicații (Authorization Manager).", "Puține aplicații le folosesc; de obicei nu produce evenimente."),
        ("Other Account Management Events", "Account Management", null, "citirea amprentei parolei unui cont și verificările politicii de parole cerute de programe.", "Poate arăta încercări de a citi date despre parole."),
        ("Directory Service Access", "DS Access", null, "accesul la obiectele serviciului de director (Active Directory).", NotAppliedHere),
        ("Directory Service Changes", "DS Access", null, "schimbările obiectelor din Active Directory.", NotAppliedHere),
        ("Directory Service Replication", "DS Access", null, "copierea datelor între serverele de domeniu.", NotAppliedHere),
        ("Detailed Directory Service Replication", "DS Access", null, "detaliile copierii datelor între serverele de domeniu.", NotAppliedHere),
        ("Credential Validation", "Account Logon", null, "verificarea parolelor la intrarea în cont, inclusiv încercările eșuate, pentru conturile locale ale stației.", "Arată încercări de ghicire a parolei."),
        ("Kerberos Service Ticket Operations", "Account Logon", null, "biletele de acces Kerberos emise de un server de domeniu.", NotAppliedHere),
        ("Other Account Logon Events", "Account Logon", null, "nimic în prezent: Microsoft o păstrează rezervată.", "Nu produce evenimente; valoarea ei nu schimbă ce se vede la control."),
        ("Kerberos Authentication Service", "Account Logon", null, "cererile de autentificare Kerberos primite de un server de domeniu.", NotAppliedHere),
    ];

    private static PolicySettingGuide Audit((string Name, string? Category, string? GpoName, string What, string Why) r)
    {
        var guid = AuditSubcategories.ByName[r.Name];
        var recommended = AuditBaseline.TryGetValue(r.Name, out var b)
            ? Proc($"linia de bază de securitate Microsoft pentru Windows 11 cere „{b}”.")
            : r.Why == NotAppliedHere ? $"De obicei „No Auditing” pe o stație fără domeniu; se aplică {ProcedureValue}." : Proc();
        var where = r.Category is null
            ? "Se setează cu comanda auditpol; locul din editorul de politici depinde de versiunea Windows."
            : $"„{AdvancedAudit}\\{r.Category}\\{r.GpoName ?? "Audit " + r.Name}”; pe stația fără domeniu, în „Politica de securitate locală” (secpol.msc), la „Advanced Audit Policy Configuration”.";
        return new($"audit:{r.Name}", $"Auditul „{r.Name}”", "Înregistrează în jurnalul de securitate " + r.What, r.Why, recommended, where,
            $"auditpol /get /subcategory:\"{r.Name}\" (identificator {{{guid.ToString().ToUpperInvariant()}}}); într-un backup GPO, fișierul audit.csv.",
            AuditValues, "");
    }

    // ---- services (service, and registry Services\name\Start) ------------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string> StartValues = V(
        ("0", "pornește odată cu nucleul sistemului (driver de pornire)"), ("1", "pornește la inițializarea sistemului (driver de sistem)"),
        ("2", "pornește automat la fiecare pornire a stației"), ("3", "pornește doar la cerere (manual)"), ("4", "este dezactivat: nu poate porni"));

    private const string AirGapDisabled = "Pe o stație izolată de obicei 4 (dezactivat); decide procedura dumneavoastră.";

    private static readonly (string Name, string Display, string What, string Why, string Recommended)[] ServiceRows =
    [
        ("bthserv", "Bluetooth Support Service", "Serviciul Bluetooth: permite conectarea fără fir a telefoanelor, căștilor și altor dispozitive.", "Prin Bluetooth se pot trimite fișiere fără cablu, ocolind controlul suporturilor; pe o stație izolată trebuie oprit.", AirGapDisabled),
        ("WlanSvc", "WLAN AutoConfig", "Serviciul Wi-Fi: găsește și conectează stația la rețele fără fir.", "Cu el pornit, stația se poate conecta la o rețea fără fir și nu mai este izolată.", AirGapDisabled),
        ("RemoteRegistry", "Remote Registry", "Permite citirea și modificarea registrului stației de pe alt calculator.", "Deschide stația către acces din rețea.", AirGapDisabled),
        ("TermService", "Remote Desktop Services", "Serviciul Remote Desktop: permite lucrul pe stație de la distanță.", "Pe o stație izolată nimeni nu ar trebui să lucreze de la distanță.", AirGapDisabled),
        ("WinRM", "Windows Remote Management (WS-Management)", "Serviciul de administrare la distanță: permite rularea de comenzi pe stație din rețea.", "Deschide stația către comenzi trimise din altă parte.", AirGapDisabled),
        ("Spooler", "Print Spooler", "Serviciul de imprimare: trimite documentele la imprimantă.", "Este necesar pentru imprimare, dar a avut vulnerabilități grave (PrintNightmare); oprirea lui oprește și imprimarea.", Proc()),
        ("USBSTOR", "USB Mass Storage (driver)", "Componenta Windows care citește stick-urile și discurile USB.", "Dezactivată, stick-urile USB nu mai funcționează pe stație.", Proc("4 blochează stick-urile; 3 le permite (implicit).")),
        ("SSDPSRV", "SSDP Discovery", "Descoperirea automată a dispozitivelor din rețea (de exemplu imprimante sau televizoare).", "Inutil pe o stație izolată; mărește suprafața de atac.", AirGapDisabled),
        ("upnphost", "UPnP Device Host", "Permite stației să se prezinte ca dispozitiv UPnP în rețea.", "Inutil pe o stație izolată.", AirGapDisabled),
        ("lfsvc", "Geolocation Service", "Serviciul de localizare geografică a stației.", "Poate dezvălui poziția stației.", AirGapDisabled),
        ("MapsBroker", "Downloaded Maps Manager", "Gestionează hărțile descărcate de aplicația Hărți.", "Inutil pe o stație izolată.", AirGapDisabled),
        ("XblGameSave", "Xbox Live Game Save", "Sincronizează salvările jocurilor Xbox.", "Inutil pe o stație de lucru.", AirGapDisabled),
        ("icssvc", "Windows Mobile Hotspot Service", "Hotspotul mobil Windows: partajează conexiunea stației cu alte dispozitive.", "Ar transforma stația într-un punct de acces la rețea.", AirGapDisabled),
        ("WMPNetworkSvc", "Windows Media Player Network Sharing Service", "Partajează în rețea muzica și filmele din Windows Media Player.", "Inutil; deschide stația către rețea.", AirGapDisabled),
        ("AppIDSvc", "Application Identity", "Serviciul care aplică lista albă de programe (AppLocker).", "Dacă este dezactivat, regulile care blochează programele neaprobate nu mai sunt aplicate.", Proc("Microsoft recomandă pornire automată (2) acolo unde se folosește AppLocker.")),
        ("mrxsmb10", "SMB 1.0 client (driver)", "Componenta client a versiunii vechi (1) a protocolului de partajare a fișierelor.", "SMBv1 are vulnerabilități cunoscute folosite de viruși (de exemplu WannaCry).", "4 (dezactivat); Microsoft recomandă dezactivarea SMBv1 peste tot."),
    ];

    private static PolicySettingGuide Service((string Name, string Display, string What, string Why, string Recommended) r)
    {
        var driver = r.Display.EndsWith("(driver)", StringComparison.Ordinal);
        var where = driver
            ? $"Componenta „{r.Display}” nu apare în lista de servicii; modul ei de pornire se setează prin registru sau prin preferințele de politică de grup (Registry)."
            : $"„Servicii” (services.msc) → „{r.Display}” → „Tip pornire”; prin politica de grup, în „{@"Computer Configuration\Windows Settings\Security Settings\System Services"}” sau în preferințele de politică de grup (Services).";
        return new($"service:{r.Name}", $"Serviciul „{r.Display}”", r.What, r.Why, r.Recommended, where,
            $@"HKLM\SYSTEM\CurrentControlSet\Services\{r.Name}\Start (2 = automat, 3 = manual, 4 = dezactivat).", StartValues,
            "Serviciul nu există pe această stație (nu este instalat), deci nu poate porni.");
    }

    // ---- registry values commonly set by GPOs on an isolated station ----------------------------------------------------------------------

    private static PolicySettingGuide Reg(string keyPart, string name, string display, string what, string why, string recommended, string where,
                                          string forIt, Dictionary<string, string> values, string absent = "Valoarea nu există: Windows folosește comportamentul său implicit.") =>
        new($@"registry:{keyPart}\{name}", display, what, why, recommended, where, forIt, values, absent);

    private static readonly PolicySettingGuide[] RegistryGuides =
    [
        Reg(@"Policies\System", "EnableLUA", "Controlul contului de utilizator (UAC)", "Pornește fereastra care cere confirmare înainte ca un program să primească drepturi de administrator.",
            "Cu UAC oprit, orice program pornit de un administrator are control total asupra stației, fără nicio întrebare.", "1 (activ).",
            $"„{SecurityOptions}\\User Account Control: Run all administrators in Admin Approval Mode”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA",
            V(("1", "UAC este activ"), ("0", "UAC este oprit"))),
        Reg(@"Policies\System", "ConsentPromptBehaviorAdmin", "Cum întreabă UAC un administrator", "Stabilește dacă și cum este întrebat un administrator înainte ca un program să primească drepturi depline.",
            "Fără întrebare, un program rău-intenționat primește drepturi depline pe neobservate.", Proc("linia de bază de securitate Microsoft cere 2 (confirmare pe ecranul securizat)."),
            $"„{SecurityOptions}\\User Account Control: Behavior of the elevation prompt for administrators in Admin Approval Mode”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\ConsentPromptBehaviorAdmin",
            V(("0", "drepturile se dau fără nicio întrebare"), ("1", "se cere parola, pe ecranul securizat"), ("2", "se cere confirmarea, pe ecranul securizat"),
              ("3", "se cere parola"), ("4", "se cere confirmarea"), ("5", "se cere confirmarea doar pentru programele din afara Windows (implicit)"))),
        Reg(@"Policies\System", "PromptOnSecureDesktop", "Întrebarea UAC pe ecranul securizat", "Afișează întrebarea UAC pe un ecran separat, pe care alte programe nu îl pot apăsa în locul utilizatorului.",
            "Pe ecranul obișnuit, un program rău-intenționat ar putea răspunde singur la întrebare.", "1 (pe ecranul securizat).",
            $"„{SecurityOptions}\\User Account Control: Switch to the secure desktop when prompting for elevation”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\PromptOnSecureDesktop",
            V(("1", "întrebarea apare pe ecranul securizat"), ("0", "întrebarea apare pe ecranul obișnuit"))),
        Reg(@"Policies\System", "FilterAdministratorToken", "UAC pentru contul Administrator încorporat", "Aplică întrebarea UAC și contului Administrator încorporat.",
            "Fără ea, contul Administrator încorporat rulează orice program cu drepturi depline.", "1 (activ).",
            $"„{SecurityOptions}\\User Account Control: Admin Approval Mode for the Built-in Administrator account”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\FilterAdministratorToken",
            V(("1", "contul Administrator încorporat este întrebat"), ("0", "contul Administrator încorporat nu este întrebat"))),
        Reg(@"Policies\System", "InactivityTimeoutSecs", "Blocarea ecranului după inactivitate", "Stabilește după câte secunde fără activitate se blochează automat ecranul.",
            "Un ecran deschis pe o stație nesupravegheată poate fi folosit de oricine trece pe lângă ea.", Proc("linia de bază de securitate Microsoft cere cel mult 900 de secunde (15 minute)."),
            $"„{SecurityOptions}\\Interactive logon: Machine inactivity limit”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\InactivityTimeoutSecs",
            V(("0", "ecranul nu se blochează automat"), ("*", "numărul este în secunde"))),
        Reg(@"Policies\System", "DisableCAD", "Ctrl+Alt+Del înainte de autentificare", "Stabilește dacă utilizatorul trebuie să apese Ctrl+Alt+Del înainte să-și introducă parola.",
            "Combinația Ctrl+Alt+Del garantează că parola se introduce în fereastra reală a Windows, nu într-una falsă.", "0 (Ctrl+Alt+Del este cerut).",
            $"„{SecurityOptions}\\Interactive logon: Do not require CTRL+ALT+DEL”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\DisableCAD",
            V(("0", "Ctrl+Alt+Del este cerut"), ("1", "Ctrl+Alt+Del nu este cerut"))),
        Reg(@"Policies\System", "DontDisplayLastUserName", "Ascunderea ultimului utilizator", "Stabilește dacă ecranul de autentificare afișează numele ultimului utilizator.",
            "Numele afișat îi spune unui străin ce cont să încerce.", Proc(),
            $"„{SecurityOptions}\\Interactive logon: Don't display last signed-in”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\DontDisplayLastUserName",
            V(("1", "numele nu este afișat"), ("0", "numele ultimului utilizator este afișat"))),
        Reg(@"Policies\System", "LegalNoticeCaption", "Titlul mesajului de avertizare", "Titlul mesajului afișat înainte de autentificare.",
            "Anunță utilizatorul că stația tratează informații clasificate și că activitatea este monitorizată.", Proc(),
            $"„{SecurityOptions}\\Interactive logon: Message title for users attempting to log on”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\LegalNoticeCaption",
            V(("", "titlul este gol: nu se afișează niciun mesaj"), ("*", "textul trebuie să corespundă marcajului de clasificare aprobat"))),
        Reg(@"Policies\System", "LegalNoticeText", "Textul mesajului de avertizare", "Textul mesajului afișat înainte de autentificare.",
            "Indică nivelul maxim de clasificare și regulile de folosire a stației.", Proc(),
            $"„{SecurityOptions}\\Interactive logon: Message text for users attempting to log on”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\LegalNoticeText",
            V(("", "textul este gol: nu se afișează niciun mesaj"), ("*", "textul trebuie să corespundă procedurii aprobate"))),
        Reg("Winlogon", "AutoAdminLogon", "Intrarea automată în cont", "Pornește stația direct într-un cont, fără să se tasteze parola.",
            "Oricine pornește stația ajunge în cont, fără nicio verificare.", "0 sau lipsă (dezactivată).",
            "Nu are o politică de grup dedicată; se setează din registru (sau din „netplwiz”). Prin politica de grup se impune cu preferințele Registry.", @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoAdminLogon",
            V(("1", "intrarea automată este activă"), ("0", "intrarea automată este dezactivată")), "Valoarea nu există: intrarea automată este dezactivată."),
        Reg("Terminal S", "fDenyTSConnections", "Interzicerea conexiunilor Remote Desktop", "Permite sau interzice conectarea la stație de la distanță (Remote Desktop).",
            "Remote Desktop deschis permite lucrul pe stație din altă parte; pe o stație izolată nu trebuie să existe.", "1 (interzis).",
            $"„{AdminTemplates}\\Windows Components\\Remote Desktop Services\\Remote Desktop Session Host\\Connections\\Allow users to connect remotely by using Remote Desktop Services” = Disabled.",
            @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections; impusă prin politică: HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services\fDenyTSConnections",
            V(("1", "conexiunile de la distanță sunt interzise"), ("0", "conexiunile de la distanță sunt permise"))),
        Reg(@"LanmanServer\Parameters", "SMB1", "Partajarea fișierelor prin SMBv1 (server)", "Pornește sau oprește versiunea veche (1) a protocolului de partajare a fișierelor.",
            "SMBv1 are vulnerabilități cunoscute folosite de viruși (de exemplu WannaCry).", "0 (dezactivat).",
            "„MS Security Guide\\Configure SMB v1 server” (șablonul de politici al liniei de bază Microsoft).", @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\SMB1",
            V(("0", "SMBv1 este dezactivat"), ("1", "SMBv1 este activ")), "Valoarea nu există: pe Windows 10/11 recente SMBv1 este de obicei neinstalat; se verifică."),
        Reg("RemovableStorageDevices", "Deny_All", "Blocarea tuturor suporturilor amovibile", "Interzice orice acces (citire, scriere, rulare) la toate suporturile amovibile: stick-uri, discuri externe, CD/DVD, telefoane.",
            "Pe o stație izolată suporturile amovibile sunt principala cale de intrare și de ieșire a datelor.", Proc(),
            $"„{AdminTemplates}\\System\\Removable Storage Access\\All Removable Storage classes: Deny all access”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\Deny_All",
            V(("1", "orice acces la suporturile amovibile este interzis"), ("0", "accesul nu este interzis de această politică")), "Valoarea nu există: politica nu este configurată, accesul nu este interzis de ea."),
        Reg(@"RemovableStorageDevices\{", "Deny_Read", "Interzicerea citirii de pe o clasă de suporturi", "Interzice citirea de pe o clasă de suporturi amovibile (clasa este identificată de codul din cheie, de exemplu discurile amovibile sau CD/DVD).",
            "Împiedică aducerea pe stație a fișierelor de pe suporturi neaprobate.", Proc(),
            $"„{AdminTemplates}\\System\\Removable Storage Access\\<clasa>: Deny read access” (de exemplu „Removable Disks: Deny read access”).", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{codul clasei}\Deny_Read",
            V(("1", "citirea este interzisă"), ("0", "citirea nu este interzisă de această politică")), "Valoarea nu există: politica nu este configurată pentru această clasă."),
        Reg(@"RemovableStorageDevices\{", "Deny_Write", "Interzicerea scrierii pe o clasă de suporturi", "Interzice scrierea pe o clasă de suporturi amovibile.",
            "Împiedică scoaterea datelor de pe stație pe suporturi neaprobate.", Proc(),
            $"„{AdminTemplates}\\System\\Removable Storage Access\\<clasa>: Deny write access” (de exemplu „Removable Disks: Deny write access”).", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{codul clasei}\Deny_Write",
            V(("1", "scrierea este interzisă"), ("0", "scrierea nu este interzisă de această politică")), "Valoarea nu există: politica nu este configurată pentru această clasă."),
        Reg(@"RemovableStorageDevices\{", "Deny_Execute", "Interzicerea rulării de pe o clasă de suporturi", "Interzice pornirea programelor direct de pe o clasă de suporturi amovibile.",
            "Un program adus pe stick nu poate fi pornit direct de pe el.", Proc(),
            $"„{AdminTemplates}\\System\\Removable Storage Access\\<clasa>: Deny execute access”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{codul clasei}\Deny_Execute",
            V(("1", "rularea este interzisă"), ("0", "rularea nu este interzisă de această politică")), "Valoarea nu există: politica nu este configurată pentru această clasă."),
        Reg(@"DeviceInstall\Restrictions", "DenyRemovableDevices", "Interzicerea instalării dispozitivelor amovibile", "Împiedică Windows să instaleze orice dispozitiv nou care se declară amovibil (stick-uri, discuri externe, telefoane).",
            "Un dispozitiv care nu poate fi instalat nu poate fi folosit pe stație.", Proc(),
            $"„{AdminTemplates}\\System\\Device Installation\\Device Installation Restrictions\\Prevent installation of removable devices”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions\DenyRemovableDevices",
            V(("1", "instalarea dispozitivelor amovibile este interzisă"), ("0", "nu este interzisă de această politică")), "Valoarea nu există: politica nu este configurată."),
        Reg(@"DeviceInstall\Restrictions", "DenyUnspecified", "Interzicerea dispozitivelor neaprobate explicit", "Împiedică instalarea oricărui dispozitiv care nu este permis explicit de altă politică (lista albă de dispozitive).",
            "Doar dispozitivele aprobate pe listă pot fi instalate; orice alt stick sau dispozitiv este refuzat.", Proc(),
            $"„{AdminTemplates}\\System\\Device Installation\\Device Installation Restrictions\\Prevent installation of devices not described by other policy settings”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions\DenyUnspecified",
            V(("1", "doar dispozitivele permise explicit pot fi instalate"), ("0", "nu este interzis de această politică")), "Valoarea nu există: politica nu este configurată."),
        Reg(@"DeviceInstall\Restrictions", "DenyDeviceIDs", "Interzicerea unor dispozitive anume", "Activează lista de coduri de dispozitiv care nu pot fi instalate.",
            "Blochează anumite modele de dispozitive (de exemplu un tip de stick).", Proc(),
            $"„{AdminTemplates}\\System\\Device Installation\\Device Installation Restrictions\\Prevent installation of devices that match any of these device IDs”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions\DenyDeviceIDs (lista în subcheia DenyDeviceIDs)",
            V(("1", "lista de dispozitive interzise este activă"), ("0", "lista nu este aplicată")), "Valoarea nu există: politica nu este configurată."),
        Reg(@"DeviceInstall\Restrictions", "DenyDeviceClasses", "Interzicerea unor clase de dispozitive", "Activează lista de clase de dispozitive (de exemplu toate discurile, toate telefoanele) care nu pot fi instalate.",
            "Blochează categorii întregi de dispozitive.", Proc(),
            $"„{AdminTemplates}\\System\\Device Installation\\Device Installation Restrictions\\Prevent installation of devices using drivers that match these device setup classes”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions\DenyDeviceClasses (lista în subcheia DenyDeviceClasses)",
            V(("1", "lista de clase interzise este activă"), ("0", "lista nu este aplicată")), "Valoarea nu există: politica nu este configurată."),
        Reg(@"DeviceInstall\Restrictions", "AllowDeviceIDs", "Permiterea unor dispozitive anume", "Activează lista de coduri de dispozitiv care pot fi instalate chiar dacă alte reguli le-ar refuza.",
            "Este lista dispozitivelor aprobate (de exemplu stick-urile înregistrate ale organizației).", Proc(),
            $"„{AdminTemplates}\\System\\Device Installation\\Device Installation Restrictions\\Allow installation of devices that match any of these device IDs”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions\AllowDeviceIDs (lista în subcheia AllowDeviceIDs)",
            V(("1", "lista de dispozitive permise este activă"), ("0", "lista nu este aplicată")), "Valoarea nu există: politica nu este configurată."),
        Reg("FVE", "RDVDenyWriteAccess", "Scrierea doar pe stick-uri criptate cu BitLocker", "Interzice scrierea pe orice suport amovibil care nu este criptat cu BitLocker.",
            "Datele care pleacă de pe stație ajung doar pe suporturi criptate, care nu pot fi citite dacă se pierd.", Proc(),
            $"„{AdminTemplates}\\Windows Components\\BitLocker Drive Encryption\\Removable Data Drives\\Deny write access to removable drives not protected by BitLocker”.",
            @"HKLM\SYSTEM\CurrentControlSet\Policies\Microsoft\FVE\RDVDenyWriteAccess (sau HKLM\SOFTWARE\Policies\Microsoft\FVE\RDVDenyWriteAccess)",
            V(("1", "scrierea este permisă doar pe suporturi criptate"), ("0", "scrierea nu este restricționată de această politică")), "Valoarea nu există: politica nu este configurată."),
        Reg("Explorer", "NoDriveTypeAutoRun", "Oprirea pornirii automate (AutoPlay)", "Stabilește pentru ce tipuri de unități Windows nu mai pornește automat nimic la introducerea unui suport.",
            "Un stick sau un CD poate porni singur un program la introducere.", "255 (oprit pentru toate unitățile); cel puțin suporturile amovibile (4) și CD-urile (32).",
            $"„{AdminTemplates}\\Windows Components\\AutoPlay Policies\\Turn off Autoplay” = Enabled, „All drives”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoDriveTypeAutoRun",
            V(("255", "pornirea automată este oprită pentru toate unitățile"), ("145", "valoarea implicită Windows: pornirea automată rămâne pentru suporturile amovibile"), ("*", "numărul este o sumă de tipuri de unități: 4 = amovibile, 8 = fixe, 16 = rețea, 32 = CD/DVD, 64 = discuri RAM, 128 = necunoscute"))),
        Reg("Explorer", "NoAutorun", "Oprirea comenzilor autorun.inf", "Oprește comenzile din fișierul autorun.inf al unui suport introdus.",
            "Fișierul autorun.inf de pe un stick poate porni un program fără ca utilizatorul să facă nimic.", "1 (oprite).",
            $"„{AdminTemplates}\\Windows Components\\AutoPlay Policies\\Set the default behavior for AutoRun” = „Do not execute any autorun commands”.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoAutorun",
            V(("1", "comenzile autorun sunt oprite"), ("0", "comenzile autorun sunt permise")), "Valoarea nu există: comenzile autorun nu sunt oprite de politică."),
        Reg("WDigest", "UseLogonCredential", "Parolele în memorie (WDigest)", "Stabilește dacă Windows păstrează parolele în memorie într-o formă care poate fi citită.",
            "Cu valoarea 1, un program rău-intenționat poate extrage parolele în clar din memorie.", "0 (dezactivat).",
            "„MS Security Guide\\WDigest Authentication” (șablonul de politici al liniei de bază Microsoft) = Disabled.", @"HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest\UseLogonCredential",
            V(("0", "parolele nu sunt păstrate în clar"), ("1", "parolele sunt păstrate în memorie în clar")), "Valoarea nu există: pe Windows 10/11 parolele nu sunt păstrate în clar (implicit)."),
        Reg("Lsa", "RunAsPPL", "Protecția procesului care păstrează parolele (LSA)", "Rulează procesul care păstrează parolele (LSA) ca proces protejat, pe care alte programe nu îl pot citi.",
            "Îngreunează furtul parolelor din memorie.", "1 sau 2 (protejat).",
            $"„{AdminTemplates}\\System\\Local Security Authority\\Configures LSASS to run as a protected process”.", @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa\RunAsPPL",
            V(("0", "procesul nu este protejat"), ("1", "protejat, cu blocare în firmware (nu poate fi oprit din registru)"), ("2", "protejat, fără blocare în firmware"))),
        Reg("Lsa", "LmCompatibilityLevel", "Nivelul de autentificare LAN Manager", "Stabilește ce metode vechi de autentificare în rețea acceptă stația.",
            "Metodele vechi (LM, NTLMv1) se sparg ușor; stația trebuie să folosească doar NTLMv2.", "5 (doar NTLMv2; refuză LM și NTLM).",
            $"„{SecurityOptions}\\Network security: LAN Manager authentication level”.", @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa\LmCompatibilityLevel",
            V(("0", "trimite LM și NTLM"), ("1", "trimite LM și NTLM, NTLMv2 dacă se negociază"), ("2", "trimite doar NTLM"), ("3", "trimite doar NTLMv2"),
              ("4", "trimite doar NTLMv2; refuză LM"), ("5", "trimite doar NTLMv2; refuză LM și NTLM"))),
        Reg("Lsa", "NoLMHash", "Fără amprenta LAN Manager a parolei", "Împiedică păstrarea parolei în formatul vechi LAN Manager, foarte slab.",
            "Amprenta LAN Manager se sparge în câteva minute.", "1 (nu se păstrează).",
            $"„{SecurityOptions}\\Network security: Do not store LAN Manager hash value on next password change”.", @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa\NoLMHash",
            V(("1", "amprenta veche nu se păstrează"), ("0", "amprenta veche se păstrează la următoarea schimbare a parolei"))),
        Reg("Lsa", "RestrictAnonymousSAM", "Fără listarea anonimă a conturilor", "Împiedică un utilizator anonim să afle lista conturilor stației.",
            "Lista conturilor îi arată unui atacator ce să încerce.", "1 (interzis).",
            $"„{SecurityOptions}\\Network access: Do not allow anonymous enumeration of SAM accounts”.", @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa\RestrictAnonymousSAM",
            V(("1", "listarea anonimă este interzisă"), ("0", "listarea anonimă este permisă"))),
        Reg("Lsa", "RestrictAnonymous", "Fără listarea anonimă a conturilor și a folderelor partajate", "Împiedică un utilizator anonim să afle conturile și folderele partajate ale stației.",
            "Informațiile despre conturi și foldere ajută un atacator.", "1 (interzis).",
            $"„{SecurityOptions}\\Network access: Do not allow anonymous enumeration of SAM accounts and shares”.", @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa\RestrictAnonymous",
            V(("1", "listarea anonimă este interzisă"), ("0", "listarea anonimă este permisă"))),
        Reg("SrpV2", "EnforcementMode", "Modul listei albe de programe (AppLocker)", "Stabilește dacă regulile AppLocker blochează programele neaprobate sau doar le notează în jurnal.",
            "Doar modul de blocare oprește programele neaprobate; modul de audit doar le notează.", Proc(),
            @"„Computer Configuration\Windows Settings\Security Settings\Application Control Policies\AppLocker” → „Configure rule enforcement”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\SrpV2\<colecție>\EnforcementMode",
            V(("1", "regulile blochează programele neaprobate"), ("0", "regulile doar notează în jurnal (audit)")), "Valoarea nu există: colecția de reguli nu este configurată."),
        Reg("WindowsAI", "DisableAIDataAnalysis", "Oprirea funcției Recall", "Oprește salvarea periodică a imaginilor ecranului de către funcția Recall.",
            "Recall păstrează pe disc imagini ale ecranului, deci copii ale documentelor afișate.", "1 (oprit).",
            $"„{AdminTemplates}\\Windows Components\\Windows AI\\Turn off saving snapshots for use with Recall”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis (sau sub HKCU)",
            V(("1", "Recall nu salvează imagini"), ("0", "Recall poate salva imagini"))),
        Reg("WindowsAI", "AllowRecallEnablement", "Disponibilitatea funcției Recall", "Stabilește dacă funcția Recall poate fi activată pe stație.",
            "Vezi „Oprirea funcției Recall”: imaginile ecranului sunt copii ale documentelor.", "0 (indisponibilă).",
            $"„{AdminTemplates}\\Windows Components\\Windows AI\\Allow Recall to be enabled” = Disabled.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\AllowRecallEnablement",
            V(("0", "Recall nu poate fi activat"), ("1", "Recall poate fi activat"))),
        Reg("WindowsCopilot", "TurnOffWindowsCopilot", "Oprirea asistentului Windows Copilot", "Oprește asistentul Windows Copilot.",
            "Copilot poate trimite conținutul ecranului și întrebările către un serviciu din afara stației.", "1 (oprit).",
            @"„User Configuration\Administrative Templates\Windows Components\Windows Copilot\Turn off Windows Copilot”.", @"HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot\TurnOffWindowsCopilot (sau sub HKLM)",
            V(("1", "Copilot este oprit"), ("0", "Copilot este permis"))),
        Reg("ScriptBlockLogging", "EnableScriptBlockLogging", "Înregistrarea scripturilor PowerShell", "Înregistrează în jurnal textul fiecărui script PowerShell rulat.",
            "Fără ea, un script rău-intenționat nu lasă urme ale conținutului său.", "1 (pornită).",
            $"„{AdminTemplates}\\Windows Components\\Windows PowerShell\\Turn on PowerShell Script Block Logging”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging\EnableScriptBlockLogging",
            V(("1", "scripturile sunt înregistrate"), ("0", "scripturile nu sunt înregistrate")), "Valoarea nu există: scripturile nu sunt înregistrate integral."),
        Reg("Transcription", "EnableTranscripting", "Transcrierea sesiunilor PowerShell", "Salvează într-un fișier tot ce s-a tastat și s-a afișat în fiecare sesiune PowerShell.",
            "Păstrează o urmă completă a comenzilor rulate pe stație.", Proc("1 (pornită), cu fișierele păstrate într-un loc protejat."),
            $"„{AdminTemplates}\\Windows Components\\Windows PowerShell\\Turn on PowerShell Transcription”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell\Transcription\EnableTranscripting",
            V(("1", "transcrierea este pornită"), ("0", "transcrierea este oprită")), "Valoarea nu există: transcrierea este oprită."),
        Reg("Memory Management", "ClearPageFileAtShutdown", "Ștergerea memoriei virtuale la oprire", "Șterge fișierul de paginare (memoria virtuală de pe disc) la oprirea stației.",
            "Fișierul de paginare poate conține fragmente din documentele deschise.", Proc("1 (se șterge) este uzual pe stațiile care tratează informații clasificate."),
            $"„{SecurityOptions}\\Shutdown: Clear virtual memory pagefile”.", @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\ClearPageFileAtShutdown",
            V(("1", "fișierul de paginare se șterge la oprire"), ("0", "fișierul de paginare rămâne pe disc"))),
        Reg("Power", "HibernateEnabled", "Hibernarea", "Stabilește dacă stația poate hiberna.",
            "La hibernare tot conținutul memoriei, inclusiv documentele deschise, se scrie pe disc.", Proc(),
            "Nu are o politică de grup dedicată; se setează cu „powercfg /hibernate off” sau din registru.", @"HKLM\SYSTEM\CurrentControlSet\Control\Power\HibernateEnabled",
            V(("0", "hibernarea este oprită"), ("1", "hibernarea este permisă"))),
        Reg("CrashControl", "CrashDumpEnabled", "Copia memoriei la căderea sistemului", "Stabilește ce copie a memoriei salvează Windows pe disc când sistemul cade.",
            "Copia memoriei poate conține date din documentele deschise.", Proc(),
            "„Sistem” → „Setări avansate de sistem” → „Pornire și recuperare”; nu are o politică de grup dedicată.", @"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl\CrashDumpEnabled",
            V(("0", "nu se salvează nimic"), ("1", "copie completă a memoriei"), ("2", "copia nucleului"), ("3", "copie mică"), ("7", "copie automată"))),
        Reg(@"W32Time\Parameters", "NtpServer", "Serverul de timp", "Stabilește serverul după care își sincronizează stația ceasul.",
            "Ora din jurnale este de încredere doar dacă ceasul este sincronizat cu o sursă aprobată.", Proc(),
            $"„{AdminTemplates}\\System\\Windows Time Service\\Time Providers\\Configure Windows NTP Client”.", @"HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters\NtpServer",
            V(("*", "numele serverului trebuie să fie cel aprobat de procedură")), "Valoarea nu există: niciun server de timp nu este configurat."),
        Reg(@"W32Time\Parameters", "Type", "Modul de sincronizare a ceasului", "Stabilește cum își sincronizează stația ceasul.",
            "Un ceas lăsat liber se abate în timp și strică cronologia jurnalelor.", Proc(),
            $"„{AdminTemplates}\\System\\Windows Time Service\\Time Providers\\Configure Windows NTP Client” → „Type”.", @"HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters\Type",
            V(("NTP", "după un server de timp"), ("NT5DS", "după domeniu"), ("NoSync", "fără sincronizare"), ("AllSync", "după toate sursele disponibile"))),
        Reg(@"ConsentStore\webcam", "Value", "Accesul aplicațiilor la cameră", "Permisiunea implicită de acces la cameră pentru toate aplicațiile.",
            "O cameră permisă poate face imagini din încăpere, inclusiv ale documentelor.", "Deny (blocat).",
            $"„Setări → Confidențialitate și securitate → Cameră”; prin politica de grup: „{AdminTemplates}\\Windows Components\\App Privacy\\Let Windows apps access the camera” = Force Deny.",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\Value",
            V(("Deny", "camera este blocată"), ("Allow", "camera este permisă"))),
        Reg(@"ConsentStore\microphone", "Value", "Accesul aplicațiilor la microfon", "Permisiunea implicită de acces la microfon pentru toate aplicațiile.",
            "Un microfon permis poate înregistra conversațiile din încăpere.", "Deny (blocat).",
            $"„Setări → Confidențialitate și securitate → Microfon”; prin politica de grup: „{AdminTemplates}\\Windows Components\\App Privacy\\Let Windows apps access the microphone” = Force Deny.",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\Value",
            V(("Deny", "microfonul este blocat"), ("Allow", "microfonul este permis"))),
        Reg("Firewall", "EnableFirewall", "Firewall-ul Windows pe un profil de rețea", "Pornește sau oprește firewall-ul Windows pe profilul din cheie (domeniu, privat sau public).",
            "Fără firewall, stația acceptă orice conexiune dacă ajunge vreodată într-o rețea.", "1 (pornit) pe toate profilurile.",
            @"„Computer Configuration\Windows Settings\Security Settings\Windows Defender Firewall with Advanced Security” → proprietăți → „Firewall state” = On, pentru fiecare profil.",
            @"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\<DomainProfile|PrivateProfile|PublicProfile>\EnableFirewall",
            V(("1", "firewall-ul este pornit"), ("0", "firewall-ul este oprit"))),
        Reg("Windows Defender", "DisableAntiSpyware", "Oprirea antivirusului Microsoft Defender", "Politica veche care oprește antivirusul Microsoft Defender.",
            "Fără antivirus, programele rău-intenționate aduse pe stick nu sunt oprite.", "0 sau lipsă (antivirusul rămâne pornit), dacă nu există alt antivirus aprobat.",
            $"„{AdminTemplates}\\Windows Components\\Microsoft Defender Antivirus\\Turn off Microsoft Defender Antivirus” = Disabled sau Not configured.",
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\DisableAntiSpyware",
            V(("0", "antivirusul nu este oprit de politică"), ("1", "politica cere oprirea antivirusului")), "Valoarea nu există: politica nu oprește antivirusul."),
        Reg("Real-Time Protection", "DisableRealtimeMonitoring", "Oprirea protecției în timp real", "Oprește verificarea fișierelor în momentul în care sunt deschise sau copiate.",
            "Fără ea, un fișier rău-intenționat copiat de pe stick nu este verificat la copiere.", "0 sau lipsă (protecția rămâne pornită).",
            $"„{AdminTemplates}\\Windows Components\\Microsoft Defender Antivirus\\Real-time Protection\\Turn off real-time protection” = Disabled sau Not configured.",
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection\DisableRealtimeMonitoring",
            V(("0", "protecția în timp real nu este oprită de politică"), ("1", "politica cere oprirea protecției în timp real")), "Valoarea nu există: politica nu oprește protecția în timp real."),
        Reg(@"EventLog\Security", "MaxSize", "Dimensiunea jurnalului de securitate", "Stabilește cât de mare poate fi jurnalul de securitate înainte să se umple.",
            "Un jurnal mic pierde repede evenimentele vechi, deci urmele perioadei controlate.", Proc("linia de bază de securitate Microsoft cere cel puțin 196608 KB."),
            $"„{AdminTemplates}\\Windows Components\\Event Log Service\\Security\\Specify the maximum log file size (KB)”.", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\EventLog\Security\MaxSize",
            V(("*", "numărul este în kilobytes (KB)")), "Valoarea nu există: jurnalul are dimensiunea implicită sau cea setată direct în jurnal."),
    ];

    // ---- registry values the station control reads that are information, not policy settings ---------------------------------------------

    /// <summary>
    /// Codes of <see cref="LogAnalyzer.Dfir.Windows.Audit.CodeExplainer"/>'s registry catalogue that describe the station but are not set by a policy
    /// (version numbers, firmware type, evidence keys); CodeExplainer explains them and they need no entry here. Each one has its reason.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> InformationalOnly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [@"Winlogon\DefaultPassword"] = "o parolă salvată; aplicația verifică doar dacă există, nicio politică nu o cere",
        [@"State\UEFISecureBootEnabled"] = "starea firmware-ului, se schimbă din setările plăcii de bază, nu din politică",
        [@"Control\PEFirmwareType"] = "tipul firmware-ului, nu este o setare",
        [@"CurrentVersion\DisplayVersion"] = "versiunea Windows, nu este o setare",
        [@"CurrentVersion\CurrentBuild"] = "versiunea Windows, nu este o setare",
        [@"CurrentVersion\UBR"] = "revizia actualizărilor, nu este o setare",
        [@"PowerShellEngine\PowerShellVersion"] = "versiunea unei componente instalate, nu este o setare",
        [@"Storage\HotPlugSecureOpen"] = "comportament nedocumentat public de Microsoft; aplicația nu îi dă o valoare recomandată",
        [@"Enum\USBSTOR"] = "cheie cu istoricul dispozitivelor (probă), nu o setare",
        [@"NetworkList\Profiles"] = "cheie cu istoricul rețelelor (probă), nu o setare",
    };

    // ---- lookup ----------------------------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, PolicySettingGuide> BySecpol = SecpolGuides.ToDictionary(g => g.Id["secpol:".Length..], StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PolicySettingGuide> ByAudit = AuditRows.Select(Audit).ToDictionary(g => g.Id["audit:".Length..], StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PolicySettingGuide> ByService = ServiceRows.Select(Service).ToDictionary(g => g.Id["service:".Length..], StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Guid, string> AuditNameByGuid = AuditSubcategories.ByName.ToDictionary(kv => kv.Value, kv => kv.Key);
    private static readonly Regex ServiceStartKey = new(@"(?:^|\\)Services\\(?<svc>[^\\]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Every entry of the catalogue, in the order: account policy, audit, services, registry values.</summary>
    public static IReadOnlyList<PolicySettingGuide> All { get; } = [.. SecpolGuides, .. ByAudit.Values, .. ByService.Values, .. RegistryGuides];

    /// <summary>The service names with an entry (a policy may name any other service; it then gets the generic explanation).</summary>
    public static IReadOnlyCollection<string> KnownServices => ByService.Keys;

    /// <summary>The catalogue entry for <paramref name="s"/>, or null when the setting is not in the catalogue.</summary>
    public static PolicySettingGuide? Find(SettingRef s)
    {
        switch (s.Type)
        {
            case "secpol": return BySecpol.GetValueOrDefault(s.Name);
            case "service": return ByService.GetValueOrDefault(s.Name);
            case "audit":
                if (!AuditSubcategories.TryResolve(s.Name, out var guid)) return null;
                return AuditNameByGuid.TryGetValue(guid, out var name) ? ByAudit.GetValueOrDefault(name) : null;
            case "registry":
                // A Group Policy Preferences service item is imported as SYSTEM\CurrentControlSet\Services\<name>\Start: explain the service.
                if (s.Name.Equals("Start", StringComparison.OrdinalIgnoreCase) && ServiceStartKey.Match(s.Key) is { Success: true } m
                    && ByService.TryGetValue(m.Groups["svc"].Value, out var svc))
                    return svc;
                // The key contains the catalogue's key part; a key written as a short suffix (as the station checks name it, e.g. "System") also matches.
                return RegistryGuides.FirstOrDefault(g => KeyPartOf(g) is var (part, value)
                    && value.Equals(s.Name, StringComparison.OrdinalIgnoreCase) && s.Key.Length > 0
                    && (s.Key.Contains(part, StringComparison.OrdinalIgnoreCase) || part.EndsWith(s.Key, StringComparison.OrdinalIgnoreCase)));
            default: return null;
        }
    }

    private static (string Part, string Value) KeyPartOf(PolicySettingGuide g)
    {
        var path = g.Id["registry:".Length..];
        var cut = path.LastIndexOf('\\');
        return (path[..cut], path[(cut + 1)..]);
    }

    /// <summary>The one-line "Ce face" for the row tooltip.</summary>
    public static string OneLine(SettingRef s) => Find(s) is { } g ? $"{g.Name}: {g.What}" : Generic(s).What;

    /// <summary>The explanation panel for one plan row: what the setting does, what the opened policy requires, what the current value means.</summary>
    public static PolicySettingExplanation Explain(ControlPlan row)
    {
        var found = Find(row.Setting);
        var g = found ?? Generic(row.Setting);
        var required = $"Valoarea cerută de procedura dumneavoastră (politica deschisă, controlul {row.ControlId}): {row.Desired}" +
                       (row.DesiredValue is { } dv && Meaning(g, dv) is { } dm ? $", adică {dm}." : ".");
        return new(row.Setting.Display, g.Name, g.What, g.Why, g.Recommended, required, CurrentMeaning(g, row.Setting, row.Current, row.ReadError),
                   g.Where, g.ForIt, found is not null);
    }

    private static string? Meaning(PolicySettingGuide g, string value) =>
        g.Values.TryGetValue(value.Trim(), out var m) ? m : null;

    private static string CurrentMeaning(PolicySettingGuide g, SettingRef s, SettingValue? current, string? readError)
    {
        if (readError is not null) return $"Valoarea nu a putut fi citită pe această stație ({readError}); nu se știe ce este setat acum.";
        if (current?.Value is not { } v)
            return g.AbsentMeaning.Length > 0 ? g.AbsentMeaning
                 : s.Type == "registry" ? "Valoarea nu există: Windows folosește comportamentul său implicit." : "Valoarea nu există pe această stație.";
        if (Meaning(g, v) is { } m) return $"Stația are acum {v}: {m}.";
        return g.Values.TryGetValue("*", out var note) ? $"Stația are acum {v} ({note})." : $"Stația are acum {v}.";
    }

    /// <summary>A setting outside the catalogue: what kind of setting it is, never a guessed meaning.</summary>
    private static PolicySettingGuide Generic(SettingRef s)
    {
        const string NotInCatalogue = "Semnificația exactă a acestei setări nu este în catalogul aplicației; titlul controlului din politică o descrie.";
        switch (s.Type)
        {
            case "service":
                return new(s.Display, $"Serviciul „{s.Name}”", $"Modul de pornire al serviciului Windows „{s.Name}” (un program care rulează în fundal).",
                    "Serviciile inutile pe o stație izolată măresc suprafața de atac. " + NotInCatalogue, Proc(),
                    $"„Servicii” (services.msc) → serviciul „{s.Name}” → „Tip pornire”.", $@"HKLM\SYSTEM\CurrentControlSet\Services\{s.Name}\Start",
                    StartValues, "Serviciul nu există pe această stație (nu este instalat), deci nu poate porni.");
            case "audit":
                return new(s.Display, $"Auditul „{s.Name}”", $"O categorie a auditului avansat Windows („{s.Name}”).",
                    "Ce nu este auditat nu lasă urme și nu poate fi verificat la control. " + NotInCatalogue, Proc(),
                    $"„{AdvancedAudit}”.", $"auditpol /get /subcategory:\"{s.Name}\"", AuditValues, "");
            case "secpol":
                return new(s.Display, $"Setarea de cont „{s.Name}”", $"Setarea „{s.Name}” din politica de conturi a stației.",
                    "Face parte din regulile pentru parole și conturi. " + NotInCatalogue, Proc(),
                    @"„Computer Configuration\Windows Settings\Security Settings\Account Policies”.", $"șablon de securitate [System Access] {s.Name}", NoValues, "");
        }
        var machine = s.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) || s.Hive.Contains("MACHINE", StringComparison.OrdinalIgnoreCase);
        var gpo = s.Key.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase) || s.Key.StartsWith(@"Software\Policies", StringComparison.OrdinalIgnoreCase);
        return new(s.Display, $"Valoarea de registru „{s.Name}”",
            $"Valoarea „{s.Name}” din registrul Windows (baza de date a setărilor), {(machine ? "pentru tot calculatorul" : "pentru utilizatorul curent")}" +
            (gpo ? ", impusă prin politica de grup." : "."),
            "Politica deschisă cere o anumită valoare pentru ea. " + NotInCatalogue, Proc(),
            gpo ? $"De obicei într-un șablon administrativ („{(machine ? AdminTemplates : @"User Configuration\Administrative Templates")}”); numele exact este în titlul controlului."
                : "Direct în registru sau prin preferințele de politică de grup (Registry).",
            $@"{s.Hive}\{s.Key}\{s.Name}", NoValues, "Valoarea nu există: Windows folosește comportamentul său implicit.");
    }
}
