# todo-claude-casa3d-a1
STATUS: IN_PROGRESS        UPDATED: 2026-10-23T17:00:00+03:00
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
- #325 MERGED (02c63629b): audit npm green. Local Next build needs SWC_NATIVE_BINDING_CACHE=%USERPROFILE%\.swc-native-cache
  (owner-only ACL; AppData\Local and D:\w ACLs are refused by @swc/core).
- Merge of origin/main into #323 ABORTED (branch untouched): main got #321 (f27d1dde7) with its OWN typed lengths:
  core/typed-dimension.ts (parseTypedLength: integer=cm, decimal=m, MIN .2 m; parseTypedSize "4x5"; typedEndpoint;
  typedRect; acceptsTypedKey), two-click room (roomDraft.pending), typed chip near cursor, keys via svg onKeyDown
  (svg focused on click), state names `typed`/`setTyped` collide with A1. Also Editor imports prepareShareImages.
- OWNER DECISION 2026-10-23: keep main's number rule (integer = cm, decimal = m; "4" rejected).
- Merge f9e763ae9 (origin/main 02c63629b) DONE per plan below, plus: palette had duplicate `tool-*` ids from both
  sides -> one entry (shortcut + active + keywords); duplicate locale key cmdk.group.tools removed; help keys
  help.exMetric/exImperial. tsc 0; vitest 119/729. Browser 127.0.0.1:3200: 400->400, 4,25->425, room 400x500 exact
  4x5, beam 250 + Shift 2.500 m @13.5deg, 2mcift eaten (tool unchanged), invalid Enter keeps text, S no-op/E stair,
  help EN+RO, palette single Perete (P) / Selectie "activa · V"; test edits undone. /shares 500 once = dev compile
  race (retry 200). pytest strict_yaml: 92 unlisted all in local node_modules (env, not CI).
- Delta review of the merge (vault-reviewer Opus): APPROVE; tsc 0, vitest 119/729, parser probes ok. Low finding fixed:
  click-without-move + typed length went toward the stale hover point (e.g. 30deg) -> first click sets raw = start
  point, so Enter goes right. Browser: click, "150", Enter -> horizontal 150 wall (undone). tsc 0, vitest 119/729.
  Residual (not merge-caused, already on main): typedEndpoint unused in production; Esc with a selection while
  drawing deselects first; Delete while typing reaches del().
- Pushed 48d528351; #323 then CONFLICTING: main got #327 (UI/UX P2) + #328. Second merge (origin/main 3e5c5192e):
  conflicts KeyboardHelp (kept registry) + PlanView imports (+ core/frame). #327 added F = frame selection in PlanView.
  OWNER DECISION 2026-10-23: F = frame, Window tool -> O. lib/commands.ts window 'o', help row F/help.frame,
  test "F is not a tool key"; PLAN_A1.md key list updated. Help dialog fix: .pending is centred with left:50% so it
  shrank to half the canvas (descriptions wrapped per word at 1024) -> explicit width min(560px, 100% - 24px).
  Browser: O -> Fereastra (tooltip "(O)"), F (plan focused) frames, F unfocused no tool change, help RO lists F and
  O, 560 px no overflow; typed 4,25mx line-through, 4,25 -> 425 (undone). tsc 0; vitest 123/739.
- Merge commit a52eacee9. Full pytest in D:/w/a1: 9 failed (b2m/book_to_memory/corpus_health/strict_yaml/
  vault_state) - all working-tree scanners hit local casa3d node_modules/.next; same 6 files on a clean worktree of
  HEAD: 111 passed. (An earlier full run also had 703 order-dependent errors; the file passes alone 158/158 and the
  rerun had 0 errors.) Pushed; #323 MERGEABLE, CI started.
- 2026-10-23 CI #323: all green except R009b zero-regression-gate = test_notes_strict_yaml unlisted 4 LogAnalyzer
  no-frontmatter files from #306-#308; main 132a8eec6 red on the same test. Fix PR #332 (branch
  claude/fix-main-unreadable-loganalyzer-notes, worktree D:/w/fixyaml): 4 plain_document allowlist entries; local 10 passed.
  #332 CI: 36/36 pass incl. zero-regression-gate. Merge of #332 by the agent DENIED by auto-mode classifier
  (Merge Without Review) -> owner said "continua"; #332 MERGED 6aedeac80. Merged origin/main into A1 (6e7bf9203,
  no conflicts; main touched no casa3d file, only LogAnalyzer + allowlist). Pushed; #323 CI rerunning.
NEXT: #323 CI green -> merge #323. Old step: #332 merged -> then update #323 with main (gh pr update-branch 323) so the gate reruns; green -> merge
  #323. Earlier plan: check #323 CI once (ccd_pr get_status / gh pr checks 323). Green -> merge #323 (owner: no red at
  merge, nothing compromised); red -> read the failing job log, fix, push. After merge: mark STATUS DONE here.
  Follow-ups (not blocking): typedEndpoint unused in production; Esc with a selection while drawing deselects
  first; Delete while typing reaches del() (both pre-existing on main).
OLD PLAN (done):
  1. One parser: drop parseLengthInput from core/snap.ts (+ its tests, help.rule* keys); walls/beam/glass use
     parseTypedLength. Keep snapDraw/pointAtLength (pointAtLength keeps the 15-deg direction + Shift free).
  2. One key path: keep main's svg onKeyDown + acceptsTypedKey for wall AND room; delete A1's window capture listener
     (keep its guarantees: Backspace/Esc/Enter never reach Editor while typing - svg handler already stopPropagation;
     verify tool letters m/c/f/t/i are eaten after a digit, as acceptsTypedKey does). Extend `typing` to beam/glass_wall.
  3. Display: segments (wall/beam/glass) use A1 DrawHud (dimension line shows `typed`, invalid = line-through);
     room keeps main's typed chip. Remove main's old preview <text> label for segments (DrawHud replaces it).
  4. Keep main's two-click room + typedRect; keep A1 reset of typed/preview on tool change.
  5. Help: draw-section text from main's rule (example "320 sau 3,20; camera 4x5"), RO+EN; tests commands/i18n.
  6. Editor.tsx imports: keep both (commands + prepareShareImages). Allowlist: union.
  7. tsc, vitest --maxWorkers=2, browser (npm run dev, 127.0.0.1:3200): wall "400"->4.000 m, "4,25"->4.25, room
     "400x500", Shift free, S no-op/E stair, help RO/EN. Delta review (vault-reviewer) of the merge. Push, CI, merge #323.
RESIDUALS (record in PR): RO header wraps "Salveaza revizia" at 1440 (pre-existing?);
  palette "Keyboard shortcuts (?)" label + hint "?" duplicate (pre-existing); 75deg wall typed 4 -> 4.0002 (mm rounding).
BLOCKERS: none
RESUME: read this file + PLAN_A1.md; start from NEXT. Commits are local until the push step.
