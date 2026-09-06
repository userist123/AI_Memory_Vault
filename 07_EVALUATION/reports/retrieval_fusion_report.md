# Retrieval Fusion Laboratory — R1→R4 Empirical Report

## 1. Executive Summary & Ablation Table

| Strategy | Candidate Recall | Fact Recall (Cov) | Context Recall | Accuracy M1 (3B) | Accuracy M2 (7B) | Tokens | Latency (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|
| **R1** | 66.7% | 69.4% | 6.7% | 13.3% | 16.7% | 3198.2 | 595.2ms |
| **R2** | 63.3% | 73.3% | 6.7% | 15.0% | 13.9% | 3265.0 | 511.5ms |
| **R3** | 76.7% | 76.7% | 8.3% | 10.0% | 12.8% | 3049.6 | 845.9ms |
| **R4** | 70.0% | 76.7% | 10.0% | 15.0% | 26.7% | 3166.3 | 995.5ms |
| **FULL_CONTEXT** | 100.0% | 71.1% | 71.1% | 63.9% | 68.3% | 2204.7 | 973.1ms |

---

## 2. Signal Contribution Deltas (Ablation Insights)

- **Δ R2 - R1 (Lexical BM25 Contribution)**: Fact Recall +3.9%, M1 Accuracy +1.7%, M2 Accuracy +-2.8%
- **Δ R3 - R2 (Entity Anchor Contribution)**: Fact Recall +3.3%, M1 Accuracy +-5.0%, M2 Accuracy +-1.1%
- **Δ R4 - R3 (Graph Neighbor Expansion)**: Fact Recall +0.0%, M1 Accuracy +5.0%, M2 Accuracy +13.9%

---

## 3. Query Class Analysis

| Cognitive Class | R1 Fact Recall | R2 Fact Recall | R3 Fact Recall | R4 Fact Recall | Dominant Helpful Signal |
|---|---:|---:|---:|---:|---|
| `SIMPLE_FACT` | 88.8% | 94.5% | 94.5% | 94.5% | **Lexical BM25 (R2)** |
| `MULTI_HOP` | 62.5% | 62.5% | 68.8% | 68.8% | **Entity Boosting (R3)** |
| `TEMPORAL` | 37.5% | 37.5% | 37.5% | 37.5% | **Entity Boosting (R3)** |
| `CONTRADICTION_GUARDRAIL` | 61.0% | 69.3% | 77.7% | 77.7% | **Entity Boosting (R3)** |

---

## 4. Failure Mode Breakdown

| Strategy | DISCOVERY_FAILURE | RANKING_FAILURE | PACKING_FAILURE | MODEL_FAILURE | SUCCESS |
|---|---:|---:|---:|---:|---:|
| **R1** | 4 | 3 | 7 | 1 | 0 |
| **R2** | 5 | 1 | 8 | 1 | 0 |
| **R3** | 3 | 1 | 10 | 1 | 0 |
| **R4** | 4 | 1 | 9 | 1 | 0 |

## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
