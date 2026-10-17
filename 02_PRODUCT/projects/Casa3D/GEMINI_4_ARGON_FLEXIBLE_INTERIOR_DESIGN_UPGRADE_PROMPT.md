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

# 14. Additional owner requirement — complete, adaptable whole-home concept and systems design

**This section extends the existing brief; it does not replace or narrow earlier requirements.** The owner explicitly requires that Casa3D serve a professional interior designer who receives unrelated projects, not a consumer who selects one fixed house template. Extend the *same implementation effort and PR #258*. Do not create a disconnected demo, a parallel product, or a second source of truth.

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

Before implementation, add a traceability table mapping each new requirement to (a) existing code, (b) the exact gap, (c) a test, (d) an implementation step and (e) evidence of completion. Then implement in the same Casa3D branch/PR, prioritizing real editing workflows over breadth of superficial controls. Preserve working features and existing project data. No auto-merge. No weakening tests. No fake demos. No unsupported claims of engineering/code compliance.
