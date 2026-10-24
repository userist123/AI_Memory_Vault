# todo-claude-casa3d-a1-3
STATUS: IN_PROGRESS        UPDATED: 2026-10-24T15:00:00+03:00
TASK: Casa3D A1.3 workspace layout (roadmap todo-claude-casa3d-a1-roadmap.md, phase 2). Plan: casa3d/PLAN_A1_3.md.
BRANCH / WORKTREE: claude/casa3d-a1-3-workspace, D:/w/a13 (base origin/main a32613fba), npm ci done in app/.
OWNER DECISIONS 2026-10-24: new layout ON for all + "Layout clasic" preference; catalog stays in the right panel;
  phone sheet gets a Structure / Panel switch.
DONE: inventory, plan.
NEXT: slices 1-4 of PLAN_A1_3.md (layout state, outliner tree, Outliner+splitters+Focus wiring, inspector-order test),
  then tsc, vitest --maxWorkers=2, browser (launch config casa3d-a1, port 3200), vault-reviewer, PR, CI once, merge.
KEY FILES: app/components/Editor.tsx, app/core/plan-describe.ts, app/lib/commands.ts, app/app/globals.css,
  app/scripts/check-a11y.mjs; new: app/lib/workspace-layout.ts, app/core/outliner.ts, app/components/Outliner.tsx.
