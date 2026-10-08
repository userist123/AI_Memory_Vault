---
name: agent-orchestration
description: Use the AI Memory Vault agent definitions and capability metadata to select, coordinate, and sequence specialized agents and skills for Claude Code tasks.
---

# Agent Orchestration

## Canonical Agent Router

Use the central Agent Router at `03_IMPLEMENTATION/packages/routing/` before external execution. This skill coordinates with the router; it must not create a competing routing policy.

The router selects eligible agent/runtime/model-tier/skills/memory/verifier metadata. Runtime-specific prompt bodies stay outside the router.

If the router returns `BLOCKED`, do not silently fall back to another agent. If it returns `PLANNED`, do not report execution availability until the host confirms it.

Before assigning work, identify the task's domains and required capabilities.

## Selection

Prefer an existing specialized agent when one is documented in the Vault. Attach only the skills needed for that agent's responsibility.

## Coordination

- Separate discovery, implementation, verification, and documentation responsibilities when useful.
- Reuse existing agent contracts instead of creating conflicting duplicates.
- Record durable architectural or project decisions in the canonical Vault.
- Verify outputs before promoting generated knowledge or external material.

## Boundaries

Do not treat every folder under `06_INBOX/RAW_IMPORTS` as an approved agent. Validate provenance and classification first.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
