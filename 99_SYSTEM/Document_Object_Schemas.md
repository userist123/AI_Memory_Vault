---
id: "0c4c8b76-85c4-4fde-a14a-4bde0b840012"
type: system
lifecycle: ACTIVE
category: vault-governance
tags: [system, metadata, classification]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening continuation, 2026-08-09"
confidence: very_high
verification: verified
relations:
  - relation: supports
    target: "[[Integrity Check]]"
---

# Document Object Schemas

## Object classes

The Vault contains two distinct object classes:

1. **Memory objects** — reusable knowledge, projects, procedures, decisions, experiences, errors, lessons, preferences, resources, and hypotheses. They use [[Canonical Frontmatter]].
2. **Document objects** — operating material that is not memory: system documents, policies, specifications, templates, and indexes. They use the rules below.

`AGENTS.md` is an operating-contract file and is explicitly exempt from document frontmatter validation.

## Shared document-object fields

```yaml
---
id: "<RFC 4122 UUID>"
document_kind: system_document|policy|specification|index
document_status: active|superseded|archived
category: <scope>
created: YYYY-MM-DD                 # omit only when not recoverable
updated: YYYY-MM-DD                 # omit only when not recoverable
provenance_status: complete|incomplete|not_applicable
relations: []
---
```

Use `provenance_status: incomplete` when the available file does not establish its origin. Do not add confidence or verification fields to document objects merely to satisfy a memory-object schema.

## Kind-specific requirements

| Kind | Required additions | Validation focus |
|---|---|---|
| `system_document` | `category` | stable operating/reference context |
| `policy` | `policy_scope` | unambiguous rules and precedence |
| `specification` | `implementation_status: documentation_only|implemented` | scope boundary and non-implemented components |
| `index` | `index_scope` | links resolve; no canonical-memory claim |
| `template` | directory `90_TEMPLATES/`; `template_for` in generated frontmatter | placeholders and generated memory-object schema |

Templates are not canonical notes. The frontmatter in a template is the schema of the note it produces, so Integrity Check validates template structure without requiring a concrete stable ID for the template file itself.

## Related

- [[Canonical Frontmatter]]
- [[Integrity Check]]
- [[Storage Conventions]]

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[11 Templates and System Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
