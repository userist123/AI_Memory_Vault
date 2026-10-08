---
project_id: LOGANALYZER_DFIR
application: LogAnalyzer — local Windows DFIR platform
repository: userist123/AI_Memory_Vault_CODEX_READY
workspace: 02_PRODUCT/projects/workspaces/loganalyzer-dfir
last_updated_utc: 2026-10-08T00:00:00Z
base_main_sha: 07ec83d9
status: ACTIVE
working_branch: loganalyzer/wp0-baseline (stage 2 WP0; PR #212 is merged into main as f8dc8a31)
claimed_by: claude-code (Opus 5.5) — 2026-10-04T11:30:00Z; hardening of malformed-input handling claimed and completed 2026-10-07T18:45:00Z
stage2:
  - stage 1 audit is on main at 02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/CONTRACT_AUDIT_STAGE1.md with the two contracts next to it (LOGANALYZER_PROGRAM_REQUIREMENTS.md and LOGANALYZER_PRODUCT_UX_CONTRACT.md); its file and line references are on the audited SHA 0689f5d48, not on current main
  - the 13 owner decisions of 2026-10-08 (audit section 8) are the stage 2 basis - 1 InspectraVeritas is a separate assembly and CONTRADICTED or REJECTED block Vault export and reports; 2 section-20 states are added alongside Finding.Classification with versioned JSON; 3 static safety text and unconditional CONFORM are replaced by evidence-derived or neutral text; 4 unwired legacy code is left untouched; 5 legacy tabs stay under Advanced; 6 localisation starts as a UI-edge translation table; 7 a synthetic redistributable corpus is mandatory in CI and only the real-corpus run is waivable by a signed owner waiver; 8 the release gate is a CI job plus an owner script with the always-blocking and waivable lists of the audit; 9 audit integrity starts as a hash chain; 10 the app exports Vault proposals first (PR 209 is already merged, so the open security review note below is stale); 11 evidence lifecycle states only with no automatic deletion; 12 APT Attribution is renamed to technique overlap and says it is not attribution; 13 two applications - P1 classified is a separate build with no network, remote AI, host-modifying actions or updates compiled in, and P2/P3 unclassified is one application with air-gapped and connected modes set by signed policy at install and not downgradable by the operator, keeping all existing functionality, both sharing the core libraries
  - work package order - WP0 baseline and governance; WP1 honest shell; WP12 test and release gate in parallel; new WP-ED edition split (decision 13) after WP0, WP1 and WP12 and before WP3; WP2 finding contract and state vocabulary; then WP3 integrity and WP4 verification layer; WP5 case persistence and Home model; WP6 UI foundation; WP7 navigation and goal pages and WP8 AI surface; WP9 response consistency, WP10 reporting and WP11 parser coverage can interleave once WP2 is done; stage 2 code needs the owner go per package
  - Windows CI baseline is recorded in docs/dfir/STAGE2_BASELINE.md - main run 37746891007 on f8dc8a31 green - Dfir.Tests 252 pass and 28 skip, UI.Tests 120 pass - forensic validation UNAVAILABLE 0 of 21 corpus sections
  - Documentation/PHASE1-STATUS.md and docs/dfir/DFIR_CURRENT_ARCHITECTURE_AUDIT.md are marked historical and superseded by the stage 1 audit
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
  - REAL_DFIR_v2 P3/P4/P5 - USB, Jump Lists, Firefox, audit coverage; Evidence Graph; IOC/hash, YARA-lite, Sigma-lite (matches wevtutil) in every investigation
  - REAL_DFIR_v2 P6/P7 - policy model, lifecycle (no DRAFT apply, hash-bound, no self-approval), importers validated on 3 owner GPOs vs gpreport.xml, verified execution with plan hash confirmation, rollback, hash-chained audit; "Politici" page
  - REAL_DFIR_v2 P8 - compliance model and OSCAL assessment-results (NIST schema not validated); P9 domain inventory in the graph (synthetic data only)
  - REAL_DFIR_v2 P10 - remote collection package (authorize, hash on target, verify, import), no network from the app; P11/P12 forensic lab LADFIR_LAB=1 (174 PASS, 1 PARTIAL explained, 0 FAIL over 170 EVTX vs wevtutil)
  - REAL_DFIR_v2 P13 - anti-forensics lab (16 checks, DETECTED/NOT_DETECTED/UNDETERMINED); found RecordID reuse in Application.evtx after the 2026-08-08 unclean shutdown
  - REAL_DFIR_v2 P14 - Memory Vault proposals validated by the vault's own memory_access.propose(); nothing written to the vault, submission stays on the existing gate
  - REAL_DFIR_v2 P15 - local AI (Ollama, numeric loopback) with deterministic citation checks; model-as-judge tried and rejected (docs/dfir/AI_FORENSIC_REASONING.md)
  - 2026-10-07 (commits of that day) - cd4cb6d1 remote package hashes via .NET SHA256, not Get-FileHash; e9c193d0 gitleaks allowlist for cryptography key type annotations; b0a9cad9 CI turns failed TRX results into ::error annotations; bcb546e2 merge of main (609b01bf6); 6ff43e86 RemoteTriageService facade deleted (with its test); b733270c USN change journal parser (fsutil export) and Prefetch deletion / journal recreation from it; 0ded0577 Evidence Graph explorer in the app
  - 2026-10-07 hardening on malformed and hostile evidence (tests - RawRegistryMalformedTests, RuleRegexTimeoutTests, MalformedBinaryInputTests) - RawRegistry subkey lists (ri/lf/lh/li) walked with a visited set and a depth limit, so a list that points to itself is an InvalidDataException instead of a process-killing stack overflow; Sigma-lite and YARA-lite regexes have match timeouts, a timeout is reported (RuleTimeout, FAILED gap) and is never a match or a silent clean result; CompoundFile checks FAT/DIFAT counts and DIFAT cycles; PcapngReader no longer loops on a section header length below 12, reports a capture cut inside a block, and keeps frames whose timestamp is outside the calendar; Timestamp.FromFileTime returns an unknown time (raw kept) beyond the calendar; USN header numbers that are not numbers are InvalidDataException
verification:
  - 2026-10-07 claude/loganalyzer-reality-p0 @ 0ded0577 - Windows CI (loganalyzer-dfir-build.yml, run 37649715978) - build 0 errors, Dfir.Tests 218 pass + 28 skip, UI.Tests 120 pass; "FORENSIC VALIDATION = UNAVAILABLE (0/21 corpus sections)" - the CI run is not forensic validation, only the owner's corpus run is
  - 2026-10-07 Linux container (EnableWindowsTargeting) - the same suites have platform-caused failures that Windows CI does not have (Dfir.Tests 59, UI.Tests 6 - ACLs, wintrust, powershell/wevtutil, backslash paths); the hardening tests above are platform independent and pass there
  - 2026-10-06 claude/loganalyzer-reality-p0 - Dfir.Tests 236 pass + 4 skip (lab on demand, owner GPO samples, 2 admin-only), UI.Tests 121/121, vault guards PASS; lab run 174 PASS / 1 PARTIAL / 0 FAIL
  - 2026-10-05 claude/loganalyzer-reality-p0 - build 0 errors, UI.Tests 121/121, Dfir.Tests 134 pass + 1 admin-only skip (corpus AVAILABLE 14 of 14 sections), vault guards PASS
  - dotnet build LogAnalyzer.slnx: 0 errors
  - LogAnalyzer.Dfir.Tests: 27/27 (corpus tests skip when the local corpus is absent)
  - LogAnalyzer.UI.Tests: 94/94; LogAnalyzer.Dfir.Tests - 45 pass + 1 admin-only skip (SecurityEventIngestionService now maps the snake_case event contract; the test had been failing on main since fe4936510 / #204)
  - personal_data_guard, validate_repository_layout, repository_hygiene, exempt_area_secret_scan: PASS
next:
  - REAL_DFIR_v2 next - $MFT collection and parsing (the fsutil USN export is parsed since b733270c; closes AF06-AF08 only with $MFT); OSCAL schema validation; admin-only runs (HKLM apply, audit vs auditpol); owner run on Windows with the corpus (LADFIR_REQUIRE_CORPUS=1, LADFIR_LAB=1) and manual check of the new pages (Politici, graph explorer, remote package, AI)
  - PR 212 is merged (f8dc8a31) without a human review; a security review is still needed for PolicyExecution, SettingProviders, RemoteCollection (generated PowerShell), SystemDefenseExecutionService (netsh), MediaSanitizationEngine
  - follow-ups - CodeQL does not analyse C# (add csharp to codeql.yml); sanitize CR/LF of CaseId in RemoteCollection.PackageScript; Dfir.Core uses Windows path semantics on a portable net10.0 target
  - PR 209 is merged (stale note replaced on 2026-10-08); not touched by this work
  - next: owner decisions (push/PR of claude/loganalyzer-auto-mode, RSA licensing, legacy root files); remove legacy facade screens that show sample data
  - owner decisions pending: untrack legacy root license.lic and root-level duplicate csproj files; RSA-PSS license migration (invalidates issued keys)
  - phase 3 collectors, phase 4-9 analysis/timeline/findings, phase 10 reports and core export, phase 12 UI integration
notes:
  - the real incident corpus (D:\FORENSIC_CASE) is local evidence and must never be committed; tests read it only through targets.json + LADFIR_CORPUS
---
