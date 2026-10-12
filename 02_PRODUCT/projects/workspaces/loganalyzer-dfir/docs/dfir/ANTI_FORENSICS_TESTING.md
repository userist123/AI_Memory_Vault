# Laboratorul anti-forensics (spec §23)

Cod: `Dfir.Core/Analysis/AntiForensics.cs`. Rulează în fiecare investigație și scrie `Analysis/anti_forensics.json` și
secțiunea 6 a raportului PDF. Teste: `LogAnalyzer.Dfir.Tests/AntiForensicsTests.cs`.

## Rezultate

| rezultat | înseamnă |
|---|---|
| DETECTED | urma tehnicii a fost observată în probe (cu trimitere la fiecare înregistrare) |
| NOT_DETECTED | sursa care ar fi arătat urma a fost analizată și nu o arată |
| UNDETERMINED | sursa lipsește, nu este parsată, sau absența urmei nu dovedește nimic |

Nu există rezultatul „curat”. DETECTED descrie ce s-a întâmplat, nu intenția: ștergerea unui serviciu, schimbarea orei
sau a auditului pot fi legitime; analistul decide.

## Verificări

| id | tehnică (spec §23) | ATT&CK | sursa | DETECTED când | NOT_DETECTED doar dacă |
|---|---|---|---|---|---|
| AF01 | EVTX cleared | T1070.001 | Security 1102, System 104 (furnizor Microsoft-Windows-Eventlog) | există evenimentul | Security și System au fost analizate |
| AF02 | EVTX corrupted | T1070.001 | verificarea CRC a chunk-urilor (`EvtxRepair`) | chunk-uri cu CRC invalid | toate EVTX-urile parsate sunt întregi |
| AF03 | EVTX truncated | T1070.001 | antetul EVTX (numărul de chunk-uri, offset 42) și lungimea fișierului | fișierul nu se termină la granița unui chunk de 64 KiB sau antetul declară mai multe chunk-uri | idem |
| AF04 | RecordID gaps | T1070.001 | EventRecordID în fiecare fișier, în ordinea din fișier | lipsesc ID-uri în interiorul fișierului sau numerotarea revine (ID-uri folosite de două ori); se corelează cu System 6008 / Kernel-Power 41 | ID-uri continue și fără repetări |
| AF05 | timestamp manipulation (ora sistemului) | — | Security 4616, Kernel-General 1 | ora schimbată de alt proces decât svchost (W32Time), cu motivul 1 | System analizat |
| AF06 | Prefetch deletion | T1070.004 | `EnablePrefetcher` din hive-ul SYSTEM; jurnalul USN (export `fsutil`) | valoarea 0, sau fișiere `.pf` șterse în USN (cu numărul recreat ulterior și cel mai mare grup pe secundă) | jurnalul USN analizat nu conține ștergeri `.pf` (doar pentru fereastra lui) |
| AF07 | USN anomalies | T1070.004 | ID-ul jurnalului USN, citit ca FILETIME = momentul creării | exporturi cu ID-uri diferite, sau jurnal creat după cea mai veche înregistrare din jurnalele de evenimente (șters și recreat) | un singur jurnal, creat înainte |
| AF08 | file timestamp changes | T1070.006 | — | — | $MFT nu este parsat; „Basic info change” din USN acoperă și atributele, deci nu dovedește singur o schimbare de ore |
| AF09 | registry modifications | T1112 | Sysmon 12–14, Security 4657 | modificări pe cheile de jurnalizare/audit | există audit de registru și nu le arată |
| AF10 | log policy changes | T1562.002 | Security 4719, System 7040 (EventLog), `Start` al serviciului EventLog | audit eliminat (%%8448/%%8450), serviciul EventLog modificat sau dezactivat | niciodată: 4719 depinde de auditarea subcategoriei Audit Policy Change |
| AF11 | service deletion | T1070.009 | System 7045 față de serviciile din hive-ul SYSTEM | serviciu instalat care nu mai este configurat | toate serviciile instalate există |
| AF12 | task deletion | T1070.009 | Security 4699, TaskScheduler/Operational 141 | există evenimentul | TaskScheduler/Operational analizat |
| AF13 | process masquerading | T1036.005 | căile din Prefetch, BAM, ShimCache, Amcache, 4688, Sysmon 1 | proces de sistem în afara folderului lui | există căi de execuție |
| AF14 | ADS | T1564.004 | — | — | fluxurile alternative nu sunt colectate |
| AF15 | renamed executable | T1036.003 | Amcache `OriginalFileName` | `OriginalFileName` al unui utilitar de sistem sub alt nume | există intrări Amcache cu `OriginalFileName` |
| AF16 | firewall manipulation | T1562.004 | jurnalul Windows Firewall | 2059 (toate regulile șterse) sau regulă Allow pentru un program dintr-un folder scriabil de utilizator | jurnalul firewall analizat |

### Semnificațiile codurilor, verificate

Nu au fost presupuse; au fost citite din textele mesajelor furnizorilor (Windows 11, `wevtutil qe … /f:text`):

- Kernel-General 1, „Change Reason”: 1 = „An application or system component changed the time”, 2 = „System time synchronized
  with the hardware clock”, 3 = „System time adjusted to the new time zone”.
- Security 4719 `AuditPolicyChanges`: %%8448 = Success removed, %%8449 = Success added, %%8450 = Failure removed,
  %%8451 = Failure added.
- Firewall: 2097 = regulă adăugată, 2099 = modificată, 2052 = ștearsă, 2059 = toate regulile șterse, 2010 = profil de rețea
  schimbat.

### De ce „redenumit” înseamnă doar utilitarele de sistem

În Amcache-ul real, 579 din 3078 de intrări cu `OriginalFileName` au alt nume pe disc (actualizări, SDK-uri, `apphost.exe`
al proiectelor .NET). A trata orice diferență ca DETECTED ar fi fost zgomot. AF15 urmează tehnica ATT&CK T1036.003
(redenumirea utilitarelor de sistem), adică doar `OriginalFileName` din lista utilitarelor (cmd, powershell, certutil, rundll32…).

## Rezultatul pe corpusul NanAgent

Valorile de mai jos sunt recalculate de test cu `wevtutil`, iar AF11 este comparat cu lista WMI `Win32_Service` capturată pe stație.

| id | rezultat | ce arată |
|---|---|---|
| AF01 | DETECTED | 2026-02-25 22:10:08 UTC: jurnalele System, Application și ForwardedEvents golite de MARIUS-PC\Marius |
| AF02, AF03 | NOT_DETECTED | Security (103.631), System (31.420), Application, Firewall (864): structură intactă |
| AF04 | DETECTED | Application: după RecordID 44790 (2026-08-08 17:50:08 UTC) urmează 44760 (17:51:21); 31 de ID-uri sunt folosite de două ori. Coincide cu oprirea necurată din System (6008, Kernel-Power 41, repornire la 17:51:05). Security, System și Firewall au RecordID continuu |
| AF05 | DETECTED | 14 schimbări de oră făcute din Setări (SystemSettingsAdminFlows.exe, utilizatorul Marius), în 4616 și în Kernel-General 1 |
| AF06 | DETECTED (cu jurnalul USN) | **2026-10-03 13:54:27 UTC: 349 de fișiere `.pf` distincte șterse în aceeași secundă**, în ambele exporturi USN; 84 recreate ulterior cu același nume. USN nu arată procesul; în Prefetch, imediat după, rulează doar `svchost.exe` (13:54:28) și `gh.exe`. Prefetch rămâne activ (`EnablePrefetcher` = 3) |
| AF07 | NOT_DETECTED | jurnal unic `0x01dc84b0d77fa447`, creat la 2026-01-13 17:19:57 UTC, cu 1,5 h înainte de `InstallTime` din SOFTWARE (2026-01-13 18:50:53 UTC); fereastra exportului: 2026-10-03 13:19–20:51 UTC |
| AF09 | NOT_DETECTED | 14 evenimente 4657, niciunul pe cheile de jurnalizare |
| AF10 | DETECTED | 52 de eliminări din politica de audit (4719), făcute de utilizatorul Marius, începând cu 2026-08-08 |
| AF11 | DETECTED | 41 din 110 servicii instalate nu mai există (versiuni vechi ale Google Updater, drivere Lenovo etc.); niciunul nu apare în WMI |
| AF12 | UNDETERMINED | TaskScheduler/Operational nu este în corpus |
| AF13, AF15 | NOT_DETECTED | 4.955 de căi de execuție; 3.078 de intrări Amcache cu `OriginalFileName` |
| AF16 | DETECTED | 2026-09-19 15:17:02 UTC, în fereastra incidentului: „toate regulile au fost șterse” (Store Type 12) de `svchost.exe` |

Aceste rezultate descriu urme observate, nu atribuie intenția. De exemplu, AF16 este un eveniment generat de serviciul
firewall-ului. Legătura lui cu incidentul este doar temporală până la o analiză suplimentară.

## Limite

- $MFT și ADS nu sunt colectate și nu sunt parsate (AF08, AF14 rămân UNDETERMINED).
- USN se citește din exportul `fsutil usn readjournal C: csv`, nu din `$UsnJrnl:$J` brut. Jurnalul acoperă doar ultimele ore sau zile (32 MB). În corpus nu include ziua incidentului, 19.09.
- Ora din `fsutil` este locală. Conversia a fost verificată pe corpus față de orele de rulare din Prefetch: la cel puțin 50 de fișiere `.pf`, scrierea apare în USN în primul minut după rulare (UTC).
- Citirea ID-ului jurnalului ca FILETIME este verificată empiric pe această stație (data instalării), nu dintr-o specificație Microsoft.
- Testul de trunchiere folosește o copie a jurnalului System exportat local; corupția este acoperită de `EvtxRepairTests`.
