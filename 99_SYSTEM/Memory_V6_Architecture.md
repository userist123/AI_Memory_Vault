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

Memory V6 augments the canonical vault with an ephemeral sensor buffer, extraction of atomic memory candidates, a reviewable durable proposal queue, and a rebuildable spatial repository index. It does not replace `memory_controller/`, Markdown notes, SQLite/WAL, provenance, lifecycle control, or the audit boundary.

## Trust Boundary

1. `SensorBuffer` is in-memory and ephemeral. It never writes canonical memory.
2. `AtomicMemoryExtractor` produces only `RAW` and `unverified` candidates.
3. `MemoryProposalQueue` is a review queue stored under `06_INBOX/`; queueing is not promotion.
4. Only a controlled `MemoryController.propose()` call may create a canonical note.
5. No candidate may become `ACTIVE` automatically.
6. `SpatialIndex` is derived metadata. It is fully rebuildable and is not an authority source.

## Components

| Component | File | Role |
|---|---|---|
| Sensor Buffer | `cognitive_core/sensor_buffer.py` | Bounded per-session raw events with TTL |
| Atomic Extractor | `cognitive_core/extraction.py` | Extracts facts, decisions, preferences, tasks, lessons, procedures |
| Proposal Queue | `cognitive_core/proposal_queue.py` | Deduplicated JSONL review queue |
| Spatial Index | `cognitive_core/spatial_index.py` | Rebuildable map of paths, imports and Markdown links |
| CLI | `cognitive_core/memory_v6_cli.py` | Operational entry point |

## CLI

```bash
python -m cognitive_core.memory_v6_cli extract --text "Am decis: folosim SQLite WAL." --enqueue
python -m cognitive_core.memory_v6_cli status
python -m cognitive_core.memory_v6_cli index-repo
python -m cognitive_core.memory_v6_cli query-path cognitive_core
```

## Next Stages

1. Integrate queue approval with `MemoryController.propose()` using the existing authorization, provenance validation and audit API.
2. Add conflict detection before queue approval.
3. Add semantic, temporal, causal and entity graph indexes.
4. Add spreading activation retrieval fused with current relevance scoring and ACT-R activation.
5. Add scheduled consolidation and benchmark-driven evaluation.
