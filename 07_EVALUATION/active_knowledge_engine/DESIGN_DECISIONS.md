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

| Miss class | Cases | Meaning |
|---|---:|---|
| `GOLD_NOT_AGENT_ELIGIBLE` | 13 | never a candidate because policy excludes the gold for an agent: 11 notes carry **no `lifecycle` at all** (type `core`), 2 carry `lifecycle: raw` |
| `PAGINATION_CUT` | 10 | candidate, fused rank > 5 |
| `CANDIDATE_LIMIT_CUT` | 2 | past the 200-candidate limit with some signal (cut ranks 263-326) |
| `NO_LEXICAL_SIGNAL` | 1 | past the limit with zero BM25 and entity score: a larger limit would not recover it |
| `NEVER_CANDIDATE` | 1 | eligible, never a candidate |
| `ABSTAIN_UNMEASURABLE` | 3 | no abstention signal exists |

Of the 16 cases whose gold an agent may be served, 2 are hits (12.5%).
Separately, **`memory_get` refuses 20 of the 29 gold notes**, 7 of them policy-eligible. Two ACTIVE, verified
gold notes (cases H03, H27) fail with `BudgetExceededError: Context usage 925351 exceeds hard limit 32768
bytes` inside `cognitive_read`. An agent can see their id in `memory_search` but cannot read them. This is
a production read-path defect, recorded and not changed here.

What this says, and what it does not:
- At the agent operating point the dominant loss on held-out v2 is policy, not ranking: 13/29 gold notes
  are notes an agent is never allowed to see (no lifecycle, or raw). Whether those notes should get a
  lifecycle, or whether the benchmark should not use them as gold, is an owner decision.
- Among eligible gold, the loss is mostly ranking (10 `PAGINATION_CUT` + 2 `CANDIDATE_LIMIT_CUT`); 1 has no lexical signal at all. The loss
  funnel's 71.6% `PAGINATION_CUT` was measured on benchmark v3 (130 cases), another population.
- R016 reports the same count (2/29 graph-off context recall) but on **different cases** (exact 1/5 +
  one-hop 1/12 there; exact H01 + synonym H13 here) and at **another operating point**: HUMAN, whose
  owner historical view searches the whole non-RAW corpus, page 10, `RANKING_ARM_BASELINE` pinned.
  Its candidate recall 0.69 therefore mixes principal, candidate pool and ranking arm, and is not
  comparable with the agent path. Equal counts are not corroboration.
- 29 measurable cases cannot resolve small effects: a change must flip several cases to be distinguishable.

### Review round 1 (Milestone 2)

`vault-reviewer`: CHANGES_REQUIRED.
- 1 HIGH, reproduced: the job id ignored code and controller settings, so a candidate run could get the baseline record back. Fixed: the config now includes `code_fingerprint`, the controller settings and the sidecar hash.
- 2 MEDIUM: the "corroborated by R016" wording, and the R016 confounder left unnamed. Both are reworded above.
- 2 LOW: the fused-order miss classes, and `k` not clamped. Fixed with `REORDERED_OUT` / `CANDIDATE_LIMIT_CUT` / `GOLD_NOT_AGENT_ELIGIBLE`, and `k` capped at 20 like `memory_access`.

All fixes were written test-first.

### Review round 2 (Milestone 2)

`vault-reviewer` confirmed the round-1 fixes and checked the documented numbers against the baseline JSON.
It raised one MEDIUM and two LOW findings. This was the last review round, so the main session decided and fixed all three, test-first:

- **MEDIUM:** the hand-picked fingerprint missed `security/verified_reduction.py`, `sanitize_query` and storage/authorizer/policy.
  - Fix: the fingerprint now hashes whole read-path packages (`memory`, `retrieval`, `security`, `lifecycle`, `graph`, `observability`, this package) plus the controller source, 137 files.
  - A missing listed source now raises instead of being skipped.
- **LOW:** a gold past the candidate limit could have no signal at all.
  - Fix: new class `NO_LEXICAL_SIGNAL` (both raw scores zero), and `cut_rank` is recorded.
- **LOW:** quarantine was in the eligibility predicate, but `search()` does not filter on it.
  - Fix: the predicate is now the lifecycle floor only; quarantine stays a `memory_get` rule.

## Loop A stage `agent_readability` (2026-11-21)

### Why the reranker experiment was not run

The obvious next Loop B step was a reranker or ranking-arm experiment for the 10 `PAGINATION_CUT` cases. It was not run.

- The controller's ranking arms (A1-A4) were already compared and validated on this same held-out v2 set (`07_EVALUATION/r024_wp1_ranking/`, `07_EVALUATION/r025_wp8_a1_heldout/`).
- Re-selecting among them on held-out v2 would turn the held-out set into a tuning set.
- A new reranker needs the dev set (12 cases) for development and a preregistration before one held-out run.

### What was found instead: the read-path defect behind Milestone 2

Root cause, with a reproduction:
- `ContextBudget.apply_degradation` (`retrieval/context/budget.py`) never truncates "protected" content (lines matching MUST/NEVER/SHALL/REQUIRED/FORBIDDEN).
- So a protected note larger than the 32,768-byte hard limit always raises `BudgetExceededError` inside `cognitive_read`.
- `memory_get` then refuses it (fail closed).
- The held-out gold of H03 and H27 is `Master_Skills_Catalog_251.md`, 919,796 characters.

The new stage `active_knowledge_engine/readability.py` checks every ACTIVE/REVIEW note.
- The verdict comes from the production predicate `vault_cli.eligible_for_agent`.
- The reason comes from the exception `cognitive_read` raises.
- It records ids, paths, sizes and verification labels, never content.

### Real vault result (498 notes in scope): 49 notes an agent can never read

| Group | Notes | Cause |
|---|---:|---|
| REVIEW, `verification: verified_source` | 26 | `verified_source` is **not in the schema enum** (`lifecycle/validation/schema.py`: verified, partially_verified, unverified, inferred). All are legal sources and indexes with no provenance; the route withholds their body and `memory_access` hands over only `unverified` REVIEW notes |
| REVIEW, `verification: inferred` | 19 | a valid label, but `memory_access._review_candidate` hands over only `unverified` REVIEW notes, so an `inferred` candidate is withheld while an `unverified` one is served |
| ACTIVE, `verified`, `BudgetExceededError` | 3 | protected content over the hard limit (catalogue 925 KB, `10 Imports and Sources Map.md` 28 KB, `Continuity_Handoff.md` 22 KB) |
| ACTIVE, no verification, withheld | 1 | withheld by the route |

### Decisions left to the owner; nothing changed here

1. Is `verified_source` a label the schema should accept, or should the 26 legal notes be relabelled?
   Relabelling edits canonical notes.
2. Should `memory_access` hand over `inferred` REVIEW notes the way it hands over `unverified` ones, flagged as unverified?
   This changes the agent read contract.
3. How should a protected note over the hard limit be served?
   - Option a: split it at ingestion.
   - Option b: allow a bounded, explicitly `[PARTIAL]` excerpt of protected content.
   - Option c: keep refusing.

   Options a and b change a security boundary or canonical content.
