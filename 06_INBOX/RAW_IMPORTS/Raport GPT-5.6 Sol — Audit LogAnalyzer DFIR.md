# Audit DFIR/SOC — „LogAnalyzer DFIR Enterprise”

**Data:** 17 august 2026  
**Rol:** Principal Security Architect / Senior DFIR & Tier‑3 SOC  
**Constrângere:** 100% air-gapped; niciun apel extern în timpul analizei

## Rezumat executiv

Aplicația are o fundație solidă: EVTX și Registry, Sigma/YARA, timeline, process tree, ATT&CK, STIX/MISP și Clean Architecture. Totuși, este încă un **log analyzer cu funcții DFIR**, nu o platformă DFIR enterprise cu paritate KAPE/Velociraptor/Zimmerman/AXIOM. Diferența critică este dată de: (a) surse care supraviețuiesc ștergerii logurilor; (b) proveniență la nivel de record; (c) corelare multi-host identitate–proces–fișier–rețea; (d) raportare și export verificabile.

Hash-ul SHA‑256 la ingestie este necesar, dar nu constituie singur chain of custody. ISO/IEC 27037 acoperă identificarea, colectarea, achiziția și conservarea probelor digitale ([ISO](https://www.iso.org/standard/44381.html)); CISA cere ca fiecare probă să consemneze cum, când și de cine a fost obținută ([CISA Playbooks](https://www.cisa.gov/sites/default/files/publications/Federal_Government_Cybersecurity_Incident_and_Vulnerability_Response_Playbooks_508C_1.pdf)). Recomandarea centrală este un **Canonical Evidence Model + Evidence Graph**: fiecare parser publică obiecte normalizate, fiecare detector emite un finding explicabil, iar orice rezultat poate fi urmărit până la fișier, record/offset, parser și versiune. CASE/UCO oferă precedentul pentru reprezentarea obiectelor și a modului în care au fost procesate și interpretate ([CASE/UCO](https://pubmed.ncbi.nlm.nih.gov/31579279/)).

### Ordinea corectă de investiție

| Nivel | Rezultat obligatoriu |
|---|---|
| **P0** | Un caz Windows poate fi reconstruit chiar dacă EVTX a fost șters; fiecare constatare este reproductibilă; există raport NIS2 corect |
| **P1** | Scope multi-host, lateral movement, volatile/VSS, vizualizări investigative și exporturi SIEM/CASE |
| **P2** | UEBA offline, colaborare, rafinament vizual și maturitate de laborator |

## Principii transversale

Modelul canonic minim: `CaseId, EvidenceId, HostId, VolumeId, UserSid, ArtifactType, SourcePath, SourceHash, ParserId, ParserVersion, SourceRecordId/ByteOffset, EventTimeUtc, OriginalTime, TimeZoneBasis, TimeSemantics, TimePrecision, TimeConfidence, EntityRefs, FindingRefs`. `TimeSemantics` trebuie să distingă `Created`, `Modified`, `Recorded`, `LastExecution`, `FirstObserved`, `BatchFlushed` și `Inferred`. Microsoft avertizează că Amcache este în principal dovadă de prezență, Shimcache pe Windows 10+ nu dovedește execuția, iar Prefetch are altă valoare probatorie ([Microsoft IR Guide](https://cdn-dynmedia-1.microsoft.com/is/content/microsoftcorp/microsoft/final/en-us/microsoft-brand/documents/IR-Guidebook-Final.pdf)).

Orice `IArtifactParser` trebuie să ofere `Probe`, `ParseAsync`, `Validate`, capabilities, warnings de trunchiere/corupție, dependențe (`db` + `-wal` + `-shm`; hive + transaction logs) și metrici `seen/parsed/skipped/corrupt`. Absența unei surse înseamnă **necunoscut**, nu benign. Scorul de risc trebuie să afișeze contribuțiile, sursele și alternativele benigne; formula recomandată este `R=100×(1−Π(1−wᵢcᵢqᵢ))−penalizare_FP`, unde `cᵢ` este încrederea detectorului și `qᵢ` calitatea sursei.

---

# P0 — Critic / Esențial

## P0.1 — Evidence Vault, Provenance Ledger și chain of custody complet

1. **Numele Modulului / Funcționalității:** Evidence Vault & Reproducibility Ledger.
2. **Valoarea Tactică Forenzică:** demonstrează integritatea și traseul fiecărui obiect, permite reexecutarea analizei și separă clar proba originală, working copy și derivatul analitic. Elimină riscul ca un PDF să conțină concluzii imposibil de reprodus.
3. **Structura Tehnică / Datele Necesare:** manifest per evidence cu SHA‑256, mărime, timestamps, source URI/path, serial media/volume GUID, operator, metodă de achiziție, write-blocker, timezone/clock state; jurnal append-only hash-chained (`EntryHash=SHA256(PrevHash||CanonicalEntry)`); provenance record-level cu offset/row ID, parser+versiune+config, warning și transformări; verificare hash la deschidere/export; semnare offline a manifestului. Export CASE/UCO opțional.
4. **Modul de integrare în arhitectura existentă:** **Core:** `EvidenceItem`, `ForensicAction`, `ProvenanceRef`, `IntegrityStatus`; **Infrastructure:** encrypted object store, manifest writer, hash-chain verifier, SQLite audit tables; **UI:** Evidence Vault, custody timeline, badge „verificat/modificat/incomplet”, click până la bytes/XML original.

## P0.2 — NTFS Core: `$MFT`, `$UsnJrnl:$J`, `$LogFile` și ADS

1. **Numele Modulului / Funcționalității:** NTFS Forensic Engine.
2. **Valoarea Tactică Forenzică:** reconstruiește creare/rename/delete, urmărește drop-and-delete și identifică timestomping chiar când logurile lipsesc. MITRE arată că `$STANDARD_INFORMATION` și `$FILE_NAME` păstrează seturi distincte de timp relevante pentru T1070.006 ([MITRE Timestomp](https://attack.mitre.org/techniques/T1070/006/)).
3. **Structura Tehnică / Datele Necesare:** parsare FILE records/attributes/runlists, resident/non-resident, sequence numbers, hard links, ADS; USN v2/v3/v4 cu reason masks și join prin file-reference+sequence; redo/undo din `$LogFile`; comparație SI/FN, `BASIC_INFO_CHANGE` fără write legitim, file create→delete în fereastră scurtă, zeroed subsecond și MFT-number outliers. Păstrează deleted/unallocated records și confidence per path reconstruit.
4. **Modul de integrare în arhitectura existentă:** **Core:** `FileSystemEntity/Event`, `NtfsReference`, `TimestampAnomaly`; **Infrastructure:** parsere streaming și indexuri `VolumeId+Entry+Sequence`; **UI:** File Activity Explorer, dual SI/FN clock, USN reason filters, pivot către process/hash/YARA și timeline.

## P0.3 — Program Execution Evidence Pack

1. **Numele Modulului / Funcționalității:** Prefetch + Amcache + Shimcache + BAM/DAM + UserAssist.
2. **Valoarea Tactică Forenzică:** răspunde „ce a rulat, sub ce utilizator și cât de sigur știm?”, prin convergența surselor. Prefetch pe Windows 8+ poate păstra ultimele opt execuții, în timp ce Amcache/Shimcache nu trebuie etichetate automat drept execuție ([Microsoft IR Guide](https://cdn-dynmedia-1.microsoft.com/is/content/microsoftcorp/microsoft/final/en-us/microsoft-brand/documents/IR-Guidebook-Final.pdf)).
3. **Structura Tehnică / Datele Necesare:** `.pf` versiunile 17/23/26/30/31, run count, last-run slots, volumes și referenced files; `Amcache.hve` inventory/hash/path; SYSTEM `AppCompatCache`; BAM/DAM pe SID; NTUSER UserAssist ROT13. Motorul produce stări `Executed-Strong`, `Executed-Corroborated`, `Present-Only`, nu un boolean; join pe normalized path/hash/SID/time.
4. **Modul de integrare în arhitectura existentă:** **Core:** `ExecutionObservation` cu `EvidenceStrength`; **Infrastructure:** pluginuri version-aware; **UI:** Execution Matrix pe surse, contradicții și pivot către process tree/YARA/LOLBAS.

## P0.4 — User Activity Pack: LNK, Jump Lists, Shellbags, RecentDocs

1. **Numele Modulului / Funcționalității:** User Activity & Namespace Explorer.
2. **Valoarea Tactică Forenzică:** dovedește fișiere/foldere accesate, inclusiv pe volume USB ori shares indisponibile, și atribuie activitatea unui profil/SID.
3. **Structura Tehnică / Datele Necesare:** Shell Link header, TargetIDList, LinkInfo, StringData, ExtraData/Tracker; OLE CFB `automaticDestinations-ms`, custom streams și DestList; `UsrClass.dat` BagMRU/Bags și NodeSlot; NTUSER RecentDocs/OpenSavePidlMRU/LastVisitedPidlMRU; resolve volume serial, MAC/Droid IDs, UNC și AppID. Detectează shortcut către payload, share rar și LNK în Startup.
4. **Modul de integrare în arhitectura existentă:** **Core:** `UserActivity`, `NamespaceNode`, `ExternalVolumeRef`; **Infrastructure:** CFB/LNK/PIDL decoders; **UI:** MRU timeline, Shellbag tree, Jump List cards și click-to-pivot.

## P0.5 — Browser Forensics complet și recovery SQLite

1. **Numele Modulului / Funcționalității:** Chromium/Edge/Firefox Activity & Download Investigator.
2. **Valoarea Tactică Forenzică:** identifică phishing, download-ul inițial, redirect/referrer, căutări, extensii și exfiltration web. Chromium păstrează History/visits/download chains, iar Firefox folosește `places.sqlite`; sidecar-urile WAL pot conține activitate recentă necheckpointed ([ghid browser](https://www.sherlockforensics.com/pages/extract-browser-history-guide.html)).
3. **Structura Tehnică / Datele Necesare:** toate profilele; Chromium `History`, `Cookies`, `Login Data`, `Web Data`, `Favicons`, `Preferences`, `Extensions`, Cache/Code Cache/Service Worker/Local Storage/IndexedDB/Sessions; Firefox `places.sqlite`, `cookies.sqlite`, `formhistory.sqlite`, `logins.json`, `key4.db`, `sessionstore.jsonlz4`, `cache2`; parse main+WAL+journal+freelist, WebKit epoch 1601 și PRTime 1970. Nu afișa secrete; doar metadata/indicator de credential material, cu reveal controlat.
4. **Modul de integrare în arhitectura existentă:** **Core:** `WebVisit`, `Download`, `BrowserExtension`, `SessionArtifact`; **Infrastructure:** read-only SQLite snapshot/recovery, cache decoders; **UI:** Navigation Graph, Download Lineage și filtre profile/domain/time.

## P0.6 — SRUM/ESE și resource/network attribution

1. **Numele Modulului / Funcționalității:** SRUM Analyzer.
2. **Valoarea Tactică Forenzică:** atribuie consum de rețea și resurse unei aplicații/SID când process/network logs lipsesc; util pentru exfiltration, staging și execuție intermitentă. Microsoft subliniază batch timing-ul și retenția finită, deci timpul SRUM nu trebuie prezentat cu precizie falsă ([Microsoft IR Guide](https://www.microsoft.com/en-us/security/blog/2024/04/23/new-microsoft-incident-response-guide-helps-simplify-cyberthreat-investigations/)).
3. **Structura Tehnică / Datele Necesare:** `SRUDB.dat` ESE + `SOFTWARE` mappings; tables Network Connectivity/Usage, App Resource și Energy; AppId, UserId/SID, InterfaceLuid, bytes sent/received, foreground/background cycles. Heuristici: bytes outlier per app/user/hour, unsigned process with egress, activity while user absent; `TimeSemantics=BatchFlushed/Interval`.
4. **Modul de integrare în arhitectura existentă:** **Core:** `ResourceUsageInterval`; **Infrastructure:** ESE reader offline; **UI:** app×time heatmap, egress bars și pivot spre binary/Amcache/EVTX.

## P0.7 — Supertimeline cu semantică temporală și clock-quality

1. **Numele Modulului / Funcționalității:** Forensic Supertimeline 2.0.
2. **Valoarea Tactică Forenzică:** unifică probele fără a confunda momentul observat cu cel inferat și evidențiază contradicțiile. Plaso este modelul practic de extracție multi-artifact, iar Timesketch demonstrează valoarea analizei colaborative de timeline ([Timesketch](https://timesketch.org/)).
3. **Structura Tehnică / Datele Necesare:** normalizare UTC cu păstrarea raw time, timezone/DST din SYSTEM, skew estimat din evenimente sincronizate, precision/uncertainty interval, deduplicare fără pierderea provenance; host lanes; marker de log gap/rollover; `CorroborationCount`; export JSONL/Plaso-compatible. Causal links sunt ipoteze, nu fapte.
4. **Modul de integrare în arhitectura existentă:** **Core:** `TemporalObservation`, `TimeInterval`, `CausalHypothesis`; **Infrastructure:** merge sort streaming și temporal indexes; **UI:** lanes per host/source, uncertainty whiskers, „why this time?” și contradicții.

## P0.8 — Identity & Kerberos Detection Engine

1. **Numele Modulului / Funcționalității:** AD Authentication Correlator: Kerberoasting, AS‑REP Roasting, PtH/PtT.
2. **Valoarea Tactică Forenzică:** transformă evenimente izolate în secvențe de credential access și lateral movement. EID 4768 este TGT request, 4769 TGS request și ambele apar pe DC; Microsoft documentează câmpurile și limitările ([Microsoft 4768](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-4768), [Microsoft 4769](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-4769)).
3. **Structura Tehnică / Datele Necesare:** 4768/4769/4770/4771/4776, 4624/4625/4648/4672, Sysmon 10 și process logs; normalizează UPN/SID/IP/SPN/logon GUID. Kerberoast: 4769 RC4 `0x17`, burst distinct SPNs, service account rarity; AS‑REP: 4768 fără pre-auth pentru conturi anormale plus failed patterns; PtH: NTLM type 3, explicit creds și remote execution; allowlists și baseline per DC/account. MITRE recomandă pentru Kerberoasting RC4, volum neobișnuit și targeturi în afara baseline ([MITRE DET0157](https://attack.mitre.org/detectionstrategies/DET0157/)).
4. **Modul de integrare în arhitectura existentă:** **Core:** identity/session/ticket graph și stateful window rules; **Infrastructure:** partitionare pe domain/account/time; **UI:** Identity Investigation, SPN burst chart, source→account→service graph și explainable evidence.

## P0.9 — Process lineage, LOLBAS și masquerading multi-signal

1. **Numele Modulului / Funcționalității:** Windows Process Behavior Engine.
2. **Valoarea Tactică Forenzică:** detectează abuse de binaries legitime, proces redenumit și parent-child imposibil fără a alerta la simpla existență a `rundll32.exe`. Sysmon oferă process creation, network și file-time changes în logul Windows ([Microsoft Sysmon](https://learn.microsoft.com/en-us/sysinternals/downloads/sysmon)); catalogul LOLBAS furnizează funcții, paths și ATT&CK mappings ([LOLBAS](https://lolbas-project.github.io/)).
3. **Structura Tehnică / Datele Necesare:** 4688 + command line, Sysmon 1/3/7/10/11/13, PE OriginalFileName/Signer/hash, Prefetch/Amcache; reguli structurale: `svchost` parent≠`services.exe`, `lsass`/`winlogon` lineage invalid, Office/browser→script interpreter, service→user-writable binary, System32 image with wrong signer/path, LOLBin + suspicious switch/URL/network/child. Baseline OS build aware; normalize PID reuse prin ProcessGuid/start time.
4. **Modul de integrare în arhitectura existentă:** **Core:** process graph și declarative behavior predicates; **Infrastructure:** PE/AuthentiCode offline validation și signed LOLBAS pack; **UI:** process graph cu edge rationale, original filename, signer și rule contribution.

## P0.10 — Stateful Correlation Engine și coverage/gap awareness

1. **Numele Modulului / Funcționalității:** Offline Sequence & Evidence Fusion Engine.
2. **Valoarea Tactică Forenzică:** surprinde campanii care nu declanșează nicio regulă atomică și împiedică false reassurance când audit policy nu colectează sursa necesară.
3. **Structura Tehnică / Datele Necesare:** DSL pentru `followed_by`, `within`, `same_user/host/process/file`, threshold distinct/count, absence și cross-host; exemple: browser download→Office child→PowerShell→Run key; 4769 burst→4624 type 3→7045; process→pipe→SMB. Finding-ul conține DAG de probe, confidence și alternative benign explanation. Coverage matrix derivată din canale/IDs prezente, audit policy și perioada efectivă.
4. **Modul de integrare în arhitectura existentă:** **Core:** `SequenceRule`, `CorrelationWindow`, `CoverageRequirement`; **Infrastructure:** indexed temporal joins și rule-pack compiler; **UI:** Investigation Story Graph, „date lipsă” și simulare regulă pe caz.

## P0.11 — Parser Assurance, golden corpus și differential validation

1. **Numele Modulului / Funcționalității:** Forensic Parser Quality Gate.
2. **Valoarea Tactică Forenzică:** un parser greșit poate fabrica ori omite probe; validarea este o funcție de produs, nu doar QA intern. DEX susține proveniența transformărilor astfel încât rezultate din aceeași probă să poată fi reproduse și comparate ([U.S. OJP](https://www.ojp.gov/library/publications/dex-digital-evidence-provenance-supporting-reproducibility-and-comparison)).
3. **Structura Tehnică / Datele Necesare:** corpus curat/corupt/trunchiat pentru fiecare versiune Windows și artefact; ground truth la offset; property-based/fuzz tests; comparație cu minimum două implementări consacrate; regression snapshots; parser completeness metrics; fail-closed la format necunoscut; SBOM și licențe. Acceptance: zero silent skips și rezultate deterministe.
4. **Modul de integrare în arhitectura existentă:** **Core:** contracts și invariants; **Infrastructure:** test harness CLI izolat de UI; **UI:** Parser Health report per ingest, versiune și warnings exportabile.

## P0.12 — NIS2 Incident Reporting Workspace

1. **Numele Modulului / Funcționalității:** NIS2 24h/72h/1‑Month Reporting Pack.
2. **Valoarea Tactică Forenzică:** transformă probele în raport operațional în termenele legale și păstrează ce era cunoscut la fiecare moment. NIS2 cere early warning în 24h, notificare cu severitate/impact/IoCs în 72h și raport final în maximum o lună ([ENISA](https://www.enisa.europa.eu/topics/state-of-cybersecurity-in-the-eu/threats-and-incidents)).
3. **Structura Tehnică / Datele Necesare:** awareness timestamp imuabil; deadline engine; câmpuri pentru impact operațional/financiar, servicii/persoane, cauză probabilă, malicious/cross-border, IoCs, mitigations, status și third parties; snapshots versionate pentru 24h/72h/intermediar/final; marcaj `confirmed/provisional/unknown`; evidence links și redaction profile. Nu codifica praguri naționale ca universale: configuration pack pe jurisdicție.
4. **Modul de integrare în arhitectura existentă:** **Core:** `RegulatoryReport`, `DisclosureSnapshot`, `Deadline`; **Infrastructure:** template/version engine și PDF/JSON generator; **UI:** deadline board, completeness checks, approval/sign-off și diff între versiuni.

## P0.13 — Offline Supply Chain pentru reguli, parsere și knowledge packs

1. **Numele Modulului / Funcționalității:** Trusted Offline Update Bundle.
2. **Valoarea Tactică Forenzică:** păstrează produsul actual fără a încălca air gap-ul și previne introducerea unei reguli/parser compromise prin USB.
3. **Structura Tehnică / Datele Necesare:** bundle manifest versionat cu hashes, compatibilitate, SBOM, changelog, publisher, creation/expiry; semnătură Ed25519/X.509 verificată împotriva root keys pinned; dual control import, staging, test corpus, atomic activate/rollback; revocation package; separă executabil de data-only packs; export al versiunilor folosite în fiecare caz.
4. **Modul de integrare în arhitectura existentă:** **Core:** `TrustedPackage`, policy și compatibility constraints; **Infrastructure:** verifier și transactional installer; **UI:** Offline Update Center cu diff, semnătură, impact și rollback.

---

# P1 — Valoare Ridicată

## P1.1 — Memory, pagefile, hiberfil și crash-dump triage

1. **Numele Modulului / Funcționalității:** Volatile & Residual Memory Workbench.
2. **Valoarea Tactică Forenzică:** recuperează injected code, sockets, credentials indicators, named pipes și procese fileless care nu există pe disk.
3. **Structura Tehnică / Datele Necesare:** import raw/aff4/dmp, `hiberfil.sys`, `pagefile.sys`, `swapfile.sys`; offline bridge către Volatility 3 cu symbol packs preîncărcate și versionate; `pslist/psscan/pstree`, `netscan`, `dlllist/ldrmodules`, `malfind`, `handles`, `cmdline`, `svcscan`, `filescan`, YARA scan. Orice rezultat păstrează physical/virtual offset și plugin version; credential material masked implicit.
4. **Modul de integrare în arhitectura existentă:** **Core:** `MemoryObservation`; **Infrastructure:** sandboxed worker process cu limits; **UI:** Memory Triage, process-memory diff și pivot către disk process/hash.

## P1.2 — VSS, Restore Points, Recycle Bin și deleted-artifact recovery

1. **Numele Modulului / Funcționalității:** Historical Snapshot & Deletion Analyzer.
2. **Valoarea Tactică Forenzică:** recuperează versiuni anterioare ale hive-urilor, EVTX și bazelor browser și dovedește ștergerea/rollback-ul.
3. **Structura Tehnică / Datele Necesare:** enumerate VSS catalogs/snapshots, parse `$Recycle.Bin\SID\$I/$R`, System Restore metadata și orphan/deleted records; diff byte/schema între snapshot și current; hash fiecare derivat; nu montează read-write. Detectează ștergere în masă și artefact prezent doar istoric.
4. **Modul de integrare în arhitectura existentă:** **Core:** `ArtifactVersion`, `DeletionEvent`; **Infrastructure:** read-only VSS/E01 abstraction; **UI:** snapshot slider, before/after diff și restore-to-working-copy.

## P1.3 — Lateral Movement Correlator

1. **Numele Modulului / Funcționalității:** RDP/SMB/WinRM/WMI/PsExec Movement Engine.
2. **Valoarea Tactică Forenzică:** reconstruiește traseul între stații, contul și metoda, nu doar o listă de logon-uri.
3. **Structura Tehnică / Datele Necesare:** 4624/4625 types 3/10, 4648, 4672, 4778/4779; RDP LocalSessionManager/RemoteConnectionManager, TerminalServices; 5140/5145 shares; 7045/4697 service, 4698 task; WinRM Operational, WMI-Activity, PowerShell 4103/4104, Sysmon 3. Edge score combină source/destination, account, LogonId/GUID, protocol, time și remote-execution child (`wmiprvse`, `wsmprovhost`, service). Cercetarea MITRE asupra named pipes indică Sysmon 17/18 și Security 5145 ca surse relevante ([MITRE DC0048](https://attack.mitre.org/datacomponents/DC0048/)).
4. **Modul de integrare în arhitectura existentă:** **Core:** `HostIdentityGraph`, `MovementEdge`; **Infrastructure:** cross-case/host indexes; **UI:** Lateral Movement Graph cu time scrubber, edge evidence, direction/confidence și blast-radius.

## P1.4 — Named Pipe & IPC Abuse Detection

1. **Numele Modulului / Funcționalității:** IPC/Named Pipe Analyzer.
2. **Valoarea Tactică Forenzică:** detectează Cobalt Strike/SMB beacons, PsExec și impersonation chains, evitând greșeala „orice pipe rar este malign”. MITRE recomandă corelarea pipe creation/access cu parent-child anormal și injection context ([MITRE DET0493](https://attack.mitre.org/detectionstrategies/DET0493/)).
3. **Structura Tehnică / Datele Necesare:** Sysmon 17/18, 5145, memory handles/pipescan, process/signature; known-malicious patterns versionate, entropy/rarity, creator↔connector integrity level/user, remote SMB pipe, short lifetime și accompanying service/process. Allowlist per product/host role.
4. **Modul de integrare în arhitectura existentă:** **Core:** `IpcEndpoint`, `PipeInteraction`; **Infrastructure:** sequence rules și offline pattern pack; **UI:** pipe graph și detector explanation.

## P1.5 — Persistence Expanded

1. **Numele Modulului / Funcționalității:** Persistence Surface Mapper.
2. **Valoarea Tactică Forenzică:** extinde registry autoruns către scheduled tasks, services, WMI permanent subscriptions, startup/LNK, IFEO, AppInit, Winlogon, Office add-ins, COM hijack și BITS.
3. **Structura Tehnică / Datele Necesare:** task XML + TaskCache + 4698/4702, SYSTEM Services + 7045, WMI repository/events 5857–5861 și Sysmon 19–21, BITS qmgr databases, Startup folders, COM CLSID/InprocServer32, IFEO/SilentProcessExit. Calculează writable path, signer, missing target, path normalization și orphan mismatch între fișier/registry/event.
4. **Modul de integrare în arhitectura existentă:** **Core:** common `PersistenceMechanism`; **Infrastructure:** provider plugins; **UI:** persistence matrix `mechanism×user×host`, baseline diff și remediation preview.

## P1.6 — PowerShell, Script Host și deobfuscation pipeline

1. **Numele Modulului / Funcționalității:** Script Execution Analyzer.
2. **Valoarea Tactică Forenzică:** depășește pragul Shannon, care produce false positives și ratează obfuscation cu entropie mică; reconstruiește script blocks și lanțul de execuție.
3. **Structura Tehnică / Datele Necesare:** 4103/4104/400/403/600/800, AMSI/Defender dacă există, 4688/Sysmon 1, console history, transcripts; concat script-block IDs; decode Base64/UTF‑16, gzip/deflate, char arrays, replace/join și common PowerShell encodings în sandbox fără execuție; AST features, suspicious APIs/flags și parent/network correlation. Păstrează raw și fiecare transformare.
4. **Modul de integrare în arhitectura existentă:** **Core:** `ScriptArtifact`, `TransformStep`; **Infrastructure:** non-executing decoders/AST parser; **UI:** raw↔decoded diff, provenance și safe copy.

## P1.7 — MITRE ATT&CK Interactive Heatmap cu coverage, nu doar alerte

1. **Numele Modulului / Funcționalității:** ATT&CK Evidence & Coverage Matrix.
2. **Valoarea Tactică Forenzică:** separă „tehnică observată” de „tehnică detectabilă”; ajută analistul să găsească blind spots și probele aferente.
3. **Structura Tehnică / Datele Necesare:** pin ATT&CK version în offline pack; cell layers: findings count, max severity, confidence, host count, first/last seen, rule coverage, source availability și log gaps. Nu infera automat grup APT doar din tehnici comune; arată alternative și evidence strength.
4. **Modul de integrare în arhitectura existentă:** **Core:** `TechniqueObservation/Coverage`; **Infrastructure:** precomputed aggregates; **UI:** virtualized matrix, filters, time brush, click spre probe/reguli/hosts și export snapshot cu versiunea ATT&CK.

## P1.8 — Timeline Diff între două stații/snapshot-uri

1. **Numele Modulului / Funcționalității:** Differential Timeline & Baseline Comparator.
2. **Valoarea Tactică Forenzică:** scoate în evidență deviații sincronizate, propagation și schimbări produse de atac sau remediere.
3. **Structura Tehnică / Datele Necesare:** aliniere pe anchor events și corecție skew; canonical event fingerprint cu câmpuri ignorabile; seturi `only A`, `only B`, `changed`, `common`; fuzzy matching path/user/command; normalizează rolul hostului/OS build; confidence la match. Moduri host-vs-host, pre-vs-post și golden-image-vs-case.
4. **Modul de integrare în arhitectura existentă:** **Core:** `EventFingerprint`, `DiffSet`; **Infrastructure:** streaming merge și MinHash opțional; **UI:** side-by-side lanes, synchronized zoom și pivot bidirecțional.

## P1.9 — Sigma Transpiler cu validare de echivalență

1. **Numele Modulului / Funcționalității:** Sigma → SPL / Elastic / Sentinel KQL Export Studio.
2. **Valoarea Tactică Forenzică:** transformă investigațiile offline în detection-as-code reutilizabil în SOC, fără traduceri manuale fragile.
3. **Structura Tehnică / Datele Necesare:** nu implementa string replacement; folosește pySigma/backends/pipelines vendorizate și semnate. Backends convertesc condiția, pipelines mapează logsource/fields; documentația oficială listează backend-uri stabile Splunk și Kusto, plus SQLite ([SigmaHQ](https://sigmahq.io/docs/digging-deeper/backends)). Pentru fiecare target: schema profile, field mappings, unsupported feature diagnostics, escaping, query preview și fixtures pozitive/negative. Testează semantic aceeași regulă pe un corpus și raportează delta de rezultate.
4. **Modul de integrare în arhitectura existentă:** **Core:** intermediate rule AST și `ConversionDiagnostic`; **Infrastructure:** sandboxed pySigma worker sau port .NET verificat; **UI:** side-by-side Sigma/target, mapping editor, test results și export signed bundle.

## P1.10 — CASE/UCO și Plaso/JSONL interoperability

1. **Numele Modulului / Funcționalității:** Open Forensic Exchange Gateway.
2. **Valoarea Tactică Forenzică:** reduce vendor lock-in și permite verificarea rezultatelor cu alte unelte; STIX/MISP descriu CTI, nu suficient proveniența unei examinări.
3. **Structura Tehnică / Datele Necesare:** export/import CASE/UCO JSON-LD pentru objects/actions/provenance, Plaso-compatible JSONL/CSV, bodyfile și deterministic IDs; schema validation offline, round-trip tests și explicit loss report. CASE a fost proiectat tocmai pentru interoperabilitate și pentru a descrie handling/processing/interpretation ([CASE/UCO paper](https://pubmed.ncbi.nlm.nih.gov/31579279/)).
4. **Modul de integrare în arhitectura existentă:** **Core:** mappings Canonical↔CASE; **Infrastructure:** schema pack și serializers; **UI:** export wizard cu validation, field coverage și warnings de pierdere.

## P1.11 — NIST SP 800‑61r3 / CISA / ANSSI Playbook Workspace

1. **Numele Modulului / Funcționalității:** Standards-Aligned Incident Case Manager.
2. **Valoarea Tactică Forenzică:** conectează analiza la guvernanță, contain/eradicate/recover și lessons learned; NIST SP 800‑61r3 final din aprilie 2025 aliniază IR la CSF 2.0 și înlocuiește rev. 2 ([NIST](https://csrc.nist.gov/pubs/sp/800/61/r3/final)).
3. **Structura Tehnică / Datele Necesare:** workflow configurabil Govern/Identify/Protect context + Detect/Respond/Recover; CISA tasks pentru declare, scope, preserve, analyze, contain, eradicate, recover, communicate; RACI, approvals, timestamps, evidence links, decision rationale și after-action items. ANSSI/NIS2 sunt localizable packs, nu logică hard-coded; versiune standard în raport.
4. **Modul de integrare în arhitectura existentă:** **Core:** `PlaybookDefinition/Run/Task/Decision`; **Infrastructure:** signed YAML workflow packs; **UI:** case board, checklist, SLA/deadline și audit history.

## P1.12 — Offline CTI enrichment cu confidence și decay

1. **Numele Modulului / Funcționalității:** Local CTI Knowledge Base.
2. **Valoarea Tactică Forenzică:** corelează hash/domain/IP/certificate/toolmark fără internet și evită atribuirea excesivă.
3. **Structura Tehnică / Datele Necesare:** import STIX 2.1/MISP/CSV/TAXII-export bundles; source, TLP, confidence, valid_from/to, revoked, sightings și decay; exact/substring/CIDR/fuzzy certificate matches; conflict resolution. APT attribution se afișează `consistent with`, cu surse concurente și recency, niciodată ca verdict bazat doar pe ATT&CK overlap.
4. **Modul de integrare în arhitectura existentă:** **Core:** `IntelObject/Assertion`; **Infrastructure:** local indexed store și signed feeds; **UI:** enrichment panel, provenance/age/confidence și analyst accept/reject.

## P1.13 — Safe Containment Script Engineering

1. **Numele Modulului / Funcționalității:** Guardrailed Response Script Builder.
2. **Valoarea Tactică Forenzică:** păstrează valoarea playbook-urilor PowerShell fără a transforma analiza într-un generator de acțiuni distructive neverificate.
3. **Structura Tehnică / Datele Necesare:** scripts idempotente, `-WhatIf`, preconditions, explicit target binding, rollback, transcript/hash, minimum privilege, code signing și two-person approval pentru kill/isolation; avertizează că shutdown poate distruge volatile evidence; template separat acquire-before-contain. NIST recomandă playbook-uri acționabile, iar CISA pune colectarea/conservarea înaintea analizei și acțiunilor ulterioare ([NIST SP 800‑61r3 PDF](https://nvlpubs.nist.gov/nistpubs/specialpublications/nist.sp.800-61r3.pdf)).
4. **Modul de integrare în arhitectura existentă:** **Core:** `ResponseAction`, safety policy; **Infrastructure:** static PowerShell linter/signer; **UI:** plan preview, evidence-preservation gate, approvals și export-only (niciun remote execution).

---

# P2 — Rafinament / Nice-to-Have

## P2.1 — Explainable Offline UEBA

1. **Numele Modulului / Funcționalității:** Local Baseline & Peer-Group Anomaly Engine.
2. **Valoarea Tactică Forenzică:** găsește conturi/hosts atipice fără modele cloud și reduce regulile fixe de tip „01:00–05:00”.
3. **Structura Tehnică / Datele Necesare:** robuste z-score/MAD, empirical rarity și EWMA pentru ora logon, host count, distinct SPNs, parent-child, egress și admin shares; minimum sample size, host-role peer groups, concept drift și leave-one-incident-out. Afișează valoare, baseline, percentilă și features; nu antrena pe cazul curent implicit.
4. **Modul de integrare în arhitectura existentă:** **Core:** `BaselineProfile/AnomalyExplanation`; **Infrastructure:** deterministic local jobs; **UI:** distribution plots și suppress/label feedback auditat.

## P2.2 — Investigation Hypothesis Board

1. **Numele Modulului / Funcționalității:** Hypothesis, Competing Explanations & Negative Evidence Board.
2. **Valoarea Tactică Forenzică:** combate confirmation bias și separă faptele de inferențe, esențial mai ales la atribuirea APT.
3. **Structura Tehnică / Datele Necesare:** hypothesis, supporting/contradicting evidence, expected-but-absent source, alternative explanation, confidence history și reviewer. „Lipsă eveniment” este valid doar dacă coverage demonstrează că acel canal/interval a fost colectat.
4. **Modul de integrare în arhitectura existentă:** **Core:** `Hypothesis/EvidenceLink`; **Infrastructure:** versioned graph; **UI:** argument map și report-ready limitations.

## P2.3 — Attack Story Sankey și Kill-Chain confidence view

1. **Numele Modulului / Funcționalității:** Evidence-Weighted Attack Story.
2. **Valoarea Tactică Forenzică:** comprimă sute de evenimente într-o secvență inteligibilă pentru Tier‑3 și management fără a masca incertitudinea.
3. **Structura Tehnică / Datele Necesare:** nodes ATT&CK phase/host/account/process; edge width = evidence count, color = severity, opacity = confidence; causal edge requires rule and supporting timestamps; analyst override versionat. Click arată toate probele și alternativele.
4. **Modul de integrare în arhitectura existentă:** **Core:** view-model derivat din Evidence Graph; **Infrastructure:** preaggregates; **UI:** WPF virtualized Sankey/time scrubber, export SVG/HTML offline.

## P2.4 — Data Quality & Telemetry Gap Dashboard

1. **Numele Modulului / Funcționalității:** Forensic Readiness Scorecard.
2. **Valoarea Tactică Forenzică:** spune de ce o investigație nu poate răspunde unei întrebări și transformă gaps în recomandări de hardening/audit policy.
3. **Structura Tehnică / Datele Necesare:** expected artifacts by OS/role, present/absent/corrupt/truncated, EVTX first/last/record gaps, Sysmon configuration presence, audit subcategories, clock skew, retention and overwritten risk. Scorurile sunt pe întrebare („execuție”, „identity”, „network”), nu un procent opac unic.
4. **Modul de integrare în arhitectura existentă:** **Core:** `ReadinessRequirement`; **Infrastructure:** probes; **UI:** coverage radar/matrix și recommended collection pack.

## P2.5 — ISO/IEC 27037/27041/27042/27043 și laborator

1. **Numele Modulului / Funcționalității:** Digital Evidence Standards Compliance Pack.
2. **Valoarea Tactică Forenzică:** maturizează produsul pentru investigații disciplinare/judiciare și evaluări de laborator.
3. **Structura Tehnică / Datele Necesare:** 27037 handling/preservation; 27041 method suitability; 27042 analysis/interpretation; 27043 incident investigation; SOP references, competency/operator fields, method validation, peer review, uncertainty/limitations, equipment/tool version și retention/legal hold. Acesta este un mapping de control și evidence pack, nu o afirmație automată de certificare.
4. **Modul de integrare în arhitectura existentă:** **Core:** `ComplianceControl/Evidence`; **Infrastructure:** versioned mapping packs; **UI:** control matrix și auditor export.

## P2.6 — Collaborative review pe aceeași stație/mediu izolat

1. **Numele Modulului / Funcționalității:** Analyst Annotations, Bookmarks & Peer Review.
2. **Valoarea Tactică Forenzică:** oferă avantajele Timesketch de tagging/story/review fără serviciu cloud; Timesketch este explicit un instrument open-source de collaborative forensic timeline analysis ([Timesketch](https://timesketch.org/)).
3. **Structura Tehnică / Datele Necesare:** immutable annotation revisions, tags, bookmarks, saved searches, assignee, reviewer/sign-off, conflict handling și role-based access; annotations nu modifică event rows. Exportă author/time/revision în raport.
4. **Modul de integrare în arhitectura existentă:** **Core:** `Annotation/Review`; **Infrastructure:** SQLite WAL transactions și local RBAC; **UI:** comments, saved views, story builder și review queue.

## P2.7 — Portable acquisition recipes, fără agent obligatoriu

1. **Numele Modulului / Funcționalității:** KAPE/Velociraptor-Compatible Collection Recipe Builder.
2. **Valoarea Tactică Forenzică:** reduce situațiile în care analiza eșuează pentru că artefactul nu a fost colectat; Velociraptor documentează `Windows.KapeFiles.Targets` și colecții de preservare pentru `$MFT`, logs și memorie ([Velociraptor](https://docs.velociraptor.app/training/playbooks/preservation/)).
3. **Structura Tehnică / Datele Necesare:** import/export KAPE target YAML și Velociraptor artifact definitions unde licența permite; profiles Basic/Execution/Identity/Browser/Full; estimate size/volatility/privilege; generated manifest and hash commands; collect dependencies (hive logs, SQLite sidecars). Produsul nu execută remote și nu alterează air gap-ul.
4. **Modul de integrare în arhitectura existentă:** **Core:** `CollectionRecipe`; **Infrastructure:** parsers/generators și compatibility linter; **UI:** recipe wizard și „missing evidence” remediation.

---

## 3. Vizualizări SOC: reguli de proiectare

1. **Fiecare pixel trebuie să fie pivotabil la probă.** Heatmap-ul ATT&CK, lateral graph și Sankey trebuie să deschidă exact records, nu un tooltip fără lineage.
2. **Culoarea nu codifică două lucruri.** Hue=severity; opacity=confidence; hatch=coverage gap. Include text/icon, nu depinde exclusiv de culoare.
3. **Virtualizare obligatorie.** Nu crea WPF visual pentru fiecare din milioane de events/nodes; folosește tiling, level-of-detail, `IAsyncEnumerable`, cancellation și preaggregation.
4. **Time brushing sincronizat.** Aceeași fereastră temporală controlează timeline, process graph, movement graph și ATT&CK.
5. **No force-directed hairball by default.** Arată întâi suspicious subgraph, collapse benign infrastructure, degree caps și analyst-expand.
6. **Export reproductibil.** Orice imagine include case ID, filter/query, time range, versions și hash al datasetului derivat.

## 4. Măsuri de performanță și securitate

- Parsare în worker processes cu memory/time limits; fișierele ostile nu intră în procesul UI.
- SQLite: bulk ingest în tranzacții, prepared statements, covering/partial indexes; FTS separat; checkpoint controlat; encrypted case DB și evidence object store separat.
- Backpressure și checkpoint/resume pentru imagini mari; progres pe records/bytes, nu spinner.
- Canonical path handling pentru NTFS case-insensitivity, device paths, UNC, 8.3, ADS și Unicode confusables.
- XML/YAML/JSON parsers fără external entities, anchors bombs ori nesting nelimitat.
- Export HTML self-contained cu CSP strictă, encoding corect și fără CDN; PDF fără conținut activ.
- Licențierea HWID nu trebuie să blocheze accesul la probe în failure mode; oferă emergency read-only export și recovery escrow administrativ.
- Baza criptată trebuie să aibă KDF modern, salt per caz, key zeroization și separation între license key și evidence key.

## 5. Roadmap recomandat și criterii de acceptare

### Trimestrul 1 — „Forensic correctness”

P0.1, P0.2, P0.3, P0.7, P0.11, P0.13. Gate: golden corpus pentru fiecare parser; finding→offset demonstrabil; zero silent skip; same input+same versions→același output hash.

### Trimestrul 2 — „Windows case completeness”

P0.4–P0.6, P0.8–P0.10, P0.12. Gate: scenarii validate pentru phishing→download→execution→persistence, Kerberoast și log clearing; raport NIS2 snapshot 24h/72h/final.

### Trimestrul 3 — „Enterprise scope & interoperability”

P1.1–P1.6, P1.9–P1.12. Gate: campanie multi-host reconstruită cu confidence; Sigma round-trip tests; CASE/schema validation; memory worker cannot crash UI.

### Trimestrul 4 — „Analyst acceleration”

P1.7–P1.8, P1.13 și P2. Gate: 60 FPS la pan/zoom pe view agregat, time-to-first-result sub 30 secunde pe triage set, saved view reproductibil și peer-review auditabil.

### KPI care măsoară calitatea reală

- **Parser completeness:** parsed/(expected−known corrupt), per format/version.
- **Evidence traceability:** procent findings cu source hash + record/offset + parser version; țintă 100%.
- **Detection validation:** precision/recall pe scenarii ground truth, nu număr de reguli.
- **Time to first defensible finding**, nu doar ingest throughput.
- **Coverage transparency:** procent findings care declară required sources și procent gaps explicate.
- **Export validity:** STIX/MISP/CASE/Sigma schema pass rate 100%.
- **Reproducibility:** hash identic al normalized output în build-uri declarate compatibile.

## 6. Riscuri și decizii de produs

1. **„AI risk score” poate supra-vinde certitudinea.** Redenumiți-l „Scor explicabil de risc”; un model offline opțional nu are voie să genereze fapte sau atribuire fără evidence links.
2. **APT matching prin ATT&CK overlap este slab discriminatoriu.** Separă `observed behavior`, `CTI match` și `attribution hypothesis`; afișează alternative și intelligence recency.
3. **Generarea automată de containment poate distruge probe.** Export-only, `WhatIf`, approval și acquire-before-contain trebuie să fie default.
4. **Paritatea de funcții nu înseamnă paritatea probatorie.** Prioritatea este corectitudinea formatelor, warnings și reproducibilitatea, nu numărul de ecrane.
5. **Air gap-ul mută riscul în removable media/update chain.** Pachetele semnate, dual control, rollback și provenance de versiune sunt P0, nu operațiuni auxiliare.

## Concluzie

Versiunea următoare trebuie definită ca **platformă de examinare bazată pe probe**, nu ca un dashboard mai bogat. Cel mai mare salt de valoare vine din NTFS + execution/user/browser/SRUM, un model canonic cu provenance, corelarea stateful cross-host și raportarea NIS2 verificabilă. Abia după acestea, heatmap-urile, UEBA și narativa de atac devin state-of-the-art în sens real: nu doar spectaculoase, ci reproductibile, explicabile și apte să susțină decizii SOC, audit și proceduri judiciare.
