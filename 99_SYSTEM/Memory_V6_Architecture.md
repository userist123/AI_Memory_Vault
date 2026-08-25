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

Memory V6 augments the canonical vault with an ephemeral sensor buffer, atomic memory extraction (deterministic and optionally local-LLM-assisted via Ollama), a reviewable proposal queue, a rebuildable spatial repository index, advisory conflict detection, a controlled promotion bridge into `MemoryController.propose()`, a multi-graph memory layer, spreading-activation retrieval, read-only sleep-phase consolidation reporting with an Obsidian-rendered Markdown view, a LoCoMo-style retrieval benchmark harness, an optional git promotion hook, a non-invasive graph-based search re-ranking wrapper, a scheduled consolidation workflow, optional semantic retrieval via Ollama embeddings + Qdrant, an OWASP-adjacent static security auditor, a skill router over the 200+ operational skills catalog, and a trading-decision logging helper. It does not replace `memory_controller/`, Markdown notes, SQLite/WAL, provenance, lifecycle control, or the audit boundary — every component is additive and reversible.

## Trust Boundary

1. `SensorBuffer` is in-memory and ephemeral. It never writes canonical memory.
2. `AtomicMemoryExtractor` produces only `RAW` and `unverified` candidates.
3. `MemoryProposalQueue` is a review queue stored under `06_INBOX/`; queueing is not promotion.
4. Only a candidate explicitly marked `APPROVED` by a human/admin reviewer via the CLI may be promoted.
5. `QueuePromoter.promote_approved()` calls the existing, unmodified `MemoryController.propose()`.
6. No candidate may become `ACTIVE` automatically.
7. `ConflictDetector` is advisory-only heuristic overlap/negation detection.
8. `SpatialIndex` is derived metadata, fully rebuildable, and holds no authority.
9. `MultiGraphMemory` is derived metadata, rebuilt from canonical notes on demand.
10. `SpreadingActivationEngine` only re-ranks retrieval candidates.
11. `SleepConsolidator` is strictly read-only against canonical storage.
12. `RetrievalBenchmark` never touches canonical memory.
13. `PromotionGitHook` is disabled by default (`VAULT_GIT_AUTO_COMMIT` unset or `!= "1"`).
14. `ranked_search()` is a pure wrapper around the existing, unmodified `MemoryController.search()`.
15. `memory-consolidation.yml` only runs the read-only `consolidate` command and uploads an artifact.
16. `OllamaExtractionAdapter` is opt-in only, calls exclusively a caller-configured local endpoint, and silently returns `[]` on any failure.
17. `report_view.render_report_file()` is read-only with respect to canonical memory.
18. `SemanticRetrieval` (Ollama embeddings + Qdrant) is fully optional: if either service is unreachable, `reindex()` returns `0` and `query()` returns `[]` rather than raising. It never mutates canonical notes; Qdrant holds only a derived vector index keyed by `note_id`, never the note content itself.
19. `SecurityAuditor` is strictly read-only against the scanned filesystem. It never modifies scanned files, never executes any matched code, and `to_candidates()` only returns plain dicts for the caller to optionally run through the existing `extract`/`review`/`approve`/`promote-approved` flow — findings are never proposed automatically.
20. `SkillRouter` only reads directory names under `.agents/skills/`; it never executes a skill and never modifies the skills catalog.
21. `TradingDecisionLogger` only produces `MemoryCandidate` objects with `lifecycle=RAW`; it never calls `propose()` directly and never marks a decision as verified or executed.

## New Components (Package 8)

| Component | File | Role |
|---|---|---|
| Semantic Retrieval | `cognitive_core/qdrant_retrieval.py` | Ollama embeddings + Qdrant vector search over ACTIVE/VERIFIED notes |
| Security Auditor | `cognitive_core/security_audit.py` | OWASP-adjacent static heuristics scan (secrets, eval/exec, TLS, shell=True) |
| Skill Router | `cognitive_core/skill_router.py` | Token-overlap routing of a task description to matching skills in `.agents/skills/` |
| Trading Decision Logger | `cognitive_core/trading_decisions.py` | Shapes trading decisions into RAW `MemoryCandidate` objects for the review queue |

## CLI (Package 8 additions)

```bash
# Semantic search (requires local Ollama embedding model + local Qdrant)
python -m cognitive_core.memory_v6_cli reindex-semantic
python -m cognitive_core.memory_v6_cli search-semantic "SQLite WAL decision" --top-k 5
python -m cognitive_core.memory_v6_cli benchmark --retrieval semantic

# Security audit over any local project directory
python -m cognitive_core.memory_v6_cli security-audit --target /path/to/LogAnalyzer

# Route a task description to the closest matching operational skills
python -m cognitive_core.memory_v6_cli route-skill "harden the trading bot against secrets leaking into logs"
```

## Status

All five originally planned Memory V6 packages, three follow-on extensions, and the four Package 8 utility modules (semantic retrieval, security audit, skill routing, trading decisions) are complete and pushed to `main`.

## Remaining Future Extension

1. Real vault query/answer pairs collected into `cognitive_core/benchmarks/sample_cases.jsonl` — requires organic usage data and cannot be fabricated.
2. Production Qdrant/Ollama-embedding deployment (currently code-complete but requires the operator to run both services locally or remotely).
