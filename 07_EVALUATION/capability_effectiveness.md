# Empirical Capability Effectiveness Architecture

> **Module**: [`memory_controller/capability_effectiveness.py`](file:///memory_controller/capability_effectiveness.py)  
> **Rule**: `DECLARED != OBSERVED` (Physical runtime presence is the sole evidence base)  
> **Confidence Model**: Wilson Score Interval + Laplace Smoothing + Sample Size Guard  

---

## 1. What is a Capability?

In the AI Memory Vault, a **Capability** is a discrete cognitive or operational asset utilized during agent execution:
* **`skills`**: Operational execution packages (e.g. `SKILL-API-DESIGN`, `frontend-animation`).
* **`agents`**: Specialized persona roles participating in council deliberation (e.g. `AGENT-ROUTER`, `security-auditor`).
* **`knowledge_refs`**: Canonical memory notes and architecture references loaded into context (e.g. `00_CORE/Storage_Architecture.md`).
* **`procedure_refs`**: Verifiable operational SOPs and runbooks (e.g. `03_PROCEDURES/Import_Sanitization.md`).

---

## 2. What Does OBSERVED Mean?

An asset is **OBSERVED** if and only if it was deterministically present in the physical execution environment:
1. Emitted in `ObservedMemoryTrace.retrieved_memory_ids` (actually packed into the model's final context window).
2. Recorded in `OutcomeRecord.observed_capabilities` by verified runtime harnesses.

> [!IMPORTANT]
> **Anti-Fabrication Principle**: Unverified claims made in agent prose, prompt text, or declared intent are never treated as observations.

---

## 3. Join Logic & Relational Attribution

Each council execution is identified by a unique `run_id`:
```text
OutcomeRecord (run_id, outcome, task_category, project_id)
      ⋈ (join on run_id)
ObservedMemoryTrace (run_id, retrieved_memory_ids, retrieval_scores)
```

### Anti-Duplication Invariant
A single `run_id` contributes **at most one observation** to any `(capability_type, capability_id, task_category)` cell, regardless of how many individual traces or memory references exist for that run.

---

## 4. Outcome Mapping & Rate Calculation

Outcomes are mapped into four distinct counters:
* `success_runs`: Confirmed success with verified evidence (`test_pass`, `exit_code`, `human_confirmed`).
* `fail_runs`: Confirmed execution failure.
* `partial_runs`: Incomplete execution.
* `unknown_runs`: Unverified runs (default).

$$\text{total\_runs} = \text{success\_runs} + \text{fail\_runs} + \text{partial\_runs} + \text{unknown\_runs}$$
$$\text{observed\_rate} = \frac{\text{success\_runs}}{\text{total\_runs}} \quad (\text{for } \text{total\_runs} > 0)$$

---

## 5. Statistical Rigor: Wilson Bound & Sample Size Guard

Naive success rates (e.g. $1/1 = 100\%$) are notoriously deceptive. The capability matrix enforces:
1. **Minimum Sample Size Guard** (`MIN_SAMPLE_SIZE = 5`):
   - Any cell with $\text{total\_runs} < 5$ is strictly flagged `status = "INSUFFICIENT_DATA"`.
2. **Wilson Score Interval Lower Bound**:
   $$\text{center} = p + \frac{z^2}{2n}, \quad \text{spread} = z \sqrt{\frac{p(1-p)}{n} + \frac{z^2}{4n^2}}, \quad \text{lower} = \frac{\text{center} - \text{spread}}{1 + \frac{z^2}{n}}$$
   Provides a conservative lower bound on performance at $95\%$ confidence.
3. **Laplace Smoothing**:
   $$\text{smoothed\_rate} = \frac{\text{successes} + 1}{\text{trials} + 2}$$

---

## 6. Trend Analysis

`effectiveness_trend` segments chronological runs for a capability into a `previous_window` and a `recent_window` (each of size $\ge 5$):
* $\Delta = \text{recent\_rate} - \text{previous\_rate}$
* $\Delta \ge +0.05 \implies \mathbf{IMPROVING}$
* $\Delta \le -0.05 \implies \mathbf{DEGRADING}$
* $|\Delta| < 0.05 \implies \mathbf{STABLE}$
* If $\text{total\_runs} < 2 \times \text{window\_size} \implies \mathbf{INSUFFICIENT\_DATA}$

---

## 7. Crucial Limitation: Correlation vs. Causality

> [!CAUTION]
> **What This Matrix CANNOT Prove**:
> The capability effectiveness matrix proves **empirical co-occurrence**, NOT direct causality.
>
> - **Empirically Proven**: *"When `skill-A` was loaded in `frontend_motion` tasks, the run concluded with `success` in 10 out of 12 instances."*
> - **NOT Proven**: *"Loading `skill-A` caused the run to succeed."* (The agent may have succeeded due to another factor, base model capability, or prompt structure).


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
