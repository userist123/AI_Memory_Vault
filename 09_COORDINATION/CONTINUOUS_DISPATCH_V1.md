# AI Memory Vault — Continuous Dispatch Controller V1

## Purpose

This controller makes agent work continuous. Completion of one agent task is a dispatch event, not a reason to stop the round.

## Dispatch rule

When an agent reports or commits a completed task:

1. inspect the actual branch, commit and changed files;
2. verify the evidence produced by that agent;
3. compare the result with the current `main` SHA and active round;
4. classify the result using the canonical evidence levels;
5. identify the highest-value next task for that same lane;
6. issue the next lane-specific plan immediately;
7. do not wait for unrelated lanes;
8. do not convert the task into acceptance merely because it completed.

## Completion is not acceptance

`COMPLETED` means the agent finished its assigned work.

`ACCEPTED` means the evidence survived independent verification and the integration gate.

An agent can therefore receive another task after producing a failed, partial, blocked or contradictory result.

## Priority calculation

The next task should be selected in this order:

1. security or lifecycle regression risk;
2. blocker removal for independent verification;
3. highest-impact unresolved P0/P1 gap;
4. reproducibility of a disputed claim;
5. implementation of a reproduced defect;
6. observability needed to measure the next change;
7. research needed to constrain the next design decision.

## Lane-specific dispatch

### CODEX

On completion, inspect the evidence and dispatch the next implementation/runtime task. Prefer:

`reproduce -> focused test -> safe fix -> regression -> CI`

Never dispatch benchmark rewriting, REVIEW promotion, security weakening or unsupported autonomous learning.

### ANTIGRAVITY

On completion, inspect the observability artifact and dispatch the next measurement/trace task that removes an uncertainty exposed by CODEX, LUNA or its own results.

Do not silently alter core semantics.

### PERPLEXITY

On completion, inspect source quality and acceptance criteria, then dispatch the next research question whose answer can constrain an implementation or falsification test.

Research never becomes repository proof by itself.

### LUNA / GPT-5.6

On completion, inspect all newly available evidence and dispatch the next independent attack/reproduction task. Prefer falsification of the newest important claim over repeating already-established facts.

Never repair the implementation being audited.

## Cross-agent trigger

When one lane completes a task, other lanes may continue their current tasks. Only the next task for the completed lane is dispatched immediately.

Example:

```text
CODEX completes C1
  -> verify C1
  -> dispatch C2 or C1-rework immediately

ANTIGRAVITY continues A2 independently
PERPLEXITY continues P3 independently
LUNA continues L2 independently
```

## Round transition

A round closes only at the barrier. Continuous dispatch inside a round does not bypass the barrier.

At barrier:

`lane evidence -> reconciliation -> integration decision -> exact-SHA CI -> next round manifest`

## Required dispatch record

Every generated next-task plan must identify:

- `ROUND_ID`
- `CURRENT_MAIN_SHA`
- `COMPLETED_TASK`
- `COMPLETION_EVIDENCE`
- `RESULT_CLASSIFICATION`
- `NEXT_TASK_ID`
- `OBJECTIVE`
- `SCOPE`
- `DO_NOT_CHANGE`
- `REQUIRED_EVIDENCE`
- `EXIT_CONDITION`

## Canonical trigger

```text
AGENT COMPLETES
      |
      v
VERIFY ACTUAL RESULT
      |
      v
CLASSIFY EVIDENCE
      |
      +---- FAILED/BLOCKED ----> targeted rework task
      |
      +---- PARTIAL ------------> next gap-reduction task
      |
      +---- SUCCESS ------------> next highest-value task
      |
      v
DISPATCH NEXT TASK FOR SAME AGENT
      |
      v
CONTINUE OTHER LANES IN PARALLEL
```
