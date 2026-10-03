# H1 — Retrieval Under Indirect / Associative Cues

## Question
Can the current Memory Vault retrieval path miss a relevant memory when the query has weak lexical overlap with the memory, while the memory is recoverable through context, entities, relationships, or task state?

This is a falsifiable engineering question. It is not an assumption that the current implementation is defective.

## Baseline to freeze
Use the current production retrieval path on the research branch before changing implementation.

Record:
- query classification
- hard storage/lifecycle gate result
- candidate-generation output
- ranking output
- classifier/filter output, if present
- final disclosed set
- latency
- token count
- errors
- deterministic/repeatability information

No production retrieval code is changed for the baseline.

## Task construction
Build a fixed benchmark before implementation changes.

### Task families
1. Direct lexical — query and target share explicit key terms.
2. Paraphrase — target meaning is preserved with different wording.
3. Indirect cue — query points to a concept through a consequence, use case, or associated fact.
4. Entity/context cue — target is recoverable primarily from entities, project, environment, or task state.
5. Multi-hop associative — query -> related concept -> target memory.
6. Conflict — multiple memories are plausible; the correct target depends on context.
7. Distractor — high lexical-overlap distractor competes with a low-overlap relevant memory.

Minimum initial corpus:
- 30 target memories
- 10 tasks per family
- 3 repetitions per task
- 2 independently evaluated models

## Metrics
Primary:
- Recall@1, Recall@5, Recall@10
- MRR
- nDCG@K

Secondary:
- false retrieval rate
- omission rate
- context correctness
- conflict exposure correctness
- latency
- tokens consumed
- output consistency across repetitions

For any success metric S, Delta = (S_variant - S_baseline) / S_baseline.
When the denominator is zero, report absolute difference instead of inventing a percentage.

## Controls
The baseline and variant must use:
- identical memory corpus
- identical task set
- identical evaluator rubric
- identical model settings where possible
- identical token budget
- identical lifecycle/security gates
- identical output budget

Only the candidate-generation/retrieval mechanism under test may differ.

## Variant boundary
The first experimental variant may add associative/context-aware candidate generation.

It must not:
- change authorization
- change lifecycle policy
- promote REVIEW/RAW notes
- rewrite stored memories
- silently alter ranking weights outside the experiment
- introduce a biological mechanism merely because a source describes one

## Decision rule
A retrieval change is justified only if the benchmark shows a repeatable improvement on the predefined target family without unacceptable regression in false retrieval, conflict handling, latency, token cost, consistency, or security/lifecycle behavior.

If the baseline performs adequately, do not implement the variant.

## Research chain
BIOLOGICAL/COGNITIVE FACT -> ENGINEERING HYPOTHESIS -> EXPERIMENT -> VAULT MECHANISM

The benchmark is the required bridge between literature and implementation.

## Status
- benchmark definition: prepared
- task corpus: not yet instantiated
- baseline run: not yet executed
- variant: not implemented
- ACTIVE promotion: prohibited