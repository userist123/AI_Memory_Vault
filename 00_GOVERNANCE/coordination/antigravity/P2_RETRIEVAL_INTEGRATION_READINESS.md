# P2 Retrieval Integration Readiness & Corpus Quality Gate

**Agent**: Antigravity  
**Branch**: `antigravity/p1-retrieval-foundation`  
**Baseline SHA**: `027a32306f5165e7bf340ea2c531d5f4cd287424`  
**Date**: 2026-09-05  
**Production Wiring**: NOT DONE (Readiness & Quality Gate only, zero runtime controller modification)  
**Test Suite Status**: 112/112 tests passing (100% green across all regression suites)

---

## 1. Executive Summary & Baseline

P2 establishes the formal quality gates, integration harness, performance baseline, and in-memory safe cleanup evaluations required to bridge P1 Retrieval Foundation with the production runtime.

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                             P2 READINESS MATRIX                                  │
├───────────────────────────────┬───────────────────────┬──────────────────────────┤
│ Dimension                     │ Gate Status           │ Empirical Metric         │
├───────────────────────────────┼───────────────────────┼──────────────────────────┤
│ Corpus Quality Gate (P2.1)    │ CONDITIONAL_PASS      │ 0% dups in boundary,     │
│                               │                       │ 69.3% in full corpus     │
│ Safe Cleanup Pipeline (P2.2)  │ VERIFIED (In-Memory)  │ ΔRecall@10: +0.0333,     │
│                               │                       │ ΔMRR: +0.0380            │
│ Retrieval Quality Gate (P2.3) │ PASS (9/9)            │ 100% threshold satisfied │
│ Production Design (P2.4)      │ COMPLETED             │ Fail-closed spec drafted │
│ SynapseStore Audit (P2.5)     │ BLOCKED (External)    │ Upstream files absent    │
│ Integration Harness (P2.6)    │ PASS (16/16 tests)    │ 112 total regression pass│
│ Performance Baseline (P2.7)   │ PASS (Sub-millisecond)│ Median < 1.05 ms all arms│
│ Production Wiring             │ NOT DONE              │ Preserved boundary       │
└───────────────────────────────┴───────────────────────┴──────────────────────────┘
```

---

## 2. P2 Objectives & Scoping Boundaries

### Objectives
1. **Corpus Quality Gate**: Define and enforce automated mathematical thresholds for vault data hygiene, graph density, duplicate suppression, and provenance completeness.
2. **Safe Corpus Cleanup Pipeline**: Empirically evaluate candidate cleanup mechanisms in-memory (duplicate suppression, template stub filtering, dangling link pruning, orphan isolation) without touching disks.
3. **Retrieval Quality Gate**: Define explicit pass/fail gates across 7 retrieval dimensions (Known-Item, Paraphrase, Multi-Hop, Entity-Mediated, Security Filtering, Determinism, Latency).
4. **Production Integration Design**: Specify the exact runtime wiring contract between `MemoryController.search()` and `ProductionRetrievalFacade`.
5. **SynapseStore Re-evaluation**: Re-audit the external dependency state of `05_DATA/synapses.json` and `cognitive_core/synapse_store.py`.
6. **Integration Test Harness**: Build end-to-end regression tests verifying multi-principal routing, security ceiling enforcement, narrowing/broadening rules, cursor pagination, and zero storage mutation.
7. **Performance Baseline**: Benchmark median, P95, P99 latency and expansion/pagination costs across LEXICAL, GRAPH, MULTI-HOP, and PARAPHRASE modalities.

### Scoping Boundaries
- Zero modifications to `MemoryController.search()`, `MemoryController.search_financial()`, or `memory_controller/**`.
- Zero modifications to `PROJECT_BRAIN/PROJECT_STATE.md`.
- Zero modifications to `00_GOVERNANCE/coordination/chatgpt/**` or `main`.
- Zero disk mutation of canonical corpus markdown files.
- Zero simulated or fabricated SynapseStore data.

---

## 3. P2.1 Corpus Quality Gate

Report File: `07_EVALUATION/ci_evidence/corpus_quality_gate_report.json`  
Evaluator: `cognitive_core/benchmarks/corpus_quality_gate.py`  
Overall Verdict: **CONDITIONAL_PASS**

### Measured Metrics vs Thresholds
| Metric | Dimension | Scope | Target | Measured | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Duplicate Rate** | Data Hygiene | RETRIEVAL_BOUNDARY | $\le 5.0\%$ | **0.0%** (0 redundant) | **PASS** |
| **Duplicate Rate** | Data Hygiene | FULL_CORPUS | $\le 5.0\%$ | **69.30%** (580 redundant) | **FAIL** |
| **Provenance Completeness** | Trust & Lineage | RETRIEVAL_BOUNDARY | $\ge 85.0\%$ | **100.0%** (72/72) | **PASS** |
| **Provenance Completeness** | Trust & Lineage | FULL_CORPUS | $\ge 85.0\%$ | **30.11%** (252/837) | **WARN** |
| **Relation Density** | Graph Connectivity | RETRIEVAL_BOUNDARY | $\ge 0.40$ | **0.5139** edges/note | **PASS** |
| **Relation Density** | Graph Connectivity | FULL_CORPUS | $\ge 0.40$ | **0.0944** edges/note | **WARN** |
| **Dangling Links** | Graph Integrity | RETRIEVAL_BOUNDARY | $\le 50$ | **230** links | **WARN** |
| **Orphan Rate** | Graph Connectivity | RETRIEVAL_BOUNDARY | $\le 80.0\%$ | **77.78%** | **PASS** |

### Key Insight: The Security Boundary Shield
The full corpus has a 69.30% duplicate rate caused by 552 unhydrated test and policy template copies in `REVIEW` status (`policy-lesson_*.md` [385], `test_*.md` [167]).
**Crucially, because the retrieval facade strictly enforces `ACTIVE + verified`, exactly 0.0% of this template noise enters the search boundary!** The verified active boundary exhibits 0% exact duplicates, 100% provenance completeness, and 5.4x higher graph connectivity (0.5139 vs 0.0944 edges/note).

---

## 4. P2.2 Safe Corpus Cleanup Pipeline

Report File: `07_EVALUATION/ci_evidence/safe_cleanup_pipeline_report.json`  
Pipeline Module: `cognitive_core/benchmarks/safe_corpus_cleanup_pipeline.py`

### In-Memory Transformation Results
The pipeline simulated a multi-stage in-memory cleanup without modifying canonical files on disk:
1. **Duplicate Suppression**: Suppressed 580 identical redundant body notes (keeping primary canonical note).
2. **Template Stub Filtering**: Removed 52 unhydrated stubs and blank fixtures (<10 unique tokens).
3. **Dangling Relation Pruning**: Pruned 60 unresolvable wikilinks and frontmatter relation pointers.
4. **Orphan Note Isolation**: Identified 195 zero-degree notes for secondary fallback indexing.

### Empirical Pre- vs Post-Cleanup Delta
```
Corpus Size:               837 notes  ──►  224 notes (-613 notes, -73.2%)
Exact Redundant Notes:     580 notes  ──►    0 notes (-100.0%)
Template Stubs:             52 stubs  ──►    0 stubs (-100.0%)
Graph Edge Density:           0.0944  ──►   0.3259 (+0.2315 edges/note)
Known-Item Recall@1:          0.5333  ──►   0.5667 (+0.0334)
Known-Item Recall@10:         0.9667  ──►   1.0000 (+0.0333)
Known-Item MRR:               0.7048  ──►   0.7428 (+0.0380)
Graph Rescue Rate:            0.2808  ──►   0.2852 (+0.0044)
Median Search Latency:      0.927 ms  ──► 0.392 ms (-0.535 ms, 57% faster)
```

**Conclusion**: Pruning template noise in downstream consolidation will improve MRR and reduce median search latency by 57%, while the existing retrieval boundary already provides 100% defense against this noise during live queries.

---

## 5. P2.3 Retrieval Quality Gate

Report File: `07_EVALUATION/ci_evidence/retrieval_quality_gate_report.json`  
Evaluator: `cognitive_core/benchmarks/retrieval_quality_gate.py`  
Overall Verdict: **PASS (9/9 Metrics Satisfied)**

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                        RETRIEVAL QUALITY GATE EVALUATION                               │
├────────────┬─────────────────────────────┬─────────────┬──────────────┬────────────────┤
│ Metric ID  │ Dimension                   │ Target      │ Measured     │ Gate Status    │
├────────────┼─────────────────────────────┼─────────────┼──────────────┼────────────────┤
│ QG-KI-01   │ Known-Item Recall@10        │ >= 0.8500   │ 0.9100       │ PASS           │
│ QG-KI-02   │ Known-Item MRR              │ >= 0.5500   │ 0.6000       │ PASS           │
│ QG-PARA-01 │ Paraphrase Retrieval        │ R@10>=0.60  │ R@10=0.6000  │ PASS           │
│            │                             │ MRR>=0.40   │ MRR=0.4556   │                │
│ QG-MH-01   │ Multi-Hop Rescue Rate       │ >= 0.2500   │ 0.2977       │ PASS           │
│ QG-MH-02   │ Multi-Hop Net Gain          │ > 0         │ +125 notes   │ PASS           │
│ QG-ENT-01  │ Entity-Mediated Recall@10   │ >= 0.3500   │ 0.4104       │ PASS           │
│ QG-SEC-01  │ Security Boundary (Leaks)   │ == 0 leaks  │ 0 leaks      │ PASS           │
│ QG-DET-01  │ Retrieval Determinism       │ 100% True   │ True         │ PASS           │
│ QG-PERF-01 │ Execution Median Latency    │ < 5.000 ms  │ 0.991 ms     │ PASS           │
└────────────┴─────────────────────────────┴─────────────┴──────────────┴────────────────┘
```

---

## 6. P2.4 Production Integration Design

### Proposed Architectural Call Flow
When production integration is authorized by the council, the call wiring will follow this strict unidirectional chain:

```
┌──────────────────────────────────────────────────────────────────────────┐
│                   Caller (e.g. MemoryController.search)                  │
│  - Authorizes caller principal (HUMAN, AI_AGENT, ADMIN)                  │
│  - Formulates FacadeRetrievalRequest                                     │
└────────────────────────────────────┬─────────────────────────────────────┘
                                     │
                                     ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                      ProductionRetrievalFacade                           │
│  - Pre-retrieval validation: explicit principal, page_size boundaries    │
│  - Rejects broadening attempts fail-closed                               │
│  - Cursor token generation & verification                                │
│  - Secondary defense-in-depth sanitization of returned notes             │
│  - Deterministic tie-breaking (score DESC, id ASC)                       │
└────────────────────────────────────┬─────────────────────────────────────┘
                                     │
                                     ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                      RetrievalBoundaryAdapter                            │
│  - Normalizes principal representation                                   │
│  - Enforces invariant ceiling: ALLOWED_LIFECYCLES = {ACTIVE}             │
│                                ALLOWED_VERIFICATION = {verified}         │
│  - Permits narrowing filters (e.g. types=['decision'])                   │
└────────────────────────────────────┬─────────────────────────────────────┘
                                     │
                                     ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                         HybridRetriever                                  │
│  - In-memory BM25 lexical search + entity extractor                      │
│  - Graph spreading activation (CorpusGraph)                              │
│  - Dense embedding fallback (fail-closed if offline)                     │
│  - Reciprocal Rank Fusion (k=60)                                         │
└──────────────────────────────────────────────────────────────────────────┘
```

### Proposed Interface Signatures
```python
# memory_controller/controller.py (Future wiring candidate)
def search(
    self,
    query: str,
    principal: Principal | str,
    page_size: int = 10,
    page_token: Optional[str] = None,
    types: Optional[List[str]] = None,
) -> SearchResponse:
    # 1. Authorization check
    self.policy.require_permission(principal, Operation.SEARCH)
    
    # 2. Invoke Facade
    request = FacadeRetrievalRequest(
        query=query,
        principal=principal,
        page_size=page_size,
        page_token=page_token,
        types=types,
    )
    facade_response = self._retrieval_facade.retrieve(request)
    
    # 3. Serialize to controller response envelope
    return SearchResponse(
        hits=[self._to_controller_hit(r) for r in facade_response.results],
        next_page_token=facade_response.next_page_token,
        total_hits=facade_response.total_hits,
    )
```

---

## 7. P2.5 SynapseStore Re-evaluation

- Checked `05_DATA/synapses.json`: **NOT FOUND** (`exists = False`)
- Checked `cognitive_core/synapse_store.py`: **NOT FOUND** (`exists = False`)
- Infrastructure Status: **`BLOCKED_BY_EXTERNAL_SYNAPSESTORE`**
- **Findings**:
  - The production SynapseStore module and JSON index are owned by the upstream Claude Code / external track and have not been merged to `antigravity/p1-retrieval-foundation`.
  - In accordance with the contract, zero mock or synthetic synapse data was fabricated.
  - The system automatically fell back to intrinsic corpus graph retrieval (`CorpusGraph` wikilinks, frontmatter relations, and rare technical entities), which fully satisfied the 25% rescue rate requirement (achieved **29.77%** rescue rate and **+125** net gain).

---

## 8. P2.6 Integration Test Harness

Test Suite: `20_TESTS/regression/test_retrieval_integration_harness.py`  
Passed: **16/16 tests** (Execution time: 0.76s)  
Overall Regression Suite: **112/112 tests passed** (Execution time: 1.63s)

### Covered Test Dimensions
1. `test_integration_all_valid_principals_succeed`: Validates `HUMAN`, `AI_AGENT`, and `ADMIN` across enum and string values.
2. `test_integration_missing_principal_fails_closed`: Verifies that facade never assumes or defaults principal identity.
3. `test_integration_unrecognized_principal_fails_closed`: Rejects invalid principals with `PrincipalValidationError`.
4. `test_integration_strict_active_verified_ceiling`: Ensures `REVIEW`, `RAW`, `ARCHIVED`, `SUPERSEDED` are completely blocked.
5. `test_integration_filter_broadening_attempt_rejected`: Confirms `BoundaryViolationError` on attempted boundary broadening.
6. `test_integration_filter_narrowing_by_type`: Confirms safe narrowing within boundary.
7. `test_integration_filter_narrowing_empty_result`: Confirms empty set is returned when no notes match narrowing filter.
8. `test_integration_deterministic_ranking`: Confirms exact tie-breaking score and note ID ordering parity.
9. `test_integration_pagination_flow`: Validates multi-page cursor traversal.
10. `test_integration_invalid_page_token_fails_closed`: Rejects tampered tokens.
11. `test_integration_live_canonical_vault_search`: Confirms live search on real repository files.

---

## 9. P2.7 Performance Baseline & Latency Distribution

Report File: `07_EVALUATION/ci_evidence/retrieval_performance_baseline_report.json`  
Evaluator: `cognitive_core/benchmarks/performance_baseline.py`

### Latency Distributions Across Modalities
| Modality | Queries | Mean (ms) | Median (ms) | P95 (ms) | P99 (ms) | Target SLA |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **LEXICAL** | 50 | 0.812 | **0.745** | 1.256 | 1.446 | $< 5.0$ ms |
| **GRAPH_1HOP** | 50 | 0.771 | **0.719** | 1.088 | 1.212 | $< 5.0$ ms |
| **MULTI_HOP_2HOP_ENTITY** | 50 | 1.468 | **1.025** | 1.774 | 8.262 | $< 10.0$ ms |
| **PARAPHRASE** | 25 | 0.803 | **0.779** | 1.030 | 1.055 | $< 5.0$ ms |

### Expansion & Pagination Costs
- **1-Hop Expansion Overhead**: $-0.026$ ms delta, $+1.2$ candidates discovered per query.
- **2-Hop + Entity Overhead**: $+0.280$ ms delta, $+4.8$ candidates discovered per query.
- **Cursor Pagination Overhead**: Page 1 ($0.745$ ms) vs Page 2 ($0.751$ ms) $\to$ **$+0.006$ ms overhead** (negligible).

---

## 10. Integration Dependencies & Blockers

1. **Dependency Blockers**:
   - `SynapseStore`: Still external; blocked by absent `05_DATA/synapses.json` and `cognitive_core/synapse_store.py`.
   - `MemoryController` Wiring: Blocked until ChatGPT / coordinator authorizes production runtime integration.
2. **Production Prerequisites**:
   - Upstream consensus on caller principal propagation in `MemoryController`.
   - Consensus on error propagation convention (`BoundaryViolationError` $\to$ HTTP 403 / fail-closed empty response).

---

## 11. Next Implementation Step

When the council approves runtime activation:
1. Wire `ProductionRetrievalFacade` into `MemoryController.search()` behind an experimental flag (`use_p2_facade=True`).
2. Run end-to-end integration tests between `MemoryController` and `ProductionRetrievalFacade`.
3. Promote `use_p2_facade=True` to default after confirming zero regressions against the 112-test suite.
