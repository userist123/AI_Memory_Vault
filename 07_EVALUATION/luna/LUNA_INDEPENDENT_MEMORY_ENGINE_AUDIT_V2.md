---
type: evaluation
category: independent-audit
status: active
version: 2.0.0
evidence_policy: strict
baseline_sha: "9a663213c52b971dee28d4eff729d1e93914fdce"
agent: "LUNA / GPT-5.6"
---

# LUNA — Independent Memory Engine Audit V2

## Audit scope

Independent audit of `userist123/AI_Memory_Vault_CODEX_READY` at baseline `main @ 9a663213c52b971dee28d4eff729d1e93914fdce`.

This report is deliberately separate from Codex, Antigravity and Perplexity lanes. Their claims are not accepted as evidence unless independently reproduced.

## Ground truth

### Baseline

- `main` SHA: `9a663213c52b971dee28d4eff729d1e93914fdce` — `CODE_VERIFIED` / `RUNTIME_VERIFIED` only for repository ref resolution; local checkout execution unavailable in this environment.
- Luna audit branch: `luna/audit-v1`, created from the baseline SHA.
- Current CI status for baseline: `pending`, zero status checks reported at time of audit. `CI_VERIFIED` is therefore NOT established for `9a663213...`.
- Latest independently observable successful Memory V6 Actions run before the baseline was commit `6357344d727cce0cb197ea993bb9589ff811f21b`, run `33878520659`, conclusion `success`, with pytest jobs on Python 3.10/3.11/3.12. `CI_VERIFIED` for that prior commit only.

A local `git clone` / pytest attempt from this environment failed because outbound DNS access to `github.com` was unavailable. No local test result is claimed.

## Evidence matrix

| Area | Finding | Evidence | Status |
|---|---|---|---|
| Retrieval provider | Deterministic provider uses Jaccard/token overlap, not embeddings | `cognitive_core/semantic.py` | `CODE_VERIFIED` |
| Controller retrieval | `MemoryController.search()` uses `RetrievalEngine` then `RelevanceScorer` | `memory_controller/controller.py` | `CODE_VERIFIED` |
| Relevance scoring | Primary scorer is token overlap + confidence average | `memory_controller/context/relevance_scoring.py` | `CODE_VERIFIED` |
| Recall scoring | Recall combines semantic similarity, WM relevance, confidence/authority, activation and temporal signal | `cognitive_core/recall.py` | `CODE_VERIFIED` |
| REVIEW recall | REVIEW notes are copied and marked `_cognitive_unverified=True`; no promotion operation exists in recall path | `cognitive_core/recall.py` | `CODE_VERIFIED` |
| Raw search exclusion | Storage query excludes `RAW` from normal query results | `memory_controller/controller.py` | `CODE_VERIFIED` |
| Associative graph | Four graphs exist: semantic, temporal, causal, entity | `cognitive_core/multi_graph.py` | `CODE_VERIFIED` |
| Graph re-ranking | `ranked_search.py` re-ranks controller results using spreading activation | `cognitive_core/ranked_search.py`, `spreading_activation.py` | `CODE_VERIFIED` |
| Activation | ACT-R-style base activation and relation-based spreading are implemented | `cognitive_core/activation.py`, `synapse.py` | `CODE_VERIFIED` |
| Outcome loop | Outcome labeling appends telemetry JSONL only and explicitly does not modify canonical memory | `scripts/label_council_outcome.py` | `CODE_VERIFIED` |
| Security baseline | Security hardening tests cover authorization/lifecycle/provenance controls | `memory_controller/tests/test_security_hardening.py`, reconciliation report | `DOCUMENT_VERIFIED` + `CODE_VERIFIED` for sampled tests |
| Memory poisoning | No dedicated end-to-end evidence proving retrieved content cannot become agent authority | absence of dedicated verified test + inspected interfaces | `UNVERIFIED` |
| Full suite count | Prior README claim of 1,671 tests is not accepted as current baseline evidence | current CI run was not observed on `9a663213...` | `UNVERIFIED` |

## Track A — Retrieval

### Verdict

**Current retrieval is lexical/token-overlap dominated, not genuine semantic embedding retrieval.** `CODE_VERIFIED`.

The deterministic `SemanticProvider` implementation tokenizes text and computes Jaccard similarity. The repository therefore has a semantic-provider abstraction, but the current deterministic implementation is lexical. `cognitive_core/semantic.py` explicitly documents Jaccard instead of embeddings.

The controller's normal search path is additionally scored by `RelevanceScorer`, which uses word-set overlap and confidence. This makes a claim of fully semantic retrieval unsupported by the inspected implementation.

### Ranking

`RecallEngine` combines multiple signals, including semantic similarity, working-memory relevance, confidence/authority, activation and a temporal factor. This is a real multi-signal scoring design, but the underlying semantic input remains lexical in the deterministic provider. `CODE_VERIFIED`.

The separate `ranked_search.py` path can apply spreading activation over the multi-graph. However, the implementation calls `controller.search()` first and only then re-orders returned results. Therefore graph ranking cannot recover candidates that the controller's candidate generation omitted. `CODE_VERIFIED`.

### Critical observation

`RetrievalEngine.retrieve()` obtains `storage.query(...)`, truncates to a candidate limit, caches the truncated list, and returns it. There is no semantic candidate generation in this component. `CODE_VERIFIED`.

## Track B — REVIEW safety

The inspected `RecallEngine` explicitly pulls REVIEW notes as detached copies and tags them `_cognitive_unverified=True`. It does not perform lifecycle promotion. `CODE_VERIFIED`.

The public `read()` path remains ACTIVE-only, while `cognitive_read()` permits ACTIVE and REVIEW and tags REVIEW as unverified. `CODE_VERIFIED`.

This supports the invariant:

`REVIEW != ACTIVE`

However, a full end-to-end test that injects malicious REVIEW content and proves that a downstream agent/tool executor cannot treat that content as authority was not available. `UNVERIFIED`.

## Track C — Adversarial memory attacks

The existing security tests visibly cover principal authorization, lifecycle constraints, provenance restrictions, attestation boundaries, immutability and atomic rejection. They do not, from the inspected sections, establish a content-level prompt-injection defense at the retrieval-to-agent execution boundary.

Required distinction remains:

- memory retrievable as DATA: supported for REVIEW cognitive retrieval (`CODE_VERIFIED`);
- memory unable to become AUTHORITY: `UNVERIFIED`;
- memory unable to cause unauthorized action: `UNVERIFIED`.

No security invariant was weakened during this audit.

## Track D — Knowledge quality

The current inspected scoring model allows confidence and authority to influence ranking. `get_authority_score()` is a deterministic provenance mapping and is not persisted in frontmatter. `CODE_VERIFIED`.

The inspected code does not establish that source confidence, retrieval relevance confidence and answer correctness confidence are represented as independent calibrated quantities. `UNVERIFIED` / likely absent from the inspected path.

## Track E — Calibration

No independent evidence was established for ECE, Brier score, reliability diagrams or calibrated selective prediction on the current baseline.

Therefore:

`Calibration = UNVERIFIED`

A single numeric score should not be interpreted as simultaneously meaning source trust, retrieval relevance and answer correctness.

## Track F — Associative memory

The graph layer is real as an executable derived structure. `MultiGraphMemory` builds semantic, temporal, causal and entity graphs from note metadata/content. `CODE_VERIFIED`.

The spreading-activation engine is also real code and computes propagation over graph neighborhoods. `CODE_VERIFIED`.

The separate `ranked_search()` path can alter output order based on spreading activation. `CODE_VERIFIED`.

Important limitation: the graph re-ranking path is not the same as proving that the normal controller search path uses graph signals. `MemoryController.search()` itself uses `RetrievalEngine` and `RelevanceScorer`; graph re-ranking is a separate wrapper. `CODE_VERIFIED`.

The provided tests for `ranked_search` assert result presence/order tolerance, but the sampled tests do not prove a controlled experiment where enabling/disabling graph propagation changes ranking for a fixed candidate set. `UNVERIFIED` at behavioral level.

## Track G — Temporal memory

`RecallEngine` explicitly considers `valid_from`, `valid_until`, historical query terms, lifecycle penalties and supersession lineage. `CODE_VERIFIED`.

The implementation distinguishes historical queries from ordinary queries in its treatment of expired and superseded notes. `CODE_VERIFIED`.

End-to-end runtime proof across historical/current/contradictory test cases was not executed in this environment. `UNVERIFIED` for runtime behavior.

## Track H — Learning loop

`scripts/label_council_outcome.py` appends JSONL outcome telemetry and explicitly states that it does not modify canonical memory or enqueue/promote candidates. `CODE_VERIFIED`.

Therefore the inspected implementation does **not** demonstrate a closed loop:

`OUTCOME → EVIDENCE → MEMORY UPDATE → RETRIEVAL/RANKING → FUTURE OUTCOME`

Current classification:

**DEAD-END / PARTIAL** — outcome telemetry exists, but the inspected writer alone does not close the learning loop into retrieval or promotion. `CODE_VERIFIED`.

## Track I — Causal effectiveness

No current causal claim is accepted from architecture alone.

Required separation:

`MEMORY PRESENT != MEMORY RETRIEVED != MEMORY USED != MEMORY CAUSED OUTCOME`

A fresh controlled treatment/control/oracle execution was not performed here. Causal effectiveness is therefore `UNVERIFIED`.

## Track J — Cross-agent reconciliation

At this stage, no Codex/Antigravity/Perplexity artifacts have been accepted as independent proof. Their reports remain inputs for later reconciliation.

| Finding | Codex | Antigravity | Perplexity | Luna | Final status |
|---|---|---|---|---|---|
| Current baseline SHA | not independently supplied | not accepted | n/a | `9a663213...` | CONFIRMED |
| Retrieval is truly semantic | untrusted until evidence | pending | research only | lexical implementation observed | CONTRADICTED if claimed as full semantic |
| REVIEW remains unverified | must prove | must inspect | security guidance only | supported by code path | PARTIALLY CONFIRMED |
| Learning loop closes | not accepted without trace | n/a | research only | outcome writer is telemetry-only | REQUIRES NEW TEST |
| Graph logic affects runtime retrieval | not accepted without controlled run | architectural code shows possibility | n/a | separate re-ranker exists | PARTIALLY CONFIRMED |

## Track K — Independent score

These are **audit judgments at the current evidence state**, not product marketing scores.

| Dimension | Score / 10 | Basis |
|---|---:|---|
| Memory Foundation | 8.0 | strong controller/lifecycle/provenance foundation observed in code |
| Knowledge Quality | 5.0 | provenance/confidence exist; differentiation/calibration not proven |
| Retrieval Quality | 4.0 | real multi-signal scoring, but deterministic semantic provider is lexical |
| Cognitive/Associative Richness | 6.0 | executable activation and multi-graph components exist; normal-path effect not fully proven |
| Operational Usefulness | 7.0 | bounded search, context packing, progressive disclosure, lifecycle boundaries |
| Epistemic Safety | 7.5 | strong provenance/lifecycle/security baseline; retrieval-content poisoning boundary remains unproven |
| Calibration | 3.0 | explicit calibrated metrics/behavior not established |
| Temporal Reasoning | 6.0 | code paths exist; fresh runtime validation still needed |
| Learning Capability | 2.5 | telemetry exists, closed learning loop not established |
| Provenance | 8.5 | provenance is deeply integrated into policy and scoring |
| Overall Cognitive Maturity | 5.8 | functional memory foundation with important cognitive gaps |

## Final classification

**B — FUNCTIONAL MEMORY ENGINE WITH IMPORTANT GAPS**

This classification is provisional at the strict evidence boundary of this audit because full current-baseline runtime execution was unavailable. The inspected source supports a functional controller/memory foundation, real associative components and lifecycle controls, but does not support the stronger claim of a fully semantic, calibrated, causally learning cognitive memory engine.

## P0 findings

1. Current deterministic retrieval is lexical/Jaccard rather than embedding-semantic. `CODE_VERIFIED`.
2. Memory-content prompt-injection resistance at the retrieval-to-agent boundary is not proven. `UNVERIFIED`.
3. Current baseline CI is pending with zero reported status checks. `UNVERIFIED` for pass/fail.
4. Full current test suite result is not established for baseline `9a663213...`.

## P1 findings

1. Calibration is not established.
2. Outcome telemetry does not demonstrate a closed learning loop.
3. Graph/activation impact needs controlled A/B runtime proof.
4. Candidate generation precedes graph re-ranking, limiting graph-based recovery of omitted candidates.

## Re-test requirements

The next independent audit round should begin from the then-current `main` SHA and must include a fresh checkout/runtime environment with network-independent repository bytes available. Required evidence:

- exact `pytest -q` output;
- exact test discovery count;
- current CI run and job results;
- held-out retrieval tests not supplied by the implementation agent;
- memory poisoning tests at the retrieval-consuming boundary;
- controlled graph on/off ranking experiment;
- temporal/historical runtime cases;
- controlled learning-loop before/after experiment;
- causal memory experiment with MEMORY PRESENT / RETRIEVED / USED / CAUSED distinctions.

## Audit integrity

No production code was modified by this Luna audit.
No REVIEW note was promoted.
No security control was weakened.
No test was fabricated or locally reported as passed.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
