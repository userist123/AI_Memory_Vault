# Skill Quality Evaluation V1 — Static Baseline for 3,450 Installed Skills

**Evaluation Date**: 2026-09-03
**Evaluator**: Antigravity Cognitive Core System Auditor (Static Baseline Engine)
**Evaluation Scope**: Exactly `3,450 INSTALLED_SKILLS` under `.agents/skills/`
**Starting Commit**: `eb88cd81aac901a11ab7e3a45ba3eddc3b695260`
**Phase**: `SKILL_QUALITY_EVALUATION_V1`

---

## 1. Executive Summary & Required Metrics

```text
INSTALLED_SKILLS=3450

VALID=3449
MINIMAL=1
INCOMPLETE=0
BROKEN=0

DIRECT=192
TOOL_DEPENDENT=2518
ENVIRONMENT_DEPENDENT=47
REFERENCE_ONLY=362
UNCLEAR=331

SECURITY_NONE=3073
SECURITY_LOW=157
SECURITY_MEDIUM=211
SECURITY_HIGH=7
SECURITY_CRITICAL=2

DUPLICATION_NONE=2747
DUPLICATION_EXACT=0
DUPLICATION_NEAR=0
DUPLICATION_HIGH_OVERLAP=349
DUPLICATION_SEMANTIC=354

P0=9
P1=2166
P2=1262
P3=13
```

---

## 2. Structural Quality Distribution

| Structural Status | Count | Percentage | Description |
| :--- | :---: | :---: | :--- |
| `VALID` | 3,449 | 100.0% | Complete bundle, valid entry, coherent metadata |
| `MINIMAL` | 1 | 0.0% | Standalone SKILL.md without supporting scripts/references |
| `INCOMPLETE` | 0 | 0.0% | Missing referenced internal assets |
| `BROKEN` | 0 | 0.0% | Empty, unreadable, or missing SKILL.md |

---

## 3. Documentation Score Rubric & Distribution

### Deterministic Scoring Rubric (0-100 Points)
- **YAML Frontmatter Present** (`---` block at start): `10 pts`
- **Skill Name Defined** (`name:` in frontmatter or `# Title`): `10 pts`
- **Description Defined** (`description:` in frontmatter): `10 pts`
- **Purpose / Objective Present** (`## Purpose` / `## Overview`): `10 pts`
- **Instructions / Steps Present** (`## Instructions` / `## Steps`): `15 pts`
- **Workflow / Process Defined** (`## Workflow` / `## Process`): `10 pts`
- **Inputs / Parameters Defined** (`## Inputs` / `## Arguments`): `5 pts`
- **Outputs / Deliverables Defined** (`## Outputs` / `## Returns`): `5 pts`
- **Examples Included** (`## Examples` / code fences): `10 pts`
- **Constraints / Rules Defined** (`## Constraints` / `## Rules`): `5 pts`
- **Failure Modes / Troubleshooting** (`## Troubleshooting` / `Errors`): `5 pts`
- **Verification Steps Defined** (`## Verification` / `Validation`): `5 pts`

**Average Documentation Score**: **`56.46 / 100`**

| Score Range | Count | Percentage | Documentation Tier |
| :--- | :---: | :---: | :--- |
| `90-100 (Exemplary)` | 17 | 0.5% |
| `70-89 (Strong)` | 520 | 15.1% |
| `50-69 (Adequate)` | 2,111 | 61.2% |
| `30-49 (Basic)` | 802 | 23.2% |
| `0-29 (Incomplete)` | 0 | 0.0% |

---

## 4. Execution Readiness Classification

| Execution Readiness | Count | Percentage | Rationale |
| :--- | :---: | :---: | :--- |
| `DIRECT` | 192 | 5.6% | Procedural applicability |
| `TOOL_DEPENDENT` | 2,518 | 73.0% | Procedural applicability |
| `ENVIRONMENT_DEPENDENT` | 47 | 1.4% | Procedural applicability |
| `REFERENCE_ONLY` | 362 | 10.5% | Procedural applicability |
| `UNCLEAR` | 331 | 9.6% | Procedural applicability |

---

## 5. Dependency Analysis & Ecosystem Footprint

| Ecosystem / Tooling | Referenced / Declared Count | Percentage of Corpus |
| :--- | :---: | :---: |
| `mcp` | 1,069 | 31.0% |
| `shell` | 1,016 | 29.4% |
| `pypi` | 655 | 19.0% |
| `npm` | 584 | 16.9% |
| `docker` | 346 | 10.0% |
| `nuget` | 84 | 2.4% |
| `cargo` | 51 | 1.5% |
| `go` | 37 | 1.1% |

---

## 6. Security Static Scan Analysis

| Security Risk Level | Count | Percentage | Nature of Detected Patterns |
| :--- | :---: | :---: | :--- |
| `NONE` | 3,073 | 89.1% | Non-executing regex detection |
| `LOW` | 157 | 4.6% | Non-executing regex detection |
| `MEDIUM` | 211 | 6.1% | Non-executing regex detection |
| `HIGH` | 7 | 0.2% | Non-executing regex detection |
| `CRITICAL` | 2 | 0.1% | Non-executing regex detection |

> **Note**: Security flags indicate static regex patterns in instructional text (e.g. bash commands or setup examples), not confirmed malware. No external code was executed.

---

## 7. Static Utility Score (0-100) & Maintainability

### Deterministic Utility Formula
$$\text{STATIC\_UTILITY} = \text{Specificity (15)} + \text{Actionability (20)} + \text{Completeness (20)} + \text{Reusability (15)} + \text{Tool Awareness (10)} + \text{Verification (10)} + \text{Examples (10)}$$

**Average Static Utility Score**: **`70.12 / 100`**

### Maintainability Risk Breakdown
| Maintainability Risk | Count | Percentage |
| :--- | :---: | :---: |
| `LOW` | 3,061 | 88.7% |
| `MEDIUM` | 326 | 9.4% |
| `HIGH` | 63 | 1.8% |
| `UNKNOWN` | 0 | 0.0% |

---

## 8. Evaluation Priority Assignment

| Evaluation Priority | Count | Percentage | Recommended Action |
| :--- | :---: | :---: | :--- |
| `P0_CRITICAL_REVIEW` | 9 | 0.3% | Gated prioritization for Council review |
| `P1_HIGH_VALUE_REVIEW` | 2,166 | 62.8% | Gated prioritization for Council review |
| `P2_STANDARD_REVIEW` | 1,262 | 36.6% | Gated prioritization for Council review |
| `P3_LOW_PRIORITY` | 13 | 0.4% | Gated prioritization for Council review |

---

## 9. Quality Invariants Certification

```text
LEDGER_RECORDS = 3450
UNIQUE_SKILL_IDS = 3450
UNIQUE_PATHS = 3450
MISSING_SKILL_RECORDS = 0
DUPLICATE_LEDGER_RECORDS = 0

RAW_CORPUS_MODIFIED = NO
INSTALLED_SKILLS_MODIFIED = NO
NATIVE_SKILLS_MODIFIED = NO
```

## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
