# PERPLEXITY ADVERSARIAL VALIDATION REPORT
**Target Artifacts Under Review:**
1. `07_EVALUATION/luna/COGNITIVE_MEMORY_TARGET_MODEL_V2.md`
2. `07_EVALUATION/luna/PLANNING_INFLUENCE_EXPERIMENT_V1.md`  
**Repository:** `userist123/AI_Memory_Vault_CODEX_READY`  
**Target Branch:** `main`  
**Mission Role:** External Evidence, Epistemic Rigor, and Adversarial Falsification Agent

---

## 1. Executive Verdict

### **DECISION: ACCEPT WITH CHANGES (CONDITIONAL)**

#### Core Judgment
The transition formulated in V2—decoupling the **Vault** (passive epistemic substrate) from the **Runtime Interface** (mechanisms that translate stored state into computational bias)—is **theoretically sound, supported by external planning literature, and addresses the primary conceptual flaw of Target Model V1**.

However, the current specification and the proposed initial experiment (`PLANNING_INFLUENCE_EXPERIMENT_V1.md`) suffer from a fatal confounder:
> **The Oracle Confounder:** By setting $P(a_{\text{fatal}} \mid s) = 0.0$ and $P(a_{\text{success}} \mid s) = 0.8$ based on an injected mock memory, the proposed experiment tests an **expert oracle prior**, not an autonomous memory system. It fails to demonstrate that the memory originated from prior experience, that the applicability check was earned, or that the planner would not experience catastrophic failure under distribution shift or noisy retrieval.

To establish genuine causal attribution, the model and experimental protocol must be updated to include an **Arm C (Wrong/Stale Memory)** and an **Arm D (Unconditioned Heuristic Search)**, while bounding the prior adjustment away from hard deterministic clamps ($0.0 / 0.8$) to realistic probability updates ($P_{\text{memory}}$).

---

## 2. What V2 Gets Right

1. **Clear Division of Labor:** Distinguishes the immutable evidentiary ledger (Vault) from runtime actuation (Planner, Gateway, Formatter). This prevents the Vault from bloating into an unmaintainable monolithic operating system.
2. **Explicit Planner Precondition:** Directly resolves the "Passive Delegation Fallacy" by acknowledging that planning influence requires an external inference-time search engine (MCTS/RAP/LATS) capable of ingesting branch priors.
3. **Multi-Channel Influence Taxonomy:** Separating computational modification into **Representation**, **Planning**, **Epistemics**, and **Execution** provides the exact intervention points needed to test non-parametric behavioral control.
4. **Action-Applicability Contract (AAC):** Replacing vague semantic similarity with explicit Pre-Conditions, Relational Topology, Negative Contraindications, and Expected State Deltas creates a machine-checkable boundary for strategy transfer.
5. **Loss-Vector Token Economy:** Requires that document forging be evaluated by explicit constraint and exception retention rather than subjective summary fluency.

---

## 3. Claims That Fail or Need Qualification

| Claim in V2 Artifacts | Formal Classification | Evidence & Why it Fails / Needs Qualification |
| :--- | :--- | :--- |
| *"Memory can alter search priors without modifying weights"* | **SUPPORTED** | Confirmed by LATS (Zhou et al., 2024) and MC-DML (2025). Memory populates prior policies in tree exploration algorithms. |
| *"A prior of P=0.0 on fatal branches proves memory influence"* | **FALSIFIED (as memory proof)** | Setting a prior directly to 0.0 tests a hardcoded oracle mask, not memory learning. It artificially guarantees treatment victory. |
| *"Extracted situation schemas guarantee analogical transfer"* | **PARTIALLY_SUPPORTED** | Structure-Mapping Theory supports relational transfer, but automated LLM role-extraction frequently suffers from the Frame Problem. |
| *"Reorganization without fine-tuning alters future behavior"* | **SUPPORTED** | Graph edge re-weighting and boundary splitting alter future retrieval context and search value priors. |
| *"Memory-derived constraints can run deterministically in the LLM"* | **FALSIFIED (in LLM head)** | LLMs cannot enforce deterministic guarantees internally; enforcement must occur at the external driver/proxy layer. |
| *"Outcome alone justifies memory updates"* | **UNSUPPORTED** | Correlating an outcome with an action commits the post-hoc fallacy unless background variables and counterfactuals are controlled. |

---

## 4. Four-Channel Evidence Matrix

1. **Representation Channel:** Supported in literature (e.g., RECOMP, ThoughtSculpt). In-context structural schemas shift attention to relational invariants, but remain vulnerable to base model pre-training biases.
2. **Planning Channel:** Strongly supported in MCTS-integrated agent literature (LATS, RAP). Memory modifies tree exploration, not autoregressive token probability directly.
3. **Epistemic Channel:** Partially supported. Active Inference and EVOI offer formal mechanics, but empirical LLM implementations struggle to calibrate epistemic versus aleatoric uncertainty without external probes.
4. **Execution Channel:** Strongly supported at the tool-driver boundary (e.g., SGLang, driver wrappers). Action-masking provides 100% determinism against repeat fatal tool invocations.

---

## 5. Causal Attribution Audit

`PLANNING_INFLUENCE_EXPERIMENT_V1.md` attempts to prove:
$$\text{Memory} \longrightarrow \text{Priors} \longrightarrow \text{Search Path} \longrightarrow \text{Better Outcome}$$

### Confounder Analysis
1. **Branch Order Bias:** LLMs exhibit strong positional bias toward the first or last branch emitted.
   - *Mitigation Required:* Permute branch keys (`a1`, `a2`, `a3`, `a4`) randomly across runs.
2. **Stochastic Sampling Variance:** With temperature $> 0$, treatment victory could be a sampling anomaly.
   - *Mitigation Required:* Set generation temperature to $0.0$ for action proposals, or run $N \ge 5$ random seeds per task.
3. **Unequal Compute Budget:** If Treatment solves tasks in fewer rollouts, does it use fewer overall LLM generation tokens?
   - *Mitigation Required:* Enforce an identical upper bound on total model token expenditure across both arms.
4. **Informational Leakage in Prompt:** If Treatment’s parsed prior injection leaks more semantic explanation than Control’s advisory text, the difference is prompt quality, not planning priors.
   - *Mitigation Required:* Normalize semantic content so that Control and Treatment receive identical tokenized text; Treatment differs *only* in the programmatic initialization of the search array.

---

## 6. Oracle vs. Memory Audit

In `PLANNING_INFLUENCE_EXPERIMENT_V1.md`, the treatment arm directly sets:
- $P(a_2 \mid s) = 0.0$ (known fatal branch)
- $P(a_3 \mid s) = 0.8$ (known viable branch)

### The Oracle Flaw
This setup does not evaluate *memory*; it evaluates **how an MCTS planner behaves when an oracle reveals the answer**.

### Required Correction
To qualify as memory influence, the priors must be **derived computationally from an antecedent ingestion phase**:
1. The agent must first run **Task 0 (Acquisition Trial)** where it encounters the fatal branch and records an episode.
2. An **Extraction/Consolidation step** must parse that episode into a transition schema and calculate an updated empirical value $Q(s, a)$.
3. The derived prior in **Task 1 (Transfer Trial)** must reflect historical uncertainty:
   $$P_{\text{memory}}(a) = \frac{\exp(Q(s, a) / \tau)}{\sum_j \exp(Q(s, a_j) / \tau)}$$
   rather than an arbitrary, hand-coded clamp ($0.0 / 0.8$).

---

## 7. Applicability Audit

The **Action-Applicability Contract (AAC)** is conceptually defensible, but fragile under real-world software shifts.

### Vulnerabilities
- **Environment Predicate Brittleness:** If an applicability contract requires `Ubuntu 22.04`, running inside a Debian 12 container with identical libraries will evaluate to `NOT_APPLICABLE`, causing false-negative retrieval starvation.
- **Topological Hallucination:** LLM extractors frequently hallucinate analogies (e.g., mapping a thread deadlock to a database connection pool when the database issue was actually an authentication timeout).

### Mandatory States
The contract must support four explicit states without collapsing into a single scalar confidence score:
1. `APPLICABLE`: All environment predicates and boundary invariants verified.
2. `APPLICABLE_WITH_VERIFICATION`: Topological match holds; environment predicates unverified $\implies$ triggers active probe.
3. `CONTRAINDICATED`: Active state matches a known fatal failure boundary $\implies$ triggers negative action mask.
4. `INSUFFICIENT_EVIDENCE`: Prior cases lack statistical or causal support $\implies$ default to baseline planning.

---

## 8. Reorganization Audit

Reorganization is the only mechanism that allows memory to learn without retraining model weights.

### Causal Reorganization Rules
Before an observed outcome may update a canonical memory node, the following gates must be satisfied:
1. **Temporal Precedence Verified:** The action preceded the observed state change.
2. **Deterministic Delta Observed:** The target metric changed beyond the noise floor ($\Delta S > \sigma_{\text{noise}}$).
3. **No Hidden Confounder Flagged:** The execution environment did not emit unmodeled asynchronous events (e.g., external container restarts).
4. **Bifurcation Over Overwrite:** If a strategy succeeds in Context A and fails in Context B, the memory node must **split into conditional branches**, preserving both histories.

---

## 9. Token-Economy Audit

Target Model V2 correctly mandates that token reduction must not degrade reasoning quality.

### The Threat of Schema Syntax Inflation
Injecting raw JSON schemas with fields like `"provenance_hash"`, `"bitemporal_valid_until"`, and `"epistemic_status"` burns context window space on non-executable metadata.

### The IR Solution
Memory must compile into a **High-Density Compact IR** (Target: $< 100$ tokens per injected constraint):
```text
[CONSTRAINT-INVARIANT]
ID: CIB-042 | ENV: Linux/x86_64
BLOCK: rm -rf /var/run/* (PID_ACTIVE)
EXPECT: SystemdSocketClosed
CITE: sha256:7f9a...
```
All descriptive background, verification ledgers, and author notes remain external in the Vault, referenced purely by cryptographic hash.

---

## 10. Planning Experiment Audit

### Critical Assessment of `PLANNING_INFLUENCE_EXPERIMENT_V1.md`

| Parameter | Proposed in V1 | Audit Evaluation & Required Fix |
| :--- | :--- | :--- |
| **Task Count** | 50 tasks | **Adequate** for statistical significance under paired $t$-test ($p < 0.01$). |
| **Search Depth** | Depth = 3 | **Adequate** for synthetic refactoring; sufficient to test multi-step exploration. |
| **Rollouts** | 8 rollouts | **Too low.** With 4 branches at depth 3, 8 rollouts barely sample the tree. **Increase to 16 rollouts.** |
| **Prior Weights** | $P=0.0 / P=0.8$ | **FATAL FLAW (Oracle Bias).** Replace with softmax-derived priors from empirical $Q$-values. |
| **Arms** | 2 arms (Control vs. Treatment) | **INSUFFICIENT.** Must add **Arm C (Stale/Adversarial Memory)** to detect over-anchoring. |

---

## 11. Red-Team Scenarios

1. **Stale Memory Poisoning:** A 2024 deployment procedure runs with `--privileged`. In 2026, policy blocks this flag.
   - *Defense:* Bitemporal gate ($T_{\text{valid\_until}} < T_{\text{current}} \implies \text{Admissibility: DENIED}$).
2. **Over-Generalized Action Masking:** Agent bans `kill` after crashing a host, paralyzing itself in a disposable container.
   - *Defense:* Scope predicates bind constraints to environment tiers (`Tier: Host` vs `Tier: EphemeralSandbox`).
3. **Accidental Causal Attribution:** Service recovers due to external watchdog while agent runs an irrelevant script.
   - *Defense:* Require counterfactual probe before logging a transition as causal.
4. **Contradictory Memories:** Two verified memories recommend conflicting strategies with equal authority.
   - *Defense:* Assumption-Based Truth Maintenance (ATMS) labels each strategy with its required premise environment.
5. **Prompt Compression Operator Deletion:** Extractive compressor removes `NOT` from `"Do NOT restart daemon"`.
   - *Defense:* Loss-vector audit bans dropping boolean negation operators and relational qualifiers.

---

## 12. Required V2 Changes

Before implementation, CODEX must incorporate the following modifications into `COGNITIVE_MEMORY_TARGET_MODEL_V2.md`:

1. **Decouple Storage from Actuation:**
   - Formalize that `AI_Memory_Vault` is an **Epistemic Substrate (Storage/Index)**.
   - Introduce the **Runtime Memory Proxy (RMP)** as the operational harness that interfaces with the planner and tool execution gateway.
2. **Eliminate Arbitrary Hardcoded Priors:**
   - Forbid injecting hard $0.0 / 1.0$ priors directly from memory. Priors must be computed via softmax over historical empirical values ($Q$-scores).
3. **Mandate an Adversarial Control Arm:**
   - Require all memory evaluations to include an arm exposed to obsolete or contradictory memory to measure resilience against confirmation bias.
4. **Formalize the Compact IR Context Pack:**
   - Restrict prompt context packs to high-density, stripped invariant cards ($< 100$ tokens per item). Full schemas remain in the Vault.

---

## 13. Minimum Viable Experiment (MVE)

The revised, causally defensible experiment specification:

```text
========================================================================================
MINIMUM VIABLE PLANNING INFLUENCE EXPERIMENT (REVISED)
========================================================================================
Tasks: 30 paired synthetic debugging scenarios (4 branches, 1 optimal, 2 fatal, 1 sub-optimal).
Model: Local quantized model (qwen2.5-coder:7b or llama-3.1-8b).
Search Engine: Python MCTS (Rollouts = 16, Depth = 3, Exploration Constant = 1.414).

ARM 1 (Baseline / Uninformed Planner):
  - Uniform search priors: P(a | s) = 0.25 for all branches.
  - Zero context memory.

ARM 2 (Passive Advisory RAG):
  - Memory text injected into system prompt: "Strategy 2 deadlocks; Strategy 3 succeeds."
  - Uniform search priors: P(a | s) = 0.25.

ARM 3 (Cognitive Planning Influence - Treatment):
  - Memory-derived priors from Task 0 experience: Softmax-scaled P(a3)=0.65, P(a2)=0.05, etc.
  - Context contains minimal IR card.

ARM 4 (Adversarial / Stale Memory Control):
  - Memory derived from an altered environment where Strategy 2 was optimal.
  - Tests whether the planner can override bad memory via environmental feedback.

PRIMARY METRIC:
  Causal Planning Efficiency = (Nodes Explored to Solution in Arm 3) / (Nodes in Arm 2).
  Target: >= 40% reduction in rollout cost with zero repeat visits to known fatal branches.
========================================================================================
```

---

## 14. Evidence Table

| Component / Mechanism | Evidence Source | Evidence Classification | Key Finding |
| :--- | :--- | :--- | :--- |
| **MCTS Tree Search Guidance** | LATS (Zhou et al., 2024), RAP (Hao et al., 2023) | `STRONG_EXTERNAL_EVIDENCE` | Memory priors alter rollout efficiency and task success over ReAct. |
| **PUCT Memory Priors** | MC-DML (ICLR 2025) | `STRONG_EXTERNAL_EVIDENCE` | Populating prior arrays biases exploration toward verified actions. |
| **Action Masking Enforcement** | SGLang, Outlines documentation | `STRONG_EXTERNAL_EVIDENCE` | Driver-level constraints block contraindicated actions with 100% determinism. |
| **Vault Baseline Retrieval Gap** | `Retrieval_Bottleneck_P0_Empirical_Findings.md` | `DOCUMENT_VERIFIED` | Existing single-signal retrieval yields only 6.7% factual evidence coverage. |
| **Spreading Activation Bug** | `cognitive_core/spreading_activation.py` | `CODE_VERIFIED` | Edge weight calculation is overwritten by hop decay; graph is unweighted. |
| **Extractive Prompt Distillation** | RECOMP (Xu et al., 2024) | `STRONG_EXTERNAL_EVIDENCE` | Compresses context to 6–20% without degrading downstream task accuracy. |
| **ATMS Assumption Labeling** | de Kleer (1986), Shapiro (1993) | `STRONG_EXTERNAL_EVIDENCE` | Maintains conflicting operational rules safely via assumption sets. |

---

## 15. Final Go / No-Go

### **DECISION: GO (WITH MANDATORY MVE CHANGES)**

Target Model V2 is conceptually sound, provided it is treated as an external epistemic substrate coupled to runtime planning and driver gateways, rather than an internal neural modification. 

**Exact Handoff to CODEX:**
1. Do not touch the core Vault storage schema until the Planning Influence MVE is executed.
2. Implement the revised 4-Arm MVE under `07_EVALUATION/luna/experiments/planning_mve/`.
3. Use a Python-based MCTS runner (`rollouts=16`, `depth=3`) and test against local `qwen2.5-coder:7b`.
4. Fix the dead-code weight overwrite bug in `cognitive_core/spreading_activation.py` before running graph-conditioned prior tests.
