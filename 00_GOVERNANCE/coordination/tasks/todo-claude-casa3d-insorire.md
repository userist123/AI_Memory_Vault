# todo-claude-casa3d-insorire
STATUS: IN_PROGRESS        UPDATED: 2026-11-07
TASK: Casa3D studiul de însorire (owner: „inovare, fii ambițios”; roadmap A1 faza 4 e blocată pe textul ISO 7200).
  Design: 02_PRODUCT/projects/workspaces/casa3d/PLAN_INSORIRE.md.
BRANCH: claude/casa3d-insorire, worktree D:/w/sun (base origin/main 0d97d13c3). Dev: launch config casa3d-sun, port 3220.
DONE: core/solar.ts (NOAA), core/site-location.ts (Snapshot.site, rotunjit 0,01°), core/insolation.ts (raze prin goluri,
  glaf, aripi, tavan; pete de soare), components/SunStudy.tsx (panou etapa Concept, strat pe plan), PlanView prop overlay,
  Viewer3D prop sun + core/lighting solarLighting, advisor SUN_LOW, lib/repo.ts validare site, RO+EN.
  tsc 0; vitest 143 fișiere / 990 teste; next build OK; verificat în aplicația reală (RO, EN, iarnă, vară, nord 180°, 3D, Verificări).
REVIEW: runda 1 (4 defecte reproduse: terasă, DST/fusuri, fus invalid tăcut, hhmm + colț, perf) reparate cu regresii; runda 2 PASS.
  Rezidual acceptat: petele de soare aproximative la camere poligonale; TZ_RE refuză „UTC” simplu (Etc/UTC merge).
ETAPA 2 (owner: „mai inovator”): core/sun-design.ts (remedii generative pe front Pareto, applyRemedyWall, swapIdeas),
  core/sun-effects.ts (soare pe TV/birou/pat, câștig solar Meinel), panou: propuneri cu previzualizare/aplicare, mobilier,
  coloana kWh. Revizie 2 runde (FAIL apoi PARTIAL, toate reparate cu regresii). vitest 1002; build OK; verificat în aplicație.
ETAPA 3 (owner: „hai să le facem, fii ambițios”, după raportul de cercetare): corecturi de metodă (convenție, total + interval,
  fus orar din coordonate, fără prag 0,2), cele două mesaje art. 3 (D ≤ H max separat de verdict), vecinii în ambele sensuri
  (core/neighbours.ts: calcan, amplasament mai vechi, advisor SUN_NEIGHBOUR), etajele de deasupra umbresc, SR EN 17037, vara
  după AD O + comparator de umbrire în lei (core/overheating.ts), raport /p/[id]/sun, vecini pe plan și în 3D (engine setContext).
  Revizie etapa 3: runda 1 PARTIAL (distanța 0 punea vecinul în casă; „toString”/„constructor” trecute drept geam/protecție și
  crash în summerCheck; fusul României peste estul Ungariei/Serbiei; vecin < 1,8 m fără puncte) -> toate reparate cu regresii;
  runda 2 PASS. Rezidual acceptat: conturul fusului orar ±10–20 km la granițe (fusul se poate alege manual).
  vitest 145 fișiere / 1018 teste; next build reușit; verificat în aplicație (vecin pe plan, în 3D, raportul /sun).
ETAPA 4 (owner 2026-11-09: „inovează și renovează platforma, conceptele și utilitarele”): core/daylight.ts (FLN pe cer CIE
  acoperit + BRE split-flux, harta albastră), core/light-class.ts (calendarul anual, clasa de lumină A–E), înregistrarea zilei
  în 3D (WebM), panoul pe file, comenzi Ctrl+K, raportul cu clasa și FLN. CI etapa 3 verde (b0f0e829f).
  Revizie etapa 4: runda 1 PARTIAL (clasă A cu norma picată, transmisie dublă în CRI, -0,0 %, recorder fragil, C ≥ 14) și
  runda 2 PARTIAL (clasă pe nivel fără camere de locuit, StrictMode) -> toate reparate cu regresii. vitest 1025; build OK;
  verificat în aplicație (harta FLN, clasa B pe demo, calendarul, Ctrl+K, raportul, înregistrarea WebM).
ETAPA 5 (owner: „continuă, nu te opri până nu termini”): core/view-out.ts (vederea spre exterior EN 17037), tabelul EN cu
  soare / vedere / lumină (orbirea neevaluată), lumina la birou/masă/fotoliu + sfatul DESK_DARK, raportul cu vederea.
  Revizie etapa 5: runda 1 PARTIAL (ferestrele altor camere „afară”, solul fără obstacole/etaj) și runda 2 PARTIAL (test U
  slab) -> reparate; rezidual acceptat: solul la etaj peste nivelul de jos/terasă (doar indicația „sol”). vitest 1030; build OK.
  CI etapa 4 (0cd6ad808) verde.
NEXT: CI pe PR #371 o dată -> merge e decizia proprietarului.
IDEAS AFTER: vecinii din OpenStreetMap (Overpass, de măsurat acoperirea înălțimilor), orbirea (EN 17037), umbrirea ca variabilă în frontul Pareto, validarea FLN pe cazurile CIE 171, mobilierul așezat după lumină.
KEY FILES: app/core/{solar,site-location,insolation,neighbours,overheating,daylight,light-class,sun-design,sun-effects}.ts, app/components/{SunStudy,SunReport}.tsx,
  app/core/view-out.ts, app/tests/{insolation,sun-design,neighbours,daylight,view-out}.test.ts.
BLOCKERS: none. Art. 3 consolidat verificat în cercetare (legislatie.just.ro, forma din 05.05.2026); valorile AD O și g: de verificat în PDF.
