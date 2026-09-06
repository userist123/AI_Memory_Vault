---
id: "reg-retrieval-hypotheses-0001"
type: knowledge
lifecycle: REVIEW
category: retrieval-architecture
tags: [hypotheses, retrieval, experimental-registry, p0-evidence, candidate-generation, fusion]
created: 2026-09-01T21:56:00Z
updated: 2026-09-01T21:56:00Z
provenance:
  source_type: execution
  source_ref: "01_KNOWLEDGE/Retrieval_Bottleneck_P0_Empirical_Findings.md"
confidence: high
verification: unverified
relations:
  - type: related_to
    target_id: knw-retrieval-bottleneck-p0-0001
  - "evaluation/retrieval_fusion_experiment_spec.md"
  - type: related_to
    target_id: c754b481-44a2-4e2f-9cb2-0be36aebb498
---

# Retrieval Hypothesis Registry

This registry tracks formalized, testable empirical hypotheses regarding memory retrieval, candidate generation, and multi-signal fusion for the AI Memory Vault.

> [!IMPORTANT]
> Hypotheses registered herein are testable propositions, not canonical active facts. Promotion to canonical knowledge requires empirical verification via controlled benchmark experiments.

---

## 🔬 Registered Hypotheses & Empirical Status

### `R-H001` — Semantic-Only Exact Term Blindness
- **Status**: `CONFIRMED` (via Retrieval Fusion Lab R1 ablation).
- **Hypothesis**: Pure dense vector or semantic token-overlap candidate generation misses exact policy constraints, PRAGMA flags, and uppercase acronyms (e.g. `PRAGMA busy_timeout=5000`, `P16-P18`) when phrasing diverges.
- **Supporting Evidence**: In R1 (Semantic Only), `Q02_P16_HARDWARE_TELEMETRY` achieved only 33% fact recall. Lexical BM25 (R2) immediately increased fact recall to 67% (+34% delta).
- **Counterevidence**: Query reformulations using close semantic synonyms occasionally match if high-confidence keyword tags are present.
- **Confidence**: `High`.

---

### `R-H002` — Lexical Matching Improves Exact Policy & Guardrail Retrieval
- **Status**: `CONFIRMED` (via Retrieval Fusion Lab R2 ablation).
- **Hypothesis**: Incorporating BM25 lexical matching with inverted index term saturation directly recovers missing exact governance terms and guardrail invariants.
- **Supporting Evidence**: `SIMPLE_FACT` queries jumped from 88.8% fact recall in R1 to 94.5% in R2. `CONTRADICTION_GUARDRAIL` queries jumped from 61.0% in R1 to 69.3% in R2.
- **Counterevidence**: Lexical matching alone fails on queries with abstract phrasing or zero direct keyword overlap.
- **Confidence**: `High`.

---

### `R-H003` — Entity Anchors for Named & Domain Identifiers
- **Status**: `PARTIALLY_SUPPORTED` (via Retrieval Fusion Lab R3 ablation).
- **Hypothesis**: Explicit entity extraction (e.g. `Principal.AI_AGENT`, `SQLiteStorageEngine`, `CouncilBudgetController`) mapped to canonical note identifiers provides deterministic candidate seeds that bypass token scoring ambiguity.
- **Supporting Evidence**: Candidate recall increased from 63.3% in R2 to 76.7% in R3 (+13.4% delta), raising Guardrail fact recall from 69.3% to 77.7%.
- **Counterevidence**: Without downstream graph expansion, entity boosting alone did not immediately improve final generation accuracy for small models without prompt restructuring.
- **Confidence**: `Medium-High`.

---

### `R-H004` — Graph Expansion for Multi-Hop Cross-Document Retrieval
- **Status**: `CONFIRMED` (via Retrieval Fusion Lab R4 ablation).
- **Hypothesis**: Traversing 1-hop and 2-hop structural/relational edges from seed candidate notes recovers cross-document dependencies (e.g. linking `AGENTS.md` operating limits with `vault_cognitive_rules.md` trust boundaries) that independent document scoring cannot discover.
- **Supporting Evidence**: In R4, 1-hop relational graph expansion doubled 7B model accuracy from 12.8% in R3 to 26.7% in R4 (+13.9% accuracy jump on M2), achieving the highest multi-signal performance.
- **Counterevidence**: Graph neighbor discovery increased average latency to 995.5ms (+150ms over R3).
- **Confidence**: `High`.

---

### `R-H005` — Candidate Generation as Primary Bottleneck vs Context Budget
- **Status**: `CONFIRMED` (via P0 Diagnostic and R1-R4 Lab).
- **Hypothesis**: Inability to discover and place the relevant note into the top candidate pool is a significantly larger factor in agent failure than the size limit of the final Context Pack envelope.
- **Supporting Evidence**: When candidate recall improved from 63.3% (R2) to 76.7% (R3/R4), evidence coverage climbed to 76.7%, proving candidate composition is the primary retrieval lever.
- **Counterevidence**: Once discovery is solved, `PACKING_FAILURE` occurs if the context packer truncates section text under tight soft byte limits.
- **Confidence**: `High`.

---

### `R-H006` — Diminishing Returns of Candidate Count Without Reranking
- **Status**: `CONFIRMED`.
- **Hypothesis**: Increasing candidate count ($K > 5$) without an effective cross-signal reranking or reciprocal rank fusion mechanism introduces distraction and reduces LLM attention focus on the true answer.
- **Supporting Evidence**: Reciprocal Rank Fusion (RRF) allowed R2-R4 to keep token footprint stable (~3100 tokens) while raising evidence quality.
- **Counterevidence**: Very large context models can tolerate unranked noise better than 3B/7B local models.
- **Confidence**: `High`.

---

### `R-H007` — Model Incapacity to Compensate for Absent Grounding Evidence
- **Status**: `CONFIRMED`.
- **Hypothesis**: Increasing model parameter scale (3B $\rightarrow$ 7B) cannot reliably overcome the absence of domain-specific grounding evidence in the context pack.
- **Supporting Evidence**: `qwen2.5-coder:7b` achieved only 16.7% under R1 (evidence starvation) vs 68.3% under Full Context (evidence saturation).
- **Counterevidence**: Larger models handle nuanced reasoning and formatting better once evidence is provided.
- **Confidence**: `Very High`.

---

## 📦 Context Packing Hypotheses (P1 Series)

### `P-H001` — Section-Aware Extractive Packing Improves Fact Recall Under Budget
- **Status**: `CONFIRMED` (via P1 Context Packing Lab).
- **Hypothesis**: Section-aware extractive packing substantially improves final context fact recall under the same budget compared to whole-document degradation.
- **Supporting Evidence**: Context Fact Recall jumped from `10.0%` under P0 to `76.7%` under P2, reducing packing loss rate from `76.1%` to `0.0%`.
- **Confidence**: `Very High`.

---

### `P-H002` — Required-Fact Protection Improves Answer Correctness
- **Status**: `CONFIRMED` (via P1 Context Packing Lab).
- **Hypothesis**: Required-fact protection improves answer correctness without requiring a larger context budget.
- **Supporting Evidence**: M1 (3B) accuracy increased from `11.7%` in P0 to `61.7%` in P4 (**+50.0% gain**), while prompt tokens decreased by 51% (from 2996.7 to 1468.2 tokens).
- **Confidence**: `High`.

---

### `P-H003` — Preserving Critical Negations/Exceptions Restores Guardrail Accuracy
- **Status**: `CONFIRMED` (via P1 Context Packing Lab).
- **Hypothesis**: Preserving critical negations (`NOT`, `NEVER`, `CANNOT`, `GATED`) prevents context hallucination on guardrail constraints.
- **Supporting Evidence**: `CONTRADICTION_GUARDRAIL` context fact recall leaped from `0.0%` under P0 to `77.7%` under P2/P3/P4.
- **Confidence**: `Very High`.

---

### `P-H004` — Better Packing Recovers Full-Context Gap
- **Status**: `CONFIRMED` (via P1 Context Packing Lab).
- **Hypothesis**: Better context packing can recover the gap between selective retrieval and full-context performance.
- **Supporting Evidence**: P2 extractive packing achieved `71.1%` accuracy on M2 (7B), completely recovering and exceeding the Full-Context benchmark (`68.3%`) by eliminating prompt bloat.
- **Confidence**: `Very High`.

---

## ⏳ Temporal Memory Hypotheses (P2 Series)

### `T-H001` — Temporal Filtering Improves Current-State Retrieval
- **Status**: `PARTIALLY_SUPPORTED` (via P2 Temporal Memory Lab).
- **Hypothesis**: Temporal validity filtering improves current-state precision by suppressing expired/future notes.
- **Supporting Evidence**: T1 raised 3B accuracy from 23.8% to 29.8% and 7B accuracy from 4.8% to 11.9%, but is constrained by the absence of `valid_from` fields in static disk notes.
- **Confidence**: `Medium`.

---

### `T-H002` — Supersession Traversal Resolves Version & Lineage Queries
- **Status**: `CONFIRMED` (via P2 Temporal Memory Lab).
- **Hypothesis**: Recursive supersession traversal (`supersedes` / `superseded_by`) enables retrieval of active replacement rules when given outdated seeds.
- **Supporting Evidence**: T2 increased 3B accuracy to 40.5% and resolved `Q09_TEMPORAL_SUPERSEDED_POLICY` to 100% accuracy.
- **Confidence**: `High`.

---

### `T-H003` — Temporal + Supersession Reduces False Contradictions
- **Status**: `CONFIRMED` (via P2 Temporal Memory Lab).
- **Hypothesis**: Evaluating temporal validity intervals eliminates false contradiction flags for sequentially superseded rules.
- **Supporting Evidence**: Non-overlapping intervals (`[2020-2024]` vs `[2025+]`) were correctly classified as chronological updates rather than simultaneous contradictions.
- **Confidence**: `Very High`.

---

### `T-H004` — Bi-Temporal Representation Provides Value Over Simple Filtering
- **Status**: `PARTIALLY_SUPPORTED` (via P2 Temporal Memory Lab).
- **Hypothesis**: Bi-temporal framing (distinguishing valid time from audit observation time) improves temporal reasoning clarity.
- **Supporting Evidence**: T4 correctly answered bi-temporal distinction questions, but requires note-level frontmatter population to affect general recall.
- **Confidence**: `Medium`.



