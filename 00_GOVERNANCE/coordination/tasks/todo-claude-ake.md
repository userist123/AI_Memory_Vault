# todo-claude-ake — Active Knowledge Engine

- Task: AKE Milestone 0, repository reality audit (read-only)
- Branch: `claude/ake-m0-reality-audit` (local, not pushed), base `origin/main` @ `aa42387f2`
- Updated: 2026-11-21T00:00:00Z (Claude Code)

## Done
- Audit written: `07_EVALUATION/active_knowledge_engine/REPOSITORY_REALITY_AUDIT.md` (verdict PARTIAL)
- 9 targeted test files: 116 passed, 1 xfailed (local Python 3.14.2)

## Key findings
- Sleep consolidation is already a scheduled read-only Loop A (daily CI cron)
- `learning/closed_loop_learning.py:333` writes storage directly as AI_AGENT (unwired; must not be wired)
- ApprovalBroker / nonce / revision binding: library only, not wired
- book_to_memory eval-integrity modules are reusable for Milestone 2; B03 packet never run

## Next
- Owner decisions: audit section 7, items 1-3
- Then Milestone 1 (tests first), alternative A recommended

## Blockers
- None technical; waiting for the owner's design choice
