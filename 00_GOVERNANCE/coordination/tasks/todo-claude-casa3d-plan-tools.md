# todo-claude-casa3d-plan-tools
STATUS: REVIEW   UPDATED: 2026-10-24T03:30:00+03:00
TASK: Casa3D plan tools batch 1. Owner 2026-10-23: more tools in the left rail besides doors/walls/glass walls,
  research in one agent, implement in another. Research: D:/w/casa3d-verify/research/unelte-plan.md (local, not committed).
BRANCH / PR: claude/casa3d-plan-tools (worktree D:/w/tools), base b2f29b8c, main (Next 16, #334) merged in.
DONE:
- Server validation for columns/beams/glass walls/niches/annotations (core/plan-limits.ts, lib/repo.ts checkSnapshot).
- Passage opening (Opening.passage, key J); shaft/boxing (Column.role/height, key H); niche (Floor.niches, key N,
  visual only in 3D); persistent dimensions and text (Floor.annotations, keys Q/O/B, DXF layers DIMENSIONS/NOTES);
  ToolRail groups (lib/tool-groups.ts, components/ToolRail.tsx).
- Stage picker accessible name contains its visible text (axe label-content-name-mismatch at 1366 px).
- Review round 1 (vault-reviewer, NOT READY) fixed: fields that existed before the limits (column w/d, beam w/d, glass
  thickness, mullions) are clamped on the server instead of rejected (old projects stay savable); mullions capped in
  the panel; per-level count caps refused at placement (editor.limitReached); group menu returns focus to its button;
  plan text kept on blur / click elsewhere, focus back on the plan after Enter/Escape; bidi controls stripped.
EVIDENCE: tsc 0; vitest 767/767; build OK; check-a11y 19/19 OK; browser check of the text tool (Enter / click
  elsewhere / Escape) and screenshots in D:/w/casa3d-verify/shots/lot1-*.png.
NEXT: review round 2 (delta only), push, draft PR, CI, merge. Batch 2 (doors and wall decor) runs on
  claude/casa3d-doors-decor (D:/w/doors), checkpoint todo-claude-casa3d-doors-decor.md.
OPEN: niche is not cut into the wall in 3D; "null.trim" seen once in 3D (not reproduced); topbar wraps
  "Salvează revizia" onto a second row at 1366 px (pre-existing on main, not from this batch).
KEY FILES: app/core/plan-limits.ts, app/lib/repo.ts, app/components/{Editor,PlanView,ToolRail}.tsx,
  app/lib/tool-groups.ts, app/tests/plan-limits.test.ts, app/tests/plan-tools.test.ts.
