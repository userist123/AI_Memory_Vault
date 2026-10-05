# LogAnalyzer — Reality Audit (Faza 0)

Data auditului: 2026-10-05. Bază: `main` după #208 și #210, plus commit-ul local `5a0c3dfb9` (BAM/ShimCache/Amcache).
Metodă: s-a citit codul și s-au urmărit consumatorii în calea de producție (`grep` după tip/metodă, fără `tests/`).
S-a rulat și o scanare automată pe 283 de fișiere `.cs`. Un fișier cu nume potrivit **nu** înseamnă REAL.
REAL cere un consumator în producție și un test care verifică rezultatul real.

Legendă:
- **REAL** — rulează în aplicație, are test pe date reale sau sintetice cu rezultat verificat.
- **PARTIAL** — funcționează, dar lipsește o parte din contract (test, gap, proveniență, mod de eșec).
- **FACADE** — există cod, dar nu are consumator în aplicație sau nu face ce pretinde numele.
- **UNSAFE** — produce un rezultat fals, raportează succes fără acțiune sau modifică stația fără confirmare explicită.

Proiecte compilate (`LogAnalyzer.slnx`): App, Core, Infrastructure, Dfir.Core, Dfir.Windows, Dfir.Tests, UI.Tests, KeyGen, LicenseManager.
Fișierele de la rădăcina workspace-ului (`Views/`, `ViewModels/`, `Services/`, `App.xaml.cs`, `*.csproj`) **nu sunt compilate** în aplicație.
`Services/*.cs` de la rădăcină sunt legate doar în UI.Tests.

## 1. Integritatea probelor și cazul

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `Dfir.Core/Case/CaseWorkspace` (import, copiere, SHA-256, read-only, custodie CSV+JSONL, audit) | REAL | `ImportFile` copiază, nu mută; `RegisterStored` calculează SHA-256 și pune ReadOnly; teste în `Dfir.Tests/CoreTests.cs`. |
| Preflight al sursei înainte de parsare | ~~FACADE~~ → REAL (P0, acest branch) | Înainte: parserul primea calea fără nicio verificare. `EvidenceParserBase.Parse` verifica doar existența. Acum: `EvidencePreflight` în pipeline (vezi `EVIDENCE_MODEL.md`). |
| Detectarea modificării probei după achiziție | ~~FACADE~~ → REAL (P0) | Înainte: hash-ul din `evidence_index.jsonl` nu era recalculat niciodată. Acum: reverificat înainte și după parsare; la nepotrivire, `FAILED` + gap `EVIDENCE_MUTATED`. |
| Amprenta formatului sursei (magic bytes) | ~~FACADE~~ → REAL (P0) | Înainte: dispecerizare doar după extensie sau nume (`InvestigationPipeline.Import`). Acum: `EvidenceFingerprint.Detect`. |
| Registrul de parsere (P2: `ParserDescriptor`, `ParserRegistry`, selecție după conținut) | REAL | 6 parsere cu descriptor complet (OS, versiuni de format, limitări, status VALIDATED/TESTED din teste). Probele fără parser apar ca SKIPPED_BY_DESIGN, nu sunt ignorate. Inventarul se scrie în `Analysis/parsers.json`. `ParserRegistryTests`, `docs/dfir/PARSER_CONTRACT.md`. |
| Identitatea și versiunea parserului în rezultat | REAL | `ParseResult.Parser/ParserVersion` și `CaseWorkspace.RecordTransformation`. |
| Hash-ul sursei legat de rezultatele derivate (P1) | REAL | `ProvenanceBinder`: fiecare eveniment poartă `SourceSha256`/`ParserId`/`ParserVersion`, fiecare `EvidenceRef` poartă SHA-256. Constatările fără probă sunt respinse. Raportul reverifică probele (`ReportIntegrity`). Test pe corpus în `InvestigationTests` + `EvidenceContractTests`. |
| Locator per eveniment | REAL | `TimelineEvent.Locator` este completat de EVTX (RecordID), Prefetch, SRUM (rând), PCAPNG (cadru), BAM, ShimCache, Amcache. |
| `Dfir.Core/Model/Timestamp` (fără ora curentă ca fallback) | REAL | `Timestamp.Unknown()` este folosit când lipsește ora. Vezi ShimCache în `ExecutionArtifactParsers.cs:72`. |
| Statusuri SUCCESS/EMPTY/FAILED/NOT_AVAILABLE/PARTIAL/SKIPPED_BY_DESIGN | REAL | `Model/Enums.cs`. Prin `ParseResult.Finish()`, o excepție devine `FAILED`, nu `EMPTY`. |
| `Core/Services/ProvenanceLedgerService` (aplicația veche) | PARTIAL → corectat (P0) | Înainte: 2 × `catch {}`. Un registru necitibil era înlocuit pe tăcute cu un lanț nou la prima scriere. Acum: registrul necitibil rămâne neatins, intrările noi merg într-un fișier separat, iar `LoadError`/`LastSaveError` fac validarea să eșueze. Scrierea e atomică (tmp + move). `ProvenanceLedgerFailureTests`. |
| `Core/Services/DfirCasePackagingService` | PARTIAL | Are consumator. Un `catch {}` înghite erori. Pachetul nu conține hash-ul probelor originale, ci doar exporturile. |

## 2. Parsere

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `Dfir.Windows/Parsers/EvtxParser` + `Dfir.Core/IO/EvtxRepair` | REAL | Corpus: 170 jurnale, 348k evenimente în ~19 s. Un jurnal deteriorat recuperează 25.237 din 25.275 de evenimente, iar golul de RecordID e raportat. Teste: `EvtxRepairTests`, `CorpusRegressionTests`. |
| `Dfir.Windows/Parsers/PrefetchParser` (MAM/Xpress Huffman) | REAL | `CorpusRegressionTests` (prefetch, prefetchMsiexec). |
| `Dfir.Windows/Parsers/SrumNetworkParser` (ESE) | REAL | `CorpusRegressionTests` (srum). |
| `Dfir.Core/Network/PcapngParser` | REAL | `CorpusRegressionTests` (pcapng). |
| `SystemHiveExecutionParser` (BAM, ShimCache) | REAL | Validat pe hive-ul SYSTEM real din corpus, comparat cu `reg query`: 72 valori BAM, AppCompatCache identic octet cu octet. Validarea a găsit două defecte, corectate în acest branch: (1) aplicațiile UWP din BAM erau ignorate (31 din 72 de valori); (2) AppCompatCache era trunchiat de DiscUtils la 12 octeți (valoare big-data), deci ShimCache real avea 0 intrări (acum citit prin `RawRegistry`). Plus teste pe hive-uri sintetice. |
| `ScheduledTaskParser` + regula `PERSIST-TASK-CONFIG` (P3) | REAL | Definițiile de task (XML) devin evenimente per acțiune: comandă, argumente, utilizator, nivel, declanșatori, ascuns. Data fără fus orar rămâne neconvertită. Validat pe 303 fișiere reale contra `schtasks`. Regula de corelare semnalează taskurile care rulează din locații scriabile (inclusiv `%LOCALAPPDATA%` / `%TEMP%`). |
| `UserHiveParser` / `SoftwareHiveParser` + reguli `PERSIST-RUNKEY-USERPATH`, `PERSIST-IFEO-DEBUGGER`, `PERSIST-WINLOGON` (P3) | REAL | UserAssist (GUI: număr de rulări, timp de focus, ultima rulare), Run/RunOnce, Winlogon Shell/Userinit, IFEO Debugger. Validat pe NTUSER și SOFTWARE reale contra reg export din aceeași secundă. Citire prin `RawRegistry`, după ce validarea a arătat că DiscUtils nu redă fidel numele de valori malformate. |
| `ServicesParser` + regula `PERSIST-SERVICE-CONFIG` (P3) | REAL | Servicii și drivere din ControlSet-ul curent: ImagePath (neexpandat), ServiceDll, tip, mod de pornire, cont. Validat pe SYSTEM.hiv real contra WMI. Registrul rulează acum toți parserii care citesc o probă (SYSTEM → execuție + servicii). |
| `BrowserHistoryParser` + regula `DOWNLOAD-THEN-EXEC` (P3) | REAL | Vizite și descărcări Chromium (ore exacte, lanț de URL-uri, octeți, stare). Validat pe History real contra unei extracții independente. Regula leagă o descărcare de un program rulat din același folder în 6 ore; pe corpus reconstituie vectorul inițial al incidentului. |
| `LnkParser` (P3) | REAL | Ținta (locală / rețea), argumente, folder de lucru, orele și mărimea țintei, volumul (tip, serie, etichetă), mașina de origine (TrackerDataBlock). Validat contra shell-ului Windows pe linkurile reale. Lista de ID-uri shell și Jump Lists nu sunt încă decodate. |
| `AuditCoverage` + regula `FIREWALL-RULE-USERPATH` (P3) | REAL | Golurile de audit sunt deduse din conținutul jurnalului Security (NOT_AVAILABLE dacă o categorie lipsește, PARTIAL dacă apare în mai puțin de 10% din orele active) și intră în raport. Regulă nouă pentru reguli Allow de firewall către căi scriabile (coduri Action/Direction verificate pe evenimente reale). Validarea a găsit și corectat un bug în `IsUserWritable`: `%ProgramData%\…` producea `\\` și ocolea excluderea folderelor Microsoft. |
| `UsbDevicesParser` (P3) | REAL | USBSTOR, UAS și MountedDevices din SYSTEM, cu orele de primă instalare / ultimă conectare / ultimă deconectare. Validarea a arătat că varianta inițială (doar USBSTOR) rata unitățile UAS și dispozitivele șterse din Enum; ambele sunt acum raportate. |
| `JumpListParser` + `CompoundFile` (P3) | PARTIAL | Citire reală a Jump Lists automate: ora ultimei folosiri, număr de utilizări, fixare, mașină, linkul fiecărui element. Statusul e TESTED, nu VALIDATED: pe corpus se verifică doar consistența internă. Validarea a corectat cititorul CFB (ultimul sector parțial e valid) și a arătat streamul `DestListPropertyStore` din Windows 11. Jump Lists personalizate nu sunt citite. |
| `AmcacheParser` | REAL | Test pe corpus (`CorpusFact("amcache")`): peste 6.000 de intrări, 0 corupte, peste 90% cu SHA-1, toate cu oră. Plus test sintetic pentru SHA-1, cale, locator și semnificația „prezență”. Raportează gap dacă .LOG1/.LOG2 lipsesc. |
| `Infrastructure/Parsers/EvtxParser` (import vechi) | PARTIAL | Folosit de MainViewModel. Acum raportează înregistrările corupte și folosește `EvtxRepair`. Are 2 × `catch {}`. |
| `Infrastructure/Parsers/AntiForensicsArtifactsParser` | UNSAFE (FACADE ca consumator) | `:91 DateTime launchTime = DateTime.UtcNow;` — ora curentă folosită ca oră a probei. Marcare `ExecutionProven` ×3. Fără consumator în producție. |
| `Infrastructure/Parsers/{MftParser, UsbForensicsParser, BrowserForensicsParser, SrumParser, AmcacheShimcacheParser, LnkParserPlugin, PrefetchParserPlugin, ShimcacheParserPlugin, UserActivityParser, VolatilityBridgeParser, RdpBitmapCacheParser, CrossPlatformLogsParser, EvtxCarverEngine, M365EntraIdLogsParser, RegistryParser}` | FACADE | `grep -rl` nu găsește niciun consumator în App/Core/Dfir (doar teste sau niciunul). Conțin 16 × `ExecutionProven` și ~20 × `catch {}`. **Nu se construiește nimic peste ele** (regula producție-consumator). |
| `Core/Models/EvidenceEnums.ExecutionProven` | UNSAFE | Valoarea pretinde „execuție certă” pentru Prefetch/Amcache/BAM. Amcache/ShimCache dovedesc prezența, nu execuția. Folosită doar de parserele FACADE de mai sus. |

## 3. Timestamp-uri și erori înghițite

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `Infrastructure/Watchers/LiveEventLogWatcherService.cs:108` | UNSAFE → corectat (P0) | `TimeCreated = rec.TimeCreated ?? DateTime.UtcNow`: un eveniment fără oră primea ora curentă. |
| `Core/Services/SuperTimelineExportService.cs:72` | UNSAFE → corectat (P0) | `reg.LastWriteTime ?? DateTime.UtcNow` în cronologia exportată. |
| `App/ViewModels/MainViewModel.cs:379` | UNSAFE → corectat (P0) | `RawEventTimestamp = ... ?? DateTime.Now` pentru alertele live. |
| `Infrastructure/Parsers/AntiForensicsArtifactsParser.cs:91` | UNSAFE (FACADE) | Vezi secțiunea 2. Fără consumator, deci nu se modifică acum. |
| `DateTime.Now/UtcNow` în rest (297 de apariții în 87 de fișiere) | PARTIAL | Majoritatea sunt ora acțiunii (audit, custodie, nume de fișier, ferestre de filtrare). Acestea sunt legitime. Doar cele 4 de mai sus erau ore de probă. |
| `catch {}` în proiectele compilate (88 de apariții în 38 de fișiere) | PARTIAL | Cele mai multe sunt în cod FACADE (Infrastructure/Parsers), care nu e corectat (fără consumator). Calea reală, corectată în P0: `MainViewModel` (10 → 0; erorile de încărcare apar în bara de stare), `SystemDefenseExecutionService` (3 → 0), `ProvenanceLedgerService` (2 → 0). În `StationFacts`/`UserInvestigation`, erorile WMI/XML sunt acum păstrate (`BitLocker.SystemDrive.Error`, `_XmlError`). Rămân catch-uri tipizate și justificate (proces deja terminat la `Kill`). Rămâne `LiveEventLogWatcherService` ×1 (la oprire). |

## 4. Acțiuni asupra stației

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `Core/Services/AlertActionTriggerService.ExecuteContainmentScript` | UNSAFE → corectat (P0) | Întorcea `Success = true` fără să execute nimic: în AirGapped „[AIR-GAPPED PLAYBOOK SIMULATION] … validată”, iar în Network „Trimis semnal de izolare … PENDING AGENT ACK”, deși nu exista niciun agent. Apelat din `MainViewModel.TriggerContainmentPlaybook`. Acum raportează `NOT_EXECUTED` și comenzile sugerate. |
| `Core/Services/MediaSanitizationEngine` + certificatul | UNSAFE → corectat (P0) | Suprascrie fișierul ales fără confirmare distructivă. Certificatul avea câmpuri inventate: `HardwareSerialNumber = "HD-"+Guid`, `VerifierOperator = "Ofițer Securitate Informatică"`, `TamperEvidentAuditHash = Guid+Guid`, `IsVerifiedZeroized = true` fără citire de verificare. PDF-ul afișa hash-ul SHA-256 al șirului gol când lipsea hash-ul de audit. |
| `Dfir.Windows/Containment/*` (firewall per program, suspendare, scanare) | REAL | Confirmare explicită în UI. Izolarea automată e oprită implicit. Programele de încredere sunt identificate prin SHA-256. Teste: `ContainmentTests` (unul doar ca administrator). |
| `Infrastructure/Services/SystemDefenseExecutionService` (izolare stație, blocare IoC) | UNSAFE → corectat (P0) | Înainte: `RestoreNetworkAccess` și `BlockMaliciousIoC` raportau succes indiferent de codul de ieșire netsh. `BlockMaliciousIoC` bloca IP-ul fix 185.220.101.5 la țintă goală, iar ținta nevalidată permitea injectarea de argumente netsh. Șapte metode fără apelant raportau succes fără verificare (oprire procese după nume, `sc delete`, ștergere driver, RunAsPPL, powercfg, ștergere reguli). Acum: APPLY → VERIFY prin `netsh … show rule` (cod de ieșire, independent de limbă), statusuri VERIFIED/NOT_VERIFIED/FAILED/REJECTED, țintă validată ca IP/CIDR, nume de regulă determinist. Metodele fără apelant au fost eliminate. `DefenseActionVerificationTests`. |
| `Core/Services/Network/CyberAttackCountermeasureEngine` | PARTIAL | Atribuirea a fost eliminată în #208. Rămâne cod de contramăsură fără verificare post-aplicare. |
| `Core/Services/IncidentResponsePlaybookService` | PARTIAL | Scrie fișiere de playbook. Nu modifică stația. |

## 5. Rețea

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `Core/Services/Connectivity` (detectare pasivă NLM, fail-closed AirGapped, `NetworkPolicy`) | REAL | `OperatingModeTests`. NLM nu trimite trafic. |
| Servicii cu ieșire în rețea: `LiveThreatIntelService`, `M365LiveConnectorService`, `SiemForwarderService`, `AuditCollectionService` (UDP), `DirectoryCollector`, `UserInvestigation` | REAL (HTTP/UDP) / PARTIAL (LDAP, jurnale DC) | Toate apelează `NetworkPolicy.EnsureAllowed` înainte de I/O. `AirGappedNoNetworkTests` injectează un handler HTTP care numără cererile: 0 cereri în AirGapped pentru toate cele 5 apeluri HTTP. Testul de control confirmă că în Network cererea ajunge la handler. UDP e acoperit de `OperatingModeTests`. LDAP și jurnalele DC nu au încă un test de interceptare. |
| `Correlation.cs`, `ProcessScanner.cs`, `YaraRuleEngine.cs` | REAL (fără rețea) | Potrivirile „WebClient/HttpClient” sunt șiruri de detecție, nu apeluri. |

## 6. Detecție și atribuire

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `Dfir.Core/Analysis/Correlation` (15 reguli + `INCIDENT-CHAIN`) | REAL | Reconstruiește automat lanțul NanAgent din 19.09.2026 pe corpus (`InvestigationTests`). |
| `LiveStateAnalyzer` | REAL | Rulează pe fotografia live colectată. Proveniența e dată de `EvidenceRef`. |
| `Core/Services/AptAttributionEngine` | PARTIAL | Etichetat „suprapunere de tehnici — NU este atribuire” din #208. Încă are consumator în UI. |
| `Core/Services/{RemoteTriageService, DnsTunnelingClassifier, LivingOffTheCloudEngine, ProcessInjectionDetector, RansomwareDetectionEngine, SysmonCorrelationEngine}` | FACADE | Fără consumator în calea de producție. |
| `Core/Services/Network/LiveSecurityMonitoringEngine`, `StixMispExportService` | PARTIAL | Au consumator. Conțin încă texte sau valori demo (vezi scanarea „Demo”). |

## 7. Politici, conformitate, domeniu

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `Dfir.Windows/Audit/ControlEvaluator` (C/P/A/U/D/N/S) | REAL | Citire, fără modificare. CONFORM/NECONFORM/DE VERIFICAT/NEDETERMINAT cu probe. `ControlAuditTests`. |
| Motor de politici (`.lapolicy`, GPO, `Registry.pol`, OSCAL, ciclu DRAFT→RETIRED, APPLY/VERIFY) | FACADE (inexistent) | Nu există cod. Este planificat în P6–P8. |
| `Dfir.Windows/Domain/*` (LDAP DM01–DM31, jurnale DC, e-mail EM01–EM05) | PARTIAL | Testat doar pe date sintetice (`DomainAndMailTests`). Stația de dezvoltare nu e în domeniu. |

## 8. Teste și validare forensică

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `LogAnalyzer.UI.Tests` | REAL | 94/94 la ultima rulare (#210). |
| `LogAnalyzer.Dfir.Tests` | REAL | 53 trecute, 1 sărit (doar ca administrator). |
| Teste pe corpus (`CorpusFact`) | REAL | Starea `FORENSIC VALIDATION = AVAILABLE/PARTIAL/UNAVAILABLE` se scrie la fiecare rulare (`forensic_validation.txt`, rezumatul jobului CI). Cu `LADFIR_REQUIRE_CORPUS=1`, lipsa corpusului eșuează rularea (verificat: 9 eșecuri fără corpus). `docs/dfir/FORENSIC_TEST_LAB.md`. |
| Validare diferențială (alt instrument) | FACADE (inexistent) | Planificat în P12. |

## 9. Cod moștenit necompilat

| COMPONENT | REAL / PARTIAL / FACADE / UNSAFE | EVIDENCE |
|---|---|---|
| `ViewModels/`, `Views/`, `App.xaml.cs`, `*.csproj` de la rădăcina workspace-ului | FACADE | Nu sunt în `LogAnalyzer.slnx`. Conțin încă `AlertActionTriggerService`, 58 × `Now`, 24 × `catch {}`, 11 × demo. Ștergerea lor e decizia proprietarului. |

## Ordinea de remediere (P0, acest branch)

1. `AlertActionTriggerService`: întoarce `NOT_EXECUTED` și nu mai raportează succes fără acțiune.
2. Sanitizare:
   - confirmare distructivă explicită;
   - citire de verificare a zeroizării;
   - fără câmpuri inventate în certificat (gol = „nedeclarat”);
   - `IsVerifiedZeroized` numai după citire.
3. Cele 3 fallback-uri de timestamp din calea de producție.
4. `EvidencePreflight` (existență, mărime, SHA-256, amprentă) + detectarea modificării înainte și după parsare.
