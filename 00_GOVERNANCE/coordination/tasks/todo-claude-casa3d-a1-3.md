# todo-claude-casa3d-a1-3
STATUS: IN_PROGRESS        UPDATED: 2026-10-24T21:00:00+03:00
TASK: Casa3D A1.3 workspace layout (roadmap todo-claude-casa3d-a1-roadmap.md, phase 2). Plan: casa3d/PLAN_A1_3.md.
BRANCH / WORKTREE: claude/casa3d-a1-3-workspace, D:/w/a13 (base origin/main a32613fba), npm ci done in app/.
OWNER DECISIONS 2026-10-24: new layout ON for all + "Layout clasic" preference; catalog stays in the right panel;
  phone sheet gets a Structure / Panel switch.
DONE (commit 1 on branch): lib/workspace-layout.ts, core/outliner.ts (+ parent in describePlan), components/Outliner.tsx
  (APG tree), components/PanelSplitter.tsx, Editor wiring (grid ws-studio, Focus Ctrl+\, classic pref in ⋯ and Ctrl+K,
  phone sheet switch), commands ws.focus/ws.outliner/ws.classic, RO/EN strings, inspector markers +
  tests/inspector-order.test.ts (inline snapshot), check-a11y audits outliner + classic list.
VERIFIED: tsc 0; vitest 136 files / 893 tests pass; check-a11y 21 states 0 violations (dev server :3213); browser
  1440 px EN+RO (tree select/expand, splitter drag + keys, reload keeps widths, Focus via key and button, classic
  round trip), phone 375 px RO (Structure/Panel switch, select from tree). Fixed in browser: room row label, room
  glyph looked like a checkbox, Focus button style specificity, pressed sheet button contrast.
REVIEW: round 1 NOT READY (keys leaked from tree into editor; right splitter scrolled with inspector; + 5 low) ->
  fixed in commit 2. Round 2 (delta) READY with 2 minor gaps (focus on display:none inspector; nested ternaries in the
  order test) -> fixed in commit 3 (focus recovery in a post-commit effect, skipped on mount). Browser re-verified.
  Accepted residual: Ctrl+\ untested on a physical RO keyboard (alt:'any' covers AltGr in code); inspector order test
  freezes the inline blocks + RoomPanel/PlacementPanel, not DoorPanel/WallLookPanel/RoomLookPanel/RoomOptionsPanel.
VERIFIED (final): tsc 0; vitest 136 files / 893 tests; check-a11y 21 states 0 violations (before round-1 fixes).
NEXT: push -> draft PR -> CI once -> merge; then mark phase 2 DONE in todo-claude-casa3d-a1-roadmap.md.
ENV: launch config casa3d-a13 (port 3213) added to the primary checkout's .claude/launch.json (not committed).
  next-env.d.ts is rewritten by `npm run dev`: never commit it.
KEY FILES: app/components/{Editor,Outliner,PanelSplitter}.tsx, app/core/{outliner,plan-describe}.ts,
  app/lib/{workspace-layout,commands}.ts, app/app/globals.css (end of file), app/scripts/check-a11y.mjs.
