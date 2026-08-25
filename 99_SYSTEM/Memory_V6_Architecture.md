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

Memory V6 augments the canonical vault with an ephemeral sensor buffer, extraction of atomic memory candidates, a reviewable durable proposal queue, a rebuildable spatial repository index, advisory conflict detection, and a controlled promotion bridge into `MemoryController.propose()`. It does not replace `memory_controller/`, Markdown notes, SQLite/WAL, provenance, lifecycle control, or the audit boundary.

## Trust Boundary

1. `SensorBuffer` is in-memory and ephemeral. It never writes canonical memory.
2. `AtomicMemoryExtractor` produces only `RAW` and `unverified` candidates.
3. `MemoryProposalQueue` is a review queue stored under `06_INBOX/`; queueing is not promotion.
4. Only a candidate explicitly marked `APPROVED` by a human/admin reviewer via the CLI may be promoted.
5. `QueuePromoter.promote_approved()` calls the existing, unmodified `MemoryController.propose()` — authorization, provenance validation, and audit logging all apply exactly as before.
6. No candidate may become `ACTIVE` automatically; `propose()` still creates notes in `RAW` lifecycle, subject to the existing `review()`/`promote()`/`attest()` pipeline.
7. `ConflictDetector` is advisory-only heuristic overlap/negation detection. It never blocks, deletes, or auto-resolves; it only annotates the `review` CLI output.
8. `SpatialIndex` is derived metadata. It is fully rebuildable and is not an authority source.

## Components

| Component | File | Role |
|---|---|---|
| Sensor Buffer | `cognitive_core/sensor_buffer.py` | Bounded per-session raw events with TTL |
| Atomic Extractor | `cognitive_core/extraction.py` | Extracts facts, decisions, preferences, tasks, lessons, procedures |
| Proposal Queue | `cognitive_core/proposal_queue.py` | Deduplicated JSONL review queue |
| Conflict Detector | `cognitive_core/conflict_detector.py` | Advisory overlap/negation heuristic vs. ACTIVE/VERIFIED notes |
| Queue Promoter | `cognitive_core/queue_promoter.py` | Bridges APPROVED candidates into `MemoryController.propose()` |
| Spatial Index | `cognitive_core/spatial_index.py` | Rebuildable map of paths, imports and Markdown links |
| CLI | `cognitive_core/memory_v6_cli.py` | Operational entry point |

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

## CLI (full)

```bash
python -m cognitive_core.memory_v6_cli extract --text "..." --enqueue
python -m cognitive_core.memory_v6_cli status
python -m cognitive_core.memory_v6_cli index-repo
python -m cognitive_core.memory_v6_cli query-path cognitive_core
python -m cognitive_core.memory_v6_cli review --show-conflicts
python -m cognitive_core.memory_v6_cli approve <candidate_id>
python -m cognitive_core.memory_v6_cli reject <candidate_id>
python -m cognitive_core.memory_v6_cli promote-approved --principal ai_agent
```

## Next Stages

1. Add semantic, temporal, causal and entity graph indexes (multi-graph memory).
2. Add spreading activation retrieval fused with current relevance scoring and ACT-R activation.
3. Add scheduled sleep-phase consolidation as a background/cron job.
4. Add a LoCoMo-style internal benchmark harness to evaluate retrieval quality over time.
5. Wire git auto-commit (already available in `memory_controller/git_auto_commit.py` if present) to fire on `promote_approved()`.
