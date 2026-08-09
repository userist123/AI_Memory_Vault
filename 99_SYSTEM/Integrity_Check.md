---
id: "0c4c8b76-85c4-4fde-a14a-4bde0b840008"
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
  - relation: implements
    target: "[[Quality Control]]"
---

# Integrity Check

Run before promotion, before a material batch change, and after a migration.

## Required detections

1. invalid or unparsable YAML frontmatter;
2. missing `id`, `type`, or `lifecycle`, and malformed or duplicate IDs;
3. lifecycle, confidence, or verification values outside the canonical enums;
4. wikilinks whose target note does not exist (with an allowlist for explicitly marked future links);
5. probable duplicates by title, IDs, overlapping claims, and semantic review queue;
6. path references other than `06_INBOX/RAW_IMPORTS/` for raw imports;
7. obvious conflicts: opposing current claims in the same scope without `contradicts` relation or scope distinction;
8. files in `RAW_IMPORTS/`, or content derived from them, included in a canonical-memory index;
9. imported derivatives without `provenance.original_path`, or with an original path that does not exist.

## Results and remediation

Report the file, rule, severity, and suggested remediation. Do not auto-delete, overwrite, or auto-merge. A failed check blocks promotion to `ACTIVE` unless a documented human exception exists.

## Scope boundary

This specification defines checks only. It does not implement a controller, RAG runtime, index, embeddings, graph database, or automation.

## Related

- [[Canonical Frontmatter]]
- [[Storage Conventions]]
- [[Promotion and Human Review]]
