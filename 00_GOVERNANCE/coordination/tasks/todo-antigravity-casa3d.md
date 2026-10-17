# todo-antigravity-casa3d
STATUS: DONE    UPDATED: 2026-10-17T23:34:00+03:00
TASK: Casa3D Next-Gen Upgrade (PR #304) — functional engineering (Workstream A), UI/UX & accessibility (Workstream B), 3D lifecycle/performance (Workstream C), and regression testing (Workstream D)
BRANCH / PR: gemini/casa3d-nextgen-functionality-uiux / PR #304
SPEC: 00_GOVERNANCE/coordination/projects/CASA3D/GEMINI_4_ARGON_NEXTGEN_UPGRADE_PROMPT.md
DONE:
- Installed and adapted `cost-router` for Antigravity (`~/.gemini/config`), Codex (`~/.codex`), Gemini CLI (`~/.gemini`), and Qwen Code (`~/.qwen`) with strict enforcement that subagents never use the main chat model (`Model="flash"` / `"flash_lite"`).
- Reconciled `main`, PR #257 (`origin/claude/casa3d-opinion-52d7cf`), and PR #258 (`origin/codex/casa3d-memory`) onto `gemini/casa3d-nextgen-functionality-uiux` (PR #304).
- Implemented Slice 1A: Piece facing & directional `frontClearance` / `frontFootprint` + `FRONT_CLEARANCE_BLOCKED` in `@casa3d/twin-core` (`twin.ts`, `engine.ts`, `index.ts`) and wired `FRONT_CLEARANCE` in `app/lib/twin.ts`.
- Implemented Slice 1B: `edgeHasFreeSpan` & `edgeHandlePoint` in `app/core/room-edit.ts` + `PlanView.tsx` offset handle priority so room edges 100% covered by a door remain resizable on the 2D canvas.
- Implemented Slice 2: Fixed redundant Undo entry on `WARNING` revert (`pending.discard?.()` in `app/components/Editor.tsx`).
- Implemented Slice 3: Interactive validation error-to-object focus path (`issueTarget` in `app/core/validate.ts`, keyboard/click selection in `Editor.tsx` and `TwinDesignPanel.tsx`).
- Implemented Slice 4: Full WebGL resource disposal (`house`, `furniture`, `contact`, `probes`, `ground`, `sky`, owned textures/materials), `webglcontextlost`/`webglcontextrestored` recovery, dynamic `prefers-reduced-motion` listener, and keyboard camera navigation in `Viewer3D.tsx` and `viewer3d-engine.js`.
- Implemented Slice 5: Cross-platform Python binary resolution (`win32 ? 'python' : 'python3'`) across Vitest suites (`project-export`, `commercial`, `contract`, `commercial-review`).
- Wrote `02_PRODUCT/projects/Casa3D/NEXTGEN_AUDIT.md` and `02_PRODUCT/projects/Casa3D/NEXTGEN_IMPLEMENTATION.md`.
- Verified full test & typecheck suites: `@casa3d/twin-core` 59/59 PASS (10 files), `app` `tsc --noEmit` 0 errors, `app` Vitest 586/586 PASS (97 files).
NEXT (in order):
- None (ready for review on PR #304).
BLOCKERS: None.
KEY FILES:
- 02_PRODUCT/projects/Casa3D/{NEXTGEN_AUDIT,NEXTGEN_IMPLEMENTATION,VERIFICATION_2026-10-10}.md
- 02_PRODUCT/projects/workspaces/casa3d/packages/twin-core/src/{twin,engine,index}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/core/{room-edit,validate}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/components/{Editor,PlanView,Viewer3D,TwinDesignPanel,viewer3d-engine.js}
