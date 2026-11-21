# todo-claude-ake — Active Knowledge Engine

- Task: AKE Milestones 0-2 (audit, read-only shadow runner, Loop B retrieval quality) + Loop A agent_readability
- Branch: `claude/ake-m0-reality-audit`, draft PR #396; secret-scan fix also in draft PR #397
- Updated: 2026-11-21 (Claude Code)

## Done
- M0 audit: `07_EVALUATION/active_knowledge_engine/REPOSITORY_REALITY_AUDIT.md` (verdict PARTIAL)
- Decisions (owner delegated, recommended options): `07_EVALUATION/active_knowledge_engine/DESIGN_DECISIONS.md`
- M1: `03_IMPLEMENTATION/packages/active_knowledge_engine/` (runner, CLI), guard for closed_loop_learning
- M2: `retrieval_quality.py`, `compare.py`, baseline `07_EVALUATION/active_knowledge_engine/baselines/heldout_v2_ai_agent_k5.json`
  - 2/29 hits at k=5; 13 gold policy-excluded (11 no lifecycle, 2 raw); memory_get refuses 20/29 gold
- Loop A agent_readability: 49/498 ACTIVE/REVIEW unreadable via memory_get (44 REVIEW dropped: no inference provenance;
  3 protected over budget: 2 token limit ~9 KB, 1 byte limit 32 KiB)
- Reranker experiment NOT run: re-selecting arms on held-out v2 would leak
- Full suite 5354 passed, 1 flaky socket failure (REST test, passes alone 3x) (local, Python 3.14); two review rounds per milestone

## Next
- Owner decisions: REVIEW read contract (44 notes); protected notes over ~9 KB (split / [PARTIAL] / refuse);
  lifecycle for the 11 `core` gold notes;
  Loop B improvement threshold; consolidation cap (100 notes per run)
- Loop B: a new reranker only via dev set + preregistration, then one held-out run with `compare`
- Unexplained: 1 ACTIVE note without verification dropped from the pack
- Milestone 3 (proposal-only learning) after the owner reviews PR #396

## Blockers
- PR CI: `enforce` (gitleaks history) needed the #397 fingerprint; cherry-picked onto this branch
