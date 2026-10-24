# Casa3D — Gemini 4 Argon implementation brief
## Research-backed upgrade: flexible professional interior-design workspace

**Purpose:** Use this document as the execution prompt for Gemini 4 Argon. The goal is to evolve Casa3D from a set of prepared examples and limited configurators into a genuinely flexible, project-driven tool for a professional interior designer who receives unrelated homes, apartments, room layouts, client briefs, and existing plans.

**Repository:** `userist123/AI_Memory_Vault`  
**Existing Casa3D work to inspect first:** PR #257 and PR #258, plus the current Casa3D project records and the actual application source available on those branches.  
**Execution language:** Keep code identifiers and technical documentation in English where consistent with the codebase. The product must retain Romanian and English localization if already supported.  
**Important:** This file is a task specification, not evidence that any feature described below is already implemented.

---

# 1. Mission and non-negotiable product direction

You are the implementation agent, not a concept artist producing a mockup. Inspect the real repository and implement working functionality in the existing Casa3D application. Do not stop after writing a plan, polishing a landing page, adding static screenshots, or creating clickable controls that do nothing.

Casa3D is intended for a professional interior designer who receives different projects from different clients. The next project may be:
- a new apartment with a rectangular plan;
- an irregular or angled existing house;
- a renovation with a supplied blueprint or scanned plan;
- a multi-room apartment with a different number and arrangement of rooms;
- a house with multiple levels, stairs, sloped ceilings, dormers, balconies, or unusual openings;
- one room only, where the designer must create custom joinery or furniture;
- a client project that changes repeatedly during review.

The designer must not have to choose a fixed house template and accept its assumptions. Templates are optional starting points only. A blank, editable project and a way to reconstruct or import an existing plan are first-class workflows.

**Core rule:** the actual editable project model is the source of truth. The 2D plan, 3D scene, object hierarchy, dimensions, material schedule, quantities, costs, and client presentation must derive from that same model. Do not create separate fake data for a demo view.

Preserve the established Casa3D architecture and its safety principles:
- the Digital Twin / geometry model is the source of geometric truth;
- AI may propose semantic changes, but it does not directly author unchecked coordinates or bypass geometry validation;
- every accepted geometry mutation goes through the existing geometry engine and validators;
- errors block application; warnings require an explicit review/confirmation path;
- significant edits support preview, reject/apply, undo/redo, revision history, and recoverable save behavior;
- do not invent product IDs, prices, sizes, availability, warranties, licensing, or certifications;
- imported models and textures must have tracked source and license information;
- preserve existing data, API contracts, persistence, and backward compatibility unless a migration is necessary and tested.

# 2. Mandatory reconnaissance before coding

1. Read the current Casa3D product specification, constitution, phase documents, gap analyses, research notes, and verification ledger. Search the repository rather than assuming every path is on the current default branch.
2. Inspect PR #257 and PR #258, including their changed files, commits, and any existing implementation/research artifacts. Identify what is actually present in the target branch and what exists only in another PR or a historical snapshot.
3. Inspect the real application source, route structure, geometry engine, Digital Twin schema, 2D editor, 3D viewer, catalog, materials, import/export, autosave/draft sync, undo/redo, tests, and current UI. Run the existing checks before changing code.
4. Produce a short baseline matrix with each capability classified as:
   - **REAL** — source code and a test or reproducible runtime behavior confirm it;
   - **PARTIAL** — only some paths/objects/workflows work;
   - **DEMO** — static fixture, hard-coded showcase, fake result, or control without complete behavior;
   - **UNKNOWN** — source or runtime evidence is missing;
   - **BROKEN** — a reproducible failure exists.
5. Map every planned capability to existing modules and tests. Reuse existing functionality; do not duplicate engines or introduce a parallel source of truth.
6. Keep research findings, implementation claims, and verification evidence separate. Never claim a feature is complete just because a component, route, type, or test fixture exists.

Do not wait for owner clarification when a safe, reversible implementation decision can be derived from the codebase. Record unresolved product decisions as explicit blockers rather than silently guessing.

# 3. Research basis and what it means for Casa3D

Use the following official product/documentation sources as design references. They are examples of established workflows, not permission to copy proprietary assets or claim parity without implementation.

- RoomSketcher — exact-dimension drawing, editable walls/rooms, doors/windows, furniture placement and synchronized 2D/3D project: https://www.roomsketcher.com/features/draw-floor-plans/
- RoomSketcher Room Planner — existing-plan conversion/trace workflows, adjustable furniture, live 3D and shareable outputs: https://www.roomsketcher.com/room-planner/
- Sweet Home 3D — searchable/extensible furniture catalog; change size, thickness, location, orientation, colors and textures; levels; blueprint import; 2D/3D; export: https://www.sweethome3d.com/features/ and https://www.sweethome3d.com/users-guide/
- Homestyler — upload plan/model, custom model upload, free-form modeling, parametric cabinetry, material editing, multi-floor design, rendering and export: https://www.homestyler.com/solution/design_floor-planner
- SketchUp interior design — user-created models, reusable assets, materials, plans, moodboards and client-facing presentations: https://www.sketchup.com/en/industries/interior-design
- Planner 5D custom product library — drag/drop room editing and support for importing/customizing 3D products: https://support.planner5d.com/en/articles/15174546-room-editor-and-custom-product-library
- Three.js TransformControls — established translate/rotate/scale gizmo interaction, snapping and axis controls: https://threejs.org/docs/pages/TransformControls.html
- Three.js GLTFLoader — glTF/GLB import and supported asset extensions: https://threejs.org/docs/pages/GLTFLoader.html
- Blender mesh modeling and non-destructive modifiers — direct modeling plus editable, non-destructive geometry operations: https://docs.blender.org/manual/en/latest/modeling/meshes/introduction.html and https://docs.blender.org/manual/en/latest/modeling/modifiers/introduction.html

**Research conclusion:** professional flexibility comes from an editable geometry model, direct manipulation, exact dimensions, reusable and custom objects, real import/export, predictable selection/transforms, material editing, multi-level support, and project outputs. A large catalogue alone does not solve the problem. A beautiful UI alone does not solve it either.

# 4. Priority P0 — make project creation genuinely flexible

Implement or close the gaps below based on the reconnaissance. Do not rebuild a capability that already works; improve its actual missing behavior.

## 4.1 Start a project in several ways

Support distinct workflows:
- blank project / draw from scratch;
- import and trace an existing plan image (PNG/JPG/WebP; PDF only if a safe, reliable parser already exists or can be added appropriately);
- set the image scale using a known measurement, adjust opacity/rotation/position, lock/unlock the underlay, and hide it;
- enter or change real dimensions numerically;
- duplicate an existing project as a new independent version;
- use a template as an optional starting point, never as the only way to start.

If DWG/DXF import is already supported, test it against actual supported content and document the limits. Do not pretend a raster image is vector CAD. If implementing DXF import is feasible in the current stack, support a defined subset with validation and explicit unsupported-element reporting. Do not silently discard entities.

## 4.2 Real 2D plan editor

The editor must let the designer create and edit arbitrary layouts, not just select predefined room cards:
- draw wall segments with exact length and angle;
- connect walls, create corners, close rooms, and handle T/L junctions;
- select, move, split, join, extend, shorten, and delete walls;
- edit wall length, thickness, height, and construction type numerically;
- support non-rectangular rooms and angled walls;
- support interior partitions, half-height walls, glass partitions, niches and openings when the geometry model can represent them;
- place, rotate, resize, and reposition doors/windows with configurable width, height, sill, offset, and opening direction where relevant;
- add dimensions, room labels, area/perimeter readouts, notes, and measurement annotations;
- use grid, endpoint/edge/axis snapping, angle/length constraints, temporary dimensions, and clear snap indicators;
- allow snap/grid toggles and configurable units (metric first; do not mix units silently);
- support multi-selection, box selection, duplicate, copy/paste, delete, align/distribute where sensible, keyboard shortcuts, and context menus;
- provide undo/redo for each meaningful editing operation, with no partial mutation if validation fails;
- maintain selection and focus predictably while changing tools;
- show useful errors when a geometry operation cannot be completed.

Do not implement a fake 2D editor in which dragging changes only a visual overlay while the underlying model remains unchanged.

## 4.3 Multiple levels and building complexity

A project should be able to contain zero, one, or many levels:
- add, rename, duplicate, reorder and delete levels with confirmation and dependency checks;
- set each level's elevation, floor-to-floor height and ceiling height;
- show/hide other levels as ghost/reference layers;
- connect levels with stairs and landings;
- ensure stairs have dimensions and a consistent connection between levels;
- support sloped ceilings/roof planes, dormers, balconies, terraces and porches only where the current geometry engine can model them correctly; otherwise implement a clearly scoped first slice and document the rest;
- never force all homes into a single rectangular shell.

A house with a different topology must be editable, not merely represented as a different label on the same model.

# 5. Priority P0 — real object editing and custom furniture

This is a central requirement. The designer must be able to add objects that are not in the default catalogue and build concepts from components.

## 5.1 Scene hierarchy and direct manipulation

Provide a usable object/scene tree grouped by level and room. For every supported object:
- select by clicking in the 2D plan, 3D viewport, or hierarchy;
- show selection clearly in every synchronized view;
- move, rotate and scale using a 3D gizmo and exact numeric fields;
- constrain transformations to axes/planes; support configurable translation/rotation snapping;
- edit dimensions without accidentally distorting the asset's proportions unless the user explicitly enables non-uniform scaling;
- duplicate, rename, hide/show, lock/unlock and delete objects;
- group/ungroup objects; support reusable assemblies/components;
- show transform origin/pivot where useful;
- expose relevant properties in a contextual inspector rather than forcing the user through unrelated modal dialogs;
- prevent accidental camera manipulation while transforming an object;
- ensure keyboard/mouse interactions do not conflict with text inputs, 2D drawing tools, or camera controls.

Use the current 3D stack's supported controls where appropriate. Do not add Three.js-specific code if the app uses a different renderer.
## 5.2 Custom furniture / parametric objects

The designer must be able to create a custom object from primitives and/or editable parameters. Prioritize practical interior-design use cases:
- base cabinet, wall cabinet, tall cabinet, drawer unit, open shelving, bookcase;
- kitchen base units, wall units, tall units, island, countertop and backsplash;
- wardrobes with configurable width/height/depth, doors, shelves, drawers, plinth and handles;
- vanity units, bathroom cabinets, shower screens and simple fixtures;
- desks, dining tables, benches, TV/media units and custom storage;
- panels, wall cladding, decorative slats, built-in niches and false ceilings;
- custom simple forms built from boxes, cylinders, planes, extruded profiles or other primitives supported by the engine.

Each parametric family must expose relevant dimensions and options; a width change must update geometry predictably, not stretch labels or unrelated child parts. Components must have stable IDs and a schema/version so they can be saved, duplicated, edited, and migrated safely.

Where full mesh modeling is too large for the current engine, provide a real first slice: a parametric component builder plus primitive composition, dimensions, material assignment and save-to-user-library. Do not market it as unrestricted Blender-like mesh editing.

## 5.3 User-owned asset library

Build a persistent library for designer-created objects and assemblies:
- create a custom object, name it, categorize it, add tags, dimensions, thumbnail, source and notes;
- save it to a user/project library and reuse it in another room/project;
- edit an instance without unintentionally mutating every other instance;
- explicitly offer “edit this instance” versus “edit shared component” when the architecture supports linked components;
- duplicate an asset before editing when required;
- add user-defined catalogue categories and search/filter/sort;
- distinguish built-in, imported, custom, and manufacturer assets;
- preserve source attribution and license/usage rights for imported assets;
- never ship unlicensed external models as if Casa3D owned them.

## 5.4 Import custom 3D models

Implement a reliable, guarded import path for GLB/GLTF first if compatible with the existing renderer. Consider OBJ only if support is feasible and safe in the existing architecture.
- validate extension, MIME/type, file size and parsing outcome; do not trust extension alone;
- show progress, errors and a useful preview before insertion;
- allow placement, rotation, scale, orientation correction and numeric dimensions;
- compute and show a bounding box and initial dimensions;
- offer a safe fit-to-size action without losing the original proportions;
- handle missing textures/materials visibly;
- prevent remote URL fetching/SSRF and path traversal; prefer local upload and controlled storage;
- dispose renderer resources correctly when removing/replacing imported models;
- preserve asset provenance and license metadata;
- ensure imported geometry cannot bypass the Digital Twin's required validation when it participates in measurements, quantities or downstream calculations.

Imported render-only objects may be marked as non-measurable/non-BOQ until a user confirms scale and classification. Make this distinction visible.

# 6. Priority P0 — materials, finishes, lighting and room-level control

A designer needs to change the look of each project, not just select one global style.

- assign materials independently to walls, floors, ceilings, furniture and supported model surfaces;
- allow color, texture, roughness/finish and texture scale/orientation where supported;
- include surface-specific material assignment, not just global tinting of an entire model;
- provide a swatch/material library with user-created materials and favourites;
- support per-room finish schedules: wall finish, floor finish, ceiling finish, skirting, tile pattern/grout where appropriate;
- allow paint/material changes to be previewed and reverted before applying;
- provide reusable palettes/moodboards linked to actual project materials;
- make daylight, time-of-day and artificial lighting controls meaningful if the current renderer supports them;
- distinguish fast interactive preview from high-quality rendering; do not present a placeholder image as a render result;
- ensure material assignments survive save/reload, duplication, revision, import/export and project migration.

Do not promise physically accurate rendering unless it has been verified against the chosen rendering path.

# 7. Priority P1 — designer workflow and professional outputs

Implement working end-to-end workflows, prioritizing based on current gaps.

## 7.1 Room and project management
- create/rename/reorder rooms and set room purpose independently of geometry;
- edit room name, area, perimeter, ceiling height and finish assignments;
- select a room to isolate/highlight it in both 2D and 3D;
- room-specific object lists and visibility filters;
- copy a room/finish scheme when appropriate without overwriting geometry accidentally;
- show geometry warnings and unresolved dimensions clearly.

## 7.2 Design alternatives and client review
- save multiple named design alternatives/variants against the same base project;
- compare alternatives with a meaningful visual and data diff: changed objects, dimensions, materials, quantities and costs;
- switch between alternatives without destructive overwrites;
- allow a designer to preview changes before applying them;
- keep client approval tied to the exact version shown; stale approval must fail safely;
- distinguish draft, proposed, approved and superseded states;
- record who changed what and when where the existing identity model supports it.

## 7.3 Quantities and costs
- connect geometry and measurable catalogue items to BOQ/material calculations only when the model contains enough data;
- mark estimates and missing inputs explicitly;
- never generate fictitious prices or supplier availability;
- when a custom object lacks verified price data, allow manual price/cost entry with author and timestamp, or leave it UNKNOWN;
- show what changed in the quantity/cost calculation after a geometry or material change;
- make it possible to export the current BOQ and a finish/material schedule from the same saved project state.

## 7.4 Professional export and presentation
- export a dimensioned 2D plan and a project summary in PDF/print format using existing infrastructure;
- export a high-resolution image from a real rendered view where supported;
- export a material/finish schedule and BOQ;
- preserve units, scale, project title, revision/version and timestamp on outputs;
- provide useful camera presets (top, front, side, isometric, room-level view) and saved views;
- support a client presentation mode with minimal editing chrome and clearly labelled design version;
- support a walkthrough only if the camera/navigation implementation is real and testable;
- do not claim DXF/GLB/OBJ/PDF export unless a round-trip or artifact test verifies it.

# 8. UI/UX direction: professional tool, not a template showcase

The UI must prioritize the work surface and reduce friction during long design sessions.

Suggested desktop workspace:
- top bar: project, level, save/sync status, undo/redo, selection mode, units, version and export;
- left panel: tools, project/level hierarchy, room/object tree and searchable catalogue;
- center: switchable/synchronized 2D and 3D workspaces; optionally split view with adjustable divider;
- right inspector: contextual properties for the selected wall, room, opening, furniture, component, material or light;
- bottom/status area: current tool, dimensions, snap status, selected object and validation warnings.

Requirements:
- keyboard navigation and shortcuts for frequent tools;
- visible focus states, accessible labels, adequate contrast, and reduced-motion support;
- responsive layout for smaller screens without making the desktop editor unusable;
- consistent empty/loading/error/success states;
- useful empty state that starts a blank project or imports a plan;
- no fake activity indicators, misleading progress bars or dead buttons;
- preserve unsaved changes and show clear save/conflict status;
- non-destructive dialogs with explicit confirmation for destructive actions;
- avoid modal overload; use inspector panels and context menus where faster;
- tooltips should explain the action and shortcut;
- make it clear whether an operation changes one object, a linked component, a room, a level, or the whole project.

The visual style can be modern and premium, but functionality and spatial clarity take priority over decorative animations. Use the existing design system where possible; avoid a wholesale rewrite that breaks working routes or tests.

# 9. Geometry, data integrity, persistence and safety

Treat these as architectural acceptance requirements, not optional polish.

- all units and coordinate conversions must be explicit; maintain the canonical project unit;
- wall joins, openings, rooms, floor areas, multi-level elevations and dimensions must be computed from actual geometry;
- validate object bounds and invalid geometry before accepting mutations;
- invalid edits must leave the prior valid state unchanged;
- keep 2D and 3D views synchronized from the same persisted model;
- save/reload must preserve object IDs, geometry, transforms, materials, level assignment, custom-component definitions and catalogue references;
- implement or reuse schema migrations and backward-compatible loading for existing projects;
- test duplicate/copy/paste and undo/redo for ID collisions and reference corruption;
- handle concurrent save conflicts without silently overwriting newer work;- avoid destructive project migration and avoid touching production/customer data in tests;
- sanitize imported files and user-provided text; enforce authentication/authorization on private APIs and uploaded assets;
- avoid logging secrets, client personal data, project share tokens or private file contents;
- ensure share links and public client views expose only intended data;
- never bypass existing approval, authority, validation or licensing gates.

# 10. Required test scenarios

Write tests before or alongside implementation. Test behavior, not merely the presence of UI labels.

At minimum cover:
1. Create a blank project and draw an irregular, non-rectangular room using exact dimensions.
2. Split/join/move a wall and verify computed room geometry/area and 3D update.
3. Add an opening to a wall and verify dimensions and placement survive reload.
4. Add a second level and connect it with stairs; verify elevations and persistence.
5. Create a custom cabinet with parameters, save it to the user library, insert it into another room, and edit one instance without unintended shared mutation.
6. Import a valid GLB/GLTF model, position/rotate/scale it, save/reload, and verify bounding box and metadata.
7. Reject an invalid/corrupt/oversized or unsupported model with a useful error and no partial project mutation.
8. Assign a custom finish to one surface and verify it does not unintentionally change unrelated surfaces.
9. Undo/redo a series of geometry and object operations and compare canonical project state.
10. Save, reload, duplicate, and migrate a project while preserving stable IDs and references.
11. Change a design alternative and verify the version diff, quantities and client approval binding.
12. Verify missing price/license/source data remains UNKNOWN and is not fabricated.
13. Exercise keyboard/mouse tool interactions so camera controls, selection and text inputs do not conflict.
14. Test small viewport layout, keyboard focus, accessible labels and reduced-motion behavior.
15. Run existing project tests and ensure no regressions in current client-link, BOQ, procurement, revisions, export, authentication and geometry validation.

Add at least one end-to-end test for the full flow: blank/imported plan → custom geometry → custom furniture → material assignment → save/reload → 2D/3D verification → export. Use realistic non-rectangular fixtures, not only the current sample templates.

# 11. Execution plan and engineering gates

Work in small, reviewable increments. Recommended sequence:

**Gate A — Baseline**
- establish current branch and merge base;
- inspect PR #257/#258 and identify overlap/conflicts;
- run baseline test/typecheck/build commands;
- record failures that existed before changes.

**Gate B — Core editor**
- wall/room drawing and exact dimensions;
- selection, snapping, numeric inspector, undo/redo;
- synchronized 2D/3D state and geometry validation.

**Gate C — Custom objects**
- hierarchy and transform gizmos;
- parametric furniture/component builder;
- persistent user library;
- validated model import.

**Gate D — Finish workflow**
- per-surface materials, room finish schedules and meaningful preview controls.

**Gate E — Professional delivery**
- alternatives/version diff, BOQ/finish schedule, exports, client presentation and regression coverage.

**Gate F — UX, accessibility and hardening**
- keyboard shortcuts, focus, responsive layout, loading/error states, persistence conflicts, import security and performance.

Adjust the sequence to avoid duplicating capabilities already implemented. Do not defer all meaningful functionality behind flags or a future TODO list.

For each increment:
1. state the failing test or concrete gap;
2. implement the smallest complete vertical slice;
3. run the focused tests;
4. run TypeScript/typecheck, full relevant test suite and production build;
5. inspect the actual UI/runtime when the environment supports it;
6. record exact command, exit code, and relevant output;
7. review the diff for accidental scope expansion, security regressions and broken compatibility.

Do not use a grep pipeline's exit status as proof that tests passed. Preserve the test runner's own exit code. Never weaken or delete a test merely to make CI green. Do not claim a browser/runtime test if it was not run.

# 12. Definition of done

This task is not done when the page looks better or the PR description lists features. It is done only when:
- the actual application supports at least one complete, tested workflow from a blank or imported plan through custom geometry and custom objects to saved/reloaded project and export;
- the editor supports irregular layouts and exact dimensions, not only fixed rectangular demo rooms;
- custom furniture/components can be created, saved, reused and edited;
- object transforms and surface/material edits modify the real persisted model;
- the 2D plan and 3D view stay consistent;
- invalid geometry/imports fail safely with no partial mutation;
- the relevant tests, typecheck and build have verified results;
- the PR documents what is REAL, PARTIAL, DEMO, UNKNOWN or BROKEN, with evidence;
- known limitations and untested scenarios are listed honestly.

If time or context limits prevent all requirements from being implemented, complete a coherent high-value vertical slice, provide evidence, and leave a prioritized implementation ledger. Never label unimplemented work as complete.

# 13. Final report expected from Gemini 4 Argon

Respond in Romanian and provide:
1. Baseline: branch/commit, architecture, and REAL/PARTIAL/DEMO/UNKNOWN/BROKEN matrix.
2. Research synthesis: what product workflows informed the changes and why.
3. Implementation: files/modules changed and user-visible behavior delivered.
4. Tests: exact commands, exit codes, pass/fail counts and any untested UI/runtime behavior.
5. Security/data-integrity review: import validation, persistence, geometry invariants, licensing and privacy.
6. Remaining gaps ranked P0/P1/P2, each with evidence and acceptance criteria.
7. A short manual acceptance checklist for the designer.
8. Explicit statement of what was not implemented or could not be verified.

**Do not auto-merge. Do not bypass owner approval. Do not report success without fresh evidence.**

---
