# Book-to-Memory Research Track Contract

## Purpose

This registry prevents a literature finding from being mistaken for an engineering requirement or a production mechanism.

Every problem in `PROBLEM_MATRIX.md` must move through the same evidence chain:

`SOURCE -> ENGINEERING HYPOTHESIS -> EXPERIMENT -> RUNTIME EVIDENCE -> DECISION`

A problem is **not experimentally resolved** because a book map, chapter reference, or conceptual similarity exists.

## Track states

- `SOURCE_PENDING`: source relevance identified, required page-level evidence not frozen.
- `HYPOTHESIS_READY`: an engineering hypothesis is explicit and falsifiable.
- `EXPERIMENT_READY`: corpus, controls, metrics, and failure criteria are defined.
- `EVIDENCE_PENDING`: experiment definition exists but runtime evidence is absent.
- `EVIDENCE_AVAILABLE`: frozen evidence exists and is reproducible.
- `DECISION_PENDING`: evidence exists but engineering decision has not been recorded.
- `CLOSED_NO_CHANGE`: evidence does not justify a production change.
- `CLOSED_CHANGE_VALIDATED`: evidence supports a bounded mechanism and its implementation has independently passed validation.

No track may skip directly from `SOURCE_PENDING` or `HYPOTHESIS_READY` to a production change.

## Initial track map

| Track | Problem family | Current state | First falsifiable question | Primary boundary |
|---|---|---|---|---|
| H1 | Retrieval / indirect cues | EXPERIMENT_READY | Can weak lexical overlap cause eligible gold memories to disappear before final context? | candidate_generation -> context_pack |
| H2 | Consolidation | SOURCE_PENDING | Does consolidation improve durable recall without overwriting provenance or increasing false certainty? | write/consolidation lifecycle |
| H3 | Forgetting / decay | SOURCE_PENDING | Does controlled decay improve retrieval utility while preserving recoverability of rarely accessed but valid memories? | lifecycle/retrieval |
| H4 | Interference / conflict | HYPOTHESIS_READY | Can competing memories cause context-dependent retrieval errors that explicit conflict handling reduces? | ranking/context |
| H5 | Reconsolidation | SOURCE_PENDING | Can memory updates preserve provenance and prior state while incorporating verified new evidence? | write path |
| H6 | Working memory / context budget | HYPOTHESIS_READY | Which bounded context representation preserves task-critical facts under a fixed token budget? | context_pack |
| H7 | Episodic / semantic separation | SOURCE_PENDING | Does separating event-specific and generalized knowledge reduce cross-context retrieval errors? | memory type/ranking |
| H8 | Procedural memory | SOURCE_PENDING | Can reusable procedures be retrieved by task state rather than only lexical similarity? | candidate_generation |
| H9 | Salience / attention | SOURCE_PENDING | Does explicit salience improve useful retrieval enough to justify its false-priority cost? | ranking |
| H10 | Confidence / familiarity | HYPOTHESIS_READY | Can confidence/familiarity signals be calibrated without converting repetition into false evidence? | ranking/provenance |
| H11 | Meta-memory | SOURCE_PENDING | Can the system represent uncertainty about whether a memory exists or is retrievable without fabricating recall? | response contract |
| H12 | Agent routing | HYPOTHESIS_READY | Can memory retrieval route by task/principal context without weakening authorization or lifecycle gates? | request/routing |
| H13 | Feedback / stability | SOURCE_PENDING | Can feedback improve retrieval while preventing reinforcement of incorrect memories? | learning/governance |
| H14 | Provenance / epistemic state | EXPERIMENT_READY | Can every promoted memory retain sufficient evidence and conflict state for later audit? | write/governance |
| H15 | Token economy / disclosure | HYPOTHESIS_READY | Can disclosure preserve task correctness while reducing unnecessary memory/context tokens? | disclosure/context |

## Problem-to-track coverage

The problem matrix contains broader literature/problem labels than the initial experiment tracks. Multiple matrix rows may map to one track when they share the same falsifiable boundary; that grouping must be explicit.

| Matrix problem | Track | Grouping rationale |
|---|---|---|
| Retrieval / indirect cues | H1 | Same candidate-generation/retrieval question |
| Candidate generation / recall | H1 | Same candidate-generation boundary |
| Consolidation | H2 | Distinct write/lifecycle behavior |
| Forgetting | H3 | Distinct retention/decay behavior |
| Interference | H4 | Conflict between competing memories |
| Reconsolidation | H5 | Distinct update/rewrite semantics |
| Context | H1, H6 | H1 tests retrieval context; H6 tests context-budget representation |
| Working memory | H6 | Context-budget behavior |
| Episodic vs semantic | H7 | Memory-type separation |
| Procedural memory | H8 | Task-state/procedure retrieval |
| Salience / attention | H9 | Ranking priority signal |
| Confidence / familiarity | H10 | Evidence-confidence calibration |
| Conflict | H4 | Same competing-memory boundary |
| Decay | H3 | Same retention/decay boundary |
| Meta-memory | H11 | Uncertainty/availability response contract |
| Agent routing | H12 | Principal/task routing |
| Feedback / stability | H13 | Learning feedback and stability |
| Provenance / epistemic state | H14 | Evidence/write governance |
| Token economy / disclosure | H15 | Disclosure/context budget |

A track may cover multiple matrix problems only when the shared primary boundary and controls remain explicit. If a proposed experiment changes the boundary, it must become a separate track or a separately registered variant.

## Required evidence for every track

Before a track can enter `EVIDENCE_AVAILABLE`, it must identify:

1. exact source evidence and provenance;
2. one falsifiable engineering hypothesis;
3. frozen corpus/input identity;
4. controls and invariant security/lifecycle constraints;
5. primary and secondary metrics;
6. predefined failure/regression criteria;
7. raw runtime evidence sufficient to attribute failures;
8. reproducibility information;
9. a decision record stating whether production change is justified.

## Evaluation anti-gaming rules

A research result must not be declared an improvement from aggregate averages alone.

For paired baseline/variant experiments:

1. compare the same case, repetition and model whenever possible;
2. report absolute and relative deltas; relative deltas are invalid as the sole decision criterion;
3. report the per-case direction of change, not only the mean;
4. report uncertainty or a paired confidence interval when the sample size permits;
5. report all predefined primary metrics, including regressions;
6. never drop failed, timed-out, or degraded runs because they make the variant look worse;
7. a variant cannot qualify as a win by improving a secondary metric while regressing a protected primary metric;
8. zero-denominator metrics use absolute differences, never fabricated percentages;
9. evaluator/model disagreement must be reported rather than silently averaged away;
10. the decision record must state which predefined criterion triggered the decision.

### Minimum decision record

Every evidence-backed track must record:

- baseline and variant identifiers;
- exact sample/case counts;
- paired observations or explicit reason pairing was impossible;
- primary metric deltas;
- protected-regression results;
- uncertainty estimate where applicable;
- excluded/degraded run count and reasons;
- final decision criterion and outcome.

## Safety and governance invariants

Research tracks must not:

- promote RAW or REVIEW memories merely to make an experiment succeed;
- weaken authorization or principal boundaries;
- rewrite historical evidence to fit a hypothesis;
- treat model-generated interpretation as source evidence;
- copy full book text into retrieval memory when an evidence reference is sufficient;
- use historical benchmark numbers as a current baseline without a frozen current corpus;
- implement a mechanism before the experiment establishes a measurable need.

## Destructive-mechanism safety

Tracks that can delete, suppress, overwrite, demote, or materially rewrite stored memory must be evaluated in **shadow mode** first.

This applies at minimum to:
- H3 forgetting/decay;
- H5 reconsolidation;
- any future consolidation mechanism that mutates existing notes;
- any variant that changes lifecycle state or removes retrieval eligibility.

Shadow-mode requirements:
1. operate on a frozen copy or simulation layer, never the authoritative corpus;
2. preserve before/after identities and provenance;
3. record every proposed mutation;
4. compare retrieval/task outcomes against an unchanged control;
5. define rollback before execution;
6. do not promote the mechanism to production from shadow evidence alone.

A research experiment must never make the authoritative corpus worse merely to obtain a measurable effect.

## H1 special case

H1 already has dedicated artifacts:

- `H1-CORPUS-CONTRACT.md`
- `validate_h1_corpus.py`
- `H1-RUNTIME-EVIDENCE-CONTRACT.md`
- `H1-RETRIEVAL-ASSOCIATIVE-RECALL-BENCHMARK.md`

Its current blocker remains exact availability of the current Raw Inbox corpus. No H1 baseline may be admitted until that gate is cleared.

## Decision principle

The default outcome of a research track is **no production change** unless evidence demonstrates a bounded improvement under the declared constraints.

This registry is research governance, not a mechanism specification.
