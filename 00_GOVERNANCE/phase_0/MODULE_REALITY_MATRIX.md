---
id: 64fd38ce-3a9c-40af-8348-1021ffcb0fca
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

# Module reality matrix — Phase 0

A module is production only when the chain holds: entry point → production
call-site → module → output consumed → observable effect → end-to-end test →
telemetry. Where the chain breaks, the status says where.

Consumers were counted by import scan over `03_IMPLEMENTATION/`, `30_SCRIPTS/`
and `cognitive_core/`, excluding the module's own file, tests and benchmarks.
A plain `grep` for the module name is not evidence: the word appears in 3,661
imported skills.

| Module | Production importers | Output consumed? | Effect on `search()` output | Status |
|---|---:|---|---|---|
| `memory/controller.py::search` | entry point | — | — | PROVEN_PRODUCTION |
| `hybrid_retrieval` | 3 (`brain_pack`, `candidate_generation`, `edge_proposer`) | yes | ranks candidates | INTEGRATED_UNVERIFIED |
| `synapse_store` | 8, incl. `controller.py` | yes, behind a flag | graph expansion, off by default | PARTIAL |
| `working_memory` | 4, incl. `controller.py` | **yes** | replaces the note list | INTEGRATED_UNVERIFIED |
| `global_workspace` | 1 (`controller.py`) | reorders only | no measured change | PARTIAL |
| `reasoning` | 2 (`controller.py`, `executive`) | **no** — writes to `candidate_trace` only | none by construction | TRACE_ONLY |
| `executive` | 1 (`controller.py`) | **no** — writes `executive_intent` only | none by construction | TRACE_ONLY |
| `spreading_activation` | 1 (`ranked_search.py`) | only when graph expansion is on | forced off when expansion is off (`controller.py:624`) | BLOCKED |
| `graph/plasticity` | 4, incl. `synapse_store.py` and `controller.py` | yes, on a purge | removes edges, with a journal | INTEGRATED_UNVERIFIED |
| `attention` | 1 (`working_memory`) | indirect | unmeasured | PARTIAL |
| `brain_pack` | **0** | no | none | DEAD_CODE |
| `consolidator` | **0** by import | no | none | DORMANT |
| `memory_mcp_server` | 0 (it is itself an entry point, launched by `.mcp.json`) | yes | serves agents | PROVEN_PRODUCTION |

`TRACE_ONLY` is not one of the statuses the mandate lists; it is `PARTIAL` with
the reason named. It is kept distinct because it is the failure mode that this
matrix exists to catch.

## The cognitive-module claim, checked

`468fe9992` states that five cognitive modules are "wired in production and
evaluated", each with "a production consumer verified by grep". Recomputing
`07_EVALUATION/cognitive_core/module_v3_results.json` reproduces the report's
table exactly — 4 wins and 14 losses for `working_memory`, 0/0 for the other
four — so **the arithmetic is sound**. What the numbers mean is not.

- `reasoning` and `executive` write a synthesis string and a parsed intent into
  `candidate_trace` and never touch `results`. A 0-win, 0-loss outcome was
  guaranteed before the run. Reporting it as "does not win — stays off, with
  the evidence" presents a tautology as a measurement.
- `global_workspace` reorders notes already returned, which recall at k=5 cannot
  see.
- `spreading_activation` ran with graph expansion on but at the default budget,
  which adds 0 new nodes whenever there are 20 or more lexical seeds — 153 of
  160 queries in the benchmark. The arm reduces to the baseline.
- `working_memory` is the one real test: it changes the note list and it made
  retrieval worse. That verdict stands.

Probed directly on this SHA, one query per flag: `working_memory`,
`global_workspace`, `reasoning` and `executive` do fire (their trace fields
appear); the returned ids are identical to baseline in every case.

**Evidence gap.** `module_v3_results.json` keeps only `id`, `class`, the two
recall booleans, latency and error. It does not record which notes were
returned, so no one can re-derive a recall value from the artefact. The
aggregation is auditable; the measurement is not.

---

## Re-checked after PRs #177 and #178, 2026-09-20

**`graph/plasticity` left DORMANT.** It had zero production importers at Phase
0. The audited purge now runs through it: `synapse_store.py` calls
`prune_specific_edges` and `rollback_prune`, `controller.py` exposes
`prune_synapses` and `rollback_synapses`. Verified by import scan, not by a
grep for the word.

Its status is `INTEGRATED_UNVERIFIED` rather than `PROVEN_PRODUCTION` for one
reason, and the reason matters more than the label: **the rollback restores the
in-memory synapse store, not the repository.** The purge that actually happened
was a rewrite of 29 notes' frontmatter, and `rollback()` does not touch files.
Undoing it means `git revert`. A test that prunes a re-synthesised store and
compares field by field proves the plasticity layer; it does not prove the
change on disk is reversible.

**`reasoning` and `executive` stay TRACE_ONLY**, now pinned by
`20_TESTS/test_annotation_only_modules.py`: with either flag on, the ids
returned are identical to baseline, and the trace says so.

**`brain_pack` is still DEAD_CODE** and `consolidator` still has no importer.
Neither was touched by any wave so far.

