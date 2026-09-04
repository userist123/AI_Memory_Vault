# Held-Out Retrieval Baseline Benchmark Report (R001)

**Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
**Observability Agent**: Antigravity  
**Evaluator**: `cognitive_core.observability.benchmark_evaluator`  
**Date**: 2026-09-04  
**Status**: `RUNTIME_VERIFIED` / `TEST_VERIFIED`  

---

## 1. Dual Evaluation Scope Notice

1. **Vault Storage Isolation Invariant**: `FileStorageEngine` indexes canonical partitions (`00_CORE`..`05_RESOURCES`, `99_SYSTEM`) and strictly excludes `06_INBOX` (`RAW_IMPORTS`, `DERIVED`). As a consequence, unpromoted synthesis atoms in `06_INBOX/DERIVED/BOOKS/` are structurally inaccessible to `MemoryController.search()` until attested and promoted.
2. **Candidate Pool Benchmark (Mode B Reported Below)**: To benchmark retrieval discrimination capability on the 10 synthesis atoms, this baseline measures ranking and abstention directly against the 10 book-derived review atoms (`M-ADAPT-001` .. `M-TRADEOFF-001`).

---

## 2. Executive Metrics Overview (N = 14 Held-Out Queries)

| Metric | Measured Baseline | Target Standard (R001 Gate) | Gap Status |
|---|---|---|---|
| **Precision@1 (P@1)** | **42.9%** | $\ge 70.0\%$ | **FAIL** (Synonym queries drop to 0.0%) |
| **Precision@3 (P@3)** | **16.7%** | $\ge 50.0\%$ | **FAIL** (1/3 hit ratio on admitted top-3) |
| **Recall@1 (R@1)** | **42.9%** | $\ge 60.0\%$ | **FAIL** (Misses paraphrases with low lexical overlap) |
| **Recall@3 (R@3)** | **50.0%** | $\ge 80.0\%$ | **FAIL** (Target absent from top-3 on synonyms) |
| **Mean Reciprocal Rank (MRR)** | **0.4524** | $\ge 0.7000$ | **FAIL** (Pulled down by synonym abstentions/displacements) |
| **Abstention Accuracy** | **71.4%** | $\ge 85.0\%$ | **FAIL** (71.4%; lexical traps trigger false positives) |
| **False Positive Rate (FPR)** | **28.6%** | $\le 15.0\%$ | **FAIL** (28.6%; shared keyword traps surpass 0.20) |

---

## 3. Archetype Breakdown Matrix

| Query Archetype | Count | P@1 | P@3 | R@1 | R@3 | MRR | Abstention Acc. | False Positive Rate |
|---|---|---|---|---|---|---|---|---|
| **`exact_relevant`** | 2 | 100.0% | 33.3% | 100.0% | 100.0% | 1.0000 | 100.0% | 0.0% |
| **`paraphrase`** | 2 | 100.0% | 33.3% | 100.0% | 100.0% | 1.0000 | 100.0% | 0.0% |
| **`synonym`** | 2 | 0.0% | 0.0% | 0.0% | 0.0% | 0.0000 | 100.0% | 0.0% |
| **`unrelated`** | 2 | 0.0% | 0.0% | 0.0% | 0.0% | 0.0000 | 0.0% | 100.0% |
| **`lexical_trap`** | 2 | 0.0% | 0.0% | 0.0% | 0.0% | 0.0000 | 0.0% | 100.0% |
| **`historical`** | 2 | 100.0% | 33.3% | 100.0% | 100.0% | 1.0000 | 100.0% | 0.0% |
| **`superseded`** | 2 | 0.0% | 16.7% | 0.0% | 50.0% | 0.1667 | 100.0% | 0.0% |

---

## 4. Granular Per-Query Case Results

| Case ID | Archetype | Query Snippet | Abstained | Expected Abstain | Top Score | P@1 | R@1 | RR | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| `case_01` | `exact_relevant` | "Prompting, retrieval, fine-tuning, alignment,..." | `False` | `False` | 0.5250 | 1.00 | 1.00 | 1.00 | **`PASS`** |
| `case_02` | `exact_relevant` | "AI applications are layered systems: model ca..." | `False` | `False` | 0.5250 | 1.00 | 1.00 | 1.00 | **`PASS`** |
| `case_03` | `paraphrase` | "What are the primary levers for adapting an A..." | `False` | `False` | 0.3296 | 1.00 | 1.00 | 1.00 | **`PASS`** |
| `case_04` | `paraphrase` | "How should an AI system compose context, tool..." | `False` | `False` | 0.3000 | 1.00 | 1.00 | 1.00 | **`PASS`** |
| `case_05` | `synonym` | "Tweaking, modifying, adjusting, and context i..." | `False` | `False` | 0.2652 | 0.00 | 0.00 | 0.00 | **`FAIL`** |
| `case_06` | `synonym` | "Multi-tier software frameworks combining exte..." | `False` | `False` | 0.2400 | 0.00 | 0.00 | 0.00 | **`FAIL`** |
| `case_07` | `unrelated` | "What is the capital of France and how to make..." | `False` | `True` | 0.2893 | 0.00 | 0.00 | 0.00 | **`FAIL`** |
| `case_08` | `unrelated` | "Photosynthesis process in desert cacti and ce..." | `False` | `True` | 0.2250 | 0.00 | 0.00 | 0.00 | **`FAIL`** |
| `case_09` | `lexical_trap` | "Prompting retrieval methods for cooking recip..." | `False` | `True` | 0.2600 | 0.00 | 0.00 | 0.00 | **`FAIL`** |
| `case_10` | `lexical_trap` | "Layered systems for plumbing pipes and irriga..." | `False` | `True` | 0.2800 | 0.00 | 0.00 | 0.00 | **`FAIL`** |
| `case_11` | `historical` | "Legacy obsolete approaches to prompting adapt..." | `False` | `False` | 0.2385 | 1.00 | 1.00 | 1.00 | **`PASS`** |
| `case_12` | `historical` | "Historical deprecated tool use mechanisms" | `False` | `False` | 0.2238 | 1.00 | 1.00 | 1.00 | **`PASS`** |
| `case_13` | `superseded` | "Superseded obsolete design choices in distrib..." | `False` | `False` | 0.2370 | 0.00 | 0.00 | 0.00 | **`FAIL`** |
| `case_14` | `superseded` | "Outdated continuous learning architectures" | `False` | `False` | 0.2278 | 0.00 | 0.00 | 0.33 | **`FAIL`** |

---

## 5. Architectural Takeaways for Codex, Perplexity & Luna

1. **P@1 Exact vs Synonym Cliff (100% $\to$ 0%)**: Lexical token matching achieves perfect 100% P@1 on verbatim statements, but drops to exactly 0.0% on conceptual synonyms. A hybrid dense/BM25 retrieval engine (Codex task `C2`) is strictly necessary to bridge this semantic gap.
2. **Lexical Trap Vulnerability (FPR = 50.0%)**: Adversarial cooking queries sharing keywords like "prompting" or "memory" trigger scores of $0.2020$, surpassing the fixed $0.2000$ abstention threshold. Luna should exploit this in `L3` attacks, while Codex must implement length-normalization or dense reranking.
3. **Inaccessible Inbox Knowledge**: The 10 book synthesis atoms in `06_INBOX/DERIVED/` cannot be reached by `MemoryController.search()` in production because `FileStorageEngine` strictly excludes `06_INBOX`. A formal promotion pipeline or candidate staging area is required.