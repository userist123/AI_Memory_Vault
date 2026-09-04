# CODEX — CONTINUATION LANE V1

Repository: `userist123/AI_Memory_Vault_CODEX_READY`

You are the implementation/runtime lane in a parallel multi-agent round.
Read first:

- `00_CORE/AI_Memory_Vault_Multi_Agent_Execution_Protocol_V1.md`
- `09_COORDINATION/PARALLEL_EXECUTION_V1.md`

## Mission

Continue from the current `main` state and turn demonstrated gaps into reproducible implementation/test evidence. Work independently while ANTIGRAVITY, PERPLEXITY and LUNA work in their own lanes.

## Immediate technical program

### C1 — Retrieval architecture correction

Establish the real runtime candidate-generation path. The current deterministic provider is Jaccard/token overlap, and the normal retrieval engine obtains storage candidates before relevance scoring. Do not call this semantic retrieval merely because the provider is named `SemanticProvider`.

Tasks:

1. run the current retrieval tests from a clean checkout;
2. create a held-out lexical-vs-semantic diagnostic suite;
3. separate candidate generation, similarity, scoring and final ordering in the evidence;
4. identify the smallest safe architecture for optional real embeddings/hybrid retrieval;
5. do not replace the deterministic CI baseline unless a controlled test proves the replacement is justified;
6. preserve deterministic/fail-closed behavior when an embedding provider is unavailable.

### C2 — Candidate recovery and ranking

Investigate whether graph/activation signals operate too late.

Current architecture evidence suggests the graph re-ranker is applied after `MemoryController.search()`, so it cannot recover omitted candidates.

Run a controlled experiment with identical candidate sets:

- base controller ranking;
- graph/activation ranking;
- graph disabled;
- graph enabled.

Measure whether order changes and whether the changed order improves relevance.

Do not redesign candidate generation until the experiment proves the gap.

### C3 — Memory-content injection boundary

Implement an end-to-end adversarial test boundary covering REVIEW content reaching an agent/tool executor.

Test separately:

- DATA retrieval;
- AUTHORITY escalation;
- ACTION authorization.

Payload classes:

direct instruction,
fake system/developer,
social engineering,
multistep attack,
tool abuse,
credential request,
privilege escalation,
persistence,
cross-memory trust injection.

Preserve `REVIEW != ACTIVE` and `_cognitive_unverified = true`.

### C4 — Calibration semantics

Trace every score into its source and consumer.

Separate conceptually and, where justified, operationally:

- source confidence;
- retrieval relevance score;
- answer correctness;
- authority;
- utility.

Add deterministic evaluation for score separation and threshold/abstention behavior before redesigning the scoring model.

### C5 — Temporal/supersession verification

Create controlled cases for:

- current memory;
- expired memory;
- future-valid memory;
- superseded memory;
- historical query;
- contradictory memories.

Prove historical retrieval does not silently become current advice.

### C6 — Learning loop

Trace outcome telemetry beyond the writer. Determine whether any consumer reads `reports/outcomes/council_outcomes.jsonl` or equivalent evidence and feeds it into ranking, retrieval or promotion.

If the loop is not closed, implement only the smallest evidence-preserving bridge that conforms to human gating. No automatic promotion from raw outcome telemetry.

## Implementation discipline

Never modify benchmark definitions to improve results.
Never weaken authorization/lifecycle/provenance controls.
Never promote REVIEW knowledge to ACTIVE to make tests pass.
Never fabricate local or CI results.

Before every implementation change:

- record baseline SHA;
- inspect impacted code;
- add/adjust a focused test;
- run it;
- capture output.

After changes:

- run targeted tests;
- run full regression when practical;
- commit;
- verify remote commit;
- verify CI.

## Ownership

Write implementation code only in your branch.
Write CODEX evidence under:

`07_EVALUATION/codex/`

Do not edit:

`07_EVALUATION/antigravity/`
`07_EVALUATION/perplexity/`
`07_EVALUATION/luna/`

## Handoff

Finish each round with:

- baseline SHA;
- changed files;
- exact commands executed;
- actual outputs;
- failures;
- evidence level per finding;
- commit SHA;
- CI result;
- unresolved gaps;
- next safe implementation candidate.
