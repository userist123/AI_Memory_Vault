# Cognitive Interference Gate: Retrieval Architecture & Mathematical Specification

> **Research Document**: `08_RESEARCH/RETRIEVAL/COGNITIVE_INTERFERENCE_GATE_DESIGN.md`  
> **Topic**: Retrieval under indirect/associative cues without working memory context saturation or proactive interference.  
> **Theoretical Foundation**: ACT-R (Anderson, Lewis & Vasishth), Soar (Laird), hippocampal pattern separation (Yassa et al.), and bounded spreading activation.  
> **Integrity Constraint**: Notes remain 100% inert Markdown data; all mutable scores, activation states, and vector indices reside strictly in sidecar structures.

---

## 1. Executive Problem Statement

In the persistent AI Memory Vault, naive expansion (unbounded spreading activation or broad semantic recall) introduces **Proactive Interference** and **Context Dilution**:
1. When query lexical overlap (BM25) is weak or indirect, naive dense retrieval pulls topically related but functionally misleading distractors.
2. Unbounded graph traversal inflates token cost, violating the strict `MAX_SYNTHESIS_INPUT = 2500` and working memory budget ($1,500$ tokens).
3. Hard collisions between near-duplicate or historically updated notes pollute agent decision-making.

### Solution Principle: *Retrieve Broadly, Admit Narrowly*
Separate retrieval into two explicit stages:
- **Candidate Generation (High Recall)**: Parallel BM25 + Dense + Weighted RRF + Bounded 1-Hop Graph.
- **Cognitive Interference Gate (High Precision & Bounded Diversity)**: Activation scoring, mismatch penalty, competitor density suppression, and budgeted MMR knapsack selection.

---

## 2. Mathematical Scoring Model

For each candidate note/span $i$, the raw cognitive activation score $A_i^{raw}$ is defined as:

$$A_i^{raw} = \alpha R_i + \beta B_i + \chi C_i + \delta G_i - \lambda_m M_i - \lambda_f F_i - \lambda_t T_i$$

### Components:
1. **$R_i$ (Calibrated Reranker Probability)**:
   Calibrated cross-encoder score for the query-passage pair $(q, \text{span}_i)$.
2. **$B_i$ (Base-Level Activation / Recency & Frequency)**:
   Derived from ACT-R historical usage:
   $$B_i = \min\left(B_{max}, \ln \left(\sum_{k=1}^{n_i} (t_{now} - t_{ik} + \epsilon)^{-d}\right)\right)$$
   *(Clipping prevents "popularity equals truth" bias).*
3. **$C_i$ (Cue Support with Fan Effect)**:
   $$C_i = \sum_{j \in J_q} W_j m_{ji} (S_{max} - \ln(1 + \text{fan}_j))$$
   Where $\text{fan}_j$ penalizes generic cues appearing in hundreds of notes.
4. **$G_i$ (Bounded 1-Hop Graph Activation)**:
   $$G_i = \max_{s \in Seeds} \left[ p_s \cdot w_{sv} \cdot r_{type(s, v)} \cdot e^{-\kappa \Delta t_{sv}} \right]$$
   *(Uses $\max$ rather than unbounded sum to prevent highly-connected hubs from dominating).*
5. **$M_i$ (Mismatch Penalty)**:
   $$M_i = \frac{\sum_{j \in J_q} W_j \rho_j (1 - \text{sim}_j(q_j, m_{ij}))}{\sum_{j \in J_q} W_j \rho_j + \epsilon}$$
6. **$F_i$ (Competitor Density / Local Confusion Penalty)**:
   $$F_i = \log \left( 1 + \sum_{k \in \mathcal{N}_i} \exp \left( \frac{\text{sim}(e_i, e_k) - \mu}{\tau_s} \right) \right)$$
7. **$T_i$ (Temporal / Stale Version Penalty)**:
   Penalizes superseded, unverified, or out-of-date records.

---

## 3. Set-Level Selection & Working Memory Knapsack

To avoid context redundancy, candidates are admitted iteratively using **Maximal Marginal Relevance (MMR)**:

$$A_i^{set} = A_i^{raw} - \lambda_r \max_{s \in S} \text{sim}_{mem}(i, s)$$

### Admission Invariants:
1. **Absolute Threshold**: $A_i^{set} \ge \theta_{abs}$ (rejects low-confidence noise).
2. **Confidence Margin**: $A_i^{set} - A_{runner\text{-}up}^{same\_cluster} \ge \theta_{margin}$ (abstains when two competing memories cannot be separated).
3. **Knapsack Token Constraint**:
   $$\max_{S} \sum_{i \in S} A_i^{set} \quad \text{s.t.} \quad \sum_{i \in S} \text{tokens}_i \le 1500, \quad |S| \le K_{wm}$$

---

## 4. Pipeline Execution & Deadline Budget (< 50 ms)

| Stage | Budget (p95) | Action | Fallback on Timeout |
|---|---|---|---|
| **Query Parsing & Embed** | 6 ms | Extract structured cues, intent, entities | Fallback to raw query |
| **BM25 + Dense Search** | 8 ms | Parallel retrieval (top 40 BM25, top 40 Dense) | Return available branch |
| **RRF Fusion & Seed Gate** | 1 ms | Weighted RRF producing $\le 50$ pool | Top 8 seeds |
| **1-Hop Bounded Expansion** | 3 ms | $\le 5$ neighbors/seed, cap 24 nodes | Skip graph expansion |
| **Cross-Encoder Rerank** | 24 ms | Top 24 batched rerank | Use RRF ranks |
| **Interference Gate & MMR** | 3 ms | Apply $A_i^{set}$ and competitor density | Top-k by $A_i^{raw}$ |
| **Context Packing** | 5 ms | Pack evidence spans under 1500 tokens | Return partial set |
| **TOTAL** | **50 ms** | Deadline-enforced execution | Deterministic fallback |

---

## 5. Sidecar Storage Separation Architecture

To protect repository immutability:
- **Inert Source of Truth**: Markdown notes under `01_ARCHITECTURE/`, `02_PRODUCT/`, `10_DOCUMENTATION/`.
- **Sidecar Indices**:
  - `lexical_index.db`: BM25 inverted index.
  - `vector_index.bin`: HNSW embeddings on evidence spans.
  - `synapse_graph.db`: Typed graph adjacency table.
  - `activation_ledger.jsonl`: Timestamped access history (append-only).
  - `retrieval_cache.db`: Ephemeral query cache with FINST-style suppression.
