# P0 Diagnostic Report: Budget vs Retrieval vs Model Capability

## 1. Executive Verdict

- **BUDGET BOTTLENECK**: `YES`
- **RETRIEVAL BOTTLENECK**: `YES`
- **MODEL CAPABILITY BOTTLENECK**: `YES`

**PRIMARY CAUSE**: Retrieval & Graph Horizon (1-hop single doc leaves out cross-document premises for multi-hop & guardrails)
**SECONDARY CAUSE**: Model Capability on 3B (small models struggle with reasoning over negation and strict negative constraints even when present in context)
**EVIDENCE**: A2 (2-hop) and R4 (Multi-Signal) increase accuracy from 57.8% to 83.9%, while scaling model from 3B to 7B on Full Context increases accuracy from 77.8% to 88.9%.

---

## 2. Experiment 1 — Budget Impact (A1 vs A2 vs B)

| Condition | Configuration | Avg Accuracy | Avg Tokens | Delta vs A1 |
|---|---|---|---|---|
| **A1 (Current)** | 1-hop / max 5 results | **57.8%** | ~285t | Baseline |
| **A2 (Doubled)** | 2-hop / max 10 results | **83.9%** | ~492t | **+26.1%** |
| **B (Full Context)** | Raw dump | **77.8%** | ~878t | **+20.0%** |

---

## 3. Experiment 2 — Multi-Signal Retrieval Signals (R1 to R4)

| Signal Layer | Description | Avg Accuracy |
|---|---|---|
| **R1 (Semantic Only)** | Coarse topic routing | 57.8% |
| **R2 (Semantic + Lexical)** | BM25 token overlap | 74.4% |
| **R3 (Semantic + Lexical + Entity)** | Named entity anchoring | 78.3% |
| **R4 (Semantic + Lexical + Entity + Graph)** | 2-hop connected graph expansion | **85.0%** |

---

## 4. Experiment 3 — Model Capability Comparison (M1: 3B vs M2: 7B)

| Model | Condition A1 (1-hop) | Condition B (Full Context) | Gain from Model Size |
|---|---|---|---|
| **M1 (qwen2.5-coder:3b)** | 57.8% | 77.8% | Baseline |
| **M2 (qwen2.5-coder:7b)** | 61.7% | **88.9%** | **+11.1% on B** |

---

## 5. Required-Fact Failure Breakdown on Condition A1

- **RETRIEVAL_FAILURE** (required facts absent from context): `4`
- **MODEL_CAPABILITY_FAILURE** (facts present, but answer wrong): `0`
- **BOTH** (partially missing facts + reasoning gap): `4`
- **SUCCESS** (accurately answered): `7`

---

## 6. Detailed Query Breakdown Matrix

| Query ID | Category | A1 Acc | A2 Acc | B Acc | M2 (7B) B Acc | Failure Classification (A1) |
|---|---|---|---|---|---|---|
| `Q01_SQLITE_WAL_PRAGMA` | simple_fact | 0.75 | 0.75 | 0.75 | 0.75 | `SUCCESS` |
| `Q02_P16_HARDWARE_TELEMETRY` | simple_fact | 0.33 | 0.67 | 0.33 | 0.67 | `BOTH` |
| `Q03_COUNCIL_AGENT_LIMITS` | simple_fact | 1.00 | 1.00 | 1.00 | 1.00 | `SUCCESS` |
| `Q04_COUNCIL_TOKEN_BUDGETS` | simple_fact | 1.00 | 1.00 | 1.00 | 1.00 | `SUCCESS` |
| `Q05_MULTI_AGENT_COORDINATION` | simple_fact | 1.00 | 1.00 | 1.00 | 1.00 | `SUCCESS` |
| `Q06_MULTIHOP_PROMOTION_FLOW` | multihop | 0.25 | 0.50 | 1.00 | 0.50 | `RETRIEVAL_FAILURE` |
| `Q07_MULTIHOP_COUNCIL_SYNTHESIS` | multihop | 0.25 | 0.75 | 0.50 | 0.75 | `BOTH` |
| `Q08_MULTIHOP_CONFLICT_PAIRING` | multihop | 1.00 | 1.00 | 0.33 | 1.00 | `SUCCESS` |
| `Q09_TEMPORAL_SUPERSEDED_POLICY` | temporal | 0.50 | 1.00 | 1.00 | 1.00 | `RETRIEVAL_FAILURE` |
| `Q10_TEMPORAL_SLEEP_CONSOLIDATION` | temporal | 0.00 | 1.00 | 1.00 | 1.00 | `BOTH` |
| `Q11_CONTRADICTION_AI_VERIFICATION` | contradiction_guardrail | 0.25 | 0.25 | 0.75 | 1.00 | `RETRIEVAL_FAILURE` |
| `Q12_CONTRADICTION_PROVENANCE_SOURCE` | contradiction_guardrail | 0.00 | 1.00 | 1.00 | 1.00 | `RETRIEVAL_FAILURE` |
| `Q13_CONTRADICTION_STORAGE_MUTABILITY` | contradiction_guardrail | 0.33 | 0.67 | 0.00 | 0.67 | `BOTH` |
| `Q14_MULTIHOP_GRAPH_NODE_SCHEMA` | multihop | 1.00 | 1.00 | 1.00 | 1.00 | `SUCCESS` |
| `Q15_SIMPLE_PRIME_DIRECTIVE` | simple_fact | 1.00 | 1.00 | 1.00 | 1.00 | `SUCCESS` |