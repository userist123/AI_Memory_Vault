---
id: "e4eca2b5-20e0-4082-aeb3-588d598f10c9"
type: knowledge
lifecycle: REVIEW
category: cognitive_core.architecture
tags: [multi-agent, orchestrator, router-agent, retrieval-agent, critic-agent, verifier-agent, consolidator-agent, architecture]
created: 2026-08-15
updated: 2026-08-15
provenance:
  source_type: ai
  source_ref: "Direct source verification of cognitive_core/orchestrator.py and cognitive_core/agents/*.py"
confidence: high
verification: unverified
relations: []
---

# Multi-Agent Pipeline Architecture

This note exists specifically to bridge a gap: the five specialized worker agents below are Python classes (`.py` files), which Obsidian cannot link directly. This note is their Markdown-linkable representation, so their real relationships are visible in the vault's graph view, not just in code.

## The Five Worker Agents

All five live in `cognitive_core/agents/`, all inherit from `BaseWorkerAgent`, and all are instantiated by `MultiAgentOrchestrator` (`cognitive_core/orchestrator.py`) against the same shared `MemoryController` and `ToolRouter` instances -- so none of them can bypass the canonical trust boundary (see [[04_MEMORY/Lessons/Trust_Boundary_Hardening_Requires_Attest_Not_Overlay]]).

| Agent | permitted_actions | Role |
|---|---|---|
| `RouterAgent` | `search`, `read` | Triage: analyzes query complexity, decides dispatch needs |
| `RetrievalAgent` | `search`, `read` | Hybrid retrieval: spreading activation (`ActivationEngine`) + semantic recall scoring (`RecallEngine`) |
| `CriticAgent` | `read`, `propose` | Evaluates failure outcomes (6-stage Reflexion) and critiques candidate memories (SelfRefine) |
| `VerifierAgent` | `read` (only) | Validates provenance/verification claims against source-of-truth hierarchy -- cannot itself write anything |
| `ConsolidatorAgent` | `search`, `read`, `propose`, `archive` | Wraps the legacy `Deduplicator`/`Consolidator` services; runs deduplication + lesson consolidation |

## Real Pipeline Relationship (verified from `MultiAgentOrchestrator.route_and_dispatch()`)

```
MultiAgentOrchestrator.route_and_dispatch(principal, query, context)
    |
    +-- RouterAgent.process_task()        (triage, additive observability)
    |
    +-- RetrievalAgent.process_task()     (canonical retrieval path -- single execution per dispatch)
    |       uses: ActivationEngine + RecallEngine
    |
    +-- VerifierAgent.process_task()      (read-only verification check)
    |
    +-- CriticAgent.process_task()        (evaluation/critique)
    |
    +-- Synthesis (orchestration_history + total_context_used)
```

`ConsolidatorAgent` is NOT part of `route_and_dispatch()` -- it is invoked separately by `MultiAgentOrchestrator.run_maintenance_pipeline()`, which runs deduplication (`scan_for_duplicates`) and lesson consolidation (`consolidate_lessons`) as a background maintenance operation, not as part of answering a single query. This was a deliberate architectural decision made when the two agent systems (`MultiAgentOrchestrator`'s own `SubagentSpec`-gated dispatch and the `cognitive_core/agents/*` classes) were unified -- see the P0 orchestrator integration commits for the full history.

## Trust Boundary Note

No agent in this pipeline can escalate trust on its own:
- `VerifierAgent` cannot write anything (`read`-only).
- `CriticAgent` and `ConsolidatorAgent` can `propose`, but any resulting note still passes through `MemoryController.propose()`'s P0 guards -- it cannot be created pre-verified or with privileged provenance.
- The only path to `verification="verified"` remains `MemoryController.attest()`, restricted to `HUMAN`/`ADMIN` -- no worker agent has `attest` in its `permitted_actions`.

## Related
- [[00_CORE/GRAPH/09 Agent Evidence Map]]
- [[00_CORE/GRAPH/01 Cognitive System Map]]
- [[04_MEMORY/Lessons/Trust_Boundary_Hardening_Requires_Attest_Not_Overlay]]
