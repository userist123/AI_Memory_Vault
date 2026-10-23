# todo-claude-casa3d-a1-2
STATUS: IN_PROGRESS        UPDATED: 2026-10-23T22:10:00+03:00
TASK: Casa3D A1.2 (phase 1 of todo-claude-casa3d-a1-roadmap.md): editable wall dimensions, joined-wall preview + T snap,
  context menu from the command registry, 3 residual bugs. Spec: 02_PRODUCT/projects/workspaces/casa3d/PLAN_A1_2.md.
BRANCH / PR: claude/casa3d-a1-2-completion, worktree D:/w/a12 (base main dd6c9ba34) / no PR yet. WIP commit local.
DONE (all in the WIP commit):
- core/wall-edit.ts setWallLength (a fixed, b moves, walls sharing old b follow; refuses <20 cm, >1 km, opening out)
  + tests/wall-edit.test.ts (7). Inspector length + plan both call Editor onSetWallLength (toast on refusal).
- core/snap.ts: kind 'wall' (T junction on a wall axis, grid along it, not the wall the anchor sits on) + tests;
  SEGMENT_THICKNESS shared with Editor; preview drawn at real thickness, square cap; DrawHud T glyph.
- lib/commands.ts: menuLabel, group 'context', commands frame (F, display only), wall.length, view3d, room.furnish,
  room.split, column.shape; contextCommands(kind); help row F now from registry. tests/commands.test.ts +4.
- components/PlanContextMenu.tsx (APG menu, arrows/Home/End/Enter/Esc/Tab, click outside, clamped to viewport);
  CSS .plan-menu / .plan-dim-input in app/globals.css; locale keys wallEdit.*, plan.dimEdit*, ctx.* (ro+en).
- PlanView: right-click in Select selects + opens menu (during drawing still cancels); Shift+F10 / Menu key opens at
  selection centre; dimension hit rect >= 44x24 px (data-dim), double-click counted manually in down()
  (pointerdown.detail is 0 in Chrome; capture moves dblclick to svg), input opened via setTimeout (mousedown focus
  would blur it); Enter applies via parseTypedLength, invalid stays red, Esc cancels; menu/input close on zoom/pan.
- Bugs: Esc while drawing cancels drawing even with a selection; Delete/Backspace swallowed while drawing;
  typedEndpoint + its test removed.
- tsc 0; vitest --maxWorkers=2 126 files / 757 tests (before the last PlanView fixes: re-run).
- Browser (launch config casa3d-a12, port 3201; local test project e814273a... "My home", demo apartment):
  menu on window opening (Frame F / Delete), on wall (Edit length / Frame / Delete), arrows+Home ok; Edit length ->
  "30" refused (red + toast "door or window would no longer fit"), "750" applied, corner followed, ONE Ctrl+Z restores;
  double-click on dim 320 opens input, Esc closes with no change, focus back on plan; Shift+F10 opens at selection;
  wall tool: preview at 15 cm thickness snapped onto top wall axis (279.2 deg, T); Delete + Backspace while drawing:
  wall count unchanged (8).
2026-10-23 session 2: phantom Undo NOT REPRODUCED on 3 paths (fresh load; 750 + Ctrl+Z then p/click/Delete/Backspace;
  refused 30 then same): Undo stays disabled, server draft identical. "Unsaved changes" was the transient debounced
  save after Ctrl+Z (draftRev 6->7, chip back to "Saved"); old probe regex read body text. RO checked: menu
  (Editează lungimea / Încadrează / Șterge), input aria "Lungimea peretelui" aria-invalid, toast RO; T glyph path at
  the wall axis x=3.20 when zoomed. tsc 0; vitest 126 files / 757 tests green.
Review round 1 (vault-reviewer): 6 findings, all fixed in commit "fix(casa3d): A1.2 runda 1": zero-length wall refused
  (wallEdit.degenerate), moved neighbours checked (<20 cm -> wallEdit.neighbourShort, openings -> openingOut), Inspector
  length applies on Enter/blur (no T-foot capture, one undo), Ctrl/Cmd+Delete/Backspace swallowed while drawing,
  untouched dim text closes input (imperial ok), same length = no undo step. +3 wall-edit tests. tsc 0; targeted vitest
  42/42; browser verified all. Accepted residual: 90deg corner neighbour becomes slanted (matches spec).
NEXT: full vitest (PowerShell: npx vitest run --maxWorkers=2; bash npx gave exit 127), round-2 review of the delta
  only, PR, CI once, merge if green, mark phase 1 DONE in the roadmap checkpoint.
KEY FILES: app/components/PlanView.tsx, PlanContextMenu.tsx, Editor.tsx (onSetWallLength, SEGMENT_THICKNESS),
  core/wall-edit.ts, core/snap.ts, lib/commands.ts, lib/locales/{ro,en}.ts, app/globals.css, tests/{wall-edit,snap,commands,typed-dimension}.test.ts.
ENV: dev server `casa3d-a12` in repo-root .claude/launch.json (local, not committed); vitest needs --maxWorkers=2.
RESUME: read this file + PLAN_A1_2.md; start from OPEN.
