# LogAnalyzer — Contractul parserelor

Starea la 2026-10-05 (P2). Cod: `LogAnalyzer.Dfir.Core/Parsing/IEvidenceParser.cs`, `ParserRegistry.cs`, `LogAnalyzer.Dfir.Windows/Parsers/WindowsParsers.cs`.

## Interfața

```csharp
public interface IEvidenceParser
{
    ParserDescriptor Descriptor { get; }
    PreflightResult Preflight(EvidenceItem item, string fullPath);
    ParseResult Parse(EvidenceItem item, string fullPath, IEventSink sink, CancellationToken ct);
}
```

`EvidenceParserBase` impune semantica erorilor (spec §5):
- excepție → `FAILED`, sau `PARTIAL` dacă s-au extras deja înregistrări;
- anulare → `PARTIAL`;
- zero înregistrări fără eroare → `EMPTY`;
- fișier lipsă → `NOT_AVAILABLE`.

`Preflight` = `EvidencePreflight.Check` cu formatele din descriptor (vezi `EVIDENCE_MODEL.md`).

## Descriptorul

| Câmp | Semnificație |
|---|---|
| `ParserId`, `Version` | identitatea, scrisă în fiecare eveniment, `ParseResult` și custodie |
| `Artifact` | ce citește, în cuvinte |
| `SourceTypes`, `FileNames` | ce probe îi revin după tipul declarat (`EventLog:*` = prefix) sau după nume |
| `Fingerprints` | formatele de conținut pe care le poate citi (`evtx`, `prefetch`, `prefetch_mam`, `ese`, `regf`, `pcapng`, `task_xml`, `sqlite`, `lnk`, `cfb`) |
| `SupportedOs`, `FormatVersions` | unde rulează și ce versiuni de format înțelege |
| `Limitations` | ce **nu** face; apare în `Analysis/parsers.json` al fiecărui caz |
| `Status` | `VALIDATED` (regresie pe corpus real) / `TESTED` (doar date sintetice, valori exacte) / `EXPERIMENTAL` (fără teste) |
| `Validation` | testele care susțin statusul |

Statusul vine din testele existente, nu din ce pretinde parserul.

## Selecția (`ParserRegistry.Select`)

1. **Candidații:** parserele al căror descriptor acceptă tipul sau numele probei.
2. **Fără candidat:** proba apare în `parsing.json` ca `SKIPPED_BY_DESIGN`, cu motivul („niciun parser înregistrat pentru tipul …”). Excepție: fotografiile live, care sunt citite de `LiveStateAnalyzer`. Nicio probă nu este ignorată în tăcere.
3. **Preflight** cu reuniunea formatelor candidaților. Dacă eșuează (lipsă, blocată, modificată, alt format), parsarea este refuzată: `FAILED` + gol + audit.
4. **Alegerea:** rulează **toți** parserii al căror descriptor conține formatul recunoscut din conținut. O sursă poate alimenta mai mulți parseri: un hive SYSTEM dă și artefacte de execuție (`SystemHiveExecutionParser`), și servicii (`ServicesParser`). Fiecare are propriul `ParseResult`, propria post-verificare și propria proveniență pe evenimente.
5. **Post-verificare:** după parsare, `parser.Preflight` încă o dată. Dacă sursa s-a schimbat, evenimentele ei sunt eliminate.

Registrul refuză doi parseri cu același `ParserId`.

## Parserele înregistrate

| ParserId | Artefact | Formate | Status | Validare |
|---|---|---|---|---|
| EvtxParser 1.0 | EVTX | EVTX 3.x | VALIDATED | corpus (Defender, Application), EvtxRepairTests |
| PrefetchParser 1.0 | Prefetch | SCCA v30/v31, MAM | VALIDATED | corpus (SETUP.EXE, MSIEXEC.EXE) |
| SrumNetworkParser 1.0 | SRUM Network Data Usage | ESE | VALIDATED | corpus (msbuild.exe, bytes exacți) |
| DefenderDetectionHistoryParser 1.0 | Istoricul detecțiilor Defender (DetectionHistory) | TLV binar | TESTED | înregistrare reală EICAR din laborator + teste de input ostil |
| NotepadTabStateParser 1.0 | Filele Notepad din Windows 11 (TabState) | „NP” + uLEB128 + UTF-16 + CRC32 | TESTED | două file reale din laborator (Notepad 11.2312.18.0, filă nesalvată și filă a unui fișier salvat) + teste de input ostil (uLEB128, lungimi, plafoane, UTF-16, FILETIME, CRC32, text ostil); WindowState neinterpretat |
| AnyDeskConnectionTraceParser 1.0 | Jurnalul de sesiuni AnyDesk (connection_trace.txt) | text: direcție, „AAAA-LL-ZZ, HH:MM”, metodă, ID la distanță, ID local | TESTED | mostre sintetice TEST (nu o instalare reală) + teste de input ostil (UTF-16, ANSI, linii uriașe, ID-uri și metode ostile, date imposibile și în viitor, rânduri false, plafoane, fișier blocat); ora locală se convertește doar cu fusul probei |
| AnyDeskTraceParser 1.0 | Jurnalul AnyDesk (ad.trace, ad_svc.trace): rândurile despre sesiuni | text: „nivel dată oră proces pid tid nr modul - mesaj” | TESTED | mostre sintetice TEST + teste de input ostil (IP-uri și ID-uri invalide, caractere ascunse și bloc Tag, linii tăiate la 2.048, plafon de 50.000 de rânduri relevante, 64 MiB, fișier blocat) |
| TeamViewerConnectionsParser 1.0 | Jurnalul de sesiuni TeamViewer (Connections_incoming.txt, Connections.txt) | text cu tab: ID, nume, început „zz-LL-AAAA HH:MM:SS”, sfârșit, cont, tip, GUID | TESTED | mostre sintetice TEST + teste de input ostil (nume și ID-uri ostile, interval invers, date imposibile și în viitor, tab lipsă, linii uriașe, plafoane, fișier blocat); TeamViewer15_Logfile.log nu este citit |
| WindowsSearchIndexParser 1.0 | Indexul Windows Search (Windows.db, Windows 11) | SQLite, tabelele SystemIndex_1_PropertyStore și _Metadata | TESTED | index real din laborator (Windows 11 26100, 1.171 intrări cu cale) + baze sintetice de input ostil (id-uri de coloană diferite, tipuri de valoare, FILETIME, plafoane, bază coruptă, alt Windows.db, SHA-256 al probei neschimbat); AutoSummary doar în câmpul sensibil |
| WindowsSearchGatherParser 1.0 | Baza gatherer Windows Search (Windows-gather.db, Windows 11) | SQLite, tabelele SystemIndex_Gthr și SystemIndex_GthrPth (colație UNICODE_en-US_LINGUISTIC_IGNORECASE înregistrată) | TESTED | baze sintetice cu schema reală de Windows 11 26100 (lanț de căi, cicluri, FILETIME ostil, nume de 5 MB, plafoane, bază coruptă, WAL al altei baze, SHA-256 al probei neschimbat); un rând poate rămâne după ștergerea fișierului; TransactionFlags neinterpretat |
| PcapngParser 1.0 | PCAPNG | PCAPNG 1.0 | VALIDATED | corpus (fluxuri, DNS, SNI) |
| SystemHiveExecutionParser 1.0 | BAM (inclusiv UWP), ShimCache | BAM Win10 1709+, ShimCache 10ts (inclusiv big-data) | VALIDATED | hive SYSTEM real vs `reg query`: 72 valori BAM, AppCompatCache identic octet cu octet |
| AmcacheParser 1.0 | Amcache InventoryApplicationFile | Win10/11 | VALIDATED | Amcache.hve real (>6000 intrări) + sintetic |
| UserHiveParser 1.0 | NTUSER: UserAssist, Run/RunOnce | UserAssist v5/v3 | VALIDATED | NTUSER real vs reg export: fiecare intrare UserAssist (număr de rulări, FILETIME), fiecare valoare Run/RunOnce |
| SoftwareHiveParser 1.0 | SOFTWARE: Run/RunOnce (+WOW6432Node), Winlogon, IFEO Debugger | — | VALIDATED | SOFTWARE real vs reg export (Run, RunOnce, WOW6432Node, Winlogon Shell/Userinit) |
| ServicesParser 1.0 | SYSTEM: servicii și drivere (ControlSet curent) | — | VALIDATED | SYSTEM.hiv real vs WMI Win32_Service: nume, StartMode, StartName, PathName pentru toate cele peste 300 de servicii (3 cu StartMode „Unknown” în WMI) |
| BrowserHistoryParser 1.0 | Istoric Chromium (Chrome, Edge): vizite, descărcări, lanț de URL-uri | schema History cu urls/visits/downloads | VALIDATED | History real (Default) vs extracția independentă din investigația manuală: 5 descărcări identice; toate cele peste 2000 de vizite din 18–20.09 (URL-urile lungi erau trunchiate în referință) |
| LnkParser 1.0 | Shortcut .lnk (MS-SHLLINK) | antet, LinkInfo, StringData, TrackerDataBlock | VALIDATED | 182 linkuri reale din Recent comparate cu shell-ul Windows (WScript.Shell): țintă, argumente, folder de lucru; shell-ul pierde caracterele non-ANSI („ș” → „?”), parserul păstrează calea Unicode |
| UsbDevicesParser 1.0 | SYSTEM: stocare USB (USBSTOR, UAS, MountedDevices) | Windows 8 – 11 | VALIDATED | SYSTEM.hiv real vs Partition/Diagnostic 1006: toate cele 5 dispozitive din jurnal găsite cu producător și model identice; fiecare oră de ultimă conectare din hive corespunde unui 1006 (±2 min) |
| JumpListParser 1.0 | Jump List automată (OLE CFB + DestList + linkuri) | DestList v3–v6 | **TESTED** | CFB sintetic cu valori exacte; pe 54 de fișiere reale doar consistență internă (1.880 de intrări, 1.555 de căi identice între DestList și linkuri). Nu există încă o referință independentă, deci nu e VALIDATED |
| FirefoxHistoryParser 1.0 | Istoric Firefox (places.sqlite): vizite, descărcări | Firefox 57+ | VALIDATED | places.sqlite real comparat cu modulul `sqlite3` din Python (altă implementare SQLite): fiecare vizită (oră brută, URL) și fiecare descărcare |
| ScheduledTaskParser 1.0 | Definiții de task (System32\Tasks XML) | schema 1.1–1.6 | VALIDATED | 303 fișiere reale comparate cu `schtasks /query /v` (comanda + argumentele, „Multiple actions”); XML sintetic |

Limitările complete sunt în descriptori și în `Analysis/parsers.json`.

## Defect găsit la validare: valori „big data” trunchiate

DiscUtils.Registry 0.16.13 nu citește celulele `db` (valori peste 16.344 octeți, format hive 1.4+). În loc de valoare întoarce cei 12 octeți ai antetului `db`, fără nicio eroare. Pe hive-ul real, AppCompatCache avea astfel 0 intrări în loc de toate.

`LogAnalyzer.Dfir.Core/IO/RawRegistry` citește o valoare urmând nk → lf/lh/li/ri → vk → db → segmente. Acum ShimCache se citește prin el, iar rezultatul e identic octet cu octet cu `reg query`.

Al doilea defect DiscUtils, găsit la validarea UserAssist: numele de valori stocate ca UTF-16 care nu formează text valid (unele programe scriu octeți ANSI acolo) nu sunt întoarse așa cum le arată regedit. Câteva intrări UserAssist reale nu puteau fi astfel puse în corespondență cu exportul. `RawRegistry` decodează numele exact după flag-ul din vk (Latin-1 pentru nume comprimate, unități UTF-16 neschimbate altfel) și întoarce `REG_EXPAND_SZ` neexpandat.

**Regulă:** parserele noi de registru citesc prin `RawRegistry.OpenKey` (subchei, valori brute cu tip, LastWriteTime), nu prin DiscUtils. Toate parserele de registru (SYSTEM, servicii, Amcache, NTUSER, SOFTWARE) citesc acum doar prin `RawRegistry`. DiscUtils.Registry a rămas doar în proiectul de teste, pentru a construi hive-uri sintetice; validarea pe corpus a rămas identică după mutare.

## Adăugarea unui parser

Parserul se adaugă doar cu:
- format real documentat;
- descriptor complet;
- teste cu valori exacte;
- test negativ (format greșit);
- test de corupție;
- corpus, când există.

Statusul pornește de la `EXPERIMENTAL` și urcă doar odată cu testele. Parserele vechi din `LogAnalyzer.Infrastructure/Parsers` nu sunt în registru (vezi `REALITY_AUDIT.md`, FACADE).
