# LogAnalyzer - Maparea documentului "Lessons Learned" (107 secțiuni) pe cerințe, WP și cod

> Stare: DOAR MAPARE. Nu s-a modificat cod, nimic commit/push. Regula proprietarului respectată: nu s-a refăcut nicio cercetare/audit existent; ce e ACOPERIT trimite la rândul/WP existent fără re-verificare în cod. Cod verificat doar pentru NOU/EXTINDE.

## 1. Surse și metodă

| Element | Valoare |
|---|---|
| Intrare nouă | `02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/LOGANALYZER_LESSONS_LEARNED_COMPLETE.md` (107 secțiuni; tokenii "citeturn…" ignorați) |
| Reutilizat (neverificat din nou) | `CONTRACT_AUDIT_STAGE1.md` (rânduri R1-R23 / U1-U25, §7 WP0-WP12, §8 deciziile 1-15), cele două contracte, `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md` (WP-ED, WP-PKG, WP2 în lucru), `WINDOWS_TOOLING_COMPAT.md`, `HG585_ACCREDITATION_REQUIREMENTS.md` (parcat; doar fapte de cod) |
| Cod verificat | `origin/main` @ `94020777d` (WP12 îmbinat), doar pentru elementele NOU/EXTINDE, prin `git archive` + grep (fără worktree, nimic rulat) |
| Prescurtări căi | față de `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`: DC=`LogAnalyzer.Dfir.Core/`, DW=`LogAnalyzer.Dfir.Windows/`, App=`LogAnalyzer.App/`, Core=`LogAnalyzer.Core/`, PIPE=`DW/Investigation/InvestigationPipeline.cs` |
| Verdict | ACOPERIT = deja în rând/WP/decizie; NOU = absent din orice rând; EXTINDE <rând> = rând existent, dar documentul adaugă conținut material |
| Stare cod (NOU/EXTINDE) | IMPLEMENTED / PARTIAL / UNWIRED / MISSING, cu aceleași reguli ca în audit (fără consumator de producție = nu IMPLEMENTED) |
| Tier | din §101: T1 obligatoriu, T2, T3. Elementele fără listare în §101 sunt încadrate după sursa de date de care depind (marcat "T1*" = modelul e T1, sursele de îmbogățire sunt T3) |

## 2. Rezumat (numărători)

| Verdict | Secțiuni | % |
|---|---|---|
| ACOPERIT (rând/WP/decizie existentă) | 24 | 22% |
| NOU | 54 | 50% |
| EXTINDE <rând> | 29 | 27% |
| **Total** | **107** | |

Starea în cod a celor 83 de secțiuni NOU + EXTINDE (verificat pe `origin/main` @ `94020777d`):

| Verdict | IMPLEMENTED | PARTIAL | UNWIRED | MISSING | Total |
|---|---|---|---|---|---|
| NOU | 0 | 3 (§8, §21, §22) | 0 | 51 | 54 |
| EXTINDE | 0 | 29 | 0 | 0 | 29 |
| **Total** | **0** | **32** | **0** | **51** | **83** |

§72 este numărat MISSING (stiva nouă nu are nimic; partea legacy contrazice regula, vezi tabelul).
Nicio secțiune NOU/EXTINDE nu e IMPLEMENTED: nimic din lecții nu este complet, cu consumator de producție și test. UNWIRED apare doar tangențial (§47: `DnsTunnelingClassifier`, deja R7.9). Din cele 83, `PARTIAL` înseamnă aproape mereu "sursa/parserul există, dar regula de corelare, starea standard sau modelul de dovadă lipsesc".

Distribuție pe tier (83 NOU/EXTINDE): Tier 1 = 48 (inclusiv 4 marcate T1*, model T1 / surse T3), Tier 2 = 23, Tier 3 = 12.

## 3. Constatare structurală importantă (nu era în audit)

Auditul Stage 1 nu a tratat trei module deja conectate la UI, care acoperă o parte din lecții, dar **în afara cazului DFIR** (rezultatele lor sunt `ControlCheck`/`ActionEntry`, nu `Finding`/graf/custodie, deci nu primesc dovezi, stări standard sau verificare):

| Modul | Cod | Consumator producție | Ce acoperă din lecții |
|---|---|---|---|
| "Control stație" (A01-A07, C01-C06, P01-P14, U01-U06, D01, N01-N02, S01-S02) | `DW/Audit/ControlEvaluator.cs`, `DW/Audit/StationFacts.cs` / `App/ViewModels/StationControlViewModel.cs:52` | politici, audit, ștergere jurnale, ora, conturi, RDP, USB, rețele cunoscute, software |
| "Domeniu / utilizator" (DM01-DM31) | `DW/Domain/DomainEvaluator.cs`, `DirectoryCollector.cs`, `UserInvestigation.cs`, `DomainGraph.cs` / `App/ViewModels/DomainInvestigationViewModel.cs:62,93` | AD, conturi privilegiate, autentificări pe DC |
| "E-mail" (EM01-EM05) | `DW/Domain/MailInvestigation.cs:94-125` / `DomainInvestigationViewModel.cs:116,134` | reguli inbox, forward, rafale, destinatari externi |

Aceste module folosesc `ControlStatus {Conform, Neconform, DeVerificat, Nedeterminat}` (`ControlEvaluator.cs:7`), un al șaptelea vocabular de stări. Mai multe verdicte "Conform" se dau pe absența evenimentelor (ex. D01 `ControlEvaluator.cs:357`, A05 `:228`), ceea ce contrazice §61/§100 din lecții. Sunt tratate mai jos ca "bază de reutilizat" pentru WP14/WP15/WP16, nu ca funcționalitate de refăcut.

## 4. Tabel pe secțiuni (§1-§107)

| § | Titlu | Verdict | Rânduri / WP | Stare cod | Dovadă (cale:linie) | Tier |
|---|---|---|---|---|---|---|
| 1 | Principiul fundamental (întrebări + pipeline) | ACOPERIT | R1.1-R1.5, R9.*, R19.1; WP5/WP7 | - | - | - |
| 2 | Artefactul nu este acțiune; fiecare parser: ce poate/nu poate dovedi, limite, surse de corelare, confidence | EXTINDE R4.0/R5.2/R19.10 | WP2 (câmpuri) + WP11 (conținut per parser) | PARTIAL | `ParserDescriptor` are `Limitations`, `Status`, `Validation` (`DC/Parsing/IEvidenceParser.cs:13-30`), dar nu "WHAT IT CAN PROVE / CANNOT PROVE / CORRELATION SOURCES / CONFIDENCE" ca câmpuri; consumator `parsers.json` (PIPE:139) | T1 |
| 3 | Stările standard ale dovezii (10) | ACOPERIT | R20.0-R20.*, R19.3, decizia 2; WP2 | - | - | - |
| 4 | Coverage și confidence obligatorii; absența dovezii nu e dovada absenței | ACOPERIT | R19.7-R19.9, U3, U21, R3.10; WP5 | - | - | - |
| 5 | Parser health (10 stări: AVAILABLE…NOT_ENABLED) | EXTINDE R3.10/U20 | WP2 (enum) + WP5 (afișare) | PARTIAL | `EvidenceStatus {Success, Empty, Failed, NotAvailable, Partial, SkippedByDesign}` (`DC/Model/Enums.cs:4`): lipsesc UNSUPPORTED, INVALID, BLOCKED, NOT_PRESENT, NOT_ENABLED; `ParserMaturity` (`IEvidenceParser.cs:10`) e altă axă; erorile apar în PDF §4 (R3.10) | T1 |
| 6 | Rapoarte (concluzie vs tehnic; nu simplifica dovada) | ACOPERIT | R14.1-R14.9, U15; WP10 | - | - | - |
| 7 | AI interpret, nu autoritate | ACOPERIT | R11.*, R10.9, U11, decizia 14; WP8 | - | - | - |
| 8 | Ciclul de viață al jurnalului (WRITE/EXPORT/ARCHIVE/VERIFY/ROTATE/CLEAR…); clear nu e automat atac | NOU | WP15 | PARTIAL (detectare da, semantică greșită) | Ștergerea e detectată: `AF01` (`DC/Analysis/AntiForensics.cs:80-91`), `LOG-TAMPER` (`DC/Analysis/Correlation.cs:90`), `A05` (`DW/Audit/ControlEvaluator.cs:228`); dar niciun model export→hash→arhivare→verificare→clear; `LOG-TAMPER` dă High pe orice 1102/104 (`Correlation.cs:92`), `A05` dă Neconform WP15a (2026-10): profilul de proceduri stochează procedura de rotire (EXPORT→HASH→ARCHIVE→VERIFY→CLEAR) și conturile/ferestrele aprobate; clear-ul devine Routine/Unexpected, iar 1100 și 4719 primesc același ciclu (decizia 27). Potrivirea pașilor procedurii cu probe (export/hash/arhivă înainte de clear) NU este făcută încă. | T1 |
| 9 | Clear lunar în program de lucru; ROUTINE/EXPECTED vs UNEXPECTED | NOU | WP15 (profil de proceduri) | PARTIAL (WP15a: profil introdus manual, clear Routine/Unexpected; program de lucru doar comparație) | Nicio noțiune de program de lucru sau procedură de rotație în DFIR (grep `working hours`/`program de lucru` = 0 în DC/DW); depinde de intrări de la proprietar (Q2) WP15a: `DC/Profile/*`, `LogMaintenancePolicy` din profil în Correlation/AF01/motoarele legacy; secțiune goală = „nedefinit”. | T1 |
| 10 | HG 585: LogAnalyzer trebuie să fie modul Access Evidence | NOU | WP13 | MISSING | Nu există modul; singura legătură: `AuditCoverage` semnalează lipsa 4663 (`DC/Analysis/AuditCoverage.cs:23`) | T1* |
| 11 | Access Evidence: câmpuri (user, clearance, need-to-know, zonă, document, clasificare, acțiune…) și acțiuni (OPEN…CLASSIFICATION_CHANGE) | NOU | WP13 | MISSING | `RelationType` nu are Copied/Accessed/Printed/Transmitted/Mounted (`DC/Graph/EvidenceGraph.cs:11-16`); `CaseInfo` nu are zonă/clearance (`DC/Model/CaseInfo.cs:4-18`); nu există normalizare 4663; "fișier există ≠ citit" e respectat doar implicit (R5.2) | T1* |
| 12 | Clasificarea documentelor (NATO/naționale, echivalențe) | NOU | WP13 | MISSING | grep `NATO/STRICT SECRET/SECRET DE SERVICIU/COSMIC` în DC/DW/App = 0 (doar un "Clasificare" ≠ nivel, `DC/Memory/VaultExport.cs:61`); tabel de echivalențe absent | T1* |
| 13 | Detectarea documentelor clasificate (marcaje, metadate, etichete DLP/IRM; nu din nume) | NOU | WP13 | MISSING | Niciun parser de marcaje/etichete; `LnkParser`/`JumpListParser` dau doar căi | T3 (surse DLP/DMS) |
| 14 | Document peste nivelul permis (document vs stocare vs zonă vs utilizator vs need-to-know) | NOU | WP13 | MISSING | Nu există niveluri pe stocare/zonă/utilizator în model | T3 |
| 15 | Medii de stocare: urmărire (ID, serie, proprietar, clasificare, zonă, utilizator, first/last seen, fișiere copiate…) | EXTINDE R4 (UsbDevicesParser) | WP14 | PARTIAL (WP14a: registru de medii cu proprietar, clasificare, zonă, utilizator; first/last seen per mediu; fișiere scrise doar cu dovezi) | Serie, VID/PID, first install/last arrival/removal, literă: `DW/Parsers/UsbDevicesParser.cs:12-60`, înregistrat `DW/Parsers/WindowsParsers.cs:14` (Validated); lipsesc proprietar, clasificare, zonă, utilizator atribuit, fișiere scrise/copiate pe mediu WP14a: `DC/Registers/MediaRegister.cs` (nr. de înregistrare, serie, VID/PID, tip, clasificare, utilizator, zonă, valabilitate, stare), jurnal de audit înlănțuit, copie în caz cu SHA-256; `MEDIA-*` în `DC/Analysis/Wp14Rules.Media.cs`. Inventarul fizic, custodele, emiterea/returnarea și distrugerea (rândul 53) nu sunt modelate. | T1 |
| 16 | USB și medii amovibile; USB inserat + scrieri + hash + proces de copiere = SUPPORTED COPY | EXTINDE R4 / R6.5 | WP14 (corelare) + WP11 (surse) | PARTIAL (WP14a: USB complet din USBSTOR, Partition/Diagnostic 1006, 6416, Kernel-PnP 400/410, DriverFrameworks 2003/2100/2102; scriere doar cu dovezi) | Prezență: parser + canal `Partition/Diagnostic` colectat (`DW/Acquisition/Collectors.cs:19`) + `D01` (`ControlEvaluator.cs:346-361`, consumator `StationControlViewModel.cs:52`). Nu există regulă USB în `Correlation.cs` (grep `usb` = 0), nici Kernel-PnP/DriverFrameworks, nici potrivire de hash, nici legătură cu robocopy/xcopy WP14a: `MEDIA-*` per mediu + `MEDIA-FILE-ACTIVITY` (LNK/JumpList = acces, 4663 cu mască de scriere și USN de creare = scriere; altfel „prezență, nu copiere”). Nu există potrivire de hash și nici legătură cu robocopy/xcopy (WP14b). | T1 |
| 17 | CD/DVD/optic separat de USB (IMAPI, ISO, burn) | NOU | WP14 | PARTIAL (WP14a: `MEDIA-OPTICAL-ACTIVITY`, separat de USB) | Doar `DriveTypes` "CDROM" în `DC/FileSystem/LnkParser.cs:17`; grep `IMAPI/DVD/\.iso/burn` = 0 WP14a: unitate optică (Kernel-PnP/6416), IMAPI, LNK cu volum CDROM, imagine .iso montată (VHDMP) și instrumente de inscripționare din `Data/airgap_lists.json`; inscripționarea se susține doar din IMAPI sau instrument executat; un disc optic nu are serie, deci nu se compară cu registrul. | T1 |
| 18 | Medii neautorizate: AUTHORIZED MEDIA vs OBSERVED MEDIA | NOU | WP14 | PARTIAL (WP14a: AUTHORIZED / REGISTERED / UNREGISTERED / UNAUTHORIZED / UNKNOWN față de registrul introdus) | Doar text de recomandare "Comparați cu registrul suporturilor aprobate" (`ControlEvaluator.cs:361`); nu există inventar de import (Q1) WP14a: `Wp14Rules.Media.cs` compară seria observată cu registrul; registru gol = „registru nedefinit” (UNKNOWN), niciodată „conform”. Utilizatorul și zona se potrivesc doar dacă sursele le conțin. | T1 |
| 19 | Sanitizare medii (secure erase, format, diskpart clean, cipher /w) | NOU | WP14 | MISSING | grep `cipher /w/diskpart/sdelete/Format-Volume/sanitiz` = 0 în DC/DW; singura "sanitizare" e în legacy (MainViewModel, HG585 doc §2.7) | T2 |
| 20 | Air-gap: canale de transfer, hartă ZONA A/B/exterior | NOU | WP14 | MISSING | Nu există model de zone/canale | T1 |
| 21 | Conectare la rețea într-un sistem air-gapped (NIC up, DHCP, IP, rută, profil) | NOU (extinde N01/N02) | WP14 | PARTIAL (WP14a: `AIRGAP-NETWORK-CONNECTED`, `AIRGAP-DHCP-LEASE`; D01/N01/N02 nu mai dau Conform pe absență) | Profile rețea din registry + WLAN 8001/8003 + NetworkProfile 10000/10001: `ControlEvaluator.cs:363-392`, `StationFacts.cs:58-59`; canalele sunt colectate și în DFIR (`Collectors.cs:21`) dar nu au regulă; lipsesc DHCP-Client, NIC enable/disable, rută, ARP. Rezultatul "Conform" pe zero profile (`ControlEvaluator.cs:389`) contrazice §61 WP14a: NetworkProfile 10000/10001 (durată), DHCP-Client 50036/50037/50065/1103, destinații și canale autorizate din profil (Info); fără profil: „conectivitate observată, necesită explicație”. Lipsesc ruta și ARP. | T1 |
| 22 | NIC / Wi-Fi / Bluetooth (adaptor, driver, SSID, pairing) | NOU | WP14 | PARTIAL (WP14a: `AIRGAP-WIFI-ASSOCIATED`, `AIRGAP-BLUETOOTH-PAIRED`, `AIRGAP-NIC-ADDED`) | Wi-Fi asociere prin 8001 (`StationFacts.cs:58`); Bluetooth, instalare adaptor/driver, BSSID: absente WP14a: WLAN 8001/11001/11005, noduri BTHENUM/BTHLE, Kernel-PnP 400 clasa Net. BSSID și activarea/dezactivarea adaptorului nu sunt în sursele colectate. | T1 |
| 23 | Importuri din echipamente de rețea (switch, router, firewall, DNS, DHCP, proxy, VPN, IDS, EDR, SIEM, RADIUS, 802.1X, NAC) | NOU | WP16 | MISSING | Doar PCAPNG (R4.16) și `RemoteCollection` către gazde Windows; niciun import de aparate | T2/T3 |
| 24 | E-mail (acces, trimitere, atașamente, delegări, reguli, corelare cu documente) | EXTINDE R9/R7 (modul E-mail) | WP16 | PARTIAL | EM01-EM05 pe CSV Exchange: `DW/Domain/MailInvestigation.cs:94-125`, consumator `DomainInvestigationViewModel.cs:116,134`; nu produce `Finding`/graf; lipsesc mailbox access, atașamente, ștergere/mutare, delegare, corelare cu fișier/USB/clasificare. Notă P1: scriptul folosește `Connect-ExchangeOnline` (rulat de administratorul de mail, nu de aplicație) | T2 |
| 25 | Reguli e-mail (inbox/forward/extern/delegare/transport/impersonare) | EXTINDE R7 | WP16 | PARTIAL | Inbox rules EM03 și forwarding EM04 implementate (`MailInvestigation.cs:108-125`); lipsesc permisiuni mailbox/delegare, transport rules, admin impersonation | T2 |
| 26 | Servere de fișiere / SMB (4624/5140/5145/4663/4670, acces în masă) | NOU | WP11 (parser/normalizare EVTX, T1) + WP17 (volum) | MISSING | Nicio regulă/parser pentru 5140/5145/4670; 4663 apare doar ca lipsă de audit (`AuditCoverage.cs:23`) | T1 |
| 27 | NAS (SMB/NFS audit, snapshot, ACL, USB atașat) | NOU | WP16 | MISSING | Niciun import NAS | T2 |
| 28 | Servicii web interne (intranet, DMS, portaluri; import HTTP, autentificare, uploads) | NOU | WP16 | MISSING | Niciun parser IIS/HTTP/aplicații | T2 |
| 29 | Web upload/download (browser + proces + rețea + hash + destinație) | EXTINDE R4.11/R6.5 | WP16 + WP11 | PARTIAL | Download→execuție: `DOWNLOAD-THEN-EXEC` (`DC/Analysis/Correlation.cs:419`, parsere browser `DW/Parsers/BrowserHistoryParser.cs:12`); upload de document: absent (`NET-USERPATH-UPLOAD` `Correlation.cs:300` e din octeți SRUM, nu dovadă de fișier) | T2 |
| 30 | Platforme web / aplicații interne: acțiuni de business (login, descărcare, share, schimbare rol/clasificare, export) | NOU | WP16 | MISSING | - | T2 |
| 31 | Politici Windows / GPO: detectare create/delete/modify/link/filter/script/firewall/Defender/audit/USB… | NOU | WP15 | PARTIAL (WP15b: din EVTX, ambele ediții; gPLink din LDAP rămâne doar în ediția neclasificată, nefăcut) | WP15b (2026-10): `DC/Analysis/PolicyTimeline.cs` transformă Security 5136/5137/5139/5141/4739/4719/4907 și Microsoft-Windows-GroupPolicy/Operational în înregistrări (cine, când, țintă, atribut, vechi/nou), `Analysis/policy_timeline.json`, secțiunea „Cronologie politici”. Setările GPO din SYSVOL nu sunt în EVTX. Înainte: doar import de backup GPO pentru motorul de politici (`DC/Policy/PolicyImport.cs:98-107`) și evaluare stare curentă; detectarea modificărilor GPO din evenimente și gPLink din LDAP: grep = 0 (`DW/Domain/DirectoryCollector.cs`) | T1 |
| 32 | Ce s-a întâmplat când politica a fost scoasă (BEFORE→CHANGE→AFTER→APPLICATION→BEHAVIOR) | NOU | WP15 | PARTIAL (WP15b: axa BEFORE→CHANGE→AFTER→APPLICATION→BEHAVIOR și configured/applied/enforced/observed; comportamentul doar pentru audit, restul neevaluabil cu motiv) | WP15b: `PolicyChain` per modificare, `SettingLevels` per setare a politicii așteptate (legătura din profilul WP15a), regula `POLICY-CONTROL-GAP` (Medium; High doar dacă auditarea e dezactivată și o constatare High cade în fereastră), AF05 extins (magnitudine/direcție, fus orar, inversare RecordID/oră) și ferestre UNKNOWN în verificarea TEMPORAL. Înainte: motorul de politici dă diferență curent/dorit la un moment dat (`DW/Policy/PolicyWorkbench.cs:97`), nu axă temporală | T1 |
| 33 | GPO timeline cu diff (GUID, versiune, DC, admin, old/new, link, OU, filtru, replicare, aplicare client) | NOU | WP15 | MISSING | - | T1 |
| 34 | Politici scoase temporar (cât timp, ce sisteme, ce activitate în interval, ticket) | NOU | WP15 | MISSING | - | T1 |
| 35 | Schimbări politică audit (audit/advanced audit, logging PS, command-line, log size, retenție) | EXTINDE R4/AF (ANTI_FORENSICS_TESTING) | WP15 | PARTIAL | 4719: `Correlation.cs:90`, `AF10` (`AntiForensics.cs:232`), `AF09` (`:219`), `A06` (`ControlEvaluator.cs:236`), `A01-A02` (`:209-217`); lipsesc PowerShell/command-line logging ca schimbare, corelare cu GPO/secedit | T1 |
| 36 | Defender / unelte de securitate (oprire, excluderi, tamper, EDR, firewall) | EXTINDE R7 | WP11 + WP15 | PARTIAL | `DEF-TAMPER` (`Correlation.cs:76`), `DEF-DETECTION` (`:61`), `P07-P10` (`ControlEvaluator.cs:159-175`), `AF16` firewall (`AntiForensics.cs:321`); lipsesc oprire serviciu/agent EDR, dezinstalare agent, tamper protection | T1 |
| 37 | Conturi și privilegii (creat/activat/grup/admin local); creat ≠ folosit | EXTINDE R4.1 | WP11 | PARTIAL | În Control stație: `U01-U02` (`ControlEvaluator.cs:258-283`); în pipeline DFIR nicio regulă 4720/4732/4728 (grep `4720/4732` în `Correlation.cs` = 0); corelarea creat→folosit lipsește | T1 |
| 38 | Servicii / persistență | ACOPERIT | R4.8-R4.10, R6.5 (reguli `PERSIST-*`); WP11 | - | - | - |
| 39 | RDP / administrare la distanță (WinRM, PsExec, SMB admin share, SSH) | EXTINDE R4.1/R6.5 | WP11 | PARTIAL | `REMOTE-RDP-PUBLIC` doar pentru IP extern (`Correlation.cs:332`), `U03` (`ControlEvaluator.cs:318`), canale TerminalServices colectate (`Collectors.cs:17`); WinRM/PsExec/SSH/Remote Registry și RDP intern: absente | T1 |
| 40 | Print / scan / flux fizic (print→scan→PDF→USB ca lanț) | NOU | WP16 (+ lanț în WP14) | MISSING | grep `PrintService/print job/scanner` = 0 | T3 |
| 41 | Import print server / MFP / scan / fax | NOU | WP16 | MISSING | - | T3 |
| 42 | Backup și replicare (început/eșec/restore/snapshot/suport atașat; backup ≠ sigur) | NOU | WP16 | MISSING | grep `wbadmin/8222/snapshot` (în afara politicii) = 0 | T2 |
| 43 | Ștergere snapshot (singur ≠ atac; + modificare în masă) | NOU | WP11 | MISSING | `vssadmin.exe` doar în lista de utilitare redenumite (`AntiForensics.cs:44`) | T2 |
| 44 | Activitate în masă (1/10/1000/10000 fișiere; bulk read/copy/rename/delete…) cu baseline configurabil | NOU | WP17 | MISSING | - | T2 |
| 45 | Data staging (arhive mari, ISO, director temporar, agregare) | NOU | WP17 | MISSING | - | T2 |
| 46 | Transfer de rețea (sursă, destinație, utilizator, proces, octeți; INTERNAL/AUTHORIZED/UNEXPECTED…) | EXTINDE R4.6/R4.16 | WP11 + WP14 | PARTIAL | SRUM octeți per aplicație, PCAPNG, `IpClassifier`; reguli `NET-LOLBIN-TRAFFIC`/`NET-USERPATH-UPLOAD` (`Correlation.cs:300`); clasificarea AUTHORIZED/UNEXPECTED cere listă de destinații (Q4) | T2 |
| 47 | DNS (domenii noi/rare, schimbare server) | EXTINDE R4.17/R7.9 | WP11 | PARTIAL | Canal `DNS-Client/Operational` colectat (`Collectors.cs:21`), fără detecții; `DnsTunnelingClassifier` UNWIRED (R7.9) | T2 |
| 48 | DHCP / IP (lease nou/neașteptat, gateway, server DHCP) | NOU | WP14 | PARTIAL (WP14a: `AIRGAP-DHCP-LEASE`) | - WP14a: adresa, serverul și gateway-ul sunt citate doar dacă evenimentul le conține; semnificația ID-urilor depinde de versiunea Windows. | T2 |
| 49 | Firewall (regulă veche/nouă, cine, de ce, trafic după schimbare) | EXTINDE R4/AF16 | WP15 | PARTIAL | Reguli create/șterse: `AF16` (`AntiForensics.cs:321`), `Correlation.cs:189-191` (2004/2005/2097/2099); lipsesc old/new rule, profil, trafic observat după | T1 |
| 50 | Model AIR-GAP INTEGRITY (categorie de findings, 19 subcategorii) | NOU | WP14 | PARTIAL (WP14a: categoria „Air-gap integrity”, 19 subcategorii în date) | Nu există categorie; `Finding.Category` e text liber (`DC/Model/Analysis.cs`) WP14a: `Finding.Category = "Air-gap integrity"` pe sisteme air-gapped/autonome, `Finding.AirGap` cu subcategoria; lista de 19 în `Data/airgap_lists.json`; fila „Integritate air-gap” în investigație. | T1 |
| 51 | Transfer zonă-la-zonă (ZONE, CLASSIFICATION FLOW VIOLATION) | NOU | WP14 (model) + WP13 (clasificare) | MISSING | - | T3 |
| 52 | Transferuri între SPAD / RTD-SIC | NOU | WP14 | MISSING | - | T3 |
| 53 | Medii de stocare: control și inventar (custode, locație, emitere, returnare, distrugere) | NOU | WP14 | PARTIAL (WP14a: registru de medii și utilizatori) | Import de registru/inventar absent (Q1) WP14a: registrul de medii și cel de utilizatori/abilitări (decizia 17, introdus manual; autentificarea în aplicație lipsește, vezi blocajele). Custode, locație, emitere, returnare, distrugere: neimplementate. | T3 |
| 54 | Storage classification mismatch (DOCUMENT > STORAGE = CRITICAL REVIEW) | NOU | WP13 | MISSING | - | T3 |
| 55 | Mediu neautorizat: AUTHORIZED/REGISTERED/UNREGISTERED/UNAUTHORIZED/UNKNOWN | NOU | WP14 | PARTIAL (WP14a: cinci stări implementate) | - WP14a: vezi rândul 18. | T1 (blocat de Q1) |
| 56 | Ciclul de viață al documentului (CREATE…DESTROY, cine/când/unde/cum/autorizare/dovadă) | NOU | WP13 | MISSING | - | T3 |
| 57 | Clasificare modificată (adăugată/scoasă/coborâtă/ridicată; etichete DLP/IRM) | NOU | WP13 | MISSING | - | T3 |
| 58 | Documente șterse: delete / recycle / secure delete / retention purge / arhivare | EXTINDE R4.14 | WP13 | PARTIAL | Doar ștergeri din USN (`DC/FileSystem/UsnJournalParser.cs`, `AF06` `AntiForensics.cs:181`); Recycle Bin/versiuni/snapshot neparsate | T2 |
| 59 | Integritatea dovezii (cale, mărime, timestamps, SHA-256, parser, transformări, hash original vs copie) | ACOPERIT | R3.1-R3.6, R3.9; WP3 | - | - | - |
| 60 | Chain of custody (cine/ce/când/unde/de ce/hash/rezultat) | ACOPERIT | R3.7, R16.10, R22.7, decizia 9; WP3 | - | - | - |
| 61 | Sursă lipsă = SOURCE UNAVAILABLE + impact asupra verdictului | ACOPERIT | R3.10, R19.7, R10.7, U21; WP5 | - | - | - |
| 62 | Model AD/GPO (domeniu, DC, OU, GPO, link, versiune; replicare→client→comportament) | EXTINDE (modul Domeniu) | WP15 | PARTIAL | `DomainEvaluator` DM01-DM31 (`DW/Domain/DomainEvaluator.cs:23-92`), consumator `DomainInvestigationViewModel.cs:62`, `DomainGraph.cs:52`; GPO/OU/link/replicare: lipsesc; rezultatele nu intră în cazul DFIR (Q5, decizia 5 le ține sub Advanced) | T1 |
| 63 | "Ce s-a întâmplat când politica a fost scoasă" ca finding de prim rang (CONTROL GAP FOLLOWED BY MEDIA ACTIVITY) | NOU | WP15 + WP14 | MISSING | - | T1 |
| 64 | Politici urmărite (Audit, PS, Defender, Firewall, USB, instalare dispozitive, rețea, parole, RDP, WinRM, Update, AppLocker, WDAC, screen lock, BitLocker) | EXTINDE R12.1 (POLICY_ENGINE) | WP15 | PARTIAL | Motor de politici (registry/audit/service/secpol) `DC/Policy/PolicyModel.cs:7-30`, `DW/Policy/SettingProviders.cs`; Control stație P01-P14 (`ControlEvaluator.cs:134-189`: parole, lockout, UAC, RDP, firewall, Defender, BitLocker, SMBv1, WDigest, USB); lipsesc AppLocker, WDAC, WinRM, Update, screen lock, device installation | T1 |
| 65 | Schimbări de politică: OLD/NEW/WHO/WHEN/SOURCE/GPO/OU/TARGET/APPLICATION/DURATION | NOU | WP15 | MISSING | Jurnalul de politică are hash-chain (`DC/Policy/PolicyExecution.cs:279`) dar doar pentru schimbările făcute de aplicație | T1 |
| 66 | Control gap (CONTROL, DISABLED START/END, AFFECTED SYSTEMS, ACTIVITY DURING GAP) | NOU | WP15 | MISSING | - | T1 |
| 67 | E-mail + informație clasificată (lanț acces→staging→atașament→e-mail→destinatar→transfer) | NOU | WP16 (după WP13) | MISSING | - | T3 |
| 68 | Web + informație clasificată | NOU | WP16 (după WP13) | MISSING | - | T3 |
| 69 | Fișier server/NAS + medii amovibile (NAS→citiri mari→staging→USB→scrieri) ca secvență ridicată automat | NOU | WP14 (regula) + WP16/WP11 (surse) | MISSING | Doar `INCIDENT-CHAIN` generic (`Correlation.cs:443`) | T1 (fără NAS: SMB + USB) |
| 70 | Evidence Graph multi-sursă (USER→NAS/USB/browser/e-mail) | EXTINDE R8.1 | WP2/WP13 | PARTIAL | Graf implementat și conectat (R8.*), dar `RelationType` nu are Copied, Accessed, Transmitted, Printed, Mounted/Unmounted (`EvidenceGraph.cs:11-16`); tipuri noi de entități (dispozitiv, zonă, document clasificat) neverificate/absente | T1 |
| 71 | Baseline (rotație, USB, admin, GPO, backup, e-mail, acces, mentenanță) | NOU | WP17 | MISSING | În stiva nouă nu există; în legacy, comparație de cronologii (`App/ViewModels/MainViewModel.cs:3007-3027`) | T2 |
| 72 | After-hours pentru prioritizare, nu "suspicious" | NOU | WP15 (profil program) + corecție legacy | MISSING (stiva nouă); legacy CONTRAZICE | `ExplainableAiRiskEngine.cs:74-81` (Core/Services) adaugă puncte de risc pentru logon 01:00-05:00; `UserBehaviorAnalyticsEngine.cs:25` fereastră fixă 23-06; `MainViewModel.cs:2441-2496` | T1 (regulă anti-overclaim) |
| 73 | Anomalii în masă (comparativ cu baseline) | NOU | WP17 | MISSING | - | T2 |
| 74 | Backup și arhivare (verificat: destinație, hash, retenție, restore test) | NOU | WP16 | MISSING | - | T2 |
| 75 | NIS2: ce se transformă în capabilități (asset inventory, logging, … reporting) | ACOPERIT | R12.1-R12.4 (COMPLIANCE_MODEL), decizia 11, todo "audit/compliance report profile"; WP10 | - | - | - |
| 76 | NIS2 logging: ce se loghează, unde, retenție, cine revizuiește, alerte, ce lipsea | EXTINDE R12/U21 | WP5 (matrice) + WP10 (profil audit) | PARTIAL | `AuditCoverage` (doar 4688/5156/4663: `AuditCoverage.cs:17-24`), `A01-A04` (`ControlEvaluator.cs:209-226`); retenție/cine revizuiește/alerte: absente | T1 |
| 77 | Ciclul incidentului (DETECT→…→LESSONS LEARNED, cu aprobare) | NOU | WP9 | MISSING | Răspunsul are doar REQUEST→VERIFY (R13); nu există stări de incident | T2 |
| 78 | Eficacitatea controlului: exists + applied + enforced + blocked + attempt logged | NOU | WP15 | MISSING | Motorul de politici verifică prin re-citire (R13.4/R12), nu lanțul complet | T1 |
| 79 | CONFIGURED ≠ APPLIED ≠ ENFORCED ≠ OBSERVED | NOU | WP15 (+ SemanticType CONFIGURATION din WP2) | MISSING | `SemanticType` lipsește (R5.1); `P14` raportează politica USB ca stare curentă (`ControlEvaluator.cs:189`) | T1 |
| 80 | Config drift (așteptat vs actual) | EXTINDE R12.1 | WP15 | PARTIAL | `PolicyWorkbench.Assess` (`DW/Policy/PolicyWorkbench.cs:97`) + pagina `PolicyViewModel.cs:115-141`; finding explicit CONFIGURATION DRIFT și istoric: absente | T1 |
| 81 | Software neautorizat (portabil, nesemnat, driver, remote, arhivare, imaging, USB utility) | EXTINDE R4.4/R6.5 | WP15 (listă aprobată) + WP11 | PARTIAL | `S01-S02` (`ControlEvaluator.cs:394-423`), `EXEC-USERPATH`, `LIVE-UNSIGNED-USERPATH` (`PIPE:286`); nu există listă de software aprobat (Q3) | T2 |
| 82 | Software nou portabil + USB + arhivă mare + scrieri | NOU | WP14 | MISSING | - | T1 |
| 83 | Instrumente remote (AnyDesk, TeamViewer, VNC, RustDesk…) | NOU | WP11 (+ listă aprobată WP15) | MISSING | grep `AnyDesk/TeamViewer/RustDesk/VNC` = 0 în DC/DW | T2 |
| 84 | Manipulare timp (oră, fus orar, NTP, serviciu oprit, drift) + timestamp nesigur reduce confidence | EXTINDE R6.2 | WP2 + WP15 | PARTIAL | `AF05` (`AntiForensics.cs:142-158`, 4616 + Kernel-General 1), `A07` (`ControlEvaluator.cs:242`); lipsesc fus orar, NTP/W32Time oprit, drift și legătura cu confidence | T1 |
| 85 | Log tampering: clasificare ROUTINE/AUTHORIZED/UNEXPECTED/UNEXPLAINED/SUSPICIOUS | EXTINDE R16/AF | WP15 | PARTIAL | Detectare: AF01-AF04, AF10, `LOG-TAMPER`, `LOG-GAP` (`Correlation.cs:90,120`); clasificarea pe context lipsește; log forwarding/SIEM agent oprit: absente | T1 |
| 86 | Evidence gap detection (gap de timestamp, sursă lipsă, parser gap, arhivă, drift) | EXTINDE R3.10/R10.7 | WP3/WP5 | PARTIAL | Goluri RecordID: `AF04` (`AntiForensics.cs:102`), `LOG-GAP` (`Correlation.cs:120`), audit dezactivat: `AuditCoverage.Gaps` (PIPE:200); gap de timp (01:02→04:15) și drift: absente | T1 |
| 87 | Verificare independentă (LogAnalyzer investighează, Veritas contestă) | ACOPERIT | R10.0, decizia 1; WP4 | - | - | - |
| 88 | Model de verdict (finding, evidence, correlation, contradictions, missing, coverage, confidence, verdict) | ACOPERIT | R19.*, R10.*, U4, U13; WP2/WP4. Notă: "External disclosure: NOT ESTABLISHED" = afirmație explicită de limită, intră în §100 | - | - | - |
| 89 | Meniu pentru utilizator (HOME…ADVANCED; Windows logs, Policy changes, Classified information, Removable media, E-mail, Web, File servers, NAS) | ACOPERIT | R17.1, R18.*, U2; WP6/WP7. Notă: termenii noi se adaugă în glosarul WP6 | - | - | - |
| 90 | Modulul "What happened?" (narațiune cronologică + concluzie) | ACOPERIT | R1.1, U9, U24.2; WP7 | - | - | - |
| 91 | Modulul "What is unknown?" | ACOPERIT | U8, U24.5, R19.7; WP6 | - | - | - |
| 92 | Modulul "Why?" | ACOPERIT | U5, R1.3; WP6 | - | - | - |
| 93 | "Show me the evidence" (claim→sursă→eveniment→raw→hash) | ACOPERIT | U6, U24.4, R1.4; WP6 | - | - | - |
| 94 | Evidence graph: lanț de entități și relații (accessed, copied, transmitted, printed, mounted…) | EXTINDE R8.1 | WP2/WP13 | PARTIAL | Vezi §70: `RelationType` (`EvidenceGraph.cs:11-16`) acoperă Executed…Detected, nu Copied/Transmitted/Printed/Mounted/Accessed | T1 |
| 95 | Integrare Memory Vault (doar informație verificată) | ACOPERIT | R15.1-R15.4, decizia 10; WP4/WP7 | - | - | - |
| 96 | Confidențialitate / minimizare (scop, minimizare date, scop caz, scop colectare, redactare, acces, retenție, audit) | EXTINDE R3.8/decizia 11 | WP3 (câmpuri de scop/scop colectare) + WP10 (redactare la export) | PARTIAL | Doar `Sensitivity` pe dovadă, implicit Confidential (`DC/Model/Enums.cs:16`, `DC/Model/EvidenceItem.cs:32`, `DC/Case/CaseWorkspace.cs:68`); `CaseInfo` fără scop/limită de colectare (`CaseInfo.cs:4-18`); redactare = 0 în grep; retenție doar în decizia 11 (WP3, neimplementat) | T1 |
| 97 | Corelare multi-sursă cu provenance per sursă | ACOPERIT | R3.6, R6.5, R9.4; sursele noi sunt tratate la §23-30 (WP16) | - | - | - |
| 98 | Stări disponibilitate sursă (AVAILABLE/NOT_AVAILABLE/NOT_ENABLED/NOT_APPLICABLE/NOT_COLLECTED) | EXTINDE R3.10/U21 | WP2 + WP5 | PARTIAL | `EvidenceStatus` (`Enums.cs:4`) are NotAvailable, SkippedByDesign; lipsesc NOT_ENABLED (auditul era oprit), NOT_APPLICABLE, NOT_COLLECTED ca stări distincte; `AuditCoverage` folosește NotAvailable pentru "audit dezactivat" (`AuditCoverage.cs:44`) | T1 |
| 99 | Severitate separată de confidence | ACOPERIT | R19.4, R19.9 (câmpuri separate `Severity`/`Confidence`, `Enums.cs:12-14`); afișare U4; WP2/WP6 | - | - | - |
| 100 | Reguli anti-overclaim (NEVER artifact→execuție … AI→fapt verificat) | EXTINDE R5.2/R5.4/R2.2/R12.3 | WP2 (teste de contract) + addendum WP1 | PARTIAL | Aplicat: graf fără cauzalitate (R8.3), compliance fără rotunjire (R12.3), AI (R11). Încălcări găsite: legacy `ExecutionProven` (R5.2), scor after-hours (§72), `Conform` pe absență (`ControlEvaluator.cs:357,389`), clear = High (`Correlation.cs:92`), "Conform" pe zero evenimente în A05 (`:228`). Nu există suită care să codifice cele 13 reguli NEVER | T1 |
| 101 | MVP: Tier 1/2/3 | ACOPERIT | Prioritizare; folosit în coloana Tier și în §5 | - | - | - |
| 102 | Formula finală | ACOPERIT | R9, R1, R23 | - | - | - |
| 103 | Model special air-gapped (canale AUTHORIZED?/OBSERVED?/WHEN/WHO/OBJECT/CLASSIFICATION/TRANSFER/DESTINATION/EVIDENCE) | NOU | WP14 | PARTIAL (WP14a: `Finding.AirGap`) | Vezi §20, §50 WP14a: `AirGapDetail` poartă canalul, autorizat?, observat, când, cine, obiect, clasificare (caz / registru), direcție, destinație, probe; ce lipsește din surse se spune în cuvinte („necunoscut: …”). | T1 |
| 104 | Model special HG 585 (PERSON→AUTHORIZATION→NEED-TO-KNOW→SYSTEM/ZONE→CLASSIFICATION→DOCUMENT/MEDIA→ACCESS→TRANSFER→ARCHIVE→RETENTION→DESTRUCTION) | NOU | WP13 | MISSING | Nu există persoană/autorizare/need-to-know în model; registrele de acces ale organizației nu sunt importabile (Q1, Q2). Legătură: WP-ACR-05 (parcat) adaugă marcaj pe cazurile aplicației, altceva decât clasificarea documentelor analizate | T1* |
| 105 | Model NIS2 (ASSET→RISK→CONTROL→…→LESSON LEARNED) | EXTINDE R12.1 | WP5 (inventar de active în caz) + WP15 + WP10 | PARTIAL | Lanțul Requirement→Control→Evidence→Assessment există (`DC/Compliance/ComplianceModel.cs`); active și risc: absente | T2 |
| 106 | Surse oficiale de referință | ACOPERIT | `HG585_QUOTE_SOURCES.md`, `HG585_ACCREDITATION_REQUIREMENTS.md` Partea 1 | - | - | - |
| 107 | Concluzie de produs (un singur Evidence Graph, 9 rezultate finale) | ACOPERIT | R1, R19.1, R23, U25 | - | - | - |

## 5. Pachete de lucru propuse (WP13-WP17) și addendum-uri la WP existente

Reguli respectate: decizia 13 (ediția P1 clasificată nu conține rețea/AI/cod care modifică gazda: toate WP de mai jos sunt doar citire/analiză/import de fișiere, deci intră în ambele ediții), decizia 14 (totul deterministic, fără AI), niciun `wmic`, nimic nu e construit peste componente UNWIRED (Global Production-Consumer Rule). Fiecare WP nou trebuie să producă `Finding`/`TimelineEvent`/relații de graf în cazul DFIR, nu `ControlCheck` paralele.

| WP | Titlu | Secțiuni acoperite | Conținut (scop) | Depinde de | Tier |
|---|---|---|---|---|---|
| **WP13** | Access Evidence și clasificare (HG 585) | 10, 11, 12, 13, 14, 54, 56, 57, 58, 104 (+51, 70, 94 în parte) | Vocabular de acțiuni (OPEN…CLASSIFICATION_CHANGE); înregistrare Access Evidence peste 4663/5140/5145/USN/LNK; scară de clasificare cu echivalențe NATO/naționale ca date de configurare (nu din numele fișierului); detectare doar din marcaje/etichete/metadate importate; reguli CLASSIFICATION MISMATCH (document > stocare/zonă/utilizator) doar când datele există, altfel NOT_ASSESSED; tipuri noi de entități/relații în graf (Copied, Accessed, Transmitted, Printed, Mounted) | WP2 (SemanticType, stări), WP3 (câmpuri de caz), WP4 (verdict); coordonat cu WP-ACR-05 (parcat) pentru câmpul de clasificare din `CaseInfo` | T1* (model + 4663), T3 (surse DLP/DMS) |
| **WP14** | Integritate air-gap și control medii | 15-23 (fără 23), 46 (în parte), 48, 50-53, 55, 69, 82, 103 | Categoria AIR-GAP INTEGRITY ca `Finding.Category` cu subcategoriile din §50; model de zone și canale (§20, §103); USB complet (Kernel-PnP, scrieri, hash, proces de copiere), CD/DVD/IMAPI/ISO separat; NIC/Wi-Fi/Bluetooth/DHCP/rută; inventar de medii autorizate importat din fișier (stări AUTHORIZED…UNKNOWN, niciodată "malițios" din UNREGISTERED); sanitizare medii; corelări (control gap→USB, NAS/SMB→staging→USB, tool portabil+USB+arhivă) | WP2, WP5 (acoperire), WP15 (control gap), WP11 (surse EVTX); Q1 și Q2 la proprietar | T1 (USB, CD/DVD, NIC), T2 (sanitizare, DHCP), T3 (zone/SPAD/inventar) |
| **WP15** | Proceduri, politici GPO, control gap, ciclu de viață jurnale | 8, 9, 31-36, 49, 62-66, 72, 78-80, 81 (listă), 84, 85, 105 | Profil de proceduri importat (program de lucru, rotație lunară, ce se exportă și unde; Q2); clasificare ROUTINE/AUTHORIZED/UNEXPECTED/UNEXPLAINED/SUSPICIOUS pentru ștergerea jurnalelor (înlocuiește "High pe orice clear"); evenimente GPO (5136/5137/5141/4739) + gPLink/versiune din LDAP + export GPO; axă BEFORE→CHANGE→AFTER→APPLICATION→BEHAVIOR; control gap ca finding de prim rang; vocabular CONFIGURED/APPLIED/ENFORCED/OBSERVED; config drift în timp; manipulare timp cu reducere de confidence | WP2, WP3 (stări de ciclu de viață), WP4; reutilizează motorul de politici (`DC/Policy`), `AntiForensics`, `AuditCoverage` | T1 |
| **WP16** | Import multi-sursă: NAS, fișiere, e-mail, web, rețea, print, backup | 23-25, 27-30, 40-42, 67, 68, 74 | Importatori de fișiere (fără rețea) pentru: audit NAS (SMB/NFS), HTTP/IIS și aplicații interne, Exchange (extinde EM01-EM05 și le transformă în `Finding`), DNS/DHCP/proxy/VPN/IDS/EDR/SIEM, print server/MFP, jurnale de backup; fiecare cu `ParserDescriptor`, provenance și stări de disponibilitate; lanțurile clasificat→e-mail/web după WP13 | WP2, WP11 (contractul de parser), WP13 pentru lanțuri | T2 (NAS, e-mail, web, DNS, DHCP, EDR, SIEM), T3 (print, DLP, NAC, VPN, proxy, DMS) |
| **WP17** | Baseline, volum și staging | 26 (volum), 44, 45, 71, 73 | Praguri configurabile (1/10/1000/10000), bulk read/copy/delete/rename/archive, staging (arhive, ISO, directoare temporare), baseline învățat din cazuri anterioare și comparat; "anomalie ≠ incident"; fără a folosi after-hours ca semnal de rău | WP13 (acțiuni), WP14 (medii), WP15 (profil program) | T2 |

### Addendum-uri la WP existente (fără WP nou)

| WP | Adaosuri din lecții |
|---|---|
| WP1 (închis) → mic addendum "WP1b" | Corectarea afirmațiilor legacy găsite acum: scor de risc pentru after-hours (`Core/Services/ExplainableAiRiskEngine.cs:74-81`, `UserBehaviorAnalyticsEngine.cs:25`); `Conform` pe absență de evenimente în Control stație (`ControlEvaluator.cs:228,357,389`, 30 de apariții `ControlStatus.Conform`); `LOG-TAMPER` High pe orice clear devine temporar "de revizuit" până la WP15 |
| WP2 | Câmpuri per-parser "poate/nu poate dovedi, surse de corelare" (§2); stări de sănătate parser și de disponibilitate sursă extinse (§5, §98: UNSUPPORTED, INVALID, BLOCKED, NOT_PRESENT, NOT_ENABLED, NOT_APPLICABLE, NOT_COLLECTED); relații de graf noi (§70, §94); suită de teste "anti-overclaim" pentru cele 13 reguli NEVER (§100); reducere de confidence pentru timestamp nesigur (§84); aceste adaosuri sunt aditive, compatibile cu decizia 2 (WP2 este în lucru: de trimis agentului WP2 înainte să înghețe schema) |
| WP3 | Gap de timp în jurnal și drift (§86); câmpuri de caz: scop, limită de colectare, categorie de sistem, retenție (§96, deja în todo); registrul de acces al aplicației nu se confundă cu Access Evidence (WP13) |
| WP5 | Matricea de acoperire extinsă la jurnalizare NIS2: ce se loghează, retenție, cine revizuiește (§76); inventar de active în caz (§105) |
| WP9 | Ciclul incidentului DETECT…LESSONS LEARNED cu actor/aprobare (§77) |
| WP10 | Redactare la export (§96); profilul de raport audit/NIS2 cu lanț ASSET→…→LESSON (§105) |
| WP11 | Reguli și parsere T1: SMB 5140/5145/4670 (§26), conturi creat→folosit (§37), RDP intern/WinRM/PsExec (§39), oprire agent securitate (§36), snapshot șters (§43), DNS (§47), instrumente remote (§83) |

### Ordine propusă (cu dependențe)

Starea curentă: WP-ED → WP-PKG ∥ WP2 în lucru; urmează WP3, WP4 (audit §7/§8).

1. WP2 (în lucru) primește addendum-ul de mai sus **înainte** de înghețarea schemei.
2. WP3, WP4 neschimbate (plus adaosurile de mai sus).
3. **WP1b** (mic, oricând după WP2, înainte de WP15).
4. **WP15** (log lifecycle + GPO + control gap): cel mai mare câștig T1, nu depinde de datele proprietarului în afara profilului de proceduri (Q2), iar rezultatul (control gap) alimentează WP14.
5. **WP14** (medii și air-gap): după WP15 și WP11-T1; necesită răspunsul la Q1 pentru inventarul de medii; USB/CD/NIC pot începe fără inventar (OBSERVED, cu AUTHORIZED = UNKNOWN).
6. **WP11 T1** (SMB, conturi, RDP, agenți) în paralel cu WP14.
7. WP5 → WP6 → WP7 neschimbate; pagina "Removable media / Policy changes / Classified information" intră în WP7 după WP14/WP15/WP13.
8. **WP13** (Access Evidence): după WP4; felia T1 (acțiuni + 4663/5140/5145) poate merge mai devreme, clasificarea (T3) așteaptă surse și Q1/Q2.
9. **WP16** (importuri T2) apoi T3, **WP17**.
WP8, WP9, WP10, WP12 ca în audit.

## 6. Primele 10 goluri Tier 1

1. **Ciclul de viață al jurnalului nu există; orice clear e tratat ca incident** (§8, §9, §85): `LOG-TAMPER` High, `A05` Neconform. Riscă fals pozitiv pe procedura lunară normală. WP15.
2. **GPO/politică: nicio detectare a schimbărilor și nicio axă temporală** (§31-§34, §62, §65): 0 evenimente GPO, 0 gPLink. WP15.
3. **Control gap ca finding** (§34, §63, §66): absent; cel mai valoros lanț din lecții. WP15 (+WP14).
4. **Configured/applied/enforced/observed** (§78-§80): motorul de politici verifică doar starea curentă. WP15.
5. **USB: nicio corelare prezență→scrieri→hash→proces** (§16): doar prezența (parser Validated). WP14.
6. **CD/DVD complet absent** (§17): 0 cod. WP14.
7. **Conectare la rețea în sistem izolat** (§21, §22, §50, §103): există doar în Control stație (N01/N02) și cu "Conform" pe zero profile; nu e în cazul DFIR. WP14.
8. **Medii autorizate vs observate** (§18, §55): niciun inventar; blocat de Q1. WP14.
9. **Stări de sănătate parser și disponibilitate sursă incomplete** (§5, §98) și **suită anti-overclaim** (§100): fundația pentru restul; trebuie în WP2 acum. WP2.
10. **SMB/file server și Access Evidence minimal** (§26, §10-§11): 0 reguli 5140/5145; 4663 doar ca lipsă de audit. WP11 + WP13.

Urmează ca importanță: confidențialitate/minimizare (§96), manipulare timp fără legătură la confidence (§84), gap-uri de timp (§86).

## 7. Întrebări pentru proprietar (aplicația nu poate inventa răspunsurile)

1. **Q1 Inventar de medii autorizate.** De unde vine lista de medii aprobate (serii, proprietar, clasificare, zonă, custode, utilizator atribuit)? Registrul de evidență a mediilor al organizației (Excel/CSV/export dintr-o aplicație) sau unul creat în aplicație? Fără el, §18/§55/§53 rămân la OBSERVED + "autorizare necunoscută". Ce format de import acceptăm?
2. **Q2 Autorizări și need-to-know.** Sursa pentru clearance-ul persoanelor și need-to-know (§11, §14, §104): registrul de personal/ORNISS sau altă listă? Aplicația nu poate deduce autorizarea din Windows. Se importă într-un fișier, sau câmpurile rămân NOT_ASSESSED?
3. **Q3 Profil de proceduri și politici așteptate.** Pentru fiecare organizație: program de lucru, procedura de rotație a jurnalelor (zi, oră, unde se arhivează, cine), lista de software aprobat și de instrumente remote aprobate, politica GPO așteptată. Cine o furnizează și în ce format (YAML al motorului de politici, existent)?
4. **Q4 Zone, sisteme și destinații.** Lista zonelor (rețea/sistem, nivel), canalele de transfer aprobate și destinațiile de rețea autorizate (§20, §46, §51, §52). Există o schemă a rețelei care poate fi importată?
5. **Q5 Rezultatele din Control stație / Domeniu / E-mail.** Se aliniază la modelul DFIR (devin `Finding` în caz) sau rămân module independente sub Advanced (decizia 5)? Răspunsul schimbă ordinea WP14-WP16, fiindcă majoritatea bazei existente stă în ele.
6. **Q6 Scară de clasificare.** Confirmați tabelul de echivalențe din §12 ca date de configurare și ce etichete/marcaje reale se pot citi (DLP, IRM, DMS, antet de document). Fără surse tehnice, §13/§14/§54 rămân NOT_ASSESSED.
7. **Q7 Ordinea Tier 3.** Sursele T3 (DLP, NAC, VPN, proxy, DMS, registre clasificate) sunt disponibile la clienții țintă sau amânate?
8. **Q8 Scop și minimizare (§96).** Ce câmpuri obligatorii de scop/limită de colectare cere organizația pe fiecare caz, și cine aprobă o colectare extinsă?
9. **Q9 Corecții legacy (WP1b).** Aprobați relabelarea scorului after-hours și a verdictelor "Conform" pe absență din Control stație ca addendum mic la decizia 3?
10. **Q10 E-mail în ediția P1.** Scripturile Exchange se generează și se rulează de administratorul de mail (aplicația nu se conectează); confirmați că fluxul este acceptabil și în P1 (decizia 13), sau în P1 rămâne doar import de CSV.
