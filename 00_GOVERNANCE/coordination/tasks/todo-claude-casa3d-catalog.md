# todo-claude-casa3d-catalog
STATUS: IN PROGRESS  UPDATED: 2026-10-24T20:00:00+03:00
TASK: Casa3D catalog: verified products from D:/w/casa3d-verify/research/produse-preturi.json (2026-10-24)
BRANCH: claude/casa3d-catalog-decor (from main 281884b7)   PR: none (push only)
PLAN:
1. Hornbach as allowed supplier (RETAILER_HOSTS, catalog suppliers, material link slug, tests regex, links page).
2. Furniture groups plantaArtificiala, plantaNaturala, lampadar, veioza, semineu, focarLemne:
   catalog.v1.json + catalog-evidence.2026-10-24.json, rules, plan symbols, room fit, 3D models, chimney warning.
3. Materials: door_system (pocket/sliding), folding doors (door, doorTypes folding), towel_radiator, moulding,
   new panel_radiator / railing / hvac_split + materials-evidence.2026-10-24.json; quote uses chosen product.
4. Tests (offer-evidence, quote with product), legacy golden unchanged; tsc, vitest, build, a11y, slides, shots.
RULES: labor section NOT integrated (stays unknown); no invented prices or dimensions.
DONE: (none yet)
NEXT: step 1
KEY FILES: app/data/*, app/core/{outbound,rules,plan-symbols,furniture-fit,boq,types,validate,technical,railings}.ts,
  app/components/{viewer3d-engine.js,Editor.tsx,TechPanel.tsx}, app/tests/{catalog-growth,materials-catalog}.test.ts
