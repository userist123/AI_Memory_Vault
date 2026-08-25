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

Memory V6 augments the canonical vault with an ephemeral sensor buffer, extraction of atomic memory candidates, a reviewable durable proposal queue, a rebuildable spatial repository index, advisory conflict detection, a controlled promotion bridge into `MemoryController.propose()`, a multi-graph memory layer (semantic, temporal, causal, entity), and spreading-activation retrieval. It does not replace `memory_controller/`, Markdown notes, SQLite/WAL, provenance, lifecycle control, or the audit boundary.

## Trust Boundary

1. `SensorBuffer` is in-memory and ephemeral. It never writes canonical memory.
2. `AtomicMemoryExtractor` produces only `RAW` and `unverified` candidates.
3. `MemoryProposalQueue` is a review queue stored under `06_INBOX/`; queueing is not promotion.
4. Only a candidate explicitly marked `APPROVED` by a human/admin reviewer via the CLI may be promoted.
5. `QueuePromoter.promote_approved()` calls the existing, unmodified `MemoryController.propose()` — authorization, provenance validation, and audit logging all apply exactly as before.
6. No candidate may become `ACTIVE` automatically; `propose()` still creates notes in `RAW` lifecycle, subject to the existing `review()`/`promote()`/`attest()` pipeline.
7. `ConflictDetector` is advisory-only heuristic overlap/negation detection. It never blocks, deletes, or auto-resolves; it only annotates the `review` CLI output.
8. `SpatialIndex` is derived metadata. It is fully rebuildable and is not an authority source.
9. `MultiGraphMemory` (semantic/temporal/causal/entity) is derived metadata, rebuilt from canonical notes on demand. It is never itself canonical and holds no provenance authority.
10. `SpreadingActivationEngine` only re-ranks retrieval candidates; it never creates, modifies, or deletes notes, and never changes lifecycle or verification state.

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
| CLI | `cognitive_core/memory_v6_cli.py` | Operational entry point |

## Multi-Graph Memory

Four orthogonal graphs are rebuilt on demand from the same note corpus, mirroring the MAGMA architecture (multi-graph retrieval over semantic/temporal/causal/entity relations) instead of a single monolithic similarity index:

- **Semantic graph** — edges from shared tags or shared category.
- **Temporal graph** — chronological chain of notes within the same category, ordered by `created`/`updated`.
- **Causal graph** — built strictly from explicit `relations` entries on notes (`replaces`, `replaced_by`, `causes`, `leads_to`, `depends_on`, `blocks`), never inferred.
- **Entity graph** — edges between notes sharing extracted capitalized-token entities (heuristic, advisory).

```python
from cognitive_core.multi_graph import MultiGraphMemory

graph_memory = MultiGraphMemory().build_from_notes(all_notes)
graph_memory.semantic.neighbors("note-id")
```

## Spreading Activation Retrieval

`SpreadingActivationEngine` propagates a seed activation score across all four graphs with exponential hop decay (`decay ** hop`), then fuses per-graph results using configurable graph weights before combining with base relevance scores from the existing `RelevanceScorer`.

```python
from cognitive_core.spreading_activation import SpreadingActivationEngine

engine = SpreadingActivationEngine(graph_memory, decay=0.6, max_hops=2)
ranked = engine.rank(base_scores={"seed-note-id": 1.0}, top_k=10)
```

This module is read-only with respect to canonical memory: it only re-ranks candidate note IDs and never mutates lifecycle, verification, or content.

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

# 5. Continue through the existing lifecycle as usual
# review() -> promote() -> attest() using memory_controller.controller.controller directly or vault_cli.py
```

## Next Stages

1. Add scheduled sleep-phase consolidation as a background/cron job.
2. Add a LoCoMo-style internal benchmark harness to evaluate retrieval quality over time.
3. Wire git auto-commit (`memory_controller/git_auto_commit.py`, if present) to fire on `promote_approved()`.
4. Feed `MultiGraphMemory` + `SpreadingActivationEngine` output into `MemoryController.search()` as an optional re-ranking stage, behind a feature flag.
