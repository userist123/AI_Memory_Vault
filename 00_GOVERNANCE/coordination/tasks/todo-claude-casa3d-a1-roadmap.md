# todo-claude-casa3d-a1-roadmap
STATUS: IN_PROGRESS        UPDATED: 2026-11-01
TASK: Casa3D plan A1 (research docs/research/UI_UX_MOTION_APLICATII_COMPLEXE_2026.md §A1, in draft PR #318), the rest
  after A1.1 (#323 merged c2a04d4d8). OWNER DECISION 2026-10-23: "Toate pe rand" = all phases below, in order,
  one phase per session (CLAUDE.md), each its own branch/worktree/PR, owner rule: no red at merge.
PHASES (in order; mark each DONE with PR + merge commit):
1. DONE 2026-10-24: PR #341 merged d8337090f (2 review rounds, CI 44/44 green). A1.2 completion of rows 1-3 — branch claude/casa3d-a1-2-completion, worktree D:/w/a12 (base main dd6c9ba34).
   a. Editable dimensions after drawing: click a wall's dimension in the plan -> type length (same parser
      core/typed-dimension.ts parseTypedLength, integer=cm, decimal=m) -> wall resized keeping its start point and
      connected walls following (check how core/geometry joins walls first). One undo step.
   b. Joined-wall preview while drawing (show how the new wall will join existing ones: corner cleanup in preview).
   c. Context menu at cursor (right-click in plan) built from lib/commands.ts registry (scope = selection), shortcut
      shown per item, keyboard reachable (Shift+F10 / ContextMenu key), RO+EN.
   d. Residual bugs (pre-existing): Esc with a selection while drawing deselects first (should cancel drawing first);
      Delete/Backspace while typing a length reaches del(); typedEndpoint in core/typed-dimension.ts unused in prod
      (wire it or remove it + tests).
2. DONE 2026-10-24: PR #349 (2 review rounds, CI 40/40 green; owner: new layout ON + classic pref, catalog stays
   right, phone Structure/Panel switch; checkpoint todo-claude-casa3d-a1-3.md). A1.3 workspace layout (row 4): outliner left (levels -> rooms -> walls/openings/furniture, toggles with catalog),
   contextual inspector right, docked resizable panels with persisted sizes, Focus toggle; behind a feature flag with
   "classic layout" preference; inspector field-order snapshot test.
3. DONE 2026-11-01: PR #369, branch claude/casa3d-a1-viewport (2 review rounds), checkpoint todo-claude-casa3d-a1-viewport.md, RESEARCH_A1_VIEWPORT.md,
   PLAN_A1_VIEWPORT.md. Viewport 3D (row 5): render on demand (idle 0), tiers phone/laptop/desktop, static furniture batching.
   Research changed the plan: ACES kept (Neutral yellows the paint), no pre-baked light (editable plans), path tracing skipped
   (three-gpu-pathtracer requires WebGPU since 0.0.25). Measured on a real S9 (Mali-G72); integrated-GPU laptops: UNVERIFIED.
4. Drawing sheet ISO 7200 (row 6): title block as object, ISO 5457 formats, viewports from saved views at declared
   scale, revision/status fields, vector PDF + DXF with named layers (walls, openings, dimensions, MEP, annotations).
   ISO 7200 mandatory-field list is UNVERIFIED until checked against the purchased standard: phase 4 needs the owner's purchased
   ISO 7200 text for its mandatory-field list; NOT started.
NOT HERE: row 8 accessibility = other session (todo-claude-casa3d-a11y-p3.md, PR #331); rows 7 and 9 after phase 4,
  ask owner then.
PER PHASE: read PLAN_A1.md style -> write PLAN_A1_<n>.md (inventory on main, decisions, slices test-first) -> owner
  questions only for real product choices -> implement (vault-worker for bounded slices, main session for design) ->
  tsc, vitest --maxWorkers=2, browser on real app (npm run dev, launch config casa3d-a1 port 3200; screenshots,
  RO+EN, text fit, contrast) -> vault-reviewer Opus on the diff (max 2 rounds) -> PR -> CI once -> merge.
ENV NOTES: npm ci needed in each new worktree (app/); vitest default workers OOM with dev server -> --maxWorkers=2;
  local full pytest has env-only failures from node_modules/.next (strict_yaml etc.), CI is the reference.
NEXT: phase 4 only once the purchased ISO 7200 text is available: new branch + worktree from main (npm ci in app/).
  Notes from A1.2/A1.3: every new plan selection kind needs a context menu entry (lib/commands.ts CONTEXT) and an
  `inspector:<kind>` marker in Editor.tsx (tests/inspector-order.test.ts lists the kinds); new casa3d .md docs and
  checkpoints need an entry in 20_TESTS/fixtures/unreadable_notes_allowlist.json (insert next to related entries, not at
  the end: appends at the end conflict); tests/lot4-legacy-golden.json hashes describePlan output (structural fields count).
