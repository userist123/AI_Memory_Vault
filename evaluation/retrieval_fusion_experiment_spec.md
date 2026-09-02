# Retrieval Fusion Experiment Specification (P0c Preparation)

> **Document Type**: Controlled Experiment Specification  
> **Status**: DRAFT / SPECIFICATION ONLY (Production implementation blocked until audited)  
> **Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
> **Empirical Foundation**: Derived directly from P0 Diagnostic measurements ([`01_KNOWLEDGE/Retrieval_Bottleneck_P0_Empirical_Findings.md`](file:///01_KNOWLEDGE/Retrieval_Bottleneck_P0_Empirical_Findings.md))

---

## 1. Objective & Hypothesis

This specification defines a rigorous, controlled experiment to evaluate the incremental accuracy and evidence recall contributions of layering four distinct candidate generation signals against the baseline 15 LoCoMo queries.

### Core Proposition
Single-signal semantic retrieval leaves a 64.4% factual evidence gap on cross-document and exact-term queries. Progressively fusing Lexical BM25, Entity Anchors, and Graph Expansion will close this gap without exceeding the sparse context envelope ($<2500$ input tokens).

---

## 2. Experimental Controls & Invariants

To guarantee scientific validity, the following parameters are held **strictly identical** across all experimental conditions:

1. **Test Dataset**: Exact same 15 queries from [`evaluation/full_context_baseline.py`](file:///evaluation/full_context_baseline.py).
2. **Knowledge Corpus**: Exact same canonical Markdown vault notes ingested from disk into `StorageEngine`.
3. **Inference Models**: `qwen2.5-coder:3b` (Light tier) and `qwen2.5-coder:7b` (Standard tier) on local Ollama runtime.
4. **Prompt Format**: Fixed prompt envelope `[CONTEXT]\n{context}\n\n[QUESTION]\n{query}\n\n[ANSWER]`.
5. **Evaluator**: Deterministic rule-based keyword & fact presence verifier ([`evaluate_response_accuracy`](file:///evaluation/full_context_baseline.py)).
6. **Isolation Principle**: **Exactly one retrieval layer is toggled per experimental condition**.

---

## 3. Real Codebase Architectural Mapping

Per the strict empirical audit, signals are mapped to existing codebase modules without synthetic mock wrappers:

| Retrieval Condition | Signal Composition | Codebase Implementation Status | Source File / Anchor |
|---|---|:---:|---|
| **R1** (Semantic Only) | Dense token-overlap + confidence weighting | `EXISTS` | [`memory_controller/context/relevance_scoring.py`](file:///memory_controller/context/relevance_scoring.py) |
| **R2** (Semantic + Lexical) | R1 + BM25 inverted index frequency scoring | `PARTIAL` | [`memory_controller/financial_search.py`](file:///memory_controller/financial_search.py) (`BM25Scorer`) |
| **R3** (R2 + Entity) | R2 + Named Entity Resolution & tag binding | `PARTIAL` | [`memory_controller/financial_search.py`](file:///memory_controller/financial_search.py) (`FinancialEntityResolver`) |
| **R4** (R3 + Graph) | R3 + 1-hop/2-hop Relational Graph traversal | `PARTIAL` | [`cognitive_core/multi_graph.py`](file:///cognitive_core/multi_graph.py) (`MultiGraph`) |

> [!IMPORTANT]
> Because R2, R3, and R4 are currently `PARTIAL` (existing in specialized modules rather than the canonical `MemoryController.search` path), the experiment harness must import and execute the actual classes from those modules directly without monkeypatching or modifying production configuration files.

---

## 4. Query Class Taxonomy & Expected Signal Synergy

The 15 baseline queries are categorized into 5 cognitive dimensions with theoretical signal relevance hypotheses:

| Query ID | Cognitive Class | Primary Challenge | Hypothesized Beneficial Signal |
|---|---|---|---|
| `Q01_SQLITE_WAL_PRAGMA` | `SIMPLE_FACT` | Exact PRAGMA parameter name | Lexical BM25 (R2) |
| `Q02_P16_HARDWARE_TELEMETRY` | `SIMPLE_FACT` | Exact invariant identifier (`P16`) | Lexical BM25 (R2) |
| `Q03_COUNCIL_AGENT_LIMITS` | `SIMPLE_FACT` | Operating numeric ceiling | Semantic / Lexical (R1, R2) |
| `Q04_COUNCIL_TOKEN_BUDGETS` | `SIMPLE_FACT` | Exact token allocation numbers | Lexical BM25 (R2) |
| `Q05_MULTI_AGENT_COORDINATION`| `SIMPLE_FACT` | Tool names & coordination files | Entity Anchoring (R3) |
| `Q06_MULTIHOP_PROMOTION_FLOW` | `MULTI_HOP` | Bridging `Principal` $\rightarrow$ `REVIEW` $\rightarrow$ `ACTIVE` | Graph Expansion (R4) |
| `Q07_MULTIHOP_COUNCIL_SYNTHESIS`| `MULTI_HOP` | Specialist token limit $\leftrightarrow$ synthesis budget | Graph Expansion (R4) |
| `Q08_MULTIHOP_CONFLICT_PAIRING`| `MULTI_HOP` | Contradiction pairing cap formula | Graph Expansion (R4) |
| `Q09_TEMPORAL_SUPERSEDED_POLICY`| `TEMPORAL` | Deprecated vs active policy resolution | Temporal Metadata / Lineage (R4) |
| `Q10_TEMPORAL_SLEEP_CONSOLIDATION`| `TEMPORAL`| Timestamp-ordered starvation prevention | Temporal Metadata / Lineage (R4) |
| `Q11_CONTRADICTION_AI_VERIFICATION`| `GUARDRAIL`| AI agent self-verification prohibition | Lexical + Graph (R2, R4) |
| `Q12_CONTRADICTION_PROVENANCE_SOURCE`| `GUARDRAIL`| Gated source type validation | Lexical + Entity (R2, R3) |
| `Q13_CONTRADICTION_STORAGE_MUTABILITY`| `GUARDRAIL`| Immutability of hardware telemetry | Lexical + Entity (R2, R3) |
| `Q14_MULTIHOP_GRAPH_NODE_SCHEMA`| `MULTI_HOP` | Controlled node typing schema | Entity + Graph (R3, R4) |
| `Q15_SIMPLE_PRIME_DIRECTIVE` | `SIMPLE_FACT` | Exact prime directive quote | Lexical BM25 (R2) |

---

## 5. Measured Metrics Specification

For every query and condition ($R_1 \dots R_4$), the harness must record:

1. **`retrieval_candidate_recall`**:
   $$\text{Candidate Recall} = \frac{|\text{Retrieved Note IDs} \cap \text{Gold Note IDs}|}{|\text{Gold Note IDs}|}$$
2. **`required_fact_recall` (Evidence Coverage)**:
   $$\text{Evidence Coverage} = \frac{|\text{Required Fact Keywords Present in Context Pack}|}{|\text{Total Required Fact Keywords}|}$$
3. **`final_context_fact_recall`**:
   $$\text{Context Fact Recall} = \frac{|\text{Required Fact Keywords Retained after Progressive Disclosure}|}{|\text{Total Required Fact Keywords}|}$$
4. **`answer_correctness`**:
   Deterministic semantic evaluation against gold criteria $[0.0, 1.0]$.
5. **`token_usage`**:
   Actual prompt input tokens + generated output tokens recorded via telemetry.
6. **`latency_ms`**:
   End-to-end execution latency (retrieval + packaging + generation).

---

## 6. Execution Procedure

```text
STEP 1: Ingest canonical disk notes into real StorageEngine.
STEP 2: For condition in [R1, R2, R3, R4]:
          For query in EVAL_CASES:
            1. Execute candidate generation for target condition.
            2. Build Context Pack via ContextPackBuilder (bounded to budget.max_notes=5).
            3. Measure candidate_recall and evidence_coverage.
            4. Execute local model inference (M1: 3b, M2: 7b).
            5. Evaluate response accuracy and record token/latency telemetry.
STEP 3: Generate comparative report and delta analysis (R1 -> R2 -> R3 -> R4 -> Full Context B).
```

---

## 7. Memory Quality Principle & Architectural Insight

$$\text{Candidate Recall} > \text{Candidate Count}$$

- $10\text{ irrelevant results} < 5\text{ relevant results}$
- $\text{larger page\_size} \neq \text{better retrieval}$

Long-term memory quality depends on an integrated 7-stage cognitive pipeline:
$$\text{Memory Quality} = \text{Representation} \times \text{Indexing} \times \text{Candidate Gen} \times \text{Reranking} \times \text{Activation} \times \text{Packing} \times \text{Verification}$$

---

## 8. Skill Evolution Pipeline Implication

The telemetry and evidence logs collected during multi-signal retrieval directly supply the future autonomous Skill Evolution loop:

$$\text{Experience} \longrightarrow \text{Outcome Event} \longrightarrow \text{Evidence} \longrightarrow \text{Retrieval Signal} \longrightarrow \text{Pattern} \longrightarrow \text{Candidate Capability} \longrightarrow \text{Skill}$$

---

## 9. Protected Core Governance & Safe Execution Boundaries

- **Zero Production Modification**: The execution of this experiment must not alter production files ([`Council_Runtime_Profile.yaml`](file:///Council_Runtime_Profile.yaml), [`ContextPackBuilder`](file:///memory_controller/context/pack_builder.py), [`model_tiers.json`](file:///config/model_tiers.json)).
- **Review Boundary**: All experimental logs and artifacts must be written strictly to `evaluation/` and `04_MEMORY/`.
- **Fail-Closed Validation**: If a required signal module cannot be cleanly instantiated, the runner must report `STATUS = MISSING` rather than generating simulated synthetic outputs.
