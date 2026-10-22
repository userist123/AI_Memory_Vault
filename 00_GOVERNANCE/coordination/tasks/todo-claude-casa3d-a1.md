# todo-claude-casa3d-a1
STATUS: IN_PROGRESS        UPDATED: 2026-10-22T23:30:00+03:00
TASK: Casa3D A1.1 — command registry, drawing precision (snap HUD, Shift bypass, typed length), proven undo
BRANCH / PR: claude/casa3d-a1-precision (worktree D:/w/a1, base origin/main f0ee40d67) / not opened yet
SPEC: 02_PRODUCT/projects/workspaces/casa3d/PLAN_A1.md (decisions + slices 1-3)
OWNERSHIP: lib/commands.ts, core/snap.ts, tests/{commands,snap,history}.test.ts (new); Editor.tsx (keydown, commands,
  tool buttons), PlanView.tsx (drawing paths, HUD), KeyboardHelp.tsx, lib/locales/{ro,en}.ts (new keys only).
  Parallel: PR #316 (casa3d P0 hardening) touches Editor.tsx shares section only; PR #312/#314 globals.css tokens.
DONE:
- Research reports committed: draft PR #318 (branch claude/frontend-research-2026, hygiene 0, personal data PASS).
- Inventory of origin/main (Explore agent), plan written. npm ci done in D:/w/a1/.../casa3d/app; baseline tsc exit 0.
- Skills loaded for this phase: creative-web-director + frontend-design:frontend-design (load both again before UI code).
- Slice 1 DONE (TEST_VERIFIED, browser check pending): lib/commands.ts + tests/commands.test.ts; keys V P C U F T G L S M,
  I=tech; tsc 0, vitest 109 files/658 tests pass. Residual: Ctrl+D title in placement panel (Editor ~L872) still literal.
NEXT (in order):
1. Slice 2 precision (core/snap.ts + tests first, then PlanView HUD).  3. Slice 3 history tests.
4. tsc, vitest, next build; real-PC browser check with screenshots (RO+EN); vault-reviewer (Opus); draft PR.
BLOCKERS: none
RESUME: new session (previous one passed 250k context). Read only this file + PLAN_A1.md, then dispatch slice 1 to
  vault-worker (sonnet) with the slice spec from PLAN_A1.md; tests first; verify yourself; one worker at a time.
