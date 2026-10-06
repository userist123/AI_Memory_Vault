using System;
using System.Collections.Generic;

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
        };

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
            text ??= Other.TryGetValue(eventId, out var o) &&
                     (provider.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) || provider.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                      provider.Contains("Sysmon", StringComparison.OrdinalIgnoreCase) || provider.Contains("MsiInstaller", StringComparison.OrdinalIgnoreCase))
                ? o : null;
            text ??= Security.TryGetValue(eventId, out var s) && eventId >= 4600 ? s : null;
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
