> HISTORICAL DOCUMENT (banner added 2026-10-08, WP0). Last written 2026-10-04. Superseded by `CONTRACT_AUDIT_STAGE1.md` (stage-1 contract audit, 2026-10-08). Do not rely on this file for the current state of the code; it is kept unchanged below for history.

# DFIR — Current Architecture Audit (Phase 0/1)

- Date: 2026-10-04
- Location: AI Memory Vault, `02_PRODUCT/projects/workspaces/loganalyzer-dfir/` (canonical workspace). Vault branch `claude/loganalyzer-dfir-platform` from `origin/main` `69afe312a`.
- The audit was first run on the standalone repository `userist123/LogAnalyzer.UI` (`feature/sqlite-dashboard`), whose code matches this workspace except some Core service files. Its 35 uncommitted changes are preserved there on branch `wip/pre-dfir-snapshot`.
- Baseline in this workspace: build **0 errors, 181 warnings**. Legacy tests **70/71**. The one failure, `SecurityEventIngestionServiceTests.ReadsValidMetadataOnlyEvent`, comes from `main` commit `fe4936510` (#204) and is independent of the DFIR projects, which that test project does not reference.
- Toolchain: .NET SDK 10.0.301 (also 9.0.310). Windows 11 26200. PowerShell 5.1.

## 1. Solution layout

| Project | TFM | Role |
|---|---|---|
| LogAnalyzer.Core | net10.0 | Models (`ParsedEvent`, `ForensicArtifact`, `TimelineItem`, `IocItem`, AD/UBA/compliance models) and ~80 analysis/report services |
| LogAnalyzer.Infrastructure | net10.0 | Parsers (EVTX, Prefetch, SRUM, MFT, Amcache/Shimcache, browser, LNK, USB, triage CSV…), `DatabaseService` (SQLite + SQLCipher), `AuditCollectionService`, live EventLog watcher, Sigma/YARA engines |
| LogAnalyzer.AirGapped | net10.0-windows (WPF) | Standalone edition: MVVM (CommunityToolkit), QuestPDF, one `MainViewModel` of 199 KB |
| LogAnalyzer.Network | net10.0-windows (WPF) | Enterprise edition: a near-copy of AirGapped (same 199 KB `MainViewModel`) |
| LogAnalyzer.KeyGen | net10.0-windows | License key generator |
| LogAnalyzer.UI.Tests | net10.0-windows | xUnit, 69 tests |

Repository hygiene issues:
- The repo root still holds a legacy single-app layout (`LogAnalyzer.UI.csproj`, `LogAnalyzer.AirGapped.csproj`, `LogAnalyzer.Network.csproj`, `App.xaml`, `MainWindow.xaml`, `Views/`, `ViewModels/`, `Services/`, `Themes/`). It is not part of `LogAnalyzer.slnx`, but its csproj files glob the subfolders, so building them directly would compile everything twice.
- `AI_Memory_Vault_CODEX_READY/` (a separate project) is nested inside this repository.
- `license.lic` and `Generate-LicenseKey.ps1` are tracked at the root.

## 2. What is real vs. façade (spot-checked against the spec)

| Component | Finding | Spec rule violated |
|---|---|---|
| `Infrastructure/Parsers/PrefetchParser.cs` | `DecompressMam` returns a **zero-filled buffer** instead of decompressing (Win10/11 Prefetch is always MAM). Run count and time are therefore read from zeros; it falls back to the file mtime and still labels the result `EvidenceStrength.ExecutionProven` | §29 (no false execution proof), §75, Bug class C |
| `Infrastructure/Parsers/SrumParser.cs` | Does **not** read the ESE database; emits one descriptive artifact marked `ExecutionProven` | §28/§75: must say NOT_AVAILABLE or parse for real |
| `Infrastructure/Parsers/EvtxParser.cs` | Real reader (`EventLogReader`), but `TimeCreated ?? DateTime.Now` **fabricates time**; errors are swallowed; no evidence/provenance link | §113/§153, §75, Bug class C |
| Whole codebase | **117** empty `catch { }` blocks (24 in each `MainViewModel`) | §75, §167 (no swallowed exceptions) |
| Whole codebase | **13** `?? DateTime.Now/UtcNow` timestamp fallbacks | §153 |
| `Core/Services/AptAttributionEngine.cs` (+ UI) | Infers named APT actors | §147 (no attribution) |
| `Core/Services/Network/CyberAttackCountermeasureEngine.cs`, `Infrastructure/Services/SystemDefenseExecutionService.cs`, `IncidentResponsePlaybookService.cs` | Kill processes, change firewall or registry from the analysis app | §62 (remediation separated, explicit confirmation) |
| `LiveThreatIntelService`, `SiemForwarderService`, `M365LiveConnectorService`, `YaraRuleEngine` | Make outbound network calls | §148 (offline-first, no silent upload) |
| `MainViewModel` ×2 | 41 demo/sample-data markers; 199 KB god-object duplicated across editions | §3 (no fictitious data), §167 |
| `ForensicArtifact` / `ParsedEvent` models | No EvidenceId, collector, status, temporal type, UTC/local split, or parent evidence | §8, §70, §112, §114 |

Reusable foundations:
- `ForensicArtifact` already has `SourceFilePath`, `SourceOffset`, `SourceSha256`, `TimeSemantics` and `EvidenceStrength`. These are good concepts to carry forward.
- `ProvenanceLedgerService`, `DfirCasePackagingService`, `CaseSnapshotService`, `SuperTimelineExportService`, `C2BeaconingDetector` and `ProcessLineageCorrelator` exist and can be wrapped or migrated.
- SQLite infrastructure, QuestPDF, the WPF theme and the MVVM toolkit stay.

## 3. Decision: build a clean DFIR subsystem next to the existing app

Editing the 199 KB view models in place, and every existing parser, would risk the 69 green tests and the two editions. Following §5.3 ("extend, refactor locally, introduce interfaces, preserve compatibility"), the DFIR platform is added as **new projects**:

```
LogAnalyzer.Dfir.Core      (net10.0)          models, status/temporal/classification enums, hashing, provenance,
                                              chain of custody, timeline, correlation, findings, IOC, gaps, exports
LogAnalyzer.Dfir.Windows   (net10.0-windows)  preflight, tool discovery, collectors, Windows-native parsers
                                              (EVTX, Prefetch/MAM, SRUM/ESE, MSI, Defender, Tasks, PCAPNG…)
LogAnalyzer.Dfir.Cli       (net10.0-windows)  `ladfir` command line: preflight | acquire | import | analyze | report | verify
LogAnalyzer.Dfir.Tests     (net10.0-windows)  unit + integration + regression on the real corpus
```

The WPF editions then consume these services (Phase 12) through new views, without rewriting their existing screens. The defective legacy parsers are fixed or retired, with their tests kept green or replaced deliberately.

## 4. Regression corpus available on this host

| Corpus | Path | Notes |
|---|---|---|
| Full case 2026-10-03/04 | `D:\FORENSIC_CASE` | 170 raw EVTX, Defender, Prefetch (305), SRUM, MSI, PCAPNG, ETW, hives, tasks, browser history copies, SHA256 manifest |
| V11 collection | `C:\Users\Marius\Desktop\New folder (3)\NanAgent-Forensic-V11_20261003_221532` | Collection named in the spec |

Tests that need the corpus are skipped (not failed) when it is absent, so CI stays green on other machines. Expected artifacts live in `LogAnalyzer.Dfir.Tests/Corpus/NanAgentCase/targets.json` and are never hard-coded in production code (§73).

## 5. Planned modifications (phase order per §166)

0–1. Audit (this document) ✔
2. Case / EvidenceItem / Provenance / status / temporal models, plus a hashing engine and chain of custody
3. Collection engine: preflight, tool discovery, collectors with collection audit
4. Event parsing: EVTX (raw-first), Defender, PowerShell, Security
5. Execution and persistence: real Prefetch (MAM), SRUM (ESE), tasks, services, registry, MSI
6. Network: PCAPNG parser, socket snapshots, PktMon/ETW, SRUM network bytes
7–9. Timeline, correlation graph, findings with confidence and evidence requirements
10. Reporting and export: all §64 files, core pack, integrity verification
11. Regression on the real corpus
12. UI integration in the AirGapped and Network editions
13–15. Performance, security hardening, final audit
