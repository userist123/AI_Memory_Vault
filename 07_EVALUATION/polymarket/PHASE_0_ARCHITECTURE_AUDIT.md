# Polymarket Prediction System — Phase 0 Architecture Audit

**Audit branch:** `r046/polymarket-phase0-audit`

**Audit base / current main at start:** `a5e8935d4fed899daebbaf0a075d97249c967c8c`

**Scope:** architecture and evidence audit only. No implementation code was changed in this phase.

## Evidence policy

Evidence levels used by this audit:

1. executed test output
2. reproducible benchmark artifact
3. actual source/code behaviour
4. versioned evaluation report
5. documentation
6. commit message / branch name
7. agent-reported claim

Levels 6–7 are not treated as proof when a stronger level is available. Where this audit could not directly verify a runtime claim from the repository interface, it is marked `UNVERIFIED`.

## Repository state

- `main` exists and was observed at `a5e8935d4fed899daebbaf0a075d97249c967c8c`.
- The repository metadata identifies `main` as the default branch.
- `main` is not protected according to the branch metadata returned by GitHub (`protected: false`).
- A dedicated Phase 0 branch, `r046/polymarket-phase0-audit`, was created from that exact main SHA before audit work.
- The available GitHub workflow-run query returned no PR-triggered runs for the current main SHA. This does **not** prove that CI is absent or failing; it means current CI pass/fail for this SHA is `UNVERIFIED` from the connector evidence available here.

**Evidence:** current branch metadata and commit payload; evidence level 3/6 for repository state, level 3 for the branch creation action. Current CI result: `UNVERIFIED`.

---

# 1. What exists and is production-ready

### 1.1 Memory controller / canonical search path

`03_IMPLEMENTATION/packages/memory/controller.py` is the real controller implementation. `00_GOVERNANCE/VAULT_STATE.md` explicitly states that `memory_controller/` is a shim and the real controller is `03_IMPLEMENTATION/packages/memory/controller.py`, roughly 1000 lines, with `search()` at line 274.

`controller.py` imports and wires validation, provenance validation, lifecycle policy, cache, query classification, retrieval, relevance scoring, progressive disclosure, context budgeting, context-pack construction, and financial search components.

**Assessment:** production-path component, subject to the limitations documented below.

**Evidence:** `00_GOVERNANCE/VAULT_STATE.md`; `03_IMPLEMENTATION/packages/memory/controller.py`. Evidence level 3/5. The repository's own state document additionally says its claims are re-checked by `20_TESTS/test_vault_state_accuracy.py`, but that test was not executed through this audit interface; actual current pass status is `UNVERIFIED`.

### 1.2 Query-driven candidate generation

The production retrieval path now passes the actual query text into `generate_candidates()` after hard storage gates. `candidate_generation.py` explicitly states that the pre-r004 path never read the query and effectively returned first-N insertion order.

The candidate generator is deterministic, closed-set, and fail-closed. It reuses the existing BM25/entity primitives instead of introducing a second lexical scorer. `DEFAULT_CANDIDATE_LIMIT = 200`.

**Assessment:** real production capability; suitable as the retrieval front door for a Polymarket evidence query.

**Evidence:** `03_IMPLEMENTATION/packages/retrieval/context/retrieval.py`; `03_IMPLEMENTATION/packages/retrieval/context/candidate_generation.py`; `20_TESTS/regression/test_candidate_generation_call_path.py` referenced by the implementation. Evidence level 3; associated regression-test existence is level 3, current execution status `UNVERIFIED`.

### 1.3 Held-out benchmark v2

The v2 benchmark contract is the valid benchmark contract. It explicitly supersedes v1 and documents why v1 was invalid: gold IDs did not resolve in the corpus and the v1 scoring therefore produced structural zero recall.

`20_TESTS/test_benchmark_v2_gold_integrity.py` checks gold-reference resolvability, required facts, abstain/gold consistency, required query classes, graph class/runtime consistency, frozen hashes, line-ending normalization and deliberate tamper detection.

**Assessment:** production-grade *evaluation infrastructure* in the sense that the contract contains the necessary integrity controls. It is not evidence that retrieval quality is good.

**Evidence:** `07_EVALUATION/heldout_retrieval_benchmark_v2/CONTRACT.md`; `20_TESTS/test_benchmark_v2_gold_integrity.py`. Evidence level 3/4.

### 1.4 Lifecycle / mutation authority

`00_GOVERNANCE/VAULT_STATE.md` records `lifecycle/policy.py` as the sole lifecycle authority and states that 7/7 mutation paths are gated and AST-verified.

**Assessment:** existing authority that Polymarket learning must reuse rather than replicate.

**Evidence:** `00_GOVERNANCE/VAULT_STATE.md`; `03_IMPLEMENTATION/packages/lifecycle/policy.py`. Evidence level 3/5. Current test execution `UNVERIFIED`.

### 1.5 Provenance and evidence-bundle primitives

The vault already has:

- `03_IMPLEMENTATION/packages/lifecycle/validation/provenance.py` — requires `source_type` and `source_ref`.
- `03_IMPLEMENTATION/packages/memory/evidence_bundle.py` — builds immutable evidence snapshots with content hashes, source references, extraction dates, valid intervals, lifecycle and verification fields.
- `03_IMPLEMENTATION/packages/memory/evidence_verifier.py` — present in the production memory package.

**Assessment:** strong reusable foundation for Polymarket evidence provenance. It is not yet a Polymarket temporal evidence system by itself.

**Evidence:** files above. Evidence level 3.

### 1.6 Temporal memory capability

`03_IMPLEMENTATION/packages/lifecycle/temporal_controller.py` supports explicit `as_of` and `known_as_of` filtering. It filters on `valid_from` / `valid_until` and provenance `extraction_date`, performs lineage resolution, deterministic temporal ranking, conflict reporting and signed temporal pagination.

**Assessment:** highly relevant to anti-hindsight protection. This should be extended/reused, not reimplemented.

**Evidence:** `03_IMPLEMENTATION/packages/lifecycle/temporal_controller.py`. Evidence level 3.

### 1.7 Learning promotion gate

`03_IMPLEMENTATION/packages/lifecycle/learning_promotion_gate.py` requires authorized human/admin promotion, verified evidence, matching evidence hashes, no stale/missing memories, promotable confidence and a bound confidence snapshot.

**Assessment:** directly reusable as the eventual boundary for promoting post-resolution learning into the Vault.

**Evidence:** file itself. Evidence level 3.

### 1.8 Graph / attribution / plasticity primitives

The graph store is real. The vault documents graph counts and one-hop semantics. `graph/plasticity.py` defines an attribution-aware learning loop with five memory states, bounded weight changes, negative feedback, append-only telemetry and fail-closed behaviour.

However, the vault's own state document says graph plasticity is not wired into production and graph expansion is off by default.

**Assessment:** reusable research substrate, **not** a production Polymarket learning path yet.

**Evidence:** `03_IMPLEMENTATION/packages/graph/synapse_store.py`; `03_IMPLEMENTATION/packages/graph/plasticity.py`; `00_GOVERNANCE/VAULT_STATE.md`. Evidence level 3.

---

# 2. What exists but is experimental / not production-validated

### 2.1 Graph expansion

`MemoryController.search()` contains graph-expansion logic, but the documented production default is `enable_graph_expansion=False`. `VAULT_STATE.md` also notes a budget interaction: r004 raised the lexical candidate limit to 200 while the shipped graph budget contract was designed around a much smaller candidate window, resulting in zero effective expansion under the unchanged default.

**Assessment:** experimental / disabled by default.

### 2.2 R024 ranking arms

R024 WP-1 measured a strong dev-set ranking effect:

- baseline context recall: 0.000 (0/8)
- fused-score arm: 0.375 (3/8)
- candidate recall stayed 0.500 across arms

The report explicitly says the result cleared a pre-registered falsification bar and recommended `fused_score`, but also says the n=8 dev set is small and that heldout confirmation is required before flipping the production default.

**Assessment:** useful experiment, not by itself sufficient production evidence.

**Evidence:** `07_EVALUATION/r024_wp1_ranking/PHASE_A_ATTRIBUTION.md`; `07_EVALUATION/r024_wp1_ranking/PHASE_B_ARMS.md`. Evidence level 4.

### 2.3 Abstention

R025 WP-11 establishes that the previous abstain metrics were tautological and that there is **no production abstention mechanism**. The measured signals attempted for abstention were not separable; indeed, RRF rank fusion could assign high fused scores to all-zero raw-score ties.

**Assessment:** experimental diagnosis only. Do not reuse `fused_score` as if it were a calibrated confidence measure.

**Evidence:** `r025/abstention-metric/07_EVALUATION/r025_wp11_abstention/WP11_ABSTENTION_METRIC.md`. Evidence level 4.

### 2.4 Graph benchmark

R025 WP-12 found that most existing graph cases did not actually exercise graph traversal. Only two new cases could be verified end-to-end as genuine graph-expansion cases; the prior graph comparison therefore measures that the benchmark rarely reaches the graph mechanism, not a clean estimate that graph traversal itself is useless.

**Assessment:** benchmark weakness, not evidence for a useful Polymarket graph strategy.

**Evidence:** `r025/graph-expansion/07_EVALUATION/r025_wp12_graph_expansion/WP12_GRAPH_EXPANSION.md`. Evidence level 4.

---

# 3. What is missing for a serious Polymarket research system

The following are not established by the current repository evidence:

1. Canonical Polymarket market schema and immutable market snapshots.
2. Historical market-state replay.
3. Resolution-rule representation and resolution-risk scoring.
4. A prediction/forecast ledger tied to market snapshots.
5. Forecast probability calibration on held-out temporal data.
6. Market-implied probability versus calibrated model probability comparison.
7. Net-EV calculation after fees, spread, slippage and liquidity constraints.
8. A Polymarket-specific risk engine and position-sizing policy.
9. A real abstention policy with an independently validated signal.
10. Paper-trading execution simulation.
11. Historical reconstruction of the information set available at prediction time.
12. Polymarket-specific benchmark/ablation showing Memory Vault adds value over the market baseline.
13. A validated execution adapter.
14. A safe live-trading gate.

**Evidence level:** mostly 3/5 by absence of identified components in the audited production tree and documentation; because a full exhaustive file-by-file absence proof was not completed for every possible implementation, these are **`UNVERIFIED` as absolute repository-wide absence claims** and should be treated as “not found / not evidenced” rather than logical proof of nonexistence.

---

# 4. What must not be duplicated

| Need | Reuse existing component | Why |
|---|---|---|
| retrieval | `MemoryController` + `RetrievalEngine` | canonical query-driven path already exists |
| candidate generation | `candidate_generation.py` | already owns BM25/entity candidate generation |
| ranking | existing `RelevanceScorer` / ranking-arm mechanism | current ranking behaviour is measurable and already instrumented |
| provenance | lifecycle provenance validator | source fields already have a contract |
| temporal filtering | `TemporalMemoryController` | already models `as_of` / `known_as_of` |
| evidence snapshot | `evidence_bundle.py` | immutable evidence bundle + hashes already exists |
| evidence/promotion | `LearningPromotionGate` | already enforces human/admin promotion and evidence validity |
| graph storage | `SynapseStore` | canonical graph store already exists |
| attribution/plasticity | `graph/plasticity.py` | learning semantics already defined, albeit unwired |
| lifecycle | `lifecycle/policy.py` | documented sole lifecycle authority |
| security / signed pagination | existing authorizer + pagination token | already implemented |

**Evidence:** source files listed above and `00_GOVERNANCE/VAULT_STATE.md`. Evidence level 3/5.

---

# 5. Retrieval bottlenecks

There are at least three measured bottlenecks relevant to Polymarket.

### 5.1 Candidate generation is not the whole retrieval problem

The production path now generates up to 200 candidates using fused BM25/entity ranking. That solved the earlier “query text not read” failure mode, but downstream ordering still matters.

R024 WP-1 showed four dev-set losses at the disclosure stage, all `ranked_out`: gold candidates existed at positions 25, 152, 76 and 194 while `page_size=10`. The fused-score ranking arm recovered 3/8 context cases on the dev set versus 0/8 baseline.

### 5.2 `fused_score` must not be treated as probability/confidence

R025 WP-11 found that rank-based RRF can assign relatively high fused scores to queries with no real lexical/entity signal because fully tied zero-score rankings are still deterministically ordered. This makes `fused_score` unsuitable as a direct abstention/confidence measure.

### 5.3 Graph retrieval is not yet a dependable rescue path

Graph expansion is off by default and the benchmark evidence shows the current graph test class mostly does not cause the graph mechanism to be exercised.

**Implication for Polymarket:** the first evidence retriever should be built around the canonical query-driven path plus explicit temporal filtering and evidence hashing. A future graph layer should be additive and separately measured, not assumed to rescue weak lexical retrieval.

**Evidence:** R024 WP-1 reports; R025 WP-11; R025 WP-12; current retrieval implementation. Evidence levels 3/4.

---

# 6. Where calibration is absent

There is no evidence in the current audited main tree of a production Polymarket probability-calibration engine. The ontology scaffold itself identifies `confidence` as a slot with no validated module, while `RelevanceScorer` maps qualitative note confidence labels (`very_high`, `high`, etc.) into ranking numbers. That field is metadata confidence for memory notes, not event-probability calibration.

R025 WP-11 explicitly demonstrates that the retrieval `fused_score` is not a separable abstention signal and can be inverted by zero-score RRF tie behaviour.

Therefore there is currently no demonstrated component that converts forecast probabilities into calibrated event probabilities or tracks Brier/log loss/ECE by temporal holdout.

**Assessment:** calibration is a major missing layer and must be an explicit phase before any claim of trading edge.

**Evidence:** `01_ARCHITECTURE/ontology/ONTOLOGY_SPEC.md`; `03_IMPLEMENTATION/packages/retrieval/context/relevance_scoring.py`; R025 WP-11. Evidence levels 3/4.

---

# 7. Where temporal leakage can enter

### Evidence leakage

The temporal adapter provides `known_as_of` filtering based on provenance `extraction_date`, but not every source necessarily has that field populated or verified. A Polymarket system must require evidence timestamps/availability, not merely rely on document dates.

### Price leakage

No Polymarket market-snapshot mechanism has been evidenced. Historical forecasts could therefore accidentally consult current prices unless snapshots are created first-class.

### Resolution leakage

No Polymarket-specific resolution-rule / resolution-timestamp contract is currently evidenced. This is a direct risk for training or replay because resolved outcomes can contaminate forecast context.

### Memory leakage

The Vault supports bitemporal filtering, but a Polymarket system must make `known_as_of` a mandatory part of historical replay and evidence selection rather than an optional convenience.

### Benchmark leakage

The v2 benchmark has explicit freeze and integrity mechanisms. These should be reused as the model for Polymarket datasets.

**Assessment:** temporal memory infrastructure is a strong starting point, but the Polymarket boundary does not yet exist; leakage defense is therefore not complete.

**Evidence:** `temporal_controller.py`; v2 benchmark contract/test. Evidence levels 3/4.

---

# 8. Survivorship / selection bias risks

The current Vault evaluation history already demonstrates why this matters:

- graph results describe only a graph-reachable subpopulation, not the full corpus;
- graph cases can fail to exercise the intended mechanism;
- dev and heldout sets are separate and should remain so;
- abstention cases were previously scored tautologically;
- R025 documents that some seemingly good numbers were corrected downward after metric defects were discovered.

For Polymarket, additional mandatory controls are:

- preserve markets that were later resolved as losses, not just currently active markets;
- preserve delisted/cancelled/invalidated markets with explicit status;
- freeze evaluation windows before model tuning;
- avoid selecting only liquid or easy-to-source markets for evaluation unless that is explicitly the population being studied;
- keep market-class and time-regime coverage visible;
- never drop “no trade” opportunities from denominators when measuring selection quality.

**Current implementation:** not evidenced as Polymarket-specific. `UNVERIFIED`.

**Evidence supporting the risk model:** v2 benchmark contract; R025 WP-11; R025 WP-12. Evidence level 4.

---

# 9. R024 / R025 experiments — disposition for Polymarket work

## R024

### Already in main / incorporated

- Query-driven candidate generation and the production search path exist on current main.
- R024 ranking-arm infrastructure and measured reports are present in the current main tree.
- R024 WP-5 corpus-dilution work was merged into main, as indicated by the merge-base commit shown by GitHub.

**Status:** incorporated behaviour/evaluation infrastructure where visible on current main.

### Useful but not safe to blindly promote

- `fused_score` ranking result: useful because it identifies ranking as a real bottleneck, but the dev sample is small and R025 WP-11 demonstrates that the same RRF score must not be interpreted as confidence.
- graph conclusions: useful for caution, but R025 WP-12 shows the graph benchmark was mostly not exercising graph traversal.

### Superseded / invalid

- R024's old headline retrieval numbers that used the tautological abstention scoring are superseded by the corrected R025 WP-11 baseline (`0.7407` candidate recall, `0.1481` context recall on 27 measurable answerable heldout cases for graph-off).

## R025

### `r025/abstention-metric`

Useful diagnosis and metric repair. The branch report states no abstention signal was found and the metrics were corrected to `UNMEASURABLE` for unanswerable cases.

**Disposition:** port the *measurement discipline*, not the idea that a production abstention signal already exists.

### `r025/query-classifier`

Its branch commit documents a classifier-filter collapse caused by inferred lifecycle filters and two experimental softening arms that improved context recall from 10/37 to 15/37 on the frozen heldout+dev set without making a case worse. The branch is not the current main tip, so its code is historical/experimental relative to this audit.

**Disposition:** investigate and potentially port only after comparing the exact diff against current main and rerunning the heldout contract. Do not merge the branch wholesale.

### `r025/edge-promotion`

Historical experiment stream covering edge promotion, heldout validation, classifier work and graph expansion.

**Disposition:** evidence source; no direct branch merge.

### `r025/graph-expansion`

Historical graph-expansion work. WP-12's final report is especially important because it corrected the benchmark's understanding of what was actually being exercised.

**Disposition:** do not port graph behaviour into Polymarket retrieval until a genuine graph-on benchmark exists.

### `r025/a1-heldout-validation`

Contains heldout validation artifacts and abstention work.

**Disposition:** evidence source; validate against current main before reuse.

### Overall branch rule

GitHub branch existence or commit messages are not sufficient to declare an R025 behaviour integrated. The current main tree and current test/evaluation output are the authority.

**Evidence:** current main `VAULT_STATE.md`; R024 reports; R025 reports; branch commit metadata. Evidence levels 3/4/6. Full current-main re-execution of all R025 experiments is `UNVERIFIED`.

---

# 10. Minimum viable architecture for the first measurable Polymarket result

The smallest useful first result should **not** be live trading and should **not** be the full twelve-phase system.

The minimum measurable slice is:

```text
Historical market snapshot
        ↓
Canonical question + resolution rules
        ↓
Temporal evidence retrieval from Vault
        ↓
Forecast probability
        ↓
Held-out comparison against market probability
        ↓
Calibration metrics
        ↓
No-trade / candidate-opportunity classification
```

More concretely, the first measurable architecture needs only:

1. canonical market + timestamp snapshot;
2. historical evidence query using the existing retrieval stack;
3. `known_as_of` enforcement;
4. a prediction record with probability + evidence IDs + timestamps;
5. a market-price baseline;
6. Brier/log-loss/calibration evaluation;
7. a frozen temporal heldout dataset;
8. a baseline-versus-memory-enhanced ablation.

The first success criterion should be:

> Does Vault-informed forecasting improve held-out probability quality or economically meaningful net edge versus the market-implied baseline?

Not:

> Does the bot make money on a small backtest?

### Postpone

Postpone until the first measurable result exists:

- live wallet execution;
- automatic order placement;
- aggressive Kelly sizing;
- graph/plasticity learning in production;
- broad multi-agent prediction councils;
- dashboards beyond basic research reports;
- cross-category generalized models;
- autonomous memory promotion from trading outcomes.

These features increase complexity and/or overfitting risk before there is evidence of a predictive signal.

---

# Phase 0 conclusion

The Memory Vault already contains most of the **memory-side substrate** needed for a serious Polymarket research system: canonical retrieval, candidate generation, provenance, evidence bundles, temporal filtering, lifecycle governance, graph storage, attribution and learning-promotion controls.

What it does **not** yet demonstrate is the **prediction-side substrate** that turns those capabilities into a measurable forecasting edge: market snapshots, resolution semantics, probability calibration, temporal replay, prediction ledger, market-vs-model comparison, execution realism and Polymarket-specific heldout evaluation.

The most important architectural constraint is that the first Polymarket phase must reuse the existing temporal/evidence/retrieval path rather than build a second memory stack. The most important evaluation constraint is that `fused_score` must never be treated as calibrated confidence, and current graph behaviour must not be assumed to provide incremental retrieval value.

## Acceptance status

All ten required audit questions have either a cited repository artifact/test/report or an explicit `UNVERIFIED` designation. Phase 0 implementation code was not changed.

## Single smallest next slice

**Phase 1: canonical Polymarket market model + immutable timestamped snapshots.**

Reason: every later prediction, replay, calibration and anti-leakage measurement depends on having a trustworthy historical market-state boundary first. Do not build the prediction council, calibration engine, trading engine or live execution before this contract exists and is tested.
