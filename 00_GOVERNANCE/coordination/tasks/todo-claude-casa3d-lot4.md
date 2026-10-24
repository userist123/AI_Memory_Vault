# todo-claude-casa3d-lot4
STATUS: DONE for items 1-4 and 6; item 5 NOT DONE (see below); PR #348 review fixes pushed UPDATED: 2026-10-24T18:10:00+03:00
TASK: Casa3D lot 4 "tools around the house" (unelte-plan.md P2.4, P2.5; usi-decor.md P2.b4, P2.c3; P2.c2/P2.c4 skipped)
BRANCH / PR: claude/casa3d-house-lot4, PR #348 (open; main merged in, review fixes pushed)    BASE: 215d1260
DONE:
- Floor.ceilingZones (rect in a room, dropCm 5-60, coveCm, led): tool Shift+Z in Finishes (Decor group, 3 slots),
  plan dash-dot "TF -15", panel, 3D box with cove/LED, quote (plasterboard + side band + cove, LED ml, labour),
  CEILING_ZONE_OUTSIDE_ROOM / OVERLAP / LOW (underside < door or window top + 5 cm next to it), DXF CEILING_ZONES.
- Room.spaceKind select ("Tip spațiu") in the survey room panel; server drops unknown values.
- Floor.railings (a, b, height, kind metal|glass): tool Shift+Y in Survey (Structure group, 7 slots), chained clicks;
  3D metal list + glass through peretiSticla with h; quote unknown in ml; RAILING_LOW < 0.9 m; DXF RAILINGS; addLevel copies.
- WallFeature kind 'frames' (boiserie) with frames {count?, marginM?, gapM?}: auto count, frames skip openings,
  moulding in whole pieces + labour ml, wall paint unchanged, 3D relief, elevations, server limits.
- RugChoice x/z + RoomFinishes.rugs (max 2 extra): panel position/centre/add, plan, 3D, quote line per rug,
  tex.rugOutside / rugOverlap, decor sofa/bed rules on the real position (rugHints).
- Server (core/plan-limits.ts): clamp finite, reject NaN/Infinity/strings, caps, unique ids.
VERIFIED: tsc 0; vitest 886/886 (134 files); build; check-a11y 20/20; check-slides 5/5;
  shots D:/w/casa3d-verify/shots/lot4-*.png (script D:/w/casa3d-verify/lot4-shots.mjs).
  Legacy: tests/lot4-legacy-golden.test.ts hashes of blank/demo outputs captured on main before lot 4.
NOT DONE (item 5): catalog has no plant/floor-lamp/table-lamp/fireplace groups; adding them needs product pages
  through the catalog-evidence process (tests/catalog-growth.test.ts ties offers to evidence files), plus engine
  models, plan symbols and room-fit rules. Not started, no data invented.
RESIDUALS: ceiling zones are rectangles, not copied to new levels; railing has no price; spaceKind only labels
  (no area exclusion yet); rug position is numeric (no drag); the new tool keys use Shift (letters exhausted).
KEY FILES: core/{ceiling-zones,railings,finishes,textiles,decor,boq,validate,dxf,plan-limits,plan-describe}.ts,
  lib/{viewer-input,commands,tool-groups}.ts, components/{Editor,PlanView,FinishesPanel,viewer3d-engine}.*,
  tests/{house-tools-lot4,lot4-legacy-golden}.test.ts
PR #348 FIXES (2026-10-24): merged origin/main (merge, no rebase) - context menu (#341) kept, ceiling_zone and railing added to
  ContextKind / CONTEXT (frame, delete); railing is a segment tool, so right click while drawing ends the chain without a menu
  (main's rule for wall / beam). Golden test passed unchanged after the merge (no hash regeneration).
  Ceiling zone not below the room's dropped ceiling: no quote lines, no 3D, warning CEILING_ZONE_ABOVE_ROOM_CEILING (RO/EN);
  validateFloor takes the room drop (roomCeilingDropCm). Rug at the room centre (+-1 cm) counts as centred (no false tex.rugSofa).
  Server keeps only RugChoice keys in rug and rugs[]. Railing under 0.2 m = 400. Rug buttons named with the rug number.
  VERIFIED: tsc 0; vitest 905/905 (135 files); build; check-a11y 20/20; check-slides 5/5;
  shots D:/w/casa3d-verify/shots/lot4-fix-*.png (script D:/w/casa3d-verify/lot4-fix-shots.mjs).
