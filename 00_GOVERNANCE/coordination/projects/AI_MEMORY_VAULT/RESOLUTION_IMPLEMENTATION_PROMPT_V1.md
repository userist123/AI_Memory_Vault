# Bounded Terminal Resolution — Implementation Prompt V1

## Mission

Implement the cognitive-memory architecture as a bounded task-resolution pipeline with an explicit terminal result. Do not model the whole architecture as an infinite loop.

## Canonical semantic contract

```text
TASK
  ↓
EXPERIENCE
  ↓
MODEL / PATTERN
  ↓
APPLICABILITY
  ↓
INFLUENCE
  ↓
DECISION CANDIDATE
  ↓
BOUNDED VERIFICATION LOOP
  ↓
REORGANIZATION
  ↓
TERMINAL RESULT
  ↓
FINAL RESPONSE
```

Verification is the only repeatable sub-process. Reorganization is a post-verification memory update and must never implicitly reopen the current task.

## Terminal outcomes

A task must terminate in exactly one of:

- `RESOLVED`
- `ABSTAINED`
- `HUMAN_CONFIRMATION_REQUIRED`

`FINAL_RESPONSE` is the final observable stage and cannot transition to another cognitive stage for the same resolution session.

## Mandatory invariants

1. Verification has an explicit finite budget.
2. No verification is allowed after terminalization.
3. Reorganization cannot re-enter TASK/EXPERIENCE for the current session.
4. A final response is emitted only after the terminal gate is satisfied.
5. Human-confirmation-required is terminal and does not execute the verification loop unless a separate confirmation session is explicitly started.
6. Evidence, applicability, contradiction, safety and verification cost remain separate signals; do not collapse them into one canonical confidence scalar.
7. Retrieval and influence remain separate operations.
8. The memory compiler must not consume experiment oracle labels when generating influence.
9. Experimental negative results remain unchanged; do not tune parameters after observing outcomes.
10. Production claims require runtime/CI evidence; source-only implementation is not runtime proof.

## Required implementation workflow

### PLAN

Before every substantive change:

- verify current `main` SHA;
- read project `CURRENT.md`;
- inspect the exact current source and tests;
- define the smallest change that proves one invariant;
- state what evidence will be required.

### EXECUTION

- implement the smallest testable unit;
- preserve provenance and existing benchmark inputs;
- keep experimental harnesses isolated from production semantics unless an explicit integration boundary exists;
- commit directly to `main` under the sequential-main policy.

### VERIFICATION

Run the exact changed test path and capture stdout/stderr.

Required contract checks:

- terminal result exists;
- verification is bounded;
- terminalization is idempotent;
- no post-terminal verification occurs;
- reorganization happens only after verification;
- reorganization cannot re-enter the current task;
- human confirmation terminates without executing the current verification loop;
- oracle leakage remains absent.

### HANDOFF

Persist:

```text
WHAT I DID
WHERE
EVIDENCE
WHAT FAILED / REMAINS
EXACT NEXT ACTION
```

Update the project's `CURRENT.md` with the new commit SHA and evidence level.

## Current implementation boundary

The first executable implementation belongs in the isolated Planning Influence MVE and/or a reusable `cognitive_core` resolution contract. Do not claim that the entire LLM runtime now obeys this architecture until the controller/execution path explicitly consumes the terminal contract and a runtime test proves it.

## Decision rule for disagreement

An implementation may reject a proposed design when it can demonstrate a concrete failure mode such as:

- conflating cognitive representation with execution control;
- allowing unbounded recursive verification;
- allowing reorganization to reopen the current task implicitly;
- allowing unverified memory to hard-gate actions without an explicit execution boundary;
- changing experiment inputs after observing results.

When disagreeing, record the reason, implement the safer alternative, and preserve the original proposal in the audit trail.


## 🔗 Legături Sinaptice
- [[Governance_Repository_Spine_Specification|Governance]]
- [[00 Core Map]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
