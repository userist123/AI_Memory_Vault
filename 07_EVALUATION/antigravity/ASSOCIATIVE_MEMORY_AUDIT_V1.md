# Associative Memory & Spreading Activation Audit V1 (Antigravity Observability)

**Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
**Observability Agent**: Antigravity  
**Date**: 2026-09-04  
**Status**: `CODE_VERIFIED` / `TEST_VERIFIED`  

---

## 1. Executive Architectural Audit

We audited the associative memory implementation across:
- `cognitive_core/multi_graph.py` [`MultiGraphMemory`]
- `cognitive_core/spreading_activation.py` [`SpreadingActivationEngine`]
- `cognitive_core/activation.py` [`ActivationEngine`, `ActivationTracker`]
- `cognitive_core/recall.py` [`RecallEngine`]
- `memory_controller/controller.py` [`MemoryController.search()`]

---

## 2. Five Core Architectural Determinations

### Q1. Does graph structure affect runtime retrieval?

* **CURRENT STATE**: **NO** in production API; **PARTIAL** in experimental cognitive pipeline.
  - In `MemoryController.search()`, graph edges are not loaded or traversed. Retrieval uses `QueryClassifier` $\to$ `RetrievalEngine.retrieve()` (category/tag query) $\to$ `RelevanceScorer.score()` (token overlap).
  - In `cognitive_core/executive.py:212`, `ActivationEngine.activate_from_query()` traverses `SynapticGraph` (extracting `[[wiki-links]]` from note content) via BFS up to `max_depth=3`. However, `multi_graph.py`'s 4 orthogonal graphs (`semantic`, `temporal`, `causal`, `entity`) are completely detached from runtime search.
* **MISSING MECHANISM**: Integration bridge between `MultiGraphMemory` and `RetrievalEngine`.
* **WHY REQUIRED**: True graph-augmented retrieval requires edge-weight propagation to boost multi-hop associations that lack direct keyword overlap.
* **PROPOSED DESIGN**: Pass pre-compiled `MultiGraphMemory` into `RetrievalEngine` to expand the initial candidate set by 1 hop along `causal` and `semantic` edges.
* **TRADE-OFFS**: Graph expansion adds 15-30ms retrieval latency and increases context token usage, risking budget overflow (`Council_Context_Budget.md` limit: `MAX_GRAPH_EXPANSION = 1 hop`).
* **MEASUREMENT PLAN**: Run `RetrievalChallengeV1` comparing Precision@K with and without 1-hop expansion.

---

### Q2. Does spreading activation change ranking?

* **CURRENT STATE**: **YES** in `RecallEngine`; **NO** in `MemoryController.search()`.
  - In `RecallEngine.recall()`, activation is assigned a weight of `0.25`:
    $$\text{final\_score} = (0.35 \times \text{sim}) + (0.15 \times \text{wm}) + (0.15 \times \text{conf}) + (0.25 \times \text{activation}) + (0.10 \times \text{temp})$$
    When a note has prior access history, its ACT-R base-level activation boosts its final score by up to $+0.25$.
  - However, in `SpreadingActivationEngine` (`spreading_activation.py`), the decay propagation formula:
    $$\text{propagated} = \text{score} \times (\text{decay}^{\text{hop}+1})$$
    is only invoked in unit test `cognitive_core/tests/test_multi_graph.py`. It is never called during live user query retrieval.
* **MISSING MECHANISM**: Runtime wiring of `SpreadingActivationEngine.rank()` into `MemoryController.search()`.
* **WHY REQUIRED**: Recency and frequency of concept access should naturally prime related knowledge chunks.
* **PROPOSED DESIGN**: Allow `ActivationTracker` to feed seed access frequencies into `SpreadingActivationEngine` when `activation_mode="act_r"` is enabled.
* **TRADE-OFFS**: Cache poisoning risk if runaway queries artificially inflate non-relevant cluster activations.
* **MEASUREMENT PLAN**: Benchmark repeated-query priming on 10 consecutive coding tasks in `test_memory_ablation.py`.

---

### Q3. Is multi-hop real?

* **CURRENT STATE**: **STRUCTURAL IN TEST SUITES; DORMANT IN PRODUCTION RUNTIME.**
  - `SynapticGraph.extract_synapses()` and `MultiGraphMemory.add_edge()` structurally support multi-hop traversals.
  - Tests (`test_multi_graph.py:61`) verify 2-hop propagation ($A \to B \to C$).
  - However, in production agent loops (`RealAgentExecutionHarness.execute()`), queries are single-hop text searches via `controller.search()`. No second-hop expansion is performed.
* **MISSING MECHANISM**: Automated multi-hop expansion step between candidate retrieval and synthesis.
* **WHY REQUIRED**: Complex queries (e.g. "How does circuit breaker failure threshold interact with bulkhead concurrency limits?") require connecting two disjoint notes linked through an intermediate design pattern note.
* **PROPOSED DESIGN**: Implement an explicit `1-hop` link expander in `ContextPackBuilder` that appends neighbor summaries up to a strict 200-token ceiling.
* **TRADE-OFFS**: May exceed `MAX_SPECIALIST_OUTPUT = 600 tokens` if fan-out is high.
* **MEASUREMENT PLAN**: Multi-hop Q&A evaluation using `Q07_MULTIHOP_COUNCIL_SYNTHESIS` fixture.

---

### Q4. Are memory types operational or structural only?

* **CURRENT STATE**: **STRUCTURAL IN METADATA; MINIMALLY OPERATIONAL IN RETRIEVAL.**
  - Memory types (`fact`, `decision`, `procedure`, `lesson`, `task`, `intent`, `tool`, `failure`, `correction`, `outcome`) are strictly validated in `multi_graph.py` (`CONTROLLED_NODE_TYPES`).
  - In `MemoryController.query()`, notes can be filtered by `types=[...]`.
  - In `QueryClassifier`, query intent is mapped to target types.
  - However, the scoring formula in `RelevanceScorer` and `RecallEngine` treats all types identically (a `lesson` is scored using the exact same weights as a `tool` or `fact`).
* **MISSING MECHANISM**: Type-specific scoring weights (e.g. boosting `procedure` for "how to" queries, boosting `failure` for error diagnosis).
* **WHY REQUIRED**: A user asking "How to configure WAL mode?" needs procedures, not historical experience reflections.
* **PROPOSED DESIGN**: Dynamically scale `weights["semantic"]` and `weights["authority"]` based on intent-to-type alignment.
* **TRADE-OFFS**: Risk of over-filtering if classifier misidentifies intent.
* **MEASUREMENT PLAN**: Measure Type-Recall@3 on 20 intent-classified procedural queries.

---

### Q5. Can measurable effects be demonstrated?

* **CURRENT STATE**: **DEMONSTRATED IN ABLATION 01 (TASK SUCCESS RATE +10.0 pp).**
  - In the real Ollama `qwen2.5-coder:3b` benchmark (`07_EVALUATION/memory_ablation_2026-09.md`):
    - Control (zero memory): 25.0% success rate (5/20)
    - Treatment (retrieved memory): 35.0% success rate (7/20)
    - Absolute delta: $+10.0$ percentage points ($+40.0\%$ relative improvement).
  - Demonstrates empirical effectiveness under tested conditions.
  - However, this was achieved via basic top-$k$ text injection, NOT via graph spreading activation.
* **MISSING MECHANISM**: Ablation experiment comparing `top-k text search` vs `graph-spreading augmented retrieval`.
* **WHY REQUIRED**: To prove whether graph complexity actually justifies its runtime overhead.
* **PROPOSED DESIGN**: Implement `GRAPH_ABLATION_01` comparing standard `MemoryController.search()` vs `SpreadingActivationEngine.rank()`.
* **TRADE-OFFS**: Additional benchmark execution time (~120s on local LLM).
* **MEASUREMENT PLAN**: Paired benchmark across the 20 canonical ablation tasks.
