using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services.Network
{
    public class CountermeasureAction
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty; // "Isolate", "KillProcess", "BlockIoC", "RevokeAuth"
        public string PowerShellSnippet { get; set; } = string.Empty;
        public bool IsRecommended { get; set; } = true;
    }

    public class AttackerIntelligenceDetails
    {
        public string SourceIpOrDomain { get; set; } = string.Empty;
        public string LikelyActorName { get; set; } = string.Empty;
        public string ActorCountryOrOrigin { get; set; } = string.Empty;
        public string Motivation { get; set; } = string.Empty;
        public string TargetUserOrAccount { get; set; } = string.Empty;
        public string AttackProcessPath { get; set; } = string.Empty;
        public string AttackHashSha256 { get; set; } = string.Empty;
        public string KnownToolsUsed { get; set; } = string.Empty;
        public string DefenseRecommendation { get; set; } = string.Empty;
    }

    public class CountermeasurePlaybook
    {
        public string AttackCategory { get; set; } = string.Empty;
        public string ThreatLevel { get; set; } = "Critical";
        public string ImmediateObjective { get; set; } = string.Empty;
        public AttackerIntelligenceDetails AttackerIntel { get; set; } = new();
        public List<CountermeasureAction> Actions { get; set; } = new();
        public string ForensicsGuidance { get; set; } = string.Empty;
    }

    public class CyberAttackCountermeasureEngine
    {
        /// <summary>
        /// Generează planul de combatere și acțiuni specifice pentru un atac detectat.
        /// </summary>
        public CountermeasurePlaybook GeneratePlaybook(DetectedIssue alert, string hostname = "localhost")
        {
            var playbook = new CountermeasurePlaybook
            {
                ThreatLevel = alert.Severity,
                AttackerIntel = ExtractDynamicAttackerIntel(alert, hostname)
            };

            string titleLower = (alert.Title ?? string.Empty).ToLowerInvariant();
            string tech = (alert.MitreTechniqueId ?? string.Empty).ToUpperInvariant();

            // 1. Phishing & Social Engineering (T1566 / T1059)
            if (titleLower.Contains("phishing") || tech.StartsWith("T1566") || titleLower.Contains("download cradle"))
            {
                playbook.AttackCategory = "🎣 Tentativă de Phishing & Descărcare Payload";
                playbook.ImmediateObjective = "Blocarea accesului la serverul atacatorului, oprirea descărcării de malware și prevenirea furtului de credențiale.";
                playbook.ForensicsGuidance = "Verificați folderul %TEMP% și Downloads pentru fișiere .LNK, .ISO, .VBS sau .HTA. Extrageți domeniul din comanda curl/powershell și adăugați-l pe lista neagră.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🚫 Blochează Conexiunile către Domeniul / IP-ul de Phishing",
                    Description = "Adaugă regulă de blocare pe Windows Firewall și rescrie DNS Hosts pentru a preveni contactarea serverului C2 de phishing.",
                    ActionType = "BlockIoC",
                    PowerShellSnippet = "New-NetFirewallRule -DisplayName 'DFIR_Block_Phishing_C2' -Direction Outbound -Action Block -RemoteAddress Any",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Termină Procesul de Descărcare (PowerShell / mshta / curl)",
                    Description = "Oprește imediat procesul utilizat pentru descărcarea sau lansarea payload-ului de phishing.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Get-Process -Name 'powershell','mshta','curl' -ErrorAction SilentlyContinue | Stop-Process -Force",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🔑 Revocă Sesiunile & Forțează Resetarea Parolei",
                    Description = "În caz de inginerie socială sau phishing de credențiale, forțează resetarea parolei contului și invalidarea token-urilor de autentificare.",
                    ActionType = "RevokeAuth",
                    PowerShellSnippet = "Revoke-AzureADUserAllRefreshToken (sau net user [Utilizator] /logonpasswordchg:yes)",
                    IsRecommended = false
                });
            }
            // 2. Ransomware & Shadow Copy Deletion (T1490)
            else if (titleLower.Contains("ransomware") || tech == "T1490")
            {
                playbook.AttackCategory = "🚨 Atac de tip Ransomware (Criptare Date)";
                playbook.ImmediateObjective = "Izolarea instantanee a calculatorului din rețea pentru a opri propagarea laterală și protejarea share-urilor de rețea.";
                playbook.ForensicsGuidance = "Realizați un dump de memorie RAM înainte de oprirea stației pentru recuperarea eventualelor chei de decriptare din memorie.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛡️ IZOLEAZĂ GAZDA DIN REȚEA (TAIERE TRAFIC)",
                    Description = "Blochează complet toate conexiunile de rețea de intrare și ieșire pentru a opri răspândirea ransomware-ului.",
                    ActionType = "Isolate",
                    PowerShellSnippet = "New-NetFirewallRule -DisplayName 'DFIR_EMERGENCY_ISOLATE' -Direction Inbound,Outbound -Action Block -Profile Any",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Oprire Forțată Procese Suspecte & VssAdmin",
                    Description = "Termină procesele vssadmin, bcdedit și executabilele nesemnate din AppData/Temp.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Get-Process | Where-Object { $_.Path -like '*AppData*' -or $_.Name -match 'vssadmin|bcdedit' } | Stop-Process -Force",
                    IsRecommended = true
                });
            }
            // 3. Furt Credențiale / Mimikatz / LSASS Dump (T1003)
            else if (titleLower.Contains("lsass") || titleLower.Contains("mimikatz") || tech.StartsWith("T1003"))
            {
                playbook.AttackCategory = "🚨 Tentativă Furt Credențiale (Mimikatz / LSASS)";
                playbook.ImmediateObjective = "Protejarea bazei de date de securitate SAM/LSA și resetarea imediată a conturilor administrative active pe stație.";
                playbook.ForensicsGuidance = "Activați Windows Defender Credential Guard (LSA Protection) prin registru pentru a preveni citirea directă a memoriei LSASS.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Oprește Procesul Atacator & Șterge Uneltele de Dump",
                    Description = "Termină procesul care a solicitat acces la memoria LSASS și curăță folderul din care a fost lansat.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Get-Process | Where-Object { $_.Path -match 'mimikatz|procdump|sqldumper' } | Stop-Process -Force",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛡️ Activează Protecția LSA (RunAsPPL)",
                    Description = "Forțează pornirea LSASS ca Protected Process Light (PPL) pentru a bloca orice tentativă viitoare de injectare.",
                    ActionType = "Harden",
                    PowerShellSnippet = "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa' -Name 'RunAsPPL' -Value 1 -Type DWord",
                    IsRecommended = true
                });
            }
            // 4. Exploits Kernel Ring 0 & BYOVD (T1068 / T1543.003)
            else if (titleLower.Contains("driver kernel") || titleLower.Contains("byovd") || tech.Contains("T1068"))
            {
                playbook.AttackCategory = "☣️ Exploit Kernel Ring 0 & Driver Vulnerabil (BYOVD)";
                playbook.ImmediateObjective = "Oprirea imediată a serviciului driver vulnerabil, blocarea comunicării C2 și aplicarea politicii HVCI.";
                playbook.ForensicsGuidance = "Verificați folderul C:\\Windows\\System32\\drivers pentru fișiere .sys nesemnate sau semnate cu certificate revocate. Rulați 'fltmc' și 'sc.exe query type= driver'.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛡️ Izolează Gazda din Rețea (Oprire Exfiltrare)",
                    Description = "Taie orice conexiune externă pentru a împiedica atacatorul să preia controlul prin driverul kernel.",
                    ActionType = "Isolate",
                    PowerShellSnippet = "New-NetFirewallRule -DisplayName 'DFIR_Block_BYOVD' -Direction Outbound -Action Block",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Oprește & Șterge Serviciul Driverului Vulnerabil",
                    Description = "Oprește serviciul kernel instalat înainte de a apuca să dezactiveze componentele de securitate.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "sc.exe stop [NumeDriver]; sc.exe delete [NumeDriver]",
                    IsRecommended = true
                });
            }
            // 5. Injecție Memorie & Process Hollowing (T1055)
            else if (titleLower.Contains("hollowing") || titleLower.Contains("injecție") || tech.StartsWith("T1055"))
            {
                playbook.AttackCategory = "💉 Injecție Exclusivă în Memorie & Process Hollowing (T1055)";
                playbook.ImmediateObjective = "Neutralizarea procesului gazdă compromis în RAM și tăierea canalului de comunicare C2.";
                playbook.ForensicsGuidance = "Efectuați un Process Dump complet pe PID-ul afectat pentru a extrage payload-ul necriptat direct din memoria RAM înainte de terminare.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Neutralizează Procesul Gazdă Compromis (Kill Process Tree)",
                    Description = "Oprește forțat procesul legitim injectat (ex: notepad.exe, svchost.exe neautorizat) și procesele copil create.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Stop-Process -Id [TargetPID] -Force",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛡️ Izolează Gazda pe Windows Firewall",
                    Description = "Blochează traficul generat de payload-ul din memorie către IP-ul de comandă (C2).",
                    ActionType = "Isolate",
                    PowerShellSnippet = "New-NetFirewallRule -DisplayName 'DFIR_Block_Memory_C2' -Direction Outbound -Action Block",
                    IsRecommended = true
                });
            }
            // 6. BadUSB / Rubber Ducky (T1052.001)
            else if (titleLower.Contains("badusb") || titleLower.Contains("rubber ducky") || tech.Contains("T1052"))
            {
                playbook.AttackCategory = "🔌 Atac Fizic BadUSB / Rubber Ducky (HID Keystroke Injection)";
                playbook.ImmediateObjective = "Blocarea portului USB, oprirea shell-ului deschis prin injectare de taste și aplicarea politicii P16-P18.";
                playbook.ForensicsGuidance = "Inspectați jurnalele SetupAPI.dev.log pentru VID/PID-ul dispozitivului USB introdus. Deconectați fizic mediul extern.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Neutralizează Imediat Shell-urile Deschise (PowerShell / CMD)",
                    Description = "Oprește toate procesele de consolă generate de atacul BadUSB.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Stop-Process -Name 'powershell','cmd' -Force",
                    IsRecommended = true
                });
            }
            // 7. Furt Token-uri Sesiune & Infostealere (T1539 / T1556)
            else if (titleLower.Contains("infostealer") || titleLower.Contains("token") || titleLower.Contains("cookie") || tech.Contains("T1539"))
            {
                playbook.AttackCategory = "🍪 Furt Sesiuni, Infostealere & Bypass MFA (AiTM / Stealer)";
                playbook.ImmediateObjective = "Invalidarea token-urilor de autentificare cloud, oprirea procesului stealer și resetarea parolelor.";
                playbook.ForensicsGuidance = "Verificați fișierele din %LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default și trimiteți revocare token în Entra ID.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🔑 Revocă Toate Sesiunile & Token-urile Active (M365 / Cloud)",
                    Description = "Forțează deconectarea pe toate dispozitivele și cere re-autentificare completă cu MFA.",
                    ActionType = "RevokeAuth",
                    PowerShellSnippet = "Revoke-AzureADUserAllRefreshToken -ObjectId [UserObjectId]",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Neutralizează Procesul Stealer & Blochează Exfiltrarea",
                    Description = "Termină procesul care accesează fișierele de profil ale browserului.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Stop-Process -Id [StealerPID] -Force",
                    IsRecommended = true
                });
            }
            // 8. Otrăvire Rețea Locală (LLMNR / Responder T1557.001)
            else if (titleLower.Contains("responder") || titleLower.Contains("llmnr") || tech.Contains("T1557"))
            {
                playbook.AttackCategory = "📡 Otrăvire Rețea Locală & Captură NTLM (Responder / Poisoning)";
                playbook.ImmediateObjective = "Blocarea traficului LLMNR/NBT-NS pe adaptor și identificarea IP-ului atacatorului în LAN.";
                playbook.ForensicsGuidance = "Verificați tabela ARP ('arp -a') pentru a identifica adresa MAC a nodului care răspunde cu broadcast fals.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛡️ Dezactivează LLMNR & NetBIOS pe Firewall",
                    Description = "Blochează porturile UDP 5355 (LLMNR) și 137 (NetBIOS) pentru a opri scurgerea hash-urilor NTLM.",
                    ActionType = "BlockIoC",
                    PowerShellSnippet = "New-NetFirewallRule -DisplayName 'Block_LLMNR' -Protocol UDP -LocalPort 5355,137 -Action Block",
                    IsRecommended = true
                });
            }
            // 9. Potato Exploits (T1134)
            else if (titleLower.Contains("potato") || titleLower.Contains("seimpersonate") || tech.Contains("T1134"))
            {
                playbook.AttackCategory = "🥔 Escaladare Privilegii prin Abuz Token-uri (Potato / SeImpersonate)";
                playbook.ImmediateObjective = "Oprirea procesului care încearcă imitarea token-ului SYSTEM și carantinarea serviciului afectat.";
                playbook.ForensicsGuidance = "Inspectați procesele care dețin SeImpersonatePrivilege (ex: conturi de serviciu IIS, SQL).";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Neutralizează Procesul de Escaladare & Fiii",
                    Description = "Termină forțat arborele de procese care încearcă crearea token-ului fals.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Stop-Process -Id [TargetPID] -Force",
                    IsRecommended = true
                });
            }
            // 10. Atac pe Siliciu CPU & Rowhammer (T1499)
            else if (titleLower.Contains("siliciu") || titleLower.Contains("rowhammer") || titleLower.Contains("spectre") || tech.Contains("CPU Silicon"))
            {
                playbook.AttackCategory = "🔬 Atac pe Siliciu CPU & Memorie Fizică (Rowhammer / Spectre / Side-Channel)";
                playbook.ImmediateObjective = "Neutralizarea procesului de hammering/cache-timing, activarea mitigărilor microcod și flush memorie RAM.";
                playbook.ForensicsGuidance = "Verificați versiunea microcodului CPU cu 'Get-CimInstance Win32_Processor'. Asigurați-vă că mitigările Spectre/Meltdown sunt active în registry.";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Oprește Procesul de Hammering / Cache-Timing",
                    Description = "Termină procesul suspect cu activitate anormală de bucle de memorie sau măsurători de ceas de înaltă precizie.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Stop-Process -Id [TargetPID] -Force",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛡️ Activează Mitigările Speculative Execution în Registru",
                    Description = "Setează cheile de registru recomandate de Microsoft pentru activarea izolării de memorie (KVA Shadow / Retpoline).",
                    ActionType = "Harden",
                    PowerShellSnippet = "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management' -Name 'FeatureSettingsOverride' -Value 0 -Type DWord",
                    IsRecommended = true
                });
            }
            // 11. Air-Gap & Exfiltrare Acustică Ventilatoare (Fansmitter / T1048)
            else if (titleLower.Contains("acustic") || titleLower.Contains("ventilatoare") || titleLower.Contains("fansmitter") || tech.Contains("Air-Gap"))
            {
                playbook.AttackCategory = "🔊 Exfiltrare Acustică prin Ventilatoare (Air-Gap Jumping / Fansmitter)";
                playbook.ImmediateObjective = "Resetarea controller-ului PWM hardware al ventilatoarelor, oprirea procesului de modulație și blocarea canalelor ascunse.";
                playbook.ForensicsGuidance = "Inspectați procesele care apelează API-uri de control al vitezei ventilatoarelor (ex: WinRing0, OpenHardwareMonitor, ACPI calls).";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Neutralizează Procesul de Modulație Acustică",
                    Description = "Oprește procesul care controlează turația ventilatoarelor pentru emiterea de cod Morse/audio.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Stop-Process -Id [TargetPID] -Force",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛡️ Resetează Politica de Răcire Hardware (Conform HG 585 / NATO TEMPEST)",
                    Description = "Restabilește controlul automat BIOS/UEFI asupra ventilatoarelor și blochează apelurile I/O de la utilizator.",
                    ActionType = "Harden",
                    PowerShellSnippet = "powercfg /setactive SCHEME_BALANCED",
                    IsRecommended = true
                });
            }
            // 12. Default / Generic Hacking Incident
            else
            {
                playbook.AttackCategory = "⚠️ Incident Cibernetic & Activitate Suspectă";
                playbook.ImmediateObjective = "Limitarea accesului atacatorului, colectarea artefactelor de memorie și blocarea persistenței.";
                playbook.ForensicsGuidance = "Verificați cheile de autorun din Registru (Run/RunOnce) și Task-urile programate (Scheduled Tasks).";

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛡️ Izolează Gazda din Rețea",
                    Description = "Aplică profil de carantină pe adaptorul de rețea.",
                    ActionType = "Isolate",
                    PowerShellSnippet = "New-NetFirewallRule -DisplayName 'DFIR_Emergency_Isolation' -Direction Outbound -Action Block",
                    IsRecommended = true
                });

                playbook.Actions.Add(new CountermeasureAction
                {
                    Title = "🛑 Termină Procesele Copil Neautorizate",
                    Description = "Oprește procesele suspecte create în ultimele 5 minute.",
                    ActionType = "KillProcess",
                    PowerShellSnippet = "Stop-Process -Id [TargetPID] -Force",
                    IsRecommended = true
                });
            }

            return playbook;
        }

        private AttackerIntelligenceDetails ExtractDynamicAttackerIntel(DetectedIssue alert, string hostname)
        {
            var intel = new AttackerIntelligenceDetails();
            string rawContent = alert.Explanation ?? string.Empty;
            string userHost = $"{Environment.UserName} @ {hostname}";

            if (alert.RelatedEvents != null && alert.RelatedEvents.Count > 0)
            {
                var ev = alert.RelatedEvents[0];
                rawContent += " " + (ev.Message ?? string.Empty);
                if (!string.IsNullOrEmpty(ev.MachineName)) userHost = $"{Environment.UserName} @ {ev.MachineName}";
            }

            intel.TargetUserOrAccount = userHost;

            // 1. Extract URL or IP
            var urlMatch = Regex.Match(rawContent, @"https?://[a-zA-Z0-9\-\.]+(?::\d+)?(?:/[^\s""'>]*)?", RegexOptions.IgnoreCase);
            var ipMatch = Regex.Match(rawContent, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");

            if (urlMatch.Success)
            {
                intel.SourceIpOrDomain = urlMatch.Value;
            }
            else if (ipMatch.Success)
            {
                intel.SourceIpOrDomain = $"IP: {ipMatch.Value} (Remote C2)";
            }
            else
            {
                intel.SourceIpOrDomain = "nedeterminată (alerta nu conține o adresă)";
            }

            // 2. Extract Process
            if (rawContent.Contains("vssadmin", StringComparison.OrdinalIgnoreCase)) intel.AttackProcessPath = "vssadmin.exe (Shadow Copy Deletion)";
            else if (rawContent.Contains("certutil", StringComparison.OrdinalIgnoreCase)) intel.AttackProcessPath = "certutil.exe (URL Cache Downloader)";
            else if (rawContent.Contains("powershell", StringComparison.OrdinalIgnoreCase)) intel.AttackProcessPath = "powershell.exe (ScriptBlock Execution)";
            else if (rawContent.Contains("mshta", StringComparison.OrdinalIgnoreCase)) intel.AttackProcessPath = "mshta.exe (HTML Application Host)";
            else if (rawContent.Contains("curl", StringComparison.OrdinalIgnoreCase)) intel.AttackProcessPath = "curl.exe (Web Payload Downloader)";
            else intel.AttackProcessPath = "nedeterminat (alerta nu numește procesul)";

            // 3. No file is available here: the alert text is not a sample, so no "malware hash" is reported.
            intel.AttackHashSha256 = "";

            // 4. Attribution is NOT inferred from a technique: the same MITRE technique is used by many unrelated actors.
            //    Only typical tools and a defensive recommendation are given; attribution needs confirmed infrastructure,
            //    malware and context, established by the investigator.
            string tech = (alert.MitreTechniqueId ?? string.Empty).ToUpperInvariant();
            intel.LikelyActorName = "Neatribuit (tehnica nu identifică un actor; atribuirea cere probe confirmate)";
            intel.ActorCountryOrOrigin = "necunoscută";
            if (tech.Contains("T1490") || rawContent.Contains("ransomware", StringComparison.OrdinalIgnoreCase))
            {
                intel.Motivation = "posibil: extorcare prin criptare (tipar de ransomware)";
                intel.KnownToolsUsed = "unelte tipice tehnicii: vssadmin, wbadmin, bcdedit";
                intel.DefenseRecommendation = "Izolați stația de share-uri și verificați copiile de rezervă offline.";
            }
            else if (tech.Contains("T1566") || rawContent.Contains("phishing", StringComparison.OrdinalIgnoreCase))
            {
                intel.Motivation = "posibil: furt de credențiale / acces inițial";
                intel.KnownToolsUsed = "unelte tipice tehnicii: atașamente sau linkuri, certutil, mshta, PowerShell";
                intel.DefenseRecommendation = "Blocați URL-ul/adresa din alertă și resetați sesiunile utilizatorului.";
            }
            else if (tech.Contains("T1003") || rawContent.Contains("lsass", StringComparison.OrdinalIgnoreCase))
            {
                intel.Motivation = "posibil: obținerea de credențiale";
                intel.KnownToolsUsed = "unelte tipice tehnicii: mimikatz, procdump, comsvcs.dll MiniDump";
                intel.DefenseRecommendation = "Activați LSA Protection (RunAsPPL) și schimbați parolele conturilor privilegiate.";
            }
            else
            {
                intel.Motivation = "nedeterminată";
                intel.KnownToolsUsed = "nedeterminate";
                intel.DefenseRecommendation = "Analizați procesul și conexiunile în fila „Izolare procese suspecte”.";
            }

            return intel;
        }
    }
}
