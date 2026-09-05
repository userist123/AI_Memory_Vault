---
type: core
category: confidence
status: active
version: 1.0.0
id: "3452fafe-8b80-445e-a742-9ceb68662e69"
document_kind: policy
document_status: active
provenance_status: incomplete
relations: []
policy_scope: vault-governance
---

# Confidence Model

## Levels

### Very High

Directly verified by the user, execution, test, or authoritative primary source.

### High

Strong evidence, stable documentation, or repeated successful use.

### Medium

Plausible and supported but not independently verified.

### Low

AI-generated, inferred, old, ambiguous, or weakly supported.

### Unknown

Insufficient evidence.

## Metadata

Recommended:

```yaml
confidence: high
verification: verified
provenance:
  source_type: user|official|execution|experience|ai|inference|import
  source_ref: "..."
```

Confidence measures evidence strength; verification records the verification state. Neither is inferred from a note's filename, lifecycle, or source platform. Imported AI content starts as `low`/`medium` with `unverified`/`inferred` unless independently checked.

The controlled schema and enums are defined in [[Canonical_Frontmatter|Canonical Frontmatter]].

## Conflict Rule

When two notes conflict:

1. prefer stronger provenance;
2. prefer verified information;
3. compare dates;
4. check scope/environment;
5. preserve both when context differs;
6. create a Decision or Conflict note if unresolved.

Never silently overwrite a high-confidence note with a low-confidence import.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
