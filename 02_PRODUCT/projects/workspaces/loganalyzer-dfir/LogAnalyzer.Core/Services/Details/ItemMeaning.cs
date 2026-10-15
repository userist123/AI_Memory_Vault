using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Core.Services.Details
{
    /// <summary>
    /// The „Ce înseamnă” tab of the detail window for every kind of item (owner 2026-10-11: „la majoritatea primesc «Nu există o explicație
    /// predefinită»”): what the item is, why it matters, how serious, what to do, plus attack techniques and registry locations in plain words.
    /// Texts are documented semantics (Microsoft, MITRE ATT&amp;CK); a technique association is a hint, never proof of an attack.
    /// </summary>
    public static class ItemMeaning
    {
        // ---- what each item kind is ----------------------------------------------------------------------------------------------------

        private static readonly Dictionary<string, (string Name, string What)> Kinds = new()
        {
            ["ExplainableRiskFactor"] = ("Factor de risc", "Un motiv pentru care aplicația a crescut scorul de risc al stației. Punctele arată cât contează din scorul total de 100."),
            ["AiAnomalyItem"] = ("Anomalie detectată automat", "Un comportament care iese din tiparul obișnuit al stației. Anomalia nu înseamnă automat un atac; este un semn de verificat."),
            ["UbaAnomalyItem"] = ("Comportament neobișnuit al unui utilizator", "Activitatea unei persoane diferă de obiceiurile ei (oră, stație, volum)."),
            ["AttackStorylineNode"] = ("Etapă în firul atacului", "Un pas din succesiunea reconstruită a unui posibil atac (de exemplu intrare, persistență, exfiltrare)."),
            ["AptActorProfile"] = ("Profil de grup de atacatori", "Un grup cunoscut ale cărui tehnici seamănă cu ce s-a găsit. Asemănarea NU înseamnă că grupul a atacat; arată doar tipare comune."),
            ["AzureAdFinding"] = ("Constatare în contul cloud (Azure AD / Entra)", "O activitate a unui cont din cloud care merită verificată."),
            ["ComplianceCheckResult"] = ("Rezultat de conformitate", "Cum se raportează stația la o cerință dintr-un standard sau o lege (de exemplu NIS2, ISO 27001)."),
            ["CorrelatedProcessNode"] = ("Proces (program care a rulat)", "Un program pornit pe stație, cu cine l-a pornit și ce a pornit la rândul lui."),
            ["ProcessNode"] = ("Proces (program care a rulat)", "Un program în arborele proceselor: cine l-a pornit și ce a pornit la rândul lui."),
            ["DetectedIssue"] = ("Alertă", "O problemă detectată de regulile aplicației, cu evenimentele pe care se bazează."),
            ["DnsAuditFinding"] = ("Constatare DNS", "O problemă în serviciul de nume al rețelei (DNS), care traduce nume de calculatoare în adrese."),
            ["EmployeeSessionActivity"] = ("Sesiune de lucru", "Când a intrat și a ieșit o persoană pe stație, de câte ori a blocat și deblocat ecranul."),
            ["FileServerAuditFinding"] = ("Constatare pe serverul de fișiere", "Un acces la un folder partajat sau la un fișier care merită verificat."),
            ["IocItem"] = ("Indicator de compromitere (IOC)", "O urmă concretă (adresă, nume de fișier, amprentă) asociată cu activitate rău-intenționată cunoscută. Se caută pe alte stații."),
            ["KerberosAdFinding"] = ("Constatare Kerberos / Active Directory", "O problemă la autentificarea în domeniu (Kerberos este sistemul de bilete prin care conturile se autentifică în rețea)."),
            ["LolbasFinding"] = ("Program Windows folosit abuziv (LOLBAS)", "Un program care face parte din Windows, dar a fost folosit într-un mod des întâlnit la atacatori (ca să nu aducă programe proprii)."),
            ["MitreTechnique"] = ("Tehnică de atac (MITRE ATT&CK)", "O metodă cunoscută prin care atacatorii acționează, din catalogul public MITRE ATT&CK."),
            ["MultiEventCorrelationFinding"] = ("Corelare între mai multe evenimente", "Mai multe evenimente care, puse împreună într-un interval scurt, formează un tipar de atac cunoscut."),
            ["ProvenanceLedgerEntry"] = ("Intrare în registrul de custodie", "O operație făcută asupra unei probe (cine, când, ce), înlănțuită prin amprente: dacă cineva modifică o intrare, lanțul se rupe și se vede."),
            ["SigmaRule"] = ("Regulă de detecție Sigma", "O regulă publică, într-un format standard, care descrie ce evenimente indică o anumită activitate."),
            ["StandaloneSamFinding"] = ("Constatare pe conturile locale (SAM)", "O problemă la conturile locale ale stației (baza SAM păstrează conturile și parolele locale)."),
            ["StorageAuditItem"] = ("Element de stocare", "Un fișier, folder sau suport cu un risc asociat (de exemplu acces neobișnuit, dimensiune, loc)."),
            ["AdAttributeDelta"] = ("Modificare în Active Directory", "Un atribut al unui obiect din domeniu (cont, grup, calculator) a fost schimbat."),
            ["TimelineItem"] = ("Element de cronologie", "Un moment din cronologia incidentului, cu sursa lui."),
            // Senior review E2: row types of the control, domain, mail, anti-forensics, containment and media screens.
            ["ControlCheck"] = ("Verificare de control", "O regulă verificată de aplicație (pe stație, în domeniu sau în cutia poștală), cu rezultatul ei: conform, neconform, de verificat sau nedeterminat."),
            ["AntiForensicCheck"] = ("Verificare de ștergere a urmelor", "Aplicația a căutat în probe o metodă cunoscută prin care cineva își ascunde urmele (jurnale golite, ora schimbată, fișiere șterse). O urmă găsită arată ce s-a întâmplat, nu de ce; poate avea și o explicație legitimă."),
            ["SuspectProcess"] = ("Program suspect care rulează", "Un program pornit acum pe stație care are unul sau mai multe semne folosite des de programele rău-intenționate (loc neobișnuit, conexiuni spre exterior)."),
            ["ProcessIncident"] = ("Incident de izolare a unui program", "Un program oprit sau izolat de aplicație (blocat în firewall, suspendat), cu tot ce s-a făcut și ce s-a găsit despre el."),
            ["MailMessage"] = ("Mesaj de e-mail", "Un mesaj din urmărirea mesajelor serverului de e-mail: cine l-a trimis, cui, când și de pe ce adresă IP. Conținutul mesajului nu este citit."),
            ["UserActivity"] = ("Activitatea unui cont pe stație", "Câte autentificări (la tastatură, de la distanță, eșuate) și câte sesiuni cu drepturi de administrator a avut un cont în perioada controlată."),
            ["LiveDeviceEntry"] = ("Dispozitiv conectat sau deconectat", "Un dispozitiv (stick, disc, telefon) care tocmai a fost conectat sau scos, comparat cu registrul suporturilor aprobate."),
            ["DeviceAssessment"] = ("Dispozitiv comparat cu registrul", "Un dispozitiv cunoscut de stație și dacă figurează în registrul suporturilor aprobate."),
        };

        private static readonly string[] WhyProps = ["Description", "Explanation", "Details", "Detail", "Reason", "AnomalyReason", "EvidenceSummary", "SecurityImpact", "LegalJustification", "StorageImpact", "Verdict", "MatchText"];
        private static readonly string[] DoProps = ["RemediationActionRo", "ContainmentActionRo", "RequiredAction", "Recommendation", "WhatToDo"];
        private static readonly string[] MitreProps = ["MitreTechniqueId", "MitreId", "TechniqueId", "TechId", "MitreTags", "Attack"];

        // Explainers registered by assemblies Core cannot reference (the App registers the control-check guides, which live in Dfir.Windows).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<object, IEnumerable<string>>> Explainers = new();

        /// <summary>Adds the plain-language lines an outer assembly knows for items of one type (matched by type name); replaces an earlier registration.</summary>
        public static void RegisterExplainer(string typeName, Func<object, IEnumerable<string>> explain) => Explainers[typeName] = explain;

        /// <summary>Plain-language lines for any object shown in a grid. Never empty: at worst it says what the item is and where to look.</summary>
        public static void Explain(object item, List<string> into)
        {
            var t = item.GetType();
            string P(string n) => t.GetProperty(n)?.GetValue(item) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";

            if (Kinds.TryGetValue(t.Name, out var k)) into.Add($"Ce este: {k.Name}. {k.What}");
            // A registered explainer (for example the control-check guide) already says what was found and what to do: it replaces the generic lines.
            if (Explainers.TryGetValue(t.Name, out var external))
            {
                into.AddRange(external(item).Where(l => !string.IsNullOrWhiteSpace(l)));
                if (into.Count > 0) return;
            }
            foreach (var n in WhyProps) if (P(n) is { Length: > 0 } w) into.Add($"{Label(n)}: {w}");
            if (t.Name is "SuspectProcess" && t.GetProperty("Reasons")?.GetValue(item) is IEnumerable<string> reasons && reasons.Any())
                into.Add("De ce este suspect: " + string.Join("; ", reasons));
            if (t.Name is "AntiForensicCheck" && P("Result") is { Length: > 0 } afr)
                into.Add("Rezultat: " + afr switch
                {
                    "Detected" => "DETECTAT — urma există în probe; se cere o explicație.",
                    "NotDetected" => "NEDETECTAT — probele analizate nu arată această metodă. Nu înseamnă că stația este „curată”, doar că urma nu apare.",
                    _ => "NEDETERMINAT — sursa necesară lipsește, deci verificarea nu s-a putut face.",
                });
            if (t.Name is "ControlCheck" && P("Status") is { Length: > 0 } cs) into.Add($"Rezultat: {cs}.");
            if (t.Name is "ProcessIncident")
            {
                if (P("Trigger") is { Length: > 0 } trig) into.Add($"Ce l-a declanșat: {trig}");
                if (P("State") is { Length: > 0 } state) into.Add($"Starea izolării: {state}.");
            }
            if (t.Name is "UserActivity")
                into.Add($"Pe scurt: {P("InteractiveLogons")} intrări la tastatură, {P("RemoteLogons")} de la distanță, {P("FailedLogons")} eșuate, {P("PrivilegedSessions")} sesiuni cu drepturi de administrator. Comparați cu cine avea voie să lucreze pe stație.");

            if (P("WeightPoints") is { Length: > 0 } pts) into.Add($"Cât contează: {pts} puncte din scorul de risc de 100. Cu cât mai multe, cu atât factorul trage mai mult scorul în sus.");
            if (P("Severity") is { Length: > 0 } sev) into.Add($"Cât de grav: {Severity(sev)}.");
            if (P("EvidenceSource") is { Length: > 0 } src) into.Add($"Pe ce se bazează: {src}.");
            if (P("Status") is { Length: > 0 } st && t.Name == "ComplianceCheckResult") into.Add($"Rezultat: {st}.");

            foreach (var n in MitreProps)
                foreach (var id in Regex.Matches(P(n), @"T\d{4}(?:\.\d{3})?").Select(m => m.Value).Distinct())
                    into.Add(Technique(id));

            foreach (var n in DoProps) if (P(n) is { Length: > 0 } d) into.Add($"Ce faceți: {d}");
            if (t.Name is "CorrelatedProcessNode" && P("IsSuspicious") == "True") into.Add("Procesul a fost marcat ca suspect; motivul este scris mai sus. Arătați-l unui specialist înainte de orice acțiune.");
            if (t.Name is "AptActorProfile") into.Add("Ce faceți: nimic pe baza asemănării singure; folosiți-o doar ca indiciu pentru ce să mai căutați.");
            if (t.Name is "ProvenanceLedgerEntry") into.Add("Ce faceți: nimic; intrarea este o dovadă a ce s-a făcut cu proba. Verificarea lanțului se face din pagina „Lanțul de custodie”.");

            if (into.Count == 0)
                into.Add($"Acest element ({t.Name}) nu are încă o explicație scrisă în aplicație. Valorile lui sunt în fila „Toate câmpurile”.");
        }

        private static string Label(string prop) => prop switch
        {
            "Description" => "De ce apare",
            "Explanation" => "De ce apare",
            "Details" => "Detalii",
            "Detail" => "Ce s-a găsit",
            "Reason" => "De ce",
            "Verdict" => "Concluzie",
            "MatchText" => "Față de registru",
            "AnomalyReason" => "De ce este suspect",
            "EvidenceSummary" => "Ce s-a găsit",
            "SecurityImpact" => "Ce efect are asupra securității",
            "LegalJustification" => "Temeiul",
            "StorageImpact" => "Efect",
            _ => prop,
        };

        /// <summary>Severity words (English or Romanian, as the engines write them) in plain Romanian.</summary>
        public static string Severity(string s) => s.Trim().ToLowerInvariant() switch
        {
            "critical" or "critic" or "critică" => "CRITIC — se tratează imediat",
            "high" or "ridicat" or "ridicată" or "mare" => "RIDICAT — se verifică în aceeași zi",
            "medium" or "moderate" or "mediu" or "medie" or "moderat" => "MEDIU — se verifică",
            "low" or "scăzut" or "scăzută" or "redus" => "SCĂZUT — de obicei nu este o problemă",
            "info" or "informational" or "informativ" => "INFORMATIV — nu indică o problemă, doar descrie",
            _ => s,
        };

        // ---- MITRE ATT&CK in plain words -----------------------------------------------------------------------------------------------

        private static readonly Dictionary<string, string> Mitre = new()
        {
            ["T1003"] = "furtul parolelor sau al amprentelor lor din memorie ori din fișierele sistemului",
            ["T1003.001"] = "furtul parolelor din memoria procesului LSASS (de exemplu cu Mimikatz)",
            ["T1003.002"] = "copierea bazei SAM, care conține parolele conturilor locale",
            ["T1003.006"] = "imitarea unui controler de domeniu pentru a primi parolele tuturor conturilor (DCSync)",
            ["T1005"] = "colectarea de date de pe stație",
            ["T1018"] = "căutarea altor calculatoare în rețea",
            ["T1021"] = "conectarea la alte calculatoare cu conturi valide (mișcare laterală)",
            ["T1021.001"] = "conectare la alt calculator prin Remote Desktop",
            ["T1021.002"] = "acces la folderele administrative partajate ale altui calculator (C$, ADMIN$)",
            ["T1021.004"] = "conectare la alt calculator prin SSH",
            ["T1021.006"] = "rulare de comenzi pe alt calculator prin WinRM",
            ["T1027"] = "ascunderea codului prin codificare sau criptare, ca să nu fie recunoscut",
            ["T1027.004"] = "compilarea codului abia pe stația atacată, ca să nu fie detectat",
            ["T1033"] = "aflarea utilizatorului curent și a drepturilor lui",
            ["T1036"] = "deghizarea unui program rău-intenționat ca program legitim",
            ["T1036.003"] = "redenumirea sau copierea unui program de sistem pentru a induce în eroare",
            ["T1036.005"] = "folosirea unui nume sau a unui loc care seamănă cu al programelor legitime",
            ["T1039"] = "copierea de date din foldere partajate în rețea",
            ["T1040"] = "interceptarea traficului de rețea",
            ["T1041"] = "trimiterea datelor furate pe canalul de comandă al atacatorului",
            ["T1047"] = "rularea de comenzi prin WMI (instrumentul de administrare Windows)",
            ["T1048"] = "scoaterea datelor printr-un protocol neobișnuit",
            ["T1052"] = "scoaterea datelor pe un suport fizic (de exemplu stick USB)",
            ["T1052.001"] = "scoaterea datelor pe un stick USB",
            ["T1053"] = "pornirea automată a unui program prin activitate planificată",
            ["T1053.002"] = "pornirea unui program la o oră fixă prin comanda „at”",
            ["T1053.005"] = "pornirea automată a unui program prin Task Scheduler",
            ["T1055"] = "ascunderea codului în alt proces care rulează",
            ["T1055.001"] = "injectarea unei biblioteci (DLL) în alt proces",
            ["T1055.002"] = "injectarea unui program portabil în alt proces",
            ["T1055.003"] = "deturnarea unui fir de execuție al altui proces",
            ["T1055.004"] = "injectare prin apeluri asincrone (APC)",
            ["T1055.012"] = "golirea unui proces legitim și înlocuirea codului lui (process hollowing)",
            ["T1057"] = "listarea programelor care rulează",
            ["T1059"] = "rularea de comenzi sau scripturi",
            ["T1059.001"] = "rularea de comenzi PowerShell",
            ["T1059.003"] = "rularea de comenzi în linia de comandă Windows (cmd)",
            ["T1059.004"] = "rularea de comenzi în shell Unix",
            ["T1059.005"] = "rularea de scripturi Visual Basic",
            ["T1068"] = "exploatarea unei vulnerabilități pentru a obține drepturi mai mari",
            ["T1070"] = "ștergerea urmelor",
            ["T1070.001"] = "golirea jurnalelor Windows",
            ["T1070.004"] = "ștergerea fișierelor folosite",
            ["T1070.006"] = "modificarea datelor fișierelor (timestomping), ca să pară mai vechi sau mai noi",
            ["T1070.009"] = "ștergerea dovezilor de persistență",
            ["T1071"] = "comunicare cu atacatorul prin protocoale obișnuite",
            ["T1071.001"] = "comunicare cu atacatorul prin web (HTTP/HTTPS)",
            ["T1071.004"] = "comunicare cu atacatorul ascunsă în cereri DNS",
            ["T1074"] = "adunarea datelor într-un loc înainte de a fi scoase",
            ["T1074.001"] = "adunarea datelor pe stația locală înainte de a fi scoase",
            ["T1078"] = "folosirea unui cont valid (furat sau împrumutat)",
            ["T1078.002"] = "folosirea unui cont valid de domeniu",
            ["T1078.003"] = "folosirea unui cont local valid",
            ["T1078.004"] = "folosirea unui cont cloud valid",
            ["T1082"] = "aflarea informațiilor despre sistem",
            ["T1083"] = "căutarea fișierelor și folderelor",
            ["T1087"] = "listarea conturilor",
            ["T1091"] = "răspândire prin suporturi amovibile (stick-uri)",
            ["T1095"] = "comunicare printr-un protocol de rețea nestandard",
            ["T1098"] = "modificarea unui cont pentru a păstra accesul",
            ["T1105"] = "aducerea de programe sau fișiere de la atacator pe stație",
            ["T1106"] = "folosirea directă a funcțiilor interne Windows",
            ["T1110"] = "ghicirea parolelor prin încercări repetate",
            ["T1110.001"] = "ghicirea parolei unui cont prin încercări repetate",
            ["T1110.003"] = "încercarea unor parole comune pe multe conturi (password spraying)",
            ["T1112"] = "modificarea registrului Windows",
            ["T1114"] = "colectarea e-mailurilor",
            ["T1114.003"] = "redirecționarea e-mailurilor către altcineva",
            ["T1127.001"] = "rularea codului prin instrumentul de compilare MSBuild",
            ["T1133"] = "intrare prin servicii de acces de la distanță (VPN, RDP expus)",
            ["T1134"] = "manipularea drepturilor unui proces",
            ["T1134.001"] = "furtul sau imitarea drepturilor altui proces",
            ["T1136"] = "crearea unui cont nou pentru acces",
            ["T1136.001"] = "crearea unui cont local nou pentru acces",
            ["T1137"] = "persistență prin aplicațiile Office",
            ["T1140"] = "decodarea fișierelor ascunse",
            ["T1189"] = "infectare prin vizitarea unui site compromis",
            ["T1190"] = "exploatarea unei aplicații expuse în rețea",
            ["T1195"] = "compromiterea unui furnizor de software",
            ["T1197"] = "folosirea serviciului BITS pentru a descărca sau a rula programe",
            ["T1202"] = "rularea comenzilor indirect, prin alt program",
            ["T1200"] = "conectarea unui dispozitiv fizic străin (tastatură falsă, stick care tastează singur, placă de rețea)",
            ["T1204"] = "rularea unui fișier rău-intenționat de către utilizator",
            ["T1204.002"] = "deschiderea unui fișier atașat sau descărcat rău-intenționat",
            ["T1204.004"] = "lipirea și rularea de către utilizator a unei comenzi copiate dintr-o pagină înșelătoare (de exemplu în fereastra Executare, Win+R)",
            ["T1207"] = "înregistrarea unui controler de domeniu fals",
            ["T1218"] = "rularea codului prin programe semnate ale Windows, ca să treacă de protecții",
            ["T1218.001"] = "rularea codului prin fișiere de ajutor compilate (CHM)",
            ["T1218.004"] = "rularea codului prin InstallUtil",
            ["T1218.005"] = "rularea codului prin mshta",
            ["T1218.007"] = "rularea codului prin msiexec",
            ["T1218.010"] = "rularea codului prin regsvr32",
            ["T1218.011"] = "rularea codului prin rundll32",
            ["T1219"] = "instalarea unui program de control de la distanță",
            ["T1222.001"] = "schimbarea drepturilor pe fișiere și foldere",
            ["T1485"] = "distrugerea datelor",
            ["T1486"] = "criptarea datelor pentru răscumpărare (ransomware)",
            ["T1489"] = "oprirea serviciilor",
            ["T1490"] = "ștergerea copiilor de rezervă și a punctelor de restaurare, ca datele să nu poată fi recuperate",
            ["T1498"] = "blocarea rețelei prin trafic excesiv",
            ["T1499"] = "blocarea unui serviciu prin suprasolicitare",
            ["T1505"] = "persistență prin componente ale serverelor",
            ["T1505.003"] = "instalarea unei pagini web de control (web shell) pe server",
            ["T1529"] = "oprirea sau repornirea sistemului",
            ["T1539"] = "furtul cookie-urilor de sesiune din browser",
            ["T1542.001"] = "modificarea firmware-ului de sistem",
            ["T1543"] = "crearea sau modificarea unui proces de sistem pentru persistență",
            ["T1543.003"] = "crearea sau modificarea unui serviciu Windows pentru persistență",
            ["T1546.003"] = "persistență prin abonamente WMI care rulează o comandă la un eveniment",
            ["T1546.008"] = "înlocuirea funcțiilor de accesibilitate (de exemplu Sticky Keys) pentru acces fără parolă",
            ["T1546.012"] = "persistență prin opțiunile de depanare ale programelor (IFEO)",
            ["T1546.015"] = "persistență prin deturnarea componentelor COM",
            ["T1547"] = "pornirea automată la autentificare sau la pornirea sistemului",
            ["T1547.001"] = "pornire automată prin cheile Run din registru sau prin folderul Startup",
            ["T1547.004"] = "pornire automată prin setările Winlogon",
            ["T1548"] = "ocolirea mecanismelor de control al drepturilor",
            ["T1548.002"] = "ocolirea UAC (fereastra care cere confirmarea drepturilor de administrator)",
            ["T1550"] = "folosirea unor date de autentificare furate (hash, bilete) fără parolă",
            ["T1555"] = "furtul parolelor salvate",
            ["T1555.003"] = "furtul parolelor salvate în browser",
            ["T1555.004"] = "furtul parolelor din Windows Credential Manager",
            ["T1556"] = "modificarea procesului de autentificare",
            ["T1557"] = "interceptarea comunicării dintre două calculatoare (om la mijloc)",
            ["T1557.001"] = "otrăvirea răspunsurilor de nume în rețea (LLMNR/NBT-NS) pentru a capta parole",
            ["T1558"] = "furtul sau falsificarea biletelor Kerberos",
            ["T1558.003"] = "cererea de bilete Kerberos pentru a sparge offline parolele conturilor de serviciu (Kerberoasting)",
            ["T1558.004"] = "obținerea datelor de spart pentru conturi fără pre-autentificare (AS-REP roasting)",
            ["T1560"] = "arhivarea datelor înainte de a fi scoase",
            ["T1562"] = "slăbirea apărării",
            ["T1562.001"] = "dezactivarea sau modificarea antivirusului ori a altor protecții",
            ["T1562.002"] = "dezactivarea jurnalelor de evenimente Windows",
            ["T1562.004"] = "dezactivarea sau modificarea firewall-ului",
            ["T1563"] = "preluarea unei sesiuni de la distanță existente",
            ["T1564.004"] = "ascunderea datelor în fluxuri alternative NTFS",
            ["T1565"] = "modificarea datelor",
            ["T1566"] = "phishing (mesaje înșelătoare)",
            ["T1566.001"] = "phishing cu atașament",
            ["T1566.002"] = "phishing cu link",
            ["T1567"] = "scoaterea datelor printr-un serviciu web",
            ["T1567.001"] = "scoaterea datelor într-un depozit de cod",
            ["T1567.002"] = "scoaterea datelor într-un serviciu de stocare în cloud",
            ["T1568.002"] = "domenii generate automat pentru comunicarea cu atacatorul",
            ["T1569"] = "rularea programelor prin servicii de sistem",
            ["T1569.002"] = "rularea unui program prin crearea unui serviciu (de exemplu PsExec)",
            ["T1570"] = "copierea instrumentelor de atac între calculatoarele rețelei",
            ["T1572"] = "tunelarea traficului prin alt protocol",
            ["T1573"] = "comunicare criptată cu atacatorul",
            ["T1574.001"] = "deturnarea ordinii în care se încarcă bibliotecile (DLL)",
            ["T1574.002"] = "încărcarea unei biblioteci rău-intenționate lângă un program legitim (DLL side-loading)",
            ["T1599"] = "ocolirea separării dintre rețele",
            ["T1621"] = "bombardarea cu cereri de autentificare în doi pași până la acceptare",
            ["T1484.001"] = "modificarea politicilor de grup (GPO) ale domeniului",
        };

        /// <summary>One technique in plain words; a sub-technique without its own text falls back to its parent. Unknown IDs are named, not invented.</summary>
        public static string Technique(string id)
        {
            var text = Mitre.TryGetValue(id, out var t) && t.Length > 0 ? t
                     : id.Contains('.') && Mitre.TryGetValue(id[..id.IndexOf('.')], out var p) && p.Length > 0 ? p + " (o variantă a ei)"
                     : null;
            return text is null
                ? $"Tehnica de atac {id} (catalogul MITRE ATT&CK): nu are încă o descriere în aplicație. Asocierea arată la ce să fiți atenți, nu dovedește un atac."
                : $"Tehnica de atac {id}: {text}. Asocierea arată la ce să fiți atenți, nu dovedește un atac.";
        }

        // ---- registry locations ------------------------------------------------------------------------------------------------------

        private static readonly (Regex Path, string Text)[] RegistryAreas =
        [
            (new(@"\\BCD0{8}", RegexOptions.IgnoreCase), "Baza de date de pornire a Windows (BCD): ce sistem pornește, din ce disc, cu ce opțiuni (recuperare, semnarea driverelor, depanare). „Element” și „Type” sunt setări codate ale pornirii. Este configurație normală; contează doar dacă cineva a dezactivat recuperarea sau verificarea semnăturii driverelor."),
            (new(@"\\(Run|RunOnce|RunServices)(\\|$)", RegexOptions.IgnoreCase), "Cheie de pornire automată: programul din valoare pornește singur la fiecare pornire sau autentificare. Este locul preferat de programele rău-intenționate pentru a rămâne pe stație; fiecare program de aici trebuie să fie cunoscut."),
            (new(@"\\Services\\[^\\]+$|\\Services\\[^\\]+\\Parameters", RegexOptions.IgnoreCase), "Configurația unui serviciu Windows (program care rulează permanent în fundal): ImagePath = ce program, Start = cum pornește (2 automat, 3 manual, 4 dezactivat)."),
            (new(@"\\Enum\\USBSTOR", RegexOptions.IgnoreCase), "Lista dispozitivelor de stocare USB conectate vreodată, cu producător, model și serie."),
            (new(@"\\Enum\\USB(\\|$)", RegexOptions.IgnoreCase), "Lista tuturor dispozitivelor USB conectate vreodată (nu doar stocare: tastaturi, telefoane, adaptoare)."),
            (new(@"\\Enum\\SWD\\WPDBUSENUM", RegexOptions.IgnoreCase), "Dispozitive portabile (telefoane, camere, playere) conectate prin MTP/PTP și unitățile lor de stocare."),
            (new(@"\\MountedDevices", RegexOptions.IgnoreCase), "Ce litere de unitate (D:, E:…) au primit discurile și stick-urile conectate."),
            (new(@"Windows Portable Devices", RegexOptions.IgnoreCase), "Dispozitivele portabile (telefoane, stick-uri) cunoscute de Windows, cu numele afișat."),
            (new(@"\\BTHPORT\\Parameters\\Devices", RegexOptions.IgnoreCase), "Dispozitivele Bluetooth asociate cu stația (telefoane, căști, tastaturi)."),
            (new(@"NetworkList\\Profiles", RegexOptions.IgnoreCase), "Rețelele la care s-a conectat stația, cu data primei și ultimei conectări."),
            (new(@"UserAssist", RegexOptions.IgnoreCase), "Programele pornite de utilizator din interfața grafică, cu numărul de porniri (numele sunt codate ROT13)."),
            (new(@"AppCompatCache|ShimCache", RegexOptions.IgnoreCase), "Lista programelor văzute de sistemul de compatibilitate (ShimCache): arată că un program a existat pe stație."),
            (new(@"\\bam\\State\\UserSettings|\\bam\\UserSettings", RegexOptions.IgnoreCase), "Ultima rulare a fiecărui program, pe utilizator (BAM)."),
            (new(@"RecentDocs", RegexOptions.IgnoreCase), "Documentele deschise recent de utilizator."),
            (new(@"TypedPaths|RunMRU|TypedURLs", RegexOptions.IgnoreCase), "Ce a tastat utilizatorul în bara de adrese, în „Run” sau în browser."),
            (new(@"\\Winlogon", RegexOptions.IgnoreCase), "Setările de autentificare ale Windows: shell, programul de după autentificare, pornirea automată în cont."),
            (new(@"\\Control\\Lsa", RegexOptions.IgnoreCase), "Setările componentei care verifică parolele (LSA): protecții împotriva furtului de parole."),
            (new(@"\\Policies\\", RegexOptions.IgnoreCase), "Setare impusă prin politica de grup (GPO) sau de administrator."),
            (new(@"Windows Defender|Microsoft\\Windows Defender", RegexOptions.IgnoreCase), "Setări ale antivirusului Microsoft Defender (inclusiv excluderi)."),
            (new(@"FirewallPolicy|SharedAccess", RegexOptions.IgnoreCase), "Setările și regulile firewall-ului Windows."),
            (new(@"Terminal Server", RegexOptions.IgnoreCase), "Setările de acces de la distanță (Remote Desktop)."),
            (new(@"\\Uninstall\\", RegexOptions.IgnoreCase), "Programele instalate, așa cum apar în „Aplicații instalate”."),
            (new(@"Image File Execution Options", RegexOptions.IgnoreCase), "Opțiunile de depanare ale programelor: un „Debugger” aici poate înlocui pornirea unui program cu altul (tehnică de persistență)."),
            (new(@"\\Shell\\.*\\command|\\shell\\open\\command", RegexOptions.IgnoreCase), "Ce program deschide un tip de fișier; o schimbare neobișnuită poate porni cod rău-intenționat la deschiderea fișierelor."),
            (new(@"ShellBags|\\Shell\\BagMRU|\\Shell\\Bags", RegexOptions.IgnoreCase), "Folderele deschise de utilizator în Explorer, inclusiv de pe stick-uri sau din rețea (ShellBags)."),
            (new(@"MountPoints2", RegexOptions.IgnoreCase), "Unitățile (inclusiv stick-uri și foldere din rețea) folosite de un anumit utilizator."),
        ];

        /// <summary>Lines for a registry key/value: where it is, what that area of the registry holds, the value decoded.</summary>
        public static void ExplainRegistry(string? keyPath, string? valueName, string? valueData, string? suspicion, List<string> into)
        {
            var path = keyPath ?? "";
            var hive = path.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
                ? "Setare a întregului calculator (HKEY_LOCAL_MACHINE)."
                : path.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HKEY_USERS", StringComparison.OrdinalIgnoreCase)
                    ? "Setare a unui utilizator (HKEY_CURRENT_USER / HKEY_USERS)."
                    : "";
            into.Add("Ce este: o valoare din registrul Windows, baza de date în care Windows și programele își păstrează setările. " + hive);
            var area = RegistryAreas.FirstOrDefault(a => a.Path.IsMatch(path));
            into.Add(area.Text is { } t ? "Ce conține această zonă: " + t : "Zona aceasta a registrului nu are încă o descriere în aplicație; numele cheii și al valorii arată la ce se referă.");
            if (DecodeValue(valueData) is { } decoded) into.Add($"Valoarea „{valueName}”: {decoded}");
            if (!string.IsNullOrWhiteSpace(suspicion)) into.Add("Cât de grav: " + Severity(suspicion) + ".");
        }

        /// <summary>Registry export notation (dword:, hex:, hex(2):, hex(7):) in words. Null for plain text (already readable).</summary>
        public static string? DecodeValue(string? data)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;
            var d = data.Trim();
            if (d.StartsWith("dword:", StringComparison.OrdinalIgnoreCase) && uint.TryParse(d[6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
                return $"număr {n} (în hexazecimal {d[6..]}).";
            var m = Regex.Match(d, @"^hex\((?<t>[27b])\):(?<b>[0-9a-fA-F,\\\s]*)$");
            if (m.Success)
            {
                var bytes = Bytes(m.Groups["b"].Value);
                if (m.Groups["t"].Value == "b" && bytes.Length == 8) return $"număr {BitConverter.ToUInt64(bytes, 0)} (QWORD).";
                var text = System.Text.Encoding.Unicode.GetString(bytes.Length % 2 == 0 ? bytes : bytes[..^1]).TrimEnd('\0');
                return m.Groups["t"].Value == "7" ? $"listă de texte: {string.Join(" · ", text.Split('\0', StringSplitOptions.RemoveEmptyEntries))}." : $"text: {text}.";
            }
            if (d.StartsWith("hex:", StringComparison.OrdinalIgnoreCase)) return $"date binare ({Bytes(d[4..]).Length} octeți), folosite intern de Windows; nu au o formă de citit.";
            return null;
        }

        private static byte[] Bytes(string s) =>
            s.Replace("\\", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(x => byte.TryParse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) ? b : (byte)0).ToArray();
    }
}
