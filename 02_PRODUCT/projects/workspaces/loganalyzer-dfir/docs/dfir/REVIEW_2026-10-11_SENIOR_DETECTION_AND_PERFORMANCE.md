# Senior review: detection, collection, alerting, attribution, explanation and performance (2026-10-11)

This is an independent, read-only review. All paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

- **Code findings** were verified in source along the production path (UI → pipeline → collector/parser → rule → finding → report). They are code-verified, not runtime-verified, unless marked *measured*.
- **Measurements** were taken on 2026-10-11 between 21:20 and 21:45 local time, on the owner's development machine.
- **Line numbers** are from that working tree. Another session was committing in parallel (for example `41e0f2ad3`, the asynchronous WMI query), so a few line numbers may drift by a few lines.

Status vocabulary used in the tables:

- **REAL**: works end to end.
- **SHALLOW**: wired, but too narrow or naive.
- **FACADE**: looks like it works, but does not do what the UI implies.
- **MISSING**: not implemented.
- **DUPLICATE**: two engines implement the same thing differently.
- **DEAD**: no production consumer.

Each issue lists its impact on (a) non-IT controllers and (b) CSIRT analysts.

---

## Executive summary

1. **Two detection engines run side by side and contradict each other.**
   - The **Dfir pipeline** (Investigation tab → `InvestigationPipeline`) is the trustworthy one: 62 rule contracts, evidence-bound findings, documented limits.
   - The **legacy engine** (drag-and-drop import → `AnalysisEngine`/"Sigma"/"YARA"/LOLBAS/Kerberos) analyses a truncated, channel-blind event set. It raises different severities on other pages: one High alert per single 4625, Critical for any "bypass".
2. **The legacy import silently truncates.** It analyses only the newest 20,000 events from a fixed ID list, and the AI page only the newest 50,000. Six file types are accepted and hashed but never parsed, yet the import still reports "Procesare completă".
3. **The live collector gathers only 5 source families** (event logs, Prefetch, SRUM, SYSTEM+Amcache, live state). Per-user artifacts (NTUSER, UsrClass, LNK, Jump lists, browser, PowerShell history, setupapi, task XML, $MFT) are never collected live. Several of them are not parsed on import either.
4. **Attribution is the weakest area.**
   - There is no logon-session model (4624↔4634 by LogonId with a boot epoch) and no offline SID→name map.
   - USB→user attribution relies on 6416, which almost never carries an interactive user.
   - For imported evidence, the report names the **examiner's** machine as the station.
5. **Top threat gaps** (no detection in the Dfir pipeline): LSASS dump, RunMRU/ClickFix, BYOVD (CodeIntegrity is not collected), password spray, AMSI bypass, WMI subscription persistence (5861 is collected but unused) and Kerberoasting. The RMM list has only 8 tools.
6. **Time handling bug in the legacy database.** Times are stored as local-offset strings and compared with a UTC string, so the 24 h/7 d filters are off by the UTC offset and sorting across a DST change is wrong. The USN import uses the examiner's time zone.
7. **Main-window start-up is moderate, and it is not the main slowness** *(measured, Release, warm)*:
   - services+DB ≈ 45 ms, MainViewModel ≈ 215–250 ms, MainWindow ≈ 190 ms.
   - Cold runs right after a build: 1.5–2.3 s in services+DB.
   - The "36 eager engines" are **not** the cost: each is about 0 ms.
   - The measured costs are `StationControlViewModel` (≈110 ms, it deserializes every previous control report), the knowledge-base JSON (≈186 ms cold) and JIT.
8. **The real freeze comes right after the window appears, and the start-up test does not measure it.**
   - The constructor's `Task.Run(() => Dispatcher.Invoke(...))` runs all database reloads plus `RunAiAnalysis` on the **UI thread**.
   - *Measured:* ≈ 0.45 s with today's 7.6k-event database and ≈ 1.25 s with 50k events. It grows linearly, plus whatever the AI engines add.
   - Searching is a full scan: 0.64 s per keystroke at 50k events.
9. **The local database file is 990 MB but holds 7,629 events.** 98.5% of its pages are free (238,073 of 241,627), because `ClearDatabase` never vacuums. The system disk was at **100% (245 MB free)** during this review, which broke a benchmark with "database or disk is full".
10. **Highest-value fixes, in order:**
    1. Move the post-start-up load off the UI thread and stop running `RunAiAnalysis` at start-up.
    2. Add VACUUM/auto_vacuum and a Timeline index.
    3. Route all imports through the Dfir pipeline, or delegate the legacy engine to the Dfir rules.
    4. Add P1 rules: LSASS, ClickFix, BYOVD, spray, AMSI, WMI, RMM list.
    5. Build a session/identity model, and separate the source host from the examiner host.
    6. Turn on ReadyToRun for the published build.

---

## Part A — Detection, collection, alerting, identification, explanation

### A.0 Production paths (verified)

| Path | Entry | Engine | Output |
|---|---|---|---|
| A — Dfir investigation | `LogAnalyzer.App/ViewModels/InvestigationViewModel.cs:316-322` | `InvestigationPipeline` steps, in order:<br>1. `Correlation.Run` (incl. `Wp11Rules`)<br>2. `Wp14Analysis`<br>3. `LiveStateAnalyzer`<br>4. `PolicyTimeline`<br>5. `SequenceRules`<br>6. `ProvenanceBinder`/`FindingContract`<br>7. `DetectionEngine` (SigmaLite/YaraLite/IOC)<br>8. `AntiForensics` (`Dfir.Windows/Investigation/InvestigationPipeline.cs:294-380`) | `findings.json`, Investigation tab, `InvestigationReportPdf` |
| B — Legacy import | `LogAnalyzer.App/ViewModels/MainViewModel.cs:1964-2330` | `Infrastructure` `EvtxParser`/`OfflineRegistryParser` → SQLite → `AnalysisEngine` (+ "Sigma", "YARA", anomaly) | `DetectedIssues` (Dashboard, Alerts pages) |
| B' — Legacy "AI" | `RunAiAnalysis` (`MainViewModel.cs:2527-2754`). It runs **at start-up**, after import, and from the button `Views/RiskAnalysisView.xaml:18` | Anomaly, YARA, Kerberos, LOLBAS, SigmaCorrelation, FileServer, UBA, SAM… | Risk/AD/Kerberos pages |
| C — Legacy live | `StartLiveMonitoring` → `LiveSecurityMonitoringEngine.EvaluateLiveEvent` (`MainViewModel.cs:1751-1806`) | ~15 keyword rules | LiveAlerts, toast |
| D — Media live | `MainWindow.xaml.cs:13` (`MediaCenter.Activate()` 3 s after Loaded) | `DeviceWatcher` / inventory | Media banner in `MainWindow.xaml:217-223` |

### A.1 Collection

**Ground truth: what is actually read.**

| Source | Live collector (path A) | Import (path A) | Legacy import (path B) |
|---|---|---|---|
| EVTX | 23 channels exported raw (EvtExportLog) (`Dfir.Windows/Acquisition/Collectors.cs:14-27`). Security is skipped when not admin (`:44`). No cap and no time window. | all records of `.evtx` | `.evtx`; analysis cap 20k newest from a fixed ID list |
| Prefetch, SRUM (network table only), SYSTEM (`reg save`), Amcache (+LOG1/2 via VSS) | yes, admin only (`Collectors.cs:77,114,242`) | yes | no |
| NTUSER / SOFTWARE | **no** | yes (UserAssist, Run, Winlogon, IFEO only) | `.dat`: Run key only (`Infrastructure/Parsers/OfflineRegistryParser.cs:19-38`) |
| LNK, Jump lists, Chromium/Firefox history, USN (fsutil text), task XML | **no** (tasks: command line only, in the live snapshot) | yes | accepted and hashed, **not parsed** |
| $MFT, raw $J, ConsoleHost_history, RunMRU, MountPoints2 (offline), ShellBags/UsrClass, setupapi.dev.log, WMI repository, SAM/SECURITY, EMDMgmt | **no** | **no** (type `file`: hashed and YARA-scanned only) | no |
| CodeIntegrity/Operational (BYOVD) | **not in channel list** (`Collectors.cs:14-29`) | if imported | — |

**Issues.**

| # | Finding | Status | Evidence | Impact (a) / (b) | Fix | Pri | Effort |
|---|---|---|---|---|---|---|---|
| C1 | The live collector never copies per-user artifacts, even as admin, although parsers for most of them exist | SHALLOW | `Dfir.Windows/Investigation/InvestigationPipeline.cs:79-80`; `Acquisition/Collectors.cs` (5 `ICollector`s) | (a) "Analyse this station" misses all user activity. (b) No KAPE-level triage. | Add a `UserArtifactsCollector`. Per profile, take VSS/raw copies of NTUSER(+LOG1/2), UsrClass, `Recent\*.lnk`, AutomaticDestinations, browser DBs and ConsoleHost_history. Also collect `System32\Tasks`, `inf\setupapi.dev.log`, and SOFTWARE/SAM via `reg save`. | P1 | M |
| C2 | No parser for setupapi.dev.log, ConsoleHost_history, RunMRU/TypedPaths/RecentDocs/MountPoints2/ShellBags, WMI OBJECTS.DATA, $MFT, raw $J or SAM | MISSING | `InvestigationPipeline.cs:111-131`; `Parsers/RegistryPersistenceParsers.cs:88-131` | (a) A KAPE folder looks "complete" on the intake screen. (b) No USB first-install time, no PowerShell history, no WMI persistence, no MFT timeline. | Add parsers in this order: setupapi → ConsoleHost_history → NTUSER RunMRU/MountPoints2/RecentDocs → SAM → MFT. List the families that are "present but not analysed" on the intake screen. | P1 | L |
| C3 | The legacy import accepts and hashes `.lnk .pf .hve .log .txt .bin .json .bmc` but parses only `.evtx/.reg/.dat/.csv`, then reports "Procesare completă" | FACADE | `MainViewModel.cs:1959-1962, 2046-2049` | (a) Misleading result. (b) Prefetch, LNK and Amcache are silently skipped. | Route every import to path A, or list the unparsed files in the summary | P1 | S |
| C4 | The legacy analysis sees only the newest 20,000 events (`ORDER BY TimeCreated DESC LIMIT`) and 10,000 registry rows, with no notice. The ID list is channel-blind: EID 1/8/10 from any provider counts as Sysmon, and 4698/4728/4756/1116/5001/5007/4769/4768/5140/5145 are not listed. | SHALLOW | `MainViewModel.cs:2257-2259`; `Infrastructure/Services/DatabaseService.cs:268` | (a) False "nothing found" on busy logs. (b) Initial access is dropped. | Page through all rows by (channel, id) and show the analysed time range | P1 | S |
| C5 | Legacy database times are stored as local-offset strings (`ToString("o")` of a Local DateTime, for example `2026-10-11T14:39:59+03:00`). The 24H/7D filters compare them with a UTC `Z` string, so the window is off by the offset. Text sorting across a DST change is wrong. A missing time becomes year 0001. | REAL bug | `Infrastructure/Parsers/EvtxParser.cs:71`; `DatabaseService.cs:130, 222-226, 262-266`; DB sample verified (*measured*) | (a) "Last 24 h" is wrong by 2–3 h. (b) Ordering errors. | Store UTC ticks (INTEGER) or UTC `o`, and store a missing time as NULL | P2 | S |
| C6 | The USN fsutil import converts times with the **examiner's** `TimeZoneInfo.Local`, and the case Timezone also comes from the examiner's machine | REAL bug | `Dfir.Core/FileSystem/UsnJournalParser.cs:15-17,116`; `Parsers/WindowsParsers.cs:15`; `InvestigationPipeline.cs:90` | (b) USN times shift by hours | Ask for the source zone at import, or read `TimeZoneInformation` from the imported SYSTEM hive | P2 | S |
| C7 | The PDF prints times in the printing machine's zone, minutes only. Finding text embeds "… UTC", so one report mixes zones. | SHALLOW | `Investigation/InvestigationReportPdf.cs:26-27,43`; `Dfir.Core/Analysis/Wp11Rules.cs:65` | (a) Two conventions on one page. (b) Close events cannot be ordered. | Print UTC with seconds, plus the source-local time in brackets | P2 | S |
| C8 | Only the copy is hashed on import. The source is not hashed before or after copying. | SHALLOW | `Dfir.Core/Case/CaseWorkspace.cs:142-146,160` | (a)/(b) A weaker acquisition hash for legal value | Hash the source while streaming the copy, compare with the copy hash, record both | P2 | S |
| C9 | EVTX parsing calls `ToXml()` and `XDocument.Parse` on every record and keeps the whole case in memory (ListSink) before sorting. The legacy path also calls `FormatDescription()` on every record. Documented rate: 348k events in about 19 s. | SHALLOW (perf) | `Dfir.Windows/Parsers/EvtxParser.cs:67-101,116-120`; `InvestigationPipeline.cs:210,272`; `Infrastructure/Parsers/EvtxParser.cs:58` | (a) Freezes or out-of-memory on multi-GB server logs. (b) Slow big cases. | Use `EventLogReader` with `BatchSize` and an `EventLogPropertySelector`; stream to SQLite and sort on disk; render messages lazily | P2 | M |
| C10 | Hive transaction logs (.LOG1/.LOG2) are never replayed, so dirty imported hives lose their latest writes. Amcache logs are copied but not applied. | SHALLOW (documented) | `RegistryPersistenceParsers.cs:78,176`; `Collectors.cs:259-268` | (b) The newest persistence entries may be missing | Replay the logs, or flag a "dirty hive" gap when the header sequence numbers differ | P2 | M |
| C11 | Live tasks: unreadable XML is skipped silently, only the first `<Command>` is read, and HKCU autoruns cover the operator only | SHALLOW | `Collectors.cs:171-185` | (a)/(b) Persistence in another user's profile is unseen | Copy `System32\Tasks` as `task_xml` evidence, record read errors, enumerate HKU\<SID> | P2 | S |
| C12 | SRUDB is copied without its logs or recovery; only the network table is parsed | SHALLOW | `Collectors.cs:113-120` | (b) Dirty-DB failures | Copy `sru\*.log`, run `esentutl /r` on the copy, add the App Resource Usage table | P3 | S |
| C13 | The repaired EVTX copy goes to `%TEMP%`, outside the case, and is neither hashed nor registered as derived evidence | SHALLOW | `Dfir.Windows/Parsers/EvtxParser.cs:50-58` | (b) Weak provenance | Write it to `Case/Derived`, hash it, record the transformation | P3 | S |
| C14 | Without admin, the gaps say "run as administrator" (good). Some operational channels fail with a raw error code instead. | REAL, minor gap | `Collectors.cs:31,44,54-57` | (a) Technical text | Map ERROR_ACCESS_DENIED to "necesită administrator" per channel | P3 | S |
| C15 | The self-test leaks temporary case folders: 48 `la_selftest_case_*` folders in %TEMP%. Delete fails on read-only evidence and the exception is swallowed. | REAL bug (minor) | `Dfir.Windows/Investigation/SelfTest.cs:120,138` | — | Clear the read-only attributes before `Directory.Delete` | P3 | S |

**Positives (verified):**
- EVTX is acquired raw.
- The parser detects chunk corruption, truncation and RecordID reuse, and counts malformed records.
- A missing time stays unknown and is never replaced by "now".
- Evidence is copied (never moved), SHA-256 hashed and made read-only, and re-hashed before and after parsing (`InvestigationPipeline.cs:243-263`).
- Custody and audit logs are hash-chained.
- `ProvenanceBinder` rejects findings with no supporting evidence.
- Gaps are reported with a remedy.
- `UNWIRED_COMPONENTS.md` and the REALITY_AUDIT entry "path B partial" are accurate.

### A.2 Detection

**Inventory** (condensed; "wired" means a non-test caller constructs or calls the rule).

| Engine | Rules | Wired? |
|---|---|---|
| Dfir `Correlation`, `Wp11*`, `Wp14*`, `PolicyTimeline`, `Sequence*`, `LiveStateAnalyzer`, `Containment` | 62 rule contracts (`Dfir.Core/Analysis/RuleContracts.cs`) | all yes (`InvestigationPipeline.cs:294-337`) |
| Dfir `AntiForensics` | AF01–AF16 (AF08 and AF14 always Undetermined, `AntiForensics.cs:234,323`) | yes (`:380`) |
| Dfir `DetectionEngine` | SigmaLite: 4 shipped rules; YaraLite: 1 | yes (`:362`). PDF only, not shown in the UI. |
| Legacy `AnalysisEngine` | 11 event rules + 5 registry rules | yes (`MainViewModel.cs:2264-2265`) |
| Legacy "Sigma" `SigmaRuleEngine` | 6 hard-coded C# checks with decorative YAML | yes (`AnalysisEngine.cs:198`) |
| Legacy "YARA" `YaraRuleEngine` | 5 substring rules over event text | yes |
| Legacy Kerberos, LOLBAS, SigmaCorrelation, FileServer, Live | 4 / 13 binaries / ≥3 scenarios / 1 / ~15 | yes (RunAiAnalysis and live) |
| Legacy, dead | `RansomwareDetectionEngine`, `LivingOffTheCloudEngine`, `SysmonCorrelationEngine`, `LateralMovementEngine`, `DnsTunnelingClassifier`, `DpapiLsassAuditor`, `AutomatedRuleGenerator`, `EntropyFeatureExtractor`, `UsbForensicsParser`, `AntiForensicsArtifactsParser` | **no production consumer** |

**Issues.**

| # | Finding | Status | Evidence | Impact (a) / (b) | Fix | Pri | Effort |
|---|---|---|---|---|---|---|---|
| D1 | Legacy brute force raises one **High** alert per single 4625. The Dfir rule raises one Medium per (IP, user) at ≥10. | DUPLICATE | `Infrastructure/Engines/AnalysisEngine.cs:35-46` vs `Dfir.Core/Analysis/Correlation.cs:316-329` | (a) Alert flood, a false "attack". (b) Pages disagree. | Retire it, or delegate to the Dfir rule | P1 | S |
| D2 | The legacy engine drops any Sigma hit whose MITRE ID equals a built-in alert's ID (for example, LSASS hits vanish) | REAL bug | `AnalysisEngine.cs:201` | (b) Lost detections | Deduplicate on (rule, event) | P1 | S |
| D3 | Legacy "Sigma" is a facade: the YAML shown on screen does not drive matching and differs from the C# code | FACADE | `Infrastructure/Engines/SigmaRuleEngine.cs:64-242` | (a) Believes Sigma ran. (b) What ran cannot be audited. | Retire it; use Dfir SigmaLite | P1 | M |
| D4 | Legacy "YARA" is substring matching, one issue per event | FACADE | `Infrastructure/Engines/YaraRuleEngine.cs:31-133` | (b) Misleading label in court | Rename it "keyword IOC" and deduplicate | P2 | S |
| D5 | Legacy severities contradict the Dfir rules: 4720 → High (Dfir Low/Info), 4732 to any group → High, "bypass" anywhere → Critical (this fires on SCCM/Intune `ExecutionPolicy Bypass`), VSS → Critical (Dfir Medium) | DUPLICATE | `AnalysisEngine.cs:63-128`; `Core/Services/SigmaCorrelationEngine.cs:58-75` vs `Wp11Rules.Accounts.cs:49-106`, `Wp11Rules.Vss.cs:48` | (a) Contradictory red alerts | Make the legacy engine a thin adapter over the Dfir rules | P1 | M |
| D6 | LOLBAS: **High** on every rundll32/msiexec/wscript/csc start, **Critical** when "http" appears | SHALLOW | `Core/Services/LolbasEngine.cs:48-70` | (a) Constant red. (b) Noise. | Require suspicious arguments, parent or path; add an allowlist | P1 | S |
| D7 | SigmaCorrelation "brute force succeeded" = any 5 failures plus **any** 4624 within 5 min, with no user/IP join, rated Critical | SHALLOW | `SigmaCorrelationEngine.cs:32-55` | (a) False Critical | Join on TargetUserName + IpAddress; exclude service logons | P1 | S |
| D8 | **LSASS dump: missing in Dfir.** No Sysmon 10, no 4656/4663 on lsass, no `comsvcs MiniDump`/`procdump -ma` rule. The legacy rule fires on any mention of lsass. | MISSING / SHALLOW | Sysmon IDs used in `Dfir.Core/Analysis/*`: 1, 4, 11, 16, 22 only; `SigmaRuleEngine.cs:232-240` | (b) The top credential-theft step is absent from the case report | New CRED-LSASS-ACCESS rule: Sysmon 10 with TargetImage lsass, GrantedAccess 0x1010/0x1410/0x1FFFFF, an AV/EDR source allowlist, and a command-line regex | P1 | M |
| D9 | **RunMRU / ClickFix**: no parser and no rule | MISSING | `Core/Services/Details/ItemMeaning.cs` (text only) | (b) A top 2025-26 initial-access vector is invisible | Parse NTUSER RunMRU; flag powershell/mshta/curl/`-enc`/URL entries; correlate with 4104 | P1 | M |
| D10 | **BYOVD**: only 7045 from a user-writable path. CodeIntegrity 3033/3077 is not collected, Sysmon 6 is unused, and there is no vulnerable-driver list. | MISSING | `Correlation.cs:124-134`; `Collectors.cs:14-29` | (b) EDR-killer drivers are missed | Collect CodeIntegrity; rule on kernel-driver 7045 + Sysmon 6 + a hash list (LOLDrivers) | P1 | M |
| D11 | Defender tamper misses 5013, 5007 changes other than Exclusions, `Set-MpPreference -Exclusion*`, and the Exclusions registry key | SHALLOW | `Correlation.cs:74-75, 344` | (b) Quiet exclusions | Extend the event IDs and values; parse `Windows Defender\Exclusions` | P1 | S |
| D12 | **Password spray** is not detected. Dfir groups by (IP, user) with no time window. The legacy live engine counts 5 failures globally and calls it "spray". | MISSING / SHALLOW | `Correlation.cs:316-317`; `Core/Services/Network/LiveSecurityMonitoringEngine.cs:164-178, 346-354` | (a) Mislabeled alerts. (b) Spray is missed. | CRED-SPRAY: ≥N distinct users per IP within T (T1110.003); add a window to brute force | P1 | S |
| D13 | **AMSI bypass** is missing. PS-SUSPICIOUS has 8 keywords, misses `iex (`, ignores the 4104 Warning auto-flag, and gives one aggregated finding. | MISSING / SHALLOW | `Correlation.cs:343-356` | (b) Obfuscated PowerShell is under-reported | Data-driven weighted keyword list (AmsiUtils, amsiInitFailed, AmsiScanBuffer, Reflection.Assembly, VirtualAlloc, `-w hidden`); one finding per script block; use Level=3 | P1 | S |
| D14 | **WMI persistence**: WMI-Activity is collected, but 5861 and Sysmon 19-21 are unused | MISSING | `Collectors.cs:20` | (b) Subscription persistence is invisible | PERSIST-WMI rule | P1 | S |
| D15 | **RMM catalog**: 8 tools. Atera, NetSupport, LogMeIn/GoTo, NinjaOne, Action1, SimpleHelp, Level, Kaseya, N-able, Zoho Assist and Tactical RMM are absent. An unapproved RMM that ran is only Medium. | SHALLOW | `Dfir.Core/Analysis/Data/remote_access_tools.json`; `Wp11Rules.Tools.cs:82` | (b) Ransomware-affiliate tooling is missed | Extend the JSON; raise executed + unapproved to High | P1 | S |
| D16 | **Exfiltration tools**: rclone/WinSCP appear only inside one sequence. NET-USERPATH-UPLOAD needs a user path (rclone in Program Files is missed). The rclone/MEGAsync engine is dead. | SHALLOW | `Data/sequence_rules.json`; `Correlation.cs:293`; `Core/Services/LivingOffTheCloudEngine.cs` | (b) Cloud exfiltration is invisible | Standalone EXFIL-TOOL rule (rclone/restic/megacmd/winscp/curl to cloud domains) | P1 | M |
| D17 | **Kerberoasting / AS-REP** exist only in the legacy engine: substring "0x17" over the English message, DC detected by name, manual button only | SHALLOW / MISSING | `Core/Services/KerberosAdAttackEngine.cs:42-83, 142-152` | (b) False positives; fails on localized messages | Read EventData (TicketEncryptionType, PreAuthType) and add a volume threshold; port to Dfir | P2 | M |
| D18 | **Ransomware mass rename**: nothing in Dfir; the legacy engine matches literal `.locked`/`.crypto` only | MISSING | `Core/Services/FileServerAuditEngine.cs:25-26` | (a)/(b) The impact phase is not shown | USN/4663 rename-rate rule per process/directory | P2 | M |
| D19 | wbadmin/bcdedit inhibit-recovery variants are missing from the Dfir VSS rule | SHALLOW | `Data/rule_lists.json` | (b) T1490 variants | Add regexes | P2 | S |
| D20 | **USB malware**: no autorun.inf rule and no "executed from a removable drive" rule outside sequences | SHALLOW | `Wp14Rules.Media.cs`; `SequenceRules.Portable.cs` | (b) USB-borne malware is missed | EXEC-REMOVABLE (Prefetch/BAM/4688 path on a removable letter; LNK target) | P2 | M |
| D21 | The shipped SigmaLite rules rate every 1102/104 **High**, while LOG-TAMPER rates planned maintenance **Info**. Both appear in the **same PDF**. | DUPLICATE | `Dfir.Core/Rules/sigma/win_security_audit_log_cleared.yml`, `win_system_eventlog_cleared.yml`; `Correlation.cs:487-514` | (a) A contradiction in a legal report | Drop those 2 rules, or carry the LogClearAssessment level | P1 | S |
| D22 | RecordID gap: AF04 says "Detected" for a gap of 2; LOG-GAP needs >50. Same report. | DUPLICATE | `AntiForensics.cs:137` vs `Correlation.cs:105` | (a) Contradiction | One threshold that accounts for filtered or rolled logs | P2 | S |
| D23 | PERSIST-TASK: the TaskScheduler 106 branch never fires (106 has no TaskContent), and `.js` also matches `.json` | REAL bug | `Correlation.cs:135-136` | (b) Missed tasks and false positives | 106/200/129 + task XML; match extensions on paths | P2 | S |
| D24 | Wrong MITRE mappings: every MEDIA-* status, including AUTHORIZED, has T1052.001; NET-LOLBIN-TRAFFIC → T1127.001; wscript → T1218; T1052.001 labelled "BadUSB/HID" (HID is T1200); combined IDs ("T1068 / T1543.003"); Office→shell → T1505.003; the RunAiAnalysis fallback `?? "T1027"`/`"T1059"` invents a technique | wrong MITRE | `Wp14Rules.Media.cs:230`; `Correlation.cs:262,310`; `MainViewModel.cs:2403, 2571, 2586, 3248`; `LiveSecurityMonitoringEngine.cs:192,225,242`; `LolbasEngine.cs:24,87` | (a) An authorized stick reads as exfiltration. (b) Heatmap and reports are wrong. | One validated technique per finding, "none" when unknown | P2 | S |
| D25 | Live engine: the literal word "phishing" → Critical; "wevtutil" + "cl" substring = log clear; the first match wins | SHALLOW | `LiveSecurityMonitoringEngine.cs:33,130` | (a) False Critical | Remove test keywords; parse arguments | P2 | S |
| D26 | SigmaLite rejects `logsource.category`, so SigmaHQ process_creation rules cannot load | SHALLOW | `Dfir.Core/Detection/SigmaLite.cs:69` | (b) No extensible rule base | Map categories to channel+EID; ship a curated pack | P2 | M |
| D27 | Dfir `DetectionEngine` hits are shown only in the PDF, not on screen | SHALLOW | `InvestigationReportPdf.cs:135-139` | (b) Hits missed on screen | Add a Detections tab | P3 | S |
| D28 | The same 7045 can produce 3–4 findings (PERSIST-SERVICE-USERPATH, REMOTE-PSEXEC, REMOTE-TOOL-PRESENT, a Sigma hit) | DUPLICATE (minor) | `Correlation.cs:124`; `Wp11Rules.Remote.cs:146`; `Wp11Rules.Tools.cs:50` | (a) Inflated counts | Link findings by evidence reference | P3 | M |
| D29 | 10 legacy detection classes are dead | DEAD | inventory above | Maintenance; implies coverage that does not exist | Delete, or wire through Dfir | P3 | S |

**Threat checklist.**

| Threat | Dfir | Legacy |
|---|---|---|
| RunMRU / ClickFix | MISSING | MISSING |
| LSASS dump | MISSING | SHALLOW |
| BYOVD | MISSING (user-path only) | SHALLOW |
| Defender tamper / exclusions | SHALLOW | SHALLOW |
| Password spray | MISSING | SHALLOW, mislabeled |
| Brute force | REAL, no window | DUPLICATE, floods |
| Kerberoasting / AS-REP | MISSING | SHALLOW |
| RMM | REAL, 8 tools | — |
| Exfiltration tools | SHALLOW | dead |
| Ransomware rename | MISSING | SHALLOW |
| vssadmin / wbadmin / bcdedit | REAL / MISSING / MISSING | SHALLOW |
| USB malware | SHALLOW | — |
| WMI persistence | MISSING | — |
| Scheduled tasks | REAL (4698), 106 dead | MISSING |
| Services 7045/4697 | REAL | SHALLOW |
| Log clearing | REAL (contradicted by SigmaLite) | REAL-ish |
| PowerShell 4104 | SHALLOW | SHALLOW |
| AMSI bypass | MISSING | MISSING |

### A.3 Alerting

**What exists.**

- **Event-log live source:** `LogAnalyzer.Connectors/Network/LiveEventLogWatcherService.cs`.
  - It uses `EventLogWatcher` subscriptions on Security (all events), Sysmon, PowerShell Operational, Windows PowerShell and System. System is filtered to event 7045 only (`:58`).
  - The unclassified edition registers it (`Edition/Unclassified/UnclassifiedComposition.cs:72`). The classified edition registers `NoLiveEventSourceFactory` (`LogAnalyzer.App.Classified/Edition/ClassifiedComposition.cs:45`).
  - It starts automatically only in network mode (`MainViewModel.cs:672-673`); otherwise it waits for a manual button (`Views/LiveSocView.xaml:19`).
- **Media live source:** `DeviceWatcher` (WMI `__InstanceCreation/DeletionEvent WITHIN 2` on `Win32_PnPEntity`, plus `Win32_VolumeChangeEvent`). It starts 3 s after the window loads.
- **Global surfaces visible on every page:**
  - the event-log toast (`MainWindow.xaml:312` → `Views/Controls/LiveAlertToast.xaml`);
  - the media banner (`MainWindow.xaml:215-224`).
- **Missing:** there is no tray icon and no Windows notification.

| # | Finding | Status | Evidence | Impact (a) / (b) | Fix | Pri | Effort |
|---|---|---|---|---|---|---|---|
| L1 | **The live-alert detail is a facade.** `OpenAlertModal` sets `IsCountermeasureModalVisible`, but no XAML binds it (0 matches for "Countermeasure" in `*.xaml`). The toast's "Inspect Playbook" button, Dashboard live-alert selection and auto-open all show nothing. The isolate and block-IoC commands have no visible trigger. | FACADE | `MainViewModel.cs` `OpenAlertModal` (~432-443), `OnSelectedLiveAlertChanged`; `LiveAlertToast.xaml:39`; `DashboardView.xaml:253` | (a) The button does nothing, so the app looks frozen. (b) No playbook or raw event from an alert. | Route to `AlertDetailWindow` (as IssuesView does), or add the overlay | P1 | M |
| L2 | **Live alerts and live events are memory-only** and lost on restart. `KnowledgeBaseService.SaveAlert` has no callers. | MISSING | `Core/Interfaces/IDatabaseService.cs:9-24`; `Core/Services/KnowledgeBaseService.cs:63`; `OnEventReceived` (~1769-1848) | (a) An overnight alert has vanished by the morning. (b) No alert history or evidence trail. | Persist the alert and its source event (SQLCipher) with an audit line; reload on start | P1 | M |
| L3 | **No acknowledgment flow.** The toast auto-hides after 7 s, and nothing records who saw it. The media banner's "Am văzut" is not audited either. | MISSING | toast timer `System.Timers.Timer(7000)` (~1840); `AlertDetailWindow.xaml.cs:34`; `MediaCenterViewModel.cs:184,190` | (a) No proof the alert was handled. (b) No triage state. | New/Seen/Closed states with operator and time in the audit log; an unacknowledged counter in the sidebar | P1 | M |
| L4 | **A media alert can be missed during an inventory.** `OnChange` → `RefreshAsync()` returns at once when `IsBusy`, the device list is stale and no banner appears. The first refresh runs exactly when the watcher starts. | REAL bug *(spot-checked)* | `MediaCenterViewModel.cs:78-80, 86, 167-170` | (a) An unregistered stick inserted at the wrong moment raises no alert. This is a false negative in the core control. | Await or queue the running refresh; assess the device from the WMI instance id | P1 | S |
| L5 | **Burst debounce drops assessments.** Earlier events in a 1.5 s burst are logged unassessed, so of two sticks inserted together only the last is checked. | SHALLOW | `MediaCenterViewModel.cs:163-166` | (a) The second stick is silently accepted | Debounce the refresh, not the events; assess every pending arrival | P1 | S |
| L6 | **The media banner has one slot**, so a new alert overwrites the previous one. A foreign device already present at start never triggers the banner. | SHALLOW / MISSING | `MediaCenterViewModel.cs:75-104, 184` | (a) The first unregistered medium is hidden; a stick present at start goes unnoticed | A list of open media alerts; raise the banner after the first inventory | P2 | S |
| L7 | **A watcher failure is invisible.** `IsLive` is true even if every WMI watcher failed, and only the media page says so. | SHALLOW | `MediaCenterViewModel.cs:141-150`; `Dfir.Windows/Devices/DeviceWatcher.cs:41-44` | (a) Believes the station is supervised when it is not | Global "supraveghere oprită/eșuată" indicator | P2 | S |
| L8 | **The live rule for System 104 can never fire**, because the System subscription is filtered to 7045 | REAL bug *(spot-checked)* | `LiveEventLogWatcherService.cs:58` vs `LiveSecurityMonitoringEngine.cs:130` | (b) A System-log clear is missed live | `EventID=7045 or EventID=104` | P1 | S |
| L9 | **Live events carry no `XmlData`, channel or RecordId.** The log-clear subject is therefore always unknown (maintenance allow-listing never applies), and detail sheets lose field-level explanations. | SHALLOW | `LiveEventLogWatcherService.cs:108-117`; `Core/Services/Details/DetailSheet.cs:89-102` | (a) Every live log clear looks unapproved. (b) Live events are not usable as evidence. | Set `XmlData = rec.ToXml()`, LogName and RecordId | P1 | S |
| L10 | **No dedup and no rate limit.** After 5 failures, every 4625 is a new High alert. The counter is global across hosts and uses a fixed 2-minute reset. Every 7045 raises an alert. `LiveAlerts` is unbounded, and each match plays a sound. | REAL (fatigue) | `LiveSecurityMonitoringEngine.cs:15, 148-180, 346-354` | (a) Sound and popups every few seconds. (b) Floods and mixed-host counts. | Dedup by (rule, host, account) per window; per-host sliding counters; cap the list; sound for High+ only | P1 | M |
| L11 | **UI-thread flood.** Every Security event (query `*`) is marshalled with a synchronous `Dispatcher.Invoke` and inserted into three UI collections. | REAL (perf) | `OnEventReceived` (~1769-1802); `LiveEventLogWatcherService.cs:48` | (a) The app hangs on a busy host or DC. (b) Delayed events. | ConcurrentQueue + a batched `BeginInvoke` every ~250 ms; pre-filter IDs in the XPath query | P1 | M |
| L12 | **Live rows are mixed into the case `Events`/`TimelineItems`** and counters but never saved, so the counts do not match the store | REAL | `OnEventReceived` (~1773-1802) | (a) Counts do not match the report. (b) Integrity confusion. | Keep live data on the Live page, or persist it first | P2 | S |
| L13 | **Clicking a stream row fabricates an alert** with `MitreTechniqueId = "T1059 (Event Log Triage)"` | REAL | `OnSelectedLiveStreamingEventChanged` (~407-419) | (a) A misleading attack hint on a logon event | Open `GenericDetailWindow` for the event; never invent a technique | P2 | S |
| L14 | **Subscription failures go only to the status text** (bare `catch {}` in `DisposeWatcher`); a missing Sysmon channel is not reported as a coverage gap | SHALLOW | `LiveEventLogWatcherService.cs:85-88, 103-106, 154` | (b) Silent coverage gap | Show "canale monitorizate: x/5" | P2 | S |
| L15 | **The MITRE matrix live highlight uses `Equals`**, so composite IDs never highlight | SHALLOW | `MainViewModel.cs` (~1816-1825) | (b) The matrix under-reports | Split IDs (and fix D24) | P3 | S |

### A.5 Explanation and terminology

**Positive coverage:**
- Every Dfir `RuleId` has a `RuleContracts` entry.
- Every MITRE ID used in C# has an `ItemMeaning` text.
- Every station check C01…P* has a `CheckGuide`.

| # | Finding | Status | Evidence | Impact (a) / (b) | Fix | Pri | Effort |
|---|---|---|---|---|---|---|---|
| E1 | **Event IDs that are analysed but not explained:** Sysmon 7/8/10/15; Security 4656, 4662, 4663, 4742, 4756, 4800, 4801, 5136, 5137, 5141, 5379; 20001/20003; Entra codes 50053/50074/50125/53003 | MISSING | `Core/Services/Details/EventMeaning.cs:42-112` vs `Core/Services/{Kerberos,FileServer,EmployeeActivity,StandaloneSam,AzureAd,AdAttributeDelta}*Engine.cs` | (a) "nu are încă o explicație" on the very events behind DCSync, LSASS and USB findings | Add the entries, plus a test that every event ID used by a production engine has a meaning | P1 | M |
| E2 | **Row types with no ItemMeaning kind:** `ControlCheck` (DM*/EM*), `AntiForensicCheck` (AF01-16), `SuspectProcess`, `ProcessIncident`, `MailMessage`, `UserActivity`, `LiveDeviceEntry`, `DeviceAssessment`. The properties `Detail`, `Reason`, `Recommendation` and `Attack` are not mapped. | MISSING | `Core/Services/Details/ItemMeaning.cs:19-49, 74-75`; `App.xaml.cs:19-28` | (a) Domain and anti-forensics screens are unexplained | Add the kinds and property mappings; route `ControlCheck` to `CheckGuides.Explain` | P1 | S |
| E3 | **Domain/mail checks have no guide:** DM01-05, DM21-23, DM25, DM30-31 and EM01-05 have no `CheckGuide`, and the Domain view never calls `Explain` | MISSING | `Dfir.Windows/Audit/CheckGuide.cs:45-212`; `Dfir.Windows/Domain/DomainEvaluator.cs:23`; `Domain/MailInvestigation.cs:105` | (a) Kerberoasting and mail bursts are shown with no "ce verificăm / de ce contează" | Write the guides; reuse the Station Control detail panel; extend the sync test | P1 | M |
| E4 | **A finding opened by double-click** (`GenericDetailWindow`) ignores `RecommendedNextSteps`, `AlternativeExplanations` and `MissingEvidence`; `Finding` is not a kind | SHALLOW | `Dfir.Core/Model/Analysis.cs:74-77`; `ItemMeaning.cs:47-48` | (a) Loses "what to do / other explanations" | Map Finding to the FindingCard content | P2 | S |
| E5 | **False persistence claim:** `ForRegistry` adds "Cheie de pornire automată … T1547.001" to any path containing `\run`, including **RunMRU**. Run, Services and UserAssist keys are explained twice. | REAL bug | `Core/Services/Details/DetailSheet.cs:198-201` vs `ItemMeaning.cs:272-285` | (a) A command-history key is presented as malware persistence | Delete lines 198-201, or anchor the regex | P1 | S |
| E6 | **Severity vocabulary:** at least four scales are in use (Critică/Ridicată/Medie; CRITIC/RIDICAT/MEDIU; "ALERTĂ MARE"/"NOTIFICARE"; raw "Critical/High"). Investigation uses the converter in one grid only. | REAL | `Dfir.Core/Presentation/SeverityLabels.cs:8-15`; `ItemMeaning.cs:91-99`; `DashboardView.xaml:233,255`; `InvestigationView.xaml:146` vs `:185,213`; `LiveAlertToast.xaml:18` | (a) Is "Mare" the same as "Ridicată"? (b) Inconsistent report wording. | One severity converter everywhere; engines emit the enum | P1 | S |
| E7 | **"Registru" means both** the Windows registry and the media/user registers | REAL | `MainWindow.xaml:160` vs `:181-182`; `MediaCenterViewModel.cs:51,103` | (a) Controllers look in the wrong place | Always write "registrul de medii" / "registrul Windows"; add a glossary term | P2 | S |
| E8 | **Raw statuses:** `ControlStatus` shows as "DeVerificat"/"Nedeterminat"; the workbench shows "MATCHED (n)"/"Active" | SHALLOW | `StationControlView.xaml:164`; `MainViewModel.cs` `InitializeSigmaRules` | (a) Odd, English labels | Status converter | P2 | S |
| E9 | **The Live page uses English and a false claim:** "START LIVE STREAM", "Clear Stream", "Auto-refreshing < 50ms" (false, see L11) | FACADE | `Views/LiveSocView.xaml:19,33,55,56` | (a) Jargon. (b) Misleading. | Romanian labels; remove the claim | P2 | S |
| E10 | **The detail sheet shows developer names:** the C# class name as header (`Kind = type.Name`), raw property names, "Event ID", "Hive", "Export PDF" | SHALLOW | `DetailSheet.cs:84, 188-190, 229, 237`; `GenericDetailWindow.xaml:19,78` | (a) Developer names on the main explanation surface | Display names from Kinds; Romanian field labels | P2 | M |
| E11 | **The alert surfaces are in English** and contain demo fallbacks ("Live EDR Threat Detected", "Process injection and C2 beaconing…", "Forensic Security Alert Triage") | REAL | `LiveAlertToast.xaml:18-39`; `AlertDetailWindow.xaml:4,52,64` | (a) English on the main alert UX, with fake placeholder text | Translate; neutral or empty fallbacks | P1 | S |
| E12 | **The Dashboard, Alerts and Events screens are partly English:** "Prioritized Threat Queue", "Alert Title", "Playbook & Action", "Compile & Evaluate Rules", "Provider Source", "Host Machine"… | REAL | `DashboardView.xaml:42,228-258`; `AlertsAndRulesView.xaml:14-45`; `EventExplorerView.xaml:36-118` | (a) Mixed language | Translation pass through the `Term` extension, plus a XAML lint test for English Header/Content | P2 | M |
| E13 | **Other screens and strings are English:** window title "… Threat Triage & Incident Response Command Center", splash, "Export Evidence Package", "Run AI & Heuristic Evaluation", the "Live Event" category | REAL | `MainWindow.xaml:7`; `SplashWindow.xaml:4,20`; `ChainOfCustodyView.xaml:16-39`; `RiskAnalysisView.xaml:18,74`; `DataCollectionView.xaml:15,74-79` | (a) Mixed language | Same pass | P3 | M |
| E14 | **Placeholder text shown as data:** the AI page entity fallback "TargetHost", and the MITRE fallbacks (D24) | REAL | `MainViewModel.cs` `RunAiAnalysis` (~2567, 2582) | (a)/(b) Invented values look like evidence | Show "—" / "necunoscut" | P2 | S |


### A.4 Identification and attribution

| # | Finding | Status | Evidence | Impact (a) / (b) | Fix | Pri | Effort |
|---|---|---|---|---|---|---|---|
| I1 | For imported evidence, `CaseInfo.Host` and every `EvidenceItem.Host` are the **examiner's** machine name. The report says "stația {Host}" and the graph has one Host node. Imported EVTX MachineName is never compared with SystemsInScope. No MachineGuid, machine SID or disk serial is recorded. | REAL bug | `InvestigationPipeline.cs:85-88, 354`; `Dfir.Core/Case/CaseWorkspace.cs:178`; `InvestigationReportPdf.cs:43` | (a) The report names the wrong station, which matters for legal value. (b) Multi-host cases collapse. | Separate the examiner host from the source host. Take the source host from EVTX MachineName, SYSTEM ComputerName and SOFTWARE MachineGuid. Warn on a scope mismatch; one Host entity per source. | P1 | M |
| I2 | No logon-session model. LogonId linking exists only in the WinRM rule, with `FirstOrDefault` and no boot bound. ParserCapabilities claims "4688 in the same session (LogonId)". | SHALLOW (overclaim) | `Dfir.Core/Analysis/Wp11Rules.Remote.cs:91`; `ParserCapabilities.cs:46,48` | (a) "Who was logged on when X happened" cannot be answered. (b) No process→session→IP chain. | `LogonSession` builder: LogonId + boot epoch (6005/12), 4624 start, 4634/4647/4779 end, type, source IP, elevation via 4672. Link 4688/4663/5145 to it; mark overlaps as ambiguous. | P1 | M |
| I3 | The timeline user is TargetUserName → SubjectUserName. On 4688 TargetUserName is "-", so the user is "-". With no name, the raw SID is used. | REAL bug | `Dfir.Windows/Parsers/EvtxParser.cs:42,132` | (a) "-" as the user. (b) Wrong actor in pivots. | Skip "-" and S-1-0-0; prefer the Subject fields for 4688/4663/4672; keep the SID in its own field | P2 | S |
| I4 | No offline SID→name resolution (SAM and ProfileList are not parsed). The BAM user is a raw SID. UserAssist/LNK/JumpList/Browser/Prefetch events carry no user. Graph User nodes come only from BAM/UserAssist. | MISSING | `Parsers/ExecutionArtifactParsers.cs:64`; `RegistryPersistenceParsers.cs:109-124`; `Dfir.Core/Graph/EvidenceGraph.cs:139-147` | (a) "Which user" is blank or a SID. (b) No unified identity. | Case-wide identity map (ProfileList, SAM RID, EVTX Sid/Name pairs); derive the file owner from `Users\<name>\` in OriginalPath | P1 | M |
| I5 | USB→user comes only from 6416 SubjectUserName, with machine accounts filtered out. 6416 runs as SYSTEM/COMPUTER$, so the user is almost always empty and register rows never reach "Authorized". MountPoints2 is read live only, for network shares only. ParserCapabilities claims 6416 gives "the account that was logged on". | SHALLOW (overclaim) | `Dfir.Core/Analysis/Wp14Rules.Media.cs:62-69`; `Dfir.Windows/Devices/DeviceInventory.cs:300-323`; `ParserCapabilities.cs:103-104` | (a) "Who used the stick" is never answered. (b) No per-user USB link. | NTUSER MountPoints2 volume GUID ↔ SYSTEM MountedDevices ↔ USBSTOR serial, plus the user from an overlapping interactive session; fix the capability text | P1 | M |
| I6 | MountedDevices `\??\Volume{GUID}` values are discarded. VID/PID is joined by instance leaf (fails without a serial). EMDMgmt/WPD are not read, so LNK volume serials cannot be tied to a device. | SHALLOW | `Parsers/UsbDevicesParser.cs:53-55, 62-72, 88` | (b) No device↔volume↔file chain | Emit VolumeGuid; join via ParentIdPrefix/ContainerID; parse EMDMgmt/WPD | P2 | M |
| I7 | Windows pseudo-serials (second character `&`) are treated as real serials and matched against the media register | REAL bug | `UsbDevicesParser.cs:83`; `Wp14Rules.Media.cs:34-35`; `Dfir.Core/Registers/MediaRegister.cs:111-115` | (a) False "unregistered medium" alerts, or one stick counted as several | Mark them "no serial"; match on VID/PID + label + 1006 serial | P2 | S |
| I8 | "Related events" in detail windows come only from the **current page** (100 rows): `EventCorpus = () => Events.ToList()` | SHALLOW | `MainViewModel.cs:646` (`DetailSheetBuilder.EventCorpus`) | (a) "No related events" when there are some. (b) Incomplete context. | Query the DB around the event time and host instead of the page | P2 | S |

---

## Part B — Why the application is slow, and how to make it faster

### B.1 Measurements (all *measured*, 2026-10-11)

**Machine state matters.** The system disk C: had **245–509 MB free (100% used)** during the run. One benchmark failed with `SQLite Error 13: database or disk is full`. A full disk slows every write, including SQLite WAL, %TEMP% extraction and Defender caches. Free space first, before any profiling is trusted.

**1. Main-window start-up** (`LogAnalyzer.App.Tests/MainWindowStartupTests`, real services, real database).

| Build | Run | services + DB | MainViewModel | MainWindow |
|---|---|---|---|---|
| Debug (before `41e0f2ad3`) | cold, right after build | 2084 ms | 537 ms | 217 ms |
| Debug (before `41e0f2ad3`) | warm | 43 ms | 481 ms | 197 ms |
| Release (21:29 build) | cold, right after build | 2325 ms | 817 ms | 183 ms |
| Release (21:29 build) | warm, ×4 | 48–56 ms | 447–530 ms | 191–212 ms |
| Debug (after `41e0f2ad3`, WMI asynchronous) | warm, ×3 | 46 ms | 219–233 ms | 186–195 ms |
| Release (rebuilt 21:4x, after `41e0f2ad3`) | cold, right after build | 1537 ms | 608 ms | 429 ms |
| Release (rebuilt 21:4x, after `41e0f2ad3`) | warm, ×5 | 44–86 ms | 214–456 ms (steady about 215–250 ms) | 186–364 ms (steady about 190 ms) |
| Release, `DOTNET_ReadyToRun=0` (framework JIT-compiled) | warm | 54 ms | 346 ms | 330 ms |
| Release, `DOTNET_TieredCompilation=0` | warm | 85 ms | 350 ms | 199 ms |
| Release, `DOTNET_TieredPGO=0` / `TC_QuickJitForLoops=1` | warm (old build) | ≈ base | ≈ base (−5…−10%) | ≈ base |

How to read the start-up table:

- **Debug and Release are about equal** for start-up once warm. Most of the time is JIT, I/O and WPF, not optimised code.
- **The cold "services + DB" cost** (1.5–2.3 s) appears only on the first run after new binaries are written. Every run after that is about 45 ms. This is consistent with first-touch disk I/O and an antivirus scan of new DLLs and the native SQLCipher library. An installed build pays it once per update, or on every launch if single-file extraction to %TEMP% is re-scanned (needs lab).
- **ReadyToRun matters.** Turning off ReadyToRun *for the framework* adds about 130 ms to the view-model and about 140 ms to the window. The application's own assemblies are **not** ReadyToRun today (`PublishReadyToRun=false` in `LogAnalyzer.App/Properties/PublishProfiles/win-x64-singlefile.pubxml`), so they pay this JIT cost on every start.
- **Tiered-compilation knobs bring nothing.** Leave them alone.

**2. Inside the MainViewModel constructor.** These are the temporary timing lines of the other session (`%TEMP%\la_ctor.txt`, warm). The traced steps sum to about 80–150 ms:

- Auth 23–36 ms
- language + Home 16–29 ms
- feature VMs 9–16 ms
- profiles 11–16 ms
- KB categories 9–14 ms

One run showed "ProcessTree+Sigma 258 ms". That is first-touch loading of System.Management inside the `async void` before its first `await`; other runs showed 1–6 ms.

**3. Field and property initialisers**, timed one by one in a scratch harness by reflection over `MainViewModel` fields (Release).

| Item | Cold | Warm |
|---|---|---|
| All 50 eager fields together (the "36 engines" etc.) | — | 130 ms |
| …of which **`StationControlViewModel`** (`MainViewModel.cs:134`) | 110 ms | 108 ms |
| …each of the other engines (`AttackStorylineEngine`, `LolbasEngine`, `Nis2…`, …) | 0 ms | 0 ms |
| `AnalysisEngine` (Sigma/YARA/anomaly) constructor | 1 ms | 0 ms |
| `KnowledgeBaseService.LoadCategories` (Categories JSON) | 186 ms | 10 ms |

Conclusions:

- The 36 eagerly created engines are **not** a start-up cost; making them lazy would save under 5 ms.
- **`StationControlViewModel` is the cost.** Its constructor calls `LoadPreviousControls()` → `ControlArchive.List`, which fully deserializes **every** previous `control_report.json` (`Dfir.Windows/Audit/ControlGuide.cs:61-80`). That cost grows with the number of controls kept, and it is paid even if the page is never opened.

**4. The post-start-up UI-thread block: the main cause of the "slow" feeling.**

The constructor schedules `Task.Run(() => Application.Current.Dispatcher.Invoke(() => { UpdateDatabaseSize; ReloadEvtx/Registry/Timeline; ReloadDashboardStats; RunAiAnalysis; … }))` (`MainViewModel.cs:660-676`). This only *defers* the work; it then runs entirely **on the UI thread**, right after the window appears. The comment beside it ("< 50ms") is not true for any non-trivial database. The start-up test cannot see this, because `Application.Current` is null in tests.

The same calls were timed on the real database, the same calls on a synthetic 50k-event encrypted database, and with the engines `RunAiAnalysis` starts with (Release, scratch harness):

| Step (the UI thread is blocked) | Real DB (7,629 events, 990 MB file) | 50,000 events |
|---|---|---|
| Events count + first page | 26–67 ms | 20 ms |
| Timeline count + page (no index on `Timestamp`: full scan + temp B-tree) | 41 ms | 158 ms |
| Dashboard: count + `COUNT(DISTINCT MachineName)` (full scan) | 87–126 ms | 303 ms |
| `RunAiAnalysis`: `GetEvents(50000)` incl. XmlData | 144–151 ms | 521 ms |
| anomaly + "YARA" over those events | 69–77 ms | 246 ms |
| **Total before the UI responds** | **≈ 450–515 ms** | **≈ 1,250 ms** |
| (the rest of `RunAiAnalysis`: Kerberos, LOLBAS, UBA, SAM, FileServer… on the same thread) | not timed | not timed, adds to the total |
| Search keystroke (`LIKE '%x%'` count + page, 400 ms debounce, UI thread) | 88 ms | 638 ms |
| Deep page (`OFFSET 49,900`) | 4 ms | 25 ms |
| Import write rate (SaveEvents + SaveTimeline) | — | 38k events/s (every event is written twice: Events **and** Timeline) |

**5. The database file.**

- `PRAGMA page_count` = 241,627 and `freelist_count` = 238,073, so **98.5% of the 990 MB file is free pages**. `ClearDatabase` (`Infrastructure/Services/DatabaseService.cs:90-96`) does `DELETE` without `VACUUM`, and there is no `auto_vacuum`.
- `Pooling=false` (`DatabaseService.cs:662-667`): every query opens a connection, applies the key and 5 PRAGMAs, and loses the 64 MB page cache. The first count took 41 ms; the same count on a warmed connection took 2 ms.
- Indexes exist only on `Events(EventId)`, `Events(TimeCreated)` and `RegistryArtifacts(KeyPath)`. There is no index on `Timeline(Timestamp)`, `Events(MachineName)` or `RegistryArtifacts(Category)`, and no FTS.

**6. Build output.**

- `bin/Release` holds 23 managed DLLs (about 5 MB of application code) and a 113 MB `runtimes/` folder with native SQLCipher/Skia for **every** RID: linux-*, musl, arm, wasm. That is 133 MB in total, because no `RuntimeIdentifier` is set for normal builds.
- The publish profile is single-file with `IncludeNativeLibrariesForSelfExtract=true`, so natives are extracted to %TEMP% at run time.

**7. XAML virtualization.**

- `DashboardView.xaml:5` wraps the whole page in a `ScrollViewer`. The `IssuesView` and `LiveAlerts` DataGrids inside it (`:231, :253`) have only `MinHeight`. They get infinite height, so row virtualization is **off** and every alert row is realised.
- This interacts badly with D1 (one alert per 4625) and L10 (unbounded `LiveAlerts`).
- `IncidentTimelineView.xaml:37-38` has the same pattern: an ItemsControl inside a ScrollViewer, so the `VirtualizingStackPanel` settings have no effect. It shows 100 rows per page, so the impact is moderate.
- The global DataGrid style file `LogAnalyzer.App/Themes/Components/DataGrids.xaml` is **empty**, so virtualization defaults vary per grid. The Fluent theme (`App.xaml:10`) is loaded globally; its cost was not measured (see the lab list).

**8. Dispatcher and collection patterns.**

- Live monitoring uses a synchronous `Dispatcher.Invoke` per event (L11).
- Import uses a synchronous `Invoke` per file for progress (acceptable).
- `DetectedIssues.Add` is called in a loop (`MainViewModel.cs` import stage 3), raising one `CollectionChanged` and one filter pass per item, although `RangeObservableCollection` exists (`Services/RangeObservableCollection.cs`).
- Each evidence file is SHA-256 hashed **twice** on import: by `EvidenceIntakeService.Import` → `SecurePathService.ComputeSha256`, then again inline (`MainViewModel.cs` ~2019-2027). That doubles read I/O on large images.

### B.2 Ranked changes

The expected gain is measured where marked; otherwise it is estimated with the reasoning given.

| Rank | Change | Expected gain | Risk | Effort |
|---|---|---|---|---|
| 1 | **Run the post-start-up load off the UI thread.** Query on a background task and assign the collections with a single `Dispatcher.BeginInvoke`. **Do not run `RunAiAnalysis` at start-up**: run it when the AI/Risk page is opened, or after import, on a background thread. | Removes a **0.45 s (today) → 1.25 s (50k events)** UI freeze after the window appears *(measured)*; grows linearly with the case | Low (data binding is unchanged; guard against re-entrancy) | S |
| 2 | **`VACUUM` after `ClearDatabase`** (or `PRAGMA auto_vacuum=INCREMENTAL` + `incremental_vacuum`) and a one-off VACUUM now | Reclaims **≈ 930 MB** on this machine *(measured freelist)*; smaller backups and copies | Low (VACUUM on SQLCipher needs about 2× space briefly; run it with a progress note) | S |
| 3 | **Index `Timeline(Timestamp)` and `Events(MachineName)`**; replace `COUNT(DISTINCT MachineName)` with a covering-index scan or a maintained hosts table | Timeline page 158 → about 20 ms; dashboard 303 → under 50 ms at 50k (estimate from the indexed Events page at 20 ms) | Low | S |
| 4 | **One long-lived connection** (or `Pooling=true`) instead of open + key + PRAGMAs per query | 5–40 ms per query → about 1–2 ms (measured: 41 ms cold vs 2 ms warmed); dozens of queries per screen | Low–medium (thread-safety: one connection per thread, or a lock) | S |
| 5 | **Lazy `StationControlViewModel`** (create on first open of the page), and `ControlArchive.List` reading only header fields asynchronously | −110 ms MainViewModel today *(measured)*; grows with the number of controls | Low | S |
| 6 | **Load `KnowledgeBaseService.LoadCategories` on a background task** (needed only when an event is shown) | −186 ms on cold start *(measured)*, −10 ms warm | Low | S |
| 7 | **`PublishReadyToRun=true`** in the release profile (optionally `TieredCompilation` defaults) | Estimated −100 to −250 ms cold start (the framework-ReadyToRun experiment shows about 130 ms per stage when JIT is needed); larger binary | Low | S |
| 8 | **Full-text search** (SQLite FTS5 over Message, Description, KeyPath/ValueData) instead of `LIKE '%x%'`, run off the UI thread | Search keystroke 638 ms → under 20 ms at 50k events (estimate; FTS index lookup) | Medium (index build time on import; FTS5 availability in the SQLCipher bundle needs verifying) | M |
| 9 | **Live events batched**: ConcurrentQueue + `DispatcherTimer` 250 ms flush, XPath pre-filter by event ID (L11) | Removes per-event UI blocking; on a busy host or DC, from hundreds of `Invoke` calls per second to 4 batches per second | Low | M |
| 10 | **Dashboard grids virtualised**: give the alert grids a bounded height (row `*` in a Grid, not inside the page ScrollViewer), or move the page ScrollViewer inside the sections | With 5k legacy alerts, from 5k realised rows to about 30 (estimate). Combine with D1/L10 dedup. | Low | S |
| 11 | **Add `DetectedIssues` with `AddRange`** (`RangeObservableCollection`) and refresh `IssuesView` once | One change notification instead of N filter passes | Low | S |
| 12 | **Hash each evidence file once**: reuse the `EvidenceReceipt.Sha256` returned by `Import` | Halves the hashing I/O on import *(code-verified double hash)* | Low | S |
| 13 | **Stop writing every event twice** (Events and Timeline): make Timeline a view, or keep only the event id | About 40% less write volume and file size on import (estimate: Timeline rows duplicate Message) | Medium (Timeline consumers must change) | M |
| 14 | **Set `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`** (or `SatelliteResourceLanguages` + RID-specific restore) for dev/test builds | Output 133 MB → about 20 MB; faster copy, fewer files for the antivirus to scan | Low | S |
| 15 | **Store times as UTC INTEGER ticks** (fixes C5) | Correct filters and cheaper comparisons than text sorting | Medium (migration) | M |
| 16 | **IncidentTimelineView**: use a `ListBox`/`ItemsControl` with its own ScrollViewer template, and no outer ScrollViewer | Real virtualization; small gain at 100 rows | Low | S |
| 17 | **Path A EVTX streaming** (C9): an `EventLogPropertySelector` instead of `ToXml()` + `XDocument`, spilling to SQLite | Estimated 2–4× faster parsing and bounded memory on multi-GB logs (needs lab) | Medium | M |
| 18 | **Natives beside the exe** instead of single-file self-extract (or a stable extraction dir) | Avoids extraction and re-scan on first run (needs lab) | Low | S |
| 19 | **Remove the temporary timing lines** (`__T(`/`__sw`, `File.AppendAllText` per step on the UI thread) before shipping | A few ms; removes disk writes to %TEMP% | None | S |
| 20 | Do **not** spend time on lazy-creating the 36 engines or on TieredPGO / QuickJit knobs | 0–10 ms *(measured)* | — | — |

---

## Combined top-20 action plan (value ÷ effort)

| # | Action | Refs | Pri | Effort |
|---|---|---|---|---|
| 1 | Run the post-start-up load and `RunAiAnalysis` off the UI thread; `RunAiAnalysis` on demand only | B2-1 | P1 | S |
| 2 | VACUUM / auto_vacuum; Timeline and MachineName indexes; one pooled connection | B2-2,3,4 | P1 | S |
| 3 | Fix the alert facades and silent misses: System 104 subscription, live `XmlData`, media `IsBusy` race and burst debounce | L4, L5, L8, L9 | P1 | S |
| 4 | Make the live-alert detail real (route to `AlertDetailWindow`); add acknowledge + audit; persist alerts | L1, L2, L3 | P1 | M |
| 5 | Dedup and rate-limit live alerts; batch Dispatcher updates; virtualise the dashboard grids | L10, L11, B2-9,10 | P1 | M |
| 6 | Turn the legacy engine into an adapter over the Dfir rules (or remove it): brute force per 4625, "bypass" Critical, LOLBAS High, the SigmaCorrelation join, the MITRE-dedup bug | D1, D2, D5, D6, D7 | P1 | M |
| 7 | Route all imports through the Dfir pipeline, or at minimum report the 20k/50k caps and the unparsed file types | C3, C4, D3 | P1 | S |
| 8 | Remove the contradicting SigmaLite log-clear rules; one RecordID-gap threshold | D21, D22 | P1 | S |
| 9 | New P1 rules, S-effort batch: WMI 5861, password spray, AMSI and PowerShell keyword list, Defender 5013/5007 values, RMM catalog extension | D11–D15 | P1 | S |
| 10 | New P1 rules, M-effort batch: LSASS access, RunMRU/ClickFix parser + rule, BYOVD (collect CodeIntegrity), EXFIL-TOOL | D8, D9, D10, D16 | P1 | M |
| 11 | Separate the source host from the examiner host in case, report and graph | I1 | P1 | M |
| 12 | Logon-session builder plus a case-wide SID→name identity map; fix the 4688 user field | I2, I3, I4 | P1 | M |
| 13 | USB→user through MountPoints2 + MountedDevices + overlapping session; treat pseudo-serials as "no serial"; correct the ParserCapabilities claims | I5, I7 | P1 | M |
| 14 | Live collector: per-user artifacts, task XML, setupapi, SOFTWARE/SAM | C1 | P1 | M |
| 15 | Lazy `StationControlViewModel`, background KB load, ReadyToRun publish | B2-5,6,7 | P2 | S |
| 16 | Explanation gaps: EventMeaning IDs, ItemMeaning kinds and properties, DM/EM guides, RunMRU false-persistence text | E1, E2, E3, E5 | P1 | S–M |
| 17 | One severity converter; translate the alert toast and alert window; drop demo fallbacks and invented MITRE/"TargetHost" values | E6, E11, E14, D24 | P1 | S |
| 18 | Store UTC times in the legacy DB; source time zone for USN; UTC with seconds in the PDF | C5, C6, C7 | P2 | S |
| 19 | FTS5 search off the UI thread; hash evidence once; AddRange for issues | B2-8,11,12 | P2 | M |
| 20 | New parsers: setupapi → ConsoleHost_history → NTUSER RunMRU/MountPoints2 → SAM → MFT; "present but not analysed" on the intake screen | C2 | P1 | L |

---

## Uncertain / needs lab

1. **Antivirus cost.** Defender/antivirus scan cost on first launch of the single-file build: how much of the 1.5–2.3 s cold "services + DB" comes from scanning the extracted native SQLCipher/Skia libraries in %TEMP%, and does it repeat on every launch? Measure with Process Monitor on a clean VM, with and without `IncludeNativeLibrariesForSelfExtract`.
2. **ReadyToRun gain** for the application assemblies. The estimate (−100 to −250 ms) comes from the framework-ReadyToRun toggle; it needs an actual publish and timing on a target station.
3. **Fluent theme cost** (`PresentationFramework.Fluent`) on DataGrid-heavy pages, compared with the classic Aero2 theme. It was not isolated here.
4. **Remaining `RunAiAnalysis` engines** (Kerberos, LOLBAS, UBA, SAM, FileServer, compliance): their UI-thread time at 50k and 500k events was not timed.
5. **Path A EVTX throughput and memory** on a multi-GB Security log (millions of records). The documented 348k events in 19 s needs a repeat with a memory profile.
6. **FTS5 availability** in `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.10, and the index build cost at import.
7. **Live-monitor load on a domain controller** (Security `*` subscription): event rate against the UI thread before and after batching.
8. **The detection P1 rules** (LSASS, ClickFix, BYOVD, spray, AMSI, WMI) need lab replay corpora (Atomic Red Team or EVTX-ATTACK-SAMPLES-style) to set thresholds and allowlists before they are shipped.
9. **Media alert race (L4/L5)** reproduction on hardware: insert two sticks within 1.5 s, and one during the first inventory.
10. **Measurement drift.** Another session committed start-up changes (`41e0f2ad3`) during this review. The MainViewModel numbers above therefore cover both before (≈ 450–530 ms Release warm) and after (≈ 215–250 ms) that commit; re-run the start-up test once the tree is stable.
