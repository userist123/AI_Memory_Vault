# todo-claude-casa3d
STATUS: IN_PROGRESS    UPDATED: 2026-10-10T15:50:00Z
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
- 2026-10-10 10:15 UTC: PHASE4.md server verification run on LAB-PC via Remote Control session LAB-PC-toasty-raven
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
PLAN multi-level homes (next, from the global sweep, item 2):
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
NEXT (in order):
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
- 02_PRODUCT/projects/Casa3D.md
- 00_GOVERNANCE/coordination/tasks/todo-claude-casa3d.md
VERIFICATION SO FAR: pytest 20_TESTS/test_vault_state_accuracy.py 20_TESTS/test_lifecycle_schema_parity.py security/tests/test_memory_integrity.py -> 31 passed (2026-10-10, Windows and cloud)
