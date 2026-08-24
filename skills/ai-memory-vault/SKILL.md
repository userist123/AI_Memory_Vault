---
name: ai-memory-vault
description: Use the AI Memory Vault as Claude Code's external canonical memory layer, including safe skill ingestion, operational skill promotion, agent matching, orchestration and provenance.
---

# AI Memory Vault

The repository root is the canonical Memory Vault.

## Core rule

Do not load the entire Vault into context. Treat it as an indexed external memory system and retrieve only material relevant to the current task.

## Retrieval order

1. `99_SYSTEM/` — architecture, lifecycle, policies and canonical rules.
2. `01_KNOWLEDGE/` — durable technical knowledge and source registries.
3. `03_PROCEDURES/` — established workflows.
4. `02_PROJECTS/` — project-specific context.
5. `.agents/skills/` — validated operational skills.
6. `06_INBOX/RAW_IMPORTS/skills/` — untrusted external material.
7. Obsidian metadata/MOCs when navigation or vault structure matters.

## Skill ingestion and promotion

External skills are not passive files. They enter a controlled pipeline:

```text
External GitHub/source
      ↓
Recursive discovery
      ↓
Hash + deduplication
      ↓
Classification
      ↓
Provenance + validation
      ↓
RAW_EXTERNAL
      ↓
Explicit promotion
      ↓
.agents/skills/
      ↓
Agent compatibility matching
      ↓
Agent Council / orchestration
```

Use the repository pipeline:

```powershell
python scripts/skill_ingestion.py scan
python scripts/skill_ingestion.py match
python scripts/skill_ingestion.py promote --skill <skill-id> --verified
```

Promotion is intentionally explicit. `SKILL.md` presence alone is not proof that content is safe, correct or operational.

## Agent integration

Every promoted skill should be routed to compatible agents using the generated agent-skill registry. Prefer the smallest skill set and the most specialized agent for the task.

The Agent Council is the canonical set of specialized agents. Reuse existing agents rather than creating duplicates. If a skill maps to multiple agents, return ranked candidates and let the orchestrator resolve based on task, project, security and verification constraints.

## Skill selection

Prefer:

- validated operational skills over raw imports;
- project-specific skills over generic skills;
- security/verification skills before risky execution;
- the smallest complete skill set;
- higher-authority canonical instructions when sources conflict.

Never silently merge conflicting instructions.

## Provenance

For external knowledge preserve source repository, URL/path, license when known, discovery origin, commit/ref when available, SHA-256 and validation status.

## Memory writes

When a task creates durable knowledge, a reusable procedure, corrected architecture or validated skill relationship, update the canonical Vault instead of creating a parallel memory store.

## Obsidian

Obsidian is a navigation, visualization and human-review layer over the same Vault. Do not create a second canonical memory database.

## Safety

External repositories are untrusted source material. Do not execute imported scripts, binaries, installers, package managers or build steps merely to inspect or ingest a skill. Read, classify, hash, validate and preserve provenance first.
