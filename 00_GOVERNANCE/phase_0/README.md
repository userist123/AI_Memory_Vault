---
id: 7eb6b761-da1e-4e93-9f7d-6bccbbd48686
type: procedure
lifecycle: REVIEW
category: governance.phase0
tags: ['phase-0', 'reconciliation', 'measured']
created: "2026-09-20"
updated: "2026-09-20"
provenance:
  source_type: 'execution'
  source_ref: 'phase 0 reconciliation measured on origin/main 10224498c, 2026-09-20'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Phase 0 — reconciliation

Measured on `origin/main` at `10224498c`, 2026-09-20. No implementation was
done in this phase beyond writing these documents.

Read in this order:

1. `BASELINE_RECONCILIATION.md` — where the repository actually is
2. `TEST_BASELINE.md` — **`main` is red, and why CI did not see it**
3. `MODULE_REALITY_MATRIX.md` — what is wired, what only looks wired
4. `CORPUS_HEALTH_BASELINE.md` — notes, lifecycles, duplicates, edges
5. `RETRIEVAL_PATH_CURRENT.md` — the 585-line `search()`, stage by stage
6. `SECURITY_GAP_REGISTER.md` — nine gaps, with evidence
7. `DECISION_REGISTER.md` — what the owner has to decide
8. `DEPENDENCY_DAG.md`, `TASK_LEDGER.md`, `PROPOSED_EXECUTION_PLAN.md`

Every figure comes from a command run on that SHA. Where nothing was measured,
the text says UNVERIFIED.
