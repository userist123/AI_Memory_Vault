---
id: memory-v6-architecture
type: architecture
category: memory_system
lifecycle: ACTIVE
verification: unverified
confidence: high
provenance:
  source_type: official
  source_ref: repository:AI_Memory_Vault_CODEX_READY
relations:
  - relation: extends
    target: memory_controller
---

# Memory V6 Architecture

## Purpose

Memory V6 augments the canonical vault with an ephemeral sensor buffer, atomic memory extraction, a reviewable proposal queue, a rebuildable spatial repository index, advisory conflict detection, a controlled promotion bridge into `MemoryController.propose()`, a multi-graph memory layer (semantic, temporal, causal, entity), spreading-activation retrieval, read-only sleep-phase consolidation reporting, a LoCoMo-style retrieval benchmark harness, an optional git promotion hook, a non-invasive graph-based search re-ranking wrapper, and a scheduled consolidation workflow. It does not replace `memory_controller/`, Markdown notes, SQLite/WAL, provenance, lifecycle control, or the audit boundary — every component is additive and reversible.

## Trust Boundary

1. `SensorBuffer` is in-memory and ephemeral. It never writes canonical memory.
2. `AtomicMemoryExtractor` produces only `RAW` and `unverified` candidates.
3. `MemoryProposalQueue` is a review queue stored under `06_INBOX/`; queueing is not promotion.
4. Only a candidate explicitly marked `APPROVED` by a human/admin reviewer via the CLI may be promoted.
5. `QueuePromoter.promote_approved()` calls the existing, unmodified `MemoryController.propose()` — authorization, provenance validation, and audit logging all apply exactly as before.
6. No candidate may become `ACTIVE` automatically; `propose()` still creates notes in `RAW` lifecycle, subject to the existing `review()`/`promote()`/`attest()` pipeline.
7. `ConflictDetector` is advisory-only heuristic overlap/negation detection. It never blocks, deletes, or auto-resolves.
8. `SpatialIndex` is derived metadata, fully rebuildable, and holds no authority.
9. `MultiGraphMemory` (semantic/temporal/causal/entity) is derived metadata, rebuilt from canonical notes on demand. It is never itself canonical.
10. `SpreadingActivationEngine` only re-ranks retrieval candidates; it never creates, modifies, or deletes notes, or changes lifecycle/verification state.
11. `SleepConsolidator` is strictly read-only against canonical storage. It never calls `update()`, `promote()`, `archive()`, or `attest()`.
12. `RetrievalBenchmark` never touches canonical memory; it only scores a caller-supplied retrieval function against fixed test cases.
13. `PromotionGitHook` is disabled by default (`VAULT_GIT_AUTO_COMMIT` unset or `!= "1"`). When enabled, it only stages and commits files that `MemoryController.propose()` already wrote — it never edits note content, never force-pushes, and silently no-ops on any git error rather than raising into the promotion flow.
14. `ranked_search()` in `cognitive_core/ranked_search.py` is a pure wrapper around the existing, unmodified `MemoryController.search()`. It never bypasses authorization or pagination; on any internal error it degrades to the original result order rather than failing the caller.
15. The scheduled `memory-consolidation.yml` GitHub Action only runs the read-only `consolidate` CLI command and uploads the report as a workflow artifact — it never commits the report back to `main` automatically, preserving human review of any resulting action.

## Components

| Component | File | Role |
|---|---|---|
| Sensor Buffer | `cognitive_core/sensor_buffer.py` | Bounded per-session raw events with TTL |
| Atomic Extractor | `cognitive_core/extraction.py` | Extracts facts, decisions, preferences, tasks, lessons, procedures |
| Proposal Queue | `cognitive_core/proposal_queue.py` | Deduplicated JSONL review queue |
| Conflict Detector | `cognitive_core/conflict_detector.py` | Advisory overlap/negation heuristic vs. ACTIVE/VERIFIED notes |
| Queue Promoter | `cognitive_core/queue_promoter.py` | Bridges APPROVED candidates into `MemoryController.propose()` |
| Spatial Index | `cognitive_core/spatial_index.py` | Rebuildable map of paths, imports and Markdown links |
| Multi-Graph Memory | `cognitive_core/multi_graph.py` | Semantic, temporal, causal, and entity graphs derived from notes |
| Spreading Activation | `cognitive_core/spreading_activation.py` | ACT-R-style propagation across the four graphs, fused with base relevance |
| Sleep Consolidator | `cognitive_core/sleep_consolidation.py` | Read-only maintenance report: dormant notes, stale REVIEW notes, conflict pairs |
| Retrieval Benchmark | `cognitive_core/benchmarks/` | LoCoMo-style precision@k / recall@k / MRR harness |
| Promotion Git Hook | `cognitive_core/git_hooks.py` | Opt-in auto-commit of promoted note files via existing `GitIntegration` |
| Ranked Search | `cognitive_core/ranked_search.py` | Non-invasive graph re-ranking wrapper around `MemoryController.search()` |
| Scheduled Consolidation | `.github/workflows/memory-consolidation.yml` | Nightly advisory report as a workflow artifact |
| CLI | `cognitive_core/memory_v6_cli.py` | Operational entry point |

## Optional Git Auto-Commit on Promotion

```bash
export VAULT_GIT_AUTO_COMMIT=1
python -m cognitive_core.memory_v6_cli promote-approved --principal ai_agent
```

When enabled, `PromotionGitHook` stages and commits exactly the note files written by `propose()` during that call, with message `vault(promote): <ids>`. It is a no-op (returns `None`) if disabled, if no paths are resolvable, or on any git error — it never interrupts the promotion itself.

## Graph-Reranked Search (Opt-In)

```python
from cognitive_core.ranked_search import ranked_search

results = ranked_search(controller, principal, "SQLite WAL", top_k=10)
```

Or via the benchmark CLI to compare baselines:

```bash
python -m cognitive_core.memory_v6_cli benchmark --retrieval substring
python -m cognitive_core.memory_v6_cli benchmark --retrieval graph
```

`ranked_search()` never modifies `memory_controller/controller.py`; it calls the existing `search()` verbatim and only re-orders the already-authorized, already-audited result list using `MultiGraphMemory` + `SpreadingActivationEngine`.

## Scheduled Consolidation

`.github/workflows/memory-consolidation.yml` runs nightly (`0 3 * * *`, UTC) and on manual dispatch. It executes only the read-only `consolidate` command and uploads `04_MEMORY/sleep_consolidation_report.json` as a 30-day workflow artifact. It intentionally does **not** commit the report back to `main` — a human/admin reviews the artifact and decides on any follow-up `review()`/`promote()`/`supersede()` action manually.

## Full Review Workflow

```bash
# 1. Extract and queue candidates
python -m cognitive_core.memory_v6_cli extract --text "Am decis: folosim SQLite WAL." --enqueue

# 2. Review pending candidates, with advisory conflict flags
python -m cognitive_core.memory_v6_cli review --show-conflicts

# 3. Human decision per candidate
python -m cognitive_core.memory_v6_cli approve <candidate_id> --reviewer human
python -m cognitive_core.memory_v6_cli reject <candidate_id> --reviewer human

# 4. Promote all APPROVED candidates through the existing controller.propose() path
#    (optionally auto-committing via VAULT_GIT_AUTO_COMMIT=1)
python -m cognitive_core.memory_v6_cli promote-approved --principal ai_agent

# 5. Periodic maintenance (advisory only, also runs nightly via GitHub Actions)
python -m cognitive_core.memory_v6_cli consolidate
python -m cognitive_core.memory_v6_cli benchmark --retrieval graph

# 6. Continue through the existing lifecycle as usual
# review() -> promote() -> attest() using memory_controller.controller.controller directly or vault_cli.py
```

## Status

All five planned Memory V6 packages are complete:

1. Sensor buffer, atomic extraction, proposal queue, spatial index.
2. Conflict detection and controlled queue-to-controller promotion.
3. Multi-graph memory (semantic/temporal/causal/entity) and spreading-activation retrieval.
4. Read-only sleep-phase consolidation and a LoCoMo-style retrieval benchmark harness.
5. Optional git promotion hook, non-invasive graph-reranked search wrapper, and a scheduled advisory consolidation workflow.

## Future Extensions (Not Yet Implemented)

1. Local-LLM-backed extraction (Ollama) as a richer alternative to the deterministic regex extractor in `extraction.py`.
2. Real vault query/answer pairs collected into `cognitive_core/benchmarks/sample_cases.jsonl` to replace the starter fixture.
3. A dashboard or Obsidian-rendered view of `sleep_consolidation_report.json` for faster human triage.
