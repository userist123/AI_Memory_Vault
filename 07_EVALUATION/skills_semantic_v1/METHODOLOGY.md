# Skill Semantic Evaluation V1 Methodology

## 1. Overview & Principles
The **Skill Semantic Evaluation V1** establishes a reproducible, deterministic semantic assessment baseline across a stratified sample of the installed 3,450 skills.

### Important Distinction
- **DECLARED / STATIC EVIDENCE**: Measured and indexed in Phase V1 Static Baseline.
- **SEMANTIC VALUE**: Measured in this phase across 9 core dimensions (Value, Actionability, Specificity, Completeness, Reusability, Coherence, Distinctiveness, Verification, Risk Awareness).
- **USED / EFFECTIVE / CAUSAL EFFECT**: Explicitly NOT claimed in this phase. Runtime execution traces and empirical benchmark outcomes will be measured in subsequent phases.

---

## 2. Cohort Sampling Rules & Fixed Seed
- **Random Seed**: `20260903` (Python `random.Random(20260903)`)
- **Timestamp**: `2026-09-03T00:45:00Z`
- **Cohorts Defined**:
  1. `p0_security`: ALL 9 skills with security risk `HIGH` or `CRITICAL`.
  2. `high_overlap`: 100 skills randomly sampled from `HIGH_OVERLAP` and `SEMANTIC_OVERLAP`.
  3. `low_value`: 50 skills randomly sampled from `P3_LOW_PRIORITY` or `documentation_score < 50`.
  4. `high_value`: 100 skills randomly sampled from `P1_HIGH_VALUE_REVIEW` with `static_utility_score >= 80`.
  5. `tool_dependent`: 100 skills randomly sampled from `execution_readiness == 'TOOL_DEPENDENT'`.
  6. `reference_only`: 50 skills randomly sampled from `execution_readiness == 'REFERENCE_ONLY'`.
  7. `environment_dependent`: 25 skills randomly sampled from `execution_readiness == 'ENVIRONMENT_DEPENDENT'`.
  8. `random_control`: 100 skills uniformly sampled across all 3,450 skills.

Total unique skills evaluated in the benchmark cohort: **502**.

---

## 3. Semantic Evaluation Rubric & Weights
Each skill is evaluated on 9 dimensions from 0 to 10:
- **Value (20%)**: Core problem-solving capacity, domain relevance, and practical utility.
- **Actionability (15%)**: Clear imperative steps, procedural clarity, and ease of execution.
- **Specificity (10%)**: Parameter definiteness, commands, tool declarations, inputs, and outputs.
- **Completeness (10%)**: End-to-end task lifecycle coverage, prerequisites, and edge cases.
- **Reusability (10%)**: Project-agnostic nature without brittle hardcoded host paths.
- **Coherence (10%)**: Structural clarity and logical transition from premise to verification.
- **Distinctiveness (10%)**: Novelty and specific capability vs generic prompt boilerplate.
- **Verification (10%)**: Acceptance criteria, automated test commands, or checklist support.
- **Risk Awareness (5%)**: Explicit warnings, error handling, and safe operational boundaries.

$$\text{SEMANTIC\_SCORE} = \sum (\text{Score}_i \times \text{Weight}_i) \in [0, 100]$$

---

## 4. Semantic Classes Definitions
- **`CORE`**: Fundamental, highly actionable, reusable workflows with score $\ge 82$.
- **`SPECIALIZED`**: High-value domain-specific capabilities tied to tools/frameworks.
- **`COMPLEMENTARY`**: Modular skills providing supporting utility to primary skills.
- **`REDUNDANT`**: Duplicative capability where a superior canonical skill already exists.
- **`GENERIC`**: Vague prompt advice lacking actionable tooling or step-by-step procedures.
- **`REFERENCE`**: Architectural guides, cheat sheets, or normative standards.
- **`EXPERIMENTAL`**: Prototype workflows requiring compiler or specialized runtime validation.
- **`LOW_VALUE`**: Shallow, broken, or trivial skills with score $< 40$.
- **`UNSAFE_TO_USE`**: Skills containing dangerous remote execution patterns (`curl | bash`).
- **`UNCLEAR`**: Skills with contradictory or insufficient instructions.

---

## 5. Confidence Ratings
- **`HIGH`**: Comprehensive documentation ($> 1,500$ bytes) with valid bundle structure.
- **`MEDIUM`**: Adequate documentation ($500 - 1,500$ bytes).
- **`LOW`**: Brief or ambiguous text ($< 500$ bytes).

---

## 6. Gold Standard Dual Evaluation & Disagreement Threshold
- Dual independent evaluation executed on 30 sample skills across cohorts.
- **Disagreement Threshold**: $|\text{Score}_A - \text{Score}_B| \ge 10$ or $\text{Class}_A \ne \text{Class}_B$.
- Any skill meeting the threshold is flagged with `review_required: true`.
