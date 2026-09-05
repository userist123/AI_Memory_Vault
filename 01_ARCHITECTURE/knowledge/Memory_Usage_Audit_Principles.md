---
id: "knw-memory-usage-audit-principles-0001"
type: knowledge
lifecycle: REVIEW
category: audit-principles
tags: [memory-audit, anti-fabrication, evidence-hierarchy, provenance-chain, memory-trace]
created: 2026-09-02T00:16:00Z
updated: 2026-09-02T00:16:00Z
provenance:
  source_type: execution
  source_ref: "evaluation/memory_usage_audit/conversation_auditor.py"
confidence: very_high
verification: unverified
relations:
  - "01_ARCHITECTURE/System_Architecture.md"
  - "00_GOVERNANCE/protocols/Memory_Protocol.md"
  - "AGENTS.md"
---

# Memory Usage Audit Principles & Provenance Chain Invariants

This document formalizes the architectural principles and verification standards required to audit AI agent memory utilization.

---

## 1. The Core Primacy Principle

> **A memory system is not demonstrated to be useful merely because an agent has access to it.**
> 
> Useful memory requires an unbroken, empirically observable provenance chain:
> $$\text{Query / Discover} \longrightarrow \text{Retrieve} \longrightarrow \text{Load} \longrightarrow \text{Apply / Decide} \longrightarrow \text{Verify} \longrightarrow \text{Outcome Capture}$$

---

## 2. Anti-Fabrication Axioms

| Axiom | Definition | Invariant Rule |
|---|---|---|
| **Skill Mention $\neq$ Skill Activation** | Mentioning a skill in conversation is not evidence of use. | `SKILL.md` must be read via `view_file` or loaded via Council Router. |
| **Memory Access $\neq$ Memory Usage** | Having files in workspace directory is not usage. | Note must be retrieved and placed into active working memory. |
| **Memory Usage $\neq$ Memory Influence** | Quoting a rule is not proof it changed the outcome. | The code change must causally depend on the retrieved knowledge. |
| **Agent Report $\neq$ Verification Evidence** | Saying "I verified the code" is unverified assertion. | Empirical test logs (`pytest` exit code 0) or browser renders required. |

---

## 3. The 11-Stage Memory Lifecycle Audit

1. **`A. MEMORY_DISCOVERY`**: Probing index or searching vault directories (`00_CORE`..`99_SYSTEM`).
2. **`B. MEMORY_RETRIEVAL`**: Fetching specific canonical note IDs from SQLite/storage.
3. **`C. MEMORY_LOADING`**: Placing retrieved content into prompt context envelope.
4. **`D. SKILL_DISCOVERY`**: Querying capability registry or skill index.
5. **`E. SKILL_ACTIVATION`**: Reading and following specific `SKILL.md` instructions.
6. **`F. SUBAGENT_ROUTING`**: Invoking specialized subagent via `invoke_subagent`.
7. **`G. DECISION_INFLUENCE`**: Demonstrable link between memory constraint and architectural decision.
8. **`H. EXECUTION`**: Executing file writes or terminal commands.
9. **`I. VERIFICATION`**: Running unit tests, visual checks, or lint runners.
10. **`J. OUTCOME_CAPTURE`**: Recording result in append-only telemetry (`outcome_events.jsonl`).
11. **`K. CONSOLIDATION`**: Distilling new lessons learned into `tasks/lessons.md`.

---

## 4. Declared vs Observed Memory Usage

- **Declared Memory Usage**: Verbal claims or summary assertions made by an agent without corroborating tool execution records. Evaluates to `DECLARED_ONLY` (Trust Level = `T0`, Weight = 0.0).
- **Observed Memory Usage**: Machine-logged events backed by tool execution traces, file view operations, or database records (Trust Level = `T1` to `T3`).

---

## 5. Causal Memory Influence

A memory note cannot be claimed to have influenced an agent's decision simply because it was retrieved. Causal memory influence requires:
1. The note content contains a specific constraint, rule, or architectural pattern.
2. The agent explicitly refers to this constraint in its decision event.
3. The subsequent code modification implements the constraint.
4. The test execution verifies adherence to the constraint.

If any link is broken, the status remains `MEMORY_INFLUENCE_UNVERIFIED`.

---

## 6. Trace Integrity & Completeness

The canonical memory lifecycle is evaluated as:
$$\text{Query} \longrightarrow \text{Retrieve} \longrightarrow \text{Load} \longrightarrow \text{Activate} \longrightarrow \text{Decide} \longrightarrow \text{Execute} \longrightarrow \text{Verify} \longrightarrow \text{Outcome}$$

- **`COMPLETE`**: All 8 lifecycle stages are backed by observed events and valid causal links.
- **`PARTIAL`**: Some stages are observed, but the final outcome or verification is pending.
- **`BROKEN`**: A declared action lacks observed evidence (e.g. memory retrieved but not loaded before decision).

