# Casa3D — Master Prompt: Pro-Grade Parametric Home & Apartment Editor

**Target agent:** Gemini 4 Argon  
**Repository:** https://github.com/userist123/AI_Memory_Vault  
**Canonical workspace:** `02_PRODUCT/projects/workspaces/casa3d/` (verify the current path before acting)  
**Language:** communicate with the owner in Romanian; source code, identifiers and technical documentation may remain in the repository's established language.  
**Objective:** turn Casa3D into a genuinely editable, extensible home/apartment design application—not a gallery of fixed demos or a handful of hard-coded concept templates.

---

## 0. Mission and non-negotiable outcome

You are the implementation agent, not merely a reviewer or idea generator. Inspect the current repository and all relevant Casa3D branches/PRs, research current professional home-design workflows, then implement the highest-value missing capabilities in the actual application.

The result must let a user create and modify a unique project from a blank canvas or a starting template. A template is only a starting point: every room, wall, opening, stair, roof, furniture item, material, dimension and building component must be editable, replaceable, duplicable and removable where appropriate.

**Do not deliver a static mockup, fake controls, demo-only interactions, screenshots presented as implementation, or a plan that stops before code.** Every visible control must work, be disabled with a clear reason, or be explicitly marked as not yet available. Do not claim a feature works unless you can point to its implementation and verification evidence.

## 1. First: establish the actual baseline

Before coding:

1. Read the canonical Casa3D product note, governance/coordination task ledger, design decisions, verification ledger, current app README, architecture docs and test instructions.
2. Inspect the live GitHub state and diffs for existing Casa3D PRs, especially:
   - https://github.com/userist123/AI_Memory_Vault/pull/257
   - https://github.com/userist123/AI_Memory_Vault/pull/258
   Also find other Casa3D branches, commits, files and open PRs. Their state may have changed; re-check it live.
3. Inspect the actual source code, data model, geometry engine, Digital Twin, AI design DSL/solver, 2D plan, 3D viewer, persistence/revisions, catalog, BOQ/budget, share/client approval, tests and CI. Do not infer implementation from a PR description alone.
4. Run baseline typecheck, tests, production build and existing verification scripts before changing code. Capture command, exit code and relevant output. Do not hide failures behind pipes or grep.
5. Create an evidence-backed inventory with statuses:
   - **REAL** — source and execution/test evidence demonstrate it works.
   - **PARTIAL** — some paths work, but limitations are demonstrated.
   - **UNKNOWN** — not verified.
   - **MISSING** — confirmed absent after inspecting source.
6. Reconcile existing PR work before adding anything. Reuse sound code; do not duplicate systems or silently overwrite work from another branch. If a PR is stacked on another PR, preserve its dependency and document it.
7. Write a short gap matrix: user capability, current implementation, evidence, gap, proposed change, tests and dependencies. Then implement—do not stop after the audit.

Never claim a baseline test passes unless you ran it and observed its exit status. If the environment cannot run a check, mark it UNKNOWN and explain why.

## 2. Product principles

- **Editable geometry is the source of truth.** A template, AI proposal, imported model or generated preview must become editable project data, not a flattened image.
- **One semantic Digital Twin.** The same project model must drive 2D plan, 3D view, dimensions, room areas, quantities, budget, schedules and exports. No independent duplicated geometry that drifts out of sync.
- **Deterministic geometry.** AI can propose semantic edits; the existing Geometry Engine/solver validates and calculates geometry. AI must not bypass constraints or directly invent final coordinates that evade validation.
- **Custom by default.** Users must be able to start blank, draw freely with exact dimensions, edit a template, or create a project from an imported plan.
- **Progressive complexity.** Easy tools for casual users; precise dimensions, properties and advanced operations for experienced designers.
- **Real data and transparent limitations.** No fake catalogue entries presented as available products, invented prices, fake AI calls, or misleading compliance claims.
- **Preserve existing functionality and data.** Migrations must be backwards compatible where possible; maintain current APIs, URLs, share/revision semantics and project data.
- **No automatic merge or production data mutation.** Work on a dedicated branch/PR. Never expose secrets, local machine identifiers, real user paths, client data or private project information.

## 3. Core editor: create a house or apartment from scratch

Implement a real tool-driven editor, integrated with the existing stack rather than a parallel prototype.

### 3.1 Drawing and editing tools

Provide a tool palette with working operations, appropriate to the current model:

- Select, multi-select, move, rotate, resize, duplicate, delete and group/ungroup.
- Draw wall, room/closed polygon, dimension, annotation, guide/reference line and construction grid.
- Edit a selected entity numerically in a properties inspector as well as by dragging.
- Add, move, split, join and delete wall segments; edit endpoints and corners; support non-rectangular floor plans.
- Support snapping to grid, wall endpoints, corners, alignment lines and object edges. Make snapping toggleable, show snap feedback and allow a modifier key to temporarily bypass it.
- Exact units and precision: mm/cm/m and, if already supported, imperial units. Display dimensions with configurable precision.
- Keyboard shortcuts, context menu, copy/paste, duplicate, undo/redo, delete confirmation when needed, and a visible unsaved/saving/saved state.
- Pan, zoom, fit-to-plan, zoom-to-selection, orbit, focus selection and reset camera.
- Multi-select transforms and numeric alignment/distribution tools.
- Clearly distinguish selection from drawing mode; Escape cancels the current operation and never silently discards the project.

Every operation must update the canonical model and all dependent views. Add regression tests for each editing command and its undo/redo behavior.

### 3.2 Building elements that can be added and customized

Build a reusable semantic element model and inspector. The exact schema must follow the current architecture; do not introduce a competing model.

**Walls**
- Straight wall segments at minimum; curved walls if the current geometry architecture can support them robustly.
- Length, thickness, height, base/elevation, wall type, structural/non-structural classification where supported, material/texture and independent inside/outside finishes.
- Move endpoints, split/join, change thickness/height, align, offset, copy, delete.
- Correct corners/intersections and openings; validate invalid/self-intersecting or disconnected geometry and explain the issue.

**Doors and windows**
- Add to a selected wall, snap/host to it, move along it, resize, rotate where meaningful, set sill height, opening direction/handing, frame, glazing and material.
- Model the actual opening in the wall, not just an icon floating on top.
- Support common door/window types: single/double doors, sliding doors, pocket doors where feasible, fixed/casement/tilt-turn/sliding windows, and configurable dimensions.
- Validate wall-host relationship, opening dimensions and collisions. Changing the host wall must preserve or safely revalidate the opening.
- Window/door catalogue objects must have editable parameters and sensible defaults.

**Rooms and floors**
- Arbitrary room outlines, room names, net area/perimeter, floor/ceiling finish, ceiling height, skirting/baseboard and room metadata.
- Automatic room detection only when geometry proves a closed boundary; do not silently create false rooms.
- Multiple floors/levels, floor elevations, floor-to-floor height, stair connections, and a level navigator.
- Stair types and dimensions where supported, landings and validation of clearances. If code/geometry cannot safely support a stair type yet, make the limitation explicit.
- Balconies, terraces, patios, garage, storage, utility spaces and other distinct zones.
- Split, merge and reshape rooms while maintaining wall/room consistency.

**Roof and exterior**
- Roof types that the geometry engine can reliably generate: flat, shed, gable and hip as feasible; configurable pitch, eaves, ridge height, overhang and material.
- Exterior facade layers/materials, cladding, paint, stone, brick, roof coverings, gutters and downpipes where supported.
- Terrain/site boundary, driveway, paths, fences, gates, garden zones, trees and outdoor furniture as a separate site layer.
- If a complex roof or terrain operation cannot be robustly implemented in this iteration, do not fake it: implement a validated subset and document the next extension point.

**Structural and MEP concepts**
- Place editable columns, beams and simple structural elements only if the model supports them.
- Conceptual electrical outlets, switches, lights, plumbing fixtures, radiators, ventilation and appliance points with layers/visibility controls.
- These are design coordination aids, not certified structural/electrical/plumbing calculations. Label warnings and never claim code compliance without a validated ruleset and evidence.

### 3.3 Furniture and custom components — critical requirement

Users must not be limited to a few hard-coded furniture presets.

- Add a searchable, categorized catalogue: living room, bedroom, kitchen, bathroom, hallway, office, storage, lighting, appliances, sanitary fixtures, doors/windows, exterior/garden and user-defined objects.
- Add catalogue item to the active floor by click or drag-and-drop; show a placement ghost and snap feedback before committing.
- Each placed item supports position, rotation, width/depth/height, elevation, visibility, name, material/colour, quantity and custom properties where relevant.
- Allow duplicate, mirror, replace, delete, group, align, distribute and save as reusable custom component.
- Provide parameterized modules: kitchen base/wall/tall cabinets, worktops, wardrobe modules, shelves, bathroom vanity, modular sofas and storage. Dimensions and module composition must be editable.
- Allow creating a custom object from a basic parametric shape (box, cylinder, extruded polygon) and saving it to the user's catalogue. Advanced free-form mesh modelling is not required if the architecture cannot support it.
- Support importing 3D models only after validating supported formats, size, file content, dimensions and licensing/provenance. Prefer GLB/glTF if compatible with the current renderer. Never assume arbitrary uploads are safe.
- If model import is not feasible now, implement a documented adapter/interface and ship real parametric object creation, not a disabled upload button presented as working.
- Catalogue items must carry real dimensions, category, model/material reference and provenance. Prices and supplier links are optional, validated fields—not hard-coded claims.
- Detect and display collisions/clearance conflicts with severity and explanation. Allow the user to override non-critical warnings explicitly; do not silently reposition their design.

### 3.4 Materials and finishes

- Visual material picker with swatches/thumbnails, search, categories and recently used/favourites.
- Materials for walls (inside/outside), floors, ceilings, roofs, joinery, worktops, furniture, fabrics, metals, glazing and exterior surfaces.
- User-defined colours and material parameters where supported; texture scale/orientation and rotation.
- Apply to one face, one entity, selected entities, one room, one floor or all matching entities.
- Copy/paste material; undo material changes.
- Store material IDs/references in the canonical model and ensure 2D legend, 3D appearance, quantities and exports remain consistent.
- Show supplier/brand/price only when the source is real and available; show source and last-updated date for external offers.

## 4. Professional 2D/3D workflow

### 4.1 2D plan
- Architectural symbols for walls, doors and swing arcs, windows, stairs, sanitary fixtures, furniture, lighting and appliances.
- Dimension lines, area labels, room names, levels, grid, north indicator and annotation tools.
- Layer controls, visibility toggles, lock/unlock layers, isolate selected layer and select-through options.
- Select object in 2D → same object selected in 3D and property inspector, and vice versa.
- Orthographic, readable plan with line weights and a print/export mode.
- Import a background image/PDF or supported drawing as a trace/reference layer if technically feasible; calibrate its scale using a known dimension. Never treat the background as editable geometry automatically without a confirmed conversion process.
- Print/export to scale with sheet size, scale, title block, project metadata, legend and revision. Clearly label schematic vs construction-level drawings.

### 4.2 3D view
- Live synchronized 3D model; orbit, pan, zoom, walk-through/first-person, top/orthographic, exterior view and focus selected.
- Switch between full model, active floor, cutaway/section view and hide/ghost roof or ceiling to inspect interiors.
- Selection and editing from 3D where supported; property inspector stays synchronized with 2D.
- Quality/performance options, loading states, error fallback and reduced-motion support. Do not rebuild the entire scene on every pointer move.
- Lighting and shadows/material previews must degrade gracefully on weaker devices and mobile.
- Provide an obvious 2D/3D split or toggle on desktop and a usable view switch on phones; avoid forcing a cramped split screen on mobile.
- 3D viewer must show actual project geometry, not a collection of decorative canned scenes.

## 5. Project types and reusable starting points

Offer templates as editable starting points—not finished locked demos:
- Studio, one-bedroom, two-bedroom and multi-bedroom apartment.
- Narrow/long apartment, open-plan apartment, irregular floor plan, attic/loft.
- Single-storey house, two-storey house, compact urban house, family house, house with garage, terrace and garden.
- Empty canvas and import/trace-existing-plan workflow.

For every template:
1. Instantiate normal editable entities in the same model used by blank projects.
2. Allow full editing and removal of all entities.
3. Allow saving the current project as a custom template.
4. Provide meaningful dimension/room metadata and a preview generated from the real model.
5. Add tests proving that template entities can be edited, deleted, persisted and reloaded.
6. Never imply a template represents a certified or build-ready plan.

## 6. Parametric geometry and design validation

Implement the smallest robust set of reusable geometry operations that makes the editor genuinely editable.

- Keep geometry operations deterministic and independently testable.
- Define explicit model invariants: units, coordinate system, stable entity IDs, relationships, host references, levels, constraints and revision semantics.
- Use constraints for wall connections, hosted openings, aligned edges, object placement and level heights where feasible.
- Distinguish hard errors (invalid model) from soft warnings (clearance/design recommendation). Explain the exact affected entities and how to resolve the issue.
- Avoid silent clamping or unexplained automatic corrections. If a solver adjusts geometry, show a preview/diff and let the user accept or reject.
- Maintain a geometry diagnostics view for invalid intersections, dangling walls, openings outside hosts, duplicate IDs, unreachable rooms, impossible dimensions and collision/clearance warnings.
- Derive room areas, perimeters, surface quantities, materials, BOQ and budget from the validated canonical model. Add units and formula/source traceability.
- Ensure edits trigger recalculation efficiently; use dependency tracking or bounded recalculation rather than rebuilding every subsystem on each keystroke.

## 7. AI design assistant — useful, optional, and controlled

AI is an assistant to a deterministic editor, not a substitute for it.

- Accept natural-language requests such as: “Make the kitchen 30 cm wider”, “Add a 120 × 80 cm window to this wall”, “Create a wardrobe along this wall”, “Try three layouts for this room”, or “Change all bedroom floors to this material.”
- Convert requests into a typed, versioned design-operation DSL/command list with entity references, explicit parameters and preconditions.
- Validate proposed operations against the current revision, geometry constraints, room/door/window relationships, collisions, budget and project policy.
- Present a preview/diff with affected entities, dimension changes, cost/quantity impact, warnings and rollback path.
- Require explicit user confirmation before applying a multi-entity or destructive AI proposal.
- Support accept, reject, edit parameters, retry and compare alternatives.
- If the model/provider is unavailable, core manual editing must still work. Display a truthful error and do not fake an AI result.
- Use the existing Digital Twin, DSL, solver and revision model if they exist. Audit their actual wiring; do not create a second AI pipeline.
- Record model/provider, request, generated operations, validation result, approval and applied revision as appropriate, without logging secrets or sensitive prompt contents unnecessarily.

## 8. Design alternatives and variant comparison

- Create named variants/branches of a project without overwriting the original.
- Compare variants by floor plan, 3D view, room areas, dimensions, materials, BOQ, budget and validation warnings.
- Show a structured diff: added/removed/changed entities and changed properties.
- Allow restore/duplicate/rename/archive variant; protect approved/shared revisions from silent mutation.
- A variant generated by AI must be a normal editable project revision and must pass the same validator as manual edits.

## 9. Catalog, quantities, budget and outputs

Integrate with existing catalogue, BOQ/budget and procurement systems; reuse them rather than introducing duplicate sources of truth.

- Recalculate quantities from actual geometry: floor/wall/ceiling areas, skirting, paint, tiles, worktops, cabinets and component counts where formulas are defined.
- Expose the calculation assumptions, unit, waste factor and exclusions. Keep estimates clearly separate from supplier quotations.
- Support editable waste factors and material-specific consumption rates; missing rates must be shown as UNKNOWN, not guessed.
- Compare design variants on quantities and budget; show the delta after an edit.
- Keep internal purchase costs/private notes out of public client links unless the existing authorization policy explicitly allows them.
- Export project JSON or the repository's canonical format, 2D plan PDF/SVG if supported, object/material schedules, BOQ and a design presentation. Include version/revision and units.
- Round-trip tests: export → import must preserve IDs/relationships/dimensions/materials within documented precision.
- Never claim IFC/DWG/OBJ/GLB support until a real export/import path and tests prove it.

## 10. UI/UX direction: professional design studio

Use the existing approved direction where still applicable: editorial studio, warm paper/neutral surfaces, ink text, restrained walnut accent, subtle hairline dividers, polished typography, architectural whitespace and clear hierarchy. Avoid generic AI-looking dashboards, excessive monospace, gradients everywhere, random glassmorphism and long explanatory paragraphs inside forms.

Suggested workflow stages (adapt to existing implementation):
**Releveu → Plan → Concept/Variante → Finisaje → Mobilier & lumini → Instalații → Cantități & buget → Planșe → Prezentare**

Requirements:
- Main canvas gets priority; tool palette and properties inspector remain predictable.
- Contextual inspector changes with the selected object type and shows editable numeric fields, validation feedback and material controls.
- Searchable visual catalogue; swatches/thumbnails instead of long unreadable selects.
- Concise Romanian trade terminology; advanced help in tooltips or a help panel.
- Clear selection state, hover state, focus state, snapping state, active tool, pending edits and errors.
- Empty states guide the user to create a wall/room or add an object—not show a fake project as if it were theirs.
- Responsive layout: desktop keyboard workflows, tablet touch workflows and mobile single-canvas editing with sufficiently large touch targets.
- Keyboard navigation, visible focus, semantic labels, contrast, reduced-motion preference and screen-reader support for non-canvas controls.
- No horizontal overflow on mobile. Test at narrow and wide viewports.
- Consistent dialogs for destructive operations, dirty-state navigation and conflict/revision errors.
- Provide a useful first-run path, but do not lock advanced editing behind a tutorial.
- Preserve localization parity and existing tests/selectors where feasible; update them deliberately when UI labels change.

## 11. Persistence, revisions, security and compatibility

- Every editing command must persist through the existing repository/service layer and reload correctly.
- Use stable IDs and schema migrations with compatibility tests. Keep new fields optional until migrated safely.
- Autosave must have explicit pending/saved/error states, retries bounded by policy and conflict handling. Do not report “saved” before server/storage acknowledgement.
- Protect against stale revision writes; use optimistic concurrency and explicit conflict resolution. An approval must reference the exact revision the approver saw.
- Validate API inputs server-side; do not trust client-supplied dimensions, prices, ownership or approval status.
- Secure file import: content/type/size limits, safe parsing, no path traversal, no unsafe remote fetches, no private-network SSRF and no arbitrary execution.
- Keep public share endpoints limited to approved fields; test that private costs, internal notes, tokens and owner-only metadata do not leak.
- Preserve owner authority gates and approval semantics. No auto-merge, bypassing review or weakening security/test gates.
- Never place secrets, API keys, local usernames, machine names, private paths or client data in code, fixtures, commits, PR text or logs.

## 12. Research requirements

Use current official documentation and practical user workflows to verify feature expectations. Research before selecting a library or changing the architecture. Prioritize official documentation and compare actual behavior, not marketing claims.

Starting references (verify current contents and versions; these are references, not permission to copy code):
- Sweet Home 3D user guide: https://www.sweethome3d.com/users-guide/
- Sweet Home 3D documentation: https://www.sweethome3d.com/documentation/
- Sweet Home 3D features: https://www.sweethome3d.com/features/
- Autodesk Forma Building Design: https://www.autodesk.com/formabuildingdesign
- Autodesk Forma workflow overview: https://blogs.autodesk.com/forma/2026/04/07/introduction-to-forma-building-design/
- Revit wall behavior/reference: https://help.autodesk.com/cloudhelp/2027/ENU/Revit-ArchDesign/files/GUID-550F4B3E-7D49-4A4F-BEC7-4077D5B9FC85.htm

Research should answer:
1. Which editing operations are baseline expectations for a real floor-plan/home-design editor?
2. Which interactions can be implemented robustly in the existing stack?
3. What are the trade-offs between extending the current geometry engine and introducing a dependency?
4. How are dimensions, snapping, hosted openings, levels, room detection, materials and 2D/3D synchronization represented?
5. What is feasible now, what requires a later phase, and how will each claim be verified?

Record source URL, access date, the specific behavior observed, the design implication and confidence. Do not paste large copyrighted passages or copy proprietary assets.

## 13. Implementation plan and gates

Work in small, reviewable phases. Reorder based on the actual gap analysis, but do not skip the gates.

### Gate G0 — Baseline and architecture
- Inventory source, existing PRs, model, geometry engine, UI, APIs, persistence and tests.
- Run baseline checks and record evidence.
- Produce gap matrix and proposed bounded scope.

### Gate G1 — Editing foundation
- Command/operation layer, stable selection, inspector, undo/redo, snapping, numeric transforms and persistence.
- Tests for operation correctness, undo/redo, reload and invalid input.

### Gate G2 — Building geometry
- Walls/rooms/openings/levels and their relationships; geometry validation and diagnostics.
- Tests for non-rectangular layouts, wall intersections, opening hosting, level isolation and invalid geometry.

### Gate G3 — Objects/catalogue
- Place/edit/delete/duplicate furniture and parametric components; custom component creation; materials.
- Tests for transforms, collision warnings, persistence and catalogue provenance.

### Gate G4 — 2D/3D synchronization
- Selection synchronization, camera/view controls, cutaway/floor visibility, responsive behavior and performance.
- Browser tests and screenshots for real projects, not only a canned scene.

### Gate G5 — Variants, quantities and outputs
- Revision/variant diff, BOQ/budget recalculation, exports and round-trip tests.

### Gate G6 — AI operation workflow
- Typed DSL, precondition validation, preview/diff, explicit approval, apply, rollback and audit.
- Test provider failure, malformed DSL, stale revisions, rejected proposals and destructive operations.

### Gate G7 — UX, accessibility and independent review
- Verify all primary flows at desktop/tablet/mobile widths, keyboard navigation, accessibility basics and design consistency.
- Request an independent read-only review if available; verify every finding before changing code.

### Gate G8 — Final verification
Run the repository's actual commands, including at minimum:
- dependency install using the lockfile and repository policy;
- typecheck;
- full unit/integration test suite;
- production build;
- Casa3D-specific verification scripts;
- browser/e2e smoke tests for create → edit → save → reload → 2D/3D → export;
- Windows case-insensitive path/import check if relevant to the stack;
- privacy/security regression checks for public share and imported assets.

Capture exact command, exit code, test totals, build result, known failures and evidence paths. Use the command's exit status, not a grep/pipe exit status, as the gate. Do not weaken tests to obtain green CI.

## 14. Acceptance criteria (must be demonstrated)

A reviewer should be able to perform this scenario in the actual application:

1. Create a new blank project and choose units.
2. Draw a non-rectangular floor plan with at least several connected walls and two rooms.
3. Change exact wall dimensions/thickness and see consistent 2D/3D updates.
4. Add a door and window to specific walls; change size and position; confirm the wall opening follows.
5. Add another level and navigate between levels.
6. Add multiple furniture/catalogue items, move/rotate/resize/duplicate/delete them, and edit their materials.
7. Create and save a custom parametric component and place multiple instances.
8. See collision/clearance warnings without silent object movement.
9. Undo and redo edits; reload the project and verify all relationships and dimensions persist.
10. Create a variant, compare it with the original and inspect the changed entity list.
11. Verify room areas, material quantities and budget deltas recalculate from the actual geometry or show UNKNOWN where rates are missing.
12. Switch between 2D and 3D and select the same object in both.
13. Export a plan/report and confirm revision, units and dimensions are present.
14. On mobile, use the core create/select/inspect/place/edit/save flow without horizontal overflow.
15. Disable or break the AI provider and prove manual editing still works; malformed AI output must be rejected safely.

If an item is not implemented, mark it **PARTIAL/UNKNOWN/MISSING** and state the exact blocker. Do not mark the acceptance suite passed based on mocks alone.

## 15. Engineering workflow and final report

- Follow test-driven development for geometry, persistence, permissions and other deterministic behavior: add a focused failing regression test, implement the smallest fix, verify the test turns green, then run the relevant broader suite.
- Keep changes scoped; avoid unrelated Memory Vault, Book-to-Memory, LogAnalyzer or security work.
- Use existing abstractions and dependency versions unless evidence justifies change. Do not add a large 3D/BIM library without a measured need and compatibility review.
- Use separate commits with meaningful messages. Never commit with failing tests or a command whose exit status was masked.
- Do not overwrite another agent's branch or commit. If upstream changes invalidate the baseline, rebase/merge only with a clear diff and re-run tests.
- Update project docs and the task ledger with REAL/PARTIAL/UNKNOWN status and evidence.
- Open/update a dedicated PR; do not merge it automatically.

Final response to the owner in Romanian:
1. Summary of actual functionality implemented (not intended functionality).
2. Files/modules changed and architecture decisions.
3. Research sources and how they influenced design.
4. Exact tests/build/browser checks with results.
5. Screenshots or recordings of real working flows if available.
6. Known gaps, limitations and what remains unverified.
7. PR URL and commit SHA.
8. Any owner decisions genuinely required; do not ask questions that can be resolved by inspecting the code or conducting research.

**Definition of done:** the editor supports meaningful free-form editing backed by a single validated project model, visible controls perform real actions, edits persist and synchronize across views, tests demonstrate the core acceptance flow, and all unverified capabilities are explicitly identified. A polished demo without real editing does not meet the definition of done.

---

# 14. Additional owner requirement — complete, adaptable whole-home concept and systems design

**This section extends the existing brief; it does not replace or narrow earlier requirements.** The owner explicitly requires that Casa3D serve a professional interior designer who receives unrelated projects, not a consumer who selects one fixed house template. Extend the *same implementation effort and PR #305*. Do not create a disconnected demo, a parallel product, or a second source of truth. This work belongs to PR #305.

## 14.1 Start from the actual project, not a preset

Every new project must support an independent geometry and design brief. A project can start from:
- an empty canvas and manually drawn survey;
- a client/architect PDF or raster plan used as a trace underlay;
- a supported CAD file, imported with a clear report of supported and unsupported entities;
- a measured existing apartment/house;
- a partial brief such as “redesign the kitchen and living room” without inventing the rest of the home;
- an existing Casa3D project copied as a new version.

The workflow must ask for or let the designer define the actual footprint, wall positions/thicknesses, openings, dimensions, levels, ceiling heights and room names. Imported plans are references until the designer verifies scale and geometry. Provide a review/confirmation step before converting detected lines into authoritative walls. Never silently interpret a raster/PDF as precise CAD geometry or invent missing dimensions.

A template or style preset can accelerate setup, but must remain optional, editable, and subordinate to the current project's model. Do not hard-code a standard number of rooms, a rectangular footprint, one fixed kitchen, one bathroom, or one specific style.

## 14.2 Whole-home concept and style system

Implement a **project-wide design concept** that applies to the whole house/apartment but supports controlled exceptions at level, room, surface and object scope.

The concept should include editable attributes, not only a label such as “Modern”:
- design direction (e.g. contemporary, minimalist, Japandi, Scandinavian, industrial, classic, transitional, Mediterranean, eclectic or user-defined);
- palette: primary/secondary/accent colors and neutrals;
- material families and specific selected materials for floors, walls, ceilings, doors, trim, cabinetry, worktops and metal finishes;
- furniture language, shapes, proportions and finish;
- lighting temperature/intensity intent where supported;
- hardware, handles, skirting, architraves, door/window frame finish;
- textiles and decor direction;
- room-by-room overrides and object-level exceptions;
- reference images/moodboard, designer notes, client approval status and concept revision.

The user must be able to define a completely custom style, save it as a reusable preset, apply it selectively, and later update either (a) only selected objects, (b) a room, (c) a level, or (d) the whole project. Before a global change, show a preview and the number/types of affected objects; support undo and clear conflict resolution for local overrides. Never overwrite deliberate per-room decisions without showing the effect.

A concept is not complete if it only changes a theme color in the UI. It must update real persisted material/style assignments on actual project objects, and the result must survive save/reload and export.

## 14.3 Whole-home architectural and interior components

Provide a data-driven, extensible component library with create/edit/import/save/reuse behavior. Include, where supported by the actual geometry engine:

- **Building shell:** straight and angled walls, variable wall thickness/height, internal partitions, columns, beams, niches, wall openings, doors, windows, sliding doors, glazing, stairs, landings, railings, multiple floors, balconies, terraces, roof forms, sloped ceilings and dormers.
- **Room construction and finishes:** floor build-up/thickness where relevant, ceiling planes, false ceilings, bulkheads/scafas, cornices, skirting, wall panels, decorative slats, cladding, tile/stone/wood patterns and transitions between finishes.
- **Kitchen:** independently configurable base/wall/tall cabinets, fillers, end panels, plinths, fronts, hinges/handles, drawers, internal shelves, worktops, islands, backsplashes, sinks, taps and appliance cut-outs. Modules must have real dimensions and clearances, not just stretchable pictures.
- **Bathrooms:** vanity, basin, WC, bidet, bath, shower tray/walk-in shower, screens, taps, mirrors, niches, towel rails, drains and accessories; model placement and service points when supported.
- **Living, dining and bedrooms:** sofas, sectional pieces, tables, chairs, beds, wardrobes, nightstands, TV/media units, desks, shelving, curtains/blinds, rugs and custom-built storage.
- **Custom joinery:** a configurable cabinet/wardrobe/shelving builder that composes panels, carcass, fronts, shelves, drawers and hardware; supports custom dimensions, material assignment, a dimensioned elevation and a component/cut list where geometry/data permits.
- **Exterior/site if in scope:** entrance, facade finishes, paving, fence, driveway, patio/deck, garden elements and outdoor lighting.

Every family should declare its editable properties, units, constraints, default values, supported views, measurable/non-measurable status and schema version. Users must be able to add new families or user-owned assets rather than wait for a code release to support every object. A generic imported mesh must not falsely claim parametric editability.

## 14.4 Technical concept plans — especially electrical, lighting, plumbing and HVAC

The project should support **separate, toggleable, versioned plan layers** over the same verified architectural geometry. At minimum evaluate and implement the highest-value complete slices below, in this order:

### Electrical and lighting layout
- place and edit sockets/receptacles, switches, dimmers, light points, ceiling/wall lights, LED strips, data/network outlets and electrical panel symbols;
- store mounting height, wall/room association, circuit/zone, device type and designer notes;
- assign lights to switches/lighting groups and show relationships clearly;
- create schematic circuit routes where the model supports them, with panel/circuit identifiers and schedules;
- distinguish the 2D symbol from the 3D fixture and real light source;
- include lighting scenes/groups for presentation (e.g. general, task, accent) if the renderer supports it;
- produce a dedicated electrical/lighting plan and fixture/device schedule.

### Plumbing and sanitary layout
- place hot/cold water points, sanitary fixtures, drains and plumbing connection markers;
- represent pipe routes, nominal size/material/slope only where those values are entered or computed by a validated rule;
- maintain fixture-to-service-point associations and expose missing or unconnected points;
- produce a plumbing plan and fixture schedule when the required data is present.

### HVAC and ventilation
- allow HVAC equipment, supply/return/exhaust points, duct/pipe routes and control locations as typed elements where supported;
- keep system data separate from architectural geometry and allow the layer to be hidden independently;
- show incomplete connections or unknown sizing rather than inventing capacities or engineering calculations.

### Other coordinated plans
- ceiling/reflected ceiling plan, lighting plan, floor-finish plan, tile setting-out plan, furniture/joinery plan, door/window schedule, demolition/existing-vs-proposed plan, and low-voltage/smart-home layout;
- fire-safety/security elements only as documented design placeholders unless the app implements a validated code-aware workflow;
- use shared coordinates and dimensions so these views agree with the underlying model.

**Safety/competence boundary:** Casa3D can help the designer document intent and identify missing information. It must not present conceptual electrical, plumbing, HVAC, structural or fire-safety layouts as construction-ready engineering or code-compliant merely because symbols connect. Clearly label conceptual/preliminary outputs; keep rule sources, jurisdiction, version, applicability and evidence visible. Unknowns become warnings or “needs specialist verification”, never silent green checks. Any actual sizing/calculation must have an explicit, testable algorithm and suitable source basis, and should require review by a qualified professional where legally or technically necessary.

## 14.5 Real technical drawing sheets and issue sets

From the same model, provide a professional drawing-set workflow:
- choose which views/sheets to issue and in what order;
- floor plan with dimensions, room names/areas and scale;
- furniture layout, ceiling plan, lighting/electrical plan, plumbing plan, floor finishes/tile setting-out, elevations and sections where supported;
- schedules for doors/windows, fixtures, lights, finishes, materials and custom joinery;
- sheet number, project/client, designer, date, scale, units, status (draft/for review/issued), revision and change summary;
- revision cloud or equivalent change annotation where feasible;
- print/PDF output with legible line weights, symbols, labels and page size;
- compare two issued revisions and show what changed.

A screenshot of the 3D scene is not a substitute for a dimensioned technical plan. Do not include unsupported view types as blank sheets or fabricated drawings. If a plan cannot yet be generated, report it as PARTIAL/UNKNOWN and identify the exact missing model capability.

## 14.6 Surface layout and material quantities

For tiles, planks, panels, wallpaper and cladding, support actual layout intent instead of applying a generic texture only:
- choose a start/reference point and direction;
- configure tile/plank/panel dimensions, grout/gap, pattern (grid, staggered, herringbone or other implemented patterns), offsets and rotations;
- preview cuts at boundaries and openings;
- show cut pieces and potential narrow slivers;
- calculate theoretical quantities and configurable waste allowance from the actual surface geometry;
- distinguish net area, gross area, estimated waste and manually confirmed order quantity;
- generate a setting-out drawing and a schedule only when inputs are sufficient.

All quantities must derive from model geometry and declared product dimensions. Never infer a manufacturer-specific installation rule without a source. Show calculation assumptions and allow the designer to override them with a recorded reason.

## 14.7 Project data, procurement and custom products

Each catalog item or custom component should be able to carry:
- stable internal ID, category, manufacturer/brand (optional), model/SKU (optional), source URL, license/asset provenance;
- real dimensions and units, finish/material, image/model references and variant options;
- supplier, price/currency, price timestamp and availability status only when sourced or manually entered;
- project quantity, waste, labour, notes, approval status and procurement state where the existing Casa3D workflow supports it.

Allow the designer to create a product manually from client-provided data, import a local model, or reference a product URL through a controlled allowlisted workflow. Do not scrape arbitrary URLs without SSRF protection, do not trust remote images/files, and do not invent prices, SKUs, product dimensions, availability or certification. Keep internal acquisition cost/margin private from client views unless explicitly authorized by the existing access model.

## 14.8 Validation and coordination checks

Add a non-destructive project review panel that reports:
- unclosed/invalid wall geometry, unresolved dimensions and scale not confirmed;
- objects intersecting walls/openings or each other where the collision logic is reliable;
- cabinetry/appliance/door conflicts and blocked clearances;
- missing material assignments and missing product dimensions;
- unconnected electrical/plumbing/HVAC elements in the corresponding conceptual systems;
- drawing sheets with missing scale, dimensions, legend or revision metadata;
- estimates with missing prices, quantities or waste assumptions;
- imported assets with unknown scale, provenance or license.

Every finding needs severity, affected object IDs, explanation, rule/source and a way to locate the object in the plan. Separate **ERROR** (cannot safely apply/publish), **WARNING** (designer review), **INFO** (advisory), and **UNKNOWN** (not evaluated). Do not label a project compliant with building codes unless the rule set, jurisdiction, source version and actual checks are explicitly implemented and verified.

## 14.9 Expanded acceptance tests — real designer scenarios, not demos

Add realistic tests using different project shapes and briefs, not repeated versions of the same fixture:
1. Irregular apartment plan with angled walls and a niche; edit exact dimensions and confirm area/3D synchronization.
2. Existing house with two levels, stairs, different ceiling heights and a non-rectangular room.
3. Designer imports a scanned plan, calibrates it from a known dimension, traces walls, reviews detected geometry and saves it.
4. Apply a whole-home concept, override the bathroom floor and kitchen fronts, then change the global palette; verify preview, scope, overrides, undo and reload.
5. Build a custom wardrobe or kitchen run with non-default dimensions and parts, save it to the user's library and reuse it in another unrelated project.
6. Import a user GLB, set a known size, assign a material, record source/license status and confirm the BOQ treats it as measurable only after required metadata is provided.
7. Place switches, lights and outlets on the electrical layer, associate a switch group, hide/show the layer and export a plan/schedule; confirm it is labelled preliminary.
8. Place sanitary fixtures and service points; ensure missing connections are reported as unknown/incomplete rather than fabricated.
9. Set a tile pattern origin and dimensions on an irregular room, inspect edge cuts and verify quantity calculations against a deterministic fixture.
10. Change a door, wall, finish or custom cabinet and verify the correct sheets, schedules, quantities and revision diff update from the same model.
11. Export an actual PDF drawing set and open/check the artifact; a test that only checks the function was called is insufficient.
12. Confirm no feature is a dead control, hard-coded showcase, static screenshot or fake success response.

The end-to-end acceptance path should be: **new arbitrary project → verified plan geometry → whole-home concept → custom furniture/components → technical system layers → quantities/schedules → save/reload → revision comparison → real export**. Where the entire chain is too large for one iteration, implement a coherent vertical slice and explicitly mark the rest PARTIAL/UNKNOWN with follow-up acceptance criteria.

## 14.10 Additional research references

Use these as documented workflow references, not as claims that Casa3D already has equivalent capabilities:

- Chief Architect feature catalogue (2D/3D editing, custom solids, material scopes, electrical/HVAC, automatic schedules and material lists): https://www.chiefarchitect.com/products/features/
- Autodesk Revit electrical systems (devices, panels, circuits, switch systems and schedules): https://help.autodesk.com/cloudhelp/2024/ENU/Revit-MEPEng/files/GUID-8BB35A3A-475A-4FE0-99C0-B01054EE0F2A.htm
- Autodesk electrical workflow tutorial: https://www.autodesk.com/learn/ondemand/module/build-electrical-systems-in-revit
- Sweet Home 3D user guide (editable room geometry, doors/windows, furniture dimensions, levels, custom model import and export): https://www.sweethome3d.com/users-guide/
- Planner 5D plan import workflow and accepted formats: https://support.planner5d.com/en/articles/14434484-how-to-upload-a-floor-plan
- SketchUp CAD import/export and supported entity caveats: https://help.sketchup.com/en/sketchup/importing-and-exporting-cad-files
- DDX XDesignPRO (parametric cabinetry, solid modeling, custom furniture and technical output): https://www.ddxgroup.com/en/software/furniture/xdesignpro/
- Chief Architect lighting placement and material/fixture lighting: https://www.chiefarchitect.com/support/article/KB-00777/working-with-lighting.html

Research is a starting point, not authority for local building compliance. Product marketing pages can overstate capability; validate requirements against official documentation and test behavior in Casa3D itself.

---

# 15. Final instruction to Gemini — prioritize real flexibility

Do not interpret “more options” as adding a long menu of presets. The required upgrade is a flexible design system in which the designer can **create, dimension, compose, customize, save, reuse, coordinate and document** the actual objects and plans for each unique project.

Before implementation, add a traceability table mapping each new requirement to (a) existing code, (b) the exact gap, (c) a test, (d) an implementation step and (e) evidence of completion. Then implement in the same Casa3D branch/PR #305, prioritizing real editing workflows over breadth of superficial controls. Preserve working features and existing project data. No auto-merge. No weakening tests. No fake demos. No unsupported claims of engineering/code compliance.

