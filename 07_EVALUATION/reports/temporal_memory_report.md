# P2 Temporal Memory Laboratory — Empirical Report

## 1. Executive Summary & Temporal Ablation Table

| Condition | Context Recall | Accuracy M1 (3B) | Accuracy M2 (7B) | Tokens | Latency (ms) |
|---|---:|---:|---:|---:|---:|
| **T0** | 51.3% | 23.8% | 4.8% | 1487.1 | 1175.5ms |
| **T1** | 51.3% | 29.8% | 11.9% | 1504.9 | 755.5ms |
| **T2** | 51.3% | 40.5% | 4.8% | 1501.0 | 727.0ms |
| **T3** | 51.3% | 29.8% | 4.8% | 1480.1 | 545.8ms |
| **T4** | 51.3% | 27.4% | 4.8% | 1572.0 | 614.2ms |

---

## 2. Temporal Metadata Audit (Vault Notes)

| Metadata Field | Present Notes | Percentage | Availability Status |
|---|---:|---:|:---:|
| `created` | 832 | 100.0% | **AVAILABLE** |
| `updated` | 832 | 100.0% | **AVAILABLE** |
| `valid_from` | 0 | 0.0% | **MISSING** |
| `valid_until` | 0 | 0.0% | **MISSING** |
| `supersedes` | 0 | 0.0% | **MISSING** |
| `superseded_by` | 0 | 0.0% | **MISSING** |
| `lifecycle` | 832 | 100.0% | **AVAILABLE** |
| `version` | 0 | 0.0% | **MISSING** |
| `observation_time` | 0 | 0.0% | **MISSING** |

---

## 3. Query Class Breakdown (Context Recall / Accuracy)

| Cognitive Class | T0 Context Rec | T1 Context Rec | T2 Context Rec | T3 Context Rec | T4 Context Rec | Best Condition |
|---|---:|---:|---:|---:|---:|---|
| `ABSTAIN_UNKNOWN` | 67.0% | 67.0% | 67.0% | 67.0% | 67.0% | **T4** |
| `BI_TEMPORAL_OBSERVATION` | 50.0% | 50.0% | 50.0% | 50.0% | 50.0% | **T4** |
| `CURRENT_STATE` | 0.0% | 0.0% | 0.0% | 0.0% | 0.0% | **T4** |
| `HISTORICAL_STATE` | 67.0% | 67.0% | 67.0% | 67.0% | 67.0% | **T4** |
| `PRIORITY_ORDERING` | 75.0% | 75.0% | 75.0% | 75.0% | 75.0% | **T4** |
| `SUPERSESSION` | 75.0% | 75.0% | 75.0% | 75.0% | 75.0% | **T4** |
| `TEMPORAL_CONTRADICTION` | 25.0% | 25.0% | 25.0% | 25.0% | 25.0% | **T4** |

---

## 4. Failure Mode Breakdown

| Condition | SUPERSESSION_DISCOVERY | TEMPORAL_FILTER_FAIL | PACKING_FAIL | MODEL_FAIL | SUCCESS |
|---|---:|---:|---:|---:|---:|
| **T0** | 1 | 0 | 0 | 6 | 0 |
| **T1** | 1 | 0 | 0 | 5 | 1 |
| **T2** | 0 | 0 | 0 | 6 | 1 |
| **T3** | 1 | 0 | 0 | 5 | 1 |
| **T4** | 1 | 0 | 0 | 5 | 1 |