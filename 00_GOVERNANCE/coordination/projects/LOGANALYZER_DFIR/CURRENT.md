---
project_id: LOGANALYZER_DFIR
application: LogAnalyzer — local Windows DFIR platform
repository: userist123/AI_Memory_Vault_CODEX_READY
workspace: 02_PRODUCT/projects/workspaces/loganalyzer-dfir
last_updated_utc: 2026-10-05T23:00:00Z
base_main_sha: 69afe312a
status: ACTIVE
working_branch: claude/loganalyzer-reality-p0 (local, not pushed; stacked on claude/loganalyzer-execution-artifacts; #208 and #210 merged)
claimed_by: claude-code (Opus 5.5) — 2026-10-04T11:30:00Z
active_work:
  - DFIR master spec implementation (phases 0-15) as new projects LogAnalyzer.Dfir.Core / .Windows / .Tests; legacy AirGapped/Network editions left untouched until UI integration (phase 12)
done_on_branch:
  - phase 0-1 architecture audit (docs/dfir/DFIR_CURRENT_ARCHITECTURE_AUDIT.md)
  - phase 2 case/evidence/provenance model, chain of custody, SHA-256, spec status semantics
  - real parsers validated on the real incident corpus: MAM Prefetch, SRUM network usage (ESE), raw EVTX, PCAPNG (incl. Wi-Fi 802.11 frames)
  - phase 3 start: collector contract + safe external tool runner
  - licensing: LogAnalyzer.LicenseManager (WPF issuer + %APPDATA% ledger); KeyGen rewired to Core LicenseService (HWID mismatch fixed, stdin sanitised); Core VerifyLicenseString/BuildLicenseString; CoreLicenseKeyTests pin the key format
  - single application LogAnalyzer.App (LogAnalyzer.exe): AirGapped/Network chosen at startup by passive Windows NLM detection (no traffic), --mode= / LogAnalyzer.mode override, fail-closed to AirGapped, no automatic switch at runtime (warning instead); NetworkPolicy blocks syslog listener and online services in AirGapped
  - owner roadmap (docs/dfir/RESEARCH_AND_ROADMAP.md): A per-program containment + scan + incident PDF (tab 13, trusted programs by SHA-256); B full detail of any grid row on double-click with PDF (replaced facade window); C station control audit for users/admins with CONFORM/NECONFORM/DE VERIFICAT/NEDETERMINAT + PDF (tab 14); D domain LDAP checks, user investigation from DC logs, e-mail investigation via official Microsoft scripts + CSV analysis (tab 15); E full investigation pipeline - collectors, unified timeline, correlation with INCIDENT-CHAIN, report PDF (tab 16), validated on the NanAgent corpus
  - consolidation: release docs (README, SECURITY, Documentation/RELEASE-CHECKLIST, MVP-DECISIONS, ENGINEERING-PLAYBOOK) and win-x64 single-file publish profiles recovered from LogAnalyzer.UI 137a626 and adapted per edition; CI moved to root .github/workflows/loganalyzer-dfir-build.yml (pinned, path-filtered); unique never-committed files preserved in _recovered/ with SHA-256 provenance
  - REAL_DFIR_v2 phase 0 - reality audit with COMPONENT / REAL-PARTIAL-FACADE-UNSAFE / EVIDENCE in docs/dfir/REALITY_AUDIT.md
  - REAL_DFIR_v2 P0 - containment playbook reports NOT_EXECUTED instead of fake success; sanitization needs destructive confirmation, verifies zeroes by read-back, certificate has no invented fields
  - REAL_DFIR_v2 P0 - EvidencePreflight (exists, SHA-256, size, content fingerprint) before and after every parse, EVIDENCE_MUTATED blocks parsing (docs/dfir/EVIDENCE_MODEL.md)
  - REAL_DFIR_v2 P0 - station firewall actions are APPLY then VERIFY with VERIFIED or NOT_VERIFIED or FAILED or REJECTED; no fixed fallback IP; uncalled unverified remediation methods removed
  - REAL_DFIR_v2 P0 - no current time as evidence time (watcher, super timeline, alert modal); provenance ledger never overwrites an unreadable ledger; swallowed errors removed on the real path
  - tests for BAM, ShimCache, Amcache (synthetic hives + real corpus Amcache) and AirGapped zero HTTP requests
  - REAL_DFIR_v2 P1 - source SHA-256 and parser identity bound to every timeline event and finding reference; findings without evidence rejected; report re-verifies evidence (ReportIntegrity)
  - REAL_DFIR_v2 P2 - ParserDescriptor and ParserRegistry with content-based selection; evidence without a parser listed as SKIPPED_BY_DESIGN; Analysis/parsers.json (docs/dfir/PARSER_CONTRACT.md)
  - REAL_DFIR_v2 P11 partial - FORENSIC VALIDATION AVAILABLE or PARTIAL or UNAVAILABLE reported every run and in CI summary; LADFIR_REQUIRE_CORPUS=1 fails without corpus (docs/dfir/FORENSIC_TEST_LAB.md)
  - REAL_DFIR_v2 P3 - SYSTEM hive validated vs reg query (found and fixed UWP BAM entries skipped and DiscUtils big-data truncation via new RawRegistry); ScheduledTaskParser vs schtasks; UserHiveParser and SoftwareHiveParser vs reg export; persistence correlation rules
  - REAL_DFIR_v2 P3 - ServicesParser vs WMI Win32_Service; a source can feed several parsers; BrowserHistoryParser vs the manual extraction; DOWNLOAD-THEN-EXEC puts the tzd4is.cyou download at the start of the 19.09 incident chain; LnkParser vs WScript.Shell; all hive parsers on RawRegistry, DiscUtils only in tests
verification:
  - 2026-10-05 claude/loganalyzer-reality-p0 - build 0 errors, UI.Tests 121/121, Dfir.Tests 134 pass + 1 admin-only skip (corpus AVAILABLE 14 of 14 sections), vault guards PASS
  - dotnet build LogAnalyzer.slnx: 0 errors
  - LogAnalyzer.Dfir.Tests: 27/27 (corpus tests skip when the local corpus is absent)
  - LogAnalyzer.UI.Tests: 94/94; LogAnalyzer.Dfir.Tests - 45 pass + 1 admin-only skip (SecurityEventIngestionService now maps the snake_case event contract; the test had been failing on main since fe4936510 / #204)
  - personal_data_guard, validate_repository_layout, repository_hygiene, exempt_area_secret_scan: PASS
next:
  - REAL_DFIR_v2 next - P3 remaining (Jump Lists, USB/MountedDevices/SetupAPI, Recycle Bin $I, WFP 5156/5157 correlation, Firefox; MFT/USN need raw files not in the corpus); then P4 evidence graph
  - PR #209 is OPEN and needs human security review; not touched by this work
  - next: owner decisions (push/PR of claude/loganalyzer-auto-mode, RSA licensing, legacy root files); remove legacy facade screens that show sample data
  - owner decisions pending: untrack legacy root license.lic and root-level duplicate csproj files; RSA-PSS license migration (invalidates issued keys)
  - phase 3 collectors, phase 4-9 analysis/timeline/findings, phase 10 reports and core export, phase 12 UI integration
notes:
  - the real incident corpus (D:\FORENSIC_CASE) is local evidence and must never be committed; tests read it only through targets.json + LADFIR_CORPUS
---
