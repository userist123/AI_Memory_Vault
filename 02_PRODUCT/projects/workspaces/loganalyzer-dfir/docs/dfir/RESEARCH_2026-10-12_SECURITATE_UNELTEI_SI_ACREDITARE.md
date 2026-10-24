# Research 2026-10-12: security of the tool itself, accreditation, privilege, supply chain and usability evidence

Status: web research plus read-only inspection of the code (`CODE_VERIFIED` = found by grep/reading in this worktree on 2026-10-12;
`DOCUMENT_VERIFIED` = stated by the cited source). Nothing was built, run or fuzzed. Markers: **[NESIGUR]** = secondary or conflicting
source; **[NECITAT]** = current DFIR practice without a source found in this session. Effort: S ≤ 1 day, M ≤ 1 week, L > 1 week.

**Not repeated here** (already covered): HG 585/2002 requirements quoted verbatim, the gap table and work package WP-ACR
(`HG585_ACCREDITATION_REQUIREMENTS.md`, including SW-01..SW-03 and WP-ACR-08 for signing/SBOM/lock files); the edition split and the
classified-build inspection (`EDITIONS.md`, `LogAnalyzer.Edition.Tests`); SUS/SEQ targets and five users per round
(`RESEARCH_2026-10-12_UTILITATE_SI_FLUX_DE_LUCRU.md` §6, `WP18_USABILITY_SCRIPT.md`); threats against the stations
(`RESEARCH_2026-10-11_AMENINTARI_ACTUALE.md`). This document adds the angles those do not cover and points to them where they overlap.

---

## 0. Summary

1. **The tool parses hostile input at high privilege.** `LogAnalyzer.App/app.manifest:19` requests `highestAvailable` (the classified
   executable uses the same manifest), so an administrator always runs elevated, including when analysing imported evidence offline. Several
   parsers hand untrusted bytes to native Windows code (EVTX through `wevtapi`, SRUM through `esent.dll`, Prefetch through
   `RtlDecompressBufferEx`, browser history through native SQLite). That is the combination Chromium's "Rule of 2" forbids.
2. **Hardening exists but is uneven.** Managed parsers for LNK, compound files/Jump Lists, USN and pcapng have hostile-input tests
   (`LogAnalyzer.Dfir.Tests/MalformedBinaryInputTests.cs`); EVTX, registry hives, Prefetch, SRUM, SQLite, scheduled-task XML and the ZIP
   readers do not. Four parsers read whole files without a size cap; the ZIP readers have no entry, size or ratio limits.
3. **Reports can be made to lie visually.** Bidi overrides, zero-width characters and whitespace padding (the trick behind CVE-2025-9491)
   are shown raw everywhere except the removable-media inspector.
4. **Accreditation of a tool is evidence-driven, not certificate-driven** for most deployments: signed and hashed deliverables, SBOM, locked
   dependencies, documented privilege and footprint, a method-validation record (ISO/IEC 27041 style) and usability evidence. Formal
   evaluation (EUCC/Common Criteria, ANSSI CSPN, BSI BSZ, NATO NIAPC listing) is a later, sponsor-driven step. The EU Cyber Resilience Act
   reporting duties already apply since 11 September 2026 to products on the EU market.
5. **Supply chain:** one project of 19 has a lock file; test packages float; the SDK is not pinned; no `nuget.config` trust policy; no SBOM;
   binaries unsigned. All are S-effort fixes, and signing also matters on air-gapped stations because App Control (WDAC) publisher rules need it.

---

## 1. Attacks against the forensic tool itself

### 1.1 Findings

**Precedents: forensic and archive tools are attack surface**

| Case | What happened | Why it matters to LogAnalyzer | Source |
|---|---|---|---|
| iSEC Partners, 2007 | About a dozen bugs in EnCase and The Sleuth Kit: crafted media could crash the tools or potentially run code on the examiner's machine | The oldest public proof that evidence is attacker-controlled input | [Computerworld](https://www.computerworld.com/article/1582047/black-hat-researchers-say-forensics-software-can-be-hacked.html), [InfoconDB](https://infocondb.org/con/def-con/def-con-15/breaking-forensics-software-weaknesses-in-critical-evidence-collection) |
| Signal vs Cellebrite UFED / Physical Analyzer, 2021 | Code execution in the analysis software from a file on the examined phone; past and future reports could be altered | A parser bug can falsify the report, not just crash; the author notes "very little care" for the tool's own security | [Signal](https://signal.org/blog/cellebrite-vulnerabilities/) |
| The Sleuth Kit ≤ 4.14.0, 2026 | CVE-2026-40024 (`tsk_recover` path traversal: `../` names in an image write outside the recovery folder), CVE-2026-40025 (APFS heap over-read), CVE-2026-40026 (ISO9660 over-read / endless loop) | Path traversal on export and endless loops on zero-length entries are exactly the classes to test | [vuln.today](https://vuln.today/tag/sleuthkit), [Mondoo](https://mondoo.com/vulnerability-intelligence/vulnerability/CVE-2026-40025) **[NESIGUR: aggregators; confirm in NVD]** |
| Velociraptor, 2025 | CVE-2025-6264 (missing permission check, endpoint takeover); old builds installed by intruders as a remote-access tool in ransomware cases | A DFIR tool is also a dual-use binary; attackers prefer old, vulnerable, signed builds | [The Hacker News](https://thehackernews.com/2025/10/hackers-turn-velociraptor-dfir-tool.html), [SentinelOne](https://www.sentinelone.com/vulnerability-database/cve-2025-6264/) |
| WinRAR CVE-2025-8088; 7-Zip CVE-2025-11001/11002 | ADS names with relative paths drop DLL/LNK into Startup on extraction; ZIP symlinks write outside the target folder | Examiners extract evidence archives; LogAnalyzer reads case archives and GPO ZIPs itself | [ESET](https://www.welivesecurity.com/en/eset-research/update-winrar-tools-now-romcom-and-others-exploiting-zero-day-vulnerability/), [SentinelOne](https://www.sentinelone.com/vulnerability-database/cve-2025-11001/) |
| `.library-ms` (CVE-2025-24071/24054) | Explorer parses the file automatically after extraction and authenticates to a UNC path | Opening an evidence folder in Explorer is already processing hostile input | [Check Point](https://research.checkpoint.com/2025/cve-2025-24054-ntlm-exploit-in-the-wild/) |
| Non-recursive ZIP bomb | Overlapping entries reuse one compressed kernel: 10 MB → 281 TB in one pass | Size checks on declared lengths are not enough; count the bytes actually produced and reject overlapping entries | [Fifield, WOOT '19](https://www.usenix.org/conference/woot19/presentation/fifield) |
| SQLite on untrusted files | Official guidance: any database writable by another security domain "should be treated as suspect"; use `trusted_schema=OFF`, `cell_size_check=ON`, `mmap_size=0`, `quick_check` | Browser histories are attacker-writable SQLite files | [SQLite security](https://www.sqlite.org/security.html) |
| Chromium "Rule of 2" | Never combine untrusted input, unsafe implementation language and high privilege in one component | Native parsers inside an elevated process break it | [Chromium docs](https://chromium.googlesource.com/chromium/src/+/main/docs/security/rule-of-2.md) |

**Anti-forensics aimed at tools (not at the station)**

- **Record hiding inside EVTX.** DanderSpritz `eventlogedit` merges a record into the previous one by enlarging its size; viewers then skip it,
  but the bytes remain and are recoverable ([Fox-IT](https://blog.fox-it.com/2017/12/08/detection-and-recovery-of-nsas-covered-up-tracks/)).
  A later claim says renumbering plus checksum recomputation keeps the RecordID sequence clean, which defeats gap checks
  ([trackr.live](https://www.trackr.live/?p=1353)) **[NESIGUR]**.
- **Log flooding** to rotate a small log before collection **[NECITAT]**; LogAnalyzer already checks the Security log size (check A02,
  `LogAnalyzer.Dfir.Windows/Audit/CheckGuide.cs:105`), but not the event rate or whether the log wrapped inside the controlled period.
- **Display tricks.** Right-to-left override in file names, a technique in its own right ([MITRE T1036.002](https://attack.mitre.org/techniques/T1036/002/));
  bidi controls that make text render differently from its logical order ([Trojan Source, CVE-2021-42574](https://trojansource.codes/));
  mixed-script confusables such as a Cyrillic letter inside `svchost.exe` ([Unicode UTS #39](https://www.unicode.org/reports/tr39/));
  whitespace padding that hides LNK arguments in the Windows UI (CVE-2025-9491, [Trend Micro ZDI](https://www.trendmicro.com/en_us/research/25/c/windows-shortcut-zero-day-exploit.html)).
  The same tricks work on a PDF report if the tool prints the raw string.
- **Path spoofing** with trailing spaces or dots (`C:\Windows \System32\`), `\\?\` prefixes, 8.3 short names and alternate data streams
  **[NECITAT]**: detection that compares normalised strings flags them, but a human reading the report sees the trusted path.

### 1.2 What LogAnalyzer does today (`CODE_VERIFIED`)

- **Hostile-input tests:** `LogAnalyzer.Dfir.Tests/MalformedBinaryInputTests.cs` (zero-length, truncation at many offsets, random bytes, every
  length/offset/count field set to extremes; 15 s limit; only `InvalidDataException` allowed) for LNK, compound files (including a cyclic DIFAT
  chain), Jump Lists (a DestList claiming billions of entries), USN exports and pcapng (block length below minimum, timestamps beyond the calendar).
- **Bounds and caps:** MAM-compressed Prefetch rejects an uncompressed size of 0 or > 64 MiB (`LogAnalyzer.Dfir.Windows/Native/NtCompression.cs:23`);
  media inspection stops at 5,000 entries per folder (`LogAnalyzer.Dfir.Core/Devices/RemovableMediaInspector.cs:31`); YARA input ≤ 256 MiB
  (`MaxYaraFileBytes`); EVTX message text truncated to 4,000 characters (`LogAnalyzer.Dfir.Windows/Parsers/EvtxParser.cs:142`).
- **EVTX anti-forensics:** corrupt chunks are detected and a repaired copy is parsed, with the gap reported (`EvtxParser.cs:46-58`); truncation
  reported (`:60-65`); repeated RecordIDs keep unique locators and are reported (`:69-72`); `LOG-GAP`, `LOG-TAMPER` rules.
- **Failure containment:** `EvidenceParserBase.Parse` turns any managed exception into `PARTIAL`/`FAILED` with the error text
  (`LogAnalyzer.Dfir.Core/Parsing/IEvidenceParser.cs:93-110`). Native faults (access violation, stack overflow) are not catchable and end the process.
- **Case archive checks:** duplicate entry names across case and slash direction, unsafe manifest paths (`SafeEntry`), entries not in the manifest,
  manifest hash and signature (`LogAnalyzer.Dfir.Core/Case/CaseArchive.cs:185-245`).
- **Output hygiene:** spreadsheet formula injection neutralised in CSV (`LogAnalyzer.Dfir.Core/IO/Csv.cs:45`); invisible-character names
  flagged on removable media (`RemovableMediaInspector.IsBlank`).
- **Working copies:** SQLite and ESE databases are copied before opening (`BrowserHistoryParser.cs:41`, `FirefoxHistoryParser.cs:39`,
  `SrumNetworkParser.cs:41`), so the evidence file itself is not opened read-write.

### 1.3 Gaps

| # | Gap | Where |
|---|---|---|
| G1 | No hostile-input tests for EVTX (own `EvtxRepair` + native reader), registry hives (`Registry`, `DiscUtils.Registry`, `DeletedRegistryTraces`), Prefetch, SRUM, SQLite, scheduled-task XML, `TimelineCsv`, GPO ZIP, case archive | test inventory above |
| G2 | Whole-file reads without a size cap | `LnkParser.cs:40`, `JumpListParser.cs:37`, `PrefetchParser.cs:39` (`ReadAllBytes`), `UsnJournalParser.cs:84` (`ReadAllLines`) |
| G3 | ZIP readers without entry-count, total-size, ratio or overlap limits; manifest read into memory before any check; a data pack's manifest is read before its signature is verified | `CaseArchive.cs:193`, `Analysis/DataPack.cs:91-96`, `Policy/PolicyFormats.cs:177-196` |
| G4 | Native parsing in the elevated UI process (Rule of 2) | `app.manifest:19`; `EvtxParser.cs:67` (`EventLogReader`), `SrumNetworkParser` (esent), `PrefetchParser` (`RtlDecompressBufferEx`), SQLite |
| G5 | No per-parser time or record budget; only the user's cancellation | `IEvidenceParser.cs:93-103` |
| G6 | SQLite opened `Mode=ReadWrite` on the copy without `trusted_schema=OFF`, `cell_size_check`, `quick_check` | `BrowserHistoryParser.cs:46`, `FirefoxHistoryParser.cs:44` |
| G7 | Reports print raw strings: no neutralisation of bidi/zero-width/control characters; LNK whitespace padding not flagged; no mixed-script check on process, file and account names | no match for U+202E/U+2066 handling in `Dfir.Core`, `Dfir.Windows`, `App` |
| G8 | Path display does not mark trailing space/dot, `\\?\`, ADS or 8.3 forms | `Analysis/Correlation.cs:771` (`Normalize` only strips volume/drive and upper-cases) |
| G9 | No log-wrap/flooding finding (rate burst, oldest record younger than the control period while the log is at its maximum size) | — |

### 1.4 Recommendations

| ID | Recommendation | Effort |
|---|---|---|
| H1 | **Fuzz harness** for the managed parsers (LNK, compound file, Jump List, USN, pcapng, `EvtxRepair`, `TimelineCsv`, `PolicyFormats`, `CaseArchive.Verify`, `ScheduledTaskParser`) with SharpFuzz (AFL/libFuzzer for .NET, [SharpFuzz](https://www-1.nuget.org/packages/SharpFuzz)); seed corpus = existing fixtures; nightly 10-minute run in CI; every crash becomes a `MalformedBinaryInputTests` case | M |
| H2 | **Extend `MalformedBinaryInputTests`** to EVTX (header/chunk fields), Prefetch (MAM header, SCCA offsets), registry hives (cell sizes, deleted cells), SRUM copy, SQLite (truncated, wrong page size), task XML (deep nesting, huge attributes) | M |
| H3 | **`MaxInputBytes` in `ParserDescriptor`**, enforced by `EvidenceParserBase` before `ParseCore`; stream USN line by line | S |
| H4 | **Time and record budget** per parser: linked `CancellationTokenSource.CancelAfter`, maximum records; result `PARTIAL` with the reason „limită de timp/înregistrări” | S |
| H5 | **ZIP hardening** in one helper used by all three readers: ≤ N entries, ≤ N total uncompressed bytes counted while reading (not the declared length), reject overlapping local headers, verify the signature before reading anything large | S |
| H6 | **SQLite pragmas** on every evidence database: `trusted_schema=OFF`, `cell_size_check=ON`, `mmap_size=0`, `quick_check` first; triggers and views disabled | S |
| H7 | **Isolated parsing worker**: native parsers run in a child process started `asInvoker` with a restricted token and a Job object (memory and time limit); the UI process validates the returned records. A crash becomes `FAILED`, not a lost session | L |
| H8 | **Report text sanitiser** (one function for PDF, HTML, CSV, UI): bidi controls U+202A–U+202E and U+2066–U+2069, zero-width and C0/C1 controls shown as visible `⟨U+202E⟩`; runs of whitespace collapsed with a count („234 spații eliminate”); UTS #39 mixed-script flag on names | S–M |
| H9 | **Two-line paths** in reports: normalised path plus the raw path with visible markers for trailing space/dot, `\\?\`, `:stream`, `~1` | S |
| H10 | **Log-wrap finding**: log at maximum size and oldest record newer than the start of the controlled period ⇒ „jurnalul s-a suprascris în perioada controlată”; plus events-per-minute burst per channel | S |
| H11 | **Operator guidance**: never extract evidence archives with the station's archiver or open evidence folders in Explorer; let LogAnalyzer read ZIPs itself | S |

---

## 2. Requirements for software used on classified / air-gapped systems (RO / EU / NATO)

### 2.1 Findings (public sources only)

- **Romania (national).** HG 585/2002 and the INFOSEC rules are analysed verbatim in `HG585_ACCREDITATION_REQUIREMENTS.md`; accreditation is
  the decision of the security accreditation authority, not of the vendor. For EU certification schemes under the Cybersecurity Act, ENISA's
  list of national certification authorities gives Romania a contact at the national cyber directorate ([ENISA NCCA list](https://certification.enisa.europa.eu/take-action/national-cybersecurity-certification-authorities_en)).
  No public national product-evaluation scheme for security tools was found **[NESIGUR]**; nothing national is assumed below.
- **EU classified information.** The Council security rules for EU classified information require accredited communication and information
  systems ([Council Decision 2013/488/EU](https://eur-lex.europa.eu/eli/dec/2013/488/oj)); the Commission has parallel rules
  ([Decision 2015/444](https://eur-lex.europa.eu/eli/dec/2015/444/oj)). Neither publishes a checklist for analysis tools; the accreditor applies
  the system's security requirements to every component **[NESIGUR on the exact annex wording]**.
- **NATO.** The NATO Information Assurance Product Catalogue lists products with assurance information from a NATO nation's authority; a
  separate **Security Tools** category is assessed under a dedicated IA directive; meeting the minimum criteria is not sufficient for listing,
  and a listing is not by itself an approval for use ([NIAPC vendor info](https://www.ia.nato.int/niapc/Information/NIAPC-vendor-info)).
  Listing needs a sponsoring nation.
- **Evaluation schemes.**
  - **EUCC** (Common Criteria under EU law, ISO/IEC 15408 and 18045, 2022 editions): in application since 27 February 2025
    ([Regulation (EU) 2024/482](https://eur-lex.europa.eu/eli/reg_impl/2024/482/oj), [ENISA EUCC](https://certification.enisa.europa.eu/browse-topic/eucc_en)).
    Heavy: security target, ALC/AGD/ADV evidence, accredited lab.
  - **ANSSI CSPN** (France): time-boxed evaluation by a licensed lab, maximum 60 person-days since procedure v5.0 (2023), certificate valid at
    most 3 years ([ANSSI CSPN procedure](https://cyber.gouv.fr/sites/default/files/document/ANSSI-CSPN-CER-P-01%20Certification_de_securite_de_premier_niveau_v5.0.pdf)).
  - **BSI BSZ** (Germany): accelerated certification using EN 17640 (fixed-time methodology), risk-driven penetration testing, update-mechanism
    review; scopes are currently network and embedded devices ([BSI BSZ](https://bsi.bund.de/EN/Themen/Unternehmen-und-Organisationen/Standards-und-Zertifizierung/Zertifizierung-und-Anerkennung/Zertifizierung-von-Produkten/Beschleunigte-Sicherheitszertifizierung/Beschleunigte-sicherheitszertifizierung_node.html)).
    EN 17640 is the likely methodology for any light national scheme **[NESIGUR]**.
- **EU Cyber Resilience Act** ([Regulation (EU) 2024/2847](https://eur-lex.europa.eu/eli/reg/2024/2847/oj)): reporting of actively exploited
  vulnerabilities and severe incidents (24 h / 72 h / final report) applies since **11 September 2026**, also to products already on the
  market; the other obligations, including an SBOM in the technical documentation, from 11 December 2027
  ([NCSC Ireland](https://ncsc.gov.ie/cra/), [Jones Day](https://www.jonesday.com/en/insights/2026/07/eu-cyber-resilience-act-24hour-reporting-duties-start-september-11-2026)).
  Products developed exclusively for national security or defence are excluded; a dual-use product sold commercially is not
  **[NESIGUR: confirm the article and LogAnalyzer's status with a lawyer before licensing]**.
- **Forensic method validation** (what makes a tool's output usable as evidence): ISO/IEC 27041 (requirements, validation, and how vendor and
  third-party testing can support it) ([ISO](https://www.iso.org/standard/44405.html)); the UK regulator's guidance that validation shows a
  method is fit for purpose and supports ISO/IEC 17025/17020 accreditation ([gov.uk](https://www.gov.uk/government/publications/method-validation-in-digital-forensics));
  NIST's tool-testing programme with published specifications ([NIST CFTT](https://www.nist.gov/itl/ssd/software-quality-group/computer-forensics-tool-testing-program-cftt)).
- **Secure development** frameworks usually asked for in procurement: NIST SSDF ([SP 800-218](https://csrc.nist.gov/pubs/sp/800/218/final)),
  SLSA build levels ([SLSA](https://slsa.dev/spec/v1.0/levels)), SBOM minimum elements ([NTIA](https://www.ntia.gov/report/2021/minimum-elements-software-bill-materials-sbom)).

### 2.2 Mapping to LogAnalyzer

| Typical requirement | Evidence an accreditor asks for | LogAnalyzer now (`CODE_VERIFIED`) | Gap | Effort |
|---|---|---|---|---|
| Authentic, unaltered software | Authenticode signature, published SHA-256 list, self-check at start | unsigned (`SECURITY.md:30`); CI uploads the publish folder without a hash list | signing, `SHA256SUMS`, start-up self-check (WP-ACR-08) | S–M |
| Component inventory | SBOM (CycloneDX/SPDX) per release | none; manual Annex A in the HG 585 document | SBOM in CI (§4) | S |
| Dependency provenance | locked restore, signed packages, known sources | lock file only in `LogAnalyzer.Dfir.Windows` (`.csproj:9-11`); floating test versions | §4 | S |
| Reproducible build | same source ⇒ same binaries, or explained differences | `Directory.Build.props` sets only `LangVersion`; SDK `10.x` floats in CI | §4 | M |
| No network in the classified edition | build inspection | `ClassifiedEditionBuildTests` (no network, AI or host-modifying code in the P1 output) | none for the build; keep the test mandatory | — |
| Read-only on the target | proof the tool does not alter the system | `ReadOnlyCollectionTests` (no log-clearing calls or literals); `AnalysisFootprint` (F10) | measured footprint (§3) | S–M |
| Audit of the tool's own actions | tamper-evident log of who did what | hash-chained custody and audit (`Case/HashChain.cs`, custody JSONL `output.written`) | log elevation state and executable hash at session start (§3) | S |
| Least privilege | only what the task needs | `highestAvailable` for everything | §3 | M |
| Vulnerability handling | contact, process, deadlines; CRA reporting | `SECURITY.md` „Raportarea vulnerabilităților” (contact only) | written process with CRA 24 h / 72 h steps, advisory format, supported versions | S |
| Method validation | requirements, test corpus, acceptance criteria, results per version | `FORENSIC_TEST_LAB.md`, corpus tests (skip without `LADFIR_CORPUS`), `forensic_validation.txt` in CI | ISO/IEC 27041-style validation record per release (requirements → tests → result → limits) | M |
| User guidance | installation and secure-use guidance (CC AGD_PRE/AGD_OPE analogue) | `docs/dfir/*`, in-app „Ce înseamnă” texts | one operator guide per role with the secure-use rules (H11, §3) | M |
| Formal evaluation | EUCC / CSPN / NIAPC certificate | none | sponsor decision; only after the items above | L |

**Recommendation A1 (M):** a single „dosar de acreditare a instrumentului” generated per release from the repository: SBOM, `SHA256SUMS`,
signature report, test and validation results, privilege and footprint statement, edition-inspection result, usability report (§5). Most
items are CI outputs; the accreditor receives evidence, not claims.

---

## 3. Running the tool as administrator on a classified station

### 3.1 Findings

- **Principles:** minimise changes to the system and document every action ([NIST SP 800-86](https://csrc.nist.gov/pubs/sp/800/86/final));
  use trusted tools from protected media and avoid changing access times ([RFC 3227](https://www.rfc-editor.org/rfc/rfc3227)).
- **What really needs elevation** (Windows): reading the Security log needs the „Manage auditing and security log” right, or membership of
  a group the channel's access list allows — Event Log Readers by default, but the access list can differ, so check it per station
  ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/eventlog/event-logging-security), [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/608154/does-the-built-inevent-log-readers-have-access-to)) **[NESIGUR on defaults]**;
  saving a hive (`RegSaveKey`, `reg save`) needs the backup privilege ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regsavekeya));
  locked files (SRUM, Amcache) and the Prefetch folder need administrator or backup semantics **[NECITAT]**. Offline analysis of an imported case
  needs no elevation at all.
- **Write blocking:** a write blocker must not let a protected drive be changed and must still allow read-only information gathering (NIST
  CFTT specification, [NIJ report](https://www.ojp.gov/pdffiles1/nij/209833.pdf)); some software blockers failed parts of these tests
  ([NIJ ACES report](https://nij.ojp.gov/library/publications/aces-software-write-block-tool-test-report-writeblocker-windows-2000-v50200)).
  Mounting a stick in Windows can itself write to it (`System Volume Information` entries) **[NECITAT]**, so suspect media need a
  hardware blocker or a validated software policy.
- **Allow-listing:** App Control for Business (WDAC) can allow by publisher only for signed files; unsigned tools need hash rules that break
  at every build ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/select-types-of-rules-to-create)).

### 3.2 What LogAnalyzer does today (`CODE_VERIFIED`)

- Elevation is checked and used only where needed during collection: the Security channel is skipped with a recorded error when not elevated
  (`LogAnalyzer.Dfir.Windows/Acquisition/Collectors.cs:44`); hives are copied with System32 `reg.exe save` and the command is recorded
  (`Collectors.cs:242-246`); EVTX export uses `EvtExportLog` in process, never re-rendered (`Collectors.cs:11-47`).
- A warning (never a block) is written to custody and audit when the collecting account is administrator of the audited system
  (`LogAnalyzer.Dfir.Core/Case/CaseWorkspace.cs:228-240`).
- `AnalysisFootprint` (F10) states the temporary folders and registry areas the code uses and counts acquired copies and written reports
  (`LogAnalyzer.Dfir.Core/Case/AnalysisFootprint.cs`).
- No log-clearing code or literal in any shipped assembly (`LogAnalyzer.Edition.Tests/ReadOnlyCollectionTests.cs`); no host-modifying types
  in the classified build (`ClassifiedEditionBuildTests.cs:68`).

### 3.3 Gaps

- **Whole application elevated** (`highestAvailable`) also for offline analysis and report writing: maximum privilege for the code that parses
  hostile input (G4 in §1).
- **Footprint is declared, not measured:** the static list omits what running the tool creates by itself: its own Prefetch, Amcache, BAM and
  ShimCache entries, a 4688 if process auditing is on, and the native libraries the single-file build extracts at run time
  (`IncludeNativeLibrariesForSelfExtract=true`, `LogAnalyzer.App/Properties/PublishProfiles/win-x64-singlefile.pubxml:23`).
- **Write-protection state of evidence media is not recorded** at intake.
- **Session record** does not state elevation, enabled privileges or the executable's own hash.

### 3.4 Recommendations

| ID | Recommendation | Effort |
|---|---|---|
| P1 | Manifest `asInvoker`; „Colectare live” relaunches an elevated collector (same signed exe, narrow command line, writes only into the case folder); analysis and reports stay unelevated | M |
| P2 | Optional non-admin Security-log path: if the account can read the channel (check the channel access list), collect without elevation and say so | S |
| P3 | Measured footprint: list the tool's own Prefetch/Amcache/BAM names and the extraction folder in `AnalysisFootprint`; better, ship native libraries next to the exe (also WP-ACR-08) so nothing is extracted | S–M |
| P4 | Intake field „mediu protejat la scriere: da / nu / necunoscut” read from the volume (read-only flag) plus operator guidance: hardware write blocker for suspect media | S |
| P5 | Session start record in the hash-chained audit: elevated yes/no, privileges enabled, SHA-256 and signer of the running exe | S |
| P6 | Supported „run from read-only medium” mode: no file next to the exe, case folder on a separate medium; test it in CI on a read-only folder | S |

---

## 4. Supply-chain hygiene for the .NET desktop app

### 4.1 Findings

- **Lock files:** `RestorePackagesWithLockFile` writes `packages.lock.json` with content hashes; locked mode fails the restore if the graph
  changed ([NuGet blog](https://devblogs.microsoft.com/nuget/enable-repeatable-package-restores-using-a-lock-file/)).
- **Vulnerability audit:** for projects targeting .NET 10, `NuGetAuditMode` defaults to `all`, so vulnerable transitive packages warn — and
  fail the restore where warnings are errors ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nugetaudit-transitive-packages)).
- **Signature policy:** `signatureValidationMode=require` with `trustedSigners` in `nuget.config`; tampered signed packages fail with NU3008
  ([Microsoft Learn](https://learn.microsoft.com/en-us/nuget/consume-packages/installing-signed-packages)); package source mapping pins each
  package to a feed ([Microsoft Learn](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)).
- **SBOM:** CycloneDX for .NET ([cyclonedx-dotnet](https://github.com/CycloneDX/cyclonedx-dotnet), also as `CycloneDX.MSBuild`) or Microsoft's
  SPDX generator ([sbom-tool](https://github.com/microsoft/sbom-tool)).
- **Reproducibility:** deterministic compilation is the SDK default; `ContinuousIntegrationBuild` on the build server normalises paths; NuGet
  packages may still differ by dates ([.NET blog](https://devblogs.microsoft.com/dotnet/producing-packages-with-source-link/),
  [Roslyn](https://github.com/dotnet/roslyn/blob/main/docs/compilers/Deterministic%20Inputs.md)).
- **Code signing in 2026:** publicly trusted code-signing certificates are limited to 460 days from 1 March 2026 ([CA/B Forum CSC-31](https://cabforum.org/2025/11/17/ballot-csc-31-maximum-validity-reduction));
  EV certificates no longer give instant SmartScreen reputation since 2024, reputation is built per file and signer
  ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)). On an air-gapped station SmartScreen
  has no cloud anyway; the signature matters for WDAC publisher rules, integrity checks and the accreditor.
- **Defender false positives:** a heuristic flagged the compiled test assembly because test fixtures contain ransomware-style command lines
  (shadow-copy deletion). Detection content (`Analysis/Data/rule_lists.json` → `vss` patterns) is embedded in the shipped `LogAnalyzer.Dfir.Core.dll`
  (`LogAnalyzer.Dfir.Core.csproj`, `EmbeddedResource`), and `LogAnalyzer.Core` carries detector literals (`RansomwareDetectionEngine`), so shipped
  binaries carry the same risk. False positives can be submitted to Microsoft ([submission portal](https://www.microsoft.com/en-us/wdsi/filesubmission)).

### 4.2 What LogAnalyzer does today (`CODE_VERIFIED`)

- Exact versions for product packages; `LogAnalyzer.Dfir.Windows` has a lock file and locked mode under CI or Release
  (`LogAnalyzer.Dfir.Windows.csproj:9-11`); `Registry` and `Serilog` pinned with `[x]` ranges (`:20-21`).
- CI actions pinned by SHA, `permissions: contents: read` (`.github/workflows/loganalyzer-dfir-build.yml`); a repository-level dependency audit
  workflow runs a .NET audit (`.github/workflows/dependency-audit.yml`).
- `TreatWarningsAsErrors` in `Dfir.Core`, `Dfir.Windows`, `Verification` (so NuGet audit warnings fail those builds).

### 4.3 Gaps

- 18 of 19 projects without lock files; test projects use `17.*` and `2.*`; no `global.json` (SDK floats with `dotnet-version: '10.x'`); no
  `nuget.config` (no source mapping, no trusted signers); no SBOM; no signing; `Deterministic`/`ContinuousIntegrationBuild` not set; native
  libraries self-extracted at run time; detection strings embedded in shipped DLLs.

### 4.4 Concrete steps

| Step | What | Effort |
|---|---|---|
| SC1 | `Directory.Build.props`: `RestorePackagesWithLockFile=true` for all projects, `RestoreLockedMode` when `CI`; commit 19 lock files; replace floating test versions with exact ones | S |
| SC2 | `global.json` pinning the SDK (`rollForward: latestPatch`); CI `setup-dotnet` reads it | S |
| SC3 | `nuget.config` next to the solution: only nuget.org (or an internal mirror for offline builds), package source mapping, `signatureValidationMode=require`, `trustedSigners` for the nuget.org repository signature | S |
| SC4 | `ContinuousIntegrationBuild=true` in CI; a second CI job rebuilds the same commit and compares file hashes; differences listed in the release notes | M |
| SC5 | CycloneDX SBOM per edition at publish, attached to the release with `SHA256SUMS` | S |
| SC6 | Authenticode signing of every published `.exe`/`.dll` with timestamp; signing key in a cloud HSM or a hardware token kept by the owner; certificate renewal every ≤ 460 days in the release checklist | M |
| SC7 | Ship native libraries next to the exe instead of self-extraction (folder publish or MSIX); the self-check (WP-ACR-08) verifies them | S–M |
| SC8 | Keep attack-like strings out of compiled assemblies: test fixtures as data files with a trivial encoding (e.g. reversed or base64) decoded at test time; detection patterns in a signed data pack (`Analysis/DataPack.cs`) rather than embedded resources where possible; submit any remaining false positive with the signed build | S |
| SC9 | Offline-build kit for an isolated build machine: lock files + local package mirror + pinned SDK, so a build can be reproduced inside the accreditor's environment | M |

---

## 5. Usability validation evidence that counts

### 5.1 Findings

- **Definition used by standards:** usability = effectiveness, efficiency and satisfaction for specified users, goals and context of use
  ([ISO 9241-11:2018](https://www.iso.org/standard/63500.html)); a report must therefore name the role, the station type and the conditions.
- **Report format:** the Common Industry Format standardises what a usability report contains (participants, tasks, measures, results);
  originally for summative tests, now revised as ISO 25062:2025 for several evaluation approaches ([ISO 25062](https://www.iso.org/standard/84255.html)).
  A public worked template exists in health IT ([NISTIR 7742](https://www.nist.gov/publications/customized-common-industry-format-template-electronic-health-record-usability-testing)) **[NESIGUR on current URL]**.
- **Formative vs summative:** five users per role per round finds problems (formative). A claim of *validated* use is summative: the medical
  analogue expects at least 15 representative participants per distinct user group and focuses on **critical tasks** whose failure causes harm
  ([FDA guidance](https://www.fda.gov/regulatory-information/search-fda-guidance-documents/applying-human-factors-and-usability-engineering-medical-devices);
  number via [Johner Institute](https://johner-institute.com/consulting-approval/services/human-factor-engineering/summative-evaluation-validation-testing/) **[NESIGUR: secondary]**).
  Not binding here, but it is the most cited public benchmark.
- **Small-sample statistics:** report completion with an adjusted-Wald interval ([MeasuringU](https://measuringu.com/wald/)); 5 of 5 successes
  still means roughly 51–100% at 95% (own calculation); SUS from five users has a wide interval but a fairly stable mean
  ([MeasuringU SUS](https://measuringu.com/10-things-sus/)).

### 5.2 What LogAnalyzer has

`docs/dfir/WP18_USABILITY_SCRIPT.md`: scenarios A (controller, `CONTROL` station), B (duty officer, `CSIRT`), C (analyst), D (administrator
without IT training), timing, stops > 30 s, success without help. `RESEARCH_2026-10-12_UTILITATE_SI_FLUX_DE_LUCRU.md` §6.3 sets SEQ/SUS
targets. No session has been recorded; no protocol version, no critical-task list, no report format, no link to the build tested.

### 5.3 How to run and document it so it counts

| ID | Step | Effort |
|---|---|---|
| U1 | **Protocol v1** (versioned file, approved by the owner before the first session): roles, station replica (non-classified, synthetic case per `PRIVACY_OF_TEST_DATA.md`), tasks per role, success criteria, measures (completion, time, SEQ after each task, SUS after each role scenario, use errors), stop rules | S |
| U2 | **Critical tasks** with zero tolerance, defined before testing: e.g. controller marks a station compliant while a check is non-compliant; an unregistered medium is not reported; a report is signed with the wrong scope or date; an analyst reads an inference as a fact. Any failure = finding with root cause and fix before the next round | S |
| U3 | **Traceability:** each session records the SHA-256 and version of the tested executable, protocol version, participant code (role, IT experience band; no names), date, observer | S |
| U4 | **Rounds:** formative rounds of 5 per role (controller, duty officer, analyst, administrator) with fixes in between; one summative round with ≥ 15 controllers (the main, least technical group) before claiming „validat” to management or an accreditor | L (people) |
| U5 | **Report per round in CIF structure** (ISO 25062): context of use, participants table, tasks, results with adjusted-Wald intervals and SUS mean ± interval, use-error list, critical-task outcome, changes made; stored under `docs/dfir/usability/` without personal data | S |
| U6 | **No recording on a classified station**; observer notes on paper or on the replica only; consent text for participants | S |

---

## 6. Prioritised backlog (top 12)

| # | Item | Angle | Effort | Why this order |
|---|---|---|---|---|
| 1 | Authenticode signing, `SHA256SUMS`, CycloneDX SBOM per edition in CI (SC5, SC6; WP-ACR-08) | 2, 4 | S–M | first thing any accreditor and WDAC policy asks for |
| 2 | Lock files for all projects, exact test versions, `global.json`, `nuget.config` with source mapping and trusted signers (SC1–SC3) | 4 | S | cheap; makes items 1 and 9 meaningful |
| 3 | Report text sanitiser for bidi/zero-width/control characters and whitespace padding, plus `LNK-ARGS-PADDED` (H8, H9) | 1 | S–M | stops the report from showing something other than the evidence |
| 4 | `MaxInputBytes` and time/record budgets enforced in `EvidenceParserBase` (H3, H4) | 1 | S | removes memory exhaustion and hangs from whole classes of input |
| 5 | Hardened ZIP reader for case archive, data pack and GPO backup; SQLite pragmas on evidence databases (H5, H6) | 1 | S | containers are the easiest bomb vector |
| 6 | `asInvoker` by default; elevated collector only for live collection; session record with elevation and exe hash (P1, P5) | 3 | M | ends high-privilege parsing of hostile input in the common case |
| 7 | Keep attack-like strings out of compiled assemblies; submit remaining Defender false positives (SC8) | 4 | S | prevents the tool being quarantined on the station it must check |
| 8 | Fuzz harness for managed parsers and malformed-input tests for EVTX, Prefetch, registry, SRUM, SQLite (H1, H2) | 1 | M | finds the bugs items 4–5 only contain |
| 9 | Reproducible-build check job and offline-build kit (SC4, SC9) | 4 | M | lets the accreditor rebuild what was approved |
| 10 | Measured footprint and write-protection state at intake; ship native libraries next to the exe (P3, P4, SC7) | 3, 4 | S–M | turns the read-only claim into evidence |
| 11 | Usability evidence pack: protocol v1, critical tasks, traceability, CIF report per round (U1–U3, U5) | 5 | S | makes the planned WP18 sessions count |
| 12 | Isolated low-privilege parsing worker for native parsers (H7) | 1 | L | the structural fix for the Rule-of-2 violation once item 6 is done |

Follow-ups outside the top 12: tool accreditation dossier generated per release (A1, M); vulnerability-handling process with CRA reporting
steps (S); ISO/IEC 27041-style validation record (M); summative usability round with ≥ 15 controllers (U4, L); log-wrap finding (H10, S).
