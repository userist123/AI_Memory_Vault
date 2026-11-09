using LogAnalyzer.Core.Services.Edition;

namespace LogAnalyzer.Dfir.Windows.Audit;

public static partial class ControlEvaluator
{
    /// <summary>
    /// Checks P40-P47: read-only station settings taken from the DISA STIG for Windows 11 (the rule ids are cited in <see cref="CheckGuides"/>). A value that
    /// could not be read is "" (never a default, never Conform); a value that does not exist is <c>Absent</c> and the check says which Windows default it assumed.
    /// </summary>
    private static void StigChecks(StationFacts f, ControlReport r)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        bool isolated = f.Posture == StationPosture.Standalone;
        static string Shown(string v) => v == Absent ? "nesetat" : v == "" ? "necitit" : v;
        static bool Readable(string v) => v != "";
        const string lsaKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa";

        // P40 anonymous access to accounts and shares
        {
            const string title = "Acces anonim la conturi și partajări";
            (string Name, string Value, string Wanted, string Default, string Path)[] items =
            [
                ("RestrictAnonymousSAM", S("Stig.RestrictAnonymousSAM"), "1", "1", lsaKey),
                ("RestrictAnonymous", S("Stig.RestrictAnonymous"), "1", "0", lsaKey),
                ("EveryoneIncludesAnonymous", S("Stig.EveryoneIncludesAnonymous"), "0", "0", lsaKey),
                ("RestrictNullSessAccess", S("Stig.RestrictNullSessAccess"), "1", "1", @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters"),
            ];
            var bad = items.Where(i => i.Value != "" && i.Value != Absent && i.Value != i.Wanted).ToList();
            var unread = items.Where(i => i.Value == "").ToList();
            var assumed = items.Where(i => i.Value == Absent).ToList();
            var needsCheck = assumed.Where(i => i.Default != i.Wanted).ToList();
            var status = bad.Count > 0 ? ControlStatus.Neconform : unread.Count > 0 ? ControlStatus.Nedeterminat : needsCheck.Count > 0 ? ControlStatus.DeVerificat : ControlStatus.Conform;
            var assumedText = assumed.Count == 0 ? "" : " Valorile nesetate (" + string.Join(", ", assumed.Select(i => $"{i.Name}, implicit Windows {i.Default}")) + ") au fost presupuse la valoarea implicită Windows, nu citite.";
            string detail = unread.Count == items.Length ? "Setările de acces anonim nu au putut fi citite." + NoRead
                : bad.Count > 0 ? "Acces anonim permis: " + string.Join("; ", bad.Select(i => $"{i.Name} = {i.Value} (STIG cere {i.Wanted})")) + ". Un utilizator fără cont poate afla din rețea conturile și partajările stației." + assumedText
                : unread.Count > 0 ? $"Nu s-au putut citi: {string.Join(", ", unread.Select(i => i.Name))}; restul nu încalcă regula, dar rezultatul nu poate fi complet." + NoRead
                : needsCheck.Count > 0 ? $"RestrictAnonymous nu este setat; implicitul Windows este 0, iar STIG cere 1 (listarea anonimă a partajărilor rămâne posibilă)." + assumedText
                : "Accesul anonim la conturi și partajări este interzis (valorile cerute sau implicitele Windows)." + assumedText;
            Add(r, "P40", "Politici", title, status, detail,
                items.Select(i => $@"{i.Path} → {i.Name} = {Shown(i.Value)}"),
                "Setați RestrictAnonymousSAM = 1, RestrictAnonymous = 1, EveryoneIncludesAnonymous = 0 și RestrictNullSessAccess = 1 (politicile „Network access”).");
        }

        // P41 LM hash and old NTLM authentication
        {
            var noLm = S("Stig.NoLMHash");
            var lm = S("Stig.LmCompatibilityLevel");
            bool lmNum = int.TryParse(lm, out var lmLevel);
            var problems = new List<string>();
            if (noLm == "0" || (noLm is not ("" or Absent or "1") )) problems.Add($"NoLMHash = {noLm}: parola se păstrează și în formatul vechi LAN Manager, care se sparge în minute (STIG cere 1).");
            if (lm == Absent) problems.Add("LmCompatibilityLevel nu este setat; implicitul Windows este 3 (doar NTLMv2 trimis, dar serverul acceptă și metodele vechi), iar STIG cere 5.");
            else if (lmNum && lmLevel is >= 0 and <= 2) problems.Add($"LmCompatibilityLevel = {lm}: stația trimite răspunsuri LM sau NTLMv1, ușor de spart dacă sunt interceptate (STIG cere 5).");
            else if (lmNum && lmLevel is 3 or 4) problems.Add($"LmCompatibilityLevel = {lm}: STIG cere 5 (refuză LM și NTLM).");
            else if (lm != "" && !(lmNum && lmLevel == 5)) problems.Add($"LmCompatibilityLevel = {lm}: valoare neașteptată (STIG cere 5).");
            bool unread = noLm == "" || lm == "";
            var status = problems.Count > 0 ? ControlStatus.Neconform : unread ? ControlStatus.Nedeterminat : ControlStatus.Conform;
            string detail = noLm == "" && lm == "" ? "Setările LAN Manager și NTLM nu au putut fi citite." + NoRead
                : problems.Count > 0 ? string.Join(" ", problems) + (unread ? " O setare nu a putut fi citită." : "")
                : unread ? "Una dintre setările LAN Manager nu a putut fi citită; cealaltă este corectă." + NoRead
                : "Parola nu se păstrează în formatul vechi LAN Manager" + (noLm == Absent ? " (NoLMHash nesetat; implicitul Windows 10/11 este 1, presupus, nu citit)" : "") + " și stația trimite doar NTLMv2 și refuză LM și NTLM (LmCompatibilityLevel = 5).";
            Add(r, "P41", "Politici", "Hash LM și autentificare NTLM veche", status, detail,
                [lsaKey + " → NoLMHash = " + Shown(noLm), lsaKey + " → LmCompatibilityLevel = " + Shown(lm)],
                "Setați NoLMHash = 1 și LmCompatibilityLevel = 5 („Network security: LAN Manager authentication level” = doar NTLMv2, refuză LM și NTLM).");
        }

        // P42 dangerous user rights
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in S("UserRights.Names").Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (pair.IndexOf('=') is > 0 and var eq) names[pair[..eq]] = pair[(eq + 1)..];
            string Who(string sid) => names.TryGetValue(sid, out var n) ? $"{n} ({sid})" : sid;
            (string Right, string Rule, string Meaning, bool AdminsAllowed)[] rights =
            [
                ("SeTcbPrivilege", "WN11-UR-000015", "Acționare ca parte a sistemului de operare", false),
                ("SeCreateTokenPrivilege", "WN11-UR-000045", "Crearea unui jeton de acces", false),
                ("SeDebugPrivilege", "WN11-UR-000065", "Depanarea programelor", true),
            ];
            var violations = new List<string>();
            var unreadRights = new List<string>();
            var evidence = new List<string>();
            foreach (var (right, rule, meaning, adminsAllowed) in rights)
            {
                var v = S("UserRights." + right);
                var holders = v is "" or UserRights.Nobody ? [] : v.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                evidence.Add($"LSA (LsaEnumerateAccountsWithUserRight) → {right} = " + (v == "" ? "necitit" : v == UserRights.Nobody ? "(niciunul)" : string.Join("; ", holders.Select(Who))));
                if (v == "") { unreadRights.Add(right); continue; }
                var extra = adminsAllowed ? holders.Where(h => h != "S-1-5-32-544").ToList() : holders;
                if (extra.Count > 0)
                    violations.Add($"{right} („{meaning}”, {rule}) este acordat și: {string.Join(", ", extra.Select(Who))}" + (adminsAllowed ? " (doar Administrators îl poate avea)" : " (nimeni nu trebuie să îl aibă)"));
            }
            var status = violations.Count > 0 ? ControlStatus.Neconform : unreadRights.Count > 0 ? ControlStatus.Nedeterminat : ControlStatus.Conform;
            string detail = unreadRights.Count == rights.Length ? "Drepturile de utilizator nu au putut fi citite de la Autoritatea de securitate locală (LSA)." + NoRead
                : violations.Count > 0 ? "Drepturi periculoase acordate conturilor: " + string.Join("; ", violations) + ". Cine are aceste drepturi poate prelua controlul deplin al stației." + (unreadRights.Count > 0 ? $" Nu s-au putut citi: {string.Join(", ", unreadRights)}." : "")
                : unreadRights.Count > 0 ? $"Nu s-au putut citi: {string.Join(", ", unreadRights)}; restul drepturilor sunt corecte." + NoRead
                : "SeTcbPrivilege și SeCreateTokenPrivilege nu sunt acordate nimănui, iar SeDebugPrivilege este acordat doar grupului Administrators.";
            Add(r, "P42", "Politici", "Drepturi periculoase acordate conturilor", status, detail, evidence,
                "Scoateți conturile în plus din „Act as part of the operating system”, „Create a token object” (nimeni) și „Debug programs” (doar Administrators), în „User Rights Assignment”.");
        }

        // P43 AlwaysInstallElevated
        {
            var hm = S("Stig.AlwaysInstallElevatedHklm");
            var hc = S("Stig.AlwaysInstallElevatedHkcu");
            bool on = hm == "1" || hc == "1";
            // only 0 or a missing value is known to be off; any other value (2, an empty string) is not taken for "off" (review 2026-11-09)
            static bool Off(string v) => v is "0" || v == Absent;
            bool odd = !on && ((Readable(hm) && !Off(hm)) || (Readable(hc) && !Off(hc)));
            var status = on ? ControlStatus.Neconform : !Readable(hm) || !Readable(hc) ? ControlStatus.Nedeterminat : odd ? ControlStatus.DeVerificat : ControlStatus.Conform;
            string detail = on ? $"Instalarea fișierelor MSI cu drepturi ridicate este permisă ({(hm == "1" ? "pe stație (HKLM)" : "")}{(hm == "1" && hc == "1" ? " și " : "")}{(hc == "1" ? "pentru utilizatorul curent (HKCU)" : "")}): orice utilizator poate instala un pachet MSI cu drepturi de SYSTEM, deci poate prelua stația."
                : !Readable(hm) && !Readable(hc) ? "Politica de instalare MSI nu a putut fi citită." + NoRead
                : !Readable(hm) || !Readable(hc) ? "Una dintre cele două valori ale politicii de instalare MSI nu a putut fi citită." + NoRead
                : odd ? $"Politica de instalare MSI are o valoare neașteptată (HKLM = {Shown(hm)}, HKCU = {Shown(hc)}); doar 0 sau lipsa valorii înseamnă sigur „oprit”. Verificați politica."
                : "Instalarea MSI cu drepturi ridicate nu este activată (valorile lipsesc sau sunt 0). HKCU se citește doar pentru utilizatorul care a pornit aplicația.";
            Add(r, "P43", "Politici", "Instalare MSI cu drepturi ridicate (AlwaysInstallElevated)", status, detail,
                [@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Installer → AlwaysInstallElevated = " + Shown(hm),
                 @"HKCU\SOFTWARE\Policies\Microsoft\Windows\Installer → AlwaysInstallElevated = " + Shown(hc) + " (doar utilizatorul care a pornit aplicația)"],
                "Setați „Always install with elevated privileges” la Disabled, atât pentru calculator, cât și pentru utilizator.");
        }

        // P44 DEP and SEHOP
        {
            var dep = S("Stig.DepSupportPolicy");
            var sehop = S("Stig.DisableExceptionChainValidation");
            var depText = dep switch { "0" => "AlwaysOff (oprit)", "1" => "AlwaysOn (pornit pentru tot)", "2" => "OptIn (doar pentru Windows și programele care îl cer)", "3" => "OptOut (pornit pentru tot, cu excepții)", "" => "necitit", var x => $"valoare necunoscută {x}" };
            var problems = new List<string>();
            if (dep is "0" or "2") problems.Add($"DEP este {depText}: STIG cere cel puțin OptOut, ca protecția să acopere toate programele.");
            if (sehop is not ("" or Absent or "0")) problems.Add($"DisableExceptionChainValidation = {sehop}: protecția SEHOP este oprită (STIG cere 0).");
            bool unread = dep == "" || sehop == "";
            bool toVerify = (sehop == Absent) || dep is not ("" or "0" or "1" or "2" or "3");
            var status = problems.Count > 0 ? ControlStatus.Neconform : unread ? ControlStatus.Nedeterminat : toVerify ? ControlStatus.DeVerificat : ControlStatus.Conform;
            string detail = dep == "" && sehop == "" ? "Starea DEP și SEHOP nu a putut fi citită." + NoRead
                : problems.Count > 0 ? string.Join(" ", problems) + (unread ? " O sursă nu a putut fi citită." : "")
                : unread ? $"{(dep == "" ? "Starea DEP" : "Valoarea SEHOP")} nu a putut fi citită; cealaltă nu încalcă regula." + NoRead
                : toVerify ? $"DEP: {depText}. {(sehop == Absent ? "DisableExceptionChainValidation nu este setat; STIG cere valoarea 0 setată explicit (Windows 10/11 pornește SEHOP implicit, dar aplicația nu poate confirma fără valoare)." : "")}"
                : $"DEP: {depText}; SEHOP pornit (DisableExceptionChainValidation = 0).";
            Add(r, "P44", "Politici", "Protecția memoriei: DEP și SEHOP", status, detail.TrimEnd(),
                ["WMI Win32_OperatingSystem → DataExecutionPrevention_SupportPolicy = " + Shown(dep),
                 @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel → DisableExceptionChainValidation = " + Shown(sehop)],
                "Setați DEP la OptOut sau AlwaysOn (bcdedit /set nx OptOut) și DisableExceptionChainValidation la 0.");
        }

        // P45 WinRM Basic authentication and unencrypted traffic
        {
            const string winrm = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WinRM";
            (string Name, string Value, string Path)[] items =
            [
                ("Client AllowBasic", S("Stig.WinRmClientAllowBasic"), winrm + @"\Client → AllowBasic"),
                ("Client AllowUnencryptedTraffic", S("Stig.WinRmClientAllowUnencrypted"), winrm + @"\Client → AllowUnencryptedTraffic"),
                ("Service AllowBasic", S("Stig.WinRmServiceAllowBasic"), winrm + @"\Service → AllowBasic"),
                ("Service AllowUnencryptedTraffic", S("Stig.WinRmServiceAllowUnencrypted"), winrm + @"\Service → AllowUnencryptedTraffic"),
            ];
            var start = S("WinRM.ServiceStart");
            var bad = items.Where(i => i.Value == "1").ToList();
            var unread = items.Where(i => i.Value == "").ToList();
            var notSet = items.Where(i => i.Value == Absent).ToList();
            // a value other than 0, 1 or missing is not taken for "forbidden" (review 2026-11-09)
            var odd = items.Where(i => i.Value is not ("" or "0" or "1") && i.Value != Absent).ToList();
            bool serviceOff = start is "4" or Absent;
            // STIG CAT I: an unset value is a finding even with the service off (the client settings govern outgoing connections too), so it is never Conform
            var status = bad.Count > 0 ? ControlStatus.Neconform : unread.Count > 0 ? ControlStatus.Nedeterminat
                : notSet.Count == 0 && odd.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat;
            string detail = unread.Count == items.Length ? "Politicile WinRM nu au putut fi citite." + NoRead
                : bad.Count > 0 ? "WinRM permite autentificare Basic sau trafic necriptat: " + string.Join(", ", bad.Select(i => i.Name)) + " = 1. Parolele sau comenzile pot circula în clar prin rețea." + (unread.Count > 0 ? $" Nu s-au putut citi: {string.Join(", ", unread.Select(i => i.Name))}." : "")
                : unread.Count > 0 ? $"Nu s-au putut citi: {string.Join(", ", unread.Select(i => i.Name))}; restul nu încalcă regula." + NoRead
                : odd.Count > 0 ? $"Valori neașteptate: {string.Join(", ", odd.Select(i => $"{i.Name} = {i.Value}"))}; doar 0 înseamnă sigur „interzis”. Verificați politica."
                : notSet.Count == 0 ? "Autentificarea Basic și traficul necriptat sunt interzise la clientul și la serviciul WinRM (toate valorile sunt 0)."
                : serviceOff ? $"Serviciul WinRM este oprit ({(start == Absent ? "nu există" : "dezactivat")}), dar politica nu e setată pentru {string.Join(", ", notSet.Select(i => i.Name))}. STIG cere valoarea 0 setată explicit; setările clientului privesc și conexiunile pe care stația le face spre alte calculatoare."
                : $"Politica nu e setată pentru {string.Join(", ", notSet.Select(i => i.Name))} și serviciul WinRM poate porni (Start = {Shown(start)}); STIG cere valoarea 0 setată explicit.";
            Add(r, "P45", "Politici", "Autentificare Basic și trafic necriptat la WinRM", status, detail,
                items.Select(i => $"{i.Path} = {Shown(i.Value)}").Append(@"HKLM\SYSTEM\CurrentControlSet\Services\WinRM → Start = " + Shown(start)),
                "Setați la Disabled politicile „Allow Basic authentication” și „Allow unencrypted traffic”, pentru WinRM Client și WinRM Service.");
        }

        // P46 LLMNR
        {
            var v = S("Stig.LlmnrEnableMulticast");
            var status = v == "" ? ControlStatus.Nedeterminat : v == "0" ? ControlStatus.Conform : isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat;
            string detail = v == "" ? "Politica LLMNR nu a putut fi citită." + NoRead
                : v == "0" ? "Rezolvarea numelor prin difuzare (LLMNR) este oprită prin politică."
                : (v == Absent ? "Politica LLMNR nu este setată, deci LLMNR rămâne activ (implicit Windows)." : $"LLMNR este permis (EnableMulticast = {v}).")
                  + " Pe rețeaua locală un atacator poate răspunde la întrebările de nume și poate captura parole. Este o recomandare de întărire, fără regulă STIG." + (isolated ? " Pe o stație izolată trebuie oprit." : " Decideți conform procedurii.");
            Add(r, "P46", "Politici", "Rezolvarea numelor prin difuzare (LLMNR)", status, detail,
                [@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient → EnableMulticast = " + Shown(v)],
                "Setați „Turn off multicast name resolution” la Enabled (EnableMulticast = 0).");
        }

        // P47 IIS web server installed
        {
            var key = S("Stig.W3svcKey");
            var start = S("Stig.W3svcStart");
            var startText = start switch { "2" => "pornire automată", "3" => "pornire manuală", "4" => "dezactivat", Absent => "fără mod de pornire", "" => "necitit", var x => $"pornire {x}" };
            var inetStp = S("Stig.IisInetStpKey");
            bool partsOnly = key == "nu" && inetStp == "da";
            var status = key == "" ? ControlStatus.Nedeterminat : partsOnly ? ControlStatus.DeVerificat : key == "nu" ? ControlStatus.Conform : isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat;
            string detail = key == "" ? "Prezența serverului web IIS nu a putut fi citită." + NoRead
                : partsOnly ? "Serviciul web W3SVC nu este instalat, dar există componente IIS (cheia de instalare IIS, de exemplu Hostable Web Core sau uneltele de administrare). Verificați în „Funcții Windows” ce componente IIS sunt instalate; STIG cere ca IIS să lipsească."
                : key == "nu" ? "Serverul web IIS (serviciul W3SVC) nu este instalat."
                : $"Serverul web IIS este instalat (serviciul W3SVC: {startText}). Un server web deschide stația către conexiuni din rețea{(isolated ? " și nu trebuie să existe pe o stație izolată" : "; verificați dacă stația are nevoie de el")}.";
            Add(r, "P47", "Politici", "Server web IIS instalat", status, detail,
                [@"HKLM\SYSTEM\CurrentControlSet\Services\W3SVC → " + (key == "" ? "necitit" : key == "nu" ? "cheia lipsește (neinstalat)" : "cheia există (instalat)"),
                 @"HKLM\SYSTEM\CurrentControlSet\Services\W3SVC → Start = " + Shown(start),
                 @"HKLM\SOFTWARE\Microsoft\InetStp → " + (inetStp == "" ? "necitit" : inetStp == "da" ? "cheia există (componente IIS instalate)" : "cheia lipsește")],
                "Dezinstalați rolul „Internet Information Services” din „Funcții Windows” dacă stația nu este un server web aprobat.");
        }
    }
}
