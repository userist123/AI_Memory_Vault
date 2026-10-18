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
- Implemented Slice 7 (Dynamic Area-Adaptive Auto-Layout & Proportional Furniture Sizing): scaled room furnishing dynamically for spacious rooms (`furnish.ts`: living >= 23.5m² receives armchairs, sideboard dresser, up to 4 bookcases; bedroom >= 19m² receives dresser, >= 22m² reading armchair/secondary wardrobe; kitchen >= 12m² receives auxiliary sideboard; bath >= 7m² receives towel radiator and bath column tower; hall >= 8.5m² receives entry console and hall chair). Calibrated clearances (`fpHit` 1e-4 tolerance) preventing false-positive overlap collisions. In compact/medium rooms (< 22m²), prioritize compact coffee tables (75x75, 80x60, 69x69) over oversized 118x78 models.
- Calibrated 2D canvas symbols (`plan-symbols.ts`): tucked dining chairs from 0.3m outward offset to 0.12m tucked-in position under table, eliminating perceived oversized dining footprint while maintaining realistic clearance checks.
- Implemented Section 17.1 (Secure Imports & File Hardening): created `app/lib/secure-import.ts` with authentic magic bytes validation (PNG, JPEG, PDF, ZIP, GLB), size limit enforcement, path traversal filtering (`sanitizeImportFilename`), and decompression bomb ratio protection (`assertSafeDecompressionRatio`).
- Verified full test, typecheck & build suites: `@casa3d/twin-core` 59/59 PASS, `app` `tsc --noEmit` 0 errors, `app` Vitest 601/601 PASS (99 test suites), exit code 0.
NEXT (in order):
- Merge PR #304 & PR #305 and monitor designer feedback.
BLOCKERS: None.
KEY FILES:
- 02_PRODUCT/projects/Casa3D/{NEXTGEN_AUDIT,NEXTGEN_IMPLEMENTATION,VERIFICATION_2026-10-10}.md
- 02_PRODUCT/projects/workspaces/casa3d/packages/twin-core/src/{twin,engine,index}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/core/{furnish,plan-symbols,room-edit,validate,underlay,styles,technical,setting-out,user-library,coordination-review}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/lib/{db,repo,twin,design,secure-import}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/components/{Editor,PlanView,Viewer3D,TwinDesignPanel,StylePanel,TechPanel,viewer3d-engine.js}
