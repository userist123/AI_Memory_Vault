# A7 — Associative Differential Trace & Score Causality Report

**Evaluation Lane**: `ANTIGRAVITY` (Developer Observability & Architecture Inspection)  
**Round**: `R001`  
**Dispatch Reference**: `09_COORDINATION/dispatch/ANTIGRAVITY_R001_NEXT_A7.md`  
**Baseline `main` SHA**: `2c3126876cbe579afb227615b4b6c4d4048b6d42`  
**Working Branch**: `antigravity/observability-v1`  
**Epistemic Standard**: Strict separation of `RUNTIME_VERIFIED` (empirical trace/test execution) from `CODE_VERIFIED` (static AST inspection) and `UNAVAILABLE` (missing signals).

---

## 1. Executive Summary & Dual-Reality Determination

This report fulfills the dispatch requirements of **A7 (Associative Differential Trace)**. By constructing a controlled differential laboratory across a fixed 12-note representative corpus and 6 query archetypes, we evaluated the empirical behavior of the associative re-ranking layer (`cognitive_core/ranked_search.py` + `cognitive_core/spreading_activation.py`) versus the base controller retrieval (`memory_controller/controller.py`).

```
                              ┌──────────────────────────────────────────────────────────┐
                              │                 THE DUAL-REALITY GAP                    │
                              └──────────────────────────────────────────────────────────┘

     [CONTROLLED IN-MEMORY CORPUS]                                   [PRODUCTION RUNTIME (SQLite / Files)]
   • MultiGraphMemory built successfully                          • ranked_search calls controller.storage.store
   • Spreading activation propagates                              • Neither SQLite nor File engine has .store
   • Rank deltas observed (τ = 0.80 - 1.00, ρ = 0.50 - 0.90)       • AttributeError raised inside build_multi_graph
   • Edge-weight preservation alters rank (τ = 0.77 - 0.91)       • SILENTLY CAUGHT by except Exception:
   • Unretrieved graph neighbors pulled into Top-10              • Returns UNRANKED results[:top_k] (100% bypass)
                    │                                                               │
                    ▼                                                               ▼
        [BEHAVIORALLY OPERATIONAL]                                       [BEHAVIORALLY INEFFECTIVE]
        (Under isolated test runner)                                    (In real production environment)
```

### Core Empirical Findings:
1. **Production Runtime Disconnect**: `ranked_search.py` invokes `controller.storage.store.values()`. Neither `SQLiteStorageEngine` nor `FileStorageEngine` possesses a `store` attribute. Consequently, in 100% of real production or CLI queries, `ranked_search()` raises `AttributeError: 'SQLiteStorageEngine' object has no attribute 'store'`, which is silently swallowed by `except Exception: return results[:top_k]`. Spreading activation is **structurally present but behaviorally dead in production**.
2. **Relevance Score Discard / Synthetic Seeding**: `ContextPackBuilder.build()` strips the `relevance_score` calculated by `RelevanceScorer`. Downstream consumers (including `ranked_search.py`) receive zero score visibility and are forced to invent synthetic weights (`base_scores = {nid: 1.0 / (idx + 1)}`).
3. **Edge-Weight Overwrite Impact**: In baseline `spreading_activation.py` line 38, `propagated` overwrites line 37, completely discarding edge weight magnitude. In controlled comparison against Codex's weight-preserving formula, edge weight preservation produces a measurable secondary rank shift ($\tau = 0.7778 - 0.9111$, $\rho = 0.50 - 0.90$).
4. **Neighbor Expansion Reachability**: In the controlled environment, associative spreading activation successfully pulls unretrieved notes (e.g. `SEC-002`, `SEC-003`, `SEC-006`) into the top-10 result set, demonstrating graph reachability benefits when operational.
5. **GAP-010 Discovery (Classifier Substring Trap)**: `QueryClassifier` checks if `"verified" in query.lower()`. Queries with `"unverified"` trigger this substring match, forcing `lifecycle_filters=['VERIFIED']` and dropping all unverified/review candidate notes.

---

## 2. Signal Availability Matrix

To prevent misleading claims of capability, all retrieval signals are explicitly classified:

| Signal Name | Implementation Module | Observed Runtime Status | Evidence Classification |
|---|---|---|---|
| **Lexical Token Overlap** | `memory_controller/context/relevance_scoring.py` | `ACTIVE` | `RUNTIME_VERIFIED` |
| **Reciprocal Rank Seed (`1/(i+1)`)** | `cognitive_core/ranked_search.py` | `ACTIVE` (synthetic) | `RUNTIME_VERIFIED` |
| **Spreading Activation Hop Decay** | `cognitive_core/spreading_activation.py` | `ACTIVE` (test only) | `RUNTIME_VERIFIED` |
| **Semantic Graph Propagation** | `cognitive_core/multi_graph.py` | `ACTIVE` (test only) | `RUNTIME_VERIFIED` |
| **Temporal Graph Propagation** | `cognitive_core/multi_graph.py` | `ACTIVE` (test only) | `RUNTIME_VERIFIED` |
| **Causal Graph Propagation** | `cognitive_core/multi_graph.py` | `ACTIVE` (test only) | `RUNTIME_VERIFIED` |
| **Entity Graph Propagation** | `cognitive_core/multi_graph.py` | `ACTIVE` (test only) | `RUNTIME_VERIFIED` |
| **Production SQLite Graph Construction** | `cognitive_core/ranked_search.py` | `UNAVAILABLE` (`.store` missing) | `RUNTIME_VERIFIED` (Failure) |
| **Production File Engine Graph Construction** | `cognitive_core/ranked_search.py` | `UNAVAILABLE` (`.store` missing) | `RUNTIME_VERIFIED` (Failure) |
| **Dense Vector Similarity** | `cognitive_core/semantic.py` | `UNAVAILABLE` (lexical Jaccard mock) | `CODE_VERIFIED` |
| **Relevance Score in Context Pack** | `memory_controller/context/pack_builder.py` | `UNAVAILABLE` (stripped by pack) | `RUNTIME_VERIFIED` |
| **Edge Weight in Baseline Activation** | `cognitive_core/spreading_activation.py` | `UNAVAILABLE` (overwritten on L38) | `CODE_VERIFIED` |

---

## 3. Production Storage Connectivity Audit

We executed an automated test across all three storage engine types to verify whether `build_multi_graph(controller)` can instantiate a `MultiGraphMemory`:

```python
# Execution command:
# python scratch/run_a7_differential_trace.py
```

### Audit Results:

```json
{
  "in_memory": {
    "supported": true,
    "error": null,
    "nodes": 1
  },
  "file_engine": {
    "supported": false,
    "error": "AttributeError: 'FileStorageEngine' object has no attribute 'store'",
    "nodes": 0
  },
  "sqlite_engine": {
    "supported": false,
    "error": "AttributeError: 'SQLiteStorageEngine' object has no attribute 'store'",
    "nodes": 0
  }
}
```

### Architectural Diagnosis:
`build_multi_graph()` is hardcoded to `controller.storage.store.values()`. Only the testing dummy `StorageEngine` in `memory_controller/controller.py` defines `self.store: Dict[str, Dict]`. Both production engines store notes in SQLite tables or disk files and implement `.query()`, `.get()`, etc., but do not expose a `.store` attribute. Because `ranked_search()` catches `Exception` and returns `results[:top_k]`, this failure is completely silent to end users and agents.

---

## 4. Controlled A/B Differential Experiment

### 4.1 Methodology & Experimental Setup
- **Fixed Representative Corpus**: 12 canonical notes spanning 3 domains (`architecture`, `secops`, `procedure`), with controlled tags, explicit causal relations (`causes`, `replaces`, `leads_to`, `depends_on`), created timestamps, and capitalized entities.
- **Conditions Tested**:
  1. `BASE`: Pure `MemoryController.search()` (lexical token overlap + confidence).
  2. `BASELINE_ACTIVATION`: `SpreadingActivationEngine` with decay $0.6$, max hops $2$, and edge weight overwritten (current baseline code).
  3. `WEIGHT_PRESERVED_ACTIVATION`: `WeightPreservingSpreadingActivationEngine` with decay $0.6$, max hops $2$, and edge weight preserved (Codex proposed fix).
  4. `GRAPH_ABLATION`: Single-graph isolated activation across `semantic` ($1.0$), `temporal` ($0.5$), `causal` ($0.8$), and `entity` ($0.6$).

### 4.2 Differential Summary Table

| Query Archetype | Query String | Base Top-1 | Act Top-1 | Preserved Top-1 | Kendall $\tau$ (Base vs Act) | Spearman $\rho$ (Base vs Act) | Mean Rank Delta | Kendall $\tau$ (Act vs Preserved) |
|---|---|---|---|---|---|---|---|---|
| **Q1: Exact Durability** | `sqlite wal transaction durability` | `SEC-001` | `SEC-001` | `SEC-001` | **0.8000** | **0.6000** | **0.80** | **0.9111** |
| **Q2: Multi-Hop Relations** | `MemoryController architecture and attestation dependencies` | `KNOW-002` | `KNOW-002` | `KNOW-002` | **0.8000** | **0.8500** | **0.60** | **0.9111** |
| **Q3: Entity Forensics** | `Hardware telemetry device serial and SHA-256 audit` | `SEC-004` | `SEC-004` | `SEC-004` | **1.0000** | **0.7500** | **0.60** | **1.0000** |
| **Q4: Lexical Trap** | `sqlite python database testing` | `SEC-001` | `SEC-001` | `SEC-001` | **0.8000** | **0.5000** | **0.80** | **0.7778** |
| **Q5: Lifecycle Review** | `speculative hardware serial review draft` | `SEC-006` | `SEC-006` | `SEC-006` | **1.0000** | **1.0000** | **0.00** | **1.0000** |
| **Q6: Historical Superseded** | `legacy autocommit database operations rollback` | `SEC-003` | `SEC-003` | `SEC-003` | **0.8000** | **0.9000** | **0.40** | **0.8667** |

---

## 5. Detailed Query Walkthroughs

### 5.1 Q1: Exact Durability (`sqlite wal transaction durability`)

- **Candidate Reachability**: Base returned 5 notes. Activation expanded the candidate set to 10 notes by pulling graph neighbors: `SEC-002` (atomic transactions), `SEC-003` (legacy autocommit), `KNOW-003` (spreading activation), `KNOW-001` (Python sqlite tutorial), `PROC-002` (progressive disclosure).
- **Rank Shifts**:
  - `SEC-005` (SHA-256 audit log): Base rank 3 $\to$ Baseline Act rank 5 $\to$ Weight Preserved rank 3.
  - `SEC-002` (Atomic transactions): Not in Base top-5 $\to$ Baseline Act rank 3 $\to$ Weight Preserved rank 4.
- **Score Causality**:
  - `SEC-001`: True lexical relevance score = $0.9750$. Reciprocal rank seed = $1.0000$. Activation boost = $+2.9000$. Final combined score = $3.9000$.
  - In baseline activation, `SEC-002` received $+1.3800$ boost purely from hop count decay. Under weight preservation, its weight $1.0$ adjusted the boost to $+1.2800$.

### 5.2 Q2: Multi-Hop Relations (`MemoryController architecture and attestation dependencies`)

- **Candidate Reachability**: Base returned `[KNOW-002, SEC-001, SEC-004, KNOW-003, PROC-002]`.
- **Rank Inversions**:
  - `KNOW-003` (Spreading Activation dynamics): Base rank 4 (score $0.2500$) $\to$ Jumped to rank 3 in both Baseline and Weight Preserved activation (scores $1.7800$ and $1.4800$), surpassing `SEC-004` due to explicit `depends_on` causal and semantic links to `KNOW-002`.
- **Weight Preserved vs Baseline**: Kendall $\tau = 0.9111$. `SEC-001` received $+1.55$ boost in baseline vs $+1.45$ in preserved, reflecting the difference between unweighted decay and weighted edge propagation.

### 5.3 Q4: Lexical Trap (`sqlite python database testing`)

- **The Lexical Trap**: `KNOW-001` is a general Python tutorial note matching all query words. In Base search, `SEC-001` and `KNOW-001` both scored high ($0.9750$ and $0.9000$).
- **Graph Infiltration**: Graph activation pulled `SEC-002` and `PROC-001` into the top-4, pushing `SEC-005` down from rank 4 to rank 7 in baseline activation.
- **Weight Preserved Impact**: Kendall $\tau$ between Baseline and Weight-Preserved was **0.7778** (the largest divergence in the suite). `KNOW-002` dropped from rank 5 ($1.188$) to rank 7 ($0.888$) when edge weights were factored in.

---

## 6. Investigation of Edge-Weight Overwrite (Task 7)

### Code Inspection:
In `cognitive_core/spreading_activation.py` (lines 35-38):
```python
for neighbor, attrs in graph.neighbors(node_id):
    weight = float(attrs.get("weight", 1.0))
    propagated = score * self.decay * min(weight, 3.0) / 3.0 if weight > 3 else score * self.decay * (weight if weight <= 1 else 1.0)
    propagated = score * (self.decay ** (hop + 1))  # <--- OVERWRITE!
```

### Observability Impact Analysis:
1. **Line 37** executes a well-crafted saturation and normalization function:
   $$\text{effective\_weight} = \begin{cases} \frac{\min(w, 3.0)}{3.0} & \text{if } w > 3 \\ w & \text{if } w \le 1 \\ 1.0 & \text{otherwise} \end{cases}$$
2. **Line 38** immediately reassigns `propagated = score * (decay ** (hop + 1))`.
3. **Behavioral Effect**:
   - The edge attribute `weight` (which encodes tag overlap count in semantic graph and shared entity count in entity graph) is **100% neutralized**.
   - Spreading activation behaves identically whether two notes share 1 tag or 50 tags.
   - Our empirical test proves that repairing this overwrite changes ranking significantly (Kendall $\tau$ down to $0.7778$ in Q4).

---

## 7. Discovery of GAP-010: QueryClassifier Substring Trap

During the execution of Q5 (`speculative hardware serial review draft`), we investigated why an earlier query (`speculative hardware serial unverified draft`) returned zero results:

1. In `memory_controller/context/query_classifier.py` lines 53-56:
   ```python
   for stage in ["raw", "classified", "normalized", "review", "verified", "active", "superseded", "archived"]:
       if stage in lowered:
           lifecycle_filters.append(stage.upper())
   ```
2. When the user searches for an unverified note using the word `"unverified"`, `if "verified" in "unverified"` evaluates to `True`!
3. The classifier sets `lifecycle_filters = ['VERIFIED']`.
4. The storage engine filters strictly for `lifecycle == 'VERIFIED'`.
5. All unverified notes (`REVIEW` or `RAW`) are completely excluded!
6. This represents an inverted security / retrieval failure: **searching for unverified memories guarantees they cannot be found**.

---

## 8. Exit Condition Evaluation & Recommendations

### Evaluation Against Exit Criteria:
- **Criterion A**: *Graph/activation materially changes ranking and relevance under controlled comparison.*
  - **Verdict**: **SATISFIED (Controlled)**. Spreading activation produces rank shifts of $\tau = 0.80 - 1.00$, $\rho = 0.50 - 0.90$, expands candidate sets by including high-relevance unretrieved graph neighbors, and prioritizes multi-hop dependencies. Preserving edge weights introduces an additional $\tau = 0.7778 - 0.9111$ rank divergence.
- **Criterion B**: *The mechanism is shown to be structurally present but behaviorally ineffective/unproven.*
  - **Verdict**: **CONFIRMED (Production Runtime)**. In the real repository environment with SQLite or FileStorageEngine, `ranked_search.py` crashes on `.store` access and falls back to base ranking, rendering spreading activation 100% inoperative in production.

### Actionable Next Recommendations for Parallel Lanes:
1. **For CODEX (Build Lane)**:
   - Provide an abstraction for graph construction that queries storage notes via public methods (e.g. `storage.query(intent="all")` or iterating known notes) instead of accessing private/missing `.store`.
   - Restore `relevance_score` into the returned `ContextPackBuilder` result dictionary so downstream re-rankers do not have to invent reciprocal rank seeds.
   - Fix `QueryClassifier` keyword tokenization using regex word boundaries (`r"\b" + stage + r"\b"`) to prevent `"unverified"` from matching `"verified"`.
2. **For LUNA (Audit Lane)**:
   - Use `scratch/run_a7_differential_trace.py` and `telemetry/retrieval_traces/a7_differential_trace.json` to verify the exact rank deltas and prove that production SQLite calls fail closed to base ranking.
   - Attack `QueryClassifier` with adversarial substring traps (`unverified`, `inactive`, `non-archived`).
3. **For PERPLEXITY (Research Lane)**:
   - Provide standard academic formulations for fusing lexical BM25/Jaccard scores with spreading activation (e.g. CombMNZ or RRF - Reciprocal Rank Fusion) instead of raw sum of reciprocal rank + decay.

---

## 9. Artifact Handoff & Epistemic Sign-Off

- **Baseline SHA**: `2c3126876cbe579afb227615b4b6c4d4048b6d42`
- **Output Artifact**: `07_EVALUATION/antigravity/A7_ASSOCIATIVE_DIFFERENTIAL_TRACE.md`
- **Machine-Readable Trace**: `telemetry/retrieval_traces/a7_differential_trace.json`
- **Runner Script**: `scratch/run_a7_differential_trace.py`
- **Status**: `RUNTIME_VERIFIED`
