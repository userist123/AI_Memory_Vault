---
project_id: LOGANALYZER_DFIR
application: LogAnalyzer — local Windows DFIR platform
repository: userist123/AI_Memory_Vault_CODEX_READY
workspace: 02_PRODUCT/projects/workspaces/loganalyzer-dfir
last_updated_utc: 2026-10-05T03:00:00Z
base_main_sha: 69afe312a
status: ACTIVE
working_branch: claude/loganalyzer-auto-mode (stacked on claude/loganalyzer-dfir-platform, PR #208)
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
  - owner roadmap (docs/dfir/RESEARCH_AND_ROADMAP.md): A per-program containment + scan + incident PDF (tab 13, trusted programs by SHA-256); B full detail of any grid row on double-click with PDF (replaced facade window); C station control audit for users/admins with CONFORM/NECONFORM/DE VERIFICAT/NEDETERMINAT + PDF (tab 14); D domain LDAP checks, user investigation from DC logs, e-mail investigation via official Microsoft scripts + CSV analysis (tab 15)
  - consolidation: release docs (README, SECURITY, Documentation/RELEASE-CHECKLIST, MVP-DECISIONS, ENGINEERING-PLAYBOOK) and win-x64 single-file publish profiles recovered from LogAnalyzer.UI 137a626 and adapted per edition; CI moved to root .github/workflows/loganalyzer-dfir-build.yml (pinned, path-filtered); unique never-committed files preserved in _recovered/ with SHA-256 provenance
verification:
  - dotnet build LogAnalyzer.slnx: 0 errors
  - LogAnalyzer.Dfir.Tests: 27/27 (corpus tests skip when the local corpus is absent)
  - LogAnalyzer.UI.Tests: 94/94; LogAnalyzer.Dfir.Tests: 45 pass + 1 admin-only skip (SecurityEventIngestionService now maps the snake_case event contract; the test had been failing on main since fe4936510 / #204)
  - personal_data_guard, validate_repository_layout, repository_hygiene, exempt_area_secret_scan: PASS
next:
  - roadmap stage E: full investigation pipeline (collectors, unified timeline, correlation incl. log-tampering from _recovered LogIntegrityService, findings, final report) validated on the NanAgent corpus
  - owner decisions pending: untrack legacy root license.lic and root-level duplicate csproj files; RSA-PSS license migration (invalidates issued keys)
  - phase 3 collectors, phase 4-9 analysis/timeline/findings, phase 10 reports and core export, phase 12 UI integration
notes:
  - the real incident corpus (D:\FORENSIC_CASE) is local evidence and must never be committed; tests read it only through targets.json + LADFIR_CORPUS
---
