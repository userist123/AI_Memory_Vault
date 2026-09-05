# R002-C — NIS2 Romania Legal Knowledge Ingestion

Working branch: `r002-c/nis2-romania-legal-ingestion-20260906`

Base main SHA: `b42dd9a97d4620849426916aed11df478b3076d0`

## Source boundary

Only these two acts are primary legal sources for R002-C:

1. OUG nr. 155/2024.
2. Legea nr. 124/2025.

Official Portal Legislativ references:
- OUG: https://legislatie.just.ro/Public/DetaliiDocument/293121
- Legea: https://legislatie.just.ro/Public/DetaliiDocumentAfis/299675

The corpus deliberately does not ingest later amending legislation as a primary source. Later amendments are recorded only as a version-gap warning where they affect interpretation of the two requested acts.

## Artifact separation

- `primary/` = source snapshots only.
- `indexes/` = structural mappings/indexes.
- `derived/notes/` = atomic legal knowledge, always REVIEW.
- `derived/controls/` = candidate technical controls, not compliance declarations.
- `derived/tests/` = candidate evidence/tests, not proof of legal compliance.
- `review/` = legal-review queue and unresolved applicability questions.
- `R002-C_HANDOFF.md` = final handoff and status.

## Trust rules

Every primary source is marked:

```yaml
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
requires_legal_review: true
```

Derived interpretations never become ACTIVE automatically. No artifact in this directory is a legal compliance declaration or legal advice.
