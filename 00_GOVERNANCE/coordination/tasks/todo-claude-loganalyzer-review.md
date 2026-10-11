# todo-claude-loganalyzer-review

- **Task:** apply the senior review `02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/REVIEW_2026-10-11_SENIOR_DETECTION_AND_PERFORMANCE.md`
  (detection, collection, alerting, attribution, explanation, performance) to LogAnalyzer.
- **Branch:** `claude/loganalyzer-dfir-roles-ui-0a71fb` (pushed; no PR yet).
- **Updated:** 2026-10-11.

## Done (committed)
- Performance actions 1–2 (analysis off the UI thread, DB compaction + indexes), L1, L3, L4, L5, L8, L9, I1, A5 (RunMRU), C3.

## In progress
- Detection batch D11–D23 (Dfir rules) — worker, uncommitted until it reports.
- L10/L11/L12/L13/L15 live alerts: queue + batched UI timer, de-duplication (`LiveAlertThrottle`), per-station/per-source failed-logon
  counters, spray rule, no bare-keyword alerts, one technique per alert. MainViewModel edited; Core patch and tests staged in the session scratchpad.

## Next
- E1–E3 explanation gaps (EventMeaning ids, ItemMeaning kinds, domain/mail check guides).
- Legacy engine noise D1, D2, D4–D7.
- L2 persist live alerts; L6, L7; C4, C5; D8–D10, D16–D18, D20.

## Blockers
- System disk C: ~150 MB free: worktree isolation for parallel workers is impossible (each worktree ~605 MB); one worker at a time in this tree.
  Cleanup is the owner's decision (temp folder 8.4 GB, NuGet cache 0.7 GB, old worktrees).
