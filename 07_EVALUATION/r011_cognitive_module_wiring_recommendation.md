# r011 Cognitive Module Wiring: Empirical Audit & Recommendation

**Branch**: `r011/cognitive-module-wiring`  
**Date**: September 6, 2026  
**Auditor / Evaluation Lead**: ANTIGRAVITY  
**Status**: COMPLETED — RECOMMENDATION: KEEP UNWIRED  

---

## 1. Executive Summary & Core Verdict

**Recommendation**: **ALL FIVE COGNITIVE MODULES MUST STAY UNWIRED FROM THE PRODUCTION RETRIEVAL PATH (`MemoryController.search()`).**

Per the explicit task mandate:
> *"Do NOT wire them if r009 concluded that graph expansion does not help. An attention mechanism gating a candidate set that the graph did not improve is theatre that produces plausible metrics meaning nothing.*  
> *Acceptance: Either a measured improvement with the flag on, or a written recommendation that the module stay unwired, with the numbers that justify it."*

The precondition for wiring **fails decisively** on both architectural and empirical grounds:
1. **r009 Established Zero Graph Gain**: Task r009 demonstrated that graph expansion produced delta = 0.0000 recall improvement on `dev.json`, with edge traversals diluting precision. The official verdict of r009 was `enable_graph_expansion = False` (OFF by default).
2. **Empirical Attention Benchmark Yields delta = 0.0000**: Direct evaluation of `AttentionModel` scoring and re-ranking across the heldout retrieval benchmark (`dev.json`) produced identical accuracy (delta = 0.0000), while arbitrarily shuffling candidate ranks based on static metadata rather than semantic relevance.
3. **Global Production-Consumer Rule**: All candidate modules (`attention.py`, `global_workspace.py`, `executive.py`, `reasoning.py`, `working_memory.py`) have **zero production consumers** in the memory search engine.
4. **Architectural Mismatch**: The attention model relies on tick-based simulated recency and ACT-R utility learning, neither of which exists in stateless REST / CLI retrieval queries.

---

## 2. Precondition Audit: Global Production-Consumer Rule

A repository-wide AST/import scan was executed across all production Python modules:
```bash
grep -rl "<module>" --include='*.py' . | grep -v "/tests/\|test_\|benchmarks"
```

### Consumer Audit Matrix

| Module | Location | Search/Production Path Consumers | Other Vault Consumers | Architectural Status |
| :--- | :--- | :---: | :---: | :--- |
| **`global_workspace.py`** | `03_IMPLEMENTATION/packages/memory/` | **0** | 0 | Starved / Orphaned |
| **`attention.py`** | `03_IMPLEMENTATION/packages/memory/` | **0** | 0 (only standalone skills/tests) | Starved / Orphaned |
| **`executive.py`** | `03_IMPLEMENTATION/packages/memory/` | **0** | Local workspace sub-demos | Non-retrieval scope |
| **`reasoning.py`** | `03_IMPLEMENTATION/packages/memory/` | **0** | Local workspace sub-demos | Non-retrieval scope |
| **`working_memory.py`** | `03_IMPLEMENTATION/packages/memory/` | **0** | Local workspace sub-demos | Non-retrieval scope |

None of the five modules are integrated into the canonical retrieval or context assembly pipeline (`MemoryController`, `VaultIndex`, `ContextPackBuilder`). 

---

## 3. Empirical Evaluation on Benchmark (`dev.json`)

To verify whether attention-based gating could serve as a principled replacement for truncation, we executed `07_EVALUATION/r011_attention_wiring_evaluation.py` against the 10 answerable test cases in `07_EVALUATION/heldout_retrieval_benchmark_v1/dev.json`.

### Quantitative Results

| Metric | Baseline (`MemoryController.search`) | With `AttentionModel` Re-ranking | Delta |
| :--- | :---: | :---: | :---: |
| **Precision@5** | 0.0000 | 0.0000 | **+0.0000** |
| **Mean Reciprocal Rank (MRR)** | 0.0000 | 0.0000 | **+0.0000** |
| **Recall@5** | 0.0000 | 0.0000 | **+0.0000** |
| **Candidate Re-ordering Rate** | — | 50.0% (5 / 10 queries) | Shuffled |
| **Required Fact Extraction** | 0 / 10 | 0 / 10 | **+0** |

### Per-Query Diagnostic Breakdown

| Query ID | Benchmark Class | Baseline Top-2 | Attention Top-2 | Re-ordered? | Impact |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **D01** | `exact_identifier_lookup` | `knw-agent-memory-trace...`, `cat-skills...` | `knw-agent-memory-trace...`, `cat-skills...` | False | Identical |
| **D02** | `exact_identifier_lookup` | `cat-skills-251-master`, `knw-agent-memory...` | `cat-skills-251-master`, `knw-agent-memory...` | False | Identical |
| **D03** | `paraphrase` | `d001ebae...`, `c067b857...` | `cat-skills-251-master`, `d001ebae...` | **True** | No gain; gold note missed |
| **D04** | `paraphrase` | *(empty)* | *(empty)* | False | Identical |
| **D05** | `synonym_substitution` | `cat-skills-251-master`, `knw-retrieval-bottleneck...` | `cat-skills-251-master`, `knw-memory-usage...` | **True** | Swapped irrelevant notes |
| **D06** | `synonym_substitution` | `cat-skills-251-master`, `knw-agent-memory...` | `cat-skills-251-master`, `knw-agent-memory...` | False | Identical |
| **D07** | `lexical_trap` | `cat-skills-251-master`, `d001ebae...` | `cat-skills-251-master`, `8c7d5c90...` | **True** | Swapped distractor notes |
| **D08** | `lexical_trap` | `cat-skills-251-master`, `c067b857...` | `cat-skills-251-master`, `8c7d5c90...` | **True** | Swapped distractor notes |
| **D09** | `cross_cluster_multihop` | `cat-skills-251-master`, `d001ebae...` | `cat-skills-251-master`, `8c7d5c90...` | **True** | Swapped distractor notes |
| **D10** | `cross_cluster_multihop` | `cat-skills-251-master`, `knw-retrieval-bottleneck...` | `cat-skills-251-master`, `knw-retrieval-bottleneck...` | False | Identical |

### Findings from Empirical Run
1. In 5 out of 10 queries, `AttentionModel` reordered candidates solely because certain indexed notes carried a static string attribute `confidence: high` vs default `unknown`. 
2. Because the initial candidate set lacked the gold notes (which were in unindexed markdown files outside the default roots or required semantic expansion), re-ranking only shuffled irrelevant candidates.
3. Attention did **not** bridge the retrieval gap or improve fact coverage by even a single point.

---

## 4. Architectural Deficiencies & Incompatibilities

### A. Broken Module Coupling
In `03_IMPLEMENTATION/packages/memory/attention.py`:
```python
from .motivation import UtilityTracker
```
The sibling file `motivation.py` does not exist under `memory/`. It is located in `learning/motivation.py`. Importing `memory.attention` directly raises:
```text
ModuleNotFoundError: No module named 'memory.motivation'
```
While it resolves via the composite `cognitive_core` namespace shim, this reveals that `attention.py` is an unmaintained legacy artifact whose internal contracts were never normalized for independent package usage.

### B. Simulation Tick vs. Stateless Search Mismatch
`AttentionModel.calculate_score()` requires:
- `recency_tick: int`
- `current_tick: int`
- `action_type: str`
- `utility_tracker: UtilityTracker`

In a real production search request (e.g. `GET /memory/search?query=...` or `recall_cli`), there is no persistent simulation clock or action execution cycle. Forcing arbitrary tick values (e.g. `0`) reduces the recency calculation to a constant and renders utility weighting inoperable.

### C. Violation of the Prime Directive
The operating contract (`AGENTS.md`) dictates:
> *"Better memory beats more memory. Better routing beats more agents. Capability is cheap; loaded context is expensive."*

Adding an attention weighting layer on top of a static lexical retrieval set that has not been improved by graph topology is pure computational overhead. It adds CPU latency and false confidence without increasing factual density or relevance.

---

## 5. Conclusion & Actionable Verdict

| Requirement | Result |
| :--- | :--- |
| **Precondition: Measurable Gains from r009** | **FAILED** (r009 showed delta = 0.0000, graph expansion remains OFF) |
| **Precondition: Production Consumers Exist** | **FAILED** (0 consumers for attention, global workspace, executive) |
| **Empirical Evaluation Gain** | **delta = +0.0000** Precision@5, **delta = +0.0000** MRR |
| **Final Recommendation** | **KEEP ALL MODULES UNWIRED** |

The production retrieval pipeline in `MemoryController.search()` remains clean, robust, deterministic, and free of artificial cognitive scaffolding.
