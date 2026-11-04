# todo-claude-casa3d-lot3
STATUS: DONE (merged #344, 215d1260) UPDATED: 2026-10-24T17:40:00+03:00
TASK: Casa3D lot 3 "tools around the house" (unelte-plan.md P2.2, P2.3, P1.5; usi-decor.md P1.c1) + PR #344 review fixes
BRANCH / PR: claude/casa3d-house-lot3 / #344    BASE: 62be1f8e
DONE (lot): Wall.status existing|new|demolish + tool Demolare; sized radiator; AC in 3D/quote; Floor.zones (tool Zonă
  pardoseală); rail folding; caps. Details: core/{renovation,floor-zones,tech-geometry}.ts, tests/house-tools-lot3.test.ts.
DONE (review #344, tests/lot3-review-fixes.test.ts, each fails on the pre-fix code):
- core/renovation.ts activeOpenings(w) = the single helper ([] for a demolished wall); used by plan-describe, drawings
  (elevations), trade-checks VENT_BATH, advisor TECH_NO_SWITCH, furnish hall entrance keep-out, lib/twin.
- components/Drawings.tsx Walls (exported): demolished dashed without openings, new hatched (as PrintView).
- finish-schedule: `<room>:zone:<id>` -> floor row with the zone's material/layout + detail sched.zone;
  `wall-new:<id>` -> element `drywall` ("Perete nou gips-carton" / "New plasterboard wall"), room = whole home.
- levels.addLevel skips demolished walls (and their openings/niches); new walls copied.
- tool-groups FLAT_MAX = 4: a stage of <= 4 tools stays flat (Planșe 4 buttons); Finishes 5 -> 3 slots, Survey 7.
- TechPanel CmField: local draft, commit (clamped, cmToClampedM) on blur/Enter, Esc reverts; aria "Lățime calorifer, <room> (cm)".
- plan-limits isMaterialRef + FLOOR_PATTERNS shared with finishes.sanitizeFinishes/PATTERNS (zone also needs non-empty).
VERIFIED: tsc 0, vitest 860/860 (132 files), build, a11y 20/20, slides 5/5 (4 viewports OK),
  shots D:/w/casa3d-verify/shots/lot3-fix-{ceiling,floor,elevations}.png (script lot3-fix-shots.mjs).
ACCEPTED RESIDUALS / NEXT:
- New HVAC warnings / unknown quote line on old projects with hvac_split: unchanged on purpose.
- Drywall schedule row has no room (wall may border several); note text is the Romanian BOQ label.
- Demolish has no count cap; zones are rectangles only; AC plan symbol unchanged; radiator/AC not draggable on plan.
- CI not watched (agent pushes and stops).
KEY FILES: core/{renovation,plan-describe,drawings,finish-schedule,trade-checks,advisor,furnish,levels,plan-limits,
  finishes,tech-geometry}.ts, lib/{twin,tool-groups}.ts, lib/locales/{ro,en}.ts, components/{Drawings,TechPanel}.tsx,
  tests/{lot3-review-fixes,house-tools-lot3,plan-tools}.test.ts
