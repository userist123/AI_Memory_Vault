---
id: "89105d0b-9fd8-4037-906f-ed2325a9f1bc"
type: system
lifecycle: ACTIVE
category: vault-governance
tags: [system, lifecycle, validation]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening Plan, 2026-08-09"
confidence: very_high
verification: verified
relations:
  - target_id: "54b48919-d58a-4502-a20f-2717b022d375"
    type: implements
    target: "[[Memory Protocol]]"
---

# Memory Lifecycle

The canonical lifecycle is:

`RAW → CLASSIFIED → NORMALIZED → REVIEW → VERIFIED → ACTIVE → SUPERSEDED/ARCHIVED`

| State | Meaning | Storage rule |
|---|---|---|
| `RAW` | unmodified external evidence | `06_INBOX/RAW_IMPORTS/`; never canonical or indexed as canonical |
| `CLASSIFIED` | candidate has a tentative type | derivative outside RAW_IMPORTS |
| `NORMALIZED` | candidate is atomic and uses schema | derivative outside RAW_IMPORTS |
| `REVIEW` | awaits deduplication, conflict, provenance, and security review | not active canonical guidance |
| `VERIFIED` | claims were checked to stated verification level | eligible for promotion |
| `ACTIVE` | approved canonical memory | canonical folders only |
| `SUPERSEDED` | retained history replaced by a newer note | keep link to replacement |
| `ARCHIVED` | retained but no longer current | never delete solely for age |

Only a derivative moves through the lifecycle. The raw original stays `RAW` permanently.

## Related

- [[Promotion and Human Review]]
- [[Import Pipeline]]

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
