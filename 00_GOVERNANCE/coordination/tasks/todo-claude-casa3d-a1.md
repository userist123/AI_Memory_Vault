# todo-claude-casa3d-a1
STATUS: IN_PROGRESS        UPDATED: 2026-10-23T01:00:00+03:00
TASK: Casa3D A1.1 — command registry, drawing precision (snap HUD, Shift bypass, typed length), proven undo
BRANCH / PR: claude/casa3d-a1-precision (worktree D:/w/a1, base origin/main f0ee40d67) / not opened yet
SPEC: 02_PRODUCT/projects/workspaces/casa3d/PLAN_A1.md (decisions + slices 1-3)
OWNERSHIP: lib/commands.ts, core/snap.ts, tests/{commands,snap,history}.test.ts (new); Editor.tsx (keydown, commands,
  tool buttons), PlanView.tsx (drawing paths, HUD), KeyboardHelp.tsx, lib/locales/{ro,en}.ts (new keys only).
  Parallel: PR #316 (casa3d P0 hardening) touches Editor.tsx shares section only; PR #312/#314 globals.css tokens.
DONE:
- Research reports: draft PR #318. Inventory + PLAN_A1.md. npm ci in worktree.
- Slice 1 a5cadf11a: lib/commands.ts registry (keys V P C U F T G L S M, I=tech; only if tool in stage), palette Tools group,
  tooltips + aria-keyshortcuts, help generated. Behaviour change: R rotates only without Ctrl/Alt.
- Slice 2a 477060902: core/snap.ts (snapDraw endpoint>15deg>grid, Shift free; pointAtLength; parseLengthInput <100=m, >=100=cm).
- Slice 2b 86350e4ed: PlanView wired (wall/beam/glass + wall end handle), DrawHud dimension line, typed length (capture
  listener: digits , . ' " ; Enter; Backspace; Esc clears then cancels). Help `?` in 3 sections, fits canvas (scrolls at 768).
- Browser (local dev, casa3d-a1 launch config port 3200, PGlite): 4x5 room typed -> walls 4.0000/5.0000/4.0000/5.0000 closed;
  Shift -> 9.4deg/2.57m circle glyph vs 15deg/2.55m arc; 3x Ctrl+Z restores; palette Tools + keys; help EN+RO.
  Contrast #B8432C >= 4.68:1 on plan backgrounds. tsc 0; vitest 110 files / 674 tests.
- Slice 3 (this commit): tests/history.test.ts, 11 tests (seeded 60-edit undo/redo invariant, immutability, limit 100/3,
  push clears redo, begin/rollback incl. refusal + limit, discardLast, empty stacks). No History bug found.
  Duplicate tooltip now from registry. Full suite: tsc 0; vitest --maxWorkers=2 111 files / 685 tests (default
  workers OOM while dev server runs).
- Review round 1 (vault-reviewer Opus): CHANGES_REQUESTED, 3 findings fixed in this commit: stair key S->E (S walked
  3D and switched tool), bare number >30 = cm (80 -> 0.8 m) + m/c accepted while typing, typed/preview reset on tool
  change. Browser-verified (80cm -> 0.8000 m; V/P then Enter adds nothing; S no-op, E stair). vitest 111/686.
- Merged origin/main (f1b0a3ac4; allowlist union). PR #323 draft opened.
- Review round 2 (delta): fixes confirmed; 1 finding fixed here: help rule text per units (imperial = feet) + imperial
  ft/in letters and space stay on the dimension. Browser: imperial "12 ft" -> 3.6580 m, help text correct. Local dev now
  needs `npm run dev` (CASA3D_LOCAL_TRUST from main #316). vitest 115/716.
- PR #323 marked ready. Owner (2026-10-23): no red at merge, without compromising anything.
- Root cause of red "Dependency audit": next 16.4.0 critical RCE in imported trading-journal. Fixed in PR #325
  (branch claude/fix-next-critical-trading-journal, worktree D:/w/tj-next): next 16.4.2, audit critical 0, tsc 0;
  local next build blocked by machine SWC cache ACL (env, CI does not build it).
NEXT: when #325 CI green -> merge #325; merge origin/main into #323; CI green -> merge #323. Then remove casa3d-a1 from .claude/launch.json. Previously: vault-reviewer (Opus) on diff origin/main..HEAD; fix; push; draft PR; send screenshots (re-take on PR build;
  previous ones live only in the old session). Then remove casa3d-a1 from .claude/launch.json (local, untracked) when done.
RESIDUALS (record in PR): RO header wraps "Salveaza revizia" at 1440 (pre-existing?);
  palette "Keyboard shortcuts (?)" label + hint "?" duplicate (pre-existing); 75deg wall typed 4 -> 4.0002 (mm rounding).
BLOCKERS: none
RESUME: read this file + PLAN_A1.md; start from NEXT. Commits are local until the push step.
