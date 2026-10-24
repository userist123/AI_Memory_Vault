# Feature comparison: InspectraVeritas vs LogAnalyzer (2026-10-11)

Status: research, read-only. Evidence state: `DOCUMENT_VERIFIED` against UI strings only
(no binary was run, no code was read or copied). Personal names and organisation identifiers
found in the strings are replaced with [REDACTED].

## Method and limits

- Input: 4 364 UI/text strings extracted from the third-party Windows tool InspectraVeritas
  (version strings show 1.115.x; the owner reports the author agreed to a feature review).
  The strings are mostly Romanian labels, messages, report headings and changelog entries,
  plus noise (embedded PowerShell, SQL, regex, format strings).
- Every feature below is inferred from text the user would see. A string proves the feature
  is described in the UI; it does not prove it works or how well.
- LogAnalyzer status (HAVE / PARTIAL / MISSING) is judged against the capability list given by
  the owner for this review, plus a spot check of `LogAnalyzer.Dfir.Core/Profile/` for the
  transfer topic. "uncertain" marks a judgement that needs a code check before planning.
- Nothing from the tool's code, assets, logo, colours or copy is to be reused. Original strings
  are quoted below only as short evidence.

## Headline findings

1. InspectraVeritas is a **post-incident device/user forensics tool** (who plugged what, who
   used it, what happened to files). It has **no station compliance checks** (password policy,
   UAC, firewall, Defender, BitLocker, GPO) and **no registers** (media, users, transfers).
   LogAnalyzer is clearly ahead on guided station control, registers, GPO, NIS2 and
   plain-language explanation for non-specialists.
2. It is ahead of LogAnalyzer on **depth of artefacts** (deleted registry keys, $MFT deleted
   records, timestomping, ShellBags, MountPoints2, Timeline, thumbcache, WER, BITS, VSS as
   target, Windows.old), on **USB-to-user attribution with explicit ambiguity**, on the
   **"USB content found on disk by SHA-256"** search, and on **report reliability/integrity**
   (pre-report re-hash, report reliability section, digital signature + verifier, verifiable
   case archive, PII-masked export).
3. **Transfer register: none.** No string describes a transfer register, approvals, request
   numbers or transfer forms. Its closest feature is evidential: proving after the fact that a
   file on a USB stick has an identical copy on a disk. Details and a proposed design in section 4.
4. **Step reduction is a deliberate design theme** (one "collect everything" button, shared
   collection reused by every screen, single-UAC hive extraction, empty screens offering
   "Collect now", pre-report check that runs itself). Catalogue in section 5.

## 1. Feature inventory (with LogAnalyzer status)

Legend: HAVE / PARTIAL / MISSING (LogAnalyzer). Evidence = short original strings.

### 1.1 Collection and artefacts

| Feature | LA | Evidence |
|---|---|---|
| One button collects every source: main logs, archived logs, operational logs, per-profile artefacts | PARTIAL | "Colecteaza tot"; "Colecteaza toate sursele" |
| Archived event logs (Archive-*.evtx) read automatically and de-duplicated | PARTIAL (uncertain) | "citeste automat si ARHIVELE tuturor acestor jurnale (Archive-*.evtx)" |
| Long-history operational logs: Partition 1006, Kernel-PnP 400-430, DriverFrameworks, User Profile Service, TS-LocalSessionManager, Winlogon | PARTIAL (1006 HAVE) | "Jurnalele operationale (istoric lung)" |
| Offline hives opened with .LOG1/.LOG2 transaction logs applied on a working copy; dirty hive detection | PARTIAL (uncertain) | "jurnale(e) de tranzactii aplicate pe copia de lucru"; "hive murdar" |
| Deleted registry keys recovered from unallocated hive space, interest categories flagged (devices, mounts, accounts, autoruns, user history) | MISSING | "Chei sterse recuperate din registry"; "Ultima scriere inainte de stergere" |
| Raw NTFS read of locked/ACL-protected files (SeBackupPrivilege), no access-time change | MISSING | "citite brut din NTFS (fisiere blocate ...)" |
| $MFT deleted records: name, rebuilt path, $SI/$FN times, resident content (<700 B) saved | MISSING | "Fisiere sterse din $MFT"; "continut rezident salvat" |
| $LogFile partial reconstruction of create/delete/rename | MISSING | "$LogFile - operatii cu nume de fisier (reconstituire partiala)" |
| USN journal: daily summary, deletes/renames in profiles, journal-recreated-after-install flagged | PARTIAL (USN HAVE) | "Jurnal NTFS (fisiere)"; "Jurnal USN creat (dupa ID-ul jurnalului)" |
| Timestomping signal ($SI vs $FN, whole seconds, pre-1980, future) | MISSING | "Marcaje suspecte"; "posibilă antedatare a marcajelor (timestomping)" |
| ShellBags (folders viewed in Explorer, incl. USB and deleted folders) | MISSING | "Folder vizualizat (ShellBags)" |
| MountPoints2: which user mounted which volume (direct attribution) | MISSING (MountedDevices HAVE) | "MountPoints2 (montari per utilizator)" |
| UserAssist, BAM/DAM, ShimCache, Amcache (SHA-1), Prefetch, LNK, Jump Lists (incl. CustomDestinations) | HAVE | "Dovezi de executie (BAM/DAM, ShimCache, Prefetch, Amcache)" |
| RecentFileCache.bcf (Windows 7), MUICache | MISSING | "RecentFileCache.bcf (executii recente, Windows 7)"; "MUICache" |
| Recycle Bin $I with owner SID | MISSING (uncertain) | "Coș de reciclare ($I)" |
| Windows Timeline (ActivitiesCache.db), toast notifications (wpndatabase.db), clipboard entries | MISSING | "Windows Timeline (ActivitiesCache.db)"; "Notificari (wpndatabase.db)" |
| Thumbcache images extracted into the case folder | MISSING | "Miniaturi extrase (thumbcache)" |
| Windows Error Reporting, crash dumps, lsass dump flagged | MISSING | "Programe blocate si dump-uri (WER)" |
| IFEO Debugger / SilentProcessExit persistence ("sticky keys") | MISSING (uncertain) | "Redirectari la lansare (IFEO)" |
| Scheduled tasks XML vs TaskCache (hidden via missing SD / Index 0, encoded PowerShell) | PARTIAL | "valoarea SD lipseste din TaskCache\Tree" |
| Mark-of-the-Web (Zone.Identifier: source URL, referrer) | MISSING | "Fisiere descarcate (Zone.Identifier)" |
| BITS downloads (carving), certutil/bitsadmin flagged | MISSING | "BITS - descarcari (carving)" |
| Browsers: Chrome, Edge, Brave, Vivaldi, Opera, Firefox visits and downloads (no passwords/cookies) | PARTIAL (Chromium/Firefox HAVE) | "Browsere (Chrome / Edge / Brave / Opera / Firefox)" |
| Chat apps presence + URL/e-mail carving from cache (Teams, Slack, Discord; others presence only) | MISSING | "Aplicatii de chat - prezenta si urme din cache" |
| Virtualisation traces (WSL, Hyper-V, Docker, VirtualBox) | MISSING | "Virtualizare (WSL / Hyper-V / Docker)" |
| Known networks (NetworkList first/last connect, gateway MAC), Wi-Fi profiles | MISSING (uncertain) | "Retele cunoscute (NetworkList)"; "Profiluri Wi-Fi" |
| SRUM app resource usage per user/day (CPU, bytes read/written) and network per interface | PARTIAL (SRUM network HAVE) | "SRUM - aplicatii rulate si trafic de retea" |
| Optional URL/e-mail/IPv4 carving from pagefile/swapfile/hiberfil | MISSING | "Memorie virtuala - URL / e-mail / IPv4 (carving, fara moment)" |
| Volume Shadow Copy listing and a snapshot chosen as the analysis target | MISSING | "Snapshot-ul ales devine ținta analizei" |
| Windows.old: previous install SAM and Security.evtx; "Reset this PC" traces | MISSING | "Conturi din instalarea anterioara (Windows.old)"; "urmă de \"Reset this PC\"" |
| Target types: live system, external disk/mounted image, admin share \\SERVER\C$, VSS | PARTIAL | "Tinta poate fi un server din retea, prin share administrativ." |
| Preliminary source check: what exists, missing, unreadable, log coverage period; CSV into custody | PARTIAL (log size/coverage HAVE) | "VERIFICARE PRELIMINARĂ A SURSEI" |
| Triage extraction/backup set from live or external disk, manifest with SHA-256 and read method per file | MISSING (uncertain) | "Extragere si backup: set de triaj Windows ... cu manifest SHA-256" |
| RAM capture by launching the organisation's external tool, with free-space check, SHA-256, metadata, custody | MISSING | "Captura memoriei RAM (unealta externa)" |
| Parsers run in an isolated process; optional external plugin DLLs pinned by SHA-256 | MISSING | "surse parsate in proces izolat"; "plugin-uri externe optionale (DLL, hash SHA-256 in manifest)" |
| Local result cache keyed on source fingerprint (re-hash before reuse; sampled mode marked) | MISSING (uncertain) | "Cache local indexat (SQLite) pe amprenta fisierelor-sursa" |

### 1.2 Station audit checks (compliance/hardening)

| Feature | LA | Evidence |
|---|---|---|
| Policy, UAC, firewall, Defender, BitLocker, SMBv1, WDigest, password/lockout policy checks | HAVE (LA only) | none found in strings |
| "Sensitive actions" list: log clears, time change, audit policy change, Kerberos/domain policy change, service installed, privileged group changes | HAVE | "Actiuni sensibile (politica/ora/domeniu)" |
| Audit-policy-off explanation ("4663 missing because object access auditing was off") | HAVE (plain explanations) | "Cel mai probabil auditarea accesului la obiecte nu era activata" |
| LastAccessTime policy of the examining station reported (NtfsDisableLastAccessUpdate) | MISSING | "politica NtfsDisableLastAccessUpdate" |

### 1.3 Registers (media, users, transfers)

| Feature | LA | Evidence |
|---|---|---|
| Media register, users register, transfer register | LA ahead (media/users HAVE; transfer see sec. 4) | none found |
| Approved transfer channels as procedure data | HAVE (ProcedureProfile.TransferChannels) | none found |

### 1.4 USB and devices

| Feature | LA | Evidence |
|---|---|---|
| Device inventory all classes (USBSTOR, USB, WPD/MTP, SD, Bluetooth, HID, 1394) with 0064/0065/0066/0067 | HAVE | "Inventarul citeste toate categoriile de dispozitive" |
| Earliest install evidence = min(0065, 0064, setupapi incl. rotations and Windows.old) with source shown; "key recreated" warning; Windows-generated serial warning | PARTIAL | "Prima instalare (cea mai veche dovada)"; "serial generat de Windows" |
| Historical sessions reconstructed from 1006/Kernel-PnP/DriverFrameworks/Security/setupapi; approximate ends marked, not used for attribution | PARTIAL | "Sesiuni USB istorice reconstruite" |
| Serial matched as whole token (no substring false matches) | uncertain | "serialul unui USB se cauta ca TOKEN intreg" |
| "Everything about one device": all sessions, files touched per session, user/activity filters | PARTIAL | "Tot despre un dispozitiv" |
| USB-to-user attribution: direct (MountPoints2), inferred (logged-on session), AMBIGUOUS when several users | MISSING | "Corelare USB → utilizator"; "AMBIGUU" |
| "What followed the mount": executions, files, downloads from -10 min to +2 h, scored, other users excluded | MISSING | "Ce a urmat dupa montare (corelare)" |
| LNK tied to the stick on three levels: volume serial = boot-sector serial (strong), same letter (hint), nothing after disconnect | MISSING | "Dovada de volum"; "volum neverificat, indiciu" |
| **USB content on a disk**: find every USB file on a disk by SHA-256 regardless of name/folder; owner, profile, LNK/Jump List/Recycle/4663 references, involved accounts, strength Weak/Medium/Strong; PDF + CSV listing | MISSING | "Conținutul unui USB pe un disc"; "Listing: fișierele de pe USB găsite pe disc" |
| USB/folder listing with metadata and content search (txt/csv/docx/xlsx/pptx/odt, header/footer, whole words) | MISSING | "Listare si cautare pe USB/folder" |
| Security 6416/6419-6424: device recognised, activation, install blocked/allowed by policy | PARTIAL | "Security: instalare BLOCATA de politica de sistem" |
| .reg export import (USBSTOR) | MISSING (uncertain) | "Export registry USBSTOR (.reg)" |
| Test-lab orphan key deletion (USB storage only, typed confirmation) | not wanted | "Sectiunea rosie sterge DEFINITIV intrari din registry" |

### 1.5 Network

| Feature | LA | Evidence |
|---|---|---|
| SRUM traffic per app/user/day, large single-app upload flagged | PARTIAL | "volum mare trimis in retea de o singura aplicatie intr-o zi" |
| Known networks, VPN/WWAN/Wi-Fi type, hidden SSID | MISSING | "retea ascunsa (SSID nedifuzat)" |
| Downloads from non-Microsoft BITS domains, PowerShell downloads flagged | MISSING | "descarcare BITS de pe un domeniu care nu apartine Microsoft" |
| pcap, live connections, domain/mail investigation | HAVE (LA only) | none found |

### 1.6 Users and accounts

| Feature | LA | Evidence |
|---|---|---|
| Account reconstruction from SAM + ProfileList + logs; states Confirmed/Probable/Possible/Undetermined with alternative explanations; "no SAM = no verdict" | PARTIAL | "Probabil sters sau orfan"; "Nedeterminat - SAM necitit" |
| Domain vs local split using machine SID from SAM; SIDs resolved from target evidence, not the examiner's registry | uncertain | "SID-urile ... traduse ... folosind probele TINTEI" |
| Account lifecycle events (create, delete 4726, rename, disable, password reset by other account, group add) | HAVE | "resetare facuta de alt cont decat cel afectat" |
| Logon sessions paired with duration; unpaired events listed; logon types explained (2,3,4/5,7,10,11) | PARTIAL | "Sesiuni (logon imperecheat cu logoff)"; "10 - RDP: conectare la distanta." |
| Night activity 22:00-06:00 marked | MISSING (uncertain) | "Logon-uri nocturne (22:00-06:00)" |
| Per-user triage score 0-100 with reasons always shown | PARTIAL (risk factors) | "Scorul este un ghid de TRIERE" |

### 1.7 Timeline

| Feature | LA | Evidence |
|---|---|---|
| Unified timeline with groups (execution, files, devices, accounts, persistence, network, logs/time) | HAVE | "Cronologia completa" |
| Side panel per row: type, what it proves / does NOT prove, provenance | PARTIAL ("Ce înseamnă") | "Ce dovedesc și ce NU dovedesc sursele folosite" |
| "Timeline around this time (±15 min)" one-click filter | MISSING (uncertain) | "Cronologia in jurul acestei ore (±15 min)" |
| Interactive histogram / day×hour heat map, wheel zoom, drag to select range | MISSING (uncertain) | "Rotita = zoom; trage = selecteaza intervalul" |
| All times in target local time (from TimeZoneInformation); DST repeated hour marked ambiguous; CSV in UTC | PARTIAL (uncertain) | "ora de iarnă – oră repetată" |
| Improbable timestamps (pre-1995, future) counted as a warning | PARTIAL | "evenimente au marcaje improbabile" |

### 1.8 System history

| Feature | LA | Evidence |
|---|---|---|
| OS install date and registered owner, upgrades chain, Windows.old, reset | PARTIAL | "Instalare, upgrade-uri si resetari" |
| KB updates from CBS + Setup.evtx + WindowsUpdateClient, de-duplicated, failed and uninstalled flagged | MISSING | "Update-uri si hotfix-uri" |
| Boots/shutdowns with initiator, unexpected shutdowns, bugcheck | MISSING (uncertain) | "Porniri, opriri si incidente" |
| Installed/uninstalled software (MsiInstaller with user SID) | HAVE | "Software instalat / dezinstalat" |

### 1.9 Detection / IoC

| Feature | LA | Evidence |
|---|---|---|
| Hash lists (MD5/SHA-1/SHA-256) against executables still present; optional external YARA; lists and rules pinned in manifest | MISSING | "Potriviri IoC (hash / YARA) - de verificat, nu verdicte" |
| LOLBin patterns (mshta, regsvr32 scriptlet, rundll32 javascript, Base64 in command line) | HAVE (Sigma-like) | "regsvr32 cu scriptlet de la distanta" |

### 1.10 Reports and exports (incl. signing/encryption)

| Feature | LA | Evidence |
|---|---|---|
| Report types: per user, per device, system history, USB-on-disk listing | PARTIAL (per audience) | "Raport PDF per dispozitiv" |
| "Report reliability" section right after summary: partial sources, parser defects, changed hashes, ambiguous attributions, clock changes, cleared logs | MISSING | "Fiabilitatea raportului" |
| "Limits of this report, before its content" / "What is not visible and why" | PARTIAL | "Limitele raportului, inainte de continutul lui." |
| Finding model: observation / inference / conclusion; single confidence scale; correlations never CONFIRMED; partial source caps at AMBIGUOUS | PARTIAL | "corelările nu ajung niciodată la CONFIRMAT" |
| PDF with linked table of contents, page numbers, vector chart; graceful fallback to simple layout | PARTIAL | "Titlurile sunt legaturi catre sectiuni." |
| Report restricted to a period (whole collection / timeline filter / other dates), warning band, export recorded with period | MISSING | "Raport PDF pe o perioada" |
| Analysis footprint on the target (what the tool read, raw copies, processes started, access-time policy) | MISSING | "Amprenta analizei pe tinta" |
| Digital signature RSA/ECDSA with organisation certificate (.sig.json) and a verifier ("signature valid") | MISSING | "SEMNATURA VALIDA - fisierul nu a fost modificat de la semnare." |
| PII-masked export (consistent pseudonyms for users/hosts, e-mail, phone, IP, URL parts, SIDs) | MISSING | "EXPORT CU DATE PERSONALE MASCATE" |
| NDJSON (ECS) for SIEM | MISSING | "Export NDJSON (ECS) pentru SIEM" |
| STIX 2.1 indicators | HAVE | "Export STIX 2.1 (indicatori de verificat)" |
| CSV ';' UTF-8 BOM, formula injection escaped, warns when a filter is active | uncertain | "formulele potentiale sunt prefixate cu apostrof" |
| Encryption of reports/archives | MISSING in both (archive explicitly unencrypted) | "arhivă de caz (backup fișiere audit, necriptată)" |

### 1.11 Case and evidence handling

| Feature | LA | Evidence |
|---|---|---|
| Case ID, Acquisition ID, Examiner, case notes; case auto-initialised at start | HAVE (case mgmt) | "Caz initializat automat la pornirea sesiunii" |
| Custody log JSONL with chained PrevHash; broken chain detected at line N | HAVE (sealed audit chain) | "Lant rupt la linia" |
| Evidence registered with SHA-256; unstable hash (file changing) detected; baseline kept | PARTIAL | "EVIDENCE REGISTERED (UNSTABLE HASH)" |
| Re-verify all evidence hashes before generating a report; reservations shown, then "Generate?" | MISSING | "Verificarea dinainte de raport: re-verific hash-urile probelor..." |
| Verifiable case archive ZIP: manifest.csv + manifest.sha256 + archive .sha256, .partial until done, auto-verify, detects modified/missing/added/duplicate entries; destination refused on examined volume | MISSING | "Arhivă creată și verificată"; "Verifică o arhivă" |
| Manual verification commands printed for use without the tool | MISSING | "VERIFICARE FĂRĂ INSPECTRA VERITAS" |
| Changing target discards loaded evidence on purpose (no cross-system mix) | uncertain | "Schimbarea tintei arunca tot ce a fost incarcat" |
| Confirmation that inventory and logs come from the same Windows system | MISSING | "Confirmare consistenta surse" |
| Parser validation against reference tools (same records, same times, TZ offset detection) | MISSING | "COMPARAȚIE ... ↔ instrumente Eric Zimmerman" |
| Parser audit separates unreadable evidence from parser code defect | PARTIAL (uncertain) | "DEFECT PARSER" |
| Read-only preview (text/hex); executables never opened; associated-app open needs confirmation | MISSING | "Previzualizare hex (doar citire)" |
| Legal note: internal custody does not replace legal hand-over documentation | HAVE (legal research doc) | "nu inlocuieste documentatia procedurala/legala de predare-primire" |

### 1.12 Workflow and UX

| Feature | LA | Evidence |
|---|---|---|
| Menu by investigation step: INVESTIGATION (Case & source, Triage, Timeline), ENTITIES (USB, Users, File activity), TOOLS | PARTIAL | "Meniul din stanga urmeaza pasii investigatiei" |
| System history sidebar organised as questions ("What deserves checking", "Who had access", "What ran", "What was mounted", "What can sources prove") | PARTIAL | "bara laterala organizata pe intrebari" |
| Triage screen: everything worth attention, ordered, "Open screen" to the full proof | PARTIAL | "Triaj - ce e important dupa colectare." |
| Source-status badge on every screen header (Available / Partial / Read error / Unavailable / Not analysed) with per-source tooltip | PARTIAL | "arata in antet starea surselor lui" |
| Collapsible detail chapters identical to the report chapters | uncertain | "capitole pliabile, aceleasi ca in raport" |
| Progress in status bar with Stop; cancel keeps previous collection; no partial report | PARTIAL | "Opreste colectarea" |
| Filters: zone, user, text (debounced), day range, Reset, "X of Y rows" | PARTIAL | "un sumar 'X din Y randuri'" |
| Tooltip on every menu entry; keyboard focus outline | uncertain | "Fiecare intrare are o explicatie la trecerea mouse-ului." |
| Built-in manual ordered by workflow, version history, About with libraries | HAVE (user guide) | "Manualul e organizat pe flux" |

### 1.13 Settings, editions, licensing

| Feature | LA | Evidence |
|---|---|---|
| RO/EN switch applied instantly and persisted; theme follows Windows | PARTIAL (Simple/Expert language) | "Limba interfetei: romana sau engleza" |
| Settings: full-hash threshold vs sampling, IoC folder, RAM tool, signing certificate, plugins | PARTIAL | "Setari > Achizitie memorie" |
| "No-AI" edition: nothing leaves the machine, no AI-generated statement in reports | HAVE (offline) | "Editia fara AI" |
| Reproducible build, SBOM CycloneDX, NuGet vulnerability gate, UI end-to-end tests, parser fuzzing, real-case corpus required for release | PARTIAL | "SBOM CycloneDX si VALIDARE.txt in kit" |
| Licence: free for personal and internal organisational use, no resale or reverse engineering | n/a | "gratuit pentru uz personal si intern in organizatii" |

## 2. What LogAnalyzer has that InspectraVeritas does not

Guided station control with per-check and per-code plain explanation; controller manual checks
(seals, BIOS, documents); GPO import/validate/approve/plan/apply; media and users registers with
auto-fill; procedure profile (zones, approved transfer channels); NIS2 draft; MISP export; process
containment; domain/mail investigation; pcap; live WMI device alerts with register verdicts;
Windows device protections; glossary; Simple/Expert language. These remain the differentiator
for non-specialist controllers.

## 3. Gaps worth adding (ranked)

Priority P1 = high value for the owner's use case (controllers, classified stations, transfers);
effort S (< 2 days), M (< 1 week), L (> 1 week).

| # | Feature | P | Effort | Why |
|---|---|---|---|---|
| 1 | Transfer register (section 4), linked to media register and to evidence | P1 | M | Owner priority; nothing comparable exists in either tool |
| 2 | USB content found on disk by SHA-256 (verify a declared transfer or find an undeclared one) | P1 | M | Turns the transfer register from paperwork into evidence |
| 3 | "Collect everything" single button + shared collection reused by all screens | P1 | M | Biggest click reduction (section 5) |
| 4 | Pre-report evidence re-hash + "Report reliability" section | P1 | S | Cheap, strong legal value; fits existing SHA-256 chain |
| 5 | MountPoints2 direct user attribution + AMBIGUOUS state for USB sessions | P1 | M | Answers "who used the stick" honestly |
| 6 | Signed report (.sig.json, org certificate) + built-in verifier | P1 | M | Report integrity outside the app; LA already has the chain |
| 7 | "What followed the mount" (-10 min/+2 h) correlation per device | P2 | M | Directly useful in exfiltration checks |
| 8 | Verifiable case archive (manifest.csv/.sha256, auto-verify, refuse examined volume) | P2 | S | Hand-over between controllers; reuse chain-of-custody code |
| 9 | Report restricted to a period, with warning band | P2 | S | Controllers report per control period |
| 10 | ShellBags, Zone.Identifier (MotW), Recycle Bin $I | P2 | M | Low-noise user-activity evidence |
| 11 | Timestomping signal ($SI vs $FN) and $MFT deleted records | P2 | L | Anti-forensics coverage; needs raw NTFS |
| 12 | Deleted registry keys from hive slack (device/mount/autorun categories) | P2 | L | Detects USB trace cleaning |
| 13 | KB updates history + boots/shutdowns with initiator | P2 | M | Station-control evidence (patch level, unexpected shutdowns) |
| 14 | PII-masked export with consistent pseudonyms | P2 | M | Sharing with CSIRT without personal data |
| 15 | Hash-list IoC matching (optional external YARA) | P3 | M | Sigma-like rules already cover behaviour |
| 16 | VSS listing / snapshot as target; Windows.old accounts | P3 | L | Specialist use |
| 17 | Timeline, notifications, thumbcache, WER, BITS, chat/virtualisation traces | P3 | L | Breadth; low priority for controllers |
| 18 | NDJSON (ECS) SIEM export | P3 | S | Integration only |
| 19 | Parser validation script against reference tools | P3 | S | Release evidence |

## 4. Transfer register (owner priority)

### 4.1 What the tool has

- **No transfer register.** No strings for requests, approvals, approvers, transfer numbers,
  forms, classification levels, destination authorisation or transfer reports.
- **Adjacent evidential feature, "Conținutul unui USB pe un disc"** (USB content on a disk):
  - inputs: source (USB root or folder), disk/root to search, optional extra folders;
    options: estimate duration, name-only matches, list not-found files, size thresholds;
  - per file found: USB relative path, disk path, size, SHA-256 on USB and on disk with the
    computation time, created/modified/accessed on disk and USB (UTC and target time),
    $MFT record and $SI/$FN times, USN creation time, NTFS owner, profile containing the copy,
    referencing artefacts (LNK, Jump Lists, Recycle Bin, Security 4663), involved accounts,
    strength Weak/Medium/Strong, stability column (file changed during analysis);
  - outcome states: identical copy, same name different content, not found in examined zone,
    UNVERIFIED (read error or incomplete comparison), INCONCLUSIVE;
  - honest interpretation text: identical SHA-256 proves same content, not direction, time or person;
    creation-after-modification "compatible with a copy to disk at creation date";
  - output: PDF + CSV listing with evidence manifest and custody chain at the end.
- **Identity fields usable for numbering**: Case ID, Acquisition ID, Examiner, Operator,
  case notes, generated time, target; every export is recorded in custody with its hash.
- LogAnalyzer already has `ProcedureProfile.TransferChannels` (Name, FromZone, ToZone, Medium,
  Note) as approved channels, but no per-transfer entry.

### 4.2 Proposed transfer register for LogAnalyzer (recommendation, not found in the tool)

Fields per entry:
- Number: `T-<year>-<seq>` per station, gap-free, never reused; cancelled entries kept and marked.
- Date/time of request, of approval, of execution (target local time + UTC).
- Direction and channel: from zone / to zone, chosen from `TransferChannels` (blocks unapproved channel).
- Medium: picked from the media register (serial, register number, owner); auto-filled when the
  stick is connected (live WMI already in LA).
- Requester (from users register), approver, executor; approver different from requester.
- Classification level and justification (free text, short).
- File list: name, size, SHA-256 per file computed at execution; total count/size.
- Verification: after execution, optional "find on destination by SHA-256" (gap 2) and the
  USB session evidence (first/last connection, user attribution) attached as findings.
- Status: requested, approved, rejected, executed, verified, cancelled; every change in the
  sealed audit chain.
- Report: per-entry transfer form (PDF, signed when gap 6 exists) and a period register report
  (all entries in a control period, with unregistered USB sessions flagged as "transfer not in register").
- Reconciliation check: each USB storage session on the station without a matching register
  entry becomes a finding card (this is where the register and DFIR meet).

## 5. Step-reduction patterns found (owner priority)

| Pattern | Evidence | Apply to LogAnalyzer |
|---|---|---|
| One "collect everything" button for all logs, archives, operational logs and every profile | "Colecteaza tot" | Single "Collect" on the station page |
| Collect once, every screen reuses the same result; re-read only on request | "Ferestrele ... reutilizeaza rezultatul, fara o noua colectare la deschidere" | Shared collection object per case |
| Case created automatically at session start | "Caz initializat automat la pornirea sesiunii" | Auto case with editable details later |
| Standard paths auto-discovered; manual navigation only as fallback question | "Cautare automata"; "Vrei sa navighezi manual la locatia corecta?" | Same, with one prompt |
| Elevated extraction in one click, one UAC prompt, offered at the moment it is needed | "Vrei sa extrag acum SAM (un prompt UAC)?" | Offer elevation inline, not in settings |
| Automatic raw copy when running as admin (no extra step) | "citit automat din copia bruta NTFS a hive-ului SYSTEM" | Silent fallback with a visible note |
| Empty screen offers the action that fills it | "ecranul spune 'Neanalizat' si ofera 'Colecteaza acum'" | Every empty view gets one action button |
| Report page offers missing collection directly | "'Raport si export' ofera colectarea istoricului direct" | Same |
| Triage screen ordered by what to check first, with "Open screen" to the proof | "Ordonate dupa cat de mult merita verificate primele" | Station result page sorted by severity |
| Pre-report check runs itself, shows reservations, one question | "Rezervele apar în raport ... Generezi raportul?" | Auto check before PDF |
| After generation: "Open it now?" | "Raport generat. Il deschid acum?" | Same |
| Report period presets: whole collection / current filter / other dates | "Ce perioada acopera raportul?" | Presets plus "last control period" |
| ±15 min around an event in one click | "Cronologia in jurul acestei ore (±15 min)" | Context button on finding cards |
| Export respects filters, warns, offers "No = export all" | "(Alege Nu ca sa exporti toate randurile.)" | Same dialog |
| Filters with Reset and "X of Y rows"; debounced search | "un sumar 'X din Y randuri'" | Same |
| Progress in the status bar with Stop; cancel keeps previous results | "Anularea si erorile pastreaza colectarea anterioara." | Same |
| Duration estimate optional, no invented percentage | "fara procent inventat" | Same |
| Archive auto-verified after writing; .partial until complete | "verificare automata dupa scriere" | Same for exports |
| Old working copies cleaned automatically after 7 days | "curatarea automata, la pornire, a copiilor de lucru" | Same |
| Language switch instant and remembered; theme follows Windows | "aplicata pe loc si pastrata intre porniri" | Same |
| Destructive action needs typed word, not a click | "confirmarea stergerii accepta acum exact cuvantul cerut" | Same for register deletions |
| Double-click opens safe details/preview, never executes | "executabilele si scripturile nu se deschid niciodata" | Same |
| Menu entries named as questions and steps, tooltip on each | "Ce merita verificat / Ce s-a intamplat, in ordine" | Already partly in WP18 roles UI |

## 6. Notes for planning

- Several LA statuses above are marked uncertain; check code before scheduling (archived EVTX,
  hive transaction logs, Recycle Bin, timezone/DST handling, CSV formula escaping).
- Do not copy the tool's wording into LA; the Romanian evidence strings are for traceability only.
- Credits and author line in the strings: [REDACTED].
