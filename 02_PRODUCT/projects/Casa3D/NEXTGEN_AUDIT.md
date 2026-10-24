---
id: "7f4183b0-9c2a-4e11-8b40-15678a90c1d2"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, nextgen-audit, architecture-review, ui-ux]
created: "2026-10-17"
updated: "2026-10-17"
provenance:
  source_type: execution
  source_ref: "PR #304 reconciliation of main, PR #257 (claude/casa3d-opinion-52d7cf), and PR #258 (codex/casa3d-memory)"
  source_date: "2026-10-17"
  redaction: not_applicable
  provenance_status: complete
confidence: high
verification: unverified
relations:
  - type: related_to
    target_id: "28890ae9-095d-4d32-a374-54cd7996d9aa"
  - type: related_to
    target_id: "4e17d26a-74a6-408a-b0f4-069dfd20ac95"
  - type: related_to
    target_id: "d4785be1-7a70-4e45-b7ba-214e460501d4"
---

# Casa3D — Next-Gen Engineering, Functionality & UI/UX Audit (PR #304)

## 1. Branch & PR Reconciliation

| Source | Branch / Commit | Key Artefacts | Status |
|---|---|---|---|
| `main` | `0fc389d` | `02_PRODUCT/projects/Casa3D/CORE_IMPLEMENTATION_v8.md`, `SOURCE_SNAPSHOT_v8.md`, `00_GOVERNANCE/coordination/projects/CASA3D/GEMINI_4_ARGON_NEXTGEN_UPGRADE_PROMPT.md` | Contains only v8 snapshot documentation and the upgrade prompt; executable code was missing on `main`. |
| PR #257 | `origin/claude/casa3d-opinion-52d7cf` | `02_PRODUCT/projects/Casa3D/VERIFICATION_2026-10-10.md`, `00_GOVERNANCE/coordination/tasks/todo-claude-casa3d.md` | Verified Faza 0/2/4 zip artefacts and recorded 3 concrete residuals from the 11-step remediation plan. |
| PR #258 | `origin/codex/casa3d-memory` | `02_PRODUCT/projects/workspaces/casa3d/{app,packages/twin-core}`, `00_GOVERNANCE/coordination/tasks/todo-codex-casa3d.md` | Full Next.js 14 application (`app/`) + `@casa3d/twin-core` package (`packages/twin-core/`). Reconciled into PR #304 along with PR #257's verification ledger. |

## 2. Classification Matrix Across Workstreams A–D

| Area | Classification | Evidence (`file:line`) | Audit Findings & Residuals |
|---|---|---|---|
| **A1. Geometry & spatial validity (`@casa3d/twin-core` + `app/core/validate.ts`)** | **PARTIAL -> REAL** | `packages/twin-core/src/engine.ts:44-169`, `app/core/validate.ts:13-60`, `app/lib/twin.ts:47-63` | Polygon validity, wall bounds, door swing arc, window sill clearance, overlapping footprints, and minimum passage were implemented (`REAL`). **Residual fixed (Slice 1A)**: `@casa3d/twin-core` did not model piece facing / front clearance (`FRONT_CLEARANCE_BLOCKED`), relying solely on the app validator gate. |
| **A2. Room wall editing & resize handles** | **PARTIAL -> REAL** | `app/core/room-edit.ts:62-95`, `app/components/PlanView.tsx:90-95,204-210` | `edgeHandleAt` avoided doors when a free wall segment existed (`REAL`). **Residual fixed (Slice 1B)**: when a single door/opening covered 100% of a room edge, the resize handle stayed collinear with the door and `PlanView.tsx:92` yielded pointer priority to the door, preventing 2D canvas edge resizing. |
| **A3. Undo/Redo history & WARNING revert** | **PARTIAL -> REAL** | `app/core/history.ts:1-60`, `app/components/Editor.tsx:90,149,174,196,389` | `History<T>` supported `discardLast()` and transactional `begin()` (`REAL`). **Residual fixed (Slice 2)**: clicking **Revert** on a `WARNING` confirmation banner restored `pending.before` without calling `discard()`, leaving a redundant no-op entry in the Undo stack. |
| **A4. AI Design Brief, DSL & deterministic solver** | **REAL** | `packages/twin-core/src/dsl.ts`, `packages/twin-core/src/solver.ts`, `app/components/TwinDesignPanel.tsx:18-75` | Generates up to 3 deterministic layout variants (`compact`, `aerisit`, `echilibrat`) scored on identical metrics (geometry errors, warnings, room coverage, known/unknown prices, budget delta). |
| **A5. Proposal lifecycle & stale-revision protection** | **REAL** | `packages/twin-core/src/approval.ts`, `app/app/api/projects/[id]/twin/proposals/route.ts` | Enforces `draft -> approved -> applied | rejected | expired` and returns HTTP `409 STALE_PROPOSAL` when `baseFingerprint` diverges from current twin state. |
| **A6. BOQ, budget, catalog & provenance** | **REAL** | `app/core/boq.ts`, `packages/twin-core/src/boq-eval.ts`, `packages/twin-core/src/catalog-feed.ts` | Explicitly separates `knownTotal` vs `unknownCount` (`UNKNOWN` prices are never coerced to `0`), tracks `staleCount` (>30 days), and validates catalog schema/SSRF gates. |
| **B1. Contextual inspector & progressive disclosure** | **REAL** | `app/components/Editor.tsx:399-485` | Contextual right panel switches cleanly between `RoomPanel`, `WallPanel`, `OpeningPanel`, `StairPanel`, `PlacementPanel`, `DoorPanel`, `BudgetPanel`, and `TwinDesignPanel`. |
| **B2. Validation error-to-object focus path** | **PARTIAL -> REAL** | `app/core/validate.ts:62-82`, `app/components/Editor.tsx:163,389,400,463,537`, `app/components/TwinDesignPanel.tsx:59` | Previously, validation errors/warnings in the banner, floor inspector, placement inspector, and `TwinDesignPanel` were static text. **Fixed (Slice 3)**: clicking or pressing `Enter`/`Space` on any validation issue resolves `issueTarget` and focuses/selects the offending placement, door/window, wall, stair, or room. |
| **C1. 3D WebGL lifecycle, disposal & context loss** | **PARTIAL -> REAL** | `app/components/Viewer3D.tsx:29-73`, `app/components/viewer3d-engine.js:116-127,225,253,341-376` | Previously, `dispose()` only disposed renderer/shadow maps and leaked room meshes, cloned floor textures, cove/spot materials, ground/sky, and lacked `webglcontextlost` recovery. **Fixed (Slice 4)**: full scene/mesh/texture/material disposal, `webglcontextlost`/`webglcontextrestored` handling with recoverable `role="alert"` UI, dynamic `prefers-reduced-motion` listener, and keyboard camera navigation (`WASD`/Arrows/`+`/`-`/`0`). |
| **D1. Cross-platform test suite portability** | **PARTIAL -> REAL** | `app/tests/{project-export,commercial,contract,commercial-review}.test.ts` | 6 tests hardcoded `execFileSync('python3', ...)`, failing with `ENOENT` on Windows hosts where Python is installed as `python`. **Fixed (Slice 5)**. |
| **E1. Blank canvas, room split/merge & calibrated underlay trace (PR #305 §14.1)** | **PARTIAL -> REAL** | `app/core/project.ts:13-22`, `app/core/room-edit.ts:95-155`, `app/core/underlay.ts:52-88`, `app/components/Editor.tsx:422-423,522-526` | Added `emptyFloor` (`template: 'empty'`), `splitRoom` and `mergeRooms` (wired in `RoomPanel`), and `traceRoomFromUnderlay` requiring confirmed calibration (`underlay.confirmed === true`). |
| **E2. Scoped style system, override preview & custom presets (PR #305 §14.2)** | **PARTIAL -> REAL** | `app/core/styles.ts:43-128`, `app/components/StylePanel.tsx:13-45` | Added `StyleScope` (`project`, `level`, `room`), `previewStyle` showing overridden rooms before apply, `preserveOverrides`, and `createCustomStylePackage`. |
| **E3. Parametric user library & guarded GLB 3D import (PR #305 §14.3, §14.7)** | **MISSING -> REAL** | `app/core/user-library.ts:1-130`, `app/core/types.ts:141-175` | Added `createParametricItem`, `saveToUserLibrary`, `instantiateFromLibrary` (with isolated per-instance overrides), and `validateGlbAsset` (magic/version/JSON-chunk validation, SSRF protection, bounding-box extraction, and `measurable: false` when scale/source/license are missing). |
| **E4. Technical concept plans: switch groups, HVAC & schedule (PR #305 §14.4)** | **PARTIAL -> REAL** | `app/core/technical.ts:7-34,125-171`, `app/components/TechPanel.tsx:9-13`, `app/core/dxf.ts:46-50` | Added HVAC kinds (`hvac_split`, `thermostat`, `vent_exhaust`, `radiator`), `assignSwitchGroup` linking switches to light points, and `buildTechnicalSchedule` marked `PRELIMINARY_CONCEPT`. |
| **E5. Surface tile setting-out & Project Coordination Review (PR #305 §14.6, §14.8)** | **PARTIAL -> REAL** | `app/core/setting-out.ts:39-78`, `app/core/coordination-review.ts:1-96` | Added `customSurfaceSettingOut` with explicit origin (`originCm`) and pattern waste (`straight`, `brick`, `herringbone`, `diagonal`) and `reviewProjectCoordination` (`ERROR | WARNING | INFO | UNKNOWN`). |
