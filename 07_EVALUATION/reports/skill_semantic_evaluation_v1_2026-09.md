# Skill Semantic Evaluation V1 — Benchmark & Stratified Review

**Evaluation Date**: 2026-09-03  
**Evaluator**: Antigravity Cognitive Core System Auditor (Semantic Benchmark Engine)  
**Cohort Size (Unique Skills)**: `502`  
**Total Cohort Memberships**: `534`  
**Starting Commit**: `30e10bdb029446a5846d5c8480d6d7ae1381cd04`  
**Phase**: `SKILL_SEMANTIC_EVALUATION_V1`  

```text
REPORT_RECONCILIATION:
Machine-readable artifacts are authoritative.
Narrative metrics were corrected where inconsistent.
```

---

## 1. Executive Summary & Reconciled Metrics

```text
COHORT_SIZE=502
P0_SECURITY=9
HIGH_OVERLAP=100
LOW_VALUE=50
HIGH_VALUE=100
TOOL_DEPENDENT=100
REFERENCE_ONLY=50
ENVIRONMENT_DEPENDENT=25
RANDOM_CONTROL=100

SEMANTIC_CORE=30
SEMANTIC_SPECIALIZED=353
SEMANTIC_COMPLEMENTARY=25
SEMANTIC_REDUNDANT=26
SEMANTIC_GENERIC=2
SEMANTIC_REFERENCE=56
SEMANTIC_EXPERIMENTAL=8
SEMANTIC_LOW_VALUE=0
SEMANTIC_UNSAFE=2
SEMANTIC_UNCLEAR=0

REVIEW_REQUIRED=9
```

---

## 2. Cohort Cardinality & Overlap Reconciliation

```text
Cohort memberships are intentionally overlapping.
The benchmark evaluates 502 unique skills across 534 cohort memberships.
```

- **Sum of Cohort Memberships**: **`534`** ($9 + 100 + 50 + 100 + 100 + 50 + 25 + 100 = 534$)
- **Unique Skills Across All Cohorts**: **`502`**
- **Overlapping Memberships**: **`32`**
- **Duplicate Skills Within Any Individual Cohort**: **`0`** (100% unique within each cohort)

---

## 3. Score Distributions & Statistical Baseline

- **Average Semantic Score**: **`70.97 / 100`**
- **Median Semantic Score**: **`72.0 / 100`**
- **Min Semantic Score**: **`46.5 / 100`**
- **Max Semantic Score**: **`87.0 / 100`**

### Cohort-Level Score Distributions

| Cohort | Sample Size | Average Score | Primary Semantic Class |
| :--- | :---: | :---: | :--- |
| `P0 Security Cohort` | 9 | `72.6` | `SPECIALIZED` |
| `High Overlap Cohort` | 100 | `69.2` | `SPECIALIZED` |
| `Low Value Cohort` | 50 | `64.8` | `SPECIALIZED` |
| `High Value Cohort` | 100 | `78.9` | `SPECIALIZED` |
| `Tool-Dependent Cohort` | 100 | `72.5` | `SPECIALIZED` |
| `Reference Cohort` | 50 | `61.9` | `REFERENCE` |
| `Environment-Dependent Cohort` | 25 | `71.1` | `SPECIALIZED` |
| `Random Control Cohort` | 100 | `71.3` | `SPECIALIZED` |

---

## 4. P0 Security Cohort Deep Audit (9 Skills)

| Skill Name | Risk | Pattern Detected | Required Safeguard |
| :--- | :---: | :--- | :--- |
| `audit-skills` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `claude-in-chrome-troubleshooting` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `gcp-cloud-run` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `manage-skills` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `sandbase-mcp` | `CRITICAL` | Remote download and pipe execution (curl \| bash or iex webclient) | Prohibit dynamic execution; require audited local binary or checksum-verified wheel |
| `xss-html-injection` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `aspire` | `CRITICAL` | Remote download and pipe execution (curl \| bash or iex webclient) | Prohibit dynamic execution; require audited local binary or checksum-verified wheel |
| `containerize-aspnetcore` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `mcp-implementation-security-review` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |

> **Policy Decision**: All 9 skills are preserved with complete provenance and classified under `review_required: true`. Zero malicious code was executed.

---

## 5. Gold Standard Dual Evaluation (30 Skills Reconciled)

Recomputed strictly from `gold_standard_dual_evaluation.json`:

- **Evaluated Skills**: `30`
- **Average Score Delta (|A - B|)**: **`2.20` points**
- **Classification Agreement**: **`100.0%`**
- **Major Disagreements (|Delta| >= 10 or Class Mismatch)**: **`0`**
- **Review Required in Gold Sample**: **`0`**

---

## 6. Benchmark Tasks & Ledger Cardinality

- **Benchmark Task Count**: **`502`**
- **Unique Skills Referenced in Tasks**: **`502`** (1:1 mapping with evaluated skills)
- **Semantic Ledger Records**: **`502`**
- **Unique Skill IDs in Ledger**: **`502`**
- **P0 Skills Required / Audited**: **`9 / 9`** (`UNEXECUTED = TRUE`)

---

## 7. Quality Invariants

```text
BENCHMARK_EVALUATED_SKILLS = 502
BENCHMARK_TASKS_CREATED = 502
P0_SECURITY_AUDITED = 9
GOLD_STANDARD_EVALUATIONS = 30

RAW_CORPUS_MODIFIED = NO
INSTALLED_SKILLS_MODIFIED = NO
STATIC_LEDGER_MODIFIED = NO
```
