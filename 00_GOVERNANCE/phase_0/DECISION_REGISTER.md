---
id: 7435da46-a22e-4a36-b80f-60dcbd18a5df
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

# Decision register — Phase 0

Only decisions that block work and that are genuinely the owner's. Everything
reversible was decided and is recorded in `PROPOSED_EXECUTION_PLAN.md`.

| # | Decision | Options | Recommendation |
|---|---|---|---|
| **D1** | Sync the local repository | fast-forward / rebase / fresh clone | **Fast-forward.** Tree is clean, nothing local to lose. Decided and applied; listed for the record. |
| **D2** | The 148 notes with no lifecycle | (a) treat a missing lifecycle as REVIEW and filter them out of agent results; (b) classify them one by one; (c) leave as is | **(a) now, (b) over time.** Today they are returned to agents by default. |
| **D3** | Lifecycle floor at the entry points | (a) `search()` refuses to return REVIEW to `AI_AGENT` unless asked; (b) each caller passes its own filter; (c) leave open | **(a).** A default that has to be remembered at every call site is not a control. |
| **D4** | The 7 pending ontology rows | dispose / keep pending | Owner's, untouched. Blocks the ontology work only. |
| **D5** | Graph expansion default | keep off / turn on at budget 5 | **Keep off.** The preregistered rule rejected budget 5: 1 win, 14 losses, p = 0.00098. |
| **D6** | Strong relations in the live graph | purge the rejected ones / keep and re-audit | **Purge through `plasticity.py`, with journal and rollback.** 3/25 precision on strong edges, 0/5 on `supersedes`. |
| **D7** | Repository visibility | public / private | Owner's. It is public, which is why the quoting budget in #174 exists. |
| **D8** | Book licences | verify against publishers / mark every book-derived note as licence-unverified | **Mark them.** Verification is legal work, not a script. |
| **D9** | Stale PRs #135 and #90 | close / rebase | Owner's. Both predate several rewrites of the files they touch. |

## Not decisions — defects, being fixed without asking

- `main` is red: `controller.py` imports `cognitive_core.working_memory`, which
  the root shim does not export, so `recall_cli` dies without `PYTHONPATH`.
- `reasoning` and `executive` are reported as evaluated when they cannot affect
  the metric they were evaluated on.
- `module_v3_results.json` keeps no note ids, so its recall values cannot be
  re-derived.
- `verified` and `verified_source` are two spellings of one state.
