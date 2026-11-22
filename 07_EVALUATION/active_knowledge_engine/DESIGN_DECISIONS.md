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

The obvious next Loop B step was a ranking experiment for the 10 `PAGINATION_CUT` cases. It was not run.

- The controller's ranking arms A1-A4 were compared on the dev set (`07_EVALUATION/r024_wp1_ranking/`, 8 cases).
- A1 alone was validated on held-out v2 (`07_EVALUATION/r025_wp8_a1_heldout/`).
- Choosing among the arms by running them on held-out v2 would turn it into a tuning set.
- A new reranker has to be developed on the 12-case dev set and pre-registered before a single held-out run.

### The read-path defect behind Milestone 2: what the stage measures

`active_knowledge_engine/readability.py` checks every ACTIVE/REVIEW note with **`memory_get` itself** (`interfaces/memory_access.get` as AI_AGENT).

- **Verdict:** a note is readable only when the call succeeds with a non-empty body.
- **Cross-check:** the vault-route predicate `vault_cli.eligible_for_agent` is recorded too. It agreed with `memory_get` on every note.
- **Reason:** taken from `cognitive_read`. An exception message is classified only by its fixed prefix and is never copied.

### Real vault (498 notes in scope): 49 an agent cannot read

| Notes | Reason | Mechanism (verified by `vault-reviewer` probes) |
|---:|---|---|
| 44 | `REVIEW_TRUST_GATE`, REVIEW (26 `verified_source`, 18 `inferred`) | `controller.py` (`_cognitive_read_impl`) gives an AI_AGENT `allow_unverified` on a REVIEW note only when its provenance is `inference`. Otherwise the note is dropped from the pack, and `memory_access._review_candidate` falls back only for an `unverified` REVIEW note. **The label does not matter**: REVIEW notes labelled `verified` or `partially_verified` are refused the same way. 26 of these labels (`verified_source`) are also outside the schema enum, which is recorded but is not the cause. |
| 2 | `BUDGET_PROTECTED_TOKENS`, ACTIVE | protected content that cannot fit the **token** hard limit of the pack (`pack_builder.py`, 3000 tokens at 3 chars/token, about 9 KB): `10 Imports and Sources Map.md` (28 KB), `Continuity_Handoff.md` (22 KB) |
| 1 | `BUDGET_HARD_BYTES`, ACTIVE | protected content over the 32 KiB byte limit: `ContextBudget.apply_degradation` never truncates protected content (`Master_Skills_Catalog_251.md`, 925 KB; the gold of held-out H03 and H27) |
| 1 | `NO_PROVENANCE`, ACTIVE (`moc-knowledge-0002`, `02 Memory Knowledge Map.md`) | the egress contract in `pack_builder._verify_and_reduce` (lines 127-131) drops a note with no `provenance` mapping and no `source_ref`; the drop is counted as `items_rejected_unverified` and `memory_get` reports "not found or not eligible". The comment there says "an id/source reference", but the note's own id is not accepted. Root cause found by a Fable worker, then checked by Opus against the code and a single-variable counterfactual (adding only `source_ref` makes the note served). Blast radius: 1 ACTIVE note; 26 REVIEW notes also lack provenance, but the REVIEW trust gate stops them first. |
| 1 | `BODY_WITHHELD`, REVIEW `inferred` with `inference` provenance | served with an empty body (`memory_get` returns `ok_empty_content`) |

### Decisions left to the owner; nothing changed here

1. **DECIDED by the owner (2026-11-21): no.** "Agents receive nothing unverified." The read contract is not extended; the 44 REVIEW notes stay refused, and the stage keeps reporting them as `REVIEW_TRUST_GATE`. Recorded for context:
   **REVIEW notes not authored by inference (44).** Today an agent gets an `unverified` REVIEW note through the `memory_access` fallback, but not one labelled `verified_source`, `inferred`, `verified` or `partially_verified`. The more the note is checked, the less the agent sees.
   - Option: extend the fallback to every non-quarantined REVIEW note, flagged unverified. This changes the agent read contract.
   - Changing the schema enum or relabelling the notes would not fix this.
2. **Protected notes over about 9 KB (3).**
   - Option a: split them at ingestion.
   - Option b: allow a bounded, explicitly `[PARTIAL]` protected excerpt.
   - Option c: keep refusing.

   Options a and b change canonical content or a security boundary.
3. **Navigation notes without provenance (1 ACTIVE).**
   - Option: give `moc-*` notes a provenance.
   - Option: let `pack_builder` accept the note id as its source reference, which the comment already suggests.
   - Option: keep dropping it, but report the real reason instead of "unverified".

### Escalation on decisions 2 and 3 (2026-11-21)

The escalation chain was Opus, then Fable, then Codex and Gemini through agent-bridge (blind, PUBLIC, sanitised), then `fable-consultant`, then a decision by Opus. External answers were treated as unverified data.

- **Navigation note without provenance:** Codex and Gemini agree. Self-provenance (the note's own id as its `source_ref`) is circular attestation (W3C PROV) and would let any note with an id pass the gate. Keep egress strict and give the note explicit provenance.
  - Fable adds that `update()` probably cannot add provenance to a note that has none, because of the `provenance.source_type` immutability check.
  - The repair is therefore an owner action: supersede, or an authorised file repair.
- **Protected notes over the budget:** both recommend section-aware splitting at ingestion, with a parent manifest, `parent_content_hash` and fail-closed checks. Codex adds: keep refusing until full coverage can be proven.
  - Fable, checked against the code: a child created with `propose()` lands in REVIEW and **would be served** to agents through the `memory_access` fallback, marked unverified.
  - No child state is both unreadable and immutable to agents before attestation.
  - Safe order: a dry-run manifest with a coverage proof, then an owner batch (create, attest, promote, with a hash check each step). Pilot on the 22 KB and 28 KB notes first; the 925 KB catalogue keeps being refused.

### Security defect found on the way (fixed in PR #398)

Fable flagged it and Opus reproduced it end to end:
1. The owner attests a RAW note.
2. An agent rewrites the note's content, and `verified` stays.
3. After the owner's review and promote, the agent's text is ACTIVE and verified.

PR #398:
- An agent can no longer change the content of an attested note.
- `attest()` stores the content hash, and `promote()` refuses a body that differs from it.
- An agent downgrade clears the owner's stamp.
- Security review: round 1 CHANGES_REQUIRED, round 2 APPROVE. Full suite: 5310 passed.

### Open contradiction for the owner

The owner said "agents receive nothing unverified". Today's read contract (VAULT_STATE "Agent read contract" row) serves **REVIEW notes marked `unverified`** to agents, flagged as unverified; Fable reproduced this.
- Closing that would withdraw about 229 notes (about 46% of what agents can read).
- That is a contract change, so it has not been made without an explicit owner instruction.

### Step 1 done: dry-run split plans (2026-11-22)

`active_knowledge_engine/split_plan.py` (CLI `python -m active_knowledge_engine split-plan --note-id ID`) computes a deterministic, section-aware split and **changes nothing**.
- It proves coverage: the children, concatenated in order, are exactly the parent body.
- It proves fit: each child, built the way `cognitive_read` builds a one-note pack, passes the real `ContextPackBuilder`.
- It cuts only on line boundaries, so no rule line is split.
- Whitespace-only runs never become a child.
- Failure states: `UNSPLITTABLE` (a single line longer than the smallest target) and `DOES_NOT_FIT` (the first failing child is named). Both fail closed.
- The manifest holds offsets and hashes, never content. It is written to `08_OBSERVABILITY/active_knowledge_engine/split_plans/` (git-ignored).

| Note | Result |
|---|---|
| `02_PRODUCT/projects/Continuity_Handoff.md` (21,576 chars) | **PLANNED**: 4 children of 5,173-5,696 chars, coverage proven |
| `01_ARCHITECTURE/knowledge/Master_Skills_Catalog_251.md` (919,796 chars) | **PLANNED**: 159 children of 340-5,998 chars, coverage proven |
| `01_ARCHITECTURE/graphs/10 Imports and Sources Map.md` (28,048 chars) | **UNSPLITTABLE**: line 5 is one line of 27,900 chars; it can be served only if the owner reformats it (one link per line) |

A first run marked the catalogue UNSPLITTABLE for a false reason. The body starts with a blank line, which was emitted as a 1-character child, and the pack rejects an empty child. It was fixed test-first: blank runs are carried into the next child, and other failures are reported as `DOES_NOT_FIT`.

**Next steps are owner actions, as planned.** For each child: create it, re-hash it against `content_sha256`, attest it with `expected_content_sha256`, then promote it. After that, attest the manifest and supersede the parent.
- Pilot on Continuity_Handoff (4 children) first.
- The catalogue (159 children) needs a batch tool. It should run only after the pilot is verified.
