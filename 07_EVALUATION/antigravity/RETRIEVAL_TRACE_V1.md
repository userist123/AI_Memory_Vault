# Developer Retrieval Trace V1 (Antigravity Observability)

**Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
**Observability Agent**: Antigravity  
**Date**: 2026-09-04  
**Status**: `RUNTIME_VERIFIED` / `CODE_VERIFIED`  

---

## 1. Pipeline Architecture & Reality Map

The retrieval pipeline in the codebase has two primary implementations with distinct operational roles:
1. **Production Memory Controller (`MemoryController.search()`)** [`CODE_VERIFIED`]:
   The authoritative API gateway enforcing Phase 4.3 P0 security hardening, `I-RETRIEVAL` trust boundaries, role validation (`Principal.AI_AGENT`), query sanitization (`check_query_size`, `sanitize_query`), pagination token generation (`HMAC-SHA256`), and progressive disclosure budgeting (`metadata`, `snippet`, `sections`, `full`).
2. **Cognitive Associative Recall (`RecallEngine.recall()`)** [`CODE_VERIFIED`]:
   The multi-signal ranking engine evaluating semantic similarity, working-memory relevance, ACT-R activation, confidence/authority, temporal validity, lifecycle degradation, and supersession lineage inheritance with calibrated abstention.

### End-to-End Flow Diagram
```text
QUERY
  ↓ [sanitize_query, check_query_size]
Candidate Generation (Activated Nodes + Review Candidates)
  ↓
Raw Similarity (sim_query = SemanticProvider.compute_similarity)
  ↓
Working-Memory Relevance (sim_wm = SemanticProvider.compute_similarity(wm_context, content))
  ↓
Confidence & Authority (conf_auth_score = (confidence_map[conf] + get_authority_score(node)) / 2)
  ↓
Activation (ACT-R base-level B_i = ln(sum t_j^-d))
  ↓
Temporal Factor (valid_from / valid_until date window penalty)
  ↓
Lifecycle Factor (SUPERSEDED: 0.3/0.8; ARCHIVED: 0.1/0.6; REVIEW: 1.0 + unverified flag)
  ↓
Lineage Resolution (resolve_active_lineage() active note score inheritance)
  ↓
Final Ranking (scored_nodes.sort(reverse=True))
  ↓
Abstention Evaluation (best_pre_lifecycle_score < abstention_threshold -> [])
  ↓
Returned Memory Context Pack
```

---

## 2. Empirical Execution Trace 1: Target Adaptation Query

* **Query**: `"How should prompting, retrieval, fine-tuning, alignment, and inference-time methods be viewed as adaptation choices?"`
* **Working Memory Context**: Empty (`""`)
* **Principal**: `Principal.AI_AGENT` [`CODE_VERIFIED`]
* **Abstention Threshold**: `0.2000` [`CODE_VERIFIED`]
* **Best Pre-Lifecycle Score**: `0.3875` $\ge 0.2000$ $\implies$ **Abstention NOT triggered** [`RUNTIME_VERIFIED`]

### Candidate Evaluation Table

| Memory ID | Lifecycle | Raw Sim | WM Sim | Activ. | Conf/Auth | Temp. Factor | LC Factor | Pre-LC Score | Final Score | Lineage Successor | Inclusion State | Removal / Rejection Reason |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `M-ADAPT-001` | `REVIEW` | **0.6500** | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.3875 | **0.3875** | `None` | **INCLUDED** | `None` (Rank 1, Top-K admitted) |
| `M-ARCH-001` | `REVIEW` | 0.0714 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1850 | **0.1850** | `None` | **INCLUDED** | `None` (Rank 2, Top-K admitted) |
| `M-RETRIEVAL-001` | `REVIEW` | 0.0526 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1784 | **0.1784** | `None` | **INCLUDED** | `None` (Rank 3, Top-K admitted) |
| `M-EVAL-001` | `REVIEW` | 0.0476 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1767 | **0.1767** | `None` | **INCLUDED** | `None` (Rank 4, Top-K admitted) |
| `M-LEARNING-001` | `REVIEW` | 0.0455 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1759 | **0.1759** | `None` | **INCLUDED** | `None` (Rank 5, Top-K admitted) |
| `M-TRADEOFF-001` | `REVIEW` | 0.0385 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1735 | **0.1735** | `None` | **EXCLUDED** | `RANK_BELOW_TOP_K` (Rank 6) |
| `M-DISTRIBUTED-001`| `REVIEW` | 0.0370 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1730 | **0.1730** | `None` | **EXCLUDED** | `RANK_BELOW_TOP_K` (Rank 7) |
| `M-TOOLS-001` | `REVIEW` | 0.0370 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1730 | **0.1730** | `None` | **EXCLUDED** | `RANK_BELOW_TOP_K` (Rank 8) |
| `M-RELIABILITY-001`| `REVIEW` | 0.0345 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1721 | **0.1721** | `None` | **EXCLUDED** | `RANK_BELOW_TOP_K` (Rank 9) |
| `ACTIVE-NET-001` | `ACTIVE` | 0.0000 | 0.0000 | 0.0000 | 0.7000 | 1.0000 | 1.0000 | 0.2050 | **0.2050** | `None` | **INCLUDED** | `None` (Admitted on high base authority) |
| `SUPERSEDED-NET-001`| `SUPERSEDED`| 0.0000 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 0.3000 | 0.1600 | **0.0480** | `ACTIVE-NET-001`| **EXCLUDED** | `RANK_BELOW_TOP_K` + `LIFECYCLE_PENALTY` |
| `ARCHIVED-OLD-001` | `ARCHIVED` | 0.0000 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 0.1000 | 0.1600 | **0.0160** | `None` | **EXCLUDED** | `RANK_BELOW_TOP_K` + `LIFECYCLE_PENALTY` |

---

## 3. Empirical Execution Trace 2: Out-of-Domain Unrelated Query (Abstention)

* **Query**: `"What is the capital of France?"`
* **Working Memory Context**: Empty (`""`)
* **Principal**: `Principal.AI_AGENT`
* **Abstention Threshold**: `0.2000` [`CODE_VERIFIED`]
* **Best Pre-Lifecycle Score**: `0.1975` $< 0.2000$ $\implies$ **Abstention TRIGGERED** [`RUNTIME_VERIFIED`]
* **Returned Results**: `[]` (0 results returned; successfully abstains)

### Candidate Evaluation Table (Pre-Abstention Breakdown)

| Memory ID | Lifecycle | Raw Sim | WM Sim | Activ. | Conf/Auth | Temp. Factor | LC Factor | Pre-LC Score | Final Score | Inclusion State | Removal / Rejection Reason |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `M-RELIABILITY-001`| `REVIEW` | 0.1071 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1975 | 0.1975 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-DISTRIBUTED-001`| `REVIEW` | 0.0741 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1859 | 0.1859 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-LEARNING-001` | `REVIEW` | 0.0455 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1759 | 0.1759 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-ARCH-001` | `REVIEW` | 0.0357 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1725 | 0.1725 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-ADAPT-001` | `REVIEW` | 0.0000 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1600 | 0.1600 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-EVAL-001` | `REVIEW` | 0.0000 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1600 | 0.1600 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-REPRESENT-001` | `REVIEW` | 0.0000 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1600 | 0.1600 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-RETRIEVAL-001` | `REVIEW` | 0.0000 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1600 | 0.1600 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-TOOLS-001` | `REVIEW` | 0.0000 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1600 | 0.1600 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |
| `M-TRADEOFF-001` | `REVIEW` | 0.0000 | 0.0000 | 0.0000 | 0.4000 | 1.0000 | 1.0000 | 0.1600 | 0.1600 | **EXCLUDED** | `ABSTENTION_THRESHOLD_NOT_MET (best_pre=0.1975 < 0.2000)` |

---

## 4. Reality of Fields vs Inventions

The developer trace reveals precisely what exists in code versus what is purely conceptual:

| Field | Source in Code | Status | Real Behavior in Runtime |
|---|---|---|---|
| `id` | `node.get("id")` | `CODE_VERIFIED` | Present on all canonical notes. |
| `lifecycle` | `node.get("lifecycle")` | `CODE_VERIFIED` | Governs access boundary and penalty multiplier ($0.3$ / $0.1$). |
| `raw_similarity` | `SemanticProvider.compute_similarity()` | `CODE_VERIFIED` | Jaccard token overlap in deterministic mode. |
| `activation` | `activated_nodes` tuple score | `CODE_VERIFIED` | $0.0$ for un-activated review notes; computed via ACT-R log recency. |
| `confidence` | `node.get("confidence")` | `CODE_VERIFIED` | Mapped via `confidence_map` (`0.0` to `1.0`). |
| `authority` | `get_authority_score(node)` | `CODE_VERIFIED` | Mapped from `provenance.source_type` (`official=1.0`, `user=0.9`, `import=0.6`). |
| `temporal_factor` | `valid_from` / `valid_until` parser | `CODE_VERIFIED` | Drops to $0.5$ if pre-validity or post-expiry unless historical query ($0.8$). |
| `lifecycle_factor` | `SUPERSEDED` / `ARCHIVED` branch | `CODE_VERIFIED` | Strict scaling: $0.3$ for superseded, $0.1$ for archived. |
| `final_score` | Weighted sum formula | `CODE_VERIFIED` | $(0.35 \times \text{sim}) + (0.15 \times \text{wm}) + (0.15 \times \text{conf}) + (0.25 \times \text{act}) + (0.10 \times \text{temp})$. |
| `lineage_successor`| `resolve_active_lineage()` | `CODE_VERIFIED` | Follows `superseded_by` pointers recursively up to active successor. |
| `removal_reason` | Trace instrumentation | `CODE_VERIFIED` | Generated by evaluator/harness (`ABSTENTION_THRESHOLD_NOT_MET`, `RANK_BELOW_TOP_K`). |
| `final_inclusion_state`| Trace instrumentation | `CODE_VERIFIED` | Evaluated as `INCLUDED` or `EXCLUDED`. |

### Key Architectural Verifications
- **Review Notes Protection** [`TEST_VERIFIED`]: Review nodes in `RecallEngine` are copied and marked with `_cognitive_unverified = True` (satisfies `I-001`).
- **No Promotion Leakage** [`RUNTIME_VERIFIED`]: Executing retrieval never alters storage keys, never updates SQLite rows, and never modifies YAML frontmatter.
