# Skill Semantic Evaluation V1 — Benchmark & Stratified Review

**Evaluation Date**: 2026-09-03
**Evaluator**: Antigravity Cognitive Core System Auditor (Semantic Benchmark Engine)
**Cohort Size (Unique Skills)**: `502`
**Starting Commit**: `46df686b6dafe33e8a75ae9e39efae86cd239fe4`
**Phase**: `SKILL_SEMANTIC_EVALUATION_V1`

---

## 1. Executive Summary & Required Metrics

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

## 2. Score Distributions & Statistical Baseline

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

## 3. P0 Security Cohort Deep Audit (9 Skills)

| Skill Name | Risk | Pattern Detected | Required Safeguard |
| :--- | :---: | :--- | :--- |
| `audit-skills` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `claude-in-chrome-troubleshooting` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `gcp-cloud-run` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `manage-skills` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `sandbase-mcp` | `CRITICAL` | Remote download and pipe execution (curl | bash or iex webclient) | Prohibit dynamic execution; require audited local binary or checksum-verified wheel |
| `xss-html-injection` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `aspire` | `CRITICAL` | Remote download and pipe execution (curl | bash or iex webclient) | Prohibit dynamic execution; require audited local binary or checksum-verified wheel |
| `containerize-aspnetcore` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |
| `mcp-implementation-security-review` | `HIGH` | Privileged filesystem deletion or system config modification | Enforce container isolation and least-privilege non-root execution |

> **Policy Decision**: All 9 skills are preserved with complete provenance and classified under `review_required: true`. Zero malicious code was executed.

---

## 4. Gold Standard Dual Evaluation (30 Skills)

- **Evaluated Skills**: `30`
- **Average Score Delta (|A - B|)**: **`2.20` points**
- **Classification Agreement**: **`100.0%`**
- **Disagreements Flagged for Human Review**: **`0`**

---

## 5. Quality Invariants

```text
BENCHMARK_EVALUATED_SKILLS = 502
BENCHMARK_TASKS_CREATED = 502
P0_SECURITY_AUDITED = 9
GOLD_STANDARD_EVALUATIONS = 30

RAW_CORPUS_MODIFIED = NO
INSTALLED_SKILLS_MODIFIED = NO
STATIC_LEDGER_MODIFIED = NO
```