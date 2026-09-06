---
id: "317339ca-1ee5-47eb-9571-6f1517232d89"
type: procedure
lifecycle: ACTIVE
category: vault-maintenance
tags: [procedure, import, sanitization, memory]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: import
  source_ref: "Claude vault baseline"
  redaction: not_applicable
confidence: high
verification: partially_verified
relations:
  - target_id: "35ed1c6d-dd41-42e9-a9c9-1e5e3c6a4ad2"
    type: implements
    target: "[[Provenance and Redaction]]"
---

# Import Sanitization

## Purpose

Prepare an external-memory derivative without changing the original evidence.

## Procedure

1. Preserve the unmodified export permanently in `06_INBOX/RAW_IMPORTS/`.
2. Create a derivative outside `RAW_IMPORTS/`; record its source in `provenance.source_ref` and `provenance.original_path`.
3. Remove conversational noise, separate atomic concepts, classify the candidate, and assign provisional confidence and verification.
4. Redact credentials, secrets, and unnecessary personal data from the derivative only; record `provenance.redaction: applied` when used.
5. Check duplicates, contradictions, frontmatter, and links using [[Integrity Check]].
6. Move only the derivative through `RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED`.
7. Require human review for the classes defined in [[Promotion and Human Review]].

## Prohibitions

- Do not delete, rewrite, rename, or index `RAW_IMPORTS/` as canonical memory.
- Do not represent imported AI content as independently verified.
- Do not silently merge conflicting claims.

## Verification

- [ ] Original source remains present under `06_INBOX/RAW_IMPORTS/`.
- [ ] Derivative points to the original path.
- [ ] Integrity Check findings are resolved or documented.

## Related

- [[Storage Conventions]]
- [[Memory Lifecycle]]
- [[Provenance and Redaction]]

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
