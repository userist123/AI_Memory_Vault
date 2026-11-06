# todo-claude-casa3d-insorire
STATUS: IN_PROGRESS        UPDATED: 2026-11-06
TASK: Casa3D studiul de însorire (owner: „inovare, fii ambițios”; roadmap A1 faza 4 e blocată pe textul ISO 7200).
  Design: 02_PRODUCT/projects/workspaces/casa3d/PLAN_INSORIRE.md.
BRANCH: claude/casa3d-insorire, worktree D:/w/sun (base origin/main 0d97d13c3). Dev: launch config casa3d-sun, port 3220.
DONE: core/solar.ts (NOAA), core/site-location.ts (Snapshot.site, rotunjit 0,01°), core/insolation.ts (raze prin goluri,
  glaf, aripi, tavan; pete de soare), components/SunStudy.tsx (panou etapa Concept, strat pe plan), PlanView prop overlay,
  Viewer3D prop sun + core/lighting solarLighting, advisor SUN_LOW, lib/repo.ts validare site, RO+EN.
  tsc 0; vitest 143 fișiere / 990 teste; next build OK; verificat în aplicația reală (RO, EN, iarnă, vară, nord 180°, 3D, Verificări).
REVIEW: runda 1 (4 defecte reproduse: terasă, DST/fusuri, fus invalid tăcut, hhmm + colț, perf) reparate cu regresii; runda 2 PASS.
  Rezidual acceptat: petele de soare aproximative la camere poligonale; TZ_RE refuză „UTC” simplu (Etc/UTC merge).
NEXT: PR draft -> CI o dată -> merge e decizia proprietarului.
IDEAS AFTER: obstrucții exterioare (clădiri vecine ca prisme), umbrirea de la nivelul de deasupra/balcoane, raport PDF
  „studiu de însorire” pe planșe, harta anuală (ore pe lună), partajarea studiului pe linkul clientului.
KEY FILES: app/core/{solar,site-location,insolation}.ts, app/components/SunStudy.tsx, app/tests/insolation.test.ts.
BLOCKERS: none. Textul consolidat al Ordinului 119/2014 după 994/2018: NEVERIFICAT (citat după surse secundare).
