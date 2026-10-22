---
project_id: CASA3D
application: Casa3D — interior design studio (2D plan, 3D viewer, technical drawings, client share link)
repository: userist123/AI_Memory_Vault_CODEX_READY
workspace: 02_PRODUCT/projects/workspaces/casa3d
last_updated_utc: 2026-10-22T00:00:00Z
status: ACTIVE
working_branch: gemini/casa3d-nextgen-functionality-uiux (PR #304; PR #305 is fully contained in it)
claimed_by: claude-code — 2026-10-21T21:00:00Z (CI repair and client share pins); antigravity — next-gen upgrade, see its checkpoint
active_work:
  - make PR #304 green; the merge itself is the owner's action
done_on_branch:
  - next-gen functional, UI/UX and 3D lifecycle upgrade (checkpoint 00_GOVERNANCE/coordination/tasks/todo-antigravity-casa3d.md)
  - PIN-protected client comment pins on the share link (design and threat model 00_GOVERNANCE/coordination/projects/CASA3D/SHARE_PINS_DESIGN.md; checkpoint 00_GOVERNANCE/coordination/tasks/todo-claude-casa3d-pins.md)
open_prs:
  - "#304 next-gen upgrade + pins (this branch)"
  - "#305 pro editor master prompt (contained in #304, can be closed after #304 merges)"
  - "#257 docs-only follow-ups, 4 commits not in #304 (owner decision)"
  - "#258 codex memory, 218 commits not in #304, conflicts with main (separate reconciliation)"
verification: app root `npx vitest run` and `npx tsc --noEmit -p .`; vault side `python 30_SCRIPTS/verification/personal_data_guard.py`
---

# CASA3D — current state

Read the frontmatter above, then the checkpoint files it names. Do not reload old conversations.
