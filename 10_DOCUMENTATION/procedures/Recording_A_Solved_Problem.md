---
id: a6cd6aa4-910a-462b-8659-341a183536a8
type: procedure
lifecycle: REVIEW
category: governance.memory
tags: ['procedural-memory', 'completion', 'handoff', 'method']
created: "2026-09-06"
updated: "2026-09-06"
provenance:
  source_type: 'execution'
  source_ref: 'session 2026-09-06: r001-r019, measured in-repo'
confidence: high
verification: unverified
relations: []
---

# Recording a solved problem so the next agent can reuse the method

## Purpose

The vault stores what is known. This procedure covers what was *done*: how a
problem was diagnosed and solved, so the next agent applies the method instead
of rediscovering it. Four separate reviewers spent an evening re-deriving the
same findings because no such record existed.

## When this applies

Any session that diagnosed a non-obvious defect, resolved a conflict between
two correct-looking behaviours, or established a method that would transfer.
Not for routine changes whose reasoning is evident from the diff.

## What "finished" means

A task is finished when all five hold. Anything less is unfinished and must be
recorded as unfinished, with the remainder named.

1. The change is implemented and committed.
2. It is verified by something that would fail if the change were wrong —
   a test, a measurement, or a reproduction. Not "it looks right".
3. Regressions are measured against a stated baseline, in isolation.
4. What remains open is written down explicitly, including "nothing".
5. The method is recorded here if it would transfer.

A green suite is not point 2 on its own. Today a benchmark passed every test
while measuring nothing, because no test exercised it end to end.

## Required sections

Write the note as `type: lesson`, `lifecycle: REVIEW`, with these headings:

- **Problem** — what was actually wrong, not what was reported.
- **How it was found** — the diagnostic path, including what was ruled out.
- **What fixed it** — and what was deliberately left alone.
- **How it was verified** — the evidence that would have failed otherwise.
- **Reuse this when** — the generalisable trigger, written for a stranger.
- **Still open** — explicitly, even if empty.

`Still open` is what separates this from a success story. A note without it is
a claim of completeness that nobody checked.

## Rules

Never `lifecycle: ACTIVE`. Notes enter at REVIEW and are promoted by the
lifecycle policy, never by their author — a session grading its own work is the
failure mode this vault exists to prevent.

`provenance.source_type` must be `execution` when the finding came from running
something, and `inference` when it did not. That distinction is the difference
between a measurement and an opinion.

State what was measured, with the number. "Improved recall" is not a finding;
"candidate recall 0.77, context recall 0.23, n=30" is.

Record failures and dead ends as readily as successes. A recorded NO-GO saves
the next agent the same week.

## Enforcement

`20_TESTS/test_procedural_memory_contract.py` fails when a note in this
category is missing a required section or does not validate against the
canonical frontmatter schema. The contract is checked, not trusted — an
unenforced convention decays exactly like the docstring that claimed for
months that `SynapseStore` was not wired into `search()`.
