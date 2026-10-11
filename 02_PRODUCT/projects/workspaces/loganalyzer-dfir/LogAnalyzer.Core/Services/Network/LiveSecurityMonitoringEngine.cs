using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Dfir.Analysis;

namespace LogAnalyzer.Core.Services.Network
{
    public class LiveSecurityMonitoringEngine
    {
        public const string TestAlertTitle = "[TEST — NU ESTE O ALERTĂ REALĂ] Mesaj de test al căii de alertare";

        /// <summary>Approved accounts and maintenance windows from the procedure profile (WP15a); <c>null</c> = no profile, log clears stay NotAssessed.</summary>
        public LogMaintenancePolicy? MaintenancePolicy { get; set; }

        /// <summary>Failed logons kept for <see cref="FailureWindow"/>: (station, source address) → (account, time). Per station and per source, never global.</summary>
        private readonly Dictionary<string, List<(string User, DateTime Utc)>> _failures = new(StringComparer.OrdinalIgnoreCase);
        public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
        public const int SameAccountThreshold = 5, SprayAccountsThreshold = 10;

        // -enc / -EncodedCommand followed by a base64 blob; "bypass" alone is NOT suspicious (management tools use -ExecutionPolicy Bypass every day).
        private static readonly Regex EncodedPowerShell = new(@"(?:^|[\s""'])-(?:e|ec|en|enc|enco|encod|encode|encoded|encodedcommand)\s+[A-Za-z0-9+/=]{16,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PowerShellAbuse = new(@"downloadstring|downloadfile|\biex\s*\(|invoke-expression|amsiutils|amsiinitfailed|amsiscanbuffer|frombase64string\(.{0,80}\)\s*\|\s*iex", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LogClearCommand = new(@"\bwevtutil(?:\.exe)?\s+(?:/\S+\s+)*(?:cl|clear-log)\b|\bclear-eventlog\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>A named field of the event's XML (EventData/UserData), or null.</summary>
        public static string? Field(string? xml, string name)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            var m = Regex.Match(xml, "<Data Name=\"" + Regex.Escape(name) + "\">([^<]*)</Data>");
            var v = m.Success ? m.Groups[1].Value.Trim() : null;
            return string.IsNullOrEmpty(v) || v == "-" ? null : v;
        }

        /// <summary>
        /// Evaluează în memorie un eveniment nou sosit în timp real și returnează o alertă dacă se detectează un atac sau anomalie.
        /// </summary>
        public DetectedIssue? EvaluateLiveEvent(ParsedEvent ev)
        {
            if (ev == null) return null;

            string msg = (ev.Message ?? string.Empty).ToLowerInvariant();
            string provider = (ev.ProviderName ?? string.Empty).ToLowerInvariant();

            // 1. Phishing & Malicious Payload Staging (T1566 / T1204 / T1105)
            bool mshtaUrl = msg.Contains("mshta") && (msg.Contains("http://") || msg.Contains("https://"));
            bool scriptFromDownload = (msg.Contains("downloads") || msg.Contains("appdata\\local\\temp")) && (msg.Contains(".iso") || msg.Contains(".hta") || msg.Contains(".vbs") || msg.Contains(".lnk") || msg.Contains(".js ") || msg.EndsWith(".js") || msg.Contains(".js\"")) && (msg.Contains("wscript") || msg.Contains("cscript"));
            if (msg.Contains("certutil") && (msg.Contains("urlcache") || msg.Contains("-f http")) || mshtaUrl ||
                msg.Contains("curl") && (msg.Contains("http://") || msg.Contains("https://")) && (msg.Contains(" -o") || msg.Contains(">")) || scriptFromDownload)
            {
                return new DetectedIssue
                {
                    Title = "🚨 ALERTĂ MARE: Fișier adus din Internet sau script pornit din Descărcări",
                    Severity = "High",
                    MitreTechniqueId = mshtaUrl ? "T1218.005" : scriptFromDownload ? "T1204.002" : "T1105",
                    MitreTacticName = mshtaUrl ? "Defense Evasion" : scriptFromDownload ? "Execution" : "Command and Control",
                    Explanation = $"Pe [{ev.MachineName}] o unealtă a sistemului (certutil, curl, mshta sau wscript/cscript) a adus un fișier din Internet sau a pornit un script din Descărcări/Temp. Este calea obișnuită prin care ajunge un program rău pe stație după un mesaj înșelător. Verificați cine a rulat comanda (vedeți evenimentul) și ce fișier a rezultat.",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 2. Ransomware & Shadow Copy Deletion (EID 4688 / Sysmon 1 / PowerShell 4104)
            if (msg.Contains("vssadmin") && (msg.Contains("delete") || msg.Contains("shadows")) ||
                msg.Contains("bcdedit") && msg.Contains("recoveryenabled") ||
                msg.Contains("wbadmin") && msg.Contains("delete") && msg.Contains("catalog"))
            {
                return new DetectedIssue
                {
                    Title = "🚨 ALERTĂ CRITICĂ: Tentativă Distrugere Shadow Copies (Ransomware)",
                    Severity = "Critical",
                    MitreTechniqueId = "T1490",
                    MitreTacticName = "Impact",
                    Explanation = $"Detectată execuția comenzii de distrugere a copiilor de rezervă pe hostul [{ev.MachineName}]. Tehnica este caracteristică etapelor premergătoare criptării de către grupările de Ransomware (LockBit, BlackCat, Akira).",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 2. PowerShell Codificat / Obfuscat / Download Cradle / Bypass (EID 4104 / EID 4688 / Sysmon 1 / Engine 400)
            if (EncodedPowerShell.IsMatch(ev.Message ?? "") || PowerShellAbuse.IsMatch(msg))
            {
                return new DetectedIssue
                {
                    Title = "🚨 ALERTĂ MARE: Execuție PowerShell Codificat / Download Cradle",
                    Severity = "High",
                    MitreTechniqueId = "T1059.001",
                    MitreTacticName = "Execution",
                    Explanation = $"A fost interceptată o execuție suspectă de script PowerShell ascuns/obfuscat pe [{ev.MachineName}]. Comanda încearcă descărcarea de payload direct în memorie sau eludarea politicilor de execuție.",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 3. Acces Memorie LSASS / Mimikatz / Procdump
            if (msg.Contains("lsass.exe") && (msg.Contains("0x1010") || msg.Contains("0x1fffff") || msg.Contains("mimikatz") || msg.Contains("sekurlsa") || msg.Contains("logonpasswords")))
            {
                return new DetectedIssue
                {
                    Title = "🚨 ALERTĂ CRITICĂ: Tentativă Extragere Credențiale LSASS (Mimikatz / Dump)",
                    Severity = "Critical",
                    MitreTechniqueId = "T1003.001",
                    MitreTacticName = "Credential Access",
                    Explanation = $"Un proces a solicitat drepturi de acces la memoria procesului de autentificare LSASS pe [{ev.MachineName}]. Indicator puternic de atac cu unelte de tip Mimikatz sau Procdump.",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 4. Recunoaștere Privilegii / Utilizatori (whoami /priv, net user, net localgroup administrators)
            if (msg.Contains("whoami") && (msg.Contains("/priv") || msg.Contains("/all") || msg.Contains("/groups")) ||
                msg.Contains("net localgroup") && msg.Contains("administrators") ||
                msg.Contains("net group \"domain admins\""))
            {
                return new DetectedIssue
                {
                    Title = "⚠️ ALERTĂ MEDIE: Activitate de Recunoaștere & Enumerare Privilegii (Discovery)",
                    Severity = "Medium",
                    MitreTechniqueId = "T1033",
                    MitreTacticName = "Discovery",
                    Explanation = $"Comandă de enumerare a privilegiilor utilizatorului sau a membrilor grupurilor de administratori executată pe [{ev.MachineName}].",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 5. Creare Cont Administrator Nou (EID 4720 + EID 4732)
            if (ev.EventId == 4720 || ev.EventId == 4732 || (msg.Contains("net user") && msg.Contains("/add")))
            {
                return new DetectedIssue
                {
                    Title = "⚠️ ALERTĂ MEDIE: Creare / Modificare Cont Utilizator sau Grup Privilegiat",
                    Severity = "Medium",
                    MitreTechniqueId = "T1136.001",
                    MitreTacticName = "Persistence",
                    Explanation = $"A fost creat un cont local nou sau a fost adăugat un membru într-un grup administrativ pe [{ev.MachineName}].",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 6. Curățare Jurnal de Securitate (Anti-Forensics EID 1102 / 104)
            if (ev.EventId == 1102 || ev.EventId == 104 || LogClearCommand.IsMatch(msg))
            {
                string channel = ev.EventId == 1102 ? "Security" : ev.EventId == 104 ? "System" : "indicat în linia de comandă wevtutil";
                var (clearUser, clearDomain) = LogClearAssessment.SubjectFromXml(ev.XmlData);
                var clear = LogClearAssessment.AssessUnscheduled(channel, new DateTimeOffset(ev.TimeCreated.ToUniversalTime()), MaintenancePolicy, clearUser, clearDomain);
                return new DetectedIssue
                {
                    Title = "⚠️ Jurnal de evenimente golit (necesită verificare)",
                    Severity = clear.Severity.ToString(),
                    MitreTechniqueId = "T1070.001",
                    MitreTacticName = "Defense Evasion",
                    Explanation = $"Golire de jurnal ({channel}) (EID {ev.EventId} / wevtutil cl) pe [{ev.MachineName}]. Golirea poate fi mentenanță planificată sau ascundere de urme după compromitere; {clear.Reason}",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 7. Instalare Serviciu Nou / Lateral Movement (EID 7045)
            if (ev.EventId == 7045 || msg.Contains("psexec") || msg.Contains("psexesvc"))
            {
                bool isPsExec = msg.Contains("psexec") || msg.Contains("psexesvc");
                return new DetectedIssue
                {
                    Title = isPsExec ? "🚨 ALERTĂ MARE: Serviciu de Execuție la Distanță (PsExec / Lateral Movement)" : "⚠️ NOTIFICARE: Serviciu Nou Instalat în Sistem",
                    Severity = isPsExec ? "High" : "Low",
                    MitreTechniqueId = "T1543.003",
                    MitreTacticName = "Persistence",
                    Explanation = $"A fost înregistrat un nou serviciu de sistem pe [{ev.MachineName}]. {ev.Message}",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 8. Eșecuri repetate de autentificare (EID 4625): același cont (T1110.001) sau multe conturi din aceeași sursă (T1110.003)
            if (ev.EventId == 4625)
            {
                var failure = TrackFailedLogon(ev);
                if (failure is not null) return failure;
            }

            // 9. Exploits Kernel Ring 0 / BYOVD - Bring Your Own Vulnerable Driver (T1068 / T1543.003)
            if (msg.Contains("gdrv.sys") || msg.Contains("mhyprot2.sys") || msg.Contains("procexp152.sys") ||
                msg.Contains("dbutil") || msg.Contains("rtcore64.sys") || msg.Contains("kprocesshacker") ||
                msg.Contains("byovd") || msg.Contains("loldrivers") ||
                (msg.Contains("kernel driver") && (msg.Contains("installed") || msg.Contains("service installed") || msg.Contains("vulnerable driver"))))
            {
                return new DetectedIssue
                {
                    Title = "☣️ ALERTĂ CRITICĂ: Tentativă Încărcare Driver Kernel Vulnerabil (BYOVD / Ring 0)",
                    Severity = "Critical",
                    MitreTechniqueId = "T1068",
                    MitreTacticName = "Privilege Escalation",
                    Explanation = $"A fost detectată tentativa de încărcare a unui driver de sistem vulnerabil cunoscut (BYOVD - Bring Your Own Vulnerable Driver) pe [{ev.MachineName}]. Atacatorii folosesc această tehnică pentru a eluda EDR-ul și a obține execuție de cod la nivel de Kernel Ring 0.",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 10. Injecții Exclusiv în Memorie / Process Hollowing (T1055 / T1055.012)
            if (msg.Contains("process hollowing") || msg.Contains("reflective dll") || msg.Contains("process injection") ||
                msg.Contains("virtualalloc") && (msg.Contains("page_execute_readwrite") || msg.Contains("0x40") || msg.Contains("writeprocessmemory")) ||
                msg.Contains("injected") && (msg.Contains("notepad") || msg.Contains("calc") || msg.Contains("svchost") || msg.Contains("spoolsv") || msg.Contains("werfault")))
            {
                return new DetectedIssue
                {
                    Title = "💉 ALERTĂ CRITICĂ: Injecție Exclusivă în Memorie & Process Hollowing",
                    Severity = "Critical",
                    MitreTechniqueId = "T1055.012",
                    MitreTacticName = "Defense Evasion",
                    Explanation = $"A fost detectată o anomalie gravă de memorie RAM pe [{ev.MachineName}]: un proces legitim de sistem a fost golit și injectat cu cod malițios direct în memorie fără scriere de fișiere pe disc (Process Hollowing).",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 11. Atac Fizic / Hardware (BadUSB / Rubber Ducky HID Injection T1052)
            if (msg.Contains("rubber ducky") || msg.Contains("badusb") || msg.Contains("hid keyboard") ||
                (msg.Contains("usb") && (msg.Contains("keystroke") || msg.Contains("payload injection"))))
            {
                return new DetectedIssue
                {
                    Title = "🔌 ALERTĂ CRITICĂ: Atac Fizic BadUSB / Rubber Ducky (HID Keystroke Injection)",
                    Severity = "Critical",
                    MitreTechniqueId = "T1200",
                    MitreTacticName = "Initial Access",
                    Explanation = $"A fost detectat un dispozitiv hardware neautorizat care simulează tastarea umană la viteză mare (BadUSB/Rubber Ducky) încercând injectarea de comenzi pe [{ev.MachineName}].",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 12. Furt Sesiuni, Infostealere & Bypass MFA (AiTM / Evilginx / Token Theft T1556 / T1539)
            if (msg.Contains("evilginx") || msg.Contains("evilproxy") || msg.Contains("lumma") || msg.Contains("redline") ||
                msg.Contains("stealc") || (msg.Contains("cookie") && (msg.Contains("theft") || msg.Contains("exfiltrat"))) ||
                (msg.Contains("appdata") && (msg.Contains("login data") || msg.Contains("web data")) && msg.Contains("sqlite")))
            {
                return new DetectedIssue
                {
                    Title = "🍪 ALERTĂ CRITICĂ: Furt Token-uri Sesiune & Bypass MFA (Infostealer / AiTM)",
                    Severity = "Critical",
                    MitreTechniqueId = "T1539",
                    MitreTacticName = "Credential Access",
                    Explanation = $"Tentativă de extragere a bazelor de date cu credențiale și cookie-uri de sesiune OAuth/MFA din browser pe [{ev.MachineName}].",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 13. Otrăvire Rețea Locală & Captură NTLM (LLMNR / NBT-NS Poisoning - Responder T1557.001)
            if (msg.Contains("responder.py") || msg.Contains("inveigh") || msg.Contains("ntlmrelayx") || msg.Contains("ntlm relay"))
            {
                return new DetectedIssue
                {
                    Title = "📡 ALERTĂ MARE: Otrăvire Rețea Locală & Captură NTLM (Responder / LLMNR Poisoning)",
                    Severity = "High",
                    MitreTechniqueId = "T1557.001",
                    MitreTacticName = "Credential Access",
                    Explanation = $"Activitate malițioasă de broadcast/spoofing LLMNR/NBT-NS detectată în subrețea pe [{ev.MachineName}]. Un atacator interceptează hash-urile de autentificare NTLMv2.",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 14. Abuz de Token-uri & Escaladare Privilegii (PrintSpoofer / Potato Exploits T1134)
            if (msg.Contains("printspoofer") || msg.Contains("juicypotato") || msg.Contains("godpotato") ||
                (msg.Contains("seimpersonateprivilege") && (msg.Contains("escalat") || msg.Contains("system"))))
            {
                return new DetectedIssue
                {
                    Title = "🥔 ALERTĂ CRITICĂ: Escaladare Privilegii prin Abuz Token-uri (Potato / SeImpersonate)",
                    Severity = "Critical",
                    MitreTechniqueId = "T1134.001",
                    MitreTacticName = "Privilege Escalation",
                    Explanation = $"Tentativă de escaladare la NT AUTHORITY\\SYSTEM prin exploatarea dreptului SeImpersonatePrivilege pe [{ev.MachineName}].",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 15. Reguli Ascunse Cloud & Exfiltrare Email (M365 Forwarding Rules T1114.003)
            if (msg.Contains("inboxrule") || msg.Contains("forwardingrule") || (msg.Contains("oauth") && msg.Contains("mail.readwrite")))
            {
                return new DetectedIssue
                {
                    Title = "☁️ ALERTĂ MARE: Reguli Ascunse de Redirecționare Email & Exfiltrare Cloud",
                    Severity = "High",
                    MitreTechniqueId = "T1114.003",
                    MitreTacticName = "Collection",
                    Explanation = $"Regulă automată suspectă de copiere/redirecționare a mesajelor de email către o destinație externă neautorizată pe [{ev.MachineName}].",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 16. CPU side channels cannot be measured from an event log: a keyword is only a hint to check by hand
            if (msg.Contains("rowhammer") || msg.Contains("zenbleed") || msg.Contains("cpu silicon"))
            {
                return new DetectedIssue
                {
                    Title = "[NEVERIFICAT - doar cuvinte cheie] Mesaj care pomenește un atac asupra procesorului",
                    Severity = "Low",
                    MitreTechniqueId = "",
                    MitreTacticName = "",
                    Explanation = $"Mesajul de pe [{ev.MachineName}] conține un cuvânt asociat atacurilor asupra procesorului (Rowhammer, Zenbleed). Un jurnal de evenimente nu poate arăta un astfel de atac; aceasta NU este o detecție, doar un indiciu de verificat manual.",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 17. Air-Gap Săritură & Exfiltrare Acustică / Ventilatoare (Fansmitter / Covert Channel T1048)
            if (msg.Contains("fansmitter") || msg.Contains("acoustic") || msg.Contains("ventilatoare") ||
                msg.Contains("pwm fan") || msg.Contains("air-gap covert channel") || msg.Contains("tempest"))
            {
                return new DetectedIssue
                {
                    Title = "[NEVERIFICAT - doar cuvinte cheie] Posibil canal acustic ascuns (Fansmitter)",
                    Severity = "Low",
                    MitreTechniqueId = "T1052",
                    MitreTacticName = "Exfiltration",
                    Explanation = $"Mesajul de pe [{ev.MachineName}] conține un cuvânt cheie asociat tehnicii Fansmitter. Nu există nicio măsurătoare acustică sau de hardware a ventilatoarelor; aceasta NU este o detecție confirmată de exfiltrare, doar un indiciu de verificat manual.",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            // 18. Comenzi de Test / Simulare - synthetic: labelled so it cannot be mistaken for a real detection.
            if (msg.Contains("simulare dfir") || msg.Contains("test alert"))
            {
                return new DetectedIssue
                {
                    Title = TestAlertTitle,
                    Severity = "High",
                    MitreTechniqueId = "T1059.001",
                    MitreTacticName = "Execution",
                    Explanation = $"ALERTĂ DE TEST, NU O DETECȚIE REALĂ. Mesajul de test primit de pe [{ev.MachineName}] a declanșat calea de alertare; nu indică vreo activitate malițioasă.",
                    IsTestAlert = true,
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            }

            return null;
        }

        private DetectedIssue? TrackFailedLogon(ParsedEvent ev)
        {
            var at = ev.TimeCreated == default ? DateTime.UtcNow : ev.TimeCreated.ToUniversalTime();
            var user = Field(ev.XmlData, "TargetUserName") ?? "(necunoscut)";
            var source = Field(ev.XmlData, "IpAddress") ?? Field(ev.XmlData, "WorkstationName") ?? "(sursă necunoscută)";
            var key = $"{ev.MachineName}|{source}";
            if (!_failures.TryGetValue(key, out var list)) _failures[key] = list = new();
            list.RemoveAll(f => at - f.Utc > FailureWindow);
            list.Add((user, at));
            if (_failures.Count > 500)
                foreach (var k in _failures.Where(p => p.Value.Count == 0 || at - p.Value[^1].Utc > FailureWindow).Select(p => p.Key).ToList()) _failures.Remove(k);

            int accounts = list.Select(f => f.User).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (accounts >= SprayAccountsThreshold)
                return new DetectedIssue
                {
                    Title = "🚨 ALERTĂ MARE: Parole încercate pe multe conturi din aceeași sursă (password spraying)",
                    Severity = "High",
                    MitreTechniqueId = "T1110.003",
                    MitreTacticName = "Credential Access",
                    Explanation = $"Din sursa {source}, pe [{ev.MachineName}], s-au încercat fără succes {accounts} conturi diferite în ultimele {FailureWindow.TotalMinutes:0} minute. " +
                                  "Cineva încearcă aceeași parolă pe multe conturi. Verificați ce calculator este sursa și dacă vreunul din conturi a reușit apoi să intre (eveniment 4624).",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            int same = list.Count(f => f.User.Equals(user, StringComparison.OrdinalIgnoreCase));
            if (same >= SameAccountThreshold)
                return new DetectedIssue
                {
                    Title = "⚠️ ALERTĂ MEDIE: Parolă greșită de mai multe ori pentru același cont",
                    Severity = "Medium",
                    MitreTechniqueId = "T1110.001",
                    MitreTacticName = "Credential Access",
                    Explanation = $"Contul {user} a primit {same} parole greșite din sursa {source} pe [{ev.MachineName}] în ultimele {FailureWindow.TotalMinutes:0} minute. " +
                                  "Poate fi un utilizator care și-a uitat parola sau cineva care o ghicește. Întrebați titularul contului; dacă nu a fost el, tratați ca incident.",
                    CreatedAt = DateTime.UtcNow,
                    RelatedEvents = new List<ParsedEvent> { ev }
                };
            return null;
        }
    }
}
