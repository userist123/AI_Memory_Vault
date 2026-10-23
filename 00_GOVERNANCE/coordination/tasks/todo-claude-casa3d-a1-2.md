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
OPEN (investigate first): after p, click, hover, Delete, Backspace the Undo button became ENABLED and status
  "Unsaved changes", selection cleared, geometry identical (8 walls, 9 openings, 19 placements, 5 rooms). Find what
  was committed: compare GET /api/projects/<id> draft before/after one Ctrl+Z (window.__before was being captured).
  Suspects: Shift stuck after shift+F10 (first click came out free, 3.589/1.957 not on grid), Editor tool switch,
  an empty commit. Then: browser check of RO texts + T glyph at zoom, re-run tsc/vitest, vault-reviewer (Opus) on the
  diff (max 2 rounds), PR, CI once, merge if green, mark phase 1 DONE in the roadmap checkpoint.
KEY FILES: app/components/PlanView.tsx, PlanContextMenu.tsx, Editor.tsx (onSetWallLength, SEGMENT_THICKNESS),
  core/wall-edit.ts, core/snap.ts, lib/commands.ts, lib/locales/{ro,en}.ts, app/globals.css, tests/{wall-edit,snap,commands,typed-dimension}.test.ts.
ENV: dev server `casa3d-a12` in repo-root .claude/launch.json (local, not committed); vitest needs --maxWorkers=2.
RESUME: read this file + PLAN_A1_2.md; start from OPEN.
