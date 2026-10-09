# Checkpoint — claude-wp15 (LogAnalyzer WP15: procedure profile, log lifecycle, policy timeline)

- **Task:** queue item 5 of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`.
  - Lessons-learned rows 8, 9, 31 and 32 in `docs/dfir/LESSONS_LEARNED_MAPPING.md`.
  - Owner decision 18 (procedure profile entered manually, with paste/import), decision 19 (zones) and
    decisions 27-32 (2026-10-09, recommendations accepted) in `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §8.
- **Split:**
  - **WP15a** (this branch): the profile and its use.
  - **WP15b** (next PR): the GPO/policy change timeline (5136/5137/5141/4739, gPLink) as BEFORE→CHANGE→AFTER→APPLICATION→BEHAVIOR,
    configured/applied/enforced/observed, control gap, and time manipulation (4616, clock jumps).
- **WP15a branch:** `loganalyzer/wp15a-procedure-profile`, from main @ `d0fbae7e` (WP4 #240 may merge meanwhile; merge main
  before the PR). This branch already carries decisions 27-32 and the `SUPPORTED_WINDOWS.md` fix.

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## Already in place (reuse)
- `LogAnalyzer.Dfir.Core/Analysis/LogClearAssessment.cs`: `LogMaintenancePolicy` (approved accounts + maintenance windows,
  `null` = not defined) and the Routine / Unexpected / NotAssessed classifier, used by Correlation LOG-TAMPER, AntiForensics AF01
  and the legacy engines.
- `LogAnalyzer.Dfir.Core/Analysis/WorkingHours.cs`: optional input of ExplainableAiRiskEngine, UBA and AnomalyDetectionEngine
  (no penalty, comparison only).
- `LogAnalyzer.Dfir.Core/Policy/*`: PolicyModel/Import/Store/Formats/Execution. `LogAnalyzer.Dfir.Windows/Policy/PolicyWorkbench.cs`.
  `LogAnalyzer.App/Views/PolicyView.xaml`. Expected-GPO import already exists there; reuse it, do not duplicate.
- WP3a: `CaseScope` and `CaseScope.Provisional`; `LogAnalyzer.App/Services/LiveCase.cs` (provisional scope today).
- WP3b: `CaseWorkspace.Anchor()` (the two chain head hashes) and `WriteManifest`.

## WP15a spec
1. **ProcedureProfile model** in `LogAnalyzer.Dfir.Core` (new `Profile/` folder), versioned JSON with SchemaVersions. Sections:
   - working hours: per weekday intervals, holidays, shifts;
   - log maintenance: approved clearing accounts, maintenance windows (recurring, e.g. "first Monday of the month 08:00-10:00",
     plus one-off), rotation procedure (EXPORT → HASH → ARCHIVE → VERIFY → CLEAR expected order);
   - approved software: name, publisher, path pattern, hash optional;
   - expected GPO / policy: link to the existing Policy import, do not copy it;
   - zones and approved transfers (decision 19): zones, approved transfer channels, authorized network destinations.
   - Every section can be empty. Empty means "not defined" and is reported as such, never as "conform".
2. **Entry:** manual editing plus paste/import (decision 18).
   - A new App view "Profil de proceduri": tables per section, Save/Load. It is reachable from the existing navigation; no page
     is removed.
   - Import JSON (own format) and CSV per section; paste from the clipboard into a section (tab/CSV text).
   - Validation errors are listed per line and are never silently dropped.
   - Profile file: `%PROGRAMDATA%\LogAnalyzer\profile\procedure_profile.json`, with a SHA-256 recorded in each case's custody
     when a case uses it (snapshot copied into the case `Analysis/procedure_profile.json`, registered via `RecordOutput`).
3. **Wire it in** (this is the point of the profile):
   - The pipeline and LiveCase load the profile and pass `LogMaintenancePolicy` and `WorkingHours` to Correlation, AntiForensics
     and the legacy engines that already accept them.
   - With a profile, log clears become Routine / Unexpected instead of NotAssessed; without one, behaviour is unchanged.
   - Off-hours stays a comparison, never a penalty.
4. **Decision 27:** extend the lifecycle classifier to Security 1100 and 4719 exactly as decision 27 states (shutdown/restart
   pairing within 10 min with System 1074/6006/6005; GPO-applied 4719 by SYSTEM / computer account `$`; removal of
   success/failure auditing is High when not routine). LOG-TAMPER uses it. Tests for each branch.
5. **Decision 28:** the LIVE scope dialog.
   - Before the first LIVE use (StationControl / DomainInvestigation / Investigation actions that call `LiveCase.Get()`), the
     scope dialog must be completed, and the scope is stored via `LiveCase.Configure`.
   - Emergency containment (isolation) may proceed on the provisional scope; the dialog opens right after.
   - Until it is confirmed, reports/exports carry "scop provizoriu, neconfirmat".
   - Implement a minimal WPF dialog bound to `CaseScope`, and a way to confirm/replace a provisional scope in an existing LIVE
     case. The confirmation goes to the custody/audit chain (`case.scope_confirmed`).
6. **Decision 30:** the chain head hashes (`CaseWorkspace.Anchor()`) go in the footer of every case PDF:
   - `InvestigationReportPdf`, `ControlReportPdf`, `ChecksReportPdf`;
   - the export manifest;
   - a "Închidere caz" action/line that shows them for the operator to copy into the custody register.
7. **Tests (TDD):**
   - profile round trip, import JSON/CSV, paste parsing, validation errors;
   - recurring window expansion;
   - with/without profile: LOG-TAMPER/AF01 lifecycle;
   - 1100/4719 branches;
   - provisional → confirmed scope audit;
   - anchor in each PDF footer (text extraction or a generator hook);
   - empty sections reported "nedefinit".

## Verification required before push
- `dotnet build LogAnalyzer.slnx -p:EnableWindowsTargeting=true` → 0 errors.
- Dfir/UI/Edition tests: only the known Linux-only failures (Dfir 48, UI 6); all new tests pass; Edition green (the profile
  view/dialog must exist in both editions; nothing networked).
- `python3 -m pytest -q -p no:cacheprovider` once on the final head → 0 failed. New .md files go in
  `20_TESTS/fixtures/unreadable_notes_allowlist.json`.

## Done
- 2026-10-09T07:40Z claude-orchestrator: owner answered "folosește recomandări". Decisions 27-32 recorded; SUPPORTED_WINDOWS.md
  corrected to decision 15 (adds Win10 LTSC 2019/2021 incl. IoT, LTSB 2016 best effort); spec written; branch created.

- 2026-10-09 claude-wp15a: spec 1 (model Profile/, tables, validation, CSV/paste/JSON import, store, recurring window expansion, ToMaintenancePolicy/ToWorkingHours) + tests (ProcedureProfileTests, 17 pass).

## Next
- Spec 4 (1100/4719 lifecycle), spec 3 (wire pipeline/legacy engines + snapshot in custody), spec 5 (scope dialog), spec 6 (anchor in PDFs/manifest/close case), spec 2 (App view), then merge main + full suites + push. The orchestrator opens the PR.

## Blockers
- None.
