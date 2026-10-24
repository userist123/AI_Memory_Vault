# todo-claude-casa3d-lot3
STATUS: DONE (pushed, no PR) UPDATED: 2026-10-24T12:10:00+03:00
TASK: Casa3D lot 3 "tools around the house": unelte-plan.md P2.2, P2.3, P1.5; usi-decor.md P1.c1
BRANCH / PR: claude/casa3d-house-lot3 / none (PR is the coordinator's call)    BASE: 62be1f8e
DONE:
- P2.2 Wall.status existing|new|demolish (missing = existing). Tool Demolare (Y, survey, group Structure): click toggles
  existing <-> demolish; status select in the wall panel. Plan: new hatched, demolished dashed (no openings). 3D: demolished
  wall kept in plan.pereti (indices) but `demolat`, no goluri, not drawn. Openings ignored (openingsOnSide, doorOpenings,
  textiles, validate). Quote: demolition m2 + rubble unknown; new <=12.5 cm plasterboard x2 faces (priced) + labour unknown;
  thicker = masonry unknown. WALL_DEMOLISH warning on exterior walls. DXF WALLS_NEW / WALLS_DEMOLISH only when used.
  plan-describe appends the status. core/renovation.ts.
- P2.3 radiator: TechPoint widthM/heightM/model (panel|towel|decorative); placed via the MEP points tool (kind radiator),
  centred under the nearest window on the clicked wall (placeRadiator); fields in the TechPanel list. Plan ribbed rect,
  3D ribbed box, quote unknown per piece, RADIATOR_BLOCKED (tall furniture within 30 cm). Old points without widthM unchanged.
- P1.c1 AC: hvac_split rendered in 3D (80x29x20 white box on the wall face), unknown quote line, warnings HVAC overBed /
  slide (sliding/pocket door run). core/tech-geometry.ts, techIssues in technical.ts.
- P1.5 Floor.zones {id, roomId, rect, floor, layout?, label?}: tool Zonă pardoseală (Z, Finishes, group Decor; drag /
  click-click / typed). Plan hatch + area; 3D plane +1 mm; quote room minus zones + own line + labour; ZONE_OUTSIDE_ROOM /
  ZONE_OVERLAP errors; server sanitizeZone; room delete removes its zones (removeRoomZones). core/floor-zones.ts.
- Rail: railSlots folds a group with >=3 members even under GROUP_OVER (Finishes 4 -> 3 slots); survey still 7.
- Caps: PLAN_LIMITS.zone/radiator/hvac + full() at placement + checkSnapshot. Clamp finite, reject NaN/Infinity/strings.
VERIFIED: tsc, vitest 842/842, build, a11y 19/19, slides 5/5, shots D:/w/casa3d-verify/shots/lot3-*.png
ACCEPTED RESIDUALS / NEXT:
- Existing hvac_split points now show in 3D and add an unknown quote line (P1.c1 by design).
- Demolish has no count cap (it places nothing); phone rail (flat) gains 1 button in Survey and Finishes.
- Zones are rectangles only (no boundary contour); quote still on room rect (report §4.7).
- AC plan symbol unchanged (circle); radiator/AC not draggable on plan (edit in list).
KEY FILES: core/{renovation,floor-zones,tech-geometry,technical,plan-limits,boq,validate,dxf}.ts, lib/{repo,viewer-input,
  tool-groups}.ts, components/{Editor,PlanView,TechPanel,PrintView}.tsx, components/viewer3d-engine.js,
  tests/house-tools-lot3.test.ts
