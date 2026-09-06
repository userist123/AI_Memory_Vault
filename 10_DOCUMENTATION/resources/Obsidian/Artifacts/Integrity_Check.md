---
id: "00b606ec-9dda-4a8f-a797-73de0f22a025"
type: procedure
lifecycle: ACTIVE
category: vault-governance
tags: [system, integrity, validation]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening Plan, 2026-08-09"
confidence: very_high
verification: verified
relations:
  - target_id: "4940167c-cc01-4314-82f7-cece152799b1"
    type: implements
    target: "[[Quality Control]]"
---

# Integrity Check

Run before promotion, before a material batch change, and after a migration.

## Validation scopes

| Scope | Applies to | Required schema |
|---|---|---|
| Memory objects | canonical objects in `01_KNOWLEDGE/`, `02_PROJECTS/`, `03_PROCEDURES/`, `04_MEMORY/`, and `05_RESOURCES/` | [[Canonical Frontmatter]] |
| Document objects | system documents, policies, specifications, and indexes | [[Document Object Schemas]] |
| Templates | `90_TEMPLATES/` | template structure only; not canonical notes |
| Raw evidence | `06_INBOX/RAW_IMPORTS/` | preservation only; never rewrite or canonical-index |
| Operating contract | `AGENTS.md` | exempt from frontmatter validation |

## Required detections

1. invalid or unparsable YAML frontmatter in memory or document objects;
2. memory objects missing `id`, `type`, or `lifecycle`, and malformed or duplicate IDs;
3. document objects missing `id`, `document_kind`, `document_status`, or kind-specific fields;
4. templates missing the fields required in the note they generate; do not require a concrete template-file ID;
5. lifecycle, confidence, or verification values outside the memory-object enums;
6. wikilinks whose target note does not exist, except explicitly marked future links;
7. probable duplicates by title, IDs, overlapping claims, and semantic review queue;
8. path references other than `06_INBOX/RAW_IMPORTS/` for raw imports;
9. obvious conflicts: opposing current claims in the same scope without `contradicts` relation or scope distinction;
10. files in `RAW_IMPORTS/`, or content derived from them, included in a canonical-memory index;
11. imported derivatives without `provenance.original_path`, or with an original path that does not exist.

## Results and remediation

Report the file, rule, severity, and suggested remediation. Do not auto-delete, overwrite, or auto-merge. A failed check blocks promotion to `ACTIVE` unless a documented human exception exists.

## Scope boundary

This specification defines checks only. It does not implement a controller, RAG runtime, index, embeddings, graph database, or automation.

## Related

- [[Canonical Frontmatter]]
- [[Storage Conventions]]
- [[Promotion and Human Review]]

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
