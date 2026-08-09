---
id: "0c4c8b76-85c4-4fde-a14a-4bde0b840002"
type: procedure
lifecycle: ACTIVE
category: vault-governance
tags: [system, metadata, frontmatter]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening Plan, 2026-08-09"
confidence: very_high
verification: verified
relations:
  - relation: supports
    target: "[[Integrity Check]]"
---

# Canonical Frontmatter

Every new or materially updated canonical note must use this schema. Existing notes may be migrated incrementally; their missing fields are validation findings, not permission to invent values.

```yaml
---
id: "<RFC 4122 UUID>"              # generate once; never derive from filename
type: knowledge                    # see allowed types below
lifecycle: REVIEW                  # exact lifecycle state
category: <controlled-or-free-text-scope>
tags: []
created: YYYY-MM-DD
updated: YYYY-MM-DD
provenance:
  source_type: user|official|execution|experience|ai|inference|import
  source_ref: "<source, conversation, URL, or identifier>"
  source_date: YYYY-MM-DD           # omit when unknown
  original_path: "<relative RAW_IMPORTS path>" # required for derivatives
  extraction_date: YYYY-MM-DD       # required for imported derivatives
  redaction: none|applied|not_applicable
confidence: very_high|high|medium|low|unknown
verification: verified|partially_verified|unverified|inferred
relations:
  - relation: related_to
    target: "[[Target Note]]"
    target_id: "<target UUID when known>"
---
```

## Rules

- `id` is a UUID generated once at note creation and remains stable through renames and moves.
- Allowed `type` values: `knowledge`, `project`, `procedure`, `decision`, `experience`, `error`, `lesson`, `preference`, `resource`, `hypothesis`, `system`, `core`, and `index`. The last three are documentation artifacts, not memory classes.
- Allowed `lifecycle` values are exactly `RAW`, `CLASSIFIED`, `NORMALIZED`, `REVIEW`, `VERIFIED`, `ACTIVE`, `SUPERSEDED`, and `ARCHIVED`.
- `source_ref` may be empty only when no source exists; do not fabricate provenance.
- `relations.target` is a quoted wikilink. `target_id` is added when known and does not replace the wikilink.
- `confidence` describes strength of evidence; `verification` describes the verification state. They are independent.

## Related

- [[Confidence Model]]
- [[Memory Lifecycle]]
- [[Knowledge Graph Relations]]
