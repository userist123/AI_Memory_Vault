# todo-claude-ake — Active Knowledge Engine

- Task: AKE Milestone 0 (audit) + Milestone 1 (read-only shadow runner)
- Branch: `claude/ake-m0-reality-audit`, draft PR #396, base `origin/main` @ `aa42387f2`
- Updated: 2026-11-21 (Claude Code)

## Done
- M0 audit: `07_EVALUATION/active_knowledge_engine/REPOSITORY_REALITY_AUDIT.md` (verdict PARTIAL)
- Decisions (owner delegated, recommended options): `07_EVALUATION/active_knowledge_engine/DESIGN_DECISIONS.md`
- M1: `03_IMPLEMENTATION/packages/active_knowledge_engine/` + `20_TESTS/test_ake_shadow_runner.py`
- Guard: `test_vault_state_accuracy.py::test_closed_loop_learning_direct_write_path_stays_unwired` (proved to fail when wired)
- VAULT_STATE.md: two rows (AKE runner, closed_loop_learning)
- Real-vault dry run: 498 notes, 6 overlap cases, no tracked file changed, second run reused

## Next (Milestone 2)
- Loop B: held-out v2 + loss funnel as an AKE stage (PAGINATION_CUT reranker hypothesis)
- Decide the 100-note consolidation cap (covers ~20% of eligible notes per run)
- Reuse book_to_memory eval-integrity modules (leakage, paired stats, raters)

## Blockers
- None
