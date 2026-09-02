# P1 Context Packing Laboratory — Empirical Report

## 1. Executive Summary & Ablation Table

| Strategy | Context Recall | Packing Loss Rate | Accuracy M1 (3B) | Accuracy M2 (7B) | Tokens | Bytes | Latency (ms) | Gap Recov (M2) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| **P0** | 10.0% | 76.1% | 11.7% | 15.6% | 2996.7 | 4884B | 1128.5ms | 0.0% |
| **P1** | 76.7% | 0.0% | 17.2% | 25.0% | 959.7 | 12829B | 703.3ms | 100.0% |
| **P2** | 76.7% | 0.0% | 56.7% | 71.1% | 1491.6 | 5388B | 1291.4ms | 588.2% |
| **P3** | 74.4% | 3.3% | 54.3% | 60.0% | 1465.2 | 5388B | 915.7ms | 470.6% |
| **P4** | 74.4% | 3.3% | 61.7% | 63.3% | 1468.2 | 5364B | 966.5ms | 505.9% |

---

## 2. Full-Context Gap Recovery

- **P0 (Production Baseline)**: Context Recall = 10.0%, M2 Accuracy = 15.6%
- **P1 (Full Context Oracle)**: Context Recall = 76.7%, M2 Accuracy = 25.0%
- **P2**: Context Recall = 76.7% (Gap Recovered: 100.0%), M2 Accuracy = 71.1% (Gap Recovered: 588.2%)
- **P3**: Context Recall = 74.4% (Gap Recovered: 96.6%), M2 Accuracy = 60.0% (Gap Recovered: 470.6%)
- **P4**: Context Recall = 74.4% (Gap Recovered: 96.6%), M2 Accuracy = 63.3% (Gap Recovered: 505.9%)

---

## 3. Query Class Breakdown (Context Recall / Accuracy)

| Cognitive Class | P0 Context Rec | P1 Context Rec | P2 Context Rec | P3 Context Rec | P4 Context Rec | Best Strategy |
|---|---:|---:|---:|---:|---:|---|
| `SIMPLE_FACT` | 16.7% | 94.5% | 94.5% | 88.8% | 88.8% | **P2** |
| `MULTI_HOP` | 6.2% | 68.8% | 68.8% | 68.8% | 68.8% | **P3 / P4** |
| `TEMPORAL` | 12.5% | 37.5% | 37.5% | 37.5% | 37.5% | **P3 / P4** |
| `CONTRADICTION_GUARDRAIL` | 0.0% | 77.7% | 77.7% | 77.7% | 77.7% | **P3 / P4** |

---

## 4. Failure Mode Breakdown

| Strategy | BUDGET_FAILURE | SECTION_SELECTION_FAILURE | NEGATION_LOSS | TEMPORAL_LOSS | MODEL_FAILURE | SUCCESS |
|---|---:|---:|---:|---:|---:|---:|
| **P0** | 1 | 10 | 0 | 1 | 2 | 1 |
| **P1** | 0 | 0 | 0 | 0 | 14 | 1 |
| **P2** | 0 | 0 | 0 | 0 | 9 | 6 |
| **P3** | 1 | 0 | 0 | 0 | 10 | 4 |
| **P4** | 1 | 0 | 0 | 0 | 9 | 5 |