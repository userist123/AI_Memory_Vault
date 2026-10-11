using System;
using System.Collections.Generic;
using System.Linq;

namespace LogAnalyzer.Core.Services.Details
{
    /// <summary>
    /// Plain-language reading of the Windows events an investigator meets most, and of the codes inside them.
    /// Meanings are documented Microsoft semantics; nothing here asserts that an event is malicious.
    /// </summary>
    public static class EventMeaning
    {
        public static readonly Dictionary<string, string> LogonTypes = new()
        {
            ["0"] = "System", ["2"] = "interactiv (tastatură/ecran)", ["3"] = "rețea (ex. share SMB)", ["4"] = "batch (task programat)",
            ["5"] = "serviciu", ["7"] = "deblocare ecran", ["8"] = "rețea cu parolă în clar (ex. IIS basic)",
            ["9"] = "NewCredentials (runas /netonly)", ["10"] = "RemoteInteractive (RDP)", ["11"] = "CachedInteractive (fără controler de domeniu)",
            ["12"] = "CachedRemoteInteractive", ["13"] = "CachedUnlock",
        };

        public static readonly Dictionary<string, string> NtStatus = new(StringComparer.OrdinalIgnoreCase)
        {
            ["0xc000006d"] = "nume de utilizator sau parolă greșite", ["0xc000006a"] = "parolă greșită", ["0xc0000064"] = "utilizator inexistent",
            ["0xc0000234"] = "cont blocat", ["0xc0000072"] = "cont dezactivat", ["0xc000006f"] = "autentificare în afara orelor permise",
            ["0xc0000070"] = "autentificare de pe o stație nepermisă", ["0xc0000071"] = "parolă expirată", ["0xc0000193"] = "cont expirat",
            ["0xc0000224"] = "parola trebuie schimbată la următoarea autentificare", ["0xc000015b"] = "tipul de logon nu este permis contului",
            ["0xc0000133"] = "ceas desincronizat între client și server", ["0xc000005e"] = "niciun server de autentificare disponibil",
            ["0xc0000413"] = "autentificare blocată de firewall-ul de autentificare", ["0x0"] = "succes",
        };

        public static readonly Dictionary<string, string> WellKnownSids = new(StringComparer.OrdinalIgnoreCase)
        {
            ["S-1-5-18"] = "LocalSystem", ["S-1-5-19"] = "LocalService", ["S-1-5-20"] = "NetworkService",
            ["S-1-5-32-544"] = "grupul Administrators", ["S-1-5-32-545"] = "grupul Users", ["S-1-5-32-555"] = "Remote Desktop Users",
            ["S-1-5-7"] = "Anonymous", ["S-1-1-0"] = "Everyone", ["S-1-0-0"] = "NULL SID",
        };

        public static readonly Dictionary<string, string> ElevationTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["%%1936"] = "token complet (UAC dezactivat sau cont built-in)", ["%%1937"] = "token elevat (Run as administrator)", ["%%1938"] = "token limitat (neelevat)",
        };

        private static readonly Dictionary<int, string> Security = new()
        {
            [4624] = "Autentificare reușită pe această stație. Tipul de logon arată cum (local, rețea, RDP…); IpAddress/WorkstationName arată de unde.",
            [4625] = "Autentificare EȘUATĂ. Status/SubStatus spun de ce. Multe eșecuri la rând pentru același cont sau de la aceeași adresă indică ghicirea parolei.",
            [4634] = "Sesiune închisă (logoff). Se leagă de 4624 prin TargetLogonId.",
            [4647] = "Deconectare inițiată de utilizator.",
            [4648] = "Autentificare cu credențiale explicite (runas, conectare la alt server cu alt cont). Frecventă la mișcare laterală.",
            [4672] = "Sesiunii i s-au atribuit privilegii speciale (cont de administrator sau echivalent).",
            [4688] = "Proces nou creat. NewProcessName și CommandLine (dacă auditul liniei de comandă e activ) arată ce s-a rulat; ParentProcessName arată cine l-a pornit.",
            [4689] = "Proces încheiat.",
            [4697] = "Serviciu instalat în sistem (persistență frecventă).",
            [4698] = "Task programat creat (persistență frecventă).",
            [4699] = "Task programat șters.",
            [4700] = "Task programat activat.",
            [4702] = "Task programat modificat.",
            [4719] = "Politica de audit a sistemului a fost modificată (cineva poate reduce ce se înregistrează).",
            [4720] = "Cont de utilizator creat.",
            [4722] = "Cont de utilizator activat.",
            [4723] = "Încercare de schimbare a propriei parole.",
            [4724] = "Resetare de parolă a altui cont (de către un administrator).",
            [4725] = "Cont de utilizator dezactivat.",
            [4726] = "Cont de utilizator șters.",
            [4728] = "Membru adăugat într-un grup global de securitate.",
            [4732] = "Membru adăugat într-un grup local de securitate (ex. Administrators).",
            [4733] = "Membru eliminat dintr-un grup local.",
            [4738] = "Cont de utilizator modificat.",
            [4740] = "Cont blocat după prea multe parole greșite.",
            [4768] = "Tichet Kerberos TGT cerut (autentificare în domeniu).",
            [4769] = "Tichet de serviciu Kerberos cerut. Cereri RC4 (0x17) către multe servicii pot indica Kerberoasting.",
            [4771] = "Pre-autentificare Kerberos eșuată (parolă greșită în domeniu).",
            [4776] = "Validare de credențiale NTLM.",
            [4778] = "Sesiune (RDP) reconectată.",
            [4779] = "Sesiune (RDP) deconectată.",
            [4798] = "Enumerare a grupurilor locale ale unui utilizator.",
            [4799] = "Enumerare a membrilor unui grup local (recunoaștere frecventă).",
            [4616] = "Ora sistemului a fost schimbată (poate afecta cronologia și poate fi folosită pentru a ascunde activitate).",
            [5140] = "Acces la un share de rețea.",
            [5145] = "Verificare de acces la un fișier dintr-un share de rețea.",
            [5156] = "Conexiune permisă de Windows Filtering Platform.",
            [5157] = "Conexiune BLOCATĂ de Windows Filtering Platform (de exemplu de o regulă de izolare).",
            [6416] = "Dispozitiv extern nou recunoscut (ex. stick USB).",
            [1102] = "Jurnalul Security a fost ȘTERS. Pe o stație auditată aproape întotdeauna necesită explicație.",
            // Senior review E1: events the engines analyse (Kerberos/AD, file server, employee activity, credentials) and that had no meaning.
            [4656] = "S-a cerut acces la un obiect (fișier, folder sau cheie de registru). Arată cine a încercat să-l deschidă și cu ce drepturi; apare doar dacă auditul accesului la obiecte este pornit.",
            [4662] = "S-a făcut o operație asupra unui obiect din Active Directory. Dacă operația folosește drepturile de replicare a parolelor și contul NU este un controler de domeniu, poate fi un furt al parolelor tuturor conturilor (DCSync).",
            [4663] = "S-a accesat un obiect (fișier, folder sau cheie de registru). AccessMask arată ce s-a făcut: citire, scriere sau ștergere. Multe ștergeri sau scrieri în scurt timp pot indica criptare (ransomware) sau distrugere de date.",
            [4739] = "Politica domeniului (parole, blocarea conturilor) a fost modificată.",
            [4742] = "Contul unui calculator din domeniu a fost modificat. O schimbare neașteptată (de exemplu nume de serviciu sau delegare) poate pregăti un atac asupra domeniului.",
            [4756] = "Membru adăugat într-un grup universal de securitate (grup valabil în toată pădurea de domenii).",
            [4800] = "Ecranul stației a fost blocat (utilizatorul a plecat de la stație sau a apăsat Win+L).",
            [4801] = "Ecranul stației a fost deblocat (utilizatorul s-a întors și a introdus parola).",
            [4907] = "Setările de audit ale unui obiect (ce se înregistrează despre el) au fost schimbate.",
            [5136] = "Un obiect din Active Directory a fost modificat (ce atribut, valoarea veche și cea nouă). Modificările de politici de grup sau de drepturi trebuie să fie aprobate.",
            [5137] = "Un obiect nou a fost creat în Active Directory (cont, grup, calculator, politică de grup).",
            [5139] = "Un obiect din Active Directory a fost mutat în altă unitate organizațională.",
            [5141] = "Un obiect a fost șters din Active Directory.",
            [5379] = "Au fost citite credențiale salvate în Windows Credential Manager. Apare și la funcționarea normală a programelor; multe citiri la rând, făcute de un program neobișnuit, pot indica furtul parolelor salvate.",
        };

        private static readonly Dictionary<int, string> System_ = new()
        {
            [104] = "Un jurnal de evenimente a fost șters.",
            [7045] = "Serviciu nou instalat (persistență frecventă; verificați ImagePath).",
            [7036] = "Un serviciu și-a schimbat starea (pornit/oprit).",
            [7040] = "Tipul de pornire al unui serviciu a fost schimbat.",
            [6005] = "Serviciul Event Log a pornit (pornire sistem).",
            [6006] = "Serviciul Event Log s-a oprit (oprire sistem).",
            [6008] = "Oprirea anterioară a sistemului a fost neașteptată.",
            [1074] = "Oprire/repornire inițiată de un proces sau utilizator.",
            [1100] = "Serviciul de jurnalizare a evenimentelor s-a oprit. La o oprire normală a stației este firesc; altfel, cât timp serviciul este oprit nu se înregistrează nimic.",
            [1102] = "Jurnalul Security a fost ȘTERS. Pe o stație auditată aproape întotdeauna necesită explicație.",
            [1] = "Ora sistemului a fost schimbată (Kernel-General 1). Motivul (Reason) arată dacă a fost sincronizarea automată sau o schimbare făcută de o persoană ori de un program.",
            [22] = "Fusul orar al sistemului a fost schimbat (Kernel-General 22). Orele locale din jurnale se mută; orele UTC rămân corecte.",
        };

        // Senior review E1: providers the engines read that none of the dictionaries above covers. Provider is matched by a fragment of its name.
        private static readonly (string Provider, int Id, string Text)[] ByProvider =
        [
            ("Kernel-Power", 41, "Stația a repornit fără o oprire corectă (pană de curent, blocare, buton de pornire ținut apăsat). Ce rula atunci s-a întrerupt brusc."),
            ("WinRM", 6, "WinRM: această stație a deschis o sesiune de comenzi de la distanță către alt calculator."),
            ("WinRM", 91, "WinRM: pe această stație s-a creat o sesiune de comenzi de la distanță (cineva a rulat comenzi aici de pe alt calculator)."),
            ("WinRM", 142, "WinRM: o operație de comenzi de la distanță a eșuat."),
            ("DNS-Server", 257, "Server DNS: a răspuns cu succes la o cerere de nume."),
            ("DNS-Server", 258, "Server DNS: a răspuns cu eroare la o cerere de nume (numele nu există sau cererea a fost refuzată)."),
            ("DNS-Server", 259, "Server DNS: a ignorat o cerere de nume."),
            ("DNS-Server", 260, "Server DNS: a trimis cererea mai departe, către alt server DNS (recursivitate)."),
            ("GroupPolicy", 5016, "Politica de grup: o componentă (extensie) a terminat aplicarea setărilor pe stație."),
            ("GroupPolicy", 5017, "Politica de grup: un pas din aplicarea politicii s-a încheiat (cu durata lui)."),
            ("GroupPolicy", 5312, "Politica de grup: lista politicilor (GPO) care se aplică acestei stații sau acestui utilizator."),
            ("GroupPolicy", 7320, "Politica de grup: eroare la aplicare. Codul de eroare din eveniment arată cauza (de exemplu controlerul de domeniu nu a putut fi contactat)."),
            ("UserPnp", 20001, "Windows a instalat driverul unui dispozitiv nou conectat (de exemplu un stick USB). Numele și seria dispozitivului sunt în eveniment."),
            ("UserPnp", 20003, "Windows a adăugat serviciul (driverul) pentru un dispozitiv nou conectat (de exemplu un stick USB)."),
            ("volsnap", 25, "Copiile de umbră (punctele de restaurare) ale unui disc au fost abandonate, de obicei din lipsă de spațiu. Fișierele vechi nu mai pot fi recuperate din ele."),
            ("volsnap", 33, "Cea mai veche copie de umbră a unui disc a fost ștearsă ca să se facă loc pentru una nouă."),
        ];

        /// <summary>Microsoft Entra ID sign-in error codes (documented AADSTS codes), as the cloud engines read them from sign-in logs.</summary>
        public static readonly Dictionary<int, string> EntraSignInCodes = new()
        {
            [50053] = "Autentificare în cloud blocată: contul a fost blocat temporar după prea multe parole greșite sau încercarea a venit de la o adresă cunoscută ca rău-intenționată.",
            [50074] = "Autentificarea în cloud a cerut verificarea în doi pași (MFA). Parola era corectă; dacă utilizatorul nu a încercat atunci să intre, altcineva îi știe parola.",
            [50125] = "Autentificarea în cloud a fost întreruptă ca să se schimbe parola sau să se înregistreze o metodă de verificare.",
            [53003] = "Autentificare în cloud blocată de o regulă de acces condiționat a organizației (loc, dispozitiv sau risc nepermis).",
        };

        /// <summary>True when any of the tables above has a plain-language meaning for this event ID (for whatever provider).</summary>
        public static bool HasMeaning(int eventId) =>
            Security.ContainsKey(eventId) || System_.ContainsKey(eventId) || Other.ContainsKey(eventId) ||
            ByProvider.Any(p => p.Id == eventId) || EntraSignInCodes.ContainsKey(eventId);

        private static readonly Dictionary<int, string> Other = new()
        {
            [4104] = "PowerShell: conținutul unui bloc de script executat (Script Block Logging). Căutați descărcări, cod codificat, dezactivare Defender.",
            [400] = "PowerShell: motorul a pornit (HostApplication arată linia de comandă).",
            [1116] = "Microsoft Defender a detectat malware.",
            [1117] = "Microsoft Defender a acționat împotriva malware-ului.",
            [5001] = "Protecția în timp real a Microsoft Defender a fost DEZACTIVATĂ.",
            [5007] = "Configurația Microsoft Defender a fost modificată (verificați excluderile).",
            [1] = "Sysmon: proces creat (cu hash și linie de comandă).",
            [3] = "Sysmon: conexiune de rețea inițiată de un proces.",
            [11] = "Sysmon: fișier creat.",
            [22] = "Sysmon: interogare DNS a unui proces.",
            [7] = "Sysmon: un program a încărcat o bibliotecă (DLL). O bibliotecă încărcată din Temp sau din Public poate fi cod rău-intenționat strecurat într-un program legitim.",
            [8] = "Sysmon: un proces a pornit un fir de execuție în ALT proces (CreateRemoteThread). Este tehnica clasică de injectare a codului în alt program.",
            [10] = "Sysmon: un proces a deschis memoria altui proces. Dacă ținta este lsass.exe (procesul care păstrează parolele), poate fi furt de parole.",
            [12] = "Sysmon: o cheie de registru a fost creată sau ștearsă.",
            [13] = "Sysmon: o valoare din registru a fost modificată.",
            [14] = "Sysmon: o cheie sau o valoare din registru a fost redenumită.",
            [15] = "Sysmon: a fost scris un flux alternativ al unui fișier (de exemplu marcajul „descărcat de pe internet”). Datele pot fi ascunse în astfel de fluxuri.",
            [11707] = "Windows Installer: produs instalat.",
            [11724] = "Windows Installer: produs dezinstalat.",
        };

        public static void Explain(int eventId, string provider, IReadOnlyDictionary<string, string> data, List<string> into)
        {
            string? text = null;
            if (provider.Contains("Security-Auditing", StringComparison.OrdinalIgnoreCase) || provider.Equals("Security", StringComparison.OrdinalIgnoreCase))
                Security.TryGetValue(eventId, out text);
            else if (provider.Contains("Eventlog", StringComparison.OrdinalIgnoreCase) || provider.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase) ||
                     provider.Contains("Kernel-General", StringComparison.OrdinalIgnoreCase) || provider.Equals("User32", StringComparison.OrdinalIgnoreCase))
                System_.TryGetValue(eventId, out text);
            text ??= ByProvider.FirstOrDefault(p => p.Id == eventId && provider.Contains(p.Provider, StringComparison.OrdinalIgnoreCase)).Text;
            text ??= Other.TryGetValue(eventId, out var o) &&
                     (provider.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) || provider.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                      provider.Contains("Sysmon", StringComparison.OrdinalIgnoreCase) || provider.Contains("MsiInstaller", StringComparison.OrdinalIgnoreCase))
                ? o : null;
            text ??= Security.TryGetValue(eventId, out var s) && eventId >= 4600 ? s : null;
            text ??= EntraSignInCodes.TryGetValue(eventId, out var entra) &&
                     (provider.Contains("Azure", StringComparison.OrdinalIgnoreCase) || provider.Contains("Entra", StringComparison.OrdinalIgnoreCase) ||
                      provider.Contains("AAD", StringComparison.OrdinalIgnoreCase) || provider.Contains("SignIn", StringComparison.OrdinalIgnoreCase) ||
                      provider.Contains("M365", StringComparison.OrdinalIgnoreCase))
                ? entra : null;
            if (text is not null) into.Add(text);

            string D(string k) => data.TryGetValue(k, out var v) ? v : "";
            switch (eventId)
            {
                case 4624:
                case 4625:
                    var lt = D("LogonType");
                    if (LogonTypes.TryGetValue(lt, out var ltText)) into.Add($"Tip de logon {lt}: {ltText}.");
                    var who = $"{D("TargetDomainName")}\\{D("TargetUserName")}".Trim('\\');
                    if (who.Length > 0) into.Add($"Cont: {who}.");
                    var from = $"{D("IpAddress")} {D("WorkstationName")}".Trim();
                    if (from.Length > 0 && from != "-" && from != "- -") into.Add($"Origine: {from}.");
                    if (eventId == 4625)
                    {
                        var st = D("SubStatus").Length > 0 && D("SubStatus") != "0x0" ? D("SubStatus") : D("Status");
                        if (NtStatus.TryGetValue(st, out var why)) into.Add($"Motivul eșecului: {why} ({st}).");
                    }
                    if (D("ElevatedToken") == "%%1842") into.Add("Sesiunea are token elevat (drepturi de administrator).");
                    // Owner 2026-10-11: a controller must not mistake the operating system's own logons for a person's activity.
                    if (eventId == 4624 && (lt is "0" or "5" || D("TargetUserSid") is "S-1-5-18" or "S-1-5-19" or "S-1-5-20" || D("TargetUserName").EndsWith('$') ||
                                            D("TargetUserName") is "SYSTEM" or "LOCAL SERVICE" or "NETWORK SERVICE" || D("TargetUserName").StartsWith("DWM-") || D("TargetUserName").StartsWith("UMFD-")))
                        into.Add("Pe înțeles: operație normală a Windows (un serviciu sau sistemul însuși s-a autentificat), NU activitatea unei persoane. Astfel de evenimente apar de sute de ori pe zi; la control nu contează.");
                    else if (eventId == 4624 && lt is "2" or "10" or "11" or "7")
                        into.Add("Pe înțeles: o persoană a intrat pe stație" + (lt == "10" ? " de la distanță (Remote Desktop)." : lt == "7" ? " deblocând ecranul." : " la tastatură.") + " Contul și ora se compară cu cine avea voie să lucreze atunci.");
                    break;
                case 4688:
                    if (D("NewProcessName").Length > 0) into.Add($"A rulat: {D("NewProcessName")}");
                    if (D("CommandLine").Length > 0) into.Add($"Linie de comandă: {D("CommandLine")}");
                    if (D("ParentProcessName").Length > 0) into.Add($"Pornit de: {D("ParentProcessName")}");
                    if (ElevationTypes.TryGetValue(D("TokenElevationType"), out var el)) into.Add($"Elevare: {el}.");
                    break;
                case 4720:
                case 4726:
                case 4732:
                case 4728:
                    into.Add($"Cont țintă: {D("TargetDomainName")}\\{D("TargetUserName")} {D("MemberName")}; modificare făcută de: {D("SubjectDomainName")}\\{D("SubjectUserName")}.".Replace("\\ ", " "));
                    break;
                case 5157:
                case 5156:
                    into.Add($"Aplicație: {D("Application")}; destinație {D("DestAddress")}:{D("DestPort")}; protocol {D("Protocol")}.");
                    break;
                case 7045:
                    into.Add($"Serviciu: {D("ServiceName")}; executabil: {D("ImagePath")}; cont: {D("AccountName")}; pornire: {D("StartType")}.");
                    break;
            }
        }
    }
}
