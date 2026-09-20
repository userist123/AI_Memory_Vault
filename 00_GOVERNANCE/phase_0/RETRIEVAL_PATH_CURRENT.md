---
id: 7fd8ae5b-eabd-4b54-a8fb-63e6daae9557
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

# The retrieval path as it is — Phase 0

Read from `03_IMPLEMENTATION/packages/memory/controller.py`, not from the
documentation. `search()` spans lines 427–1011: **585 lines in one function**.

## Order of stages, with line anchors

| # | Stage | Where | State |
|---|---|---|---|
| 1 | query size check | `:453` `check_query_size` | active |
| 2 | sanitisation | `:455` `sanitize_query` | active |
| 3 | authorization | `:287` `authorizer.is_allowed` | active |
| 4 | lexical + entity candidates | fused ranking | active |
| 5 | fusion / ranking arm | `:620`, default `RANKING_ARM_FUSED_SCORE` | active |
| 6 | graph expansion | `:624` onward | **off by default** |
| 7 | spreading activation | inside 6 | forced off when 6 is off |
| 8 | `executive` hook | `:599` | writes `candidate_trace` only |
| 9 | `working_memory` hook | after scoring | replaces the note list |
| 10 | `global_workspace` hook | after 9 | reorders only |
| 11 | lifecycle / type filters | `lifecycles`, `types` arguments | active, caller-supplied |
| 12 | progressive disclosure | `:337` | default level `metadata` |
| 13 | pagination | `:907` `page_results = disclosed[offset:end]` | active |
| 14 | `reasoning` hook | after 13 | writes `candidate_trace` only |
| 15 | audit | `:1006` `audit_event('search', ...)` | active |

## Against the target architecture

Missing outright: intent classification as a routing stage, a dense retriever,
a reranker, a temporal filter, a verification filter, context budgeting, and
abstention. Only the lexical branch, fusion, the optional graph and the policy
filters exist.

Present but inert: graph expansion and spreading activation (off), the four
cognitive hooks (two of which cannot change output).

## What this means for the target flow

The target flow puts the policy filters after scoring and before graph
expansion. Today lifecycle filtering depends on what the caller passes in:
`search()` does not impose a lifecycle floor of its own. An agent that passes
nothing can be handed REVIEW-lifecycle notes — 175 of them exist, plus 148 with
no lifecycle at all. That is a security property, not a ranking detail, and it
belongs in Wave A with the lifecycle work, not in the retrieval wave.

**Checked on this SHA: no production caller passes a lifecycle filter.** Neither
`memory_mcp_server.py`, nor `recall_cli.py`, nor `tool_router.py` mentions
`lifecycles` or `Lifecycle` at all. Every agent-facing entry point therefore
searches the whole corpus, REVIEW notes and the 148 lifecycle-less notes
included. The curriculum evaluator is the one caller that does pass a filter
(`lifecycles=[REVIEW, ACTIVE]`), which is why its arms behave differently from
what an agent sees.
