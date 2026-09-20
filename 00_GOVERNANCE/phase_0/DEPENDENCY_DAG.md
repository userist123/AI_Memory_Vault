---
id: 0056507e-53b9-4d12-9610-a5be259403f5
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
relations: []
---

# Dependency DAG — Phase 0

```
BASE-001 fast-forward + red main fixed
   │
   ├── LIFE-001 single transition policy ──┬── SEC-001 lifecycle floor at entry points
   │                                       └── LIFE-002 enum / schema / migration
   │
   ├── RET-001 retrieval path mapped (done, this phase)
   │      └── RET-002 hooks that cannot affect output are removed or made real
   │             └── OBS-001 RetrievalTrace contract, versioned
   │                    └── EVAL-001 development benchmark
   │                           └── EVAL-002 held-out, owned by LUNA
   │
   ├── CORP-001 exact duplicates (5 groups, 13 notes)
   │      └── CORP-002 near duplicates, threshold declared first
   │
   └── GRAPH-001 edge inventory (done, this phase)
          └── GRAPH-002 proposer fixed: strong relations need explicit citation
                 └── GRAPH-003 independent re-audit, then purge via plasticity
```

Nothing downstream of `EVAL-002` starts before the held-out set exists and is
held by someone who is not building the mechanism.

`SEC-002` (prompt-injection suite) depends on `SEC-001`: there is no point
testing whether a memory can issue instructions while every entry point still
returns REVIEW notes by default.
