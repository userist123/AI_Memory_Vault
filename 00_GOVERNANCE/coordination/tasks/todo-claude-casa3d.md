# todo-claude-casa3d
STATUS: IN_PROGRESS    UPDATED: 2026-10-17T09:12:44Z
ROUND 9 (2026-10-17, route route_860d3fba2eb8): PUSHED (fed3cb90..146d421a), reviewed, PC updated.
  Fixes the 3 review findings on round 8. (1) No empty undo steps: core/room-edit.ts planRoomResize (changed/clamped)
  so the width/length inputs skip mutate when the clamp leaves the plan identical (clamp message still shown); onEdit
  'end' discards the history entry when the drag ends with sameState(before, after). (2) Stage bar: one ResizeObserver
  per element via ref-callback (observes the nav and its phase groups), measureBar only on scroll/resize; synthetic
  60-pointermove drag read .stagebar scrollWidth 2 times (was once per render). (3) Edge handle placed by edgeHandleAt:
  middle of the longest edge stretch clear of openings by the hit radius, else the longest gap between openings, else
  the middle; openings win the hit test over the handle (elementsFromPoint). Tooltip: the whole wall line moves (ro/en).
  Evidence: vitest 580/580, tsc 0, tests/room-edit.test.ts (7). Browser 1366: hall handles off the doors, click on a
  door under the handle's hit circle selects the door, clamp (kitchen 400->450 with hall at 60 cm) gives message and a
  single Undo returns to 240 with Undo then disabled; clamped drag and drag-back also leave no entry. Stage bar arrows
  at 1024/768/390, none at 1366/1920. No console errors. Shots r9-*.png in the session scratchpad.
  NEXT: independent review of fed3cb90 (+ round 8 40c59705), then push.
  REVIEW (2 rounds): drag history is now a transaction (core/history.ts begin/rollback with a generation counter; 767d040d):
  rollback removes only its own step, restores redo and the step trimmed at the 100 limit; undo/redo ignored mid-drag;
  Escape or plan unmount closes the transaction (146d421a, browser-checked: drag + Escape + Ctrl+Z). vitest 583/583, tsc 0.
  PUSHED 146d421a, PC updated without admin (server now runs non-elevated), doctor all OK.
  RESIDUAL: an edge fully covered by one door keeps the handle on the door (resize from the panel); WARNING-confirmed
  drags keep one redundant step on Revert (pre-existing).
ROUND 8 (2026-10-16, route route_123af915ca60): COMMITTED LOCALLY 40c59705 on casa3d-ui-round7, NOT pushed, no review yet.
  Neighbour resize: core/room-neighbours.ts (edgeGroup: rooms whose opposite edge is on the moved line within 1 cm and
  overlap > 1 cm, closed transitively along the line; clampEdge: neighbours never below 60 cm, drag clamps instead of
  refusing). resizeRoom(..., { neighbours: true }) moves walls once over the group's whole span (shared wall, its openings,
  perpendicular walls on both sides). Editor width/length inputs + new plan edge handles (PlanView data-k=redge, hit area
  >= 44 px) use it; one undo step; per level. tests/room-neighbours.test.ts (11 cases incl. all templates fuzz).
  Found while verifying: the save chip growing to "Modificari nesalvate" wrapped the topbar (1366: +44 px, 390: +52 px),
  shifting the plan under the pointer mid-drag; status now has a fixed width (172 px, 112 px < 640).
  Stage bar: arrows + fade edges whenever it overflows (measured in Editor, arrows' width discounted so they hide again),
  tighter buttons 1024-1279, focus scrolls the stage into view. Verified 1920/1366 fit, 1024/768/390 scroll.
  New project language: the plumbing already existed (client sends lang); hardened: projectLang() (client lang, else
  Accept-Language, else ro), newProjectRequest() default name per language, click before prefs load uses storedLang().
  Server still refuses a blank name (existing repo test). tests/project-lang.test.ts.
  Evidence: vitest 573/573, tsc 0; shots r8-*.png in the session scratchpad (drags via synthetic PointerEvents with
  setPointerCapture stubbed, not real OS input). No console errors; no horizontal scroll at 390/768.
  NEXT: independent review of 40c59705, then push. FOLLOW-UP: dragging an edge shared along a long line (e.g. demo x=3.2)
  moves every room chained on that line, by design; owner may want a modifier to resize one room only.
ROUND 7 (UI, 2026-10-16): PUSHED to codex/casa3d-memory (head 35196f30), PC updated, doctor all OK.
  DONE 131740ed: stages grouped in phases (Releveu/Concept/Detaliere/Livrare, core/stage-progress.ts) with a done dot
  derived from project data + rule text + Continue button (sidebar and <1024px bottom sheet); command palette Ctrl/Cmd+K +
  Search button (components/CommandPalette.tsx, lib/fuzzy.ts: diacritics-insensitive fuzzy, combobox/listbox, focus trap/return);
  layout-shaped skeletons (components/Skeleton.tsx) replace Loading texts in editor/print/present+share/docs/orders/site/home;
  EmptyState with explanation + primary action for empty plan, rooms, MEP points, orders, revisions, renders, advisor, options,
  filtered catalog, home list. DONE 5df1cab9: print package page navigator (components/PrintNav.tsx, lib/print-nav.ts):
  IntersectionObserver current page, jump links, prev/next, fixed rail >=1600px, sticky collapsible bar below, hidden in print;
  print page no longer scrolls sideways on phones (sheets scroll inside); duplicate React keys in room works fixed.
  Evidence: vitest 551/551 (537 + 14 new), tsc exit 0; before/after shots at 1920/1366/1024/768/390 in the session scratchpad.
  Reduced-motion paths are code-verified only (no emulation run).
  REVIEW FIXES e4505a79: palette keys stop at the dialog and the editor ignores keys from open dialogs / while the palette
  is open (arrows/Delete/R no longer touch the selected piece); Enter runs a command only from the search field or an option
  (Enter on Close just closes); macOS Ctrl+K in text fields left to the system; Present stage counts only non-revoked links;
  Concept done only with real brief input (not the style a package writes); Romanian plural for the result count.
  vitest 557/557, tsc exit 0; browser re-check at 1366 and 390 (after-palette-fix-*.png in the session scratchpad).
  35196f30: editor shortcuts pause only inside modal dialogs (dialog[open]/aria-modal), so Esc/? work again from the
  shortcuts panel and the warnings box. Independent review: round 1 CHANGES_REQUIRED (5 findings, all fixed), round 2
  APPROVE; final vitest 557/557, tsc 0. PC: old elevated server needed the owner's admin -Fix.
  FOLLOW-UPS: at 1024px the stage bar clips 09-10 with no scroll hint; the default demo project has English room names
  in Romanian mode; 'docs' done dot reflects only the level on screen.
  NEXT: room resize adjusts the neighbour rectangle.
ROUND 6 (2026-10-15/16, owner: "totul ca lucrat de profesionisti: programatori, web/UI/UX, 3D, automatizari" + tableta):
  role audits (designer, UI/UX, 3D, QA) in scratchpad; fixed in waves: 792728e3 (QA saves/rate limits/doctor, 3D realism +
  camera rule + Blender openings, designer one-total chain + single fee + autosave + diacritics), 312d073a (UI/UX tokens,
  .btn system, tabs+bottom sheet <1024px, 44px touch, AA contrast, keyboard plan, dialogs, room resize moves walls).
  Independent review of 312d073a: room-resize invalid walls (fixed), approval batch cap (dedupe + separate cap), dialogs on
  route change (fixed). 537/537, tsc clean. PC updated to 312d073a, doctor all OK through tunnel.
  616ef1d4: Blender exposure (median 0.30, highlight cap), interior-photo framing (bedrooms from the foot side), curtain rods
  exported horizontal, merged contact-shadow mesh. PC updated to 616ef1d4, doctor all OK.
  ROUND 6b (3D framing + contact shadows), commit below: walk entry used a stale square aspect (cam.aspect=1 before the
  first resize) so open doors sat at the frame edge; goRoom/renderView now resize first and roomShot takes the walk FOV.
  roomShot: wall candidates every 0.5 m, aims rotated +-12/24 deg, open door leaves/gaps penalised in the tour, doorway
  (sill) start in the tour, hero must be whole, probe() adds foreground-furniture fraction and view depth, halls shot
  along their length (corridor perspective, side doors allowed), duplicate frames removed. Contact shadows follow each
  part's outline (atlas, two blur layers, ovals for round parts). Blender room_camera: same probe (foreground furniture,
  dining chairs margin), hero/group whole, sofa not from the side, second view only for missing pieces (KEY>=1.5),
  must show them and not face windows, mesh-part count capped (bookshelves no longer pull the frame).
  Evidence: before/after shots in the session scratchpad (not in repo); vitest 537/537, tsc clean.
  NEXT UI: F-10/11 stage phases with done dots + Continue, F-13 command palette, F-24 skeletons, F-21 empty states,
  print navigator on /p/<id>/print; verify at 1920/1366/1024/768/390. Then room resize moves the neighbour rectangle.
  Owner decisions pending: repo URL update on the PC, 52 GitHub security alerts on main, accounts/hosting for sale.
ROUND 5 (2026-10-15, owner: "mergi in ordinea recomandata" = business features first): DONE 12fa60d4 + 246d3503:
  supplier markup/discount + profit report, payment schedule, proforma (+generic CSV; not a fiscal invoice), contract &
  offer with fee calculator (template, not legal advice), own products with cost price + CSV import/export. Independent
  review found fee-VAT mismatch, custom items missing from proforma, prototype pollution, CSV injection gaps, silent
  fallback; all fixed with regression tests. 478/478, tsc clean. PC NOT updated: Desktop Commander offline.
  NEXT: update PC when online; then design leftovers (rug position, ceilings/panelling), then accounts/hosting for sale.
ROUND 4 (2026-10-11 late, owner: "nu te opresti pana nu faci ceva real si gata pentru vanzare, ai grija la limitele de utilizare"):
  DONE: 73262cf5 bathroom camera frames the WC bowl (PC render out7 shown to owner); b028ca5d A/B/C options per room
  (core/room-options.ts, snapshot.roomOptions, client choice table room_choices + /api/share/[token]/choices, owner
  /api/projects/[id]/choices, RoomOptionsPanel in finishes/ffe stages, RoomChoiceBox on share link, ZIP export, e2e checked);
  a3dc9b58 lighting layers ambient/task/accent + warnings. 412/412 tests.
  FINDING: sale-readiness gap = single-designer self-hosted model (anonymous owner cookie + one password in .data/auth.json,
  tunnel). Multi-account SaaS (sign-up, per-user data, billing, legal pages) is an authority/security-boundary decision -> asked owner.
  LESSON: never kill by `pgrep -f`/`grep` of a string present in the same shell command (exit 144 twice); use ps -eo comm.
ROUND 3 (2026-10-11 evening, owner: "design mai complex + ștergere proiect + research, apoi execuție"; "baia: toate formele, ce se
  poate înlocui"; "verifică toate piesele"): research in 02_PRODUCT/projects/Casa3D/research-2026-10-11b/ (SYNTHESIS + 4 notes).
  DONE: bath = product-driven (WC mount/cistern/shape per variant, BOQ pays the chosen product, bath panel lists pieces with all
  variants + add buttons); 28 new verified Dedeman bath products (tubs, bidet, walk-in, WCs, towel rads, columns, mirror cabinets,
  boiler) with 3D models + plan symbols + tech points; trash 30 days/restore/purge/duplicate/search; full project ZIP export;
  studio profile (CUI/IBAN checked) on prints + handover; conflict-safe save across devices (draft_rev, 409); advisor rules
  (coffee ½–⅔ sofa, 90 cm behind dining chairs); mood board /p/[id]/board with 60-30-10 palette; core/product-style derives
  drawers/doors/mirrors/glass/colour/round/upholstery/chairs from product names → nightstands, chests, TV units, wardrobes, desks,
  coffee tables, mirrors, shoe cabinets, armchairs now match the ordered piece. Product gallery (233 variants) rendered in a
  Blender studio (scratchpad tools/studio.py + studio.mjs) and reviewed.
  NEXT: A/B design options per room, lighting layers, ceilings/panelling, PC re-render to show owner; gitleaks finding in
  loganalyzer-dfir (other branch) awaits owner decision.
RENDERS (2026-10-11, owner: "randarile nu sunt perfecte… sa fie exact cu piesa"): 20fc5512 + be8554ad. Kitchen sink recessed in a
  cut worktop (ExtrudeGeometry exported as 'mesh' triangles), vanity = ceramic deck with oval cut-out + lathe bowl, fronts from product
  name (sertare/uși), hung at 86 cm; oak veneer doors; office chair one-piece back; room_camera: wide lens for small rooms, aim at key
  items, vertical-crop check, second view <room>-2.png for key items left out. Local check: scratchpad/tools/closeup.py (close-ups).
  NEXT: PC render with new code and show owner; product-by-product builders (KIVIK, BESTÅ, MALM, HEMNES, WC variants, showers).
LATEST (2026-10-11 night): research round 2 plan steps 1-10 ALL committed (last 79ef7819, 388/388 tests): client delivery
  status, priced change orders (decision bound to the seen version), order/decide-by dates, handover pack, trade checks (NKBA/I5/I7),
  tile setting-out, title blocks + sheet list, site survey, time tracking, wardrobe PAL cut list (CSV), GLB export, kitchen modules
  + worktop. Security review applied (c50a05f8). Open design notes: client link shows current-draft orders; all active links share
  the same powers (no client-only role); grout consumption not in BOQ (no grout products in catalog). PC updated after each step.
TASK: Casa3D — rebuild the missing v8 layer on top of faza4, better than before (owner 2026-10-10: "Refacem ce nu este si mai bun decat era")
BRANCH / PR: codex/casa3d-memory (continues claude/casa3d-opinion-52d7cf, pushed 2026-10-10) / userist123/AI_Memory_Vault_CODEX_READY#258    BASE: 21da5bbf2
SPEC: 02_PRODUCT/projects/Casa3D.md (section "Reconciliere F4-F8"), 02_PRODUCT/projects/Casa3D/CORE_IMPLEMENTATION_v8.md, 02_PRODUCT/projects/Casa3D/SOURCE_SNAPSHOT_v8.md
DONE:
- Owner supplied casa3d-faza0/2/4.zip; copied to D:\w\casa3d (C: was 100% full). Defender clean.
- faza0: 13/13. faza4 (cumulative F1-F4): vitest 64/64, tsc + next build pass after local rename viewer3d.js -> viewer3d-engine.js (case clash on NTFS).
- Ledger written: 02_PRODUCT/projects/Casa3D/VERIFICATION_2026-10-10.md.
- Reconciled F4-F8 state in Casa3D.md: phase table row F4 -> "code present in v8 manifest, UNVERIFIED",
  new row F5-F8 UNKNOWN, new section listing the F4/F5 artefacts from the manifest and the evidence
  still missing. Note stays REVIEW/unverified.
- Searched for the v8 source on the owner's machine (C:\Users\user, D:\, git history, names casa3d*): not found.
- 2026-10-10 (cloud session): Casa3D.md updated from the ledger, not from the patch: F4 row -> DONE (TEST MODE),
  new v8 row UNVERIFIED, F4 section 'Implementare verificata', reconciliation renamed to v8 / F5-F8, evidence
  matrix, gaps, roadmap, provenance, relation to ledger 28890ae9. Note stays REVIEW/unverified.
- 2026-10-10 10:15 UTC: PHASE4.md server verification run on LAB-PC via Remote Control session <remote-control-session>
  (health 200, admin 401/200, /go/o 302 with UTM and no cookie, bot clicks not counted, 404 for missing offer).
  Recorded in VERIFICATION_2026-10-10.md; Casa3D.md F4 section updated. v8 archive hash search on the PC was blocked
  by the Claude Code permission classifier (both personal folders and D:\w\casa3d).
PLAN v8 REBUILD (2026-10-10; owner decisions 10:58 UTC: code in Vault workspace 02_PRODUCT/projects/workspaces/casa3d/;
  scope = full v8 layer with tests + L-shaped rooms and wall-room linking + mobile joystick; real AI call still deferred;
  source arrives by owner push from the PC to branch casa3d/faza4-source):
  Objective: the v8 layer exists only as a manifest + hashes; rebuild it on the verified faza4 app so every
  capability in Casa3D.md "Implementare continua" is TEST_VERIFIED, and remove the known faza4 defects.
  Spec sources (authority order): faza4 code + tests > CONSTITUTION.md > Casa3D.md v8 section > CORE_IMPLEMENTATION_v8.md
  > SOURCE_SNAPSHOT_v8.md file list. No invented commercial data; AI never supplies coordinates.
  Scope (increment order, each with Vitest red->green, tsc, next build):
    W0 bring faza4 source into the Vault workspace 02_PRODUCT/projects/workspaces/casa3d/ (git subtree from branch
       casa3d/faza4-source pushed by the owner); rename components/viewer3d.js -> viewer3d-engine.js (NTFS/APFS clash);
       add .github/workflows/casa3d-build.yml (path-filtered: npm ci, typecheck, vitest, build) like loganalyzer-dfir.
    W1 core/digital-twin.ts: canonical Digital Twin v1.0 (rooms, walls, openings, placements, units = m), fingerprint
       (SHA-256 of canonical JSON), revalidation on persist; core/geometry-engine.ts: deterministic placement +
       validation (fits, doors/windows clearance, circulation) over the existing core/layout.js + core/validate.ts.
    W2 core/view-state.ts: renderer-neutral ViewerState shared by PlanView (2D) and Viewer3D; tests.
    W3 core/design-dsl.ts: Design DSL 1.1 (ADD/REMOVE/REPLACE/MOVE; constraints near, againstWall, alignedWith,
       keepClear, orientation), schema validator, no coordinates accepted; core/design-search.ts: deterministic
       semantic Solver (DSL -> candidate geometry via Geometry Engine), up to 3 alternatives + comparator (measures,
       issues, no auto-winner).
    W4 core/design-approval.ts + app/api/projects/[id]/design/*: proposal with base fingerprint, non-persistent
       preview, stale rejection, Accept = revalidate current twin -> apply -> revision; ERROR blocks, WARNING confirm.
    W5 core/boq-search.ts: BOQ-aware evaluation of each alternative through the existing core/boq.ts
       (quantities, totals, UNKNOWN, provenance, delta vs target budget).
    W6 share: app/api/projects/[id]/shares, app/api/share/[token], app/share/[token]/page.tsx: read-only share of
       one revision by unguessable token, no owner cookie needed; core/catalog-feed.ts + data/supplier-offer-feed.example.json
       + app/api/catalog/import: supplier feed import with provenance (F5 start).
    W7 lib/ai-design.ts: AI adapter that only emits DSL (rules-engine fallback without ANTHROPIC_API_KEY); real model
       call stays DEFERRED by owner; scripts/verify-f4.mjs / verify-f5.mjs as runnable gates; PHASE5-8 docs rewritten
       from what is actually built.
  "Better than before": full Vitest coverage for every new module (not isolated smoke tests), CI on every push,
  portability fix, PHASE4.md 404/401 wording corrected, ledger updated with real outputs.
    W8 L-shaped (rectilinear polygon) rooms in the Digital Twin + Geometry Engine, with automatic wall-room linking; tests.
    W9 mobile joystick / touch navigation for Viewer3D (UI only); manual check recorded.
  Out of scope unless owner asks: real affiliate programs, payments, real AI call (deferred by owner).
  Verification per increment: vitest + tsc + next build in the cloud container (Node 22, npm registry reachable);
  the owner's PC run stays the RUNTIME_VERIFIED reference for server checks.
- 2026-10-10 11:50 UTC: owner said "fa-le tu, nu mai astepta". The PC session refuses to push faza4 without the owner's
  own confirmation (correct), so the v8 layer is being built as a framework-free package first:
  02_PRODUCT/projects/workspaces/casa3d/packages/twin-core (TypeScript 5.9, Vitest 3.2, Node 22).
  W1 DONE: src/geometry.ts (rectilinear polygons incl. L-shapes, rect-in-polygon, overlaps, wall bands),
  src/twin.ts (Digital Twin v1.0 model, normalize, SHA-256 fingerprint, wallsFromRooms, linkWalls = automatic
  wall-room linking with shared walls), src/engine.ts (validate: 16 issue codes, ERROR/WARNING; findPosition:
  deterministic 5 cm grid search with againstWall/near/keepClear/alignedWith). 24 tests green, tsc clean.
  W8 (L-shaped rooms + wall-room linking) is therefore built into the model from the start.
  CI: casa3d-build.yml has a twin-core job (ubuntu + windows) and an app job that waits for app/package.json.
- 2026-10-10 11:55 UTC: W3 DONE (catalog contract with UNKNOWN, dsl.ts validator that rejects coordinates and
  invented ids, solver.ts with solve/measure/solveAlternatives, no auto-winner). W4 DONE (approval.ts: preview never
  persists, accept revalidates the current twin, STALE/FAILED/ERROR/NEEDS_CONFIRMATION/ALREADY_DECIDED, revisions).
  W2 DONE (view-state.ts: one ViewerState for plan and 3d, reducer, renderItems with preview overlay) with W9 built in
  (joystick action: deterministic camera-frame walk, clamped). W5 DONE (boq-eval.ts: RON-only sums, UNKNOWN counted,
  budget verdict UNKNOWN whenever a price is unknown, room quantities, provenance kept). 45 tests green, tsc clean.
- 2026-10-10 12:05 UTC: W6 DONE (share.ts, catalog-feed.ts), W7 DONE (designer.ts: DesignProvider contract + rules
  engine; model provider deliberately not wired), e2e test on an L-shaped room, README. CI made green: job-level
  hashFiles() removed from casa3d-build.yml, Vitest 5 (tinypool critical), node_modules skipped by the vault scan.
  53 tests, tsc clean, npm audit 0. Casa3D.md and the verification ledger record the rebuilt layer as TEST_VERIFIED.
  PR #258 (opened by the owner from codex/casa3d-memory) is subscribed and driven from the cloud session.
- 2026-10-10 12:35 UTC: owner published faza4 at https://github.com/userist123/casa3d (main @ 44d194f, 81 files,
  viewer3d-engine.js rename included). W0 DONE: imported with git subtree into
  02_PRODUCT/projects/workspaces/casa3d/app (commit 747fec0f); in the container: npm ci, tsc clean, Vitest 64/64,
  next build OK (22 routes). App docs (CONSTITUTION, README, PHASE1-4) allowlisted as plain documents.
2026-10-10 14:45 UTC — owner: benchmark against every app of this kind worldwide, make it international, visual export,
  realistic look, customise colours/materials/sizes of everything, an advisor that says what is not good; requirements
  rewritten in 02_PRODUCT/projects/Casa3D/CERINTE_PRODUS_v2.md, analysis + status in COMPETITOR_ANALYSIS_2026-10-10.md.
  DONE and pushed (head 81c8b362): mobile/brief/variant/share fixes, format.ts, 3D light + capture + SSAO + skirting,
  revision diff, catalog filters, visual export (11 A4 pages, 6 images), appearance/customisation, advisor, editor tools
  (duplicate, nudge, measure, shortcuts, plan underlay). Independent review REQUEST CHANGES (made-to-measure priced in
  diff/export; validation gaps) acted on in 41841767. Evidence: app 171/171, tsc 0, next build OK, browser checks.
  IN PROGRESS: i18n worker (branch casa3d-i18n), templates worker (branch casa3d-templates, uncommitted at 14:45).
2026-10-10 15:50 UTC — pushed e79e28e4: 360° room panoramas (5c1f410b), DXF R12 export merged from casa3d-dxf
  (ezdxf audit 0 errors, owner-scoped route, 404 without cookie), clean-checkout fix (PGlite ENOENT on missing ./.data),
  DXF room labels fit narrow rooms. Evidence: app tsc 0, vitest 215/215, next build OK. Independent review of
  2b9f4af9..e79e28e4 running.
2026-10-10 16:55 UTC — review of the panorama/DXF batch (CHANGES_REQUESTED) acted on in 3cbe6af4 (modal keys: 9 keys
  leaked to window listeners before, 0 after, browser-verified; heading 90° fixed with a unit test against three's UVs;
  exception-safe renderPanorama; WebGL context freed; %% runs in DXF text). Multi-level homes DONE and pushed (head
  adf9a7cc): core b03a12be, stair geometry d6c65058, editor 0b98ba71, DXF (worker, e699e2d8) and 3D (worker, 45ea8e44)
  merged, 5aca8b00 (stairwells in 3D, door-blocking stairs), adf9a7cc (print/share per level). Evidence: tsc 0, vitest
  242/242, next build OK, browser: level tabs, stair in the hall, stairwell with railing, revision accepted, print 2 plans
  + 12 room pages + 12 images, share page per level, DXF two levels (ezdxf 0 errors). Independent review of
  3cbe6af4..adf9a7cc running. Next from the sweep: comments on the share link, market separate from language
  (catalog/currency/VAT), own 3D models, AR, L/U stairs and roofs.
2026-10-10 19:30 UTC — owner: "ies multe din peisaj", "librarie mica de produse", "instaleaza programe pe pc".
  Done: house-view clipping plane (aa43fc84), laptop layout verified at 4 sizes on the PC (Playwright), Blender 4.5
  photoreal pipeline with Poly Haven textures/HDRI (7e110f7c), door leaves + softer plaster (c8f49022), catalog 68 -> 212
  variants from IKEA/Dedeman/JYSK/Mobexpert verified on product pages 2026-10-10 (2ee6922e; evidence file
  data/catalog-evidence.2026-10-10.json), incremental DB seeding, nightstand real-size fix, smarter rules retry.
  Evidence: tsc 0, vitest 245/245, next build OK, browser: 212 catalog results, doors visible in 3D.
  Open: independent review of multi-level + catalog batch (reviewer agent failed earlier on org spend limit);
  Dedeman prices where two unlabeled prices were shown are marked confidence MEDIUM; eMAG blocked (HTTP 511).
2026-10-10 22:10 UTC — owner: "verifica si afiseaza tot ce se poate schimba/adauga intr-o casa ca finisaj de designer ... aplicatie
  profesionista", then "research complex cu cel mai competent agent", then approved P0 ("da"). Research (Fable agent) saved as
  02_PRODUCT/projects/Casa3D/FINISHES_RESEARCH_2026-10-10.md (REVIEW/unverified). P0 DONE: 0e75f3d1 data model + BOQ (patterns, waste,
  wallpaper rolls, slats, plaster, brick, stone, plasterboard, cove LED, cornice, spots; 13 new Dedeman products re-read on their pages;
  8 labour rates LOW), 4daf123f finishes panel, 0b7491ba 3D + Blender (procedural floors at real size, wall overlays cut around
  openings, bath tiles in 3D, drop/cove ceilings), 80b62a0d finish schedule (CSV + print). Evidence: tsc 0, vitest 263/263, next build
  OK, browser screenshots (panel, living walk with herringbone/brick/stone/spots, print schedule, CSV 26 lines). Independent review of
  3b41ec99..80b62a0d done (vault-reviewer, PARTIAL): 9 findings, fixed in d175aaf9 with 7 regression tests red on the old code
  (bath tiles double count, duplicate wall sides, cove LED none, drop-ceiling paint, CSV formula injection, sanitize 500s, slat
  count, open sides, texture disposal, Blender temp file). 6d8670de marble veins. Evidence: vitest 269/269, tsc 0, build OK; PC app
  updated and restarted on 3123 (DB got 29 materials/14 labour rates); Blender renders out3/out4 on the PC. Next: P1 from the report (lighting model CCT/CRI/lux, kitchen and bathroom as systems,
  door/window styles, textiles), Blender render of a finished room on the PC.
2026-10-10 23:05 UTC — owner "continua" on P2: 6d47a274 lighting estimate (lumen method, lux targets, CCT checks; VIRRMO/KABOMBA
  specs read on IKEA), a7f91339 curtains/sheers/blinds/rugs (17 IKEA products read on pages, rod/fullness/panel rules, textiles
  budget category, 3D), d7169ed0 interior doors + handles (5 Dedeman doors, 2 handles; warnings; 3D leaf styles). Evidence: vitest
  283/283, tsc 0, build OK, browser screenshots; PC app rebuilt at d7169ed0. Open: kitchen as a system (fronts, countertop, handles,
  tap finish) — no verified countertop product yet; independent review of 6d47a274..d7169ed0.
2026-10-11 01:40 UTC — owner "Trebuie mai complex de atat" (7-part plan), all 7 parts pushed:
  cd054301 kitchen system; bathroom system (metal finish, walk-in/cabin, wall-hung WC, tile zone 1.2/2.1/full);
  placed fixtures (core/fixtures.ts: pendants over table/counter/bed, sconces, track; plan symbols, 3D, BOQ, lux);
  style packages (core/styles.ts + StylePanel: Scandi/Modern/Japandi/Industrial/Classic, cost per style on this house);
  94 products read on live Dedeman/IKEA pages 2026-10-11 (evidence: app/data/materials-evidence.2026-10-11.json;
  hex colours approximate, paint coverage from product sheet, Oskar coverage unpublished -> listed unpriced);
  e8c70026 drawings in the print pack (core/drawings.ts + components/Drawings.tsx: ceiling/lighting plan, floor finishes
  plan, wall elevations 1:50); cfd0b505 fixes from independent Opus review (bed wall from rotation, schedule = BOQ total,
  fixtures under dropped ceiling, counter pendant warning, bed pendants <=2, owned 3D materials), regressions red on old code.
  Evidence: vitest 311/311, tsc 0, next build OK, browser screenshots of each feature. PC update started (update.ps1).
  Open: walk-in/wall-hung keep the cabin/WC catalog price (possible double count of tray); styleCosts ~6 ms per house change;
  Blender export does not yet carry fixtures; no gunmetal shower set in the catalog.
PLAN multi-level homes (done; kept for reference):
  Model: Snapshot.floor stays the ground level (saved projects and the API unchanged); new Snapshot.levels?: Floor[]
  for the levels above; Floor.stairs?: Stair {id, x, z, width, length, rotation} on the lower level, leading to the
  next level, which shows the stairwell void. Elevation is derived (ceiling heights + 0.2 m slab), never stored.
  core/levels.ts: floors(snap), levelView(snap, i) (floor = level i, placements/tech filtered to its rooms),
  mergeLevel(full, i, view), addLevel (copies the exterior shell with new ids), removeLevel, elevationOf, levelOfRoom.
  Room/wall ids unique across levels (checked on save). House-wide totals (BOQ, room schedule, diff, print, advisor,
  validation on save) iterate floors(); editor/3D/plan/tech/DXF work on levelView, so per-level code is unchanged.
  Editor: level tabs (ground, level 1, +), stair tool; 3D: active level + stairs + stairwell void; DXF/print: one
  plan per level. Twin/design proposals: ground level only, upper-level placements preserved on apply (test).
  Tests first for levels.ts, repo validation, BOQ over two levels, design apply preserving upper placements.
- 2026-10-11 redesign (owner: "sa nu mai arate ca una facuta de AI"; chose Studio editorial + project-stage flow):
  editorial tokens/fonts, 8-stage bar (Releveu..Prezentare), MaterialPicker swatches, architectural furniture
  symbols in plan (core/plan-symbols.ts), client presentation deck (/p/[id]/present), studio dashboard.
- 2026-10-11 Blender renders from the app (lib/renders.ts, /api/projects/[id]/renders): verified on LAB-PC,
  5 rooms ~2 min (job 1791718918059). Decor glTF exported as references.
- 2026-10-11 realism pass (owner: "fa totul mai realist ... usile ... nu din desen animat"): door leaves on pivots
  with hinges, architraves, handles, eased open/close + click toggle, doorSwing rule (core/doors.ts); real windows;
  Poly Haven CC0 textures + HDRI; dressed beds/sofas (d6742332); lived-in kitchen: oven door, hob zones, gooseneck
  tap, counter items, spheres in Blender export (d2c969e0). vitest 318/318, next build OK.
- 2026-10-11 client link (owner: "exportul din aplicatie ... link, ii dau link-ul se uita imi spune daca e bine"):
  Presentation stage -> "Link pentru client" (lib/share.ts clientLink: revision only if draft changed, reuses active link);
  /share/<token> = client deck + FeedbackBox per room + general (lib/feedback.ts, table share_feedback, cap 200/link in a
  locked tx, 16 KB real body limit); designer inbox ClientPanel (resolve/reopen). Public gate middleware.ts +
  lib/public-gate.ts: via tunnel only /share/*, /api/share/*, static assets. vault-reviewer findings 1-7 fixed with
  red-first tests (d78c40ab, 65df12f8). vitest 327/327. CI fixes: render stub as Node script (Windows), LICENSE.txt.
  PC: D:\casa3d-render\public-link.ps1 (cloudflared quick tunnel, sets CASA3D_PUBLIC_URL) and public-link-off.ps1
  written but NOT run: exposing the PC publicly waits for the owner's OK.
- 2026-10-11 phone access (owner: "Cum fac sa am acces pe telefon la site?" -> "Om fa asta"): public link STARTED on
  LAB-PC (cloudflared quick tunnel, URL in D:\casa3d-render\public-url.txt, changes per start); verified from
  outside: /share 200, editor/API/admin 404. Then d2843c9f: password login (lib/auth.ts scrypt + HMAC sessions,
  .data/auth.json), gate() in middleware (Node runtime), /login, local-only password card on the dashboard, phone
  layout. vitest 332/332, phone e2e (iPhone 13 emulation via cf-connecting-ip) green. Owner must set the password
  himself on http://localhost:3123 (card "Acces de pe telefon"). vault-reviewer review of d2843c9f pending.
NEXT (in order):
0. Owner OK -> run public-link.ps1 on the PC, verify the public URL from outside (only /share works). Realism: hood chimney; verify on PC at 1440/1280.
0. DONE 12:55 UTC (integration): lib/twin.ts adapter, lib/design.ts (room-scoped BOQ, stale over the full twin),
   lib/share.ts, tables design_proposals + shares, routes /design, /design/[pid], /shares, /api/share/[token],
   page /share/[token], editor tab Twin + share buttons, joystick (engine setMove), twin-core overlap policy and
   door side margin, next.config extensionAlias. Evidence: app tsc 0, vitest 71/71, next build OK, HTTP smoke on
   next start (apply -> rev 1, stale 409, share 200 no-store/noindex, bad/revoked 404); twin-core 55/55.
   Work routed with the cost-router skill: Opus reviewer in its own context (pending), Sonnet for PHASE5 and the
   state-card refresh.
1. DONE 13:05 UTC: independent review (REQUEST CHANGES) acted on: unique ids, atomic apply (claim + CAS), project-wide
   error check before the draft write, legacy warnings gated, room-size cap + adaptive grid, untwinnable pieces kept,
   share name/price, action whitelist, joystick reset. Evidence: twin-core 58/58, app tsc 0, vitest 81/81, next build OK.
   Second review (REQUEST CHANGES, post-CAS non-atomic, reproduced): apply is now one transaction (claim, CAS with
   revision number, decision, revision written from `next`); createRevision uses the same rule; apply issue refs mapped.
   Evidence: app 84/84, rollback test red on the old code. pg branch also run on local Postgres 16 (design-review 12/12, design-twin 4/4).
   Open: twin-core does not model piece facing (covered by the app validator gate). NEXT: confirm CI on the pushed head;
   owner checks the UI on the PC.
1. Owner supplies the v8 archive (or its path), or allows the PowerShell hash scan on LAB-PC; compare SHA-256 with 7bb34bf3...9628bc.
2. npm ci, full Vitest, next build; record output in CORE_IMPLEMENTATION_v8.md.
3. node scripts/verify-f4.mjs and verify-f5.mjs; record output.
4. DEFERRED by owner on 2026-10-10 ("continua fara AI deocamdata"): real model call Brief -> DSL -> Solver. Not a blocker for 1-3 and 5.
5. Only then move F4 to DONE in the phase table.
BLOCKERS / OWNER QUESTIONS:
- 2026-10-10 cloud session: no Casa3D archive is in the Vault repo or its git history; Desktop Commander device LAB-PC
  is offline, so D:\w\casa3d (faza0/2/4 extracted, npm installed) is unreachable from here. To unblock steps 1-3 and the
  PHASE4.md server check: either bring LAB-PC online (Desktop Commander app) or upload casa3d-faza4.zip / the v8
  archive into the session. Pushed state: codex/casa3d-memory @ e628911c.
- v8 source archive not on this machine (manifest path /mnt/data/casa3d-v8/app was the Claude.ai sandbox). Where is it saved locally?
- ANTHROPIC_API_KEY not set in the environment.
KEY FILES:
- 02_PRODUCT/projects/workspaces/casa3d/app/core/{bath,fixtures,styles,drawings}.ts, components/{StylePanel,Drawings}.tsx
- 02_PRODUCT/projects/Casa3D.md
- 00_GOVERNANCE/coordination/tasks/todo-claude-casa3d.md
VERIFICATION SO FAR: pytest 20_TESTS/test_vault_state_accuracy.py 20_TESTS/test_lifecycle_schema_parity.py security/tests/test_memory_integrity.py -> 31 passed (2026-10-10, Windows and cloud)
