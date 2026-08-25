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

Memory V6 augments the canonical vault with an ephemeral sensor buffer, extraction of atomic memory candidates, a reviewable durable proposal queue, a rebuildable spatial repository index, advisory conflict detection, a controlled promotion bridge into `MemoryController.propose()`, a multi-graph memory layer (semantic, temporal, causal, entity), spreading-activation retrieval, read-only sleep-phase consolidation reporting, and a LoCoMo-style retrieval benchmark harness. It does not replace `memory_controller/`, Markdown notes, SQLite/WAL, provenance, lifecycle control, or the audit boundary.

## Trust Boundary

1. `SensorBuffer` is in-memory and ephemeral. It never writes canonical memory.
2. `AtomicMemoryExtractor` produces only `RAW` and `unverified` candidates.
3. `MemoryProposalQueue` is a review queue stored under `06_INBOX/`; queueing is not promotion.
4. Only a candidate explicitly marked `APPROVED` by a human/admin reviewer via the CLI may be promoted.
5. `QueuePromoter.promote_approved()` calls the existing, unmodified `MemoryController.propose()` — authorization, provenance validation, and audit logging all apply exactly as before.
6. No candidate may become `ACTIVE` automatically; `propose()` still creates notes in `RAW` lifecycle, subject to the existing `review()`/`promote()`/`attest()` pipeline.
7. `ConflictDetector` is advisory-only heuristic overlap/negation detection. It never blocks, deletes, or auto-resolves; it only annotates the `review` CLI output and the sleep-consolidation report.
8. `SpatialIndex` is derived metadata. It is fully rebuildable and is not an authority source.
9. `MultiGraphMemory` (semantic/temporal/causal/entity) is derived metadata, rebuilt from canonical notes on demand. It is never itself canonical and holds no provenance authority.
10. `SpreadingActivationEngine` only re-ranks retrieval candidates; it never creates, modifies, or deletes notes, and never changes lifecycle or verification state.
11. `SleepConsolidator` is strictly read-only against canonical storage. It never calls `update()`, `promote()`, `archive()`, or `attest()`; it only writes an advisory JSON report for a human/admin to act on manually through the existing controlled API.
12. `RetrievalBenchmark` never touches canonical memory; it only scores a caller-supplied retrieval function against fixed test cases.

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
| CLI | `cognitive_core/memory_v6_cli.py` | Operational entry point |

## Sleep-Phase Consolidation

Inspired by biologically-grounded consolidation research, `SleepConsolidator` runs a periodic (cron-triggered or manually invoked) advisory pass:

- **Dormant candidates** — `ACTIVE`/`VERIFIED` notes not updated in `dormant_days` (default 60), scored with a simplified ACT-R-style decay heuristic `B ≈ -decay * ln(age_days + 1)`. Independent from, and not calling into, `cognitive_core/activation.py`.
- **Stale REVIEW candidates** — notes stuck in `REVIEW` beyond `stale_review_days` (default 14), flagged for a human `promote()`/reject decision.
- **Conflict pairs** — pairwise `ConflictDetector` scan across all `ACTIVE`/`VERIFIED` notes, surfacing potential contradictions for human reconciliation via `supersede()`.

The report is saved as JSON; no note is ever mutated by this pass.

```bash
python -m cognitive_core.memory_v6_cli consolidate --output 04_MEMORY/sleep_consolidation_report.json
```

## Retrieval Benchmark Harness

`cognitive_core/benchmarks/` provides a small, dependency-free LoCoMo-style harness:

- `metrics.py` — `precision_at_k`, `recall_at_k`, `mean_reciprocal_rank`.
- `retrieval_benchmark.py` — `RetrievalBenchmark` loads `(query, relevant_ids)` cases from JSONL and scores any `retrieval_fn(query) -> List[note_id]` you provide.
- `sample_cases.jsonl` — a small starter fixture; extend it with real vault query/answer pairs as they accumulate.

The CLI ships with a naive substring-match baseline retrieval function for smoke-testing; swap in `MemoryController.search()` or the `SpreadingActivationEngine`-ranked retrieval for a real evaluation.

```bash
python -m cognitive_core.memory_v6_cli benchmark --cases cognitive_core/benchmarks/sample_cases.jsonl --k 5
```

## Review Workflow

```bash
# 1. Extract and queue candidates
python -m cognitive_core.memory_v6_cli extract --text "Am decis: folosim SQLite WAL." --enqueue

# 2. Review pending candidates, with advisory conflict flags
python -m cognitive_core.memory_v6_cli review --show-conflicts

# 3. Human decision per candidate
python -m cognitive_core.memory_v6_cli approve <candidate_id> --reviewer human
python -m cognitive_core.memory_v6_cli reject <candidate_id> --reviewer human

# 4. Promote all APPROVED candidates through the existing controller.propose() path
python -m cognitive_core.memory_v6_cli promote-approved --principal ai_agent

# 5. Periodic maintenance (advisory only)
python -m cognitive_core.memory_v6_cli consolidate
python -m cognitive_core.memory_v6_cli benchmark

# 6. Continue through the existing lifecycle as usual
# review() -> promote() -> attest() using memory_controller.controller.controller directly or vault_cli.py
```

## Next Stages

1. Wire git auto-commit (`memory_controller/git_auto_commit.py`, if present) to fire on `promote_approved()`.
2. Feed `MultiGraphMemory` + `SpreadingActivationEngine` output into `MemoryController.search()` as an optional re-ranking stage, behind a feature flag.
3. Replace the naive substring baseline in the `benchmark` CLI command with a `SpreadingActivationEngine`-backed retrieval function once real vault query/answer pairs are collected.
4. Schedule `consolidate` as a recurring GitHub Action or local cron job, publishing the report to `04_MEMORY/` for human review.
