using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// One technical code in plain words, for a controller who is not an IT specialist (owner request 2026-10-11: „detaliile codurilor pe toate
/// domeniile ... coduri de identificare Windows EVTX, regiștri etc."). <see cref="What"/> = ce este, <see cref="Why"/> = de ce contează la control,
/// <see cref="When"/> = când apare / ce înseamnă valoarea. Meanings are documented Microsoft semantics; none of them asserts wrongdoing.
/// </summary>
public sealed record CodeExplanation(string Code, string Kind, string What, string Why, string When)
{
    public string Text => $"{Code} ({Kind}): {What} De ce contează: {Why} {When}".Trim();
}

/// <summary>
/// Decodes the codes that appear in control results and evidence lines: Windows event IDs (per log), registry keys and values, advanced-audit
/// subcategory GUIDs and names, well-known SIDs, logon types, audit-change codes (%%84xx) and the station's own setting names.
/// Unknown codes are never guessed: <see cref="FindIn"/> simply does not list them.
/// </summary>
public static class CodeExplainer
{
    private static CodeExplanation E(string code, string kind, string what, string why, string when = "") => new(code, kind, what, why, when);

    // ---- Windows events (log, id) --------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<(string Log, int Id), CodeExplanation> Events = new()
    {
        [("Security", 1102)] = E("Security 1102", "eveniment Windows", "Jurnalul Security (registrul de securitate al stației) a fost golit de cineva.",
            "După golire, tot ce s-a întâmplat înainte pe stație nu mai poate fi verificat. Este una dintre cele mai importante urme la un control.",
            "Apare doar când cineva golește jurnalul manual sau printr-un program; câmpul „de” arată contul care a făcut-o."),
        [("Security", 4616)] = E("Security 4616", "eveniment Windows", "Ora sistemului a fost schimbată.",
            "Cu ora schimbată, orele din toate jurnalele devin greșite; poate fi folosită pentru a ascunde când s-a întâmplat ceva.",
            "Windows își corectează singur ora (svchost.exe, cont SYSTEM) — acelea nu sunt listate. Listate sunt doar schimbările făcute de o persoană."),
        [("Security", 4624)] = E("Security 4624", "eveniment Windows", "Cineva s-a autentificat cu succes pe stație.",
            "Arată cine a lucrat pe stație și cum (la tastatură, de la distanță, deblocare ecran).", "Tipul de logon spune cum: 2 = la tastatură, 10 = de la distanță (RDP), 7 = deblocare ecran."),
        [("Security", 4625)] = E("Security 4625", "eveniment Windows", "O încercare de autentificare a EȘUAT (parolă greșită, cont inexistent sau blocat).",
            "Multe eșecuri la rând pot însemna că cineva încearcă să ghicească parola.", "Câteva eșecuri izolate sunt de obicei parole tastate greșit."),
        [("Security", 4672)] = E("Security 4672", "eveniment Windows", "O sesiune a primit drepturi speciale (de administrator).",
            "Arată cine a lucrat cu drepturi de administrator, adică putea schimba orice pe stație.", "Apare la fiecare autentificare a unui cont de administrator."),
        [("Security", 4698)] = E("Security 4698", "eveniment Windows", "A fost creat un task programat (o comandă care pornește singură la o oră sau la pornire).",
            "Task-urile programate sunt o metodă frecventă prin care un program rău-intenționat repornește singur.", "Programele legitime (actualizări) creează și ele task-uri; comparați numele cu software-ul aprobat."),
        [("Security", 4719)] = E("Security 4719", "eveniment Windows", "Politica de audit a fost modificată (ce anume înregistrează stația în jurnal).",
            "Cine reduce auditul poate face lucruri care nu mai lasă urme.", "Codul GUID arată ce categorie s-a schimbat; %%8449/%%8451 = s-a activat, %%8448/%%8450 = s-a dezactivat."),
        [("Security", 4720)] = E("Security 4720", "eveniment Windows", "A fost creat un cont de utilizator nou.", "Orice cont nou trebuie să aibă aprobare.", ""),
        [("Security", 4722)] = E("Security 4722", "eveniment Windows", "Un cont de utilizator a fost activat.", "Un cont dezactivat reactivat poate fi folosit din nou pentru acces.", ""),
        [("Security", 4723)] = E("Security 4723", "eveniment Windows", "Un utilizator a încercat să-și schimbe propria parolă.", "Este o operație normală; contează doar dacă nu se potrivește cu persoana.", ""),
        [("Security", 4724)] = E("Security 4724", "eveniment Windows", "Parola altui cont a fost resetată (de obicei de un administrator).",
            "Cine resetează parola altcuiva poate intra în contul acelei persoane.", ""),
        [("Security", 4725)] = E("Security 4725", "eveniment Windows", "Un cont de utilizator a fost dezactivat.", "Normal la plecarea unei persoane; de verificat dacă nu era prevăzut.", ""),
        [("Security", 4726)] = E("Security 4726", "eveniment Windows", "Un cont de utilizator a fost șters.", "Ștergerea unui cont poate ascunde cine a lucrat pe stație.", ""),
        [("Security", 4728)] = E("Security 4728", "eveniment Windows", "Un membru a fost adăugat într-un grup global de securitate (de domeniu).", "Grupurile dau drepturi; adăugarea trebuie aprobată.", ""),
        [("Security", 4729)] = E("Security 4729", "eveniment Windows", "Un membru a fost eliminat dintr-un grup global de securitate.", "Schimbă drepturile contului.", ""),
        [("Security", 4732)] = E("Security 4732", "eveniment Windows", "Un membru a fost adăugat într-un grup local (de exemplu Administrators).",
            "Adăugarea în Administrators dă control total asupra stației.", "SID-ul S-1-5-32-544 înseamnă grupul Administrators."),
        [("Security", 4733)] = E("Security 4733", "eveniment Windows", "Un membru a fost eliminat dintr-un grup local.", "Schimbă drepturile contului.", ""),
        [("Security", 4738)] = E("Security 4738", "eveniment Windows", "Un cont de utilizator a fost modificat (de exemplu: parola nu mai expiră).", "Unele modificări slăbesc protecția contului.", ""),
        [("Security", 4740)] = E("Security 4740", "eveniment Windows", "Un cont a fost blocat după prea multe parole greșite.", "Poate însemna o încercare de ghicire a parolei sau o parolă uitată.", ""),
        [("Security", 4756)] = E("Security 4756", "eveniment Windows", "Un membru a fost adăugat într-un grup universal.", "Grupurile dau drepturi; adăugarea trebuie aprobată.", ""),
        [("Security", 4757)] = E("Security 4757", "eveniment Windows", "Un membru a fost eliminat dintr-un grup universal.", "Schimbă drepturile contului.", ""),
        [("Security", 6416)] = E("Security 6416", "eveniment Windows", "Windows a recunoscut un dispozitiv extern nou (de exemplu un stick USB).",
            "Arată când a fost conectat un dispozitiv și ce fel de dispozitiv era.", "Apare doar dacă auditul „Plug and Play Events” este activ."),
        [("System", 104)] = E("System 104", "eveniment Windows", "Un jurnal de evenimente (altul decât Security sau chiar System) a fost golit.", "La fel ca la 1102: urmele de dinainte se pierd.", ""),
        [("System", 1074)] = E("System 1074", "eveniment Windows", "Cineva sau un program a oprit sau a repornit stația.", "Arată cine a oprit stația și motivul declarat.", ""),
        [("System", 7045)] = E("System 7045", "eveniment Windows", "Un serviciu nou (program care rulează permanent în fundal) a fost instalat.",
            "Serviciile noi sunt o metodă frecventă de a păstra un program rău-intenționat activ după repornire.", "Calea executabilului (ImagePath) arată ce program rulează."),
        [("Application", 11707)] = E("Application 11707", "eveniment Windows", "Windows Installer: un program a fost instalat cu succes.", "Software-ul instalat trebuie să fie pe lista aprobată.", ""),
        [("Application", 11724)] = E("Application 11724", "eveniment Windows", "Windows Installer: un program a fost dezinstalat cu succes.", "Dezinstalarea poate ascunde un program folosit anterior.", ""),
        [("Application", 1033)] = E("Application 1033", "eveniment Windows", "Windows Installer: instalare încheiată (cu codul de rezultat).", "Software-ul instalat trebuie să fie pe lista aprobată.", "Codul de stare 0 = reușit."),
        [("Application", 1034)] = E("Application 1034", "eveniment Windows", "Windows Installer: dezinstalare încheiată.", "Dezinstalarea poate ascunde un program folosit anterior.", ""),
        [("Microsoft-Windows-PowerShell/Operational", 4104)] = E("PowerShell 4104", "eveniment Windows", "Textul unui script PowerShell care a rulat pe stație.",
            "PowerShell este folosit și de atacatori; anumite comenzi (descărcare, cod ascuns în Base64, dezactivarea antivirusului) merită verificate.", "Apare doar dacă „Script Block Logging” este activ."),
        [("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", 21)] = E("TerminalServices 21", "eveniment Windows", "S-a deschis o sesiune de lucru; adresa arată de unde (LOCAL = la tastatură).",
            "O adresă de rețea înseamnă lucru de la distanță (Remote Desktop).", ""),
        [("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", 25)] = E("TerminalServices 25", "eveniment Windows", "O sesiune de la distanță a fost reconectată.", "Arată lucru de la distanță pe stație.", ""),
        [("Microsoft-Windows-Partition/Diagnostic", 1006)] = E("Partition 1006", "eveniment Windows", "Un disc a fost conectat la stație; jurnalul păstrează producătorul, modelul, seria și capacitatea.",
            "Este cea mai bună urmă a unui stick USB sau disc extern conectat, cu numărul de serie de comparat cu registrul suporturilor.", "BusType 7 = USB, 12 = card SD, 13 = card MMC."),
        [("Microsoft-Windows-NetworkProfile/Operational", 10000)] = E("NetworkProfile 10000", "eveniment Windows", "Stația s-a conectat la o rețea (numele rețelei este în eveniment).",
            "Pe o stație izolată, orice conectare la rețea este o abatere.", ""),
        [("Microsoft-Windows-WLAN-AutoConfig/Operational", 8001)] = E("WLAN 8001", "eveniment Windows", "Stația s-a conectat la o rețea fără fir (Wi-Fi); SSID = numele rețelei.",
            "Pe o stație izolată, o conectare Wi-Fi este o abatere.", ""),
    };

    private static readonly Dictionary<string, string> ChannelAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PowerShell"] = "Microsoft-Windows-PowerShell/Operational", ["TerminalServices"] = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational",
        ["Partition"] = "Microsoft-Windows-Partition/Diagnostic", ["NetworkProfile"] = "Microsoft-Windows-NetworkProfile/Operational",
        ["WLAN"] = "Microsoft-Windows-WLAN-AutoConfig/Operational",
        ["TerminalServices-LocalSessionManager"] = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", ["Partition/Diagnostic"] = "Microsoft-Windows-Partition/Diagnostic",
        ["WLAN-AutoConfig"] = "Microsoft-Windows-WLAN-AutoConfig/Operational",
    };

    /// <summary>The plain explanation of an event, or null when this log/id pair is not in the catalogue (never guessed).</summary>
    public static CodeExplanation? Event(string log, int id) =>
        Events.TryGetValue((ChannelAliases.GetValueOrDefault(log, log), id), out var e) ? e : null;

    // ---- registry, settings, services ----------------------------------------------------------------------------------------------------

    private static readonly CodeExplanation[] RegistryValues =
    [
        E(@"Policies\System\EnableLUA", "valoare de registru", "Pornește sau oprește UAC (fereastra care cere confirmare înainte ca un program să primească drepturi de administrator).",
            "Cu UAC oprit, orice program pornit de un administrator are control total, fără întrebare.", "1 = activ (corect), 0 = oprit."),
        E(@"Policies\System\ConsentPromptBehaviorAdmin", "valoare de registru", "Cum întreabă UAC un administrator înainte de a da drepturi complete.",
            "0 înseamnă că nu se mai întreabă nimic.", "0 = fără întrebare, 1/3 = cere parola, 2/4 = cere confirmare, 5 = cere confirmare doar pentru programe din afara Windows (implicit)."),
        E(@"Winlogon\AutoAdminLogon", "valoare de registru", "Pornire automată în cont, fără să se tasteze parola.", "Oricine pornește stația intră în cont.", "1 = activă (abatere), 0 sau lipsă = dezactivată."),
        E(@"Winlogon\DefaultPassword", "valoare de registru", "Parolă salvată în registru pentru pornirea automată.", "Parola stă în clar și poate fi citită de oricine are acces la registru.", "Aplicația verifică doar dacă există, nu o citește."),
        E(@"Terminal Server\fDenyTSConnections", "valoare de registru", "Permite sau interzice conectarea de la distanță (Remote Desktop).", "Remote Desktop deschis permite lucrul pe stație din altă parte.", "1 = interzis (corect pe stația izolată), 0 = permis."),
        E(@"LanmanServer\Parameters\SMB1", "valoare de registru", "Versiunea veche (1) a protocolului de partajare a fișierelor.", "SMBv1 are vulnerabilități cunoscute folosite de viruși (de exemplu WannaCry).", "0 sau lipsă = dezactivat."),
        E(@"Services\mrxsmb10\Start", "valoare de registru", "Pornirea componentei client SMBv1.", "Vezi SMB1.", "4 = dezactivat; 2 sau 3 = poate porni."),
        E(@"Services\USBSTOR\Start", "valoare de registru", "Pornirea componentei Windows care citește stick-urile și discurile USB.",
            "Dacă este oprită, stick-urile USB nu mai pot fi folosite pe stație.", "4 = blocat (stick-urile nu funcționează), 3 = permis (implicit)."),
        E(@"RemovableStorageDevices\Deny_All", "valoare de registru", "Politica „Toate clasele de stocare amovibilă: interzice orice acces”.", "Blochează toate suporturile amovibile.", "1 = blocat."),
        E(@"WDigest\UseLogonCredential", "valoare de registru", "Dacă Windows păstrează parolele în memorie în formă care poate fi citită.",
            "Cu valoarea 1, un program rău-intenționat poate extrage parolele în clar (tehnică Mimikatz).", "0 sau lipsă = sigur (implicit pe Windows 10/11)."),
        E(@"Lsa\RunAsPPL", "valoare de registru", "Protecția procesului care păstrează parolele (LSA).", "Îngreunează furtul parolelor din memorie.", "1 sau 2 = protejat."),
        E(@"Services\WlanSvc\Start", "valoare de registru", "Modul de pornire al serviciului Wi-Fi (WLAN AutoConfig).", "Fără serviciu pornit, stația nu se poate conecta la rețele fără fir.", "4 = dezactivat, 3 = la cerere, 2 = automat; lipsa înseamnă că serviciul nu există."),
        E(@"Services\bthserv\Start", "valoare de registru", "Modul de pornire al serviciului Bluetooth.", "Bluetooth permite trimiterea de fișiere fără cablu.", "4 = dezactivat, 3 = la cerere, 2 = automat."),
        E(@"Services\AppIDSvc\Start", "valoare de registru", "Modul de pornire al serviciului care aplică lista albă de programe (AppLocker).", "Dacă este dezactivat, regulile AppLocker nu mai sunt aplicate.", "4 = dezactivat, 3 = la cerere (normal), 2 = automat."),
        E(@"State\UEFISecureBootEnabled", "valoare de registru", "Dacă pornirea securizată (Secure Boot) este activă.", "Fără ea, stația poate fi pornită de pe un stick cu alt sistem de operare.", "1 = activă, 0 = oprită; lipsa cheii înseamnă de obicei BIOS vechi sau stație fără UEFI."),
        E(@"Control\PEFirmwareType", "valoare de registru", "Tipul de firmware al plăcii de bază.", "BIOS-ul vechi nu are protecțiile pornirii moderne.", "1 = BIOS vechi (Legacy), 2 = UEFI (modern)."),
        E(@"System\InactivityTimeoutSecs", "valoare de registru", "După câte secunde de inactivitate se blochează ecranul.", "Ecranul deschis lângă o stație nesupravegheată poate fi folosit de oricine.", "Cel mult 900 (15 minute) este corect; 0 = dezactivat."),
        E(@"SrpV2\EnforcementMode", "valoare de registru", "Modul unei colecții de reguli AppLocker (lista albă de programe).", "Doar modul de blocare oprește programele neaprobate.", "1 = blochează, 0 = doar notează în jurnal (audit)."),
        E(@"Explorer\NoDriveTypeAutoRun", "valoare de registru", "Pentru ce tipuri de unități este oprită pornirea automată.", "Un stick poate porni singur un program la introducere.", "255 = toate unitățile; trebuie să includă cel puțin suporturile amovibile (4) și CD-urile (32)."),
        E(@"Explorer\NoAutorun", "valoare de registru", "Oprește comenzile de pornire automată din fișierul autorun.inf.", "Fișierul autorun.inf al unui stick poate porni un program.", "1 = oprit (corect), 0 sau lipsă = permis."),
        E(@"CurrentVersion\DisplayVersion", "valoare de registru", "Versiunea Windows pe înțelesul utilizatorilor (de exemplu 24H2).", "Arată cât de nouă este versiunea și dacă mai primește actualizări.", "Se compară cu tabelul versiunilor suportate al organizației."),
        E(@"CurrentVersion\CurrentBuild", "valoare de registru", "Numărul de build al Windows.", "Identifică exact versiunea; 22000 sau mai mult înseamnă Windows 11.", "Numele produsului din registru poate spune „Windows 10” și pe Windows 11."),
        E(@"CurrentVersion\UBR", "valoare de registru", "Numărul de revizie al actualizărilor instalate peste build.", "Crește cu fiecare actualizare lunară instalată.", "Se citește împreună cu build-ul: 26100.1234 înseamnă revizia 1234."),
        E(@"webcam\Value", "valoare de registru", "Permisiunea implicită de acces la cameră pentru toate aplicațiile.", "O cameră permisă poate face imagini din încăpere.", "Deny = blocat, Allow = permis."),
        E(@"microphone\Value", "valoare de registru", "Permisiunea implicită de acces la microfon pentru toate aplicațiile.", "Un microfon permis poate înregistra conversații.", "Deny = blocat, Allow = permis."),
        E(@"WindowsAI\DisableAIDataAnalysis", "valoare de registru", "Politica care oprește funcția Recall (poze periodice ale ecranului).", "Recall păstrează pe disc imagini ale ecranului, deci copii ale documentelor.", "1 = oprit (corect), 0 = permis."),
        E(@"WindowsAI\AllowRecallEnablement", "valoare de registru", "Politica care permite sau nu activarea funcției Recall.", "Vezi DisableAIDataAnalysis.", "0 = indisponibilă (corect), 1 = poate fi activată."),
        E(@"WindowsCopilot\TurnOffWindowsCopilot", "valoare de registru", "Politica care oprește asistentul Windows Copilot.", "Copilot poate trimite conținutul ecranului către un serviciu din afară.", "1 = oprit (corect), 0 = permis."),
        E(@"ScriptBlockLogging\EnableScriptBlockLogging", "valoare de registru", "Înregistrarea textului scripturilor PowerShell în jurnal.", "Fără ea, un script rău-intenționat nu lasă urme.", "1 = pornită (corect), lipsa sau 0 = oprită."),
        E(@"Transcription\EnableTranscripting", "valoare de registru", "Salvarea unei transcrieri a fiecărei sesiuni PowerShell.", "Păstrează tot ce s-a tastat și tot ce s-a afișat.", "1 = pornită (corect)."),
        E(@"PowerShellEngine\PowerShellVersion", "valoare de registru", "Versiunea motorului PowerShell găsit sub această cheie.", "Versiunea 2.0 nu înregistrează scripturile și poate ocoli protecțiile noi.", "2.0 = versiunea veche prezentă; lipsa cheii = nu s-a găsit."),
        E(@"Memory Management\ClearPageFileAtShutdown", "valoare de registru", "Dacă fișierul de paginare (memoria virtuală de pe disc) se șterge la oprire.", "Poate conține fragmente de documente deschise.", "1 = se șterge (corect pentru stații clasificate), 0 = rămâne pe disc."),
        E(@"Power\HibernateEnabled", "valoare de registru", "Dacă hibernarea este permisă.", "Hibernarea scrie pe disc tot conținutul memoriei, inclusiv documentele deschise.", "0 = oprită, 1 = permisă."),
        E(@"CrashControl\CrashDumpEnabled", "valoare de registru", "Ce copie a memoriei salvează Windows la o cădere a sistemului.", "Copia poate conține date din documente deschise.", "0 = nimic, 1 = completă, 2 = nucleu, 3 = mică, 7 = automată."),
        E(@"System\LegalNoticeCaption", "valoare de registru", "Titlul mesajului de avertizare afișat înainte de autentificare.", "Anunță utilizatorul că stația tratează informații clasificate.", "Trebuie să corespundă marcajului de clasificare aprobat."),
        E(@"System\LegalNoticeText", "valoare de registru", "Textul mesajului de avertizare afișat înainte de autentificare.", "Vezi LegalNoticeCaption.", "Trebuie să indice nivelul maxim de clasificare."),
        E(@"Storage\HotPlugSecureOpen", "valoare de registru", "Setare folosită de unele versiuni de Windows pentru a înregistra accesul la fișiere pe suporturi amovibile.", "Fără ea, auditarea pe suporturi amovibile poate să nu noteze fișierele copiate.", "1 = pornită; lipsa înseamnă comportamentul implicit al versiunii."),
        E(@"Parameters\NtpServer", "valoare de registru", "Serverul de timp configurat pentru ceasul stației.", "Ora din jurnale este de încredere doar dacă ceasul este sincronizat cu o sursă aprobată.", "Lipsa înseamnă că niciun server nu este configurat."),
        E(@"Parameters\Type", "valoare de registru", "Tipul de sincronizare a ceasului stației.", "Un ceas lăsat liber se abate în timp.", "NTP = server de timp, NT5DS = domeniu, NoSync = fără sincronizare."),
        E(@"Enum\USBSTOR", "cheie de registru", "Lista tuturor dispozitivelor de stocare USB conectate vreodată la stație, cu seria lor.",
            "Arată ce stick-uri au fost folosite, chiar dacă jurnalele s-au pierdut.", "Ștergerea acestei liste este o urmă de ascundere."),
        E(@"NetworkList\Profiles", "cheie de registru", "Lista rețelelor la care stația s-a conectat vreodată, cu data primei și ultimei conectări.",
            "Pe o stație izolată arată dacă a fost vreodată conectată la o rețea.", "Profilurile pot fi șterse manual; lipsa lor nu dovedește nimic."),
    ];

    private static readonly Dictionary<string, CodeExplanation> Settings = new(StringComparer.Ordinal)
    {
        ["PASSWD_NOTREQD"] = E("PASSWD_NOTREQD", "indicator de cont", "Contul are voie să aibă parolă goală.", "Un cont fără parolă poate fi folosit de oricine.", "Indicatorul nu dovedește că parola chiar este goală; se verifică."),
        ["ExclusionPath"] = E("ExclusionPath", "setare Defender", "Un folder pe care antivirusul NU îl verifică.", "Atacatorii își pun programele în foldere excluse ca să nu fie detectate.", ""),
        ["ExclusionProcess"] = E("ExclusionProcess", "setare Defender", "Un program pe care antivirusul NU îl verifică.", "Programul exclus poate face orice fără să fie oprit de antivirus.", ""),
        ["ExclusionExtension"] = E("ExclusionExtension", "setare Defender", "Un tip de fișier pe care antivirusul NU îl verifică.", "Toate fișierele de acel tip scapă de verificare.", ""),
        ["protecție la modificare"] = E("Protecție la modificare", "setare Defender", "Împiedică oprirea antivirusului de către programe.", "Fără ea, un program rău-intenționat poate opri Defender.", "True = activă."),
        ["Domain: "] = E("Domain / Private / Public", "profil firewall", "Cele trei profiluri ale firewall-ului Windows: rețea de domeniu, rețea privată, rețea publică.", "Firewall-ul trebuie să fie activ pe toate, altfel stația e expusă când ajunge în rețeaua acelui profil.", ""),
        ["mod Circular"] = E("Circular", "mod jurnal", "Când jurnalul se umple, evenimentele cele mai vechi se suprascriu.", "Cu un jurnal mic, urmele dispar în câteva zile.", "AutoBackup = se arhivează înainte de suprascriere; Retain = nu se suprascrie."),
        // the technical sources named in the check guides
        ["auditpol"] = E("auditpol", "comandă Windows", "Comanda Windows care arată ce categorii de activitate înregistrează stația în jurnalul Security.", "Este sursa oficială a politicii de audit efective.", "Necesită drepturi de administrator."),
        ["Win32_UserAccount"] = E("Win32_UserAccount", "interogare Windows (WMI)", "Lista conturilor locale ale stației, cu starea lor (activ, dezactivat, parolă obligatorie, expirare).", "Este lista reală a conturilor, chiar dacă unele sunt ascunse din ecranul de pornire.", ""),
        ["Win32_Group"] = E("Win32_Group", "interogare Windows (WMI)", "Grupurile locale ale stației și membrii lor.", "Arată cine are drepturi de administrator sau de acces la distanță.", ""),
        ["NetUserModalsGet"] = E("NetUserModalsGet", "funcție Windows", "Citește politica de parole a stației (aceeași pe care o arată comanda „net accounts”).", "Arată regulile efective pentru parole și blocarea conturilor.", ""),
        ["MSFT_MpComputerStatus"] = E("MSFT_MpComputerStatus", "interogare Microsoft Defender", "Starea antivirusului Defender: pornit, protecție în timp real, data semnăturilor.", "Arată dacă stația este protejată acum.", "Dacă există alt antivirus, Defender poate apărea oprit; se verifică vizual."),
        ["MSFT_MpPreference"] = E("MSFT_MpPreference", "interogare Microsoft Defender", "Setările Defender, inclusiv excluderile.", "Excluderile sunt locuri pe care antivirusul nu le verifică.", ""),
        ["HNetCfg.FwPolicy2"] = E("HNetCfg.FwPolicy2", "interfață Windows", "Starea firewall-ului Windows pe fiecare profil de rețea.", "Arată dacă firewall-ul este pornit.", ""),
        ["Win32_EncryptableVolume"] = E("Win32_EncryptableVolume", "interogare BitLocker", "Starea criptării BitLocker a discului.", "Arată dacă datele sunt protejate dacă discul este scos din stație.", "ProtectionStatus 1 = protejat, 0 = neprotejat."),
        ["EventLogConfiguration"] = E("Configurația jurnalului", "setare Windows", "Dimensiunea maximă a jurnalului și ce face când se umple.", "Un jurnal mic pierde repede evenimentele vechi.", ""),
    };

    // ---- audit, SIDs, codes ----------------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, string> AuditRo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Logon"] = "autentificările pe stație (cine a intrat, cum, de unde)",
        ["Logoff"] = "ieșirile din sesiune",
        ["Account Lockout"] = "blocările de cont după parole greșite",
        ["Special Logon"] = "autentificările cu drepturi de administrator",
        ["Other Logon/Logoff Events"] = "blocarea/deblocarea ecranului, reconectări",
        ["User Account Management"] = "crearea, ștergerea și modificarea conturilor",
        ["Security Group Management"] = "schimbările în grupuri (de exemplu cine devine administrator)",
        ["Audit Policy Change"] = "schimbările politicii de audit înseși",
        ["Process Creation"] = "fiecare program pornit pe stație",
        ["Removable Storage"] = "citirea și scrierea fișierelor pe stick-uri și discuri externe",
        ["Plug and Play Events"] = "conectarea dispozitivelor externe",
        ["Security State Change"] = "pornirea și oprirea sistemului, schimbarea orei",
        ["Other Object Access Events"] = "task-urile programate și alte obiecte",
        ["Credential Validation"] = "verificarea parolelor (inclusiv pentru conturi de domeniu)",
        ["File System"] = "accesul la fișierele marcate pentru audit",
        ["Registry"] = "accesul la cheile de registru marcate pentru audit",
        ["Sensitive Privilege Use"] = "folosirea drepturilor sensibile (de exemplu preluarea fișierelor altcuiva)",
        ["Other System Events"] = "evenimente ale serviciului de jurnal",
        ["System Integrity"] = "probleme de integritate a sistemului de audit",
        ["File Share"] = "accesul la folderele partajate în rețea",
        ["Filtering Platform Connection"] = "conexiunile de rețea permise sau blocate",
        ["MPSSVC Rule-Level Policy Change"] = "schimbările regulilor firewall-ului",
        ["Authentication Policy Change"] = "schimbările politicilor de autentificare",
    };

    private static readonly Dictionary<string, (string Name, string Meaning)> Sids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-5-18"] = ("SYSTEM", "contul sistemului de operare însuși"), ["S-1-5-19"] = ("LOCAL SERVICE", "cont de serviciu Windows"),
        ["S-1-5-20"] = ("NETWORK SERVICE", "cont de serviciu Windows"), ["S-1-5-32-544"] = ("Administrators", "grupul administratorilor locali: control total"),
        ["S-1-5-32-545"] = ("Users", "grupul utilizatorilor obișnuiți"), ["S-1-5-32-546"] = ("Guests", "grupul vizitatorilor"),
        ["S-1-5-32-555"] = ("Remote Desktop Users", "cine are voie să lucreze de la distanță"), ["S-1-5-7"] = ("ANONYMOUS LOGON", "acces fără cont"),
        ["S-1-1-0"] = ("Everyone", "toată lumea"),
    };

    private static readonly Dictionary<string, string> AuditChangeCodes = new()
    {
        ["%%8448"] = "auditarea reușitelor a fost DEZACTIVATĂ", ["%%8449"] = "auditarea reușitelor a fost activată",
        ["%%8450"] = "auditarea eșecurilor a fost DEZACTIVATĂ", ["%%8451"] = "auditarea eșecurilor a fost activată",
    };

    private static readonly Dictionary<Guid, string> AuditByGuid = AuditSubcategories.ByName.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static CodeExplanation? AuditSubcategory(string nameOrGuid)
    {
        string? name = Guid.TryParse(nameOrGuid.Trim('{', '}'), out var g) ? AuditByGuid.GetValueOrDefault(g) : AuditSubcategories.ByName.ContainsKey(nameOrGuid) ? nameOrGuid : null;
        if (name is null) return null;
        var canonical = AuditSubcategories.ByName.Keys.First(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
        var ro = AuditRo.GetValueOrDefault(canonical, "o categorie a auditului avansat Windows");
        return E(Guid.TryParse(nameOrGuid.Trim('{', '}'), out _) ? $"{{{g.ToString().ToUpperInvariant()}}}" : canonical, "categorie de audit",
            $"„{canonical}”: înregistrează {ro}.", "Ce nu este auditat nu lasă urme și nu poate fi verificat la control.",
            "Success = se înregistrează reușitele, Failure = eșecurile, No Auditing = nimic.");
    }

    public static CodeExplanation? Sid(string sid)
    {
        if (Sids.TryGetValue(sid, out var s)) return E(sid, "identificator de securitate (SID)", $"{s.Name}: {s.Meaning}.", "SID-ul identifică sigur contul sau grupul, chiar dacă a fost redenumit.");
        if (Regex.IsMatch(sid, @"^S-1-5-21-[\d-]+-500$")) return E(sid, "identificator de securitate (SID)", "Contul Administrator încorporat al stației (se termină în -500), chiar dacă a fost redenumit.", "Are control total; ar trebui dezactivat sau folosit doar excepțional.");
        if (Regex.IsMatch(sid, @"^S-1-5-21-[\d-]+-501$")) return E(sid, "identificator de securitate (SID)", "Contul Guest (vizitator) al stației (se termină în -501).", "Trebuie să fie dezactivat.");
        if (Regex.IsMatch(sid, @"^S-1-5-21-[\d-]+$")) return E(sid, "identificator de securitate (SID)", "Un cont sau grup creat pe stație sau în domeniu (numărul de la final îl identifică).", "Se compară cu lista conturilor cunoscute.");
        return null;
    }

    private static readonly Dictionary<string, string> LogonTypes = new()
    {
        ["2"] = "la tastatura stației", ["3"] = "din rețea (de exemplu acces la un folder partajat)", ["4"] = "task programat", ["5"] = "serviciu Windows",
        ["7"] = "deblocarea ecranului", ["8"] = "din rețea cu parola trimisă în clar", ["9"] = "cu alte credențiale (runas /netonly)",
        ["10"] = "de la distanță (Remote Desktop)", ["11"] = "cu parola păstrată local (fără server de domeniu)",
    };

    public static CodeExplanation? LogonType(string code) => LogonTypes.TryGetValue(code, out var t)
        ? E($"Tip de logon {code}", "cod de autentificare", $"Autentificare {t}.", code == "10" ? "Pe o stație izolată, lucrul de la distanță nu ar trebui să existe." : "Arată cum a intrat persoana pe stație.")
        : null;

    // ---- finding the codes in a line of text --------------------------------------------------------------------------------------------

    private static readonly Regex EventRef = new(@"(?<log>Security|System|Application|Microsoft-Windows-[\w\-]+/\w+|PowerShell|TerminalServices(?:-LocalSessionManager)?|Partition(?:/Diagnostic)?|NetworkProfile|WLAN(?:-AutoConfig)?) (?<ids>\d{1,5}(?:\s*,\s*\d{1,5})*)\b", RegexOptions.Compiled);
    private static readonly Regex GuidRef = new(@"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?", RegexOptions.Compiled);
    private static readonly Regex SidRef = new(@"S-1-\d+(?:-\d+)+", RegexOptions.Compiled);
    private static readonly Regex AuditCode = new(@"%%84(?:48|49|50|51)", RegexOptions.Compiled);
    private static readonly Regex LogonTypeRef = new(@"(?:LogonType|Tip de logon)[\s=:]+(?<t>\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Every code recognised in <paramref name="text"/> (an evidence line, a finding, a timeline source), each listed once, in order of appearance
    /// per kind. Codes not in the catalogue are not listed: the caller shows them as written, without inventing a meaning.
    /// </summary>
    public static IReadOnlyList<CodeExplanation> FindIn(string? text)
    {
        var found = new List<CodeExplanation>();
        if (string.IsNullOrWhiteSpace(text)) return found;
        void Add(CodeExplanation? e) { if (e is not null && !found.Any(f => f.Code == e.Code)) found.Add(e); }

        // "Security 4732, 4733" lists several events of one log: each is explained.
        foreach (Match m in EventRef.Matches(text))
            foreach (var id in m.Groups["ids"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                Add(Event(m.Groups["log"].Value, int.Parse(id)));
        foreach (Match m in GuidRef.Matches(text)) Add(AuditSubcategory(m.Value));
        foreach (Match m in SidRef.Matches(text)) Add(Sid(m.Value));
        foreach (Match m in AuditCode.Matches(text)) Add(E(m.Value, "cod de modificare a auditului", AuditChangeCodes[m.Value] + ".", "Dezactivarea auditului reduce urmele pe care le poate verifica un control."));
        foreach (Match m in LogonTypeRef.Matches(text)) Add(LogonType(m.Groups["t"].Value));
        foreach (var r in RegistryValues)
        {
            // "Services\USBSTOR\Start" is recognised in "...\Services\USBSTOR → Start" and in "USBSTOR.Start=4": the value name and its parent key both appear.
            var parts = r.Code.Split('\\');
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(parts[^1])}\b") && text.Contains(parts[^2], StringComparison.OrdinalIgnoreCase)) Add(r);
        }
        foreach (var (key, e) in Settings) if (text.Contains(key, StringComparison.Ordinal)) Add(e);
        foreach (var name in AuditSubcategories.ByName.Keys.Where(n => n.Length > 6 && AuditRo.ContainsKey(n)))
            if (Regex.IsMatch(text, $@"(^|[;,(]\s*|^\s*){Regex.Escape(name)}\s*[:(]", RegexOptions.IgnoreCase)) Add(AuditSubcategory(name));
        return found;
    }

    // ---- policy settings (the „Setare” column of a policy plan) ---------------------------------------------------------------------------

    private static readonly Dictionary<string, (string What, string Why)> Services = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bthserv"] = ("Serviciul Bluetooth.", "Bluetooth permite conectarea fără fir a telefoanelor și altor dispozitive; pe o stație izolată trebuie oprit."),
        ["WlanSvc"] = ("Serviciul Wi-Fi (WLAN AutoConfig).", "Fără el stația nu se poate conecta la rețele fără fir; pe o stație izolată trebuie oprit."),
        ["RemoteRegistry"] = ("Serviciul care permite citirea registrului de pe alt calculator.", "Deschide stația către acces din rețea."),
        ["TermService"] = ("Serviciul Remote Desktop.", "Permite lucrul pe stație de la distanță."),
        ["WinRM"] = ("Serviciul de administrare la distanță (Windows Remote Management).", "Permite rularea de comenzi pe stație din rețea."),
        ["Spooler"] = ("Serviciul de imprimare.", "Necesar pentru imprimare; a avut vulnerabilități grave (PrintNightmare)."),
        ["USBSTOR"] = ("Componenta care citește stick-urile USB.", "Oprită, stick-urile nu mai funcționează."),
        ["SSDPSRV"] = ("Descoperirea automată a dispozitivelor din rețea (SSDP).", "Inutil pe o stație izolată; mărește suprafața de atac."),
        ["upnphost"] = ("Găzduirea dispozitivelor UPnP.", "Inutil pe o stație izolată."),
        ["lfsvc"] = ("Serviciul de localizare geografică.", "Poate dezvălui poziția stației."),
        ["MapsBroker"] = ("Serviciul pentru hărți descărcate.", "Inutil pe o stație izolată."),
        ["XblGameSave"] = ("Serviciu Xbox (jocuri).", "Inutil pe o stație de lucru."),
        ["icssvc"] = ("Hotspot mobil Windows (partajarea conexiunii).", "Ar transforma stația într-un punct de acces la rețea."),
        ["WMPNetworkSvc"] = ("Partajarea în rețea a Windows Media Player.", "Inutil; deschide stația către rețea."),
    };

    private static readonly Dictionary<string, (string What, string Why)> SecPol = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MinimumPasswordLength"] = ("Lungimea minimă a parolei (caractere).", "Parolele scurte se ghicesc ușor."),
        ["PasswordComplexity"] = ("Parola trebuie să conțină litere mari, mici, cifre sau simboluri.", "Parolele simple se ghicesc ușor. 1 = obligatoriu."),
        ["MaximumPasswordAge"] = ("După câte zile trebuie schimbată parola.", "Limitează timpul în care o parolă aflată poate fi folosită."),
        ["MinimumPasswordAge"] = ("După câte zile se poate schimba din nou parola.", "Împiedică revenirea imediată la parola veche."),
        ["PasswordHistorySize"] = ("Câte parole vechi nu pot fi refolosite.", "Împiedică folosirea mereu a acelorași parole."),
        ["LockoutBadCount"] = ("După câte parole greșite se blochează contul.", "Fără blocare parolele pot fi ghicite la nesfârșit. 0 = nu se blochează niciodată."),
        ["LockoutDuration"] = ("Cât timp (minute) rămâne blocat contul.", "Încetinește ghicirea parolelor."),
        ["ResetLockoutCount"] = ("După cât timp (minute) se uită parolele greșite.", "Stabilește fereastra în care se numără încercările."),
        ["ClearTextPassword"] = ("Păstrarea parolelor cu criptare reversibilă.", "Echivalent cu păstrarea parolelor în clar; trebuie 0."),
        ["EnableAdminAccount"] = ("Starea contului Administrator încorporat.", "Este ținta preferată a atacurilor; 0 = dezactivat."),
        ["EnableGuestAccount"] = ("Starea contului Guest (vizitator).", "Permite acces fără cont nominal; 0 = dezactivat."),
        ["NewAdministratorName"] = ("Numele nou dat contului Administrator încorporat.", "Un nume diferit de „Administrator” îngreunează atacurile."),
        ["NewGuestName"] = ("Numele nou dat contului Guest.", "Un nume diferit îngreunează folosirea lui."),
        ["LSAAnonymousNameLookup"] = ("Dacă un utilizator anonim poate afla numele conturilor.", "Ajută un atacator să afle ce conturi există; 0 = interzis."),
        ["ForceLogoffWhenHourExpire"] = ("Deconectarea forțată la expirarea orelor permise.", "Aplică restricțiile de program."),
    };

    /// <summary>
    /// A policy setting in plain words: a known registry value, an audit subcategory, a service or a security-policy key; for any other registry
    /// value, what kind of setting it is (computer/user, imposed by Group Policy or not), never a guessed meaning.
    /// </summary>
    public static CodeExplanation Setting(LogAnalyzer.Dfir.Policy.SettingRef s)
    {
        switch (s.Type)
        {
            case "audit":
                return AuditSubcategory(s.Name) ?? E(s.Display, "categorie de audit", $"Categoria de audit „{s.Name}”.", "Ce nu este auditat nu lasă urme și nu poate fi verificat.", "Success = reușitele, Failure = eșecurile.");
            case "service":
                return Services.TryGetValue(s.Name, out var sv)
                    ? E(s.Display, "serviciu Windows", sv.What, sv.Why, "Pornire: 2 = automat, 3 = manual, 4 = dezactivat.")
                    : E(s.Display, "serviciu Windows", $"Serviciul Windows „{s.Name}” (program care rulează în fundal).", "Serviciile inutile măresc suprafața de atac.", "Pornire: 2 = automat, 3 = manual, 4 = dezactivat.");
            case "secpol":
                return SecPol.TryGetValue(s.Name, out var sp)
                    ? E(s.Display, "politică de securitate", sp.What, sp.Why)
                    : E(s.Display, "politică de securitate", $"Setarea de securitate „{s.Name}” din politica locală.", "Face parte din regulile de securitate ale stației.");
        }
        var known = FindIn(s.Display).FirstOrDefault(e => e.Kind.StartsWith("valoare de registru") || e.Kind.StartsWith("cheie de registru"));
        if (known is not null) return known with { Code = s.Display };
        var machine = s.Hive.Contains("MACHINE", StringComparison.OrdinalIgnoreCase) || s.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase);
        var gpo = s.Key.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase) || s.Key.StartsWith(@"Software\Policies", StringComparison.OrdinalIgnoreCase);
        return E(s.Display, "valoare de registru",
            $"Valoarea „{s.Name}” din registrul Windows (baza de date a setărilor), {(machine ? "pentru tot calculatorul" : "pentru utilizator")}" +
            (gpo ? ", impusă prin politica de grup (GPO)." : "."),
            "Politica cere o anumită valoare; „Curent” arată ce are stația acum, „Dorit” ce cere politica.",
            "Semnificația exactă a acestei valori nu este în catalogul aplicației; numele setării din politică (coloana Titlu) o descrie.");
    }
}
