# Checkpoint — claude-wp1b (LogAnalyzer WP1b: log-clear lifecycle, no after-hours auto-penalty)

- **Task:** owner decision 24 (`docs/dfir/CONTRACT_AUDIT_STAGE1.md` §8): fix "every log clear = High/Critical",
  the automatic after-hours penalty, and "Conform" on absence of events.
- **Branch:** `loganalyzer/wp1b-log-clear-lifecycle` (from main @ `fff6b0c6`). PR: not opened yet.
- **Queue position:** item 2 of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`.

## Already done before WP1b (verified on main)
- "Conform" on absence: `LogAnalyzer.Core/Services/ComplianceAuditEngine.cs` never returns CONFORM (WP1). Keep it so;
  add a regression test only if none exists.

## Spec
Paths below are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

1. **One shared, pure classifier** in `LogAnalyzer.Dfir.Core/Analysis/` (e.g. `LogClearAssessment.cs`):
   - Input: the clear events (Security 1102, System 104; channel, UTC time, subject user/domain), an optional
     `LogMaintenancePolicy` (approved clearing accounts + maintenance windows; `null` = not defined yet — WP15 fills it
     from the procedure profile later), and the times of other High/Critical findings in the same case.
   - Lifecycle per clear: `Routine` (policy defined, account approved AND inside a window) / `Unexpected` (policy
     defined, no match) / `NotAssessed` (no policy).
   - Corroborating factors (each with a Romanian reason string): several channels cleared by the same subject within
     10 min; clear within ±60 min of another High/Critical finding.
   - Severity: Routine → Info; NotAssessed without factors → Medium ("necesită verificare: nu există profil de
     procedură care să arate dacă golirea a fost planificată"); Unexpected or any factor → High. A clear alone is never
     Critical.
2. **Use it everywhere a clear is scored:** `LogAnalyzer.Dfir.Core/Analysis/Correlation.cs` (~l.86-96),
   `AntiForensics.cs` AF01 (keep Detected, add lifecycle text), and the legacy engines
   `LogAnalyzer.Infrastructure/Engines/AnalysisEngine.cs` (~l.44, now "Critical"),
   `LogAnalyzer.Core/Services/Network/LiveSecurityMonitoringEngine.cs` (~l.125, "Critical", incl. `wevtutil cl`),
   `LogAnalyzer.Infrastructure/Engines/SigmaRuleEngine.cs` (~l.140-158). Legacy engines have no policy → NotAssessed path.
   Titles must not assert intent ("șters intenționat") without evidence.
3. **No after-hours auto-penalty:**
   - `LogAnalyzer.Core/Services/ExplainableAiRiskEngine.cs` (~l.74): off-hours logons add 0 points; show them as context
     ("neevaluat: programul de lucru nu este definit").
   - `LogAnalyzer.Core/Services/UserBehaviorAnalyticsEngine.cs` (~l.25): off-hours logons are context (Info), not High.
   - Add an optional `WorkingHours` input (null = not defined) so WP15 can plug in. Even when it is defined,
     after-hours activity is reported as a comparison with the profile, not as an automatic penalty.
4. **Do not remove functionality.** Both `MainViewModel.cs` copies (root `ViewModels/` and `LogAnalyzer.App/ViewModels/`)
   keep compiling and showing the counts.
5. **Tests (TDD):** xUnit in `LogAnalyzer.Dfir.Tests` for the classifier (each lifecycle, each factor, null policy),
   plus Correlation/legacy-engine severity tests. Keep `RuleContracts.cs` and `20_TESTS/test_loganalyzer_rule_catalog.py`
   in sync if rule ids or titles change.

## Verification required before push
- `dotnet build LogAnalyzer.slnx -p:EnableWindowsTargeting=true` → 0 errors.
- Dfir/UI/Edition tests: only the known Linux-only failures (Dfir 48, UI 6); all new tests pass.
- Python: `python3 -m pytest -q -p no:cacheprovider` once (expect 3719+ passed, 0 failed); allowlist entry for this file
  exists in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.

## Done
- 2026-10-09T03:10Z claude-orchestrator: spec written, branch created.
- 2026-10-09 claude-wp1b: items 1-5 implemented (TDD). Classifier `LogClearAssessment.cs` (+ `WorkingHours.cs`) in Dfir.Core;
  used by Correlation LOG-TAMPER (built after sections 1-7 so it sees other High findings; optional `maintenancePolicy` arg),
  AntiForensics AF01 (still Detected, lifecycle text appended), AnalysisEngine, LiveSecurityMonitoringEngine, SigmaRuleEngine
  (all NotAssessed -> Medium). Off-hours: ExplainableAiRiskEngine 0 points + context factor, UBA Info/RiskWeight 0,
  AnomalyDetectionEngine night logon Info; optional `WorkingHours` input on both. `LogAnalyzer.Core` now references `Dfir.Core`.
  Decision: a Routine clear that also has a corroborating factor is High (factors override lifecycle). 1100/4719 stay Medium.
  Tests: Dfir 15 classifier + 6 correlation/AF01, UI 10 legacy-engine. CONFORM regression test already existed.
- Verified: build 0 errors; Dfir 48 failed (baseline) / UI 6 failed (baseline) / Edition 5 passed. Python 3719 passed, 0 failed.
- 2026-10-09T03:40Z claude-orchestrator review: changed the Routine+factor rule. Multi-channel clearing is reported but does
  NOT escalate a Routine clear (planned rotation often clears Security/System/Application together); proximity to another
  High/Critical finding still escalates. Test `Planned_maintenance_clearing_several_channels_stays_Routine_Info` (red, then green).
  Checked: LOG-GAP findings are Medium, so a clear cannot escalate itself through its own record-id gap.

## Next
- PR against main; Windows CI green; merge. Then queue item 3 (WP3).

## Blockers
- None. Procedure profile (working hours, log rotation, approved accounts) arrives with WP15; WP1b only adds the inputs.
