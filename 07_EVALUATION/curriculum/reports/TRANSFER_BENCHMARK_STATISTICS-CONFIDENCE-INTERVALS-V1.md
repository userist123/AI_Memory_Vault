# Curriculum Transfer Benchmark Report: statistics-confidence-intervals-v1

- **Source**: Introductory Statistics 2e - Chapter 8: Confidence Intervals
- **Reader Model**: `gemini-3.1-flash-lite`
- **Timestamp**: 2026-09-19T14:49:04.807739+00:00
- **Sample Size**: N = 12 review questions, 10 traps

## 1. Summary Comparison Table

| Metric | Control Arm (No Module Notes) | Treatment Arm (With Module Notes) | Difference / Change |
|---|---|---|---|
| **Supported Answers (n=12)** | 0/12 ([0.000, 0.242]) | 7/12 ([0.320, 0.807]) | +7 net gained |
| **Unsupported / Guessed** | 0/12 | 0/12 | - |
| **Abstained (INSUFFICIENT)** | 12/12 | 5/12 | - |
| **Wrong Answers** | 0/12 | 0/12 | - |
| **Trap Pass Rate (n=10)** | 10/10 | 10/10 | - |

## 2. Paired McNemar Statistical Test

- **Contingency Table**: [Both pass: 0, Improved (Control 0 -> Treatment 1): 7, Regressed: 0, Both fail: 5]
- **Exact Two-Sided p-value**: `0.01562`
- **Significant at $\alpha = 0.05$**: **YES**
- **Conclusion**: Statistically significant difference (exact McNemar two-sided p=0.0156 < 0.05). Treatment demonstrates genuine transfer beyond random noise (improved: 7, regressed: 0).

## 3. Per-Question Paired Comparison

| Question ID | Control Choice | Control Verdict | Treatment Choice | Treatment Verdict | Shift |
|---|---|---|---|---|---|
| `openstax-stats2e-ch08-q01` | INSUFFICIENT | `ABSTAIN` | a single number calculated from a sample used to estimate a population parameter | `CORRECT_SUPPORTED` | **improved** |
| `openstax-stats2e-ch08-q02` | INSUFFICIENT | `ABSTAIN` | the proportion of repeated samples whose confidence intervals will contain the true population parameter | `CORRECT_SUPPORTED` | **improved** |
| `openstax-stats2e-ch08-q03` | INSUFFICIENT | `ABSTAIN` | The error bound decreases, making the confidence interval narrower | `CORRECT_SUPPORTED` | **improved** |
| `openstax-stats2e-ch08-q04` | INSUFFICIENT | `ABSTAIN` | standard normal distribution (z-distribution) | `CORRECT_SUPPORTED` | **improved** |
| `openstax-stats2e-ch08-q05` | INSUFFICIENT | `ABSTAIN` | Always round UP to the next higher integer to ensure the sample size is large enough | `CORRECT_SUPPORTED` | **improved** |
| `openstax-stats2e-ch08-q06` | INSUFFICIENT | `ABSTAIN` | Student's t-distribution using sample standard deviation s | `CORRECT_SUPPORTED` | **improved** |
| `openstax-stats2e-ch08-q07` | INSUFFICIENT | `ABSTAIN` | n - 1 | `CORRECT_SUPPORTED` | **improved** |
| `openstax-stats2e-ch08-q08` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-stats2e-ch08-q09` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-stats2e-ch08-q10` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-stats2e-ch08-q11` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-stats2e-ch08-q12` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |