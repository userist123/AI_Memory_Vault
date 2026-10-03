# H1 — Benchmark and Corpus Audit

## Scope
Audit of the frozen retrieval benchmarks against the current Memory Vault retrieval contract. This document does not change production retrieval.

## Finding B1 — v3 is a historical frozen benchmark, not a current-corpus benchmark
- v3 is frozen at vault commit b3ada1b54.
- Its preregistration states that its labeling corpus contained 379 returnable notes and 466 links at that snapshot.
- Its SHA-256 is eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa.
- Therefore v3 is suitable for historical reproduction, not as an unqualified current baseline.

## Finding B2 — The current benchmark contract is materially different
- The R009b v2 contract documents 842 production-index notes for its frozen corpus.
- v2 validates every non-abstain gold ID against the actual index before running.
- v2 explicitly excludes the invalid v1 methodology because v1 gold IDs did not resolve.
- This makes v2 a stronger production-path contract for current regression checking than v3, but it is still a frozen historical dataset and not automatically an H1 associative benchmark.

## Finding B3 — v3 and v2 test different questions
v3 contains 160 cases with direct, multi-hop, conceptual and abstain classes and was designed around graph-budget evaluation.
v2 contains exact identifier, paraphrase, synonym substitution, lexical trap, one-hop graph expansion and unanswerable classes.
H1 requires direct, paraphrase, indirect cue, entity/context cue, multi-hop associative, conflict and distractor families.
Therefore neither v3 nor v2 can be relabeled as the complete H1 corpus without changing the experimental question.

## Finding B4 — H1 is not yet instantiated
The H1 research document requires 30 target memories, 10 tasks per family, 3 repetitions per task and two independently evaluated models. The task corpus is explicitly marked not yet instantiated.
Consequently there is currently no measured H1 baseline and no evidence yet that the production path fails under indirect/associative cues.

## Finding B5 — Graph cases have a known contamination/reachability limitation
The v2 contract documents that the runtime graph traverses one outgoing hop, not arbitrary multi-hop paths.
It also documents that many graph candidates are already reachable through ordinary BM25/entity retrieval. Therefore a graph-labelled case is not necessarily a pure test of graph contribution.
H1 multi-hop cases must be constructed so that the gold memory is not already reachable through the baseline candidate path, otherwise the experiment cannot attribute recovery to the associative mechanism.

## Finding B6 — Abstention is currently not a recall metric
The v2 runner explicitly treats abstain cases as UNMEASURABLE because the production path has no validated abstention signal.
H1 should preserve this discipline: unanswerable/distractor cases need their own false-retrieval and noise metrics rather than being converted into fabricated recall scores.

## Finding B7 — Required H1 corpus integrity checks
Before execution, every H1 case must be checked for:
1. gold ID existence in the exact frozen corpus;
2. gold note lifecycle/security eligibility;
3. no accidental RAW/ARCHIVED inclusion;
4. required facts present in the gold evidence;
5. relation edges actually present for associative cases;
6. baseline lexical/entity reachability measured before variant execution;
7. no duplicate target leakage across task families that would inflate apparent generalization;
8. fixed task wording and evaluator rubric before measurements;
9. corpus commit and benchmark hash recorded with results.

## Finding B8 — Baseline and variant must use the same production contract
The experiment must hold principal, lifecycle/security gates, storage corpus, page size, context budget, disclosure level and ranking configuration constant.
The only changed variable should be the candidate-generation/retrieval mechanism being tested.

## Decision
- Historical v3 results: retain as historical evidence only.
- v2: retain as regression/production-path evidence, with its documented graph limitations.
- H1: do not claim baseline failure yet.
- Next research action: instantiate and integrity-audit the H1 corpus, then execute the current production path before implementing any associative mechanism.
- No ACTIVE promotion and no production retrieval modification is justified by the current evidence.

## Validator status
A research-only corpus contract and deterministic validator now exist. Structural invalidity is blocking; lexical/entity reachability remains an explicit runtime measurement prerequisite because the labeling corpus uses shortened excerpts and must not be mistaken for the production retrieval corpus.
