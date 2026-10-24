# todo-claude-casa3d-catalog
STATUS: DONE (pushed, no PR)  UPDATED: 2026-10-24T22:30:00+03:00
TASK: Casa3D catalog: verified products from D:/w/casa3d-verify/research/produse-preturi.json (2026-10-24)
BRANCH: claude/casa3d-catalog-decor (from main 281884b7)   PR: none (push only, owner opens it)
DONE:
- Hornbach allowed retailer: RETAILER_HOSTS, catalog suppliers, material link slug by supplier name (lib/db.ts),
  materials test regex, links page; shop filter lists only shops with furniture offers; Transport Hornbach unknown line.
- Furniture groups plantaArtificiala, plantaNaturala, lampadar, veioza, semineu, focarLemne: 23 offers +
  data/catalog-evidence.2026-10-24.json; 3D models plant/floorlamp/tablelamp/fireplace/stove; plan symbols;
  FRONT_CLEARANCE (semineu .6, focarLemne .8, rest 0); rooms (not in bath/kitchen for fireplaces); tech outlets;
  veioza / plantaNaturala may sit on a low piece (ALLOWED_OVERLAP, surfaceUnder -> 3D y); CHIMNEY warning (RO/EN).
- Left out (no full dimensions on page): FMK-02, JWS2999, Lafiora (Hornbach), Chester 49386, Nordflam Atina.
- 31 materials + data/materials-evidence.2026-10-24.json: door_system pocket/sliding (picker of lot 2a),
  folding doors (door, doorTypes folding; unknown line "accesorii de prindere"), towel_radiator, moulding (2 m),
  new panel_radiator / railing (specs.railPart) / hvac_split. TechPoint.product and Railing.product (metal only),
  kept by the server; pickers in TechPanel and railing panel; quote lines + unknown connection/route/installation.
- Labour (research section C) NOT integrated.
VERIFIED: tsc 0; vitest 917/917 (136 files); build; check-a11y 20/20; check-slides 5/5;
  tests/lot4-legacy-golden.test.ts passes unchanged (no hash regeneration).
  Shots D:/w/casa3d-verify/shots/catalog-*.png (script D:/w/casa3d-verify/catalog-shots.mjs).
RESIDUALS: group labels are product.category (RO only, like existing groups); 3D foliage width is not declared
  (pot footprint only); railing quantity rule (pieces / posts at 1.2 m) is an app rule; Catumia panel kit is the
  starter kit price; Megadoor cassette leaf width not declared; promo prices may change.
KEY FILES: app/data/*2026-10-24*, app/core/{outbound,rules,plan-symbols,furniture-fit,boq,types,validate,technical,
  plan-limits,finish-schedule}.ts, app/components/{viewer3d-engine.js,Editor.tsx,TechPanel.tsx,MaterialPicker.tsx},
  app/tests/{catalog-growth,materials-catalog,catalog-materials-2026-10-24}.test.ts
