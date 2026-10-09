# Checkpoint — claude-wp6b (LogAnalyzer WP6b: Romanian / English resource layer)

- **Task:** third part of queue item 7 in `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`; row **U17** in
  `docs/dfir/CONTRACT_AUDIT_STAGE1.md` (Romanian first-class, English supported, identifiers untranslated). WP7 (goal navigation) is the next PR.
- **Branch:** `loganalyzer/wp6b-localisation`, from main @ `21da5bbf` (WP6a #253 merged).

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## Already in place (reuse)
- WP6a: `Dfir.Core/Language/Glossary.cs` (RO + EN per term), `StateLabels` (10 verification states), `{views:Term}` XAML extension,
  presentation models (`FindingCardModel`, `WhyExplainer`, `EvidenceLevels`, `KnowThinkDontKnow`, `SeverityLabels`).
- WP5: Home page and aggregator, coverage matrix; WP-AUTH sign-in window; WP14 registers; WP15 procedure profile.

## Spec
1. **Resource layer.** One key → text table per language (RO default, EN), usable from C# and XAML (a markup extension such as `{loc:T Key}`),
   with a runtime language switch that updates bound text without restarting. Missing EN text falls back to RO and is reported by a test, never shown as a
   raw key. Identifiers (rule ids, event ids, channel names, file names, hashes) are never translated. Prefer .resx or a small JSON/C# table; no new
   third-party dependency.
2. **Key-based backend messages.** `Finding`, `EvidenceGap` and status texts that the UI shows gain stable keys (title_key / summary_key or equivalent)
   *in addition to* the current Romanian text, so stored cases stay readable. New code paths use keys; existing rule titles keep their Romanian text and get
   keys where a rule is touched. Reports keep Romanian by default; the export language follows the setting.
3. **Apply to (this PR):** main window shell and sidebar, Home, sign-in window and auth pages, the investigation page with the WP6a finding card and
   Know/Think/Don't-know panel, the coverage matrix, the registers pages, the procedure profile page, state/severity/glossary labels (switch them to the
   resource layer instead of their own RO/EN columns where that removes duplication). Fix the leftover English word "UNPROVEN" inside a Romanian sentence
   in `AiAnalysisViewModel` and update the App.Tests assertion that depends on it.
4. **Not in this PR:** legacy pages (dashboard, event explorer, MITRE, threat intel, etc.). List every page not yet localised in `docs/dfir/LOCALISATION.md`
   with its status, so WP7/later work can finish them. No page removed.
5. **Settings:** language choice stored per user (same place as other per-user settings), default Romanian, shown in the UI.
6. **Both editions**, no host settings changed.
7. **Tests (TDD):** every key present in RO and EN (or explicitly marked RO-only with a reason); no raw key rendered (scan the localised XAML for
   literal text that should be a key, in the pages of item 3); switching language updates a bound view model; identifiers untouched; stored case loaded
   from WP5 still displays. App.Tests run only on Windows CI: grep App.Tests and UI.Tests for every text you change and update the assertions in the same
   commit.

## Verification required before push
- Build 0 errors. Dfir 48 / UI 6 known Linux-only failures only (diff names against the baseline); Edition green.
- Full Python suite once on the final head → 0 failed. New .md files listed in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.
- Security gates fixed only through their documented exception mechanism, never by obfuscation.

## Done
- 2026-10-09T19:50Z claude-orchestrator: spec written; branch created.

## Next
- Implement 1-7.

## Blockers
- None known. English wording of domain terms (classification levels, legal terms) is a draft until the owner reviews it.
