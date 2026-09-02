# Human-Gated Capability Promotion & Retirement Engine

> **Module**: [`memory_controller/promotion_candidates.py`](file:///memory_controller/promotion_candidates.py)  
> **Core Invariant**: $\text{METRIC} \rightarrow \text{CANDIDATE}$, $\text{NEVER } \text{METRIC} \rightarrow \text{AUTOMATIC ACTION}$  
> **Attestation Requirement**: P0-P15 Trust Boundaries (AI proposes candidates; Human attests promotions/retirements).

---

## 1. Why is Promotion Human-Gated?

Autonomous self-promotion creates catastrophic feedback loops if an agent promotes brittle or adversarial skills based on statistical anomalies. Under Vault Rule P0–P15:
* `Principal.AI_AGENT` can only identify and propose review candidates (`status = "REVIEW_REQUIRED"`).
* Only `Principal.HUMAN` or `Principal.ADMIN` can attest and execute promotion, quarantine, or retirement.
* The system never executes automated deletions, file moves, status overwrites, or deletions.

---

## 2. Decision Thresholds & Criteria

| Metric | Promotion Threshold | Retirement Threshold | Role |
| :--- | :--- | :--- | :--- |
| **Statistical Estimator** | Wilson Lower Bound $> 0.85$ | Wilson Lower Bound $< 0.40$ | Sample-size calibrated binomial confidence |
| **Minimum Valid Categories** | $\ge 2$ Categories | $\ge 2$ Categories | Generalizability across operational domains |
| **Minimum Sample Size** | $N \ge 5$ (`VALID`) | $N \ge 5$ (`VALID`) | Prevents overconfidence on $1/1$ or $2/2$ anecdotes |
| **Trend Safety Gate** | No category $\mathbf{DEGRADING}$ | Trend recorded for review | Rejects capabilities exhibiting recent performance collapse |
| **Anti-Gaming Project Cap** | Max project share $\le 40\%$ | N/A | Blocks promotion if 1 project dominates observations |

---

## 3. Wilson Lower Bound vs. Observed Proportion

Naive proportions (e.g. $1/1 = 100\%$ or $4/5 = 80\%$) are heavily distorted by low sample volume. The promotion engine evaluates:
$$\text{Wilson Lower Bound } (95\% \text{ confidence}) > 0.85$$

* At $N=1, s=1$: $\text{Wilson Lower Bound} \approx 0.207 \implies \text{NOT eligible}$ (flagged `INSUFFICIENT_DATA`).
* At $N=20, s=19$: $\text{Wilson Lower Bound} \approx 0.751 \implies \text{NOT eligible}$ (below $0.85$).
* At $N=50, s=48$: $\text{Wilson Lower Bound} \approx 0.868 \implies \mathbf{Qualifies}$.

---

## 4. Multi-Category Generalizability ($\ge 2$ Categories)

A capability that performs exceptionally well in a single narrow category (e.g. `frontend_motion`) cannot be globally promoted. It must demonstrate high performance ($\text{Wilson} > 0.85$) across at least **two distinct valid task categories** (e.g. `frontend_motion` and `frontend_layout`).

---

## 5. Trend Safety Verification

Even if lifetime Wilson score exceeds $0.85$, a capability whose recent chronological window shows a performance decline ($\text{trend} = \mathbf{DEGRADING}$) is immediately **blocked from promotion**.

---

## 6. Anti-Gaming: Project Dominance Cap ($40\%$)

To prevent benchmark-gaming or synthetic project loops where an agent generates hundreds of runs in a single artificial workspace:
$$\text{Project Share} = \frac{\text{Runs in Project } X}{\text{Total Observed Runs in Cell}}$$

If $\text{Project Share} > 40\%$, the cell is flagged `PROJECT_DOMINANCE` and cannot contribute to promotion candidacy.

---

## 7. Recommendation vs. Actual Action

`flag_review_candidates()` produces structured recommendation payloads:
```json
{
  "promotion_candidates": [...],
  "retirement_candidates": [...],
  "blocked_candidates": [...]
}
```
* **No files are modified.**
* **No skills are moved or deleted.**
* **No database entries are updated destructively.**

---

## 8. Correlation vs. Causality Invariant

> [!CAUTION]
> **Empirical Association $\neq$ Causal Proof**:
> A promotion recommendation indicates strong historical co-occurrence with successful outcomes when the capability was loaded into context. It does **not** prove that the capability alone caused the success. Human review must evaluate code quality, relevance, and semantic utility.
