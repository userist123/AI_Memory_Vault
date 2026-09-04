# AI MEMORY VAULT — NEXT ROUND WORK MAP V1

## Baseline

Start each new round from the actual current `main` SHA.
Do not reuse an older SHA as the round baseline.

## Parallel lanes

### CODEX — BUILD / EXECUTE

Priority order:

1. prove current retrieval candidate generation and ranking path;
2. add held-out lexical-vs-semantic tests;
3. test REVIEW-memory injection at the agent/tool boundary;
4. validate graph/activation A-B effect;
5. validate temporal/supersession behavior;
6. trace outcome telemetry consumption;
7. implement only proven gaps;
8. run regression + CI.

### ANTIGRAVITY — OBSERVE / EXPLAIN

Priority order:

1. retrieval trace;
2. score decomposition;
3. graph/activation comparison view;
4. lifecycle visualization;
5. memory-use vs outcome observability;
6. architecture gap register.

Do not repair core logic in this lane unless explicitly reassigned.

### PERPLEXITY — RESEARCH / DESIGN EVIDENCE

Priority order:

1. memory poisoning and instruction/data separation;
2. hybrid semantic retrieval;
3. robust retrieval benchmark design;
4. calibration/selective prediction;
5. associative memory mechanisms;
6. outcome-driven learning with human gate;
7. temporal/provenance reasoning.

Every recommendation must have a testable acceptance criterion.

### LUNA — VERIFY / ATTACK

Priority order:

1. independently reproduce changed behavior;
2. maintain a held-out retrieval suite;
3. attack the memory-content security boundary;
4. challenge graph/activation claims with controlled comparisons;
5. audit score semantics and calibration;
6. audit causal memory claims;
7. audit learning-loop closure;
8. reconcile all lane outputs.

## Lane isolation

Each lane owns one evidence namespace:

- CODEX: `07_EVALUATION/codex/`
- ANTIGRAVITY: `07_EVALUATION/antigravity/`
- PERPLEXITY: `07_EVALUATION/perplexity/`
- LUNA: `07_EVALUATION/luna/`

No lane edits another lane's artifacts.

## Evidence packet

Every lane handoff must contain:

```text
ROUND_ID
BASELINE_SHA
AGENT
TASK_IDS
STARTED_AT
COMPLETED_AT
CHANGED_FILES
COMMANDS_OR_METHODS
RAW_EVIDENCE_REFERENCES
EVIDENCE_LEVELS
FAILURES
BLOCKERS
OPEN_QUESTIONS
NEXT_RECOMMENDATION
COMMIT_SHA (if any)
CI_RESULT (if applicable)
```

## Barrier protocol

After parallel work, create:

`09_COORDINATION/rounds/<ROUND_ID>/BARRIER.md`

A barrier does not merge code automatically.
It records whether each lane is:

READY
BLOCKED
FAILED
PARTIAL
NO_CHANGE

Then compare evidence.

## Integration rule

Only the designated implementation lane integrates approved code changes.

LUNA verifies after integration.

ANTIGRAVITY validates observability after integration.

PERPLEXITY updates research only when new evidence changes the design space.

## What must never happen

- Codex waits for all other agents before starting unrelated work.
- Antigravity silently changes retrieval semantics.
- Perplexity claims repository behavior.
- Luna repairs the code it is auditing.
- A prior agent's report is treated as independent proof.
- A test is weakened to make an implementation pass.
- REVIEW is promoted to ACTIVE to increase benchmark scores.
- A successful CI run on one SHA is reported as proof for another SHA.

## Target of the next complete cycle

The next completed cycle should be able to answer, with evidence:

1. Is candidate generation lexical, semantic, or hybrid?
2. Does graph/activation materially improve retrieval?
3. Can retrieved REVIEW content become instruction authority?
4. Are ranking/confidence scores calibrated enough to support abstention?
5. Does temporal reasoning distinguish historical from current knowledge?
6. Does outcome telemetry actually close into future retrieval?
7. Does memory cause better outcomes, or merely correlate with them?

No single agent is expected to answer every question.
