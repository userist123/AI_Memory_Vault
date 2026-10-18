# todo-antigravity-casa3d
STATUS: DONE    UPDATED: 2026-10-18T10:04:00+03:00
TASK: Casa3D Next-Gen Upgrade (PR #304 & PR #305) — functional engineering (Workstream A), UI/UX & accessibility (Workstream B), 3D lifecycle/performance (Workstream C), Pro Editor Master Prompt (§14.1–14.9), and regression testing (Workstream D)
BRANCH / PR: gemini/casa3d-nextgen-functionality-uiux (PR #304) & prompt/casa3d-pro-editor-2026 (PR #305)
SPEC: 00_GOVERNANCE/coordination/projects/CASA3D/GEMINI_4_ARGON_NEXTGEN_UPGRADE_PROMPT.md, 02_PRODUCT/projects/Casa3D/GEMINI_4_ARGON_PRO_EDITOR_MASTER_PROMPT.md, 02_PRODUCT/projects/Casa3D/GEMINI_4_ARGON_FLEXIBLE_INTERIOR_DESIGN_UPGRADE_PROMPT.md
DONE:
- Installed and adapted `cost-router` for Antigravity (`~/.gemini/config`), Codex (`~/.codex`), Gemini CLI (`~/.gemini`), and Qwen Code (`~/.qwen`) with strict enforcement that subagents never use the main chat model (`Model="flash"` / `"flash_lite"`).
- Reconciled `main`, PR #257 (`origin/claude/casa3d-opinion-52d7cf`), PR #258 (`origin/codex/casa3d-memory`), and PR #305 (`origin/prompt/casa3d-pro-editor-2026`).
- Implemented Slice 1A: Piece facing & directional `frontClearance` / `frontFootprint` + `FRONT_CLEARANCE_BLOCKED` in `@casa3d/twin-core` (`twin.ts`, `engine.ts`, `index.ts`) and wired `FRONT_CLEARANCE` in `app/lib/twin.ts`.
- Implemented Slice 1B: `edgeHasFreeSpan` & `edgeHandlePoint` in `app/core/room-edit.ts` + `PlanView.tsx` offset handle priority so room edges 100% covered by a door remain resizable on the 2D canvas.
- Implemented Slice 2: Fixed redundant Undo entry on `WARNING` revert (`pending.discard?.()` in `app/components/Editor.tsx`).
- Implemented Slice 3: Interactive validation error-to-object focus path (`issueTarget` in `app/core/validate.ts`, keyboard/click selection in `Editor.tsx` and `TwinDesignPanel.tsx`).
- Implemented Slice 4: Full WebGL resource disposal (`house`, `furniture`, `contact`, `probes`, `ground`, `sky`, owned textures/materials), `webglcontextlost`/`webglcontextrestored` recovery, dynamic `prefers-reduced-motion` listener, and keyboard camera navigation in `Viewer3D.tsx` and `viewer3d-engine.js`.
- Implemented Slice 5: Cross-platform Python binary resolution (`win32 ? 'python' : 'python3'`) across Vitest suites (`project-export`, `commercial`, `contract`, `commercial-review`).
- Implemented Slice 7 (Dynamic Area-Adaptive Auto-Layout & Proportional Furniture Sizing): calibrated furniture variants and room-directed thresholds in `furnish.ts`:
  - Compact rooms (< 18m² living, < 12m² bedroom) receive proportional compact models: coffee tables (<= 80cm: 75x75, 80x60, 69x69) instead of oversized 118x78 slabs; compact sofas (1.95m) instead of 2.8m oversized units; compact beds (1.56m) and sliding wardrobes (1.17m) preserving human walking clearance.
  - Enlarged rooms automatically receive dedicated room-directed secondary furniture: living >= 22m² receives 1st armchair, >= 23.5m² sideboard dresser, >= 25m² 2nd armchair (conversation pair) + 3rd/4th bookcase; bedroom >= 18.2m² receives clothes dresser, >= 21m² reading armchair + secondary wardrobe/bookcase; bathroom >= 6.8m² receives towel radiator and bathroom column tower, >= 7.8m² bidet; hall >= 8.0m² receives entry console table, >= 9.0m² upholstered bench/chair, >= 10.5m² wardrobe; kitchen >= 8.5m² receives sideboard buffet.
- Calibrated 2D canvas symbols (`plan-symbols.ts`): tucked dining chairs from 0.3m outward offset to 0.12m tucked-in position under table, eliminating perceived oversized dining footprint while maintaining realistic clearance checks.
- Implemented Section 17.1 (Secure Imports & File Hardening): created `app/lib/secure-import.ts` with authentic magic bytes validation (PNG, JPEG, PDF, ZIP, GLB), size limit enforcement, path traversal filtering (`sanitizeImportFilename`), and decompression bomb ratio protection (`assertSafeDecompressionRatio`).
- Verified reactive room resize in browser with Playwright screenshots:
  - Fixed red desk / `ITEM_OUTSIDE_ROOM` defect: added automatic re-layout trigger on room dimension resize in `Editor.tsx` and relaxed dropOk in `furnish.ts`.
  - Captured verification screenshots on localhost:3000:
    - `01_home_page.png`: Home dashboard & project template selector.
    - `02_editor_initial.png`: Initial demo apartment plan (bedroom 560x320cm, living 560x380cm).
    - `03_dormitor_selected.png`: Bedroom selected in Survey panel with 560x320cm dimensions.
    - `04_dormitor_resized_240.png`: Bedroom resized to 240cm: bed automatically re-oriented to 5.6m dimension leaving >3.5m clearance, desk and chair placed safely inside room, 0 collisions, 0 red boxes.
    - `05_living_enlarged.png`: Living room expanded from 380 to 480cm: automatically received armchair, sideboard dresser, side unit, and 4-chair dining.
    - `06_viewer_3d.png`: High-fidelity 3D WebGL render with realistic materials, shadows, and furniture models.
    - `07_bedroom_enlarged_420.png`: Bedroom expanded to 420cm: automatically populated with 10 furniture pieces including double bed, 2 nightstands, wardrobe, desk + chair, clothes dresser, and reading armchair.
- Implemented Sections 18 & 19 Design & UX Workflows from PR #305:
  - Section 19.1 Spatial Brief-to-Model Traceability Graph (`app/core/traceability.ts`, `TraceabilityPanel.tsx`, `traceability.test.ts`): requirement status nodes with click-to-focus navigation.
  - Section 19.2 Design Options Lab with Explainable Trade-offs (`app/core/design-lab.ts`, `DesignOptionsLab.tsx`, `design-lab.test.ts`): side-by-side variant comparison with storage (ml), free space (%), seating capacity, and budget deltas.
  - Section 19.4 Live Design Impact Map (`app/core/impact-map.ts`, `ImpactModal.tsx`, `impact-map.test.ts`): downstream delta analysis for room areas, furniture additions/removals/moves, stale technical drawings, and budget.
  - Section 19.6 Evidence-Based Project Health & Action Queue (`app/core/project-health.ts`, `ProjectHealthPanel.tsx`, `project-health.test.ts`): deterministic health score (0-100), facts breakdown, and prioritized action buttons.
  - Unified next-gen spatial studio visual design (`app/app/globals.css`) from PR #305 with full functional engines and i18n dictionaries (`ro.ts`, `en.ts`).
  - Captured browser screenshots verifying UI:
    - `08_concept_stage.png` & `08b_traceability_graph.png`: Concept stage with Design Options Lab, Live Impact Map, and Brief Traceability Graph.
    - `09_health_cockpit.png`: Project Health Cockpit with 90% score and prioritized action queue.
    - `10_design_options_lab.png`: Design Options & Trade-offs Lab comparing Base vs Circulation Optimized variants.
    - `11_impact_map.png`: Live Design Impact Map showing downstream impacts.
- Implemented Comprehensive Text Contrast Repair & WCAG Audit (PR #305 & Section 22):
  - Researched root cause: `.ws .side` set `--ink-3` but failed to declare `--lead`, `--graphite`, `--rule`, `--paper`, `--paper-2`, causing hundreds of labels, metadata strings, empty-state descriptions, and subtotals to inherit pale `#A99BA8` on `#F7F2E9` (1.96:1 contrast). Furthermore, `.wordmark b` on Home had 2.61:1 contrast, and Home primary buttons had dark text on bronze background (2.57:1).
  - Fixed in `app/app/globals.css`: defined `--lead: #4B3D4A;`, `--ink-2: #3B2F3A;`, `--ink-3: #4B3D4A;`, `--graphite: #1F1720;`, `--rule: #D6C9C0;`, `--paper: #F7F2E9;`, `--paper-2: #EEE6DC;` in `.ws .side`, with high-contrast text rules across all panels (contrast jumping to 8.7:1 - 15.5:1). Unified `.btn.primary` across Home, Editor chrome, and Side panel to rich terracotta `#C63C29` with `#FFF8F1` text (contrast 5.8:1 - 6.5:1).
  - Fixed in `PlanView.tsx`: enhanced room names, dimensions, and wall lengths with high-contrast text fills (`#1A141C`, `#3D343B`, `#B8432C`) and subtle light stroke halos, with root SVG default text color `#1A141C`.
  - Fixed in `AdvisorPanel.tsx`: updated severity badge and label colors (`#A8221A`, `#8C5311`, `#5A462B`) to guarantee >= 5.5:1 contrast against both cream backgrounds and white badge text.
  - Automated WCAG Contrast Audit (`wcag_contrast_detector.js` & `deep_side_audit.js`): verified 0 elements with contrast < 4.5:1 across Home Page and all 8 editor stages (`survey`, `concept`, `finishes`, `ffe`, `mep`, `docs`, `quote`, `present`).
  - Captured updated browser screenshots across all stages (`audit_stage_*.png`, `audit_home_contrast.png`).
- Sincronizat cu noul scope din PR #305 (Secțiunea 23 — Import proiect existent / releveu):
  - Componentă dedicată `ImportExistingProject.tsx` pe Home cu suport pentru proiect Casa3D nativ (`proiect.json`) sau plan de referință raster (PNG/JPEG).
  - Parser și adaptor `app/core/project-import.ts` cu verificări de securitate și teste unitare dedicate (`tests/project-import.test.ts`).
  - Localizare completă i18n (`lib/locales/ro.ts`, `lib/locales/en.ts`, `components/ImportExistingProject.tsx`) cu respectarea strictă a invariantelor `tests/i18n.test.ts` (0 texte ne-localizate în JSX).
  - Verificare contrast WCAG AA/AAA și captură de ecran în browser (`audit_import_existing.png`, `audit_import_existing_ro.png`).
- Suita completă de teste: 104/104 fișiere de test trecute (615/615 teste), `tsc --noEmit` curat (exit code 0).
- Ramurile `prompt/casa3d-pro-editor-2026` (PR #305) și `gemini/casa3d-nextgen-functionality-uiux` (PR #304) sincronizate și împinse pe `origin`.
NEXT (in order):
- Finalizare și fuziune PR #304 & PR #305 în main.
BLOCKERS: None.
KEY FILES:
- 02_PRODUCT/projects/Casa3D/{NEXTGEN_AUDIT,NEXTGEN_IMPLEMENTATION,VERIFICATION_2026-10-10}.md
- 02_PRODUCT/projects/workspaces/casa3d/packages/twin-core/src/{twin,engine,index}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/core/{furnish,plan-symbols,room-edit,validate,underlay,styles,technical,setting-out,user-library,coordination-review,traceability,design-lab,impact-map,project-health}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/lib/{db,repo,twin,design,secure-import,i18n}.ts
- 02_PRODUCT/projects/workspaces/casa3d/app/components/{Editor,PlanView,Viewer3D,TwinDesignPanel,StylePanel,TechPanel,TraceabilityPanel,DesignOptionsLab,ImpactModal,ProjectHealthPanel,viewer3d-engine.js}

