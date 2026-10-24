# todo-claude-casa3d-doors
STATUS: IN_PROGRESS (lot 2a reviewed READY; lot 2b done, not reviewed)   UPDATED: 2026-10-25T03:00:00+03:00
TASK: Casa3D lot 2 — door types (2a) + wall decor P1.b1–P1.b3 (2b), spec D:/w/casa3d-verify/research/usi-decor.md §3(b)
BRANCH / PR: claude/casa3d-doors-decor / none (pushed; PR is the coordinator's call)    BASE: 40b32343 (claude/casa3d-plan-tools)
LOT 2a (door types, filomuro): see commits da468941, 85f535d7; accepted residuals: 3D framing ignores pivot/fold, pocket click.
LOT 2b DONE (commits c1852bab, b31eb091, dded0fe7):
- P1.b1 WallFeature.alongFromM/alongToM (from the x0/z0 corner; absent = whole side) + vertical (slats). 2D overlap check,
  insertBand cuts existing bands around a new stretch. Tool "Placare" (K, Finishes): click face, click end; Enter or same spot
  = whole side; kind picker next to the plan. Plan band 4 cm + marks R1/T1/V1 + legend (Finishes, Drawings). Panel: From/To,
  Whole side, Vertical slats. 3D cut to the stretch, horizontal slats. Elevation bands u0/u1, schedule detail, BOQ on stretch.
- P1.b3 wallpaperRolls: drop rounded up to the repeat (+r/2 offset), spare 10/15 % to whole rolls (WALLPAPER_RULES),
  roll.match; BOQ text strips/drop/spare/same batch. Plaster effect (WallFeature.effect | specs.effect), EFFECT_LABOR;
  no sourced products/rates -> material + labour in "unknown" (materials.v1.json untouched). Warnings microcement wet,
  effect unpriced. Procedural 3D textures per effect.
- P1.b2 Floor.wallItems (art/mirror/shelf/tv; wallId+side, centre offset/elevation). core/wall-items.ts (geometry, face,
  mounting rule 1.45 m / 20 cm above sofa-bed-chest, issues WALL_ITEM outside/overOpening/slideZone/behindTall).
  Tool "Pe perete" (X; Concept, Finishes, FF&E), group "decor" in lib/tool-groups.ts. Panel, plan symbol, 3D (in front of
  cladding; auto art suppressed on that side), BOQ (line only with product; mounting unknown), elevations, mirror list,
  frame, Tab, PageUp/PageDown, wall delete cascade, new level does not copy. Server: plan-limits sanitizeWallItem.
DEVIATIONS FROM SPEC (deliberate): wallId missing -> item dropped (like niches), offset/elevation clamped not 400 (no
  blocked saves); height keys PageUp/PageDown instead of Alt+arrows (Alt+Left = browser Back); one code WALL_ITEM with keys.
NEXT:
1. Coordinator: independent review of lot 2b (vault-reviewer), then PR.
2. Catalogue research (materials-evidence): wall_art / mirror / wall_shelf products, riflaj/venetian/microcement rates.
3. Optional: DXF/print plan for wall items (not drawn there now), drag-on-plan for a band's ends, P2.b4 boiserie.
RISKS: old projects with wallpaper now order the spare roll (intended correction); bathTiles still skip a whole side that
  has any feature (also a partial one); 3D wall thickness for items uses the engine's .15/.25 convention.
KEY FILES: app/core/{wall-items,finishes,boq,drawings,plan-limits,validate,rules,types,plan-describe,plan-keys,frame}.ts,
  app/components/{Editor,PlanView,FinishesPanel,Drawings}.tsx, viewer3d-engine.js, finish-textures.js, lib/viewer-input.ts,
  lib/tool-groups.ts, lib/commands.ts, tests/{wall-items,finishes}.test.ts
VERIFICATION: tsc clean; vitest --maxWorkers=2 129 files / 808 tests pass; npm run build OK; check-a11y 19/19 OK on :3376;
  axe on wall-item panel + kind picker + legend + finishes panel: 0 violations (D:/w/casa3d-verify/decor-axe.mjs);
  shots D:/w/casa3d-verify/shots/decor-*.png (decor-shots.mjs). CI not checked (agent stops after push).
