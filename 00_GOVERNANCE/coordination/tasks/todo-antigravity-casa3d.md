# todo-antigravity-casa3d
STATUS: DONE    UPDATED: 2026-10-18T00:38:00+03:00
TASK: Casa3D Next-Gen Upgrade (PR #304 & PR #305) — functional engineering (Workstream A), UI/UX & accessibility (Workstream B), 3D lifecycle/performance (Workstream C), Pro Editor Master Prompt (§14.1–14.9), and regression testing (Workstream D)
BRANCH / PR: gemini/casa3d-nextgen-functionality-uiux (PR #304) & prompt/casa3d-pro-editor-2026 (PR #305)
SPEC: 00_GOVERNANCE/coordination/projects/CASA3D/GEMINI_4_ARGON_NEXTGEN_UPGRADE_PROMPT.md, 02_PRODUCT/projects/Casa3D/GEMINI_4_ARGON_PRO_EDITOR_MASTER_PROMPT.md, 02_PRODUCT/projects/Casa3D/GEMINI_4_ARGON_FLEXIBLE_INTERIOR_DESIGN_UPGRADE_PROMPT.md
DONE:
- Installed and adapted `cost-router` for Antigravity (`~/.gemini/config`), Codex (`~/.codex`), Gemini CLI (`~/.gemini`), and Qwen Code (`~/.qwen`) with strict enforcement that subagents never use the main chat model (`Model="flash"` / `"flash_lite"`).
- Reconciled `main`, PR #257 (`origin/claude/casa3d-opinion-52d7cf`), PR #258 (`origin/codex/casa3d-memory`), and PR #305 (`origin/prompt/casa3d-pro-editor-2026`).
- Implemented Slice 1A: Piece facing & directional `frontClearance` / `frontFootprint` + `FRONT_CLEARANCE_BLOCKED` in `@casa3d/twin-core` (`twin.ts`, `engine.ts`, `index.ts`) and wired `FRONT_CLEARANCE` in `app/lib/twin.ts`.
- Implemented Slice 1B: `edgeHasFreeSpan` & `edgeHandlePoint` in `app/core/room-edit.ts` + `PlanView.tsx` offset handle priority so room edges 100% covered by a door remain resizable on the 2D canvas.
- Implemented Slice 2: Fixed redundant Undo entry on `WARNING` revert (`pending.discard?.()` in `app/components/Editor.tsx`).
- Implemented Slice 3: Interactive validation error-to-object focus path (`issueTarget` in `app/core/validate.ts`, keyboard/click selection in `Editor.tsx` and `TwinDesignPanel.tsx`).
- Implemented Slice 4: Full WebGL resource disposal (`house`, `furniture`, `contact`, `probes`, `ground`, `sky`, owned textures/materials), `webglcontextlost`/`webglcontextrestored` recovery, dynamic `prefers-reduced-motion` listener, and keyboard camera navigation in `Viewer3D.tsx` and `viewer3d-engine.js`.
- Implemented Slice 5: Cross-platform Python binary resolution (`win32 ? 'python' : 'python3'`) across Vitest suites (`project-export`, `commercial`, `contract`, `commercial-review`).
- Implemented Slice 6 (PR #305 Master Prompt §14.1–14.9): blank canvas (`template: 'empty'`), `splitRoom` & `mergeRooms` (wired in `RoomPanel`), calibrated `traceRoomFromUnderlay`, scoped styles (`previewStyle`, `applyStyleScoped`, `createCustomStylePackage` + `StylePanel.tsx` UI), parametric user library & guarded GLB validation (`user-library.ts`), HVAC & switch groups (`assignSwitchGroup`, `buildTechnicalSchedule` in `technical.ts`), `customSurfaceSettingOut` (`setting-out.ts`), and `reviewProjectCoordination` (`coordination-review.ts`) + `tests/pro-editor.test.ts`.
- Verified full test, typecheck & build suites: `@casa3d/twin-core` 59/59 PASS (10 files), `app` `tsc --noEmit` 0 errors, `app` Vitest 591/591 PASS (98 files), `next build` exit 0, Vault `pytest` 47/47 PASS.
NEXT (in order):
- None (ready for review on PR #304 and PR #305).
BLOCKERS: None.
KEY FILES:
- 02_PRODUCT/projects/Casa3D/{NEXTGEN_AUDIT,NEXTGEN_IMPLEMENTATION,VERIFICATION_2026-10-10}.md
- 02_PRODUCT/projects/workspaces/casa3d/packages/twin-core/src/{twin,engine,index}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/core/{room-edit,validate,underlay,styles,technical,setting-out,user-library,coordination-review}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/components/{Editor,PlanView,Viewer3D,TwinDesignPanel,StylePanel,TechPanel,viewer3d-engine.js}

