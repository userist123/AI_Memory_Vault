---
id: "knw-retrieval-bottleneck-p0-0001"
type: knowledge
lifecycle: REVIEW
category: retrieval-architecture
tags: [retrieval, diagnostic, p0, empirical-evidence, candidate-generation, multi-signal, context-pack, locomo]
created: 2026-09-01T21:55:00Z
updated: 2026-09-01T21:55:00Z
provenance:
  source_type: execution
  source_ref: "evaluation/retrieval_diagnostic_runner.py"
confidence: high
verification: unverified
relations:
  - type: related_to
    target_id: 330fa4bc-5b7c-4fb0-8d80-bcfa148a29c9
  - "00_CORE/GRAPH/02 Memory Knowledge Map.md"
  - type: related_to
    target_id: knw-benchmarks-2026-0001
  - type: related_to
    target_id: c754b481-44a2-4e2f-9cb2-0be36aebb498
  - ".agents/rules/vault_cognitive_rules.md"
---

# Retrieval Bottleneck — P0 Empirical Findings & Architectural Specification

This knowledge document formalizes the empirical findings, mathematical measurements, failure modes, and architectural implications derived from the **P0 Real Pipeline Diagnostic Experiment** executed against the AI Memory Vault.

---

## 1. Executive Summary

Empirical measurement of the real production pipeline (`MemoryController.search` $\rightarrow$ `QueryClassifier` $\rightarrow$ `RetrievalEngine` $\rightarrow$ `RelevanceScorer` $\rightarrow$ `ProgressiveDisclosure` $\rightarrow$ `ContextPackBuilder`) revealed that the primary accuracy bottleneck in sparse-context agentic execution is **Candidate Discovery / Retrieval Composition**, rather than model reasoning or the final context byte budget.

```text
+-------------------------------------------------------------------------------+
|                      P0 REAL RETRIEVAL DIAGNOSTIC RESULTS                     |
+-------------------------------------------------------------------------------+
| Condition A1 (Default: page_size=5)   --> Evidence Coverage:  6.7% | Acc: 11.7%|
| Condition A2 (Doubled: page_size=10)  --> Evidence Coverage:  6.7% | Acc: 18.3%|
| Condition B  (Full Vault Context Dump)--> Evidence Coverage: 71.1% | Acc: 63.3%|
| Model Scaling on Full Context (3B -> 7B)                           | Acc: 75.9%|
+-------------------------------------------------------------------------------+
```

---

## 2. Observed Facts

1. **Candidate Count Invariance on Evidence**: Doubling `page_size` from 5 to 10 in Condition A2 resulted in identical factual evidence coverage (**6.7%**) as Condition A1.
2. **Missing Cross-Document Evidence**: In 14 of 15 benchmark queries, Condition A1 retrieved only single isolated notes matching literal tokens, completely omitting related governing notes (e.g. `AGENTS.md` operating limits were co-retrieved without `vault_cognitive_rules.md` security invariants).
3. **Model Performance Discrepancy**: On Condition A1, model `qwen2.5-coder:3b` scored 11.7% accuracy and `qwen2.5-coder:7b` scored 18.3%. On Condition B (Full Context), `3b` scored 63.3% and `7b` scored 75.9%.
4. **Codebase Multi-Signal Reality**:
   - `Semantic / Vector`: `PARTIAL` (Implemented in `cognitive_core/qdrant_retrieval.py` & `financial_search.py`, not connected to default `MemoryController.search`).
   - `Lexical / BM25`: `PARTIAL` (BM25 exists in `memory_controller/financial_search.py`; default `RelevanceScorer` uses token overlap).
   - `Entity Resolution`: `PARTIAL` (Entity extraction exists for financial tickers; generic vault entity resolution missing).
   - `Graph Expansion`: `PARTIAL` (4-view `MultiGraph` exists in `cognitive_core/multi_graph.py`, but `RetrievalEngine` does not traverse multi-hop graph edges automatically).

---

## 3. Measurements

The experiment executed 15 benchmark queries across 2 models and 3 conditions:

### 3.1 Aggregate Matrix

| Condition | Configuration | Evidence Coverage (%) | M1 (3B) Accuracy (%) | M2 (7B) Accuracy (%) | Mean Context Size | Mean Tokens |
|---|---|:---:|:---:|:---:|:---:|:---:|
| **Real A1** | Default `page_size=5` | **6.7%** | **11.7%** | **18.3%** | 3,142 chars | 682 |
| **Real A2** | Test `page_size=10` | **6.7%** | **18.3%** | N/A | 3,420 chars | 714 |
| **Real B** | Full Context Dump | **71.1%** | **63.3%** | **75.9%** | 9,840 chars | 1,842 |

### 3.2 Query-Level Performance

```text
Query ID                               Category                 Real A1 Cov  Real A1 Acc  Real B Cov  Real B Acc (7B)
-----------------------------------------------------------------------------------------------------------------
Q01_SQLITE_WAL_PRAGMA                  simple_fact                  0.00         0.00        1.00          1.00
Q02_P16_HARDWARE_TELEMETRY             simple_fact                  0.00         0.00        0.33          0.67
Q03_COUNCIL_AGENT_LIMITS               simple_fact                  1.00         0.50        1.00          1.00
Q04_COUNCIL_TOKEN_BUDGETS              simple_fact                  0.00         0.00        1.00          1.00
Q05_MULTI_AGENT_COORDINATION           simple_fact                  0.00         0.00        1.00          1.00
Q06_MULTIHOP_PROMOTION_FLOW            multihop                     0.00         0.00        1.00          1.00
Q07_MULTIHOP_COUNCIL_SYNTHESIS         multihop                     0.00         0.25        0.75          0.75
Q08_MULTIHOP_CONFLICT_PAIRING          multihop                     0.00         0.00        0.00          0.33
Q09_TEMPORAL_SUPERSEDED_POLICY         temporal                     0.00         0.50        0.00          0.50
Q10_TEMPORAL_SLEEP_CONSOLIDATION       temporal                     0.00         0.25        0.25          0.00
Q11_CONTRADICTION_AI_VERIFICATION      contradiction_guardrail      0.00         0.25        1.00          1.00
Q12_CONTRADICTION_PROVENANCE_SOURCE    contradiction_guardrail      0.00         0.00        1.00          1.00
Q13_CONTRADICTION_STORAGE_MUTABILITY   contradiction_guardrail      0.00         0.00        0.33          0.33
Q14_MULTIHOP_GRAPH_NODE_SCHEMA         multihop                     0.00         0.00        1.00          0.80
Q15_SIMPLE_PRIME_DIRECTIVE             simple_fact                  0.00         0.00        1.00          1.00
```

---

## 4. Interpretation

1. **Candidate Recall Bottleneck**:
   Increasing `page_size` without expanding the discovery mechanism retrieves redundant chunks from the already matched note rather than branching into adjacent conceptual domains.
2. **Model Incapacity on Missing Evidence**:
   A larger model (`7b`) cannot reliably infer domain invariants if the ground truth premises are absent from the context pack (18.3% vs 75.9%).
3. **Separation of Retrieval vs Reasoning**:
   The failure mode for 80% of errors in Condition A1 was `RETRIEVAL_FAILURE` (evidence absent), not `MODEL_REASONING_FAILURE`.

---

## 5. Confidence

- **High Confidence**: For the 15 evaluation queries tested, single-signal token overlap retrieval fails on multi-hop questions and guardrail co-references.
- **Medium Confidence**: Generalization to large-scale production workloads (requires testing across $>100$ diverse domain queries).
- **Low Confidence**: Whether reranking alone without graph expansion is sufficient for multi-hop synthesis.

---

## 6. Limitations

- Evaluation dataset size: 15 queries (structured according to LoCoMo dimensions, but bounded).
- Local LLM inference constraints: Models tested were quantized local variants (`3b` and `7b`).
- Bounded vault corpus: The benchmark evaluated core policies, procedures, and system invariants.

---

## 7. Open Questions

1. Will reciprocal rank fusion (RRF) between lexical BM25 and vector embeddings suffice for exact-term queries, or is dedicated entity anchoring required?
2. What is the optimal graph expansion depth (1-hop vs 2-hop) that maximizes multi-hop evidence coverage while respecting the strict 2500-token synthesis input budget?
3. What is the exact latency overhead of running dual candidate generators (BM25 + Vector) in an embedded SQLite/Python environment?

---

## 8. Memory Quality Principle

$$\text{Candidate Recall} > \text{Candidate Count}$$

- $10\text{ irrelevant results} < 5\text{ relevant results}$
- $\text{larger page\_size} \neq \text{better retrieval}$

**Rule Derivation**: Simply expanding candidate allocations without improving discovery precision introduces noise and degrades model attention without capturing the required premises.

---

## 9. First-Class Metric Hierarchy

To prevent conflating retrieval performance with LLM generative artifacts, future benchmarks must report four decoupled metrics:

1. $\text{retrieval\_candidate\_recall} = \frac{|\text{Retrieved Candidate Notes} \cap \text{Gold Candidate Notes}|}{|\text{Gold Candidate Notes}|}$
2. $\text{required\_fact\_recall} = \frac{|\text{Required Keywords/Facts Found in Retrieved Context}|}{|\text{Required Keywords/Facts}|}$
3. $\text{final\_context\_fact\_recall} = \frac{|\text{Required Facts Remaining after Progressive Disclosure}|}{|\text{Required Facts}|}$
4. $\text{answer\_correctness} = \text{Deterministic Rule-Based or Human Verified Semantic Accuracy } [0.0 - 1.0]$

---

## 10. Retrieval Capability Map

```text
QUERY
  │
  ▼
QUERY UNDERSTANDING [PARTIAL - memory_controller/context/query_classifier.py]
  │
  ▼
CANDIDATE GENERATION
  ├── Semantic / Vector    [PARTIAL - cognitive_core/qdrant_retrieval.py]
  ├── Lexical / BM25       [PARTIAL - memory_controller/financial_search.py]
  ├── Entity Anchoring     [PARTIAL - memory_controller/financial_search.py]
  └── Graph Seed Expansion [PARTIAL - cognitive_core/multi_graph.py]
  │
  ▼
CANDIDATE POOL MERGE / RRF [MISSING in default controller]
  │
  ▼
RERANKING                  [PARTIAL - memory_controller/context/relevance_scoring.py]
  │
  ▼
PROGRESSIVE DISCLOSURE     [EXISTS - memory_controller/context/progressive_disclosure.py]
  │
  ▼
CONTEXT PACK PACKAGING     [EXISTS - memory_controller/context/pack_builder.py]
  │
  ▼
MODEL EXECUTION            [EXISTS - cognitive_core/local_provider.py]
  │
  ▼
VERIFICATION & ATTESTATION [EXISTS - memory_controller/authorizer.py]
```

---

## 11. Memory Architecture Insight

Long-term memory system quality is a multiplicative pipeline:

$$\text{Memory Quality} = f(\text{Representation}) \times f(\text{Indexing}) \times f(\text{Candidate Gen}) \times f(\text{Reranking}) \times f(\text{Activation}) \times f(\text{Packing}) \times f(\text{Verification})$$

A high-performance vector database is only 1 of 7 essential components; if candidate generation or query understanding fails, the downstream model fails.

---

## 12. Skill Evolution Implication

The long-term capability of autonomous subagents relies on an evidence feedback loop:

$$\text{Experience} \longrightarrow \text{Outcome Event} \longrightarrow \text{Verifiable Evidence} \longrightarrow \text{Retrieval Signal} \longrightarrow \text{Synthesized Pattern} \longrightarrow \text{Candidate Capability} \longrightarrow \text{Active Skill}$$

---

## 13. Lessons for Future Agents

1. **Never equate more retrieval results with better retrieval**: Increasing `page_size` does not recover missing cross-document graph edges.
2. **Measure evidence coverage separately from answer correctness**: Always verify whether the facts exist in the context pack before evaluating model generation.
3. **Candidate recall must be measured before ranking quality**: A reranker cannot rank notes that candidate generation never discovered.
4. **Full-context baseline is a diagnostic oracle, not a production target**: Use Condition B only to isolate whether failures stem from retrieval or model reasoning.
5. **Stronger models cannot recover missing evidence reliably**: Parameter scaling does not replace grounding evidence.
6. **Retrieval improvements must be benchmarked against the same dataset**: Always evaluate against the exact 15 baseline queries to ensure empirical comparability.
7. **Every retrieval optimization must report token/latency cost**: Precision gains must fit within the 2500-token synthesis budget and sub-100ms retrieval SLA.
