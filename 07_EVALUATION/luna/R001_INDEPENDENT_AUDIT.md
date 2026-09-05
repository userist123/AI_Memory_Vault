# LUNA / GPT-5.6 — R001 Independent Audit

## Evidence status

- Round: `R001`
- Baseline `main` SHA resolved at audit start: `4764ee4615760a0480abd76ff2633aac25840e03`
- Luna branch: `luna/r001-independent-audit-20260904`
- Audit mode: independent static verification + attack planning
- Local runtime execution: `BLOCKED` — the execution environment could not resolve `github.com`, so a fresh clone and pytest run were not possible in this turn.
- Therefore no local test/run result is being claimed.

## L1 — Baseline

The current `main` was re-resolved before auditing. Prior CI evidence from older commits is not treated as evidence for this SHA.

## L3 — Retrieval architecture falsification

### Finding R001-L3-01 — candidate generation is storage/query driven

`memory_controller/context/retrieval.py` calls `self.storage.query(...)`, truncates the returned list to `candidate_limit`, and returns it. This path does not invoke `SemanticProvider` or embedding search.

Evidence: `CODE_VERIFIED`.

### Finding R001-L3-02 — declared semantic provider is deterministic lexical Jaccard

`cognitive_core/semantic.py` defines `DeterministicSemanticProvider`; its own implementation notes that it is a mock and computes basic word-overlap Jaccard similarity using token sets.

Evidence: `CODE_VERIFIED`.

### Finding R001-L3-03 — controller ranking is also lexical/token based

`memory_controller/context/relevance_scoring.py` derives query/note token sets with `.split()`, calculates overlap ratio, averages it with note confidence, and sorts descending.

Evidence: `CODE_VERIFIED`.

### Finding R001-L3-04 — graph reranking is downstream of candidate selection

`cognitive_core/ranked_search.py` first calls `controller.search(...)`, then builds graph/activation state from the returned results and reorders those IDs. It therefore cannot restore a relevant memory that was excluded before this stage.

Evidence: `CODE_VERIFIED`.

### Finding R001-L3-05 — activation edge weights are currently computed and then discarded

In `SpreadingActivationEngine._propagate_on_graph`, a weighted propagation expression is assigned to `propagated` and immediately overwritten by `score * (decay ** (hop + 1))`. The effective propagation formula therefore ignores the edge `weight` variable.

Evidence: `CODE_VERIFIED`.

Consequence: graph topology can influence reachability, but the implementation as written does not use edge weight magnitude in the final propagation score. This requires a controlled A/B test before any cognitive benefit is claimed.

## L5 — Lifecycle / provenance

`controller.cognitive_read()` explicitly permits `ACTIVE` and `REVIEW`, and marks returned REVIEW notes with `_cognitive_unverified=True`. Normal `read()` remains separate. This supports a boundary between cognitive inspection and ordinary read, but does not by itself prove that downstream agents cannot interpret retrieved REVIEW content as authority.

Evidence: `CODE_VERIFIED`; downstream authority behavior remains `UNVERIFIED` until an end-to-end runtime test exercises the retrieval-consuming agent/tool boundary.

## L7 — Learning loop

`scripts/label_council_outcome.py` is explicitly telemetry/evidence only. It appends JSONL outcome records and does not update canonical memory or enqueue/promote candidates.

Evidence: `CODE_VERIFIED`.

Conclusion: the inspected writer is not proof of a closed `OUTCOME -> EVIDENCE -> MEMORY UPDATE -> FUTURE RETRIEVAL -> FUTURE OUTCOME` loop. Loop closure remains `UNVERIFIED`.

## R001 execution blockers

1. Fresh local clone/test execution could not start because network name resolution for GitHub failed in the execution container.
2. No runtime poisoning test can be honestly marked passed from static inspection.
3. No calibration metric can be marked runtime-verified.
4. No causal memory-effect claim can be accepted without treatment/control evidence.

## Acceptance decisions

| Area | Decision | Basis |
|---|---|---|
| Retrieval semantic capability | `REWORK_REQUIRED` | Static code proves lexical/Jaccard path, not semantic candidate generation |
| Graph reranking | `ACCEPT_WITH_GAPS` | Module exists, but controlled behavioral benefit is unverified |
| Edge-weight propagation | `REWORK_REQUIRED` | Weighted expression is overwritten before use |
| REVIEW authority safety | `ACCEPT_WITH_GAPS` | Lifecycle tagging is visible; end-to-end authority containment is unverified |
| Outcome learning loop | `REJECT` as a capability claim | Telemetry writer explicitly does not close the loop |
| Causal memory benefit | `BLOCKED` | Requires executable treatment/control evidence |

## Next attack plan

- Run a held-out lexical-vs-semantic suite once a runnable checkout is available.
- Add adversarial REVIEW payloads with fake SYSTEM/developer instructions at the actual retrieval consumer.
- Compare BASE vs BASE+activation with fixed candidate sets and record exact rank changes.
- Verify whether the edge-weight overwrite is intentional; do not change it inside Luna lane.
- Trace outcome records to any actual memory mutation/consumer before calling the learning loop closed.

## Evidence classification summary

`CODE_VERIFIED` findings are limited to the exact source inspected on baseline SHA `4764ee4615760a0480abd76ff2633aac25840e03`.

Runtime-dependent conclusions remain `UNVERIFIED` or `BLOCKED`.