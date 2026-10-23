# todo-claude-casa3d-doors
STATUS: IN_PROGRESS (lot 2a done; next lots not started)        UPDATED: 2026-10-24T02:10:00Z
TASK: Casa3D lot 2a — door types (swing, double, sliding, pocket, folding, pivot) + concealed (filomuro)
BRANCH / PR: claude/casa3d-doors-decor / none (pushed; PR is the coordinator's call)    BASE: 40b32343 (claude/casa3d-plan-tools)
SPEC: app/core/types.ts (Opening.doorType / flush comment), app/core/rules.ts (DOOR_TYPE_RULES)
DONE:
- Model: Opening.doorType (absent = swing) and flush; passage wins (server drops type/flush on a passage).
  hinge/swing reused per type (hinge = parking/folding end for sliding/pocket/folding).
- Server: plan-limits sanitizeDoorType (enum, door only, width range per type only when a type is set, so old projects load).
- Rules (validate.ts): pocket without free wall (leaf + 10 cm) = ERROR, sliding = WARNING, pocket on exterior/thin wall
  and double under 1 m = WARNING; door zone depth per type (doorZoneDepth); furnish swing zones per type.
- door-geometry.ts: doorTypeOf, slideSide (free wall each side, stops at openings and joining walls), slideZone, doorSwing
  (sliding parks toward the side with more free wall by default).
- doors.ts: door.typeMismatch (product specs.doorTypes) and door.techInSlide (wall sockets/switches in the parking zone);
  DoorChoice.system (category door_system, specs.system); BOQ line `doorsys:` + finish schedule element door_system.
- Plan: door-symbols.ts drawn by PlanView (data-door-type); DXF draws double as two arcs, others as symbol segments.
- 3D (viewer3d-engine.js): double = two leaves, sliding = leaf on wall face + rail, pocket = leaf slides into the wall,
  folding = leaf folds (scale), pivot = rotates about w/4; flush = no frame/architraves. setLeaf() drives all animations.
- Editor: type select (full width), flush checkbox, per-type hint, flip buttons per type, width clamped to the type range,
  type change resets width to the type standard when out of range and drops a system made for another type.
- legacy.js round trip (tipUsa / filomuro); plan mirror names type and flush; ro/en strings.
NEXT (in order):
1. Coordinator: review + PR for this branch (contains the plan-tools commits it is based on).
2. Catalog has no door_system products yet (picker shows only when products exist); adding real ones needs product-page research.
3. DXF pivot door: leaf line only (arcs only in the plan SVG).
BLOCKERS / OWNER QUESTIONS:
- none for 2a.
KEY FILES:
- app/core/{door-geometry,door-symbols,doors,validate,plan-limits,rules,types,furnish,boq,dxf,plan-describe,finish-schedule}.ts
- app/components/{Editor.tsx,PlanView.tsx,viewer3d-engine.js}, app/features/migration/legacy.js, app/lib/locales/{ro,en}.ts
- app/tests/door-types.test.ts (17 tests)
VERIFICATION SO FAR: tsc --noEmit clean; vitest --maxWorkers=2: 128 files / 783 tests pass (the default worker count ran
out of memory locally — WebAssembly.Memory allocation failed in workers; environment, not code). Local dev server captures
(plan 1600, door panel pocket/sliding/double, 3D) and axe WCAG A/AA on the door panel: 0 violations. CI not checked (agent stops after push).
