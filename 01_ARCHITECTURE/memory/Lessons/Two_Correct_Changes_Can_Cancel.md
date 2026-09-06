---
id: fd63129c-091c-4171-9eb4-b7c28190b1a4
type: lesson
lifecycle: REVIEW
category: engineering.diagnosis
tags: ['integration', 'regression', 'measurement', 'method']
created: "2026-09-06"
updated: "2026-09-06"
provenance:
  source_type: 'execution'
  source_ref: 'session 2026-09-06: r001-r019, measured in-repo'
confidence: high
verification: unverified
relations: []
---

# Two independently correct changes can cancel each other and report success

## Problem

Graph expansion ran on every query, reported status `ok`, and added exactly zero nodes. No test failed, no error appeared, and the graph-on arm was silently identical to the baseline.

## How it was found

The expansion budget was `min(2*len(notes), 20) - len(notes)`. A separate, correct change had raised the lexical candidate limit from a small number to 200, which makes that expression negative. Every test still passed because the tests use small mock corpora where the formula behaves. The contradiction that exposed it was a metric that cannot happen: context recall above candidate recall, meaning a note reached the final pack without being a candidate.

## What fixed it

Nothing was silently rebalanced. The two constraints are genuinely incompatible — a context budget capping total candidates at 20, and a recall guarantee producing 200 — so the default was left untouched and an explicit override was added for measurement, with the conflict written down for an owner to decide.

## How it was verified

Measured by running both arms through `MemoryController.search()` on the real corpus and comparing per-case. The zero-expansion cases are now hard failures under strict mode instead of silent successes.

## Reuse this when

After changing any limit, re-run everything downstream that **derives** a value from it, not just what references it by name. Arithmetic on a constant is a dependency that no import graph shows. And treat an impossible metric as a bug in the measurement before believing it.

## Still open

The budget conflict itself is unresolved by design and belongs to whoever owns the context budget.
