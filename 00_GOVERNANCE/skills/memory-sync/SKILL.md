---
name: memory-sync
description: Synchronize Claude's working knowledge with the canonical AI Memory Vault while preserving provenance, avoiding duplicate memory, and keeping Obsidian as a navigation/presentation layer.
id: "skill-memory-sync"
title: "Memory Synchronization"
type: procedure
lifecycle: REVIEW
verification: unverified
created: '2026-08-24'
updated: '2026-09-04'
provenance:
  source_type: unknown
  source_ref: "00_GOVERNANCE/skills/memory-sync/SKILL.md"
---

# Memory Synchronization

Use the Vault as the source of truth for durable memory.

## Rules

- Read existing canonical records before creating new ones.
- Detect duplicates and superseded records before writing.
- Preserve source and provenance metadata.
- Keep raw external imports separate from canonical knowledge.
- Do not make Obsidian a competing memory database.
- When a durable change is required, update the canonical record and any relevant index/MOC rather than creating parallel copies.

## Synchronization layers

`RAW -> validation -> classification -> canonical knowledge -> agents/skills -> Obsidian navigation`

When synchronization cannot be performed safely, report the missing information instead of fabricating state.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
