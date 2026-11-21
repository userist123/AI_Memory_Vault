# AKE design decisions

The owner's standing instruction (2026-11-21, "do what is needed, as recommended") delegated the decisions
in `REPOSITORY_REALITY_AUDIT.md` section 7 to the recommended option. Each choice below is the reversible one.

| # | Decision | Choice | Why | How to reverse |
|---|---|---|---|---|
| 1 | Architecture | **Alternative A**: thin read-only runner over `SleepConsolidator` + `ConflictReviewWorkflow` | reuses what is in production; one writer (the controller) stays the only writer | delete `03_IMPLEMENTATION/packages/active_knowledge_engine/` |
| 2 | `closed_loop_learning.py` direct write | **quarantine by guard test**, no code change | the module is unwired; a refactor to proposals-only belongs to Milestone 3 | remove `test_closed_loop_learning_direct_write_path_stays_unwired` |
| 3 | `conflict_review.py` | **wired** as the AKE `conflict_review` stage (cases only, never resolved) | matches the handoff's Conflict Analyst contract | — |
| 3 | `learning/agents/`, `learning_promotion_gate.py` | **left as is** (not wired, not retired) | retiring is not reversible in spirit; no AKE need yet | — |
| 4 | Approval-binding claims | AKE records state `ApprovalBroker` is not wired (`limitations`) | the library is not in production | — |
| 5 | `04_CONFIG/agent_budgets.json` | **not loaded** | changes every benchmark operating point; remains an owner decision | — |
| 6 | First Loop B target | `PAGINATION_CUT` reranker hypothesis (loss funnel) | highest measured miss class | Milestone 2 |
| 7 | Providers | deterministic stages only in Milestone 1; no LLM, no network | air-gap safe; AST-checked | Milestone 2+ |

## Milestone 1 scope delivered

- `python -m active_knowledge_engine run --dry-run` (anything else exits 2)
- job id = trigger + SHA-256 of the ACTIVE/VERIFIED/REVIEW notes (bodies hashed, not copied) + SHA-256 of the config (UTC date included)
- stages: `integrity` (production `SleepConsolidator` + `ConflictDetector`) and `conflict_review` (`ConflictReviewWorkflow`)
- record: `08_OBSERVABILITY/active_knowledge_engine/<job_id>.json`, self-hashed (`record_sha256`), git-ignored
- COMPLETED reused unchanged; FAILED / STALE_SNAPSHOT kept as `<job_id>.attemptN.json` and retried

## First real-vault run (2026-11-21, HEAD aa42387f2 + this change, local)

498 eligible notes, consolidation budget 100 (budget exhausted: 100/498 processed),
29 dormant, 71 stale REVIEW, **6 conflict cases, all `overlap` (token overlap 0.9-1.0: near-duplicates, no contradiction)**.
After review round 1 the run reports `COMPLETED_WITH_SKIPS` (exit 3): **387 `.md` files storage cannot index**
(VAULT_STATE measured 293 on 2026-10-14; the difference is not investigated here). No tracked file changed
(`git status` identical before and after), the repository's `audit_log.jsonl` did not grow, and a second run reused the record.

## Review

- Round 1 (`vault-reviewer`, independent context): CHANGES_REQUIRED, 1 HIGH (CLI audit log in CWD; state row
  overstated "writes only its record"), 2 MEDIUM (snapshot missed `category`; skipped files invisible), 3 LOW
  (corrupt record blocked the job; reused record unverified; attempts overwritable). All six fixed test-first.

## Not done (next milestones)

- the 100-note consolidation cap (`_DEFAULT_MAX_ITEMS_PER_RUN` in `sleep_consolidation.py`) means one run covers ~20% of the eligible notes; rotating or raising the cap is a Milestone 2 decision
- Loop B (held-out v2 + loss funnel), Loop D (b2m harness), LLM analysts, scheduling, proposals

## Milestone 2 — Loop B, retrieval quality (2026-11-21)

Decisions taken under the same standing instruction:

| Decision | Choice | Why |
|---|---|---|
| Benchmark | frozen held-out v2 (`heldout.json`), verified against its `.sha256` before any case runs; dev set not used for evaluation | v1 is invalid; held-out stays held-out |
| Operating point | `MemoryController.search(AI_AGENT, query, page_size=5)`, the call behind `memory_search` | measure what an agent gets, not the owner view |
| Miss taxonomy | `PAGINATION_CUT`, `FILTERED`, `NEVER_CANDIDATE`, `GOLD_NOT_IN_STORAGE`, `ABSTAIN_UNMEASURABLE` | separates a structural zero from a retrieval failure |
| Comparison | exact McNemar on paired per-case hits; alpha 0.05 (convention, recorded in each result); refuses another digest, k or principal | no improvement claimed without a paired test on the same frozen set |
| Thresholds | **none chosen** for "how much better is enough" | the owner's decision (handoff §9.2) |
| Record content | case ids, classes, ranks; never query text or note content | held-out queries stay out of logs and prompts |

### Baseline (real vault, 2026-11-21)

`baselines/heldout_v2_ai_agent_k5.json`: **2/29 hits at k=5** (6.9%, Wilson 95% CI 1.9-22.0%), MRR@5 0.026.
Misses: 17 `NEVER_CANDIDATE`, 10 `PAGINATION_CUT`, 0 `FILTERED`, 0 `GOLD_NOT_IN_STORAGE` (all 29 gold notes are in storage).
By class: exact identifier 1/5, synonym 1/4, paraphrase 0/4, lexical trap 0/4, one-hop graph 0/12.
Corroborated by R016 graph-off context recall, 2/29 (HUMAN, page 10).

What this says, and what it does not:
- The agent operating point retrieves almost nothing from held-out v2. The dominant loss here is
  `NEVER_CANDIDATE` (17/29), not `PAGINATION_CUT` (10/29). The loss funnel's 71.6% `PAGINATION_CUT`
  was measured on benchmark v3 (130 cases): a different population, so it is not contradicted.
- R016 reports candidate recall 0.69 with a controller built on `VaultIndex`; the production agent path
  (FileStorageEngine, no index) finds 12/29 as candidates. Why the two candidate generators differ is the
  first open question for Loop B; nothing is changed here.
- 29 measurable cases cannot resolve small effects: a change must flip several cases to be distinguishable.
