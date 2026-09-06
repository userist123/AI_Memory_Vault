---
id: f10029dc-e28e-4710-bf3a-b2b48bfb84dc
type: preference
lifecycle: REVIEW
category: governance.prompting
tags: ['prompting', 'english', 'handoff', 'method']
created: "2026-09-06"
updated: "2026-09-06"
provenance:
  source_type: "user"
  source_ref: "stated 2026-09-06, generalising the trading-bot preference"
confidence: high
verification: unverified
relations: []
---

# Every AI-facing prompt is written in English, in full

## Problem

Requests arrive as one informal line in Romanian. What reached other agents was
sometimes that same line, so the receiving agent began by reconstructing intent,
context and constraints that the sender already knew.

## How it was found

Stated directly by the user, generalising an existing narrower preference:
`Trading_Bot_Prompt_Language_English` recorded the same rule but scoped only to
trading-bot prompts. The rule is not domain-specific.

## What fixed it

Replies to the user stay in Romanian. Everything transmitted to another agent
is English and complete: verified context, task, requirements, what is
forbidden, the traps already paid for, the skills and data to consult, the
method for measuring, and acceptance criteria that can fail.

`30_SCRIPTS/prompt/compile_task_prompt.py` emits the deterministic half —
current commit, live corpus and graph numbers, recorded methods, standing traps
— so no brief starts from a blank page or from stale figures.

## How it was verified

The compiler runs against the live vault and injects measured values rather
than copied ones. Where it cannot measure something it prints
`UNAVAILABLE: <error>` into the prompt instead of omitting the line, so a
missing fact is visible to the receiving agent rather than silently absent.

## Reuse this when

Handing work to any agent, human or otherwise. Detail lost in translation is
detail lost, and an under-specified brief is paid for twice: once by the sender
in re-explanation, once by the receiver in rediscovery.

## Still open

The compiler fills context, traps and acceptance. Task, requirements and
forbidden remain `TODO` markers for the sender to complete — deliberately, since
those require judgement about the specific work. A brief shipped with `TODO`
left in it is unfinished.
