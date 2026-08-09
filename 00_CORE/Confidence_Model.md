---
type: core
category: confidence
status: active
version: 1.0.0
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

Confidence measures evidence strength; verification records the verification state. Neither is inferred from a note's filename, lifecycle, or source platform. Imported AI content starts as `low`/`medium` with `unverified`/`inferred` unless independently checked.`r`n`r`nThe controlled schema and enums are defined in [[Canonical Frontmatter]].`r`n`r`n## Conflict Rule

When two notes conflict:

1. prefer stronger provenance;
2. prefer verified information;
3. compare dates;
4. check scope/environment;
5. preserve both when context differs;
6. create a Decision or Conflict note if unresolved.

Never silently overwrite a high-confidence note with a low-confidence import.
