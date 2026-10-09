# Checkpoint — claude-wp14b (LogAnalyzer WP14b: combined sequences)

- **Task:** last part of queue item 6 in `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`.
  Lessons-learned rows 63, 69, 82 (`docs/dfir/LESSONS_LEARNED_MAPPING.md`). Owner decisions 19 (zones/transfers), 24 (no "Conform" on absence),
  33/34 (signed-in identity as *who*).
- **Branch:** `loganalyzer/wp14b-sequences`, from main @ `154dc427` (WP14a #246 and WP-AUTH #247 merged).
- The fourth sequence (classified document → print → scan → USB) waits for WP13 / WP16a. Do not implement it here.

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## Already in place (reuse, do not duplicate)
- WP15b: `Dfir.Core/Analysis/PolicyTimeline.cs`, rule `POLICY-CONTROL-GAP` (a control off / removed and when it came back).
- WP14a: `Wp14Rules*.cs` (MEDIA-* incl. `MEDIA-FILE-ACTIVITY` with write vs presence evidence, AIRGAP-*), `Wp14Data` + `Analysis/Data/airgap_lists.json`,
  `Finding.AirGap`, `Wp14Analysis` called from `InvestigationPipeline` after `Correlation.Run`.
- WP11-T1: `Wp11Rules*.cs` (SMB 5140/5145, 4663 object access, accounts, RDP/WinRM, security agents), data lists in `Analysis/Data/`.
- Execution artifacts: Prefetch / BAM / Amcache / ShimCache parsers (path + time; presence vs execution semantics).
- WP4 verifier: every new finding gets a verdict; `RuleContracts.cs` + `20_TESTS/test_loganalyzer_rule_catalog.py` must list new rule ids.
- Generic `INCIDENT-CHAIN` in `Correlation.cs` stays; the new rules are specific sequences, not a replacement.

## Spec
1. **One sequence engine, data-driven windows.** A small pure helper in `Dfir.Core/Analysis/` that orders steps on the UTC timeline and joins
   them by host, account (where known), drive letter / volume and time window. Windows (minutes/hours) live in a data JSON next to the WP14a lists,
   not in code. Each step records: rule/source, time, account, object, evidence reference.
2. **SEQ-CONTROL-GAP-MEDIA (row 63):** a `POLICY-CONTROL-GAP` (USB/device-install/audit/Defender control off or removed) followed, inside the gap
   or within the window after it, by media activity (`MEDIA-*` observation or `MEDIA-FILE-ACTIVITY`). Title states the facts in order
   ("control X dezactivat la …; mediu Y conectat la …; control restabilit la …"). Severity: High on a classified scope, Medium otherwise;
   Critical only with write evidence on an unregistered/unauthorized medium during the gap on a classified scope.
3. **SEQ-SMB-STAGING-USB (row 69, without NAS):** remote share access (5140/5145, or 4663 on a network path) → local staging (files created/written
   under a local folder: USN / LNK / 4663 write) → write to removable media (`MEDIA-FILE-ACTIVITY` write evidence). Join by file name or by
   staging folder where evidence allows; otherwise report the sequence as "corelare temporală, fără legătură dovedită între fișiere".
4. **SEQ-PORTABLE-USB-ARCHIVE (row 82):** execution of software from a removable drive or a user-writable path first seen in the period
   (Prefetch/BAM/Amcache), plus creation of a large archive (.zip/.7z/.rar/.tar/.gz, size threshold in the data JSON, where size is known), plus writes to
   removable media. Tool names for archivers/portable tools come from the data list.
5. **Honesty rules (decision 24 and the WP4 contract):**
   - Never assert intent or exfiltration. Say what was observed, in order, and which link is missing.
   - A step whose source was not collected is "pas neobservat (sursa X indisponibilă)", never skipped silently and never treated as absent.
   - With fewer steps than the minimum (document it per rule), no sequence finding; the individual findings stay as they are.
   - Each sequence lists its constituent findings (ids) so the investigation view can navigate to them.
6. **Who:** the account from the evidence; never the signed-in app user (that is the examiner, not the subject).
7. **UI:** the investigation view shows sequence findings in their category with the ordered steps. No page removed. Both editions.
8. **Tests (TDD):** each rule positive and negative; window boundaries; missing source → "pas neobservat"; minimum steps; no intent words in titles
   (test a deny-list of words such as "exfiltrare", "intenționat", "furt"); verifier verdict present; Edition green; rule catalog in sync.

## Verification required before push
- Build 0 errors.
- Dfir 48 / UI 6 known Linux-only failures only (diff the failing names against the baseline); Edition green.
- Full Python suite once on the final head → 0 failed.
- New .md files are listed in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.
- Security gates are fixed only through their documented exception mechanism, never by obfuscation (COMMON_EXEC rule).

## Done
- 2026-10-09T14:50Z claude-orchestrator: spec written; branch created; 6b marked done in STAGE2_PLAN.
- 2026-10-09 claude-wp14b: items 1-6 and 8 in code. `SequenceEngine` (pure: Follows / Within / JoinAccount / JoinHost / JoinVolume / Order / NotObserved), `SequenceData` + `Data/sequence_rules.json`
  (windows, size threshold, minimum steps, tool lists), `SequenceRules*.cs` (3 rules), `Finding.Sequence` (`SequenceDetail`), 3 catalog entries, call in `InvestigationPipeline` after the policy timeline,
  61 tests in `LogAnalyzer.Dfir.Tests/Wp14bSequenceTests.cs` (red run with the rules disabled: 28 failed; green after). Dfir 48 failing = baseline names, UI 6 = baseline, Edition 15/15.

- 2026-10-09 claude-wp14b: item 7. "Secvențe" tab in `InvestigationView.xaml` (shared by both editions: App.Classified links the same view), `InvestigationViewModel.SequenceFindings / SequenceSummary / SequenceSteps`,
  one added assertion in `LogAnalyzer.App.Tests/ViewModelSmokeTests.cs` (App.Tests run on Windows CI only; no existing text was changed). Slnx builds, 0 errors.

## Next
- Final checks: Dfir/UI/Edition tests vs baseline, full Python suite once, final report.

## Blockers
- None. Owner questions (defaults until reviewed): windows (control gap +120 min after; SMB to staging 120 min; staging to medium 240 min; program to archive 480 min; archive to medium 240 min),
  archive size threshold 100 MB, severities (CGM High/Medium, Critical only with write on an unregistered or unauthorized medium in the gap on a classified scope; SMB and portable: High with a
  proven link or three firm steps on a classified scope, one level lower otherwise), control words for the gap (usb, removable, device install, audit, defender, antivirus ...).
- Known limits to decide with the owner: `POLICY-CONTROL-GAP` carries no restoration time, so "control restabilit" is always "pas neobservat" until WP15b exposes the restoration; USN rows
  without a drive letter cannot be used as local staging (they cannot be told from the removable volume); Prefetch/BAM carry no account, so portable-software steps show "cont necunoscut".
