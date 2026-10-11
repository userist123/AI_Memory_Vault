# Cercetare 2026-10-11: cum sunt compromise efectiv stațiile Windows în 2024–2026 și ce urme lasă

Stare: cercetare web, `DOCUMENT_VERIFIED` doar unde sursa e citată. Nimic de aici nu a fost rulat pe o stație de test și nicio regulă nu a fost implementată.
Marcaje: **[NESIGUR]** = sursă secundară, contradictorie sau lipsă; **[NECITAT]** = din practica DFIR curentă, fără sursă găsită în această sesiune, de verificat în laborator (`FORENSIC_TEST_LAB.md`).
Notații pentru urme: **(implicit)** = Windows le produce fără configurare; **(config)** = necesită politică de audit, canal activat sau Sysmon; **(server-off)** = implicit oprit pe Windows Server.
Constrângeri: LogAnalyzer e instrument read-only, offline. Documentul nu conține identificatori ai organizației care folosește instrumentul. Victimele publice din România sunt descrise generic în text; linkurile de presă rămân doar ca citare.

---

## 0. Rezumat de context (ce spun rapoartele anuale)

| Sursă | Constatare relevantă pentru stațiile Windows |
|---|---|
| Mandiant M-Trends 2025 (date 2024) | Exploatarea vulnerabilităților: 33% din vectorii inițiali, al cincilea an la rând pe primul loc; credențialele furate au urcat de la 10% la 16% și au depășit phishingul (14%), mai ales din cauza infostealerelor; la ransomware, brute force a fost primul vector (26%) ([Mandiant](https://cloud.google.com/blog/topics/threat-intelligence/m-trends-2025/), [Infosecurity](https://www.infosecurity-magazine.com/news/vulnerability-credential-initial)) |
| Mandiant M-Trends 2026 (date 2025) | Exploatarea: 32%, încă primul loc; vishingul a sărit la 11% și e al doilea vector; timpul median de staționare a urcat la 14 zile; predarea accesului de la brokerul de acces inițial la al doilea grup s-a redus de la peste 8 ore (2022) la 22 de secunde (2025); brokerii folosesc malvertising și ClickFix ([Mandiant](https://cloud.google.com/blog/topics/threat-intelligence/m-trends-2026), [SecurityWeek](https://www.securityweek.com/m-trends-2026-initial-access-handoff-shrinks-from-hours-to-22-seconds/)) |
| CrowdStrike GTR 2025 / 2026 | 79% din detecțiile din 2024 au fost fără malware (mâini pe tastatură, instrumente legitime); vishing +442% între H1 și H2 2024; timpul mediu de „breakout” eCrime a scăzut de la 48 min (2024) la 29 min (2025), cel mai rapid 27 s ([CrowdStrike 2025](https://www.crowdstrike.com/en-us/blog/crowdstrike-2025-global-threat-report-findings/), [CrowdStrike 2026](https://crowdstrike.com/en-us/press-releases/2026-crowdstrike-global-threat-report)) |
| Sophos Active Adversary 2025 / 2026 | 2025: credențialele compromise, prima cauză (41%); RDP abuzat în 84% din cazuri; 56% din cazuri = servicii externe (VPN, firewall) cu conturi valide; timp median până la AD: 11 ore ([Channel Insider](https://www.channelinsider.com/security/sophos-active-adversary-2025/), [Sophos](https://www.sophos.com/zh-cn/press/press-releases/2025/04/sophos-report-56-sophos-ir-and-mdr-cases-adversaries-logged-instead)). 2026: 67% din incidente au cauză de identitate; brute force 15,6% aproape egal cu exploatarea 16%; lipsa MFA în 59% din cazuri; staționare medie 3 zile ([Sophos 2026](https://www.sophos.com/en-us/press/press-releases/sophos-active-adversary-report-2026-identity-attacks-dominate-as-threat-groups-proliferate)) |
| Microsoft Digital Defense Report 2025 | Atacurile pe identitate +32% (iul. 2024–iun. 2025); peste 97% încep cu ghicirea masivă a parolelor; infostealerele (ex. Lumma) devin vector inițial, nu doar instrument post-compromitere; social engineering la helpdesk ([Microsoft](https://news.microsoft.com/source/asia/2026/01/07/microsoft-releases-2025-digital-defense-report-highlighting-the-changing-cyber-threat-landscape-and-the-importance-of-security-in-the-ai-era/), [The Record](https://therecord.media/microsoft-warns-of-surge-identity-hacks-passwords)) |
| Red Canary TDR 2026 (date 2025) | Top 10 tehnici: T1078.004, T1059.001, T1059.003, T1530, T1105, T1114.003, T1047, T1204.004, T1564.008, T1027 ([Red Canary](https://redcanary.com/threat-detection-report/techniques/)); 6 din top 10 amenințări sunt legate de „paste and run” (ClickFix) ([Zscaler/Red Canary](https://www.zscaler.com/blogs/cybersecurity-best-practices/scarlet-goldfinch-clickfix)); mshta revenit în top în raportul 2025, din cauza fake CAPTCHA ([Red Canary 2025](https://redcanary.com/blog/threat-detection/2025-threat-detection-report)) |
| ENISA Threat Landscape 2025 | 4.875 incidente (iul. 2024–iun. 2025); phishing ~60% din intruziuni, exploatare ~21%; 68% din intruziuni urmate de malware; administrația publică, sectorul cel mai vizat (38,2%) ([ENISA](https://www.enisa.europa.eu/news/etl-2025-eu-consistently-targeted-by-diverse-yet-convergent-threat-groups), [Infosecurity](https://www.infosecurity-magazine.com/news/phishing-dominates-euwide/)) |
| Cisco Talos IR Q4 2025 / Q1 2026 | Exploatarea serviciilor publice: ~40% (Q4) față de 62% (Q3, valul ToolShell); în Q1 2026 phishingul revine pe primul loc (peste o treime) ([Cybersecurity Dive](https://www.cybersecuritydive.com/news/cisco-threat-report-exploitation-phishing/810977/)) |
| DNSC (România), raport 2024 | Nivel de amenințare „ridicat”, ~27 mil. evenimente din senzori; malware +286,8%, fraude +40,2% ([Agerpres](https://agerpres.ro/economic/2025/08/31/aproximativ-27-080-de-milioane-de-atacuri-cyber-extrase-din-senzorii-de-detectie-ai-dnsc-in-2024-rap--1480071), [G4Media](https://www.g4media.ro/raport-al-directoratului-national-pentru-securitate-cibernetica-fraudele-informatice-au-crescut-in-romania-cu-peste-40-in-2024-atacurile-malware-majorare-cu-2868.html)); ransomware Phobos/Backmydata pe un furnizor de software spitalicesc (feb. 2024, peste 20 de spitale afectate, Phobos se răspândește tipic prin RDP) ([TVR Info](https://tvrinfo.ro/dnsc-recomanda-cu-fermitate-ca-nimeni-sa-nu-plateasca-rascumpararea-catre-atacatorii-sistemului-informatic-hipocrate/), [HotNews](https://hotnews.ro/26-de-spitale-atacate-informatic-care-este-compania-care-furnizeaza-solutia-hipocrate-17118)); Lynx pe un furnizor de energie (dec. 2024), DNSC a publicat YARA și IOC ([BleepingComputer](https://www.bleepingcomputer.com/news/security/lynx-ransomware-behind-electrica-energy-supplier-cyberattack/), [Agerpres](https://agerpres.ro/economic/2024/12/11/dnsc-atacul-cibernetic-asupra-electrica-a-apartinut-gruparii-lynx-ransomware-expertii-recomanda-util--1401911)); actori pro-ruși scanează zilnic infrastructura energetică ([Europa Liberă](https://romania.europalibera.org/amp/seful-dnsc-actori-pro-rusia-scaneaza-zilnic-de-mii-de-ori-infrastructurile-it-ale-companiilor-energetice-din-romania/33482002.html)) |
| MITRE ATT&CK v17 / v18 | v17 (apr. 2025): platformă ESXi, consolidare Hijack Execution Flow, Network → Network Devices ([MITRE v17](https://attack.mitre.org/resources/updates/updates-april-2025/), [Help Net](https://www.helpnetsecurity.com/2025/04/23/released-mitre-attck-v17-0-now-with-esxi-attack-ttps/)); T1219.002 Remote Desktop Software ca sub-tehnică ([MITRE T1219.002](https://attack.mitre.org/techniques/T1219/002/)); T1204.004 Malicious Copy and Paste creat din cauza ClickFix ([Red Canary midyear](https://redcanary.com/blog/threat-detection/2025-threat-detection-report-midyear/)). v18 (oct. 2025): secțiunea „Detection” înlocuită cu **Detection Strategies** + **Analytics**, Data Sources depreciate, Data Components refăcute ([MITRE v18](https://attack.mitre.org/resources/updates/updates-october-2025/)) |

**Consecință pentru LogAnalyzer:** maparea MITRE din aplicație trebuie (a) să folosească ID-urile v17+ (`T1204.004`, `T1219.002`), (b) să lege fiecare regulă de un „Analytic” v18 (artefact + câmpuri), nu de vechiul „Data Source”. **[NESIGUR]**: nu am verificat ce versiune ATT&CK are încărcată aplicația (`ItemMeaning.cs` declară 149 de tehnici, conform ultimului commit).

### Cazuri DFIR Report folosite ca „adevăr de teren” (2025–2026)

| Caz | Acces inițial | Urme-cheie | Timp până la criptare |
|---|---|---|---|
| RansomHub, iun. 2025 | Password spray pe RDP expus, 6 conturi reușite (4624) | CredentialsFileView (5379), Mimikatz DCSync (4662), Atera + Splashtop ca servicii (7045), netscan cu fișiere `delete.me`, rclone SFTP pe 443, `vssadmin delete shadows`, `wevtutil cl` | ~118 h ([DFIR Report](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/)) |
| Lynx, dec. 2025 | Logon RDP cu credențiale valide, fără brute force (4624 tip 3 apoi 10, `WorkstationName` străin) | conturi „look-alike” (`administratr`) adăugate în Domain Admins, AnyDesk instalat pe DC, NetExec, 7-Zip, upload pe temp.sh (istoric browser), joburi Veeam șterse | ~178 h ([DFIR Report](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/)) |
| KongTuke FileFix → Interlock RAT, iul. 2025 | Fake CAPTCHA, utilizatorul lipește comanda | `powershell -ep Bypass -w H`, `php.exe` din `%AppData%\Roaming\php\`, cheie Run în HKCU, C2 prin `trycloudflare.com` | n/a ([DFIR Report](https://thedfirreport.com/2025/07/14/kongtuke-filefix-leads-to-new-interlock-rat-variant/)) |
| Lunar Spider, sep. 2025 | JS deghizat în formular fiscal → MSI → `rundll32` | Run key „Update”, task `SchedulerLsass`, UAC bypass `ms-settings`, `C:\Unattend.xml` cu parolă DA, LSASS 0x1010 apoi 0x1fffff, Zerologon, rclone redenumit `sihosts.exe` | ~2 luni ([DFIR Report](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/)) |
| Bumblebee + AdaptixC2 → Akira, 2025/2026 | Căutare Bing → domeniu fals → MSI troianizat semnat cu certificat revocat | side-loading `consent.exe` + `msimg32.dll`, `wbadmin` pentru `ntds.dit`, `psql.exe` pe tabela Veeam `credentials`, lsassy (comsvcs MiniDump), RustDesk/cloudflared ca servicii, `ssh -R`, FileZilla SFTP 77 GB, BYOVD | ~44 h ([DFIR Report](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)) |
| ActiveMQ → LockBit, feb. 2026 | CVE-2023-46604 (`java.exe` părinte) | `certutil` descarcă stager, `getsystem` prin pipe numit, LSASS 0x1010 cu CallTrace UNKNOWN, AnyDesk serviciu, 1102 + 104, Defender dezactivat | ~19 zile ([DFIR Report](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)) |
| Confluence → ELPACO / LockBit, 2025 | CVE-2023-22527 pe server Windows | (vezi rapoartele) | ([DFIR Report index](https://thedfirreport.com/reports/)) |

Tipar comun (6 din 7 cazuri): **acces cu cont valid sau exploit pe serviciu expus → recunoaștere cu netscan/AdFind/nltest → furt de credențiale (LSASS, NTDS, Veeam) → RMM ca serviciu → RDP lateral → exfiltrare cu rclone/SFTP/site de upload → ștergere backup + shadow copies → criptare manuală prin RDP.**

---

## 1. Top 30 tehnici după prevalență (2024–2026)

Ordinea combină frecvența din rapoartele de mai sus (Red Canary, Sophos, Mandiant, CrowdStrike) cu prezența în cazurile DFIR Report. Nu e un clasament statistic unic **[NESIGUR]**: fiecare furnizor măsoară altceva (clienți, cazuri IR, detecții).

Câmpurile din evenimente sunt numele XML din `EventData`. Sysmon (ID-uri `Sysmon 1/3/7/8/10/11/13/22/23`) apare doar dacă stația îl are instalat **(config)**.

---

### 1. Conturi valide — T1078 (.002 domeniu, .003 local, .004 cloud)

- **De ce e în top:** prima cauză la Sophos (41% în 2025, 67% identitate în 2026), 16% la Mandiant, T1078.004 locul 1 la Red Canary ([Sophos 2026](https://www.sophos.com/en-us/press/press-releases/sophos-active-adversary-report-2026-identity-attacks-dominate-as-threat-groups-proliferate), [Mandiant](https://cloud.google.com/blog/topics/threat-intelligence/m-trends-2025/), [Red Canary](https://redcanary.com/threat-detection-report/techniques/)).
- **Urme Windows:**
  - Security 4624 **(implicit pentru succes)**: `TargetUserName`, `LogonType` (2/3/7/10/11), `IpAddress`, `WorkstationName`, `AuthenticationPackageName`, `ElevatedToken`. În cazul Lynx, `WorkstationName` era un nume generic `DESKTOP-xxxxxxx` străin de domeniu ([DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/)); în cazul Akira apar `WORK` și `kali` ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
  - 4648 (logon cu credențiale explicite, `runas`) și 4672 (privilegii speciale) **(implicit)** **[NECITAT]**.
  - Fișiere de răspuns cu parole: `C:\Unattend.xml`, `C:\Windows\Panther\Unattend.xml` — folosite de Lunar Spider ([DFIR Report](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/)).
  - T1078.004 (cloud) nu lasă urme pe stație în afara cookie-urilor/tokenurilor din profilul de browser; e în afara perimetrului principal al aplicației.
- **Idee de detecție:** logon reușit (4624 tip 10 sau 3) de la un `WorkstationName` care nu există în inventarul stațiilor sau care se potrivește cu modele implicite (`DESKTOP-[A-Z0-9]{7}`, `WIN-…`, `kali`, `WORK`), plus `ElevatedToken=Yes`; prima apariție a unui IP sursă pentru un cont privilegiat.
- **Fals pozitive:** laptopuri noi neînregistrate, VPN cu NAT, conturi de serviciu.
- **Pentru controlor:** cineva a intrat cu o parolă adevărată, deci sistemul nu a „văzut” un atac, ci un utilizator obișnuit venit de pe un calculator necunoscut.

### 2. Servicii de acces la distanță expuse / RDP — T1133, T1021.001

- **De ce e în top:** RDP abuzat în 84% din cazurile Sophos 2025; 56% din cazuri au început prin servicii externe cu conturi valide ([Channel Insider](https://www.channelinsider.com/security/sophos-active-adversary-2025/)); Phobos/Backmydata, ransomware-ul din atacul spitalelor din România, se răspândește tipic prin RDP ([TVR Info](https://tvrinfo.ro/dnsc-recomanda-cu-fermitate-ca-nimeni-sa-nu-plateasca-rascumpararea-catre-atacatorii-sistemului-informatic-hipocrate/)).
- **Urme Windows:**
  - `Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational` 1149 **(implicit)**: `User`, `Domain`, `Source Network Address`. Înseamnă conexiune autentificată la nivel de rețea, nu neapărat logon complet ([Andrea Fortuna](https://andreafortuna.org/2020/06/04/windows-forensic-analysis-some-thoughts-on-rdp-related-event-ids)).
  - `…LocalSessionManager/Operational` 21 (logon), 22 (shell), 24 (deconectare), 25 (reconectare) **(implicit)**; Security 4778/4779 (reconectare/deconectare) ([Velociraptor RDPAuth](https://docs.velociraptor.app/artifact_references/pages/windows.eventlogs.rdpauth/)).
  - 4624 `LogonType=10` (sau 7 la deblocare; în cazul Lynx, tip 7 a arătat reutilizarea unei sesiuni pe DC) ([DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/)).
  - RDP prin tunel (cloudflared, RustDesk) apare ca 4624 de la `127.0.0.1` sau tip 2 ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
  - Registru: `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections` (0 = RDP activat); RDP deschis în firewall printr-un `rdp.bat` în cazul LockBit ([DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)); istoricul RDP de ieșire în `NTUSER.DAT\Software\Microsoft\Terminal Server Client\Servers` și `Default` **[NECITAT]**.
- **Idee de detecție:** corelare 1149 → 4624 tip 10 în ≤ 60 s din IP public sau din `127.0.0.1`; RDP activat (modificare `fDenyTSConnections`) urmat de logon RDP; un cont care face RDP pe ≥ 3 servere în < 30 min (fan-out).
- **Fals pozitive:** administratori, jump-server, helpdesk.
- **Pentru controlor:** cineva a lucrat pe acest calculator „de la distanță”, ca și cum ar fi stat la tastatură; trebuie să știm cine și de unde.

### 3. Ghicirea parolelor (brute force / password spraying) — T1110.001, T1110.003

- **De ce e în top:** primul vector la ransomware la Mandiant 2025 (26%); 15,6% la Sophos 2026; peste 97% din atacurile pe identitate observate de Microsoft încep așa ([Infosecurity](https://www.infosecurity-magazine.com/news/vulnerability-credential-initial), [Sophos 2026](https://www.sophos.com/en-us/press/press-releases/sophos-active-adversary-report-2026-identity-attacks-dominate-as-threat-groups-proliferate), [The Record](https://therecord.media/microsoft-warns-of-surge-identity-hacks-passwords)). Caz RansomHub: ~4 ore de spray pe RDP, 6 conturi reușite ([DFIR Report](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/)).
- **Urme Windows:** 4625 **(implicit pe Windows 10/11 pentru eșec [NESIGUR pe Server])**: `TargetUserName`, `IpAddress`, `Status`/`SubStatus` (`0xC000006A` parolă greșită, `0xC0000064` utilizator inexistent), `LogonType`; 4740 blocare cont; pe DC 4771 (Kerberos pre-auth eșuat) și 4776 (NTLM) **[NECITAT]**.
- **Idee de detecție:** spray = același `IpAddress`, ≥ 10 `TargetUserName` distincte cu 4625 în 30 min, urmat de un 4624 din același IP; brute force = același utilizator, ≥ 20 eșecuri în 10 min.
- **Fals pozitive:** parole expirate salvate în aplicații, scanere de vulnerabilități autorizate, telefoane cu parolă veche.
- **Pentru controlor:** cineva a încercat multe parole la rând până a nimerit una; dacă după încercări apare o intrare reușită, contul trebuie considerat compromis.

### 4. Exploatarea aplicațiilor expuse — T1190

- **De ce e în top:** primul vector la Mandiant 6 ani la rând (33% → 32%); 40–62% la Talos; dispozitive de margine (Palo Alto, Ivanti, Fortinet), Confluence, ActiveMQ, SharePoint „ToolShell” ([Mandiant 2026](https://cloud.google.com/blog/topics/threat-intelligence/m-trends-2026), [Cybersecurity Dive](https://www.cybersecuritydive.com/news/cisco-threat-report-exploitation-phishing/810977/), [DFIR Report index](https://thedfirreport.com/reports/)). Akira exploatează VPN Cisco și Veeam; Play/IAB exploatează SimpleHelp (CVE-2024-57726/57727/57728) ([HIPAA Journal/CISA](https://www.hipaajournal.com/akira-ransomware-advisory-nov-2025/), [CISA AA23-352A](https://www.cisa.gov/news-events/cybersecurity-advisories/aa23-352a)).
- **Urme Windows:**
  - Proces-părinte de server care pornește shell: `java.exe` (ActiveMQ, Confluence) → `cmd.exe`/`powershell.exe`/`certutil.exe` ([DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)); `w3wp.exe` → `powershell`/`cmd` (SharePoint) ([SentinelOne](https://www.sentinelone.com/blog/sharepoint-toolshell-zero-day-exploited-in-the-wild-targets-enterprise-servers/)). Sursă: 4688 cu `ParentProcessName` + `CommandLine` **(config: Audit Process Creation + „Include command line”)** sau Sysmon 1.
  - Fișiere: webshell `spinstall0.aspx` (și variante) în `…\Web Server Extensions\16\TEMPLATE\LAYOUTS\`, `web.config` modificat ([SentinelOne](https://www.sentinelone.com/blog/sharepoint-toolshell-zero-day-exploited-in-the-wild-targets-enterprise-servers/)); USN `FILE_CREATE` pentru `.aspx/.jsp` sub directoare web **(implicit, $UsnJrnl)**.
  - Amcache/Prefetch: binar nou în `%TEMP%` al contului de serviciu (stager `uFSyLszKsuR.exe`) ([DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)). Prefetch e **(server-off)** pe Windows Server [NECITAT].
- **Idee de detecție:** `ParentImage ∈ {java.exe, w3wp.exe, tomcat*.exe, httpd.exe, sqlservr.exe, ScreenConnect.Service.exe, SimpleHelp*}` și `Image ∈ {cmd, powershell, certutil, bitsadmin, rundll32, mshta, whoami, net, nltest}`; fișier nou executabil sau script web în rădăcini web.
- **Fals pozitive:** scripturi de întreținere ale aplicației (ex. Tomcat care rulează `cmd` pentru backup).
- **Pentru controlor:** atacatorul a folosit o greșeală dintr-un program instalat pe server, fără nicio parolă, ca să pornească propriile comenzi.

### 5. Phishing, vishing și social engineering pe helpdesk/Teams — T1566 (inclusiv .004 Spearphishing Voice), T1656

- **De ce e în top:** ENISA ~60% din intruziuni; vishing al doilea vector la Mandiant 2026 (11%); CrowdStrike +442% ([ENISA](https://www.enisa.europa.eu/news/etl-2025-eu-consistently-targeted-by-diverse-yet-convergent-threat-groups), [SecurityWeek](https://www.securityweek.com/m-trends-2026-initial-access-handoff-shrinks-from-hours-to-22-seconds/), [CrowdStrike](https://www.crowdstrike.com/en-us/blog/crowdstrike-2025-global-threat-report-findings/)). Fostul ecosistem Black Basta: bombardament cu e-mailuri + apel Teams de la „IT” + Quick Assist/AnyDesk ([Sophos](https://www.sophos.com/blog/sophos-mdr-tracks-two-ransomware-campaigns-using-email-bombing-microsoft-teams-vishing), [The Hacker News](https://thehackernews.com/2025/06/former-black-basta-members-use.html)).
- **Urme Windows:**
  - Prefetch `QUICKASSIST.EXE-*.pf` / Amcache, `ms-teams.exe` → `cmd.exe`/`powershell.exe`, instalare AnyDesk imediat după apel **[NECITAT pentru calea exactă Quick Assist pe Windows 11 (aplicație Store)]**.
  - Atașamente: Mark-of-the-Web `Zone.Identifier` (ADS) cu `ZoneId=3` și `HostUrl` **(implicit pe NTFS)** [NECITAT]; LNK/JumpList pentru fișiere deschise din `Downloads`/`Outlook\INetCache\Content.Outlook` [NECITAT].
  - Istoric browser: descărcări de RMM, pagini de autentificare false.
- **Idee de detecție:** în aceeași sesiune de utilizator, în ≤ 30 min: proces Teams/Outlook/browser → (QuickAssist sau instalare RMM) → `cmd`/`powershell`; volum mare de e-mailuri primite nu e vizibil pe stație (în afara aplicației).
- **Fals pozitive:** suport IT legitim prin Quick Assist.
- **Pentru controlor:** utilizatorul a fost păcălit la telefon sau pe chat de cineva care s-a dat drept „IT-ul” și i-a lăsat controlul calculatorului.

### 6. ClickFix / FileFix („lipește și rulează”) — T1204.004

- **De ce e în top:** loc 8 în Red Canary 2026; 6 din top 10 amenințări legate de el; MITRE a creat tehnica special ([Red Canary](https://redcanary.com/threat-detection-report/techniques/), [Zscaler/Red Canary](https://www.zscaler.com/blogs/cybersecurity-best-practices/scarlet-goldfinch-clickfix), [Red Canary midyear](https://redcanary.com/blog/threat-detection/2025-threat-detection-report-midyear/)); brokerii de acces îl folosesc ([SecurityWeek](https://www.securityweek.com/m-trends-2026-initial-access-handoff-shrinks-from-hours-to-22-seconds/)); ESET raportează +517% în H1 2025 **[NESIGUR, citat secundar]** ([Prophet Security](https://www.prophetsecurity.ai/blog/clickfix-attack)).
- **Urme Windows:**
  - `NTUSER.DAT\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU` **(implicit)**: valori `a`, `b`, `c`… cu textul comenzii (sufix `\1`), ordinea în `MRUList`; textul e lizibil chiar dacă payload-ul e ofuscat ([Prophet Security](https://www.prophetsecurity.ai/blog/clickfix-attack)). Se scrie doar la lansare reușită **[NESIGUR]**.
  - FileFix trece prin bara de adresă a Explorer, deci **nu** apare în RunMRU ([trackr.live](https://www.trackr.live/?p=1134)); de verificat `…\Explorer\TypedPaths` **[NECITAT]**.
  - Proces: `explorer.exe` → `powershell.exe -ep Bypass -w H … iex` / `mshta http…` / `curl … | cmd` ([DFIR Report FileFix](https://thedfirreport.com/2025/07/14/kongtuke-filefix-leads-to-new-interlock-rat-variant/)); PowerShell 4104 conține blocul decodat ([TrustedSec](https://trustedsec.com/blog/building-a-detection-foundation-part-3-powershell-and-script-logging)).
  - Urmări: `php.exe` portabil în `%AppData%\Roaming\php\`, cheie Run în HKCU, DNS/pcap spre `*.trycloudflare.com` ([DFIR Report FileFix](https://thedfirreport.com/2025/07/14/kongtuke-filefix-leads-to-new-interlock-rat-variant/)).
- **Idee de detecție:** orice valoare RunMRU care conține `powershell|pwsh|mshta|curl|wget|bitsadmin|certutil|msiexec|rundll32|regsvr32|cmd /c|http://|https://|iex|-enc|-w h` → alertă; corelează cu Prefetch/4688 al interpretorului în ±2 min; `explorer.exe` părinte direct al `powershell -w hidden`.
- **Fals pozitive:** administratori care folosesc Win+R pentru `cmd`/`powershell` fără argumente de rețea (filtrare: doar cu URL sau argumente de ascundere/codare).
- **Pentru controlor:** o pagină web l-a convins pe utilizator să copieze și să lipească singur o comandă care a instalat programul atacatorului; Windows a păstrat comanda.

### 7. Malvertising / otrăvire SEO → instalator troianizat — T1189, T1608.006, T1204.002

- **De ce e în top:** M-Trends 2026 numește malvertising ca metodă tipică a brokerilor ([SecurityWeek](https://www.securityweek.com/m-trends-2026-initial-access-handoff-shrinks-from-hours-to-22-seconds/)); caz Bumblebee→Akira: căutare Bing după un program de administrare → domeniu fals → MSI semnat cu certificat revocat, rulat de un administrator ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
- **Urme Windows:** istoric browser (căutare + domeniu tip „look-alike” + descărcare `.msi`); Application log `MsiInstaller` 1033/11707 **(implicit)** [NECITAT]; Zone.Identifier; Amcache cu editor/semnătură; fișiere noi în `C:\Program Files\…\ApplicationInstallationFolder_*` cu `consent.exe` + DLL ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
- **Idee de detecție:** MSI instalat al cărui `HostUrl` (Zone.Identifier) sau domeniu din istoric nu e domeniul oficial al producătorului; binar de sistem (`consent.exe`, `WAB.exe`) prezent în afara `System32`.
- **Fals pozitive:** oglinzi legitime de descărcare.
- **Pentru controlor:** cineva a descărcat un program „oficial” de pe un site fals găsit pe un motor de căutare; programul a mers, dar a adus cu el și virusul.

### 8. PowerShell — T1059.001

- **De ce e în top:** loc 2 Red Canary 2026, în top 4 în fiecare an din 2019 ([Red Canary](https://redcanary.com/threat-detection-report/techniques/), [Red Canary – threat sounds](https://redcanary.com/threat-detection-report/threat-sounds/)).
- **Urme Windows:** `Microsoft-Windows-PowerShell/Operational` 4104 — blocurile „suspecte” sunt scrise la nivel Warning **(implicit, fără politică)**, toate blocurile doar **(config: Script Block Logging)** ([TrustedSec](https://trustedsec.com/blog/building-a-detection-foundation-part-3-powershell-and-script-logging), [Velociraptor](https://docs.velociraptor.app/artifact_references/pages/windows.eventlogs.powershellscriptblock)); `Windows PowerShell` 400/403/600 (pornire motor, `HostApplication` cu linia de comandă) **(implicit)** [NECITAT]; `ConsoleHost_history.txt` în `%AppData%\Microsoft\Windows\PowerShell\PSReadLine\` **(implicit pentru sesiuni interactive)** [NECITAT]; Prefetch `POWERSHELL.EXE`.
- **Idee de detecție:** 4104 Level=3 (Warning) → oricare; 4104/400 cu `-enc|-EncodedCommand|FromBase64String|IEX|Invoke-Expression|DownloadString|Net.WebClient|VirtualAlloc|Invoke-ShareFinder|Invoke-Mimikatz`; scrieri mixte de majuscule `pOWerShELl` ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
- **Fals pozitive:** SCCM/Intune, scripturi de inventar, module care conțin cuvintele-cheie.
- **Pentru controlor:** PowerShell e „linia de comandă avansată” a Windows; atacatorii o folosesc pentru că e deja instalată și nu ridică suspiciuni.

### 9. Linia de comandă și scripturi (cmd, VBS, JS) — T1059.003, T1059.005, T1059.007

- **De ce e în top:** cmd locul 3 Red Canary 2026 ([Red Canary](https://redcanary.com/threat-detection-report/techniques/)); `nocmd.vbs` → `rcl.bat` → rclone în cazul RansomHub; JS de 2 MB deghizat în formular la Lunar Spider; `start.vbs` + `run.bat` pentru exfiltrare ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/)).
- **Urme Windows:** Prefetch `WSCRIPT.EXE`/`CSCRIPT.EXE`/`CMD.EXE`; 4688 **(config)**; fișiere `.vbs/.js/.bat` în `C:\ProgramData`, `%TEMP%`, `Desktop\000\`; LNK în Recent pentru `.js` deschis din Downloads.
- **Idee de detecție:** `wscript/cscript` care rulează un fișier din `Downloads|%TEMP%|ProgramData` sau de pe unitate amovibilă; `cmd.exe /c` cu peste 200 de caractere albe (semnătură Raspberry Robin) ([Fortinet](https://community.fortinet.com/fortiedr-20/threat-coverage-how-fortiedr-protects-against-raspberryrobin-malware-116242)).
- **Fals pozitive:** scripturi de logon din NETLOGON.
- **Pentru controlor:** atacatorul scrie mici fișiere cu comenzi și le pornește cu programele Windows obișnuite.

### 10. Execuție prin binare de încredere și DLL side-loading — T1218 (.005 mshta, .007 msiexec, .010 regsvr32, .011 rundll32), T1574.001/T1574.002

- **De ce e în top:** mshta înapoi în top 10 Red Canary din cauza fake CAPTCHA ([Red Canary 2025](https://redcanary.com/blog/threat-detection/2025-threat-detection-report)); `rundll32 upfilles.dll,stow`, `rundll32 wscadminui.dll,wsca`, `rundll32 …\sys.dll,StartUp471` la Lunar Spider; `consent.exe` + `msimg32.dll` la Bumblebee; PlugX side-loading pe stick ([DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/), [DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/), [CERT-NG](https://cert.gov.ng/advisories/self-spreading-plugx-usb-drive-malware-infecting-systems-worldwide)). Proiectul LOLBAS catalogează binarele ([CrowdStrike](https://www.crowdstrike.com/blog/8-lolbins-every-threat-hunter-should-know/)). În v17, Hijack Execution Flow a fost consolidat ([MITRE v17](https://attack.mitre.org/resources/updates/updates-april-2025/)): de verificat ID-ul curent pentru side-loading **[NESIGUR]**.
- **Urme Windows:** Prefetch `RUNDLL32.EXE-<hash>.pf` (hash-ul depinde de linia de comandă — mai multe fișiere = mai multe DLL-uri diferite) [NECITAT]; Amcache (DLL nesemnat lângă EXE semnat); 4688; Sysmon 7 (ImageLoad).
- **Idee de detecție:** `rundll32` cu DLL din `ProgramData|AppData|Temp|Users\Public` sau cu extensie ne-`.dll` (`.dl_`, `.bin`); `mshta http*`; `regsvr32 /i:http* scrobj.dll`; EXE Microsoft semnat (`consent.exe`, `WAB.exe`, `wscadminui.exe`) rulat din afara `System32`/`Program Files\Windows*`.
- **Fals pozitive:** aplicații care distribuie propriile copii ale bibliotecilor; instalatoare MSI.
- **Pentru controlor:** atacatorul ascunde virusul în spatele unui program Microsoft de încredere, ca să pară activitate normală.

### 11. Aducerea de instrumente din exterior — T1105

- **De ce e în top:** loc 5 Red Canary 2026 ([Red Canary](https://redcanary.com/threat-detection-report/techniques/)); `certutil` a descărcat stagerul Metasploit în cazul LockBit; cURL în campaniile Teams post-Black Basta ([DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/), [The Hacker News](https://thehackernews.com/2025/06/former-black-basta-members-use.html)).
- **Urme Windows:** CryptnetUrlCache (`%USERPROFILE%\AppData\LocalLow\Microsoft\CryptnetUrlCache\`) pentru `certutil -urlcache` [NECITAT]; `Microsoft-Windows-Bits-Client/Operational` (job, URL) și baza `qmgr.db` din `C:\ProgramData\Microsoft\Network\Downloader\` **(implicit)** ([Elcomsoft](https://blog.elcomsoft.com/2026/03/windows-file-system-artefacts-under-cprogramdata/)); ID-ul exact (59/60) **[NESIGUR]**; Prefetch `CERTUTIL.EXE`, `CURL.EXE`, `BITSADMIN.EXE`; Zone.Identifier pe fișierele descărcate de browser (nu și de certutil) [NECITAT].
- **Idee de detecție:** `certutil` cu `-urlcache|-split|-f|-decode`; `curl|wget|Invoke-WebRequest` urmat în ≤ 60 s de crearea unui EXE/DLL în `%TEMP%`/`ProgramData`; job BITS cu URL extern și comandă de notificare.
- **Fals pozitive:** actualizări software, Windows Update (BITS).
- **Pentru controlor:** după ce a intrat, atacatorul și-a adus „trusa de scule” de pe internet folosind programe care există deja în Windows.

### 12. WMI (execuție și persistență) — T1047, T1546.003

- **De ce e în top:** loc 7 Red Canary 2026 ([Red Canary](https://redcanary.com/threat-detection-report/techniques/)); Adaptix lansat prin WMI sub `WmiPrvSE.exe`, ștergere shadow copies prin WMI, oprire servicii SQL/IIS cu WMIC ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
- **Urme Windows:** `Microsoft-Windows-WMI-Activity/Operational` 5861 (legătură permanentă filtru-consumator) **(implicit pe Windows 10+)** ([MITRE DS0005](https://attack.mitre.org/datasources/DS0005), [Splunk](https://research.splunk.com/endpoint/71bfdb13-f200-4c6c-b2c9-a2e07adf437d)); depozitul `C:\Windows\System32\wbem\Repository\OBJECTS.DATA` (consumatori `CommandLineEventConsumer`/`ActiveScriptEventConsumer`) [NECITAT]; proces `WmiPrvSE.exe` → `cmd.exe /Q /c … 1> \\127.0.0.1\ADMIN$\__<timp> 2>&1` (tiparul Impacket wmiexec) [NECITAT]; Sysmon 19/20/21.
- **Idee de detecție:** 5861 cu consumator ≠ `SCM Event Log Consumer` ([Splunk](https://research.splunk.com/endpoint/71bfdb13-f200-4c6c-b2c9-a2e07adf437d)); `WmiPrvSE.exe` părinte al `cmd/powershell/rundll32`; `wmic … shadowcopy delete` sau `process call create` spre alt host.
- **Fals pozitive:** agenți de monitorizare (SCCM, antivirus) care folosesc abonamente WMI.
- **Pentru controlor:** WMI e „telecomanda” internă a Windows; atacatorul o folosește ca să pornească programe pe alte calculatoare sau la fiecare repornire.

### 13. Ofuscare — T1027, T1140

- **De ce e în top:** loc 10 Red Canary 2026 ([Red Canary](https://redcanary.com/threat-detection-report/techniques/)); JS cu cod real ascuns între linii de comentarii ([DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/)); PowerShell Base64+gzip care apelează `VirtualAlloc` ([DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)); stealere USBStealer cu 3DES/XOR ([ESET Sednit](https://www.welivesecurity.com/2014/11/11/sednit-espionage-group-attacking-air-gapped-networks/)).
- **Urme Windows:** 4104 (blocul decodat apare separat când e rulat prin `IEX`) ([TrustedSec](https://trustedsec.com/blog/building-a-detection-foundation-part-3-powershell-and-script-logging)); linii de comandă lungi, entropie mare, `^`/`"` inserate.
- **Idee de detecție:** entropia Shannon a liniei de comandă > 5,0 și lungime > 500; `FromBase64String` + `GzipStream`/`DeflateStream`; fișier `.js/.vbs` > 1 MB.
- **Fals pozitive:** instalatoare legitime cu argumente codate.
- **Pentru controlor:** comanda a fost „mâzgălită” intenționat ca să nu poată fi citită ușor de om sau de antivirus.

### 14. Programe legitime de control la distanță (RMM) — T1219, T1219.002

- **De ce e în top:** Atera + Splashtop (RansomHub), AnyDesk (Lynx, LockBit), RustDesk (Akira), ScreenConnect/NetSupport/AnyDesk (Storm-1811), AnyDesk/NinjaOne/Level.io (Storm-0501); CISA listează AnyDesk/ConnectWise/N-Able pentru RansomHub ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [MITRE T1219.002](https://attack.mitre.org/techniques/T1219/002/), [CISA AA24-242A](https://cisa.gov/news-events/cybersecurity-advisories/aa24-242a)); RMM instalate din surse legitime, active peste o lună înainte de criptare ([Cato Networks](https://www.catonetworks.com/blog/cato-ctrl-investigation-of-rmm-tools/)).
- **Urme Windows:**
  - System 7045 **(implicit)**: `ServiceName` (`AnyDesk Service`, `ScreenConnect Client (…)`, `Atera Agent`, `Splashtop…`, `RustDesk`), `ImagePath`, `StartType` ([ReliaQuest](https://reliaquest.com/blog/rmm-tool-abuse/), [DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/)).
  - AnyDesk: `C:\ProgramData\AnyDesk\ad_svc.trace` (instalat; IP-ul în linia „Logged in from”), `%AppData%\AnyDesk\ad.trace`, `connection_trace.txt` (ID-ul AnyDesk al celuilalt, acceptat/refuzat) ([Inversecos](https://www.inversecos.com/2021/02/forensic-analysis-of-anydesk-logs.html), [Cyber Triage](https://www.cybertriage.com/blog/dfir-next-steps-suspicious-anydesk-use/)).
  - Splashtop: `C:\Program Files (x86)\Splashtop\Splashtop Remote\Server\log\agent_log.txt`, `SPLog.txt` ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/)).
  - ScreenConnect: `Session.db`, `User.xml`, `user.config` sub directoarele programului ([Daniel Cortez/LOLRMM](https://danielcortez.substack.com/p/beware-of-rmms)).
  - RustDesk: 4624 tip 2 de la `127.0.0.1`, argumente `--tray`, `--cm` ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
  - Amcache/Prefetch/BAM pentru executabilul portabil chiar fără serviciu.
- **Idee de detecție:** listă LOLRMM (nume serviciu, nume executabil, nume produs din Amcache) × lista RMM aprobate din politica stației; alertă la RMM neaprobat, severitate maximă dacă instalarea e pe DC/server de backup sau la < 24 h după un logon RDP străin.
- **Fals pozitive:** helpdesk extern care chiar folosește AnyDesk.
- **Pentru controlor:** s-a instalat un program de tip „TeamViewer” care permite cuiva din afară să controleze calculatorul oricând, fără să mai aibă nevoie de parolă.

### 15. Extragerea parolelor din memoria LSASS — T1003.001

- **De ce e în top:** prezent în 4 din 6 cazuri DFIR Report: Mimikatz `sekurlsa::logonpasswords` (RansomHub), Metasploit cu `GrantedAccess 0x1010` și CallTrace UNKNOWN (LockBit), tipar 0x1010 → 0x1fffff din `runonce.exe`/`gpupdate.exe` (Lunar Spider), lsassy prin `comsvcs.dll MiniDump` cu fișiere în `\Windows\Temp` cu extensii false (Akira) ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/), [DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/), [DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)); CISA: RansomHub folosește Mimikatz ([CISA AA24-242A](https://cisa.gov/news-events/cybersecurity-advisories/aa24-242a)).
- **Urme Windows:**
  - Sysmon 10 `TargetImage=lsass.exe`, `GrantedAccess ∈ {0x1010, 0x1410, 0x1fffff, 0x143a}`, `CallTrace` cu `UNKNOWN` **(config)** ([SOC Investigation](https://www.socinvestigation.com/os-credential-dumping-lsass-memory-vs-windows-logs/)).
  - Fără Sysmon: 4688 `rundll32.exe … comsvcs.dll, MiniDump <PID> <cale> full` **(config)**; Prefetch `RUNDLL32.EXE` + USN `FILE_CREATE` `.dmp`/fișier mare în `C:\Windows\Temp`, `ProgramData` **(implicit)**; Defender 1116/1117 cu nume de tip `Behavior:Win32/LsassDump` sau `HackTool:Win32/Mimikatz` **(implicit dacă Defender e activ)** [NECITAT pentru numele exacte]; `procdump -ma lsass` (Prefetch `PROCDUMP*.EXE`).
  - Configurare care facilitează furtul: `HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest\UseLogonCredential=1`; `HKLM\SYSTEM\CurrentControlSet\Control\Lsa\RunAsPPL` absent/0 [NECITAT].
- **Idee de detecție:** (Sysmon 10 pe lsass cu mască din listă de la proces nesemnat/neașteptat) SAU (linie de comandă cu `comsvcs` + `MiniDump`) SAU (fișier `.dmp` > 30 MB creat în afara `%LOCALAPPDATA%\CrashDumps`) SAU (UseLogonCredential schimbat în 1).
- **Fals pozitive:** antivirus/EDR, Task Manager „Create dump file” de către administrator, WER.
- **Pentru controlor:** atacatorul a „fotografiat” memoria în care Windows ține parolele utilizatorilor conectați, ca să le folosească pe alte calculatoare.

### 16. Baza de conturi a domeniului și SAM (NTDS.dit, DCSync, SAM) — T1003.003, T1003.006, T1003.002

- **De ce e în top:** `wbadmin` a copiat `ntds.dit` + SYSTEM + SECURITY în `C:\ProgramData` (Akira); Mimikatz `lsadump::dcsync` cu fișiere CSV per domeniu (RansomHub) ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/), [DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/)).
- **Urme Windows:**
  - Application, sursa `ESENT`, ID 216/325/326/327 cu `ntds.dit` într-o cale diferită de `%SystemRoot%\NTDS\` **(implicit pe DC)** ([Sigma ntdsutil](https://detection.fyi/sigmahq/sigma/windows/builtin/application/esent/win_esent_ntdsutil_abuse), [Hack The Box](https://www.hackthebox.com/blog/ntds-dumping-attack-detection)).
  - Linii de comandă: `ntdsutil "ac i ntds" "ifm" "create full"`, `vssadmin create shadow`, `diskshadow`, `esentutl /y … ntds.dit`, `wbadmin start backup … ntds`, `reg save HKLM\SAM|SYSTEM|SECURITY` ([Hack The Box](https://www.hackthebox.com/blog/ntds-dumping-attack-detection)).
  - DCSync: 4662 pe DC cu `Properties` conținând `1131f6aa-9c07-11d1-f79f-00c04fc2dcd2` / `1131f6ad-9c07-11d1-f79f-00c04fc2dcd2` / `89e95b76-444d-4c62-991a-0facbeda640c` și `SubjectUserName` care nu e cont de calculator al unui DC **(config: Audit Directory Service Access + SACL)** [NECITAT pentru GUID-uri; sursa DFIR confirmă doar folosirea 4662].
- **Idee de detecție:** ESENT 325/327 cu `ntds.dit` în afara `\NTDS\`; fișiere `ntds.dit`/`SYSTEM`/`SAM`/`*.hiv` noi în `ProgramData|Temp|Users`; 4662 cu GUID de replicare de la cont utilizator.
- **Fals pozitive:** backup de sistem al DC (Windows Server Backup), promovare DC prin IFM.
- **Pentru controlor:** atacatorul a copiat „cartea de parole” a întregii rețele; după asta orice cont trebuie considerat cunoscut de el.

### 17. Parole din aplicații, browsere, DPAPI și backup (inclusiv infostealere) — T1555 (.003 browser, .004 Credential Manager), T1539, T1552.001

- **De ce e în top:** infostealerele devin vector inițial (Microsoft), credențialele furate urcă la 16% (Mandiant), Lumma: peste 394.000 de calculatoare infectate în 2 luni, ~2.300 domenii confiscate în mai 2025 ([Microsoft MDDR](https://news.microsoft.com/source/asia/2026/01/07/microsoft-releases-2025-digital-defense-report-highlighting-the-changing-cyber-threat-landscape-and-the-importance-of-security-in-the-ai-era/), [Microsoft Lumma](https://blogs.microsoft.com/on-the-issues/2025/05/21/microsoft-leads-global-action-against-favored-cybercrime-tool/)). În intruziuni: CredentialsFileView (RansomHub), stealer Latrodectus pentru browser/Outlook, `Veeam-Get-Creds.ps1` (Lunar Spider), `psql.exe` pe tabela `credentials` din VeeamBackup decriptată cu DPAPI (Akira) ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/), [DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
- **Urme Windows:**
  - Security 5379 (citire din Credential Manager) **(implicit [NESIGUR pe toate versiunile])** — apărut la CredentialsFileView ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/)).
  - Chrome App-Bound Encryption (din v127) împinge stealerele să pornească browserul cu argumente de depanare / fereastră ascunsă (`--headless`, `--remote-debugging-port`, `--window-position=-…`) ([eSentire](https://mdr.esentire.com/Threat-Briefing-2024-11-15), [Elcomsoft](https://blog.elcomsoft.com/2026/01/browser-forensics-in-2026-app-bound-encryption-and-live-triage/)); acces la `Login Data`, `Cookies`, `Local State` de către alt proces decât browserul (USN/Sysmon 11) [NECITAT].
  - `psql.exe` / `sqlcmd` pe server de backup; `Veeam-Get-Creds.ps1` în 4104.
  - Fișiere cu parole: `Unattend.xml`, `sysprep.inf`, `*.kdbx`, `web.config` citite [NECITAT].
- **Idee de detecție:** browser pornit cu `--remote-debugging-port`/`--headless` de un părinte ≠ browser/explorer; 5379 în rafală (> 20 în 1 min) de la același proces; `psql.exe … VeeamBackup … credentials`; 4104 cu `Get-Creds|credentials|ProtectedData]::Unprotect`.
- **Fals pozitive:** automatizare web (Selenium/Puppeteer) pe stații de dezvoltare; administratorii Veeam.
- **Pentru controlor:** un program a citit parolele salvate în browser, în Windows sau în programul de backup; acestea pot fi folosite de oriunde din internet.

### 18. Kerberoasting și AS-REP roasting — T1558.003, T1558.004

- **De ce e în top:** enumerare de SPN-uri în cazul Akira ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)); instrumentele publice (Rubeus, Impacket) cer implicit RC4 ([Hack The Box](https://www.hackthebox.com/blog/kerberoasting-attack-detection)).
- **Urme Windows:** pe DC 4769 **(implicit pe DC)**: `ServiceName`, `TicketEncryptionType=0x17` (RC4), `TicketOptions=0x40810000`, `IpAddress`, `Status=0x0` ([Sigma RC4](https://sigma.controlassurance.com/rules/win_security_susp_rc4_kerberos/), [Datadog](https://docs.datadoghq.com/security/default_rules/def-000-924)); 4768 cu `PreAuthType=0` pentru AS-REP roasting [NECITAT].
- **Idee de detecție:** 4769 cu 0x17 pentru `ServiceName` care nu se termină în `$` și ≠ `krbtgt`; același cont cere ≥ 15 servicii distincte în 30 s ([ManageEngine](https://www.manageengine.com/log-management/mitre-attack/credential-access/steal-kerberos-tickets-t1558.html)); cont configurat AES care primește 0x17.
- **Fals pozitive:** sisteme vechi (NAS, aplicații legacy) care folosesc doar RC4.
- **Pentru controlor:** atacatorul a cerut „bilete de acces” pentru conturile de serviciu, ca să le spargă parola acasă, pe calculatorul lui.

### 19. NTLM relay și forțarea autentificării (coercion) — T1557.001, T1187

- **De ce e în top:** NTLM relay + PetitPotam rămân calea clasică spre ADCS și DC; detecția principală e logonul anonim NTLM pe DC ([NCC Group](https://nccgroup.com/au/research-blog/detecting-and-hunting-for-the-petitpotam-ntlm-relay-attack), [Guardsix](https://guardsix.com/blog/detecting-petitpotam-a-relay-attack-that-can-compromise-your-domain-without-any-authentication)). **[NESIGUR]** pentru prevalența exactă în 2025–2026: rapoartele anuale citate nu o cuantifică.
- **Urme Windows:** 4624 cu `TargetUserName=ANONYMOUS LOGON`, `AuthenticationPackageName=NTLM`, `WorkstationName` gol, pe DC **(implicit)**; 4624 tip 3 NTLM în care contul e contul de calculator al unui DC (`DC01$`) spre o stație non-DC ([ManageEngine](https://www.manageengine.com/log-management/mitre-attack/credential-access/adversary-in-the-middle-T1557.html)); 5145 pe `\\*\IPC$` cu `lsarpc`/`efsrpc` **(config: Detailed File Share)** ([Blumira](https://www.blumira.com/?p=36300)); registru `HKLM\SYSTEM\CurrentControlSet\Control\Lsa\LmCompatibilityLevel` (< 5 = NTLMv1 permis) [NECITAT].
- **Idee de detecție:** pe DC, 4624 NTLM anonim cu `WorkstationName` vid; cont `*$` al unui DC autentificat NTLM tip 3 pe alt host; politică NTLMv1 permisă = constatare de conformitate.
- **Fals pozitive:** echipamente vechi care se autentifică anonim, scanere.
- **Pentru controlor:** atacatorul a păcălit un server important să se „legitimeze” la el și a refolosit acea legitimare ca să intre în altă parte.

### 20. Abuz de servicii de certificate (ADCS) — T1649

- **De ce e în top:** ESC1 (cerere cu SAN pentru alt utilizator) rămâne o escaladare directă la Domain Admin; auditul CA e **oprit implicit** ([Black Hills](https://www.blackhillsinfosec.com/detecting-adcs-privilege-escalation/)). **[NESIGUR]** pentru prevalență în cazurile 2025–2026.
- **Urme Windows:** pe CA, 4886 (cerere) și 4887 (emis) **(config: auditul CA)**: `Requester`, `Attributes` (SAN/UPN), `CertificateTemplate` ([Black Hills](https://www.blackhillsinfosec.com/detecting-adcs-privilege-escalation/)); pe DC 4768 cu `CertificateIssuerName` (PKINIT) [NECITAT]; SAN poate lipsi din eveniment în funcție de `EDITF_ATTRIBUTESUBJECTALTNAME2` **[NESIGUR]**.
- **Idee de detecție:** 4887 unde `Requester` ≠ UPN-ul din SAN; urmat de 4768 PKINIT pentru acel UPN de la IP-ul solicitantului.
- **Fals pozitive:** conturi de înrolare (enrollment agents), MDM.
- **Pentru controlor:** atacatorul și-a făcut un „ecuson” digital pe numele unui administrator, iar rețeaua îl acceptă ca atare.

### 21. Recunoașterea rețelei și a domeniului — T1087, T1018, T1046, T1135, T1482, T1069

- **De ce e în top:** prezentă în toate cazurile: SoftPerfect `netscan.exe` (cu fișiere `delete.me` pe share-uri și `rundll32 davclnt.dll,DavSetCookie`), Advanced IP Scanner, AdFind (`ad_users.txt`, `ad_computers.txt`), `nltest /dclist` și `/domain_trusts`, `net group "domain admins" /domain`, `Invoke-ShareFinder`, NetExec (`.nxc\`, `smb.db`), rustscan ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/), [DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/)); Play folosește AdFind și GMER ([CISA AA23-352A](https://www.cisa.gov/news-events/cybersecurity-advisories/aa23-352a)); căutare de host-uri cu `VEEAM|BCK|BACK` în nume ([DFIR Report FileFix](https://thedfirreport.com/2025/07/14/kongtuke-filefix-leads-to-new-interlock-rat-variant/)).
- **Urme Windows:** Prefetch/Amcache/BAM pentru `netscan.exe`, `advanced_ip_scanner.exe`, `adfind.exe`, `nxc.exe`, `rustscan.exe`, `nltest.exe`; USN/5145 `delete.me` pe share-uri; `reg query HKLM\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters` (căutare Hyper-V) ([DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/)); 4104 cu `Invoke-ShareFinder`, `Get-ADComputer`; `secedit /export`.
- **Idee de detecție:** ≥ 3 comenzi distincte din {`nltest /dclist`, `nltest /domain_trusts`, `net group "domain admins"`, `whoami /groups`, `quser`, `systeminfo`, `ipconfig /all`, `route print`, `arp -a`} de la același utilizator în 10 min; orice execuție a unui scanner din listă; fișier `delete.me` creat pe ≥ 3 share-uri.
- **Fals pozitive:** administratori, inventare automate.
- **Pentru controlor:** atacatorul a „cercetat terenul”: a aflat ce calculatoare, ce administratori și ce copii de siguranță există.

### 22. Mișcare laterală prin SMB/PsExec/execuție de servicii — T1021.002, T1569.002, T1570

- **De ce e în top:** PsExec (Lunar Spider, Play), servicii cu nume aleatoare de 6 litere (RansomHub), Metasploit prin creare de servicii pe DC (LockBit), `LB3.exe -psex` ([DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/), [DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/), [CISA AA23-352A](https://www.cisa.gov/news-events/cybersecurity-advisories/aa23-352a)).
- **Urme Windows:** pe ținta: 7045 `PSEXESVC` sau `ServiceName` aleator `^[A-Za-z]{6,8}$` cu `ImagePath` în `ADMIN$`/`%SystemRoot%\*.exe` sau `cmd.exe /c echo … > \\.\pipe\…` (getsystem) **(implicit)** ([DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)); 4624 tip 3 cu același `TargetLogonId` imediat înainte; registru `HKLM\SYSTEM\CurrentControlSet\Services\PSEXESVC`; pe sursă: Prefetch `PSEXEC.EXE`, cheie `NTUSER\Software\Sysinternals\PsExec\EulaAccepted` [NECITAT]; binare cu nume aleator și dimensiune identică copiate pe mai multe servere (USN) ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/)).
- **Idee de detecție:** 7045 cu `ServiceName` care nu apare în baza de servicii cunoscute și `ImagePath` conținând `\\.\pipe\` sau `%COMSPEC%`/`cmd.exe /c`; 4624 tip 3 + 7045 pe același host în ≤ 2 min.
- **Fals pozitive:** instrumente de administrare (PDQ, SCCM) care instalează servicii temporare.
- **Pentru controlor:** atacatorul a „sărit” de pe un calculator pe altul folosind mecanismele de administrare ale rețelei.

### 23. Persistență prin servicii Windows — T1543.003

- **De ce e în top:** Atera, Splashtop, AnyDesk, RustDesk, cloudflared, drivere vulnerabile — toate instalate ca servicii ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
- **Urme Windows:** System 7045 **(implicit)**; Security 4697 **(config: Audit Security System Extension)**; 7040 (schimbare tip pornire) **(implicit)**; registru `HKLM\SYSTEM\CurrentControlSet\Services\<nume>` (`ImagePath`, `Type`, `Start`, `ObjectName`) cu timpul de modificare al cheii.
- **Idee de detecție:** serviciu nou cu `ImagePath` în `ProgramData|Users|Temp|AppData`, sau binar nesemnat, sau produs RMM/tunel (`cloudflared`, `ngrok`, `ssh`, `plink`, `chisel`) ca serviciu.
- **Fals pozitive:** actualizatoare de aplicații (Google, Adobe) în ProgramData.
- **Pentru controlor:** atacatorul a pus un program să pornească automat odată cu Windows, chiar dacă nimeni nu se conectează.

### 24. Sarcini programate (inclusiv ascunse) — T1053.005, T1564 (task ascuns)

- **De ce e în top:** task `SchedulerLsass` cu `/sc onstart` (Lunar Spider); ștergerea taskului `Updater` (FileFix); lsassy folosește taskuri pentru execuție ([DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/), [DFIR Report FileFix](https://thedfirreport.com/2025/07/14/kongtuke-filefix-leads-to-new-interlock-rat-variant/), [DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)). Tarrask (HAFNIUM): taskul dispare din `schtasks` prin ștergerea valorii `SD` ([Microsoft](https://www.microsoft.com/en-us/security/blog/?p=112008)).
- **Urme Windows:** Security 4698/4699/4702 `TaskName`, `TaskContent` (XML) **(config: Audit Other Object Access)**; `Microsoft-Windows-TaskScheduler/Operational` 106/140/141/200/201 — Microsoft recomandă activarea lui, deci poate lipsi **[NESIGUR dacă e implicit pe 10/11]** ([Microsoft](https://www.microsoft.com/en-us/security/blog/?p=112008)); fișiere XML în `C:\Windows\System32\Tasks\`; registru `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree\<nume>` (`SD`, `Id`) și `…\TaskCache\Tasks\{GUID}` (`Actions`, `Path`). Task fără `SD` = ascuns; varianta cu SDDL care refuză citirea ocolește această verificare ([Binary Defense](https://binarydefense.com/resources/blog/diving-into-hidden-scheduled-tasks), [Sigma](https://detection.fyi/sigmahq/sigma/windows/registry/registry_delete/registry_delete_schtasks_hide_task_via_sd_value_removal)).
- **Idee de detecție:** din hive-ul SOFTWARE: orice cheie din `TaskCache\Tree` fără valoare `SD`, sau cu SD care refuză citirea pentru Administrators/SYSTEM; task a cărui acțiune rulează din `ProgramData|AppData|Users\Public|Temp` sau `rundll32`/`powershell -enc`; task din registru fără fișier XML corespondent (sau invers).
- **Fals pozitive:** taskuri de actualizare ale aplicațiilor din `AppData` (Teams, OneDrive).
- **Pentru controlor:** atacatorul a programat calculatorul să-i pornească singur programul, la oră fixă sau la fiecare pornire, uneori ascuns din lista vizibilă.

### 25. Chei de autopornire și ocolirea UAC — T1547.001, T1548.002

- **De ce e în top:** Run key `Update` (Lunar Spider), Run key în HKCU pentru `php.exe` (Interlock), PlugX cu cheie Run ([DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/), [DFIR Report FileFix](https://thedfirreport.com/2025/07/14/kongtuke-filefix-leads-to-new-interlock-rat-variant/), [CERT-NG](https://cert.gov.ng/advisories/self-spreading-plugx-usb-drive-malware-infecting-systems-worldwide)); UAC bypass prin `HKCU\Software\Classes\ms-settings\shell\open\command` + `DelegateExecute` și `ComputerDefaults.exe` ([DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/)).
- **Urme Windows:** `HKLM|HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `RunOnce`, `…\Policies\Explorer\Run`, `HKLM\…\Winlogon` (`Userinit`, `Shell`), dosarul Startup; `HKCU\Software\Classes\ms-settings\shell\open\command`, `…\Classes\Folder\shell\open\command` [NECITAT pentru a doua]; Sysmon 13.
- **Idee de detecție:** valoare Run care rulează din `AppData|ProgramData|Temp|Users\Public` sau `rundll32`/`wscript`/`powershell`/`php.exe`/`python.exe`; existența oricărei chei `ms-settings\shell\open\command` în HKCU.
- **Fals pozitive:** Teams, OneDrive, Spotify în `AppData`.
- **Pentru controlor:** atacatorul a scris în „lista de pornire” a Windows ca programul lui să ruleze la fiecare intrare a utilizatorului.

### 26. Creare și modificare de conturi — T1136.001/.002, T1098

- **De ce e în top:** `administratr` + „look-alike” adăugate în Domain Admins cu parolă fără expirare (Lynx); `backup_DA`/`backup_EA` (Akira); reactivarea Administrator pe DC; parole schimbate în masă la aceeași valoare (RansomHub); CISA: RansomHub creează și reactivează conturi ([DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/), [DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/), [DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [CISA AA24-242A](https://cisa.gov/news-events/cybersecurity-advisories/aa24-242a)).
- **Urme Windows:** 4720 (creat), 4722 (activat), 4724 (resetare parolă), 4738 (`UserAccountControl` cu `DONT_EXPIRE_PASSWORD`), 4728/4732/4756 (adăugare în grup) **(implicit pe DC prin Default Domain Controllers Policy [NESIGUR]; pe stații locale necesită Audit User Account Management [NECITAT])**; SAM hive pentru conturi locale.
- **Idee de detecție:** cont nou al cărui nume e la distanță Levenshtein ≤ 2 de un cont privilegiat existent sau conține `admin|backup|svc` și e adăugat în grup privilegiat în ≤ 1 h; ≥ 5 resetări de parolă de același autor în 10 min; reactivarea contului RID 500.
- **Fals pozitive:** onboarding, scripturi de provizionare.
- **Pentru controlor:** atacatorul și-a făcut propriul cont de administrator, cu nume aproape identic cu unul real, ca să nu fie observat.

### 27. Dezactivarea protecției: Defender, EDR killers, BYOVD, WFP — T1562.001, T1562.004, T1068, T1564.012

- **De ce e în top:** ESET numără ~90 de EDR killers, 54 prin BYOVD, abuzând 35 de drivere semnate; afiliații aleg instrumentul; apar și metode fără driver (anti-rootkit legitime, blocare comunicație) ([ESET](https://www.eset.com/us/about/newsroom/research/eset-research-deep-dive-edr-killers-ransomware/)); EDRKillShifter (RansomHub) cu drivere RentDrv2 / ThreatFireMonitor ([Sophos](https://www.sophos.com/en-us/blog/edr-kill-shifter)); BYOVD în cazul Akira; `SystemSettingsAdminFlows.exe` folosit pentru a dezactiva Defender (LockBit) ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/), [DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)); EDRSilencer blochează prin Windows Filtering Platform ([Sigma](https://detection.fyi/sigmahq/sigma/windows/builtin/security/win_security_hktl_edr_silencer)).
- **Urme Windows:**
  - Defender `Microsoft-Windows-Windows Defender/Operational` **(implicit)**: 5001 (protecție în timp real oprită), 5007 (configurație schimbată, `NewValue` cu `\Exclusions\`), 5010/5012 (scanare oprită), 5013 (Tamper Protection a blocat o schimbare) [NECITAT pentru 5010/5012/5013], 1116/1117 ([Sigma 5007](https://detection.fyi/sigmahq/sigma/windows/builtin/windefend/win_defender_config_change_exclusion_added/)).
  - Registru: `HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\{Paths,Processes,Extensions}`, `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\DisableAntiSpyware`, `…\Real-Time Protection\DisableRealtimeMonitoring`.
  - Driver: 7045 cu `ServiceType=kernel mode driver` și `ImagePath` comparat cu LOLDrivers ([Splunk](https://research.splunk.com/endpoint/1dda7586-57be-4a1b-8de1-a9ad802b9a7f)); `HKLM\SYSTEM\CurrentControlSet\Services\<x>\Type=1`; `.sys` nou în afara `System32\drivers` (USN/Amcache, care listează și drivere ([Cyber Triage](https://www.cybertriage.com/blog/shimcache-and-amcache-forensic-analysis-2026/))); blocare de către Vulnerable Driver Blocklist în `Microsoft-Windows-CodeIntegrity/Operational` (ID exact **[NESIGUR]**, de verificat în laborator); blocklistul Microsoft e activ implicit pe Windows 11 22H2+ ([Splunk blog](https://www.splunk.com/en_us/blog/security/these-are-the-drivers-you-are-looking-for-detect-and-prevent-malicious-drivers?p=2)).
  - WFP: 5441/5447 cu `FilterName` „Custom Outbound Filter”, 5157 cu `Application` = binar EDR **(config: Audit Filtering Platform Policy Change / Connection)** ([Sigma](https://detection.fyi/sigmahq/sigma/windows/builtin/security/win_security_hktl_edr_silencer), [Elastic](https://www.elastic.co/guide/en/security/8.18/potential-evasion-via-windows-filtering-platform.html)).
- **Idee de detecție:** 5007 cu excludere pe `C:\`, `%TEMP%`, `ProgramData`, `Users`, `Downloads` sau pe `*.exe`; 5001 neînsoțit de schimbare de politică GPO; 7045 driver kernel cu nume/hash în LOLDrivers sau `.sys` din `Temp|ProgramData|Users`; serviciu de securitate oprit (7036 „stopped”) la ≤ 10 min după instalarea unui driver; filtru WFP care vizează binare EDR.
- **Fals pozitive:** excluderi puse de administratori pentru aplicații de business; drivere de jocuri/anti-cheat sau utilitare hardware vechi (aceleași drivere vulnerabile).
- **Pentru controlor:** atacatorul a oprit sau „orbit” antivirusul, uneori folosind un driver vechi, semnat, dar cu o defecțiune cunoscută.

### 28. Ștergerea urmelor — T1070.001, T1070.004, T1070.006

- **De ce e în top:** `wevtutil cl security|system|application` (RansomHub), 1102 + 104 (LockBit), ștergerea uneltelor (peste jumătate la Lunar Spider; Sysmon 23 la Akira; rclone șters la ~20 h după exfiltrare) ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/), [DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/)); Mandiant nu a putut determina vectorul inițial în 34% din intruziuni, posibil din lipsă de jurnale ([Infosecurity](https://www.infosecurity-magazine.com/news/vulnerability-credential-initial)).
- **Urme Windows:** Security 1102 (`SubjectUserName`), System 104 (`Channel`) **(implicit)**; goluri de timp în EVTX (salt de `EventRecordID` sau intervale fără evenimente); Prefetch `WEVTUTIL.EXE`; USN `FILE_DELETE` pentru unelte cunoscute; $MFT intrări cu nume de unelte (config rclone recuperată din MFT) ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/)); `fsutil usn deletejournal` [NECITAT].
- **Idee de detecție:** 1102 sau 104 oricând (severitate mare dacă e în afara unei ferestre de mentenanță); ≥ 2 canale curățate în 5 min; Prefetch/USN arată un executabil care nu mai există pe disc.
- **Fals pozitive:** reimaginare, curățare manuală de către administrator (rar și trebuie documentată).
- **Pentru controlor:** atacatorul a șters „camera de supraveghere” a calculatorului; faptul că lipsesc înregistrări este el însuși o dovadă.

### 29. Colectare și exfiltrare (arhivare, rclone, SFTP, site-uri de upload, tunele) — T1560.001, T1567, T1048, T1572

- **De ce e în top:** rclone SFTP pe 443 (RansomHub), rclone redenumit `sihosts.exe` prin FTP ~10 h (Lunar Spider), 7-Zip + temp.sh (Lynx), FileZilla SFTP ~77 GB și SSH (Akira), cloudflared/`ssh -R`; Sophos: exfiltrare la ~3 zile mediană; CrowdStrike: furt de date începe uneori în 4 minute ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report Lunar Spider](https://thedfirreport.com/2025/09/29/from-a-single-click-how-lunar-spider-enabled-a-near-two-month-intrusion/), [DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/), [DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/), [Channel Insider](https://www.channelinsider.com/security/sophos-active-adversary-2025/), [CrowdStrike 2026](https://crowdstrike.com/en-us/press-releases/2026-crowdstrike-global-threat-report)); CISA: RansomHub folosește PuTTY, WinSCP, Rclone ([CISA AA24-242A](https://cisa.gov/news-events/cybersecurity-advisories/aa24-242a)).
- **Urme Windows:**
  - SRUM `C:\Windows\System32\sru\SRUDB.dat` **(implicit pe clienți)**: octeți trimiși/primiți per aplicație și utilizator, ~30–60 zile, scris orar ([Magnet](https://www.magnetforensics.com/blog/srum-forensic-analysis-of-windows-system-resource-utilization-monitor/), [SANS ISC](https://isc.sans.edu/diary/31896)).
  - `rclone.conf` (în `%AppData%\rclone\` sau lângă binar), linii de comandă `copy|sync … --include-from … sftp:`; Amcache arată produsul „Rclone” chiar dacă fișierul e redenumit [NECITAT].
  - Istoric browser: `temp.sh`, `/upload`, `gofile`, `mega.nz` ([DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/)); FileZilla `%AppData%\FileZilla\recentservers.xml`, `sitemanager.xml`; WinSCP `HKCU\Software\Martin Prikryl\WinSCP 2\Sessions` [NECITAT].
  - Arhive: `7zG.exe` cu părinte `explorer.exe`, arhive pe Desktop; 5145 pe SYSVOL înainte de exfiltrare ([DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/)).
  - pcap: SFTP/SSH pe 22/443 spre IP-uri VPS, `*.trycloudflare.com`, volume mari ieșire.
- **Idee de detecție:** SRUM: aplicație care nu e browser/OneDrive/Teams cu `BytesSent` > 1 GB într-o oră sau > 10× media; orice rclone (după nume, produs Amcache sau argumente); `ssh.exe -R` sau `cloudflared` pe stație/server; istoric browser cu site de upload anonim urmat de arhive create în ≤ 1 h.
- **Fals pozitive:** backup în cloud, sincronizări legitime, administratori care folosesc WinSCP.
- **Pentru controlor:** înainte de a bloca fișierele, atacatorii le-au copiat la ei, ca să amenințe apoi cu publicarea.

### 30. Blocarea recuperării și criptarea — T1490, T1486, T1489

- **De ce e în top:** finalul tuturor cazurilor ransomware: `vssadmin delete shadows /all /quiet`, `Get-VM | Stop-VM -Force`, `fsutil behavior set SymlinkEvaluation R2L:1 R2R:1` (RansomHub); joburi Veeam șterse din consolă (Lynx); shadow copies șterse prin WMI la 1 s după fiecare `locker.exe -p=… -n=15` (Akira); `LB3_pass.exe` cu parolă (LockBit) ([DFIR Report RansomHub](https://thedfirreport.com/2025/06/30/hide-your-rdp-password-spray-leads-to-ransomhub-deployment/), [DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/), [DFIR Report Akira](https://thedfirreport.com/2026/06/29/from-bing-search-to-ransomware-bumblebee-and-adaptixc2-deliver-akira-3/), [DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)); Phobos/Backmydata: extensie `.backmydata`, note `info.hta` și `info.txt` ([TVR Info](https://tvrinfo.ro/dnsc-recomanda-cu-fermitate-ca-nimeni-sa-nu-plateasca-rascumpararea-catre-atacatorii-sistemului-informatic-hipocrate/)).
- **Urme Windows:** 4688/Prefetch pentru `vssadmin`, `wmic shadowcopy delete`, `bcdedit /set … recoveryenabled no`, `wbadmin delete catalog`, `fsutil behavior set SymlinkEvaluation`; USN: rafală de `RENAME_NEW_NAME` cu aceeași extensie nouă și `FILE_CREATE` al aceluiași nume de notă în multe directoare **(implicit)**; executabil cu argumente `-pass|-p=|--dir|--mode fast|-n=|-only-local|netonly` ([DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/)); Defender 1116 `Ransom:Win32/…`; jurnalul Veeam de ștergere joburi (pe serverul de backup) ([DFIR Report Lynx](https://thedfirreport.com/2025/12/17/cats-got-your-files-lynx-ransomware/)); schimbarea imaginii de fundal (`HKCU\Control Panel\Desktop\Wallpaper`) ([DFIR Report ActiveMQ](https://thedfirreport.com/2026/02/23/apache-activemq-exploit-leads-to-lockbit-ransomware/)).
- **Idee de detecție:** oricare dintre comenzile de ștergere a copiilor/recuperării → critic; USN cu > 500 redenumiri cu aceeași extensie nouă în 5 min; același fișier `README*/info.*/*RECOVER*` creat în > 20 de directoare.
- **Fals pozitive:** administrarea spațiului de shadow copies (`vssadmin resize`), migrări de fișiere.
- **Pentru controlor:** atacatorul a distrus copiile de rezervă și apoi a blocat fișierele, ca singura cale de recuperare să pară plata.

---

## 2. Tabel sintetic: tehnica → artefactul LogAnalyzer care o vede

| # | ATT&CK | EVTX implicit | EVTX config | Registru | Fișiere/artefacte | Rețea |
|---|---|---|---|---|---|---|
| 1 | T1078 | 4624, 4648, 4672 | — | — | Unattend.xml | — |
| 2 | T1133/T1021.001 | 1149, LSM 21/22/24/25, 4778/4779 | — | fDenyTSConnections, Terminal Server Client | — | 3389 |
| 3 | T1110 | 4625, 4740 | 4771/4776 (DC) | — | — | — |
| 4 | T1190 | — | 4688 | — | USN web root, Amcache, Prefetch | exploit |
| 5 | T1566 | MsiInstaller | 4688 | — | Zone.Identifier, LNK, Prefetch QuickAssist | — |
| 6 | T1204.004 | PS 4104 (warn) | 4688, 4104 full | RunMRU, TypedPaths? | Prefetch | trycloudflare |
| 7 | T1189/T1204.002 | MsiInstaller | — | — | istoric browser, Zone.Identifier, Amcache | domeniu fals |
| 8 | T1059.001 | 4104 warn, 400 | 4104 full | — | PSReadLine history, Prefetch | — |
| 9 | T1059.003/.005 | — | 4688 | — | Prefetch wscript/cscript, scripturi | — |
| 10 | T1218/T1574 | — | 4688, Sysmon 7 | — | Prefetch rundll32 (multi-hash), Amcache | — |
| 11 | T1105 | Bits-Client | 4688 | — | qmgr.db, CryptnetUrlCache, Prefetch certutil | descărcare |
| 12 | T1047/T1546.003 | WMI 5861 | Sysmon 19–21 | — | OBJECTS.DATA | — |
| 13 | T1027 | 4104 warn | 4104 full | — | dimensiune/entropie scripturi | — |
| 14 | T1219 | 7045, 4624 tip 2 loopback | — | Services\* | ad_svc.trace, connection_trace.txt, Splashtop/ScreenConnect logs, Amcache | relay RMM |
| 15 | T1003.001 | Defender 1116 | Sysmon 10, 4688 | WDigest, RunAsPPL | .dmp, Prefetch | — |
| 16 | T1003.002/.003/.006 | ESENT 325/327 (DC) | 4662 | — | ntds.dit/SYSTEM copiate | DRSUAPI |
| 17 | T1555/T1539 | 5379 | 4688, 4104 | — | Login Data/Cookies citite | stealer C2 |
| 18 | T1558.003 | 4769 (DC) | — | — | — | Kerberos |
| 19 | T1557.001/T1187 | 4624 anonim NTLM | 5145 | LmCompatibilityLevel | — | SMB/EFSRPC |
| 20 | T1649 | — | 4886/4887 (CA) | — | — | — |
| 21 | Discovery | — | 4688, 5145 | — | Prefetch netscan/adfind/nxc, delete.me, .nxc\smb.db | scanare 445/3389 |
| 22 | T1021.002/T1569.002 | 7045, 4624 tip 3 | 5145 ADMIN$ | Services\PSEXESVC | Prefetch psexec, USN binare identice | SMB |
| 23 | T1543.003 | 7045, 7040 | 4697 | Services\* | — | — |
| 24 | T1053.005 | TaskScheduler/Operational? | 4698/4702 | TaskCache\Tree (SD), Tasks | System32\Tasks XML | — |
| 25 | T1547.001/T1548.002 | — | Sysmon 13 | Run/RunOnce/Winlogon, ms-settings | Startup folder | — |
| 26 | T1136/T1098 | 4720/4728/4732/4738 (DC) | idem pe stații | SAM | — | — |
| 27 | T1562/T1068 | Defender 5001/5007/5013, 7045 driver, CodeIntegrity | 5441/5447/5157 | Defender Exclusions/Policies | .sys noi, Amcache drivere | — |
| 28 | T1070 | 1102, 104 | — | — | goluri EVTX, Prefetch wevtutil, USN delete, MFT | — |
| 29 | T1560/T1567/T1048/T1572 | 7045 (cloudflared) | 5145 SYSVOL | WinSCP sessions | SRUM, rclone.conf, istoric browser, FileZilla XML | volume mari, SFTP |
| 30 | T1490/T1486 | Defender 1116 | 4688 | Wallpaper | USN rename/create în masă, Prefetch vssadmin | — |

---

## 3. Atacuri specifice rețelelor izolate (air-gap) și urmele lor

### 3.1 Campanii documentate

| Campanie | Actor / atribuire | Mecanism | Urme de căutat |
|---|---|---|---|
| **GoldenJackal** (2019–2024) | Neatribuit ferm; suprapuneri cu Turla/MoustachedBouncer | Stick infectat pe stația conectată (JackalWorm / componentă worm care copiază GoldenDealer pe stick); pe stația izolată se colectează fișiere; la reconectare, GoldenDealer trimite automat datele la C2. Set nou 2022–2024 în Go: GoldenUsbCopy / GoldenUsbGo (AES/RSA + gzip), unele stații ca „stagere” sau distribuitori de configurație. Prima compromitere confirmată a unor sisteme izolate, la o ambasadă și la o organizație guvernamentală UE ([The Hacker News](https://thehackernews.com/2024/10/goldenjackal-target-embassies-and-air.html), [BleepingComputer](https://www.bleepingcomputer.com/news/security/european-govt-air-gapped-systems-breached-using-custom-malware/)) | executabile pe stick (Amcache/Prefetch cu cale `E:\`), fișiere criptate/arhive pe stick, același stick (serial) văzut pe stația conectată și pe cea izolată |
| **Ramsay** (2019–2020) | Similarități cu DarkHotel **[NESIGUR]** | Spreader care infectează executabile PE de pe stick; colectează documente Word (inclusiv de pe unități de rețea și amovibile), le criptează și le **atașează în fișiere .doc** care rămân funcționale; exfiltrarea de către o componentă externă care caută „magic values” ([BleepingComputer](https://www.bleepingcomputer.com/news/security/new-ramsay-malware-steals-files-from-air-gapped-computers/)) | fișiere `.doc` modificate (dimensiune crescută, timp de modificare), executabile de pe stick cu hash schimbat, instalator fals de

---

> **DOCUMENT INCOMPLET (2026-10-11):** scrierea s-a oprit în secțiunea 3.1. Secțiunile 3 (rest) și 4 (reguli P1/P2/P3) lipsesc și trebuie completate într-o sesiune separată.
