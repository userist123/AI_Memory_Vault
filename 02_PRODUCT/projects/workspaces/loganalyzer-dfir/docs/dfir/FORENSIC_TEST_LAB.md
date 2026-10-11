# LogAnalyzer — Laboratorul de testare forensică

Starea la 2026-10-05. Cod: `LogAnalyzer.Dfir.Tests/Corpus.cs`, `ForensicValidationTests.cs`, `Corpus/NanAgentCase/targets.json`.

## Corpusul

- Corpusul real (incidentul NanAgent de pe MARIUS-PC, 19.09.2026) este probă locală. **Nu se adaugă niciodată în repository.**
- Testele îl găsesc prin `LADFIR_CORPUS` (implicit `D:\FORENSIC_CASE`).
- `targets.json` conține doar căile relative și valorile așteptate, extrase manual din investigație:
  - nume, număr de rulări, ore;
  - bytes SRUM;
  - ID-uri de eveniment;
  - fluxuri PCAPNG;
  - numărul de intrări Amcache.
- Codul de producție nu citește acest fișier.

## Trei stări, niciodată confundate

| Situație | Testele pe corpus | Raport |
|---|---|---|
| corpusul e prezent integral | rulează | `FORENSIC VALIDATION = AVAILABLE (n/n …)` |
| corpusul lipsește parțial | secțiunile lipsă sunt SĂRITE | `FORENSIC VALIDATION = PARTIAL (k/n …)` + „rezultatul verde NU constituie validare forensică” |
| corpusul lipsește | toate SĂRITE | `FORENSIC VALIDATION = UNAVAILABLE (0/n …)` + aceeași mențiune |

Starea se scrie la fiecare rulare:
- testul `Forensic_validation_state_is_reported` nu e sărit niciodată și scrie `forensic_validation.txt` lângă binarele de test;
- în CI, pasul „Report forensic validation state” o pune în rezumatul jobului și emite un avertisment când nu e `AVAILABLE`.

Motivul fiecărui test sărit conține și el „FORENSIC VALIDATION = UNAVAILABLE”.

## Rulare de validare

```powershell
$env:LADFIR_CORPUS = "D:\FORENSIC_CASE"
$env:LADFIR_REQUIRE_CORPUS = "1"
dotnet test LogAnalyzer.Dfir.Tests
```

Cu `LADFIR_REQUIRE_CORPUS=1`, testele pe corpus nu mai sunt sărite. Dacă lipsește vreo secțiune, rularea **eșuează**: o validare nu poate fi verde fără corpus. Verificat: fără corpus, 9 eșecuri; cu corpus, totul trece.

## Ce acoperă corpusul azi

| Secțiune | Parser | Ce se verifică |
|---|---|---|
| prefetch, prefetchMsiexec | PrefetchParser | SETUP.EXE: 4 rulări, ultima la 14:56:26 UTC; MSIEXEC face referire la BOOTSTRAP_7D57.CMD |
| srum | SrumNetworkParser | msbuild.exe: 7.764.746 B trimiși, 100.581.358 B primiți, la 15:15 UTC |
| defenderEvtx, msiEvtx | EvtxParser | 1116 GenCodeInjected pe NanAgent32.exe; 1033 MsiInstaller CxUtilSvc Helper |
| pcapng | PcapngParser | peste 700 de fluxuri, DNS napps-1.com, SNI chatgpt.com |
| systemHive | SystemHiveExecutionParser, RawRegistry | BAM: 72 valori comparate cu `reg query` (70 identice, 2 mai noi în fereastra de 7 s dintre capturi); AppCompatCache identic octet cu octet cu `reg query` |
| tasks | ScheduledTaskParser | toate cele 303 definiții din System32\Tasks parsate; fiecare acțiune Exec egală cu „Task To Run” din `schtasks /query /v` (fără ghilimele, cum le afișează schtasks); taskurile cu mai multe acțiuni apar ca „Multiple actions” |
| userHive | UserHiveParser, RawRegistry | NTUSER salvat la 23:50:59 vs reg export la 23:51:00: fiecare intrare UserAssist (număr de rulări, FILETIME de ultimă rulare, inclusiv numele malformate) și fiecare valoare Run/RunOnce |
| softwareHive | SoftwareHiveParser, RawRegistry | SOFTWARE vs reg export: Run, RunOnce, WOW6432Node Run/RunOnce, Winlogon Shell și Userinit |
| servicesHive | ServicesParser | SYSTEM.hiv (23:50:56) vs WMI Win32_Service (23:50:41): fiecare serviciu există, cu StartMode, cont și cale identice după normalizarea făcută de SCM (`%SystemRoot%`, `%ProgramFiles%`, `\SystemRoot\`, `\??\`). WMI dă „Unknown” pentru 3 servicii protejate; testul le numără separat. |
| chromeHistory | BrowserHistoryParser | History real, citit dintr-o copie de lucru (sursa neatinsă), comparat cu extracția separată din investigația manuală: cele 5 descărcări (ora de start, cale, URL tab, octeți) și fiecare vizită din 18–20.09.2026 UTC; referința trunchia URL-urile lungi, ceea ce testul tratează explicit |
| (investigație + History) | pipeline, DOWNLOAD-THEN-EXEC | lanțul critic din 19.09 începe cu descărcarea `SamFw_FRP_Tool_…_302044.zip` de pe tzd4is.cyou, legată cu încredere ridicată de SETUP.EXE rulat din `Downloads\SAMFW_FRP_TOOL_V5.9_SETUP_DOWNLOAD_LATES_ARCHIVE_FILE_302044` (calea din Prefetch) |
| lnk | LnkParser | toate cele peste 300 de linkuri din Recent parsate (2 fișiere sunt umplute cu zerouri, iar eșecul lor e verificat ca atare); 182 cu cale în LinkInfo comparate cu WScript.Shell: țintă, argumente, folder de lucru identice, cu excepția pierderilor ANSI ale shell-ului, numărate separat |
| securityEvtx | EvtxParser, AuditCoverage, FIREWALL-RULE-USERPATH | Security.evtx complet: 0 evenimente 5156/5157 (NOT_AVAILABLE) și 902 evenimente 4688 în doar 31 de minute din 6 săptămâni, procese de boot (smss, wininit, lsass ×23), deci PARTIAL; concordă cu auditpol.txt („No Auditing”). Firewall.evtx: peste 500 de modificări de reguli, nicio alarmă falsă pe regulile Defender/Chrome/Store |
| usbHive | UsbDevicesParser | 2 stick-uri Kingston și SSD-ul Samsung (USBSTOR), același SSD prin UAS („MSFT30…”, producător/model din Enum\SCSI după ContainerID), camera Sony DSC cunoscută doar din MountedDevices (intrarea din Enum lipsește, conectată ultima dată pe 16.06 după jurnal); orele 0066 confirmate de 1006 pentru toate cele 4 cu oră |
| jumpLists | JumpListParser, CompoundFile | 54 de Jump Lists reale: toate se deschid (4 nu au lungimea multiplu de sector, 4 sunt goale, 39 au streamul Windows 11 DestListPropertyStore); 1.880 de intrări DestList, fiecare cu linkul ei, iar 1.555 de căi coincid cu ținta linkului. Este doar o verificare de consistență internă |
| amcache | AmcacheParser | peste 6000 de intrări, 0 corupte, SHA-1 pe peste 90% |
| usn | UsnJournalParser | două exporturi `fsutil` făcute la ore diferite: peste 50.000 de USN comune, identice (nume, motiv, oră, ID fișier); orele convertite față de Prefetch (UTC); ID-ul jurnalului = ziua instalării (SOFTWARE `InstallTime`); ștergerea în masă a fișierelor `.pf` în ambele exporturi |
| (investigație) | pipeline | lanțul INCIDENT-CHAIN critic din 19.09.2026; proveniență completă; 0 constatări respinse |

## Validare diferențială cu implementări independente

`DifferentialFact` rulează un test numai dacă există și corpusul, și implementarea de referință de pe mașină. Azi referința e modulul `sqlite3` din Python. Dacă oricare lipsește, testul e sărit cu motivul „DIFFERENTIAL REFERENCE UNAVAILABLE” sau „FORENSIC VALIDATION = UNAVAILABLE”; nu trece niciodată fără ele.

| Secțiune | Parser | Referință independentă |
|---|---|---|
| firefoxPlaces | FirefoxHistoryParser | Python `sqlite3` pe o copie: toate vizitele (oră brută µs, URL) și descărcările (dateAdded, URI destinație) |
| systemHive, servicesHive, userHive, softwareHive | parserele de registru | `reg query` / `reg export` / WMI capturate pe stație |
| tasks | ScheduledTaskParser | `schtasks /query /v` |
| lnk | LnkParser | WScript.Shell |
| usbHive | UsbDevicesParser | jurnalul Partition/Diagnostic 1006 |

## Rularea de laborator (spec §21, §22)

`ForensicLab.cs` este separat de CI-ul de bază: rulează doar cu `LADFIR_LAB=1`.

```powershell
$env:LADFIR_LAB = "1"
$env:LADFIR_LAB_OUT = "D:\rapoarte"   # opțional: unde se copiază raportul
dotnet test LogAnalyzer.Dfir.Tests --filter ForensicLab
```

Pentru fiecare caz scrie REAL CORPUS, KNOWN TRUTH, EXPECTED, ACTUAL, DIFF și starea, în `forensic_lab_report.json` și `.txt` lângă binarele de test, iar cu `LADFIR_LAB_OUT` și în `.md`:

| stare | când |
|---|---|
| PASS | toate valorile comparate sunt egale |
| PARTIAL | există diferențe, iar fiecare este explicată de ceva raportat chiar de parser (un gol de probă) |
| FAIL | există o diferență neexplicată (testul eșuează) |
| UNVERIFIED | corpusul lipsește; cerut fără corpus, laboratorul **eșuează**, nu trece |

Cazuri:
- adevăr cunoscut din investigație (`targets.json`): PrefetchParser (SETUP.EXE), SrumNetworkParser (msbuild.exe),
  EvtxParser (Defender 1116), PcapngParser (fluxuri, DNS, SNI), AmcacheParser;
- diferențial (§22): **fiecare** fișier EVTX din corpus față de `wevtutil`. Se compară numărul de înregistrări (`gli`),
  primul și ultimul RecordID (`qe /rd:false|true /c:1`), continuitatea RecordID, cea mai veche și cea mai nouă oră.

Rezultat pe 2026-10-06: **174 PASS, 1 PARTIAL, 0 FAIL, 0 UNVERIFIED (175 de cazuri, 170 de fișiere EVTX, 379 MB)**.

Prima rulare a arătat 58 de FAIL. Cauza era referința, nu parserul: `oldestRecordNumber` din `wevtutil gli /lf` este 1 pentru
fișierele exportate și nu este un RecordID. Referința a fost înlocuită cu RecordID-ul primei și ultimei înregistrări
citite de `wevtutil qe`.

Cazul PARTIAL este `Application.evtx`: are 24.517 înregistrări, dar intervalul de RecordID are doar 24.486 de valori.
Citit în ordine cu `wevtutil`, jurnalul revine o dată, de la 44790 (2026-08-08 17:50:08 UTC) la 44760 (17:51:21 UTC),
deci 31 de RecordID-uri sunt folosite de două ori. Jurnalul System arată în acel moment o oprire neașteptată: EventLog 6008
(„The previous system shutdown … was unexpected”), Kernel-Power 41 și repornire la 17:51:05. Parserul EVTX raportează acum
refolosirea ca gol de probă și face locatorul unic (`EventRecordID=44760;occurrence=2`), iar AF04 o raportează împreună cu
oprirea necurată.

## Ce lipsește (planificat)

- **Validare diferențială** cu instrumente forensice dedicate (EZTools, Plaso) pentru Prefetch, SRUM și Jump Lists. Nu sunt instalate, iar aplicația nu descarcă și nu rulează instrumente externe din proprie inițiativă. EVTX este comparat cu `wevtutil`.
- Laboratorul anti-forensics: vezi `ANTI_FORENSICS_TESTING.md`.
- **Teste de corupție** pentru fiecare parser. EVTX are `EvtxRepairTests`; celelalte nu au încă.
