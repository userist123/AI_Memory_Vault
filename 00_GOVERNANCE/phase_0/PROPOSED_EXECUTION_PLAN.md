---
id: 9a16789d-0d21-44da-8df3-a83a100f6160
type: procedure
lifecycle: REVIEW
category: governance.phase0
tags: ['phase-0', 'reconciliation', 'measured']
created: "2026-09-20"
updated: "2026-09-20"
provenance:
  source_type: 'execution'
  source_ref: 'phase 0 reconciliation measured on origin/main 10224498c, 2026-09-20'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Proposed execution plan — Phase 0

Phases are sized to fit one five-hour window each. The pacing rule is part of
the work: read the remaining quota at the start, read it again before closing,
and if the window is more than 70% spent, commit, write the handover and
schedule the next phase at the reset time.

## Wave A — truth and control (one window)

Fix the red `main` first: one import line, and agent memory recall works again
without `PYTHONPATH`. Then the lifecycle work — a single transition policy,
pure and exhaustively tested — and the lifecycle floor at the three entry
points, so an agent stops receiving REVIEW notes by default.

Barrier: suite green on the exact SHA, no mutation path outside the policy, no
entry point returning REVIEW to `AI_AGENT` unasked.

## Wave B — honest retrieval (one window)

`reasoning` and `executive` either consume their output or come out. A
versioned `RetrievalTrace` with a reason code for every exclusion. Nothing new
is added to the path in this wave: the point is that what is there is real.

Barrier: no hook that only writes a trace; trace complete for every search.

## Wave C — corpus and graph (one to two windows)

The 5 duplicate groups. The proposer fixed so entity overlap can no longer
produce a strong relation. A new 50-edge sample labelled by someone who did not
build the proposer. Then the purge through `plasticity.py`, with journal and
rollback — which finally gives that module a production consumer.

Barrier: rollback restores the graph byte for byte; the labeller is not the
builder.

## Wave D — measurement (one window)

Only after C, because measuring retrieval over a graph whose strong relations
are 12% correct measures the noise. Held-out set owned by LUNA, labels unseen
by whoever builds.

## What is not planned yet

Dense retrieval, reranking, the learning loop, the UI. Each needs a
preregistered rule before it is measured, and none of them should be built on
top of a path where two hooks are decorative and no entry point filters
lifecycle.

## Running in parallel, outside the waves

The skill translation (`claude/translate-zh-skills`): 734 Markdown files, 24
done, rate-limited by the Gemini quota. It touches no file any wave touches.
