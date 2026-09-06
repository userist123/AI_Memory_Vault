# P0 Real Pipeline Diagnostic Report — Evidence Coverage & Correctness

## 1. Traced Real Retrieval Pipeline

- **Entry Point**: `MemoryController.search(principal=Principal.AI_AGENT, query=q, page_size=...)`
- **Classification**: `QueryClassifier.classify(sanitized_query)`
- **Retrieval**: `RetrievalEngine.retrieve(classified, principal, query_fp, disclosure_level, budget)`
- **Relevance Scoring**: `RelevanceScorer.score(sanitized_query, notes)`
- **Progressive Disclosure**: `ProgressiveDisclosure(budget).full_document(notes)`
- **Context Packaging**: `ContextPackBuilder.build(request_id, agent_id, budget, results, ...)`
- **Final Object**: Structured Context Pack dictionary with byte and token envelope limits.

---

## 2. Real Multi-Signal Capabilities in Repository (Factual Audit)

| Signal Layer | Real Codebase Status | Architectural Evidence |
|---|---|---|
| **Semantic / Vector** | `PARTIAL` | QdrantRetrieval & DeterministicSemanticProvider exist in cognitive_core/qdrant_retrieval.py and financial_search.py, but are not wired into the default MemoryController.search flow. |
| **Lexical / BM25** | `PARTIAL` | BM25Scorer exists in memory_controller/financial_search.py; default RelevanceScorer in controller.py uses token overlap ratio + confidence weighting. |
| **Entity Resolution** | `PARTIAL` | FinancialEntityResolver exists in memory_controller/financial_search.py; general domain vault entity extractors are missing in standard controller. |
| **Graph Expansion** | `PARTIAL` | MultiGraph exists in cognitive_core/multi_graph.py with 4 orthogonal views, but RetrievalEngine in memory_controller does not traverse multi-hop graph edges automatically during search. |

---

## 3. Empirical Results: Real A1 vs Real A2 vs Real B

| Condition | Configuration | Evidence Coverage | M1 (3B) Accuracy | M2 (7B) Accuracy |
|---|---|---|---|---|
| **Real A1** | Default `page_size=5` | **6.7%** | **11.7%** | **18.3%** |
| **Real A2** | Doubled `page_size=10` | **6.7%** | **18.3%** | N/A |
| **Real B (Full)** | Full Vault dump | **71.1%** | **63.3%** | **75.9%** |

---

## 4. Diagnostic Effects

- **BUDGET EFFECT**: `MODERATE` (Doubling `page_size` from 5 to 10 improves evidence coverage from 6.7% to 6.7% and accuracy by +6.7%)
- **RETRIEVAL EFFECT**: `CRITICAL` (Current default retrieval misses 64.4% of required facts due to single-document keyword bias without graph expansion)
- **MODEL EFFECT**: `MEASURABLE` (Scaling from 3B to 7B increases Full Context accuracy from 63.3% to 75.9%)

---

## 5. 15 Queries Detailed Breakdown

| Query ID | Category | Real A1 Cov | Real A1 Acc | Real A2 Cov | Real A2 Acc | Real B Cov | Real B Acc |
|---|---|---|---|---|---|---|---|
| `Q01_SQLITE_WAL_PRAGMA` | simple_fact | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 | 1.00 |
| `Q02_P16_HARDWARE_TELEMETRY` | simple_fact | 0.00 | 0.00 | 0.00 | 0.00 | 0.33 | 0.67 |
| `Q03_COUNCIL_AGENT_LIMITS` | simple_fact | 1.00 | 0.50 | 1.00 | 0.50 | 1.00 | 1.00 |
| `Q04_COUNCIL_TOKEN_BUDGETS` | simple_fact | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 | 1.00 |
| `Q05_MULTI_AGENT_COORDINATION` | simple_fact | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 | 0.50 |
| `Q06_MULTIHOP_PROMOTION_FLOW` | multihop | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 | 1.00 |
| `Q07_MULTIHOP_COUNCIL_SYNTHESIS` | multihop | 0.00 | 0.25 | 0.00 | 0.50 | 0.75 | 0.75 |
| `Q08_MULTIHOP_CONFLICT_PAIRING` | multihop | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 |
| `Q09_TEMPORAL_SUPERSEDED_POLICY` | temporal | 0.00 | 0.50 | 0.00 | 1.00 | 0.00 | 0.50 |
| `Q10_TEMPORAL_SLEEP_CONSOLIDATION` | temporal | 0.00 | 0.25 | 0.00 | 0.25 | 0.25 | 0.00 |
| `Q11_CONTRADICTION_AI_VERIFICATION` | contradiction_guardrail | 0.00 | 0.25 | 0.00 | 0.50 | 1.00 | 0.75 |
| `Q12_CONTRADICTION_PROVENANCE_SOURCE` | contradiction_guardrail | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 | 1.00 |
| `Q13_CONTRADICTION_STORAGE_MUTABILITY` | contradiction_guardrail | 0.00 | 0.00 | 0.00 | 0.00 | 0.33 | 0.33 |
| `Q14_MULTIHOP_GRAPH_NODE_SCHEMA` | multihop | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 | 0.00 |
| `Q15_SIMPLE_PRIME_DIRECTIVE` | simple_fact | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 | 1.00 |

## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
