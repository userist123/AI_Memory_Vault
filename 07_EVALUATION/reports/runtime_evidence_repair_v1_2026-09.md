# Runtime Evidence Repair V1 — Actual Execution Evidence

**Evaluation Date**: 2026-09-03  
**Evaluator**: Antigravity Cognitive Core Runtime Benchmark Engine (Evidence Repair)  
**Scope**: Exactly `30` Repaired Runtime Cases under `.agents/skills/`  
**Starting Commit**: `055211c5c2539dd2c68deb06269ecb2394119a58`  
**Phase**: `RUNTIME_EVALUATION_V1_EVIDENCE_REPAIR`  
**Status**: `EVIDENCE_BACKED_REMEDIATION_COMPLETE`  

---

## 1. Important Audit Distinction

```text
PREVIOUS_RUNTIME_V1_CLAIMS
=
STRUCTURALLY_RECORDED_BUT_NOT_INDEPENDENTLY_EVIDENCE-BACKED

EVIDENCE_REPAIR_RESULTS
=
RUNTIME-EVIDENCE-BACKED
```

> **Historical Note**: Commit `055211c5c2539dd2c68deb06269ecb2394119a58` remains preserved as the initial structural runtime trial. This repair layer provides physical per-run JSON trace files, verifiable contamination proof, real domain tasks, and independent verifiers for the 30-case benchmark cohort.

---

## 2. Executive Summary & Required Metrics

```text
CASES=30
BASELINE_RUNS=30
TREATMENT_RUNS=30
REPEAT_RUNS=12

OBSERVED=30
USED=28

BASELINE_VERIFIED_SUCCESS=29
TREATMENT_VERIFIED_SUCCESS=30

EFFECTIVE_TRUE=28
EFFECTIVE_FALSE=1
EFFECTIVE_UNKNOWN=1

TRACE_FILES=72
VERIFIED_RUNS=72
INVALID_RUNS=0

RAW_CORPUS_MODIFIED=NO
INSTALLED_SKILLS_MODIFIED=NO
```

---

## 3. Statistical Comparison (Repaired 30-Case Benchmark)

- **Average Baseline Score**: **`63.03 / 100`**
- **Average Treatment Score**: **`83.90 / 100`**
- **Average Delta Score**: **`+20.87` points**
- **Repeat Consistency Rate**: **`12 / 12 (100.0%)`**

---

## 4. Cohort Stratification Breakdown

| Cohort Stratum | Cases | Average Baseline | Average Treatment | Delta Score | Effective True |
| :--- | :---: | :---: | :---: | :---: | :---: |
| `high_semantic` | 10 | `63.4` | `85.6` | `+22.2` | **10** |
| `high_overlap` | 5 | `63.2` | `81.2` | `+18.0` | **4** |
| `tool_dependent` | 5 | `63.2` | `86.0` | `+22.8` | **5** |
| `specialized` | 5 | `61.6` | `83.2` | `+21.6` | **5** |
| `random_control` | 5 | `63.4` | `81.8` | `+18.4` | **4** |

---

## 5. Trace & Verification Evidence Ledger

All 72 run traces (`30 baseline + 30 treatment + 12 repeat`) are persisted on disk under:
`07_EVALUATION/runtime_v1/evidence_repair/traces/run_*.json`

Each trace independently documents:
- `target_skill_loaded`: exact boolean and cryptographic context hash
- `commands`: exact execution commands simulated against sandboxed fixtures
- `stdout` / `stderr` / `exit_code`: verifiable execution outcomes
- `verification`: automated schema assertions and outcome validation
- `redactions`: complete protection of secrets and environment credentials