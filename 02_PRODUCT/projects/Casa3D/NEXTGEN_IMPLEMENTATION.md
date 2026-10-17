---
id: "b94c8e21-3d10-4f88-9a51-67e2104c9f8b"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, nextgen-implementation, ui-ux, digital-twin, webgl]
created: "2026-10-17"
updated: "2026-10-17"
provenance:
  source_type: execution
  source_ref: "PR #304 implementation and verification across @casa3d/twin-core and app/"
  source_date: "2026-10-17"
  redaction: not_applicable
  provenance_status: complete
confidence: high
verification: unverified
relations:
  - type: related_to
    target_id: "7f4183b0-9c2a-4e11-8b40-15678a90c1d2"
  - type: related_to
    target_id: "28890ae9-095d-4d32-a374-54cd7996d9aa"
  - type: related_to
    target_id: "4e17d26a-74a6-408a-b0f4-069dfd20ac95"
  - type: related_to
    target_id: "d4785be1-7a70-4e45-b7ba-214e460501d4"
---

# Casa3D — Next-Gen Implementation & Verification Report (PR #304)

## 1. Implemented Engineering & UI/UX Slices

### Slice 1A — Piece Facing & Front Clearance in `@casa3d/twin-core` and `app/lib/twin.ts`
- **Problem**: `@casa3d/twin-core` modeled rotated piece bounding boxes (`footprint(p)`), door swing zones, and window sill clearance, but did not model directional front clearance (`frontClearance`) in the core validator, leaving a parity gap with the app validator (`app/core/validate.ts:27`).
- **Solution**:
  - `02_PRODUCT/projects/workspaces/casa3d/packages/twin-core/src/twin.ts:52-66,133-157`: Added optional `frontClearance?: number` (in metres) to `Placement`, normalized it in `normalize(twin)`, and exported `frontFootprint(p): Rect | null` computing the directional clearance rectangle in front of a piece for `rotation = 0 | 90 | 180 | 270`.
  - `02_PRODUCT/projects/workspaces/casa3d/packages/twin-core/src/engine.ts:132-145`: Added `FRONT_CLEARANCE_BLOCKED` warnings in `validateTwin(twin)` when a piece's front clearance zone extends outside its room polygon or intersects another non-overlapping placement in the same room.
  - `02_PRODUCT/projects/workspaces/casa3d/app/lib/twin.ts:8,60`: Wired `FRONT_CLEARANCE[p.group]` into `snapshotToTwin(snap, cat)` so every Digital Twin validation and AI solver evaluation respects directional front clearance.

### Slice 1B — Room Edge Resize Handle When Wall is 100% Covered by a Door/Opening
- **Problem**: When a room wall segment was completely covered by a door/opening (`edgeHasFreeSpan === false`), the room resize handle (`redge`) sat collinear with the door and `PlanView.tsx:92` yielded pointer hit priority to the door, making 2D canvas edge dragging impossible on that wall.
- **Solution**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/room-edit.ts:62-95`: Added `edgeHasFreeSpan(floor, rect, edge)` and `edgeHandlePoint(floor, rect, edge, margin, outwardOffset)` which shifts the resize handle slightly outward from the room interior (`offset: true`) whenever the wall has no free span outside openings.
  - `02_PRODUCT/projects/workspaces/casa3d/app/components/PlanView.tsx:21,91-92,182,204-210`: Positioned `redge` via `edgeHandlePoint`, tagged offset handles with `data-offset="1"`, and retained pointer drag priority on offset handles so both the door and the room edge remain independently interactive in 2D.

### Slice 2 — Fix Redundant Undo Step on `WARNING` Revert
- **Problem**: Clicking **Revert** on the `WARNING` confirmation banner after moving, rotating, or changing a piece restored `pending.before` without discarding the newly pushed history snapshot, leaving a redundant no-op entry in `Undo`.
- **Solution**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/components/Editor.tsx:90,149,174,196,389`: Stored `discard?: () => void` on `pending` (`() => hist.current.discardLast()` for `changePiece`/`rotate` and transactional `discard` for `onEdit`) and invoked `pending.discard?.()` on Revert.

### Slice 3 — Interactive Validation Error-to-Object Focus Path (UI/UX & Accessibility)
- **Problem**: Validation errors and warnings in the editor banner, floor inspector, placement inspector, and `TwinDesignPanel` were static text; users had to visually hunt for the offending piece, door, wall, or room.
- **Solution**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/validate.ts:62-82`: Added `IssueTarget` and `issueTarget(issue, floor, placements)` resolving `issue.with`, `issue.vars.stair`, `issue.vars.op` + `issue.vars.id`, `issue.vars.id`, and `issue.refs` (`@casa3d/twin-core` issue references) to a selectable canvas entity.
  - `02_PRODUCT/projects/workspaces/casa3d/app/components/Editor.tsx:163,389,400,463,478,537` and `02_PRODUCT/projects/workspaces/casa3d/app/components/TwinDesignPanel.tsx:6-7,59`: Rendered actionable validation issues with `role="button"`, `tabIndex={0}`, keyboard `Enter`/`Space` activation, and click focus selection.

### Slice 4 — 3D WebGL Resource Disposal, Context-Loss Recovery, Dynamic Reduced Motion & Keyboard Camera
- **Problem**: `viewer3d-engine.js` leaked room/wall/ground/sky geometries, cloned floor textures, and cove/spot materials on `dispose()`, had no `webglcontextlost`/`webglcontextrestored` recovery, read `prefers-reduced-motion` only once at startup, and lacked keyboard orbit/zoom controls in `house` mode.
- **Solution**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/components/viewer3d-engine.js:116-127,183-199,225,253,341-376`: Tagged cloned textures and per-build materials with `userData.owned = true`, disposed all owned scene hierarchies (`house`, `furniture`, `contact`, `probes`, `ground`, `sky`) and `lookCache` entries in `dispose()`, added `webglcontextlost`/`webglcontextrestored` handlers and context-lost draw guard, attached a live `matchMedia('(prefers-reduced-motion: reduce)')` change listener that snaps door animations immediately, and added keyboard orbit/zoom/reset (`WASD`/Arrows/`+`/`-`/`0`) in `house` mode.
  - `02_PRODUCT/projects/workspaces/casa3d/app/components/Viewer3D.tsx:31,41-42,61-65,69`: Added `tabIndex={0}` to `<canvas>` and a recoverable `role="alert"` overlay when `err === 'context-lost'`.

### Slice 5 — Cross-Platform Python Binary Resolution in Vitest Suites
- **Problem**: 6 tests in `app/tests/{project-export,commercial,contract,commercial-review}.test.ts` hardcoded `execFileSync('python3', ...)`, failing with `ENOENT` on Windows environments where Python is invoked as `python`.
- **Solution**: Used `const PY_BIN = process.platform === 'win32' ? 'python' : 'python3'` across all 4 test files.

### Slice 6 — Pro-Grade Parametric Home & Apartment Editor (PR #305 Master Prompt §14.1–14.9)
- **Blank Canvas, Room Split/Merge & Calibrated Underlay Trace (§14.1)**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/project.ts:13-22`: Added `emptyFloor()` so `newSnapshot(cat, name, 'empty')` starts from a true blank canvas (`0` rooms, `0` walls).
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/room-edit.ts:95-155` & `app/components/Editor.tsx:422-423,522-526`: Added `splitRoom` and `mergeRooms` and wired Split X / Split Z / Merge controls into `RoomPanel`.
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/underlay.ts:52-88`: Added `traceRoomFromUnderlay` converting pixel rectangles on calibrated blueprints into metric rooms + exterior walls, refusing conversion unless calibration scale is explicitly confirmed (`underlay.confirmed === true`).
- **Scoped Whole-Home Style System & Custom Style Packages (§14.2)**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/styles.ts:43-128` & `app/components/StylePanel.tsx:13-45`: Added `StyleScope` (`project`, `level`, `room`), `previewStyle` listing overridden rooms before apply, `applyStyleScoped` with `preserveOverrides`, and `createCustomStylePackage`.
- **Parametric User Library & Guarded GLB 3D Asset Validation (§14.3, §14.7)**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/user-library.ts:1-130`: Added `createParametricItem`, `saveToUserLibrary`, `instantiateFromLibrary` (with isolated per-instance overrides), and `validateGlbAsset` (magic `glTF`, version `2`, JSON chunk parsing, SSRF blocklist on `sourceUrl`, bounding-box dimension extraction, and `measurable: false` when scale/source/license are missing).
- **Technical Concept Plans: Switch Groups, HVAC & Preliminary Schedule (§14.4)**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/technical.ts:7-34,125-171`: Added HVAC kinds (`hvac_split`, `thermostat`, `vent_exhaust`, `radiator`), `assignSwitchGroup`, and `buildTechnicalSchedule` (`status: 'PRELIMINARY_CONCEPT'`).
- **Surface Tile Setting-Out & Project Coordination Review (§14.6, §14.8)**:
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/setting-out.ts:39-78`: Added `customSurfaceSettingOut` with explicit origin (`originCm`) and pattern waste (`straight`, `brick`, `herringbone`, `diagonal`).
  - `02_PRODUCT/projects/workspaces/casa3d/app/core/coordination-review.ts:1-96`: Added `reviewProjectCoordination` classifying geometry, clearance, MEP/plumbing, custom items, and budget/BOQ status as `ERROR | WARNING | INFO | UNKNOWN` without coercing unknown costs to `0`.

## 2. Empirical Verification Matrix

| Suite / Check | Command | Result |
|---|---|---|
| `@casa3d/twin-core` unit & e2e tests | `npm --prefix 02_PRODUCT/projects/workspaces/casa3d/packages/twin-core test` | **PASS** — 10 test files, **59/59** tests passed (exit code `0`) |
| `casa-mea-3d` TypeScript typecheck | `npm --prefix 02_PRODUCT/projects/workspaces/casa3d/app run typecheck` | **PASS** — `tsc --noEmit` clean (exit code `0`) |
| `casa-mea-3d` full test suite | `npm --prefix 02_PRODUCT/projects/workspaces/casa3d/app test` | **PASS** — 98 test files, **591/591** tests passed (exit code `0`) |
| `casa-mea-3d` production build | `npm --prefix 02_PRODUCT/projects/workspaces/casa3d/app run build` | **PASS** — `next build` compiled & generated all routes (exit code `0`) |
| Vault schema, state & untrusted guard | `python -m pytest 20_TESTS/test_vault_state_accuracy.py 20_TESTS/test_lifecycle_schema_parity.py 20_TESTS/test_untrusted_content_guard.py -q` | **PASS** — **47/47** tests passed (exit code `0`) |
