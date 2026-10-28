# todo-claude-casa3d-catalog
STATUS: DONE (review fixes PR #352 pushed)  UPDATED: 2026-10-24T23:59:00+03:00
TASK: Casa3D catalog: verified products from D:/w/casa3d-verify/research/produse-preturi.json (2026-10-24)
BRANCH: claude/casa3d-catalog-decor (from main 281884b7)   PR: #352
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
  starter kit price (fixed in review round, see below); Megadoor cassette leaf width not declared; promo prices may change.
KEY FILES: app/data/*2026-10-24*, app/core/{outbound,rules,plan-symbols,furniture-fit,boq,types,validate,technical,
  plan-limits,finish-schedule}.ts, app/components/{viewer3d-engine.js,Editor.tsx,TechPanel.tsx,MaterialPicker.tsx},
  app/tests/{catalog-growth,materials-catalog,catalog-materials-2026-10-24}.test.ts
REVIEW #352 FIXES (2026-10-24):
- radiator: boq radiatorFits (category + specs.sizeCm, tol 1 cm), else unknown "produsul ales are altă mărime";
  TechPanel drops the product on resize (announced in CmField role=status), picker lists only fitting sizes + "Nespecificat".
- railing: Catumia railPart "start" (page: kit complet pentru inceperea balustradei): 1 kit + unknown extension panels line;
  source of every quantity rule commented in core/boq.ts; lib/db.ts migrates an old "panel" spec once at boot.
- validate: surfaceSupport — lamp/plant overlap allowed only on the piece it fully fits on; else OVERLAP and floor in 3D.
- server: core/technical.ts pruneProducts (catalog + category) in checkSnapshot(s, mc), called by saveDraft (drop, not 400;
  doors only check id shape, no catalog check — not changed).
- a11y: pickers named with room ("Produs calorifer, X", AC, railing); new check-a11y state editor-instalatii-calorifer (21).
VERIFIED: tsc 0; vitest 920/920; build; check-a11y 21/21 OK; check-slides 5/5. Shots catalog-fix-*.png
  (script D:/w/casa3d-verify/catalog-fix-shots.mjs).
RESIDUALS: empty radiator picker shows "Nimic găsit" plus the sizes hint; railing "set"/"handrail" joining is an app rule.
