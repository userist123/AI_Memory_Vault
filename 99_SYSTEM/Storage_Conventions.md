---
id: "0c4c8b76-85c4-4fde-a14a-4bde0b840001"
type: procedure
lifecycle: ACTIVE
category: vault-governance
tags: [system, storage, provenance]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening Plan, 2026-08-09"
confidence: very_high
verification: verified
relations:
  - relation: implements
    target: "[[Memory Lifecycle]]"
---

# Storage Conventions

## Canonical locations

| Location | Purpose | Canonical memory? |
|---|---|---|
| `00_CORE/` | operating contract | yes |
| `01_KNOWLEDGE/` | stable reusable knowledge | yes |
| `02_PROJECTS/` | current project state | yes |
| `03_PROCEDURES/` | repeatable procedures | yes |
| `04_MEMORY/` | decisions, experiences, errors, lessons, preferences | yes |
| `05_RESOURCES/` | curated references | yes |
| `06_INBOX/RAW_IMPORTS/` | immutable source evidence | no |
| `90_TEMPLATES/` | note templates | no |
| `99_SYSTEM/` | system specifications | yes |

`06_INBOX/RAW_IMPORTS/` is the only accepted raw-import path. `RAW_IMPORTS` is not a valid path.

## RAW_IMPORTS protection

Raw imports are evidence and provenance, not canonical memory. Do not delete, overwrite, rename, edit, or index them as canonical knowledge. A normalized or redacted derivative must be created outside `RAW_IMPORTS/` and retain the original relative path in `provenance.original_path`.

## Naming

Use descriptive Markdown filenames. A filename may change when a title changes; the note `id` must not. Do not derive identity, provenance, or relations from filenames alone.

## Related

- [[Canonical Frontmatter]]
- [[Provenance and Redaction]]
- [[Integrity Check]]
