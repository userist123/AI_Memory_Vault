# PERPLEXITY — CONTINUATION RESEARCH LANE V1

Repository context: `userist123/AI_Memory_Vault_CODEX_READY`

You are the external research lane in a parallel multi-agent round.
Read:

- `00_CORE/AI_Memory_Vault_Multi_Agent_Execution_Protocol_V1.md`
- `09_COORDINATION/PARALLEL_EXECUTION_V1.md`

Do not modify repository implementation. Do not claim repository behavior from papers or documentation. Treat current repository findings as hypotheses until independently grounded.

## Research objective

Move beyond generic recommendations and produce research that directly changes what CODEX can test or implement and what LUNA can challenge.

## P1 — Hybrid/semantic retrieval

Research evidence-backed designs for replacing or augmenting lexical/Jaccard retrieval with semantic or hybrid candidate generation.

Compare:

- dense embeddings;
- sparse retrieval;
- BM25 + dense hybrid;
- rerankers;
- late interaction;
- small local embedding models;
- offline deterministic test substitutes.

For each:

MECHANISM
EVIDENCE
COST
FAILURE MODE
SECURITY IMPLICATION
RELEVANCE TO VAULT
MINIMUM ACCEPTANCE TEST

Do not recommend a large external service when a local deterministic option can answer the research question.

## P2 — Memory poisoning and instruction/data separation

Research persistent-memory-specific attacks and defenses.

Focus on:

- indirect prompt injection;
- memory poisoning;
- instruction/data boundary enforcement;
- trust labels;
- provenance-based filtering;
- policy-aware retrieval;
- action gating;
- memory quarantine;
- adversarial evaluation.

Produce concrete attack classes and testable mitigations.

## P3 — Retrieval evaluation

Design a benchmark methodology resistant to lexical overfitting.

Include:

- paraphrases;
- synonyms;
- antonyms;
- lexical traps;
- hard negatives;
- domain-near negatives;
- temporal queries;
- superseded memories;
- abstention cases.

Research metric choices:

Precision@K
Recall@K
MRR
nDCG
selective risk
coverage
false-positive rate
abstention rate

Explicitly discuss benchmark leakage and test-set contamination.

## P4 — Calibration and selective retrieval

Research separate calibration targets for:

1. source confidence;
2. retrieval relevance confidence;
3. answer correctness confidence.

Evaluate:

ECE
Brier score
reliability diagrams
selective prediction
risk-coverage curves
threshold selection

Provide a design that does not collapse all epistemic quantities into one scalar.

## P5 — Associative memory

Research:

- A-MEM;
- HippoRAG;
- graph-based memory;
- spreading activation;
- multi-hop memory retrieval;
- consolidation;
- temporal graph memory.

For each identify:

WHAT IS ACTUALLY MEASURED
HOW IT IMPROVES RETRIEVAL
KNOWN FAILURE MODES
COMPUTATIONAL COST
TEST THAT WOULD DISTINGUISH IT FROM LEXICAL HEURISTICS

## P6 — Outcome-to-learning loop

Research safe architectures for:

execution trace
→ outcome
→ evidence
→ candidate memory update
→ future retrieval/ranking

Focus on:

- delayed feedback;
- negative outcomes;
- anti-reinforcement of mistakes;
- human gating;
- reward hacking;
- confirmation bias;
- stale knowledge;
- rollback/supersession.

Give concrete acceptance criteria for a genuinely closed loop.

## P7 — Temporal/provenance reasoning

Research temporal validity and evidence lineage compatible with:

RAW → REVIEW → VERIFIED → ACTIVE

and historical queries.

Distinguish:

historically valid
currently valid
future-valid
superseded
contradicted
unknown validity

## Deliverable

Produce:

`07_EVALUATION/perplexity/PERPLEXITY_MEMORY_ENGINE_CONTINUATION_V1.md`

Every material point must be tagged:

EVIDENCE
INFERENCE
DESIGN RECOMMENDATION

Every external factual claim gets a primary-source citation where possible.

## Handoff

For each proposed change provide a compact:

RESEARCH QUESTION
FINDING
SOURCE
CURRENT GAP
TESTABLE ACCEPTANCE CRITERION
RISK IF IMPLEMENTED
RISK IF NOT IMPLEMENTED

The deliverable is useful only when CODEX can turn it into an executable test or design decision.
