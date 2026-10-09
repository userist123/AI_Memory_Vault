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

## WP15b spec (branch `loganalyzer/wp15b-policy-timeline`, from main @ `4627a6b0` after WP15a #243 merged)
Lessons-learned rows 31-32 (`docs/dfir/LESSONS_LEARNED_MAPPING.md`) and STAGE2_PLAN item 5. Works offline from collected evidence in both
editions (P1 has no directory connector: everything must work from EVTX alone; `LogAnalyzer.Connectors/Domain/DirectoryCollector.cs`
may add gPLink data in the unclassified edition only).
1. **Policy change events → PolicyChange records** (`Dfir.Core/Analysis/PolicyTimeline.cs`): Security 5136 (DS object modified:
   groupPolicyContainer attributes, versionNumber, gPCFileSysPath, gPLink/gPOptions on OUs/domain), 5137 (created), 5141 (deleted),
   5139 (moved), 4739 (domain policy), 4719 (audit policy, reuse WP15a classifier), 4907 (object SACL); endpoint application from
   Microsoft-Windows-GroupPolicy/Operational (4000-4007 start, 5016/5017 extension processed, 7016/7017, 8000-8007 end, 5312/5313
   applied/filtered GPO lists, 7320 errors). Each record: who, when, target (GPO GUID/name, OU), attribute, old/new value when the
   event carries them (5136 OperationType %%14674 value added / %%14675 value deleted pairs = before/after).
2. **Timeline BEFORE→CHANGE→AFTER→APPLICATION→BEHAVIOR** per GPO change: before/after values, first application on each host
   (GroupPolicy/Operational), and behaviour = observable effect in the same evidence (e.g. audit subcategory turned off → those event
   ids stop; Defender/firewall/USB policy → related events change). Behaviour is reported as "observat / neobservat / neevaluabil"
   with the reason; never inferred beyond the evidence.
3. **Configured / applied / enforced / observed** per expected policy setting: configured = expected policy (WP15a profile link to the
   existing Policy store/import), applied = GroupPolicy/Operational evidence, enforced = effective state where collected (StationFacts /
   AuditQuerySystemPolicy results already in the case), observed = events consistent with it. Each level OBSERVED/NOT_OBSERVED/UNKNOWN.
4. **Control gap finding** (new rule id, registered in `RuleContracts.cs` + `20_TESTS/test_loganalyzer_rule_catalog.py` stays green):
   configured ≠ applied/enforced/observed → finding with the exact level where it breaks, the evidence, MissingEvidence, and
   AlternativeExplanations (replication delay, filtering, WMI filter, loopback, host offline). Severity Medium by default; High only when
   the gap disables auditing or a security control AND a High finding falls in the gap window.
5. **Time manipulation (extend AF05, do not duplicate):** clock jumps from 4616 / Kernel-General 1 with magnitude and direction,
   time-zone changes (Kernel-General 22 / 4616 reason), record-order vs timestamp inversions inside one channel (RecordID increases while
   time goes back > tolerance), and mark affected windows in TimeFacts so the WP4 TEMPORAL check reports UNKNOWN there instead of
   CONTRADICTED. W32Time sync stays benign.
6. **Output + UI:** `Analysis/policy_timeline.json` (versioned, RecordOutput); a "Cronologie politici" section in the investigation view
   and the investigation PDF (list per GPO: before/after/application/behaviour, gap rows). Additive; no page removed.
7. **Tests (TDD):** synthetic EVTX-derived TimelineEvents for each event type; before/after pairing from 5136 value-added/deleted;
   application per host; behaviour observed / not observed / not evaluable; the four levels; gap finding severity rules; clock jump,
   timezone change, record-order inversion; WP4 TEMPORAL becomes UNKNOWN inside a manipulated window; classified build unaffected.

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
- spec 4 done (LogClearAssessment.AssessServiceStop/AssessAuditPolicyChange, used by LOG-TAMPER). spec 3 done: pipeline Run(procedureProfile) -> snapshot Analysis/procedure_profile.json in custody + maintenance policy to Correlation/AF; legacy engines (AnalysisEngine, LiveSecurityMonitoringEngine MaintenancePolicy), MainViewModel passes policy + WorkingHours via ProfileProvider.Shared.
- spec 6 done (ReportSeal/ReportFooter in Investigation/Control/Checks PDFs, manifest ScopeNote, CaseClosure.Close) and the Core of spec 5 (CaseWorkspace.ConfirmScope -> case.scope_confirmed, ScopeNote computed). Dfir.Tests: 48 failed = baseline.
- spec 5 WPF done (ScopeForm in Core tested; ScopeDialog; LiveCase.GetConfirmed/RequestScopeConfirmation/ScopePrompt; guards in StationControl/DomainInvestigation/Investigation VMs; emergency containment prompts right after) and 'Închidere caz' button (InvestigationView). App code is build-verified only (App.Tests cannot run on Linux).
- spec 2 done: ProcedureProfileViewModel + ProcedureProfileView ('Profil de proceduri', sidebar + tab 18), Save/Load/Import JSON/CSV/paste, per-line issues; App.Tests added but cannot run on Linux. Report line ProcedureProfileLine; LESSONS_LEARNED rows 8, 9 updated.
- 2026-10-09 claude-wp15a: merged origin/main; Dfir 48 failed (baseline) / 539 passed, UI 6 failed (baseline), Edition 14/14, build 0 errors, Python 3719 passed 0 failed. WP15a complete; ready for PR.

## Next
- WP15a merged as #243 (`4627a6b0`). Implement WP15b 1-7 on its branch, verify, push. The orchestrator opens the PR.

## Blockers
- None blocking. Safe defaults chosen (owner may revise): a maintenance section with only accounts or only windows counts as defined (clears then Unexpected); holidays are stored but unused; shifts/weekday intervals collapse to one covering range for the legacy WorkingHours; approved software, zones/transfers and the expected-policy link are stored and shown but no analyser consumes them yet; the rotation order is validated, not matched against evidence; 1100 pairs with System 1074/6006 within +-10 min or the first 6005 within 10 min after.
- App code (profile view, scope dialog, LiveCase gating) is build-verified only; App.Tests cannot run on Linux.
