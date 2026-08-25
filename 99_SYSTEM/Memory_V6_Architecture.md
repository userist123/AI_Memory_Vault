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

Memory V6 augments the canonical vault with an ephemeral sensor buffer, atomic memory extraction (deterministic and optionally local-LLM-assisted via Ollama), a reviewable proposal queue, a rebuildable spatial repository index, advisory conflict detection, a controlled promotion bridge into `MemoryController.propose()`, a multi-graph memory layer (semantic, temporal, causal, entity), spreading-activation retrieval, read-only sleep-phase consolidation reporting with an Obsidian-rendered Markdown view, a LoCoMo-style retrieval benchmark harness, an optional git promotion hook, a non-invasive graph-based search re-ranking wrapper, and a scheduled consolidation workflow. It does not replace `memory_controller/`, Markdown notes, SQLite/WAL, provenance, lifecycle control, or the audit boundary — every component is additive and reversible.

## Trust Boundary

1. `SensorBuffer` is in-memory and ephemeral. It never writes canonical memory.
2. `AtomicMemoryExtractor` produces only `RAW` and `unverified` candidates.
3. `MemoryProposalQueue` is a review queue stored under `06_INBOX/`; queueing is not promotion.
4. Only a candidate explicitly marked `APPROVED` by a human/admin reviewer via the CLI may be promoted.
5. `QueuePromoter.promote_approved()` calls the existing, unmodified `MemoryController.propose()`.
6. No candidate may become `ACTIVE` automatically; `propose()` still creates notes in `RAW` lifecycle.
7. `ConflictDetector` is advisory-only heuristic overlap/negation detection.
8. `SpatialIndex` is derived metadata, fully rebuildable, and holds no authority.
9. `MultiGraphMemory` is derived metadata, rebuilt from canonical notes on demand.
10. `SpreadingActivationEngine` only re-ranks retrieval candidates.
11. `SleepConsolidator` is strictly read-only against canonical storage.
12. `RetrievalBenchmark` never touches canonical memory.
13. `PromotionGitHook` is disabled by default (`VAULT_GIT_AUTO_COMMIT` unset or `!= "1"`).
14. `ranked_search()` is a pure wrapper around the existing, unmodified `MemoryController.search()`.
15. `memory-consolidation.yml` only runs the read-only `consolidate` command and uploads an artifact; it never auto-commits to `main`.
16. `OllamaExtractionAdapter` is opt-in only, calls exclusively a caller-configured local endpoint (never a cloud API), and silently returns `[]` on any connection or parsing failure — it can never crash or block the deterministic extraction path in `extraction.py`.
17. `report_view.render_report_file()` is read-only with respect to canonical memory: it reads an already-generated JSON report and writes a new, separate Markdown file. It never edits vault notes, and the rendered file is explicitly marked `verification: unverified` in its own frontmatter.

## Components

| Component | File | Role |
|---|---|---|
| Sensor Buffer | `cognitive_core/sensor_buffer.py` | Bounded per-session raw events with TTL |
| Atomic Extractor | `cognitive_core/extraction.py` | Deterministic extraction of facts, decisions, preferences, tasks, lessons, procedures |
| Ollama Extraction Adapter | `cognitive_core/ollama_extractor.py` | Optional local-LLM augmentation of the extractor, opt-in, no cloud calls |
| Proposal Queue | `cognitive_core/proposal_queue.py` | Deduplicated JSONL review queue |
| Conflict Detector | `cognitive_core/conflict_detector.py` | Advisory overlap/negation heuristic vs. ACTIVE/VERIFIED notes |
| Queue Promoter | `cognitive_core/queue_promoter.py` | Bridges APPROVED candidates into `MemoryController.propose()` |
| Spatial Index | `cognitive_core/spatial_index.py` | Rebuildable map of paths, imports and Markdown links |
| Multi-Graph Memory | `cognitive_core/multi_graph.py` | Semantic, temporal, causal, and entity graphs derived from notes |
| Spreading Activation | `cognitive_core/spreading_activation.py` | ACT-R-style propagation across the four graphs, fused with base relevance |
| Sleep Consolidator | `cognitive_core/sleep_consolidation.py` | Read-only maintenance report: dormant notes, stale REVIEW notes, conflict pairs |
| Report Renderer | `cognitive_core/report_view.py` | Renders the consolidation report as an Obsidian-navigable Markdown note |
| Retrieval Benchmark | `cognitive_core/benchmarks/` | LoCoMo-style precision@k / recall@k / MRR harness |
| Promotion Git Hook | `cognitive_core/git_hooks.py` | Opt-in auto-commit of promoted note files via existing `GitIntegration` |
| Ranked Search | `cognitive_core/ranked_search.py` | Non-invasive graph re-ranking wrapper around `MemoryController.search()` |
| Scheduled Consolidation | `.github/workflows/memory-consolidation.yml` | Nightly advisory report as a workflow artifact |
| CLI | `cognitive_core/memory_v6_cli.py` | Operational entry point |

## Optional Local-LLM Extraction (Ollama)

```bash
python -m cognitive_core.memory_v6_cli extract \
  --text "Am decis: folosim SQLite WAL pentru index local." \
  --use-ollama --ollama-model llama3.1 --ollama-host http://localhost:11434 \
  --enqueue
```

`OllamaExtractionAdapter` sends the text to a local Ollama endpoint only when `--use-ollama` is explicitly passed. It always coexists with, and never replaces, the deterministic regex-based extractor — both extraction paths run and their results are deduplicated by content hash.

## Obsidian Report Rendering

```bash
# Consolidate and render in one step
python -m cognitive_core.memory_v6_cli consolidate --render

# Or render an existing report on demand
python -m cognitive_core.memory_v6_cli render-report \
  --input 04_MEMORY/sleep_consolidation_report.json \
  --output 05_RESOURCES/Obsidian/Sleep_Consolidation_Report.md
```

The rendered note uses `[[wikilinks]]` for every referenced note ID, so Obsidian's graph view surfaces dormant notes, stale REVIEW items, and conflict pairs directly for human triage.

## Full Review Workflow

```bash
# 1. Extract and queue candidates (optionally with Ollama assistance)
python -m cognitive_core.memory_v6_cli extract --text "..." --enqueue

# 2. Review pending candidates, with advisory conflict flags
python -m cognitive_core.memory_v6_cli review --show-conflicts

# 3. Human decision per candidate
python -m cognitive_core.memory_v6_cli approve <candidate_id> --reviewer human
python -m cognitive_core.memory_v6_cli reject <candidate_id> --reviewer human

# 4. Promote all APPROVED candidates (optionally auto-committing via VAULT_GIT_AUTO_COMMIT=1)
python -m cognitive_core.memory_v6_cli promote-approved --principal ai_agent

# 5. Periodic maintenance (advisory only, also runs nightly via GitHub Actions)
python -m cognitive_core.memory_v6_cli consolidate --render
python -m cognitive_core.memory_v6_cli benchmark --retrieval graph

# 6. Continue through the existing lifecycle as usual
# review() -> promote() -> attest() using memory_controller.controller.controller directly or vault_cli.py
```

## Status

All five originally planned Memory V6 packages, plus two of the three follow-on "Future Extensions," are complete:

1. Sensor buffer, atomic extraction, proposal queue, spatial index.
2. Conflict detection and controlled queue-to-controller promotion.
3. Multi-graph memory and spreading-activation retrieval.
4. Read-only sleep-phase consolidation and a LoCoMo-style retrieval benchmark harness.
5. Optional git promotion hook, graph-reranked search wrapper, and a scheduled advisory consolidation workflow.
6. Optional local-LLM (Ollama) extraction adapter and an Obsidian-rendered Markdown view of the consolidation report.

## Remaining Future Extension

1. Real vault query/answer pairs collected into `cognitive_core/benchmarks/sample_cases.jsonl` to replace the starter fixture — this requires organic usage data from the actual vault and cannot be fabricated.
