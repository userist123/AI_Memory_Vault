---
type: coordination
category: multi-agent-execution
status: active
version: 1.0.0
id: "parallel-execution-v1"
baseline_policy: "00_CORE/AI_Memory_Vault_Multi_Agent_Execution_Protocol_V1.md"
---

# AI Memory Vault — Parallel Execution Controller V1

## Purpose

This controller defines how CODEX, ANTIGRAVITY, PERPLEXITY and LUNA work concurrently without competing for the same implementation surface.

The unit of coordination is a **round**. A round has one baseline `main` SHA, four isolated lanes, evidence artifacts, and a synchronization barrier.

## Operating model

```text
                         MAIN @ BASELINE SHA
                                  |
              +-------------------+-------------------+
              |                   |                   |
             CODEX           ANTIGRAVITY          PERPLEXITY
              |                   |                   |
       implementation        observability           research
       + runtime tests        + architecture        + evidence
              |                   |                   |
              +-------------------+-------------------+
                                  |
                                LUNA
                         independent audit
                                  |
                           SYNC BARRIER
                                  |
                         CONFLICT / GAP MATRIX
                                  |
                         approved next round
```

LUNA may work in parallel from the same baseline. Synchronization is for **evidence and decisions**, not for starting unrelated work.

## Baseline rule

Every agent records:

- `round_id`;
- baseline `main` SHA;
- branch name;
- start timestamp;
- task IDs;
- files owned;
- evidence produced.

An agent must not silently switch baselines during a round.

## Lane ownership

### CODEX

Can modify implementation, executable tests, CI checks and CODEX-owned evidence.

Primary areas:

- `cognitive_core/`
- `memory_controller/`
- `tests/`
- `scripts/`
- `.github/workflows/` when directly required
- `07_EVALUATION/codex/`

### ANTIGRAVITY

Can modify developer observability, visualization and architecture/audit artifacts.

Primary area:

- `07_EVALUATION/antigravity/`

Developer tooling may be added only when it is isolated and non-authoritative.

ANTIGRAVITY does not change security boundaries, lifecycle enforcement or core retrieval decisions without an explicitly assigned implementation task.

### PERPLEXITY

No repository implementation changes.

Primary output:

- research/evidence package;
- external references;
- design alternatives;
- acceptance criteria.

Repository behavior must never be reported as verified merely from research.

### LUNA

Primary area:

- `07_EVALUATION/luna/`

LUNA independently tests, challenges and reconciles evidence. LUNA does not modify another lane's implementation during the same round.

## Isolation rule

No agent may modify another agent's lane.

No agent may rewrite another agent's evidence artifact.

No agent may convert another agent's `CLAIMED_ONLY` result into `VERIFIED` without independently reproducing the evidence.

## Parallel-by-default rule

Unrelated tasks must execute concurrently.

A dependency is a reason to synchronize **the specific artifact required**, not to serialize the entire round.

Example:

```text
PERPLEXITY research on poisoning  --------+
ANTIGRAVITY observability ---------------+--> may proceed immediately
LUNA independent audit -------------------+
CODEX ground-truth/runtime --------------------> implementation track
```

## Synchronization barrier

A barrier is reached when each lane has produced one of:

- completed artifact;
- explicit blocker;
- reproducible failure;
- unresolved question.

The barrier output is:

`09_COORDINATION/rounds/<ROUND_ID>/BARRIER.md`

It contains only:

- baseline SHA;
- lane status;
- evidence links;
- conflicts;
- blockers;
- decisions required.

## Evidence classes

Use only:

`DOCUMENT_VERIFIED`
`CODE_VERIFIED`
`TEST_VERIFIED`
`RUNTIME_VERIFIED`
`CI_VERIFIED`
`CLAIMED_ONLY`
`UNVERIFIED`

## Merge discipline

Do not merge concurrent implementation branches by blindly choosing the latest commit.

Before integration:

1. compare changed files;
2. detect ownership collisions;
3. inspect tests affected by both lanes;
4. preserve security invariants;
5. run the required regression suite;
6. verify resulting `main` SHA;
7. verify CI.

## Round state machine

```text
OPEN
  -> PARALLEL_WORK
  -> EVIDENCE_READY
  -> BARRIER
  -> RECONCILIATION
  -> APPROVED / REJECTED / REWORK
  -> INTEGRATION
  -> CI_VERIFIED
  -> CLOSED
```

A rejected lane does not invalidate unrelated successful lanes.

## Failure handling

Failures are first-class outputs.

Never hide a failure by:

- changing the benchmark;
- weakening a security rule;
- changing expected values without evidence;
- marking a test skipped;
- deleting contradictory evidence.

## Current round target

The next technical focus is the gap between the documented cognitive architecture and demonstrated runtime behavior, especially:

1. lexical vs semantic retrieval;
2. retrieval candidate generation vs graph re-ranking;
3. memory-content injection at the retrieval-to-agent boundary;
4. score/calibration semantics;
5. temporal validity and supersession;
6. outcome telemetry vs actual learning-loop closure;
7. causal evidence for memory usage.

The lanes should attack these independently rather than reproducing the same audit.
