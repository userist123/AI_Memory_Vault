---
id: "35ed1c6d-dd41-42e9-a9c9-1e5e3c6a4ad2"
type: procedure
lifecycle: ACTIVE
category: vault-governance
tags: [system, provenance, redaction, security]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening Plan, 2026-08-09"
confidence: very_high
verification: verified
relations:
  - relation: implements
    target: "[[Storage Conventions]]"
---

# Provenance and Redaction

## Provenance requirements

Canonical notes retain the strongest available source reference. Imported derivatives must set `provenance.source_type: import`, identify platform/conversation or original identifier in `source_ref`, and include `original_path` relative to `06_INBOX/RAW_IMPORTS/`.

## Redaction

Never copy credentials, tokens, private keys, passwords, or unnecessary personal data into canonical notes. If a raw source contains sensitive data, leave the raw source untouched and create only a redacted derivative. Set `provenance.redaction: applied` and state the category of removed content without reproducing it.

## Integrity rules

- Raw evidence remains in `RAW_IMPORTS/` permanently.
- A redaction never overwrites its source.
- An AI-generated extraction remains `unverified` or `inferred` until independently checked.
- A source reference must be specific enough to locate the evidence without relying on the canonical filename.

## Related

- [[Storage Conventions]]
- [[Import Pipeline]]
- [[Promotion and Human Review]]
