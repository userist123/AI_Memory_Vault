# Curriculum Transfer Benchmark Report: psychology-memory-ch08-v1

- **Source**: OpenStax Psychology 2e, Chapter 8: Memory
- **Reader Model**: `gemini-3.1-flash-lite`
- **Timestamp**: 2026-09-19T14:32:51.753086+00:00
- **Sample Size**: N = 12 review questions, 10 traps

## 1. Summary Comparison Table

| Metric | Control Arm (No Module Notes) | Treatment Arm (With Module Notes) | Difference / Change |
|---|---|---|---|
| **Supported Answers (n=12)** | 0/12 ([0.000, 0.242]) | 6/12 ([0.254, 0.746]) | +6 net gained |
| **Unsupported / Guessed** | 0/12 | 0/12 | - |
| **Abstained (INSUFFICIENT)** | 12/12 | 6/12 | - |
| **Wrong Answers** | 0/12 | 0/12 | - |
| **Trap Pass Rate (n=10)** | 10/10 | 10/10 | - |

## 2. Paired McNemar Statistical Test

- **Contingency Table**: [Both pass: 0, Improved (Control 0 -> Treatment 1): 6, Regressed: 0, Both fail: 6]
- **Exact Two-Sided p-value**: `0.03125`
- **Significant at $\alpha = 0.05$**: **YES**
- **Conclusion**: Statistically significant difference (exact McNemar two-sided p=0.0312 < 0.05). Treatment demonstrates genuine transfer beyond random noise (improved: 6, regressed: 0).

## 3. Per-Question Paired Comparison

| Question ID | Control Choice | Control Verdict | Treatment Choice | Treatment Verdict | Shift |
|---|---|---|---|---|---|
| `openstax-psy2e-ch08-q01` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-psy2e-ch08-q02` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-psy2e-ch08-q03` | INSUFFICIENT | `ABSTAIN` | encoding, storage, and retrieval | `CORRECT_SUPPORTED` | **improved** |
| `openstax-psy2e-ch08-q04` | INSUFFICIENT | `ABSTAIN` | engram | `CORRECT_SUPPORTED` | **improved** |
| `openstax-psy2e-ch08-q05` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-psy2e-ch08-q06` | INSUFFICIENT | `ABSTAIN` | egocentric bias | `CORRECT_SUPPORTED` | **improved** |
| `openstax-psy2e-ch08-q07` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-psy2e-ch08-q08` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-psy2e-ch08-q09` | INSUFFICIENT | `ABSTAIN` | acrostic | `CORRECT_SUPPORTED` | **improved** |
| `openstax-psy2e-ch08-q10` | INSUFFICIENT | `ABSTAIN` | a traumatic life experience | `CORRECT_SUPPORTED` | **improved** |
| `openstax-psy2e-ch08-q11` | INSUFFICIENT | `ABSTAIN` | INSUFFICIENT | `ABSTAIN` | **both_failed** |
| `openstax-psy2e-ch08-q12` | INSUFFICIENT | `ABSTAIN` | mnemonic devices | `CORRECT_SUPPORTED` | **improved** |