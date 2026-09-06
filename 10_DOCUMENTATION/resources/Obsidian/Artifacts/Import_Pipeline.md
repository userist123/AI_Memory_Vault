---
id: "8f068f3d-cbc5-4912-b3ec-f15c3518d5ab"
type: procedure
lifecycle: ACTIVE
category: import
tags: [system, import, provenance]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening Plan, 2026-08-09"
  redaction: not_applicable
confidence: very_high
verification: verified
relations:
  - target_id: "89105d0b-9fd8-4037-906f-ed2325a9f1bc"
    type: implements
    target: "[[Memory Lifecycle]]"
---

# Import Pipeline

## Boundary

This is a documentation-only process. It does not implement parsing, indexing, embeddings, RAG, a graph runtime, or automatic promotion.

## Canonical pipeline

```text
External source
  -> RAW (preserved permanently in 06_INBOX/RAW_IMPORTS/)
  -> CLASSIFIED (derivative only)
  -> NORMALIZED
  -> REVIEW (security, provenance, duplicate and conflict checks)
  -> VERIFIED
  -> ACTIVE canonical memory
  -> SUPERSEDED/ARCHIVED when no longer current
```

## Raw evidence

`06_INBOX/RAW_IMPORTS/` is permanent evidence and provenance. Never delete, rewrite, rename, or index its contents as canonical memory. Every normalized or redacted derivative records `provenance.original_path` relative to that directory.

## Review and promotion

- Classify atomically and retain available platform, conversation, date, and source identifier.
- Do not merge merely because content is similar.
- Preserve unresolved contradictions and link them with `contradicts`.
- Require human review for the material defined in [[Promotion and Human Review]].
- Run [[Integrity Check]] before activating a candidate.

## Related

- [[Storage Conventions]]
- [[Provenance and Redaction]]
- [[Canonical Frontmatter]]

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
