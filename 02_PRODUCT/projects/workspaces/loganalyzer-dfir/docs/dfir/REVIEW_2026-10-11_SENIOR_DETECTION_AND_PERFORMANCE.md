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

<!-- A.3 and A.5 inserted below -->

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

<!-- Part B inserted below -->
