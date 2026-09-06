---
id: "b7c3d1e4-9a52-4f68-b103-2c8e5d7a4f19"
type: procedure
lifecycle: REVIEW
category: governance.prompting
tags: ['prompting', 'handoff', 'intent', 'method']
created: "2026-09-06"
updated: "2026-09-06"
provenance:
  source_type: "execution"
  source_ref: "session 2026-09-06: the r007-r016 briefs and what each one had to contain"
confidence: high
verification: unverified
relations: []
---

# Compiling an informal request into a brief another agent can execute

## Purpose

A request arrives as one line: *"see whether the graph helps"*. What must reach
another agent is a full English brief. The gap between those two is judgement,
and judgement that lives only in one head is unusable by everyone else.

This procedure encodes the transferable part. It does not make an agent
brilliant; it removes the difference between an agent that has never seen this
repository and one that spent an evening auditing it.

## The three reflexes that mattered

Everything useful found in this repository came from three habits, not from
insight:

**Run it before believing it.** A commit message said graph expansion was wired
into production; it was. A docstring said it was not; it was. Both claims came
from reading rather than executing. `inspect.getsourcefile` on the imported
symbol settles in one line what an argument cannot.

**Treat a contradiction as a bug in your own measurement.** Context recall above
candidate recall is impossible — nothing reaches the final pack without being a
candidate. That impossibility exposed a runner reading a trace key that did not
exist. When numbers cannot both be true, suspect the instrument first.

**Write down what you could not verify.** Every finding this session that
survived scrutiny carried its evidence; every one that collapsed had been
asserted. "I could not confirm X" is a result. Silence about X is not.

## Choose the kind of work first

The kind determines what the brief must contain. Getting this wrong is how
briefs fail, and each entry below was paid for:

| Kind | The request sounds like | Non-negotiable |
|---|---|---|
| `implement` | build, add, wire, make it do | tests that would fail if wrong; no widening a policy as a side effect |
| `verify` | check whether, is X actually, audit | resolve the symbol, not the name; report what stayed unverified |
| `measure` | does X help, compare, is it worth it | two arms; treatment fails loudly; STOP condition stated before running |
| `fix` | it is broken, repair, this crashes | reproduce first; enumerate what the fix newly reaches |
| `migrate` | move, rename, delete, clean up | recovery path proven before the first destructive operation |

`python 30_SCRIPTS/prompt/compile_task_prompt.py --intent <kind>` emits the
matching requirements, prohibitions and deliverables, together with the live
state of the vault.

## Worked examples from this repository

**Request:** *"see whether the graph helps"* → `measure`

Expanded to: two arms through `MemoryController.search()` with identical corpus
and principal; the graph arm running with `strict_graph_expansion=True` so a
degraded expansion raises rather than silently returning the baseline ranking; a
declared subpopulation, because the graph reaches 78 of 842 notes and must not
be pooled with whole-corpus figures; and a stop condition.

Why it mattered: the first run compared a baseline against itself. Expansion
reported status `ok` while adding zero nodes, because a candidate-limit change
elsewhere had driven the expansion budget negative. Without the fail-loud
requirement, that would have shipped as "no significant difference".

**Request:** *"promote the proposed edges"* → `measure`, not `implement`

A request phrased as an action was really a question about quality. Expanded
with a hand-verified sample of 50 and a stop condition at 70% precision. It
measured 18% and stopped. Treating it as `implement` would have promoted ~2000
edges that shared vocabulary rather than meaning.

**Request:** *"delete all the branches, leave only main"* → `migrate`

Expanded with a recovery path: an archive tag per branch, pushed and verified
32/32 before a single deletion. The instruction was explicit and the deletion
was correct; the recovery path is what made it reversible rather than a bet.

**Request:** *"check what Antigravity did"* → `verify`

Expanded to running the suite and the gate rather than reading the report. Two
of three claims held. The third — that graph wiring did not exist — was wrong,
and would have redirected a week of work, because the reviewer had grepped a
19-line shim instead of resolving the import.

## Writing the task itself

State the outcome, not the steps. *"Make the storage engine see the vault"* is
executable; *"look into the storage situation"* is not.

Include the number you expect to move and its current value. A brief that says
"improve recall" cannot be finished. One that says "candidate recall is 0.77 and
context recall 0.23; close that gap" can.

Name what you already know is out of scope. Most of the cost in this repository
came from work that was correct and unasked for.

## Rules

Replies to the requester are in their language. Everything transmitted to an
agent is English and complete — see
`01_ARCHITECTURE/memory/Preferences/AI_Facing_Prompts_In_English.md`.

Never ship a brief with `TODO` left in it. The compiler marks the sections
needing judgement precisely so an unfinished brief cannot pass for a finished
one.

Carry the definition of finished into every brief. A task is done when it is
implemented, verified by something that would have failed otherwise, measured
against a stated baseline in isolation, has its remainder written down
explicitly, and has its method recorded if it transfers.

## Enforcement

`20_TESTS/test_prompt_compiler.py` fails when a brief omits a measured fact,
drops a recorded method, loses the definition of finished, or leaks the
requester's language into an AI-facing prompt.

## Still open

Intent is chosen by the sender, not inferred from the request. Classifying
*"see whether the graph helps"* as `measure` rather than `implement` is the
single highest-leverage judgement in this procedure and remains manual. The
table above is the aid; it is not automation.
