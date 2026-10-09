# Checkpoint — claude-wp5 (LogAnalyzer WP5: case persistence, Home, coverage matrix)

- **Task:** first part of queue item 7 in `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`; WP5 in
  `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §7. Closes rows **R9.1, U3, U21, U22** (read them in that file first). WP6 (localisation, finding card) and WP7
  (goal navigation, Verify/Memory pages) are separate PRs: do not start them here.
- **Branch:** `loganalyzer/wp5-case-home`, from main @ `093d67be` (WP14b #249 merged).

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## Already in place (reuse)
- WP3/WP3b: `Dfir.Core/Case/CaseWorkspace*.cs` (case folder, hash-chained audit + custody, lifecycle states, `Recheck()`, INVALIDATED marker), `LiveCase`.
- WP4: `LogAnalyzer.Verification` (`CaseVerifier`, verdicts) and `InvestigationViewModel.VerificationLine`.
- Pipeline outputs: findings, timeline, `CollectionRow` list, `EvidenceGap` list, `ParseResult` list, `AuditCoverage`, parser capabilities.
- WP-AUTH: `OperatorIdentity` (who opens a case is audited as the signed-in account).
- WP14/WP15: case scope, registers snapshot, procedure profile.

## Spec
1. **Open an existing case (R9.1).** `CaseWorkspace` gains a load path that rebuilds the investigation result (findings, timeline, collection rows, gaps,
   parse results, scope, verification) from the files the pipeline already writes. On open: run the integrity recheck (WP3b, on a background task as LiveCase does),
   audit the open (who = signed-in account), and show the case **read-only** when it is sealed/ARCHIVED/INVALIDATED or the recheck fails. A recent-cases list
   (path, title, last opened, state) is stored per user; a missing folder is shown as missing, not removed silently. Unknown/older file versions: refuse with the reason.
2. **Coverage matrix (U21)** as a backend model in `Dfir.Core`: one row per artifact family (Windows event channels, Prefetch, Amcache, ShimCache, BAM, SRUM,
   registry hives, USN, LNK/JumpList, USB history, network profiles, browser, scheduled tasks/services, ...), each with state
   COLLECTED / PARTIAL / UNAVAILABLE / NOT_COLLECTED / NOT_SUPPORTED, the reason, and the parser status. Overall coverage = FULL / PARTIAL / MINIMAL / UNKNOWN by a
   documented rule. A partial or empty result is never "clean system".
3. **Home aggregator (U3)** as a backend service: answers *is there a problem / how serious / is the evidence trustworthy / what was found / what next*, from
   findings (severity counts), verification verdicts, integrity (chain + recheck), coverage overall and gaps. Attention level never reads SAFE / NORMAL because
   nothing fired: with no findings it says "nimic detectat în sursele analizate" plus the coverage level. Next-step suggestions are data-driven and short.
4. **Home page (App)**: new first page in the sidebar, bound to the aggregator, with the coverage matrix and an "Deschide caz existent" action. No existing page removed.
5. **First-run intent chooser (U22)**: three intents mapped to existing workflows: *Verifică acest calculator* (collection + pipeline), *Analizează probe*
   (import), *Deschide caz existent* (item 1). The question chooser (e.g. "A rulat un program?") is WP7; leave a documented hook only.
6. **Remove misleading static status texts (U3)** listed in row U3 (e.g. "ALL SYSTEMS NORMAL", "SHIELD ARMED & SECURE", "EVIDENCE VAULT SECURED", a preset
   "Lant Criptografic Verificat", "SCAZUT (Normal)"): bind them to real state or show "nedeterminat". Keep the controls and pages; change only the claims.
7. **Both editions**, Romanian UI text (WP6 adds EN later), no host settings changed.
8. **Tests (TDD):** load round trip (write case → load → same findings/timeline/gaps/scope); tampered file → read-only + reason; sealed/INVALIDATED → read-only;
   unknown version refused; coverage roll-up rules incl. empty result ≠ clean; Home aggregator never SAFE with zero findings; App view-model smoke (App.Tests run only
   on Windows CI: grep App.Tests for any text you change and update the assertions).

## Verification required before push
- Build 0 errors. Dfir 48 / UI 6 known Linux-only failures only (diff names against the baseline); Edition green.
- Full Python suite once on the final head → 0 failed. New .md files listed in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.
- Security gates fixed only through their documented exception mechanism, never by obfuscation.

## Done
- 2026-10-09T17:40Z claude-orchestrator: spec written; branch created; STAGE2_PLAN items 2-6 marked done.
- 2026-10-09 claude-wp5: items 1-3, 8 (backend) in `32e655371`: `CaseLoader` (Dfir.Windows), `CoverageMatrix`, `HomeAggregator`, `RecentCases`, read-only guard in `CaseWorkspace`,
  `TimelineCsv` reader/writer shared with the pipeline; `Wp5CaseHomeTests` (38 tests, green on Linux).
- 2026-10-09 claude-wp5: items 4-7 in `6d7b698c1`: `HomeView`/`HomeViewModel` (tab 22, first in the sidebar, default tab), intent chooser, `InvestigationViewModel.ShowLoaded`,
  `MainViewModel` risk default NEDETERMINAT, legacy root UI claims neutralised, `docs/dfir/CASE_HOME.md`, `HomeViewModelTests` (App.Tests, Windows CI only: not run here), `HonestShellTests` +2.

## Next
- Final verification: full Dfir/UI/Edition runs vs baseline, full Python suite, final push.

## Blockers
- None.

## Key files
- `LogAnalyzer.Dfir.Windows/Investigation/CaseLoader.cs`, `HomeBuilder.cs`; `LogAnalyzer.Dfir.Core/Coverage/CoverageMatrix.cs`, `Home/HomeSummary.cs`, `Case/RecentCases.cs`, `IO/TimelineCsv.cs`;
  `LogAnalyzer.App/ViewModels/HomeViewModel.cs`, `Views/HomeView.xaml`; `docs/dfir/CASE_HOME.md`.
