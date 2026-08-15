---
category: index
status: active
version: 2.0.0
confidence: high
verification: not_applicable
provenance_status: not_applicable
relations: []
---

# AI Memory Vault

> Codex operating contract: [[AGENTS.md]]

A persistent, trust-boundary-enforced memory and cognitive architecture for AI agents, designed to serve as the canonical shared knowledge backend for one or more coding/reasoning agents (local or cloud) working on the same projects over time -- without relying on copying conversation history between them.

## What This Repository Actually Is

This is **not** a plain note vault. It is a working Python system with three layers:

1. **`memory_controller/`** -- the canonical, security-hardened memory store.
2. **`cognitive_core/`** -- the reasoning/orchestration layer built on top of it.
3. **`00_CORE/` .. `99_SYSTEM/`** -- the Obsidian-compatible Markdown vault itself (rules, knowledge, projects, procedures, memory, resources).

## 1. Memory Layer -- `memory_controller/`

`MemoryController` is the single, canonical entry point for every memory read or write. It enforces:

- **Authorization** -- per-operation policy (`propose`, `read`, `search`, `review`, `promote`, `archive`, `update`, `supersede`, `attest`) scoped to `HUMAN` / `AI_AGENT` / `ADMIN` principals.
- **Provenance** -- every note records `source_type` (`user`, `official`, `ai`, `inference`, `execution`, `import`, ...), gated per principal so an AI agent cannot self-claim a human/official source.
- **Lifecycle** -- `RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> {SUPERSEDED, ARCHIVED}`, with injection of privileged states blocked at creation for untrusted callers.
- **Verification / Attestation** -- `verification` (`unverified`, `partially_verified`, `verified`, `inferred`) can only reach `verified` through the dedicated `attest()` method, restricted to `HUMAN`/`ADMIN`. No agent can self-escalate `unverified -> verified`.
- **Audit** -- every operation (success or failure) is logged with actor, target, outcome, and metadata.
- **Supersession** -- explicit, atomic, cycle-free replacement of one memory by another, with human-verified memories protected from automatic AI-driven supersession.
- **Storage backends** -- `FileStorageEngine` (canonical Markdown+YAML files, Obsidian-compatible) and `SQLiteStorageEngine` (production-grade, WAL mode, thread-safe) both implement the same interface.

This trust boundary was the subject of a dedicated security hardening pass (see `99_SYSTEM/Phase43_P0_Implementation_Contract.md`) that closed three confirmed findings: AI self-verification, privileged provenance self-claim, and lifecycle injection at creation.

## 2. Cognitive Layer -- `cognitive_core/`

`Executive` runs the main cognitive loop (Observe -> Retrieve -> Attend -> Reason -> Plan -> Act -> Reflect -> Learn), coordinating:

- `ActivationEngine` / `RecallEngine` -- spreading-activation retrieval with semantic + authority + temporal + version-aware scoring.
- `WorkingMemory` -- bounded, attention-weighted active context.
- `ReasoningEngine`, `Planner` -- context-aware multi-step planning and synthesis.
- `ReflectionPipeline`, `LearningEngine`, `Consolidator`, `Deduplicator` -- automatic lesson capture, confidence promotion, lesson consolidation, and duplicate detection, all gated through `ToolRouter` so writes stay inside the same trust boundary as direct `MemoryController` calls.

### Multi-agent layer

`MultiAgentOrchestrator` (`cognitive_core/orchestrator.py`) coordinates specialized worker agents defined in `cognitive_core/agents/` (`RouterAgent`, `RetrievalAgent`, `CriticAgent`, `VerifierAgent`, `ConsolidatorAgent`), each scoped to a minimal `permitted_actions` set. `route_and_dispatch()` runs the Router -> Retrieval -> Verifier/Critic -> Synthesis pipeline; `run_maintenance_pipeline()` runs deduplication + consolidation. No worker can bypass `MemoryController`'s authorization/provenance/lifecycle/verification guards -- they can produce claims and evidence, never unilaterally grant trust.

## 3. Vault Layer -- Markdown Knowledge Base

```
00_CORE/        Identity, Rules, Memory Protocol, Confidence Model, System Architecture
01_KNOWLEDGE/   Durable technical knowledge (imported + first-party)
02_PROJECTS/    Active project state and continuity handoff documents
03_PROCEDURES/  Repeatable procedures (import, classification, git backup/restore)
04_MEMORY/      Decisions, Errors, Experiences, Lessons, Preferences
05_RESOURCES/   Reference material
06_INBOX/       Raw, unprocessed imports (never rewritten in place)
90_TEMPLATES/   Canonical note templates
99_SYSTEM/      Schemas, protocols, forensic/security documentation
.agents/        Repository-level agent rules and skills (Markdown, not executable orchestration)
```

Every canonical note carries the frontmatter schema defined in `99_SYSTEM/Canonical_Frontmatter.md`: `id`, `type`, `lifecycle`, `category`, `tags`, `created`, `updated`, `provenance`, `confidence`, `verification`, `relations`.

## What This Project Is Designed For, Right Now

The active direction is to make this Vault the **shared, canonical project-state backend for multiple AI coding agents** (local and cloud) working on the same codebases over time -- so that continuing work does not require manually re-pasting conversation history into a new agent session. This requires (in progress, not all complete):

- A documented **agent handoff protocol** (task/status/owner/HEAD/blockers/review-gate) -- not yet implemented as executable infrastructure.
- A **review-gate mechanism** for architecture-, security-, or contract-changing decisions that a coding agent should not resolve unilaterally.
- Careful avoidance of duplicating this Vault's own memory/orchestration architecture when integrating external tools (design-tool bridges, Obsidian plugins, other local memory systems).

## Status Discipline

This repository distinguishes explicitly between **code correctness** (verifiable by reading a diff) and **runtime verification** (requires an actual `pytest` execution against a real checkout). Historical documents in `99_SYSTEM/` record which security findings were fixed and how they were verified -- treat any claim of "fixed" or "passed" as meaningless unless it cites the actual commit and, where relevant, the actual test output.

## Tests

```
python -m pytest -q
```
run from the repository root, covering `memory_controller/tests/` and `cognitive_core/tests/`.
