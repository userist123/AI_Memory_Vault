---
id: "0c4c8b76-85c4-4fde-a14a-4bde0b840005"
type: system
lifecycle: ACTIVE
category: vault-governance
tags: [system, graph, wikilinks]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening Plan, 2026-08-09"
confidence: very_high
verification: verified
relations:
  - relation: refines
    target: "[[Knowledge Graph Schema]]"
---

# Knowledge Graph Relations

Wikilinks are the portable graph layer. A relationship is semantic only when it is recorded in frontmatter `relations` or stated unambiguously in the note body.

| Relation | Meaning |
|---|---|
| `related_to` | relevant thematic association |
| `depends_on` | requires the target |
| `supports` | evidence or rationale supports target |
| `implements` | realizes target policy or design |
| `derived_from` | extracted from target/source |
| `caused_by` | target caused this note/event |
| `solved_by` | target resolved this error/problem |
| `contradicts` | claims cannot both hold in the same scope |
| `replaces` | supersedes target as the current canonical guidance |
| `used_by` | target uses this note |

Relations are directional except `related_to`. Do not add links merely to increase graph density. For unresolved disagreement, retain both notes and use `contradicts` with scope and provenance.

## Related

- [[Canonical Frontmatter]]
- [[Knowledge Graph Schema]]
