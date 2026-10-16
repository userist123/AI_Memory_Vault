namespace LogAnalyzer.Dfir.Language;

/// <summary>Language of a glossary phrase. Only the glossary uses it until the resource layer (WP6b) takes over the rest of the UI.</summary>
public enum GlossaryLanguage { Romanian, English }

/// <summary>One glossary entry (UX contract §17, audit rows R18.1-R18.12): the technical term, its plain-language phrase in each language, and a short explanation (Romanian).</summary>
/// <param name="Aliases">Other spellings that mean the same entry (the Romanian words on screen, enum names, inflected forms). Looked up like the key; never
/// part of the jargon list, so a role page may show them bare.</param>
public sealed record GlossaryTerm(string Key, string Technical, string Romanian, string English, string Explanation, IReadOnlyList<string>? Aliases = null)
{
    /// <summary>The technical name and every alias: what a screen or a report may print for this entry.</summary>
    public IEnumerable<string> Spellings => Aliases is null ? [Technical] : Aliases.Prepend(Technical);
}

/// <summary>
/// The single plain-language glossary. Normal views show the human phrase; advanced/technical views keep the technical name (with the
/// human phrase beside it), and every place that shows the human phrase also offers <see cref="Tooltip"/> with the technical name.
/// A term that is not in the table is returned unchanged: the glossary never invents a translation.
/// </summary>
public static class Glossary
{
    public static IReadOnlyList<GlossaryTerm> Terms { get; } =
    [
        new("evtx", "EVTX", "Jurnale Windows", "Windows logs",
            "Fișierele în care Windows înregistrează evenimentele sistemului, ale securității și ale aplicațiilor."),
        new("prefetch", "Prefetch", "Istoricul pornirii programelor", "Program start history",
            "Urmele pe care Windows le păstrează când un program este pornit: arată ce program și cam când, nu cine l-a pornit."),
        new("bam", "BAM", "Activitatea programelor", "Program activity",
            "Evidența Windows a programelor rulate de fiecare utilizator și a momentului ultimei rulări."),
        new("amcache", "Amcache", "Istoricul aplicațiilor", "Application history",
            "Lista aplicațiilor cunoscute de Windows; prezența unei intrări nu dovedește că aplicația a rulat."),
        new("evidence_graph", "Evidence Graph", "Legături între evenimente", "Connections between events",
            "Harta legăturilor dintre fișiere, procese, conturi, adrese și constatări; fiecare legătură arată proba sau derivarea ei."),
        new("provenance", "Provenance", "De unde provine informația", "Where this information came from",
            "Sursa unei informații: proba, fișierul (cu amprenta SHA-256) și parserul care a produs-o."),
        new("chain_of_custody", "Chain of Custody", "Lanțul de custodie", "Chain of custody",
            "Registrul cine a avut elementul de probă, când și ce s-a făcut cu el, cu amprente care arată dacă a fost modificată."),
        new("ioc", "IOC", "Indicator suspect", "Suspicious indicator",
            "Un element (adresă, domeniu, fișier, amprentă) asociat cu activitate rău-intenționată; apariția lui nu dovedește singură un atac."),
        new("sigma", "Sigma", "Regulă de detecție de securitate", "Security detection rule",
            "Regulă scrisă într-un format comun, care caută în jurnale tipare de activitate suspectă."),
        new("yara", "YARA", "Regulă de detecție pe fișiere/conținut", "File/content detection rule",
            "Regulă care caută în fișiere sau în conținut tipare asociate cu programe rău-intenționate."),
        new("correlation", "Correlation", "Evenimente legate", "Related events",
            "Evenimente care apar împreună (în timp, în cale sau în aceeași sursă); apropierea lor nu dovedește că unul l-a provocat pe celălalt."),
        new("verification", "Verification", "Verificarea probelor", "Evidence check",
            "Controlul automat al fiecărei constatări față de probele din caz; nu este o verificare externă sau umană."),
        // WP18 S3: the terms of the control line and of the administration screens.
        new("hardware_id", "Hardware ID", "Identificatorul stației", "Station identifier",
            "Un cod calculat din procesorul și placa de bază a calculatorului; identifică stația pentru licență și în rapoarte."),
        new("networklist", "NetworkList", "Rețele la care s-a conectat stația", "Networks the station connected to",
            "Lista pe care Windows o păstrează cu rețelele la care calculatorul a fost conectat vreodată, cu data primei și ultimei conectări."),
        new("usbstor", "USBSTOR", "Suporturi USB conectate", "USB storage devices connected",
            "Evidența Windows a memoriilor și discurilor USB care au fost conectate la calculator; nu arată ce fișiere s-au copiat."),
        new("auditpol", "auditpol", "Setările de jurnalizare", "Audit settings",
            "Ce fel de acțiuni înregistrează Windows în jurnalul de securitate; dacă o categorie este oprită, acțiunile ei nu lasă urme."),
        new("record_id", "RecordID", "Numărul înregistrării din jurnal", "Log record number",
            "Numărul de ordine al unei înregistrări din jurnal; lipsurile din șirul de numere arată înregistrări șterse sau pierdute."),
        new("crl", "CRL", "Lista certificatelor revocate", "Certificate revocation list",
            "Lista publicată de autoritatea de certificare cu cardurile sau certificatele care nu mai sunt valabile; se aduce pe suport, aplicația nu descarcă nimic."),
        new("ca", "CA", "Autoritatea de certificare", "Certification authority",
            "Instituția care emite certificatele de pe carduri; aplicația acceptă doar carduri emise de autoritățile importate de administrator."),
        new("sha256", "SHA-256", "Amprenta fișierului", "File fingerprint",
            "Un cod unic calculat din conținutul unui fișier; dacă fișierul se schimbă cu un singur bit, codul se schimbă, deci arată dacă proba a rămas intactă."),
        new("srum", "SRUM", "Consumul de rețea al programelor", "Network usage per program",
            "Evidența Windows a traficului de rețea făcut de fiecare program, pe ore; arată că un program a comunicat, nu cu cine."),
        new("srum_connectivity", "SRUM Network Connectivity", "Durata conectărilor la rețea", "Network connection time",
            "Tabela SRUM în care Windows notează, pe ore, cât timp a fost conectată fiecare interfață de rețea și la ce profil; arată durata, nu ce s-a transferat."),
        new("firewall_log", "pfirewall.log", "Jurnalul firewall-ului", "Firewall log",
            "Fișierul text în care firewall-ul Windows notează conexiunile permise și blocate (adrese, porturi, direcție); există doar dacă jurnalizarea este pornită."),
        new("wfp", "WFP 5156/5157", "Conexiunile văzute de firewall", "Connections seen by the firewall",
            "Evenimentele de securitate 5156 (conexiune permisă) și 5157 (conexiune blocată), cu programul și adresele; apar doar dacă auditul „Filtering Platform Connection” este pornit."),
        new("ncsi", "NCSI", "Verificarea accesului la internet", "Internet access check",
            "Serviciul Windows care stabilește dacă o rețea are ieșire la internet; evenimentul 4042 notează schimbarea (internet / doar local / fără conectivitate)."),
        new("dhcp", "DHCP", "Atribuirea automată a adresei", "Automatic address assignment",
            "Serviciul care dă stației o adresă de rețea și gateway-ul; jurnalul clientului DHCP arată pe ce interfață și când s-a obținut adresa."),
        new("wlan", "WLAN-AutoConfig", "Conectările Wi-Fi", "Wi-Fi connections",
            "Jurnalul Windows al conexiunilor fără fir: 8001 conectat, 8003 deconectat, cu numele rețelei (SSID) și adaptorul folosit."),
        new("rdp", "RDP", "Acces de la distanță", "Remote desktop access",
            "Conectarea la calculator de pe alt calculator, ca și cum ai sta în fața lui; apare în jurnale ca autentificare de tip 10."),
        new("gpo", "GPO", "Politica de grup", "Group policy",
            "Setările impuse de organizație calculatoarelor și conturilor; aplicația compară politica așteptată cu cea găsită pe stație."),
        new("anchor_code", "Audit anchor code", "Cod de ancorare", "Anchor code",
            "Un cod scurt (4 grupe de 5 caractere) care rezumă tot jurnalul aplicației până la sfârșitul controlului. Se trece pe procesul-verbal semnat; la controlul următor se introduce din nou și aplicația verifică dacă jurnalul de acum îl mai conține. Dacă nu, jurnalul a fost rescris între timp.",
            ["cod de ancorare", "codul de ancorare", "Cod de ancorare", "Codul de ancorare"]),
        new("officer_signature", "Qualified electronic signature (PAdES)", "Semnătura calificată a ofițerului de securitate", "Security officer's qualified signature",
            "Semnătura pusă pe raportul de control cu certificatul calificat de pe cardul ofițerului de securitate (codul PIN îl cere Windows, aplicația nu îl vede). Se poate pune în aplicație sau în Adobe. Doar raportul semnat astfel este raport oficial; administratorul stației poate rula controlul, dar nu îl poate semna.",
            ["semnătura calificată", "semnătură electronică calificată", "semnătura electronică calificată"]),
        new("cert_fingerprint", "Certificate SHA-256 fingerprint", "Amprenta certificatului", "Certificate fingerprint",
            "Un șir de 64 de caractere (16 grupe de 4) care identifică exact certificatul ofițerului. Se trece pe procesul-verbal; la verificare, semnătura se compară cu amprenta de pe hârtie, nu doar cu lista din calculator.",
            ["amprenta certificatului"]),
        new("write_once_media", "WORM / CD-R", "Disc care se scrie o singură dată", "Write-once disc",
            "Un CD-R sau DVD-R ars o singură dată: ce s-a scris pe el nu mai poate fi modificat. Pe el se păstrează copia jurnalului aplicației de la fiecare control.",
            ["CD-R", "DVD-R"]),
        // Usability 2026-10-16: every technical word a controller meets on the control screens and in the reports.
        new("sacl", "SACL", "Lista de audit a unui fișier", "File audit list",
            "Setarea unui fișier sau folder care spune ce acces se înregistrează în jurnalul de securitate (citire, scriere, ștergere și de către cine). Fără ea, accesul la fișier nu lasă nicio urmă în jurnal."),
        new("bitlocker_to_go", "BitLocker To Go", "Criptarea stick-ului USB", "USB stick encryption",
            "Criptarea Windows pentru memorii USB și discuri externe: fără parolă sau cheie, conținutul nu poate fi citit pe alt calculator. Un suport pierdut nu mai expune informațiile."),
        new("bitlocker", "BitLocker", "Criptarea discului", "Disk encryption",
            "Criptarea Windows a discului întreg: dacă discul este scos din calculator, datele de pe el nu pot fi citite fără cheie."),
        new("pades", "PAdES", "Semnătură electronică în PDF", "PDF electronic signature",
            "Standardul european după care semnătura electronică se pune în interiorul fișierului PDF; orice program de citire PDF (de exemplu Adobe) arată cine a semnat și dacă documentul a fost modificat după semnare."),
        new("uac", "UAC", "Confirmarea drepturilor de administrator", "Administrator confirmation prompt",
            "Fereastra Windows care cere confirmare sau parolă înainte ca un program să primească drepturi de administrator. Dacă este oprită, orice program poate prelua controlul stației fără să întrebe."),
        new("wmi", "WMI", "Interfața Windows de interogare", "Windows query interface",
            "Mecanismul prin care un program cere de la Windows informații despre conturi, discuri, setări; aplicația îl folosește doar ca să citească."),
        new("ldap", "LDAP", "Interogarea domeniului", "Directory query",
            "Felul în care un program cere de la serverul de domeniu lista conturilor și a grupurilor; folosit doar în ediția de rețea."),
        new("utc", "UTC", "Ora universală", "Universal time",
            "Ora de referință mondială, fără oră de vară; România este UTC+2 iarna și UTC+3 vara. Rapoartele păstrează ora UTC ca să poată fi comparate între stații."),
        new("dns", "DNS", "Traducerea numelor de site-uri în adrese", "Name resolution",
            "Serviciul care transformă un nume (de exemplu exemplu.ro) în adresa de rețea a serverului; o cerere DNS arată că stația a încercat să ajungă la acel nume."),
        new("mitre", "MITRE ATT&CK", "Catalogul tehnicilor de atac", "Attack technique catalogue",
            "Lista publică a tehnicilor folosite de atacatori, fiecare cu un cod (de exemplu T1059); aplicația o folosește ca să numească la fel ce găsește."),
        new("sam", "SAM", "Baza conturilor locale", "Local account database",
            "Fișierul Windows în care sunt păstrate conturile locale ale calculatorului și amprentele parolelor lor."),
        new("event_id", "Event ID", "Codul evenimentului", "Event code",
            "Numărul prin care Windows spune ce fel de eveniment a notat (de exemplu 4624 = autentificare reușită, 1102 = jurnalul de securitate a fost șters).",
            ["EID"]),
        new("vss", "VSS", "Copiile de umbră ale discului", "Shadow copies",
            "Copiile automate ale discului pe care Windows le face pentru restaurare; ștergerea lor este un semn frecvent de atac sau de ascundere a urmelor."),
    ];

    /// <summary>
    /// Words shown on screen and in the reports as they are (they are already Romanian or are the established label), but which a controller may not know:
    /// the results of a check, the severity words and a few administrative and hardware words. They are explained, never replaced, so they are not jargon.
    /// </summary>
    public static IReadOnlyList<GlossaryTerm> Explained { get; } =
    [
        new("status_conform", "CONFORM", "Corespunde regulii", "Compliant",
            "Ce s-a verificat corespunde regulii din procedură. Nu este nevoie de nicio acțiune.", ["Conform"]),
        new("status_neconform", "NECONFORM", "Încalcă regula", "Non-compliant",
            "Ce s-a găsit încalcă regula. Se trece în procesul-verbal și se cere remedierea.", ["Neconform"]),
        new("status_de_verificat", "DE VERIFICAT", "Trebuie comparat cu documentele", "To verify",
            "Aplicația arată faptele, dar doar organizația poate spune dacă erau aprobate. Comparați cu registrele și aprobările scrise.", ["DeVerificat", "De verificat"]),
        new("status_nedeterminat", "NEDETERMINAT", "Nu s-a putut verifica", "Undetermined",
            "Sursa necesară nu a putut fi citită (de exemplu aplicația nu a rulat ca administrator). Nu înseamnă nici bine, nici rău și nu înseamnă că activitatea nu a avut loc.", ["Nedeterminat"]),
        new("status_nu_se_aplica", "NU SE APLICĂ", "Regula nu privește această ediție", "Not applicable",
            "Regula nu privește postura acestei ediții (de exemplu o regulă de rețea pe o stație izolată). Nu se numără nici ca bine, nici ca abatere.", ["NuSeAplica", "NU SE APLICA", "Nu se aplică"]),
        new("status_neconfirmat", "NECONFIRMAT", "Nu s-a putut confirma aici", "Unconfirmed",
            "Verificarea nu s-a putut face complet pe această stație (de exemplu lipsește lista de revocare a autorității). Nu este o problemă găsită, dar nici o confirmare."),
        new("severity_critical", "CRITICĂ", "Gravitate foarte mare", "Critical",
            "Cea mai mare gravitate: semnul unui atac sau al unei încălcări care trebuie tratată imediat, înainte de orice altă lucrare.", ["CRITIC", "Critică", "Critical", "GRAV", "Atenție critică"]),
        new("severity_high", "RIDICATĂ", "Gravitate mare", "High",
            "Problemă serioasă care trebuie tratată în ziua controlului; poate ajuta un atacator sau poate ascunde urme.", ["RIDICAT", "Ridicată", "High"]),
        new("severity_medium", "MEDIE", "Gravitate medie", "Medium",
            "Problemă care slăbește protecția stației; se remediază în termenul stabilit de procedură.", ["MEDIU", "Medie", "Medium"]),
        new("severity_low", "SCĂZUTĂ", "Gravitate mică", "Low",
            "Abatere minoră sau un lucru de urmărit; nu cere măsuri imediate.", ["SCĂZUT", "Scăzută", "Low"]),
        new("severity_info", "INFORMATIVĂ", "Doar informație", "Informational",
            "Nu este o problemă: un fapt notat pentru context (de exemplu ce programe sunt instalate).", ["INFORMATIV", "Informativă", "Info"]),
        new("proces_verbal", "proces-verbal", "Documentul oficial al controlului", "Inspection record",
            "Documentul pe hârtie semnat de cel care controlează și de ofițerul de securitate; pe el se trec amprenta raportului și codul de ancorare.", ["procesul-verbal", "procesului-verbal", "Proces-verbal"]),
        new("usb", "USB", "Mufa pentru memorii și dispozitive externe", "USB port",
            "Mufa prin care se conectează memorii (stick-uri), discuri externe, telefoane sau tastaturi; un stick USB poate scoate informații de pe stație."),
        new("pin", "PIN", "Codul secret al cardului", "Card PIN",
            "Codul cardului ofițerului sau al operatorului; îl cere Windows, aplicația nu îl vede și nu îl păstrează."),
        new("sid", "SID", "Numărul unic al contului", "Account identifier",
            "Numărul pe care Windows îl dă fiecărui cont și grup; rămâne același chiar dacă numele contului se schimbă (de exemplu contul Administrator are mereu un SID care se termină în -500)."),
        new("hash_fingerprint", "amprentă SHA-256", "Amprenta unui fișier", "File fingerprint",
            "Un cod de 64 de caractere calculat din conținutul unui fișier; se schimbă dacă fișierul se schimbă cu un singur caracter. Pe procesul-verbal se trece amprenta raportului.",
            ["amprenta SHA-256", "Amprenta fișierului (SHA-256)", "amprenta fișierului"]),
    ];

    /// <summary>The jargon terms and the explained words: what a tooltip, a „?” button or a report appendix may look up.</summary>
    public static IEnumerable<GlossaryTerm> All => Terms.Concat(Explained);

    /// <summary>The entry (jargon or explained word) whose key, technical name or alias matches; null when there is none.</summary>
    public static GlossaryTerm? Lookup(string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return null;
        var n = Norm(term);
        return All.FirstOrDefault(t => Norm(t.Key) == n || t.Spellings.Any(s => Norm(s) == n));
    }

    /// <summary>The entry whose key or technical name matches (case-insensitive; spaces and underscores are equivalent), or null.</summary>
    public static GlossaryTerm? Find(string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return null;
        var n = Norm(term);
        return Terms.FirstOrDefault(t => Norm(t.Key) == n || t.Spellings.Any(s => Norm(s) == n));
    }

    private static string Norm(string s) => s.Trim().Replace('_', ' ').ToLowerInvariant();

    /// <summary>The human phrase in the chosen language; the term itself when it is not in the table.</summary>
    public static string Human(string term, GlossaryLanguage lang = GlossaryLanguage.Romanian) =>
        Find(term) is { } t ? (lang == GlossaryLanguage.English ? t.English : t.Romanian) : term;

    /// <summary>Normal views: the human phrase. Advanced/technical views: the technical name with the human phrase beside it.</summary>
    public static string Display(string term, bool advanced, GlossaryLanguage lang = GlossaryLanguage.Romanian) =>
        Find(term) is not { } t ? term : advanced ? $"{t.Technical} ({Human(term, lang)})" : Human(term, lang);

    /// <summary>Tooltip text: technical name, human phrase and the short explanation. Empty for a term that is not in the table.</summary>
    public static string Tooltip(string term, GlossaryLanguage lang = GlossaryLanguage.Romanian) =>
        Find(term) is { } t ? $"{t.Technical} — {Human(term, lang)}. {t.Explanation}" : "";
}
