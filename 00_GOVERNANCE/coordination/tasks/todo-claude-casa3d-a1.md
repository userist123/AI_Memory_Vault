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
NEXT: vault-reviewer (Opus) on diff origin/main..HEAD; fix; push; draft PR; send screenshots (re-take on PR build;
  previous ones live only in the old session). Then remove casa3d-a1 from .claude/launch.json (local, untracked) when done.
RESIDUALS (record in PR): RO header wraps "Salveaza revizia" at 1440 (pre-existing?);
  palette "Keyboard shortcuts (?)" label + hint "?" duplicate (pre-existing); 75deg wall typed 4 -> 4.0002 (mm rounding).
BLOCKERS: none
RESUME: read this file + PLAN_A1.md; start from NEXT. Commits are local until the push step.
