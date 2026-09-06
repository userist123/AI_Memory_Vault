---
id: bca30cd9-2208-4c0a-b274-6ec893699666
type: lesson
lifecycle: REVIEW
category: engineering.diagnosis
tags: ['architecture', 'audit', 'shim', 'method']
created: "2026-09-06"
updated: "2026-09-06"
provenance:
  source_type: 'execution'
  source_ref: 'session 2026-09-06: r001-r019, measured in-repo'
confidence: high
verification: unverified
relations: []
---

# A module existing is not a module being used, and a shim looks like an absence

## Problem

Two independent reviewers reached opposite wrong conclusions about the same code: one declared a mature system from the directory structure, the other declared graph expansion absent because a grep found nothing.

## How it was found

`memory_controller/` is a 19-line `__init__.py` that sets `__path__` across sibling packages. The implementation is `memory/controller.py`, about a thousand lines. A grep inside the shim finds nothing and is not evidence of absence. `inspect.getsourcefile` on the imported symbol settles it in one line. Conversely, nine cognitive modules exist with well-chosen names and zero production consumers.

## What fixed it

Claims about what is wired now carry the consumer grep as evidence, and `VAULT_STATE.md` records which components are real, which are present but unwired, and which are broken — enforced by tests that fail when a module the card calls unwired gains a consumer.

## How it was verified

The state card's claims are re-derived from the vault on every test run: note and edge counts, seed and gold populations, the expansion default, the traversal depth, and the shim still being a shim.

## Reuse this when

Before believing any component is in production, resolve the symbol you actually import and grep for consumers excluding tests and benchmarks. Structure and naming are the least reliable evidence available; both optimism and pessimism come from reading them instead of the call path.

## Still open

Whether the vault measurably improves a new agent's competence is still unproven; the state card makes it measurable, it does not demonstrate it.
