---
title: Skill Agent Memory MOC
status: navigation
purpose: Obsidian map for skills, agents, references and memory
---

# Skill / Agent / Memory MOC

> Central navigation surface for the relationship between reusable skills, agents, external references and the Memory Vault.

## Core maps

- [[Obsidian_Skill_Agent_Memory_Sync]]
- [[README]]
- [[README]]
- [[02_MEMORY/README]]
- [[03_COGNITIVE_CORE/README]]
- [[README]]

## External skill ingestion

- [[06_INBOX/RAW_IMPORTS/skills/_REGISTRY.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_SOURCE_REGISTRY.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_DISCOVERY_GRAPH.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_DEDUPLICATION.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_LICENSES.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_VALIDATION_REPORT.md]]

## Relationship model

```text
                    ┌──────────────┐
                    │   SOURCES    │
                    └──────┬───────┘
                           │
                           ▼
                    ┌──────────────┐
                    │ RAW SKILLS   │
                    └──────┬───────┘
                           │ verified / accepted
                           ▼
                    ┌──────────────┐
                    │  CAPABILITY  │
                    └──────┬───────┘
                           │
             ┌─────────────┴─────────────┐
             ▼                           ▼
        ┌─────────┐                 ┌───────────┐
        │ AGENTS  │                 │ REFERENCES│
        └────┬────┘                 └─────┬─────┘
             │                           │
             └────────────┬──────────────┘
                          ▼
                   ┌─────────────┐
                   │ VERIFICATION│
                   └──────┬──────┘
                          ▼
                   ┌─────────────┐
                   │   MEMORY    │
                   └──────┬──────┘
                          ▼
                   ┌─────────────┐
                   │  OBSIDIAN   │
                   └─────────────┘
```

## Rules

- Skills are reusable capabilities.
- Agents orchestrate capabilities; they do not duplicate them.
- References provide evidence/context; they are not automatically skills.
- Raw imports are not canonical memory.
- Verified knowledge may be promoted through the existing lifecycle.
- Obsidian links the system together but does not replace the canonical registries or cognitive core.

## Recommended Obsidian navigation

Use this MOC as the starting point for manual inspection of:

- skill families;
- agent capabilities;
- backend references;
- programming-language references;
- web/design skills;
- verification and provenance;
- memory promotion.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
