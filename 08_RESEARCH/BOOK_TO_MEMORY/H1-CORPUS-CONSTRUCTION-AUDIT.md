# H1 — Corpus Construction and Retrieval Contract Audit

## Scope
Audit of the current production retrieval implementation to define what an H1 benchmark case is allowed to test.

## Finding C1 — Candidate generation sees the query, but the lexical scorer does not use title explicitly
`candidate_generation._note_text()` builds the lexical document from `content` plus `category`.
`VaultIndex.Note.text` includes title + body, but the production candidate generator receives storage dictionaries and its `_note_text()` does not explicitly include the note title.
Therefore an H1 case whose only meaningful cue exists in the note title must not be classified as a clean associative-retrieval case. It would confound title availability with association.

## Finding C2 — Entity extraction is a second candidate signal, not semantic association
Candidate generation uses BM25 plus entity overlap and deterministic reciprocal-rank fusion.
Entity extraction can recover structured names/tokens, but it is not evidence of multi-hop semantic association. H1 cases must record whether the gold note is reachable through BM25, entity overlap, or neither.

## Finding C3 — RelevanceScorer is a separate ranking stage
The production scorer computes token overlap from `content` and combines it 50/50 with the note confidence value.
Therefore a low-lexical-overlap note can still rank relatively high because of confidence metadata. Conversely, a lexically relevant low-confidence note can rank lower.
H1 must record the ranking-arm configuration and must not attribute such a miss solely to candidate generation.

## Finding C4 — Query classification can change the eligible population
QueryClassifier infers memory types and lifecycle filters from whole-word matches. Under the production hard classifier arm, inferred filters are passed into the storage gate.
Therefore H1 queries containing words such as `review`, `active`, `knowledge`, `procedure`, `decision`, etc. can change the candidate population.
Case construction must either intentionally test this behavior or record and exclude such cases from the pure associative subset.

## Finding C5 — RAW exclusion is unconditional
Storage query excludes RAW notes regardless of classifier arm.
An H1 gold target must therefore be eligible under the exact principal and lifecycle policy used by the benchmark. A RAW target cannot be used to demonstrate an associative retrieval miss.

## Finding C6 — Graph is directional and one-hop by default
Graph expansion traverses outgoing `neighbors(seed)` edges. Without spreading activation, the production path performs one hop.
Therefore an H1 multi-hop case must distinguish ordinary graph reachability from spreading-activation hop-2 behavior. The query-to-target path must be recorded explicitly.

## Finding C7 — Graph-expanded targets are filtered again
Graph targets are checked for lifecycle, type and hub constraints before entering the expansion result.
A declared edge alone does not prove that the target is retrievable through graph expansion.
H1 graph cases must verify the complete runtime eligibility path, not just the existence of a relation in Markdown.

## Finding C8 — Candidate limit and final context are different boundaries
Candidate generation defaults to 200, while final context is constrained by page size and context budgets.
Therefore every H1 case needs at least two diagnostic labels: candidate miss and context miss.

## Finding C9 — H1 corpus acceptance criteria
A candidate case is accepted only if:
1. target ID exists in the frozen corpus;
2. target is eligible for the selected principal;
3. target is not RAW/ARCHIVED unless the experiment explicitly tests a lifecycle policy;
4. required answer evidence exists in the target;
5. query classification is recorded;
6. lexical/entity reachability is measured before variant execution;
7. graph path, direction and relation are recorded for associative cases;
8. target is not already a trivial baseline hit when the case is intended to test association;
9. distractors and conflicts are explicitly identified;
10. case wording and gold labels are frozen before results are observed.

## Required per-case evidence
Each H1 case should store:
- case_id
- family
- query
- gold IDs
- required facts
- principal
- corpus commit/hash
- query classification
- lifecycle/type gate
- BM25/entity reachability
- baseline candidate rank or candidate absence
- graph path and graph eligibility where applicable
- final-context position or absence
- intended failure boundary
- contamination notes.

## Current conclusion
The production path is sufficiently instrumented to perform a diagnostic H1 experiment, but the H1 corpus must be constructed with these controls. No associative mechanism should be implemented before those controls are satisfied and the current production baseline is measured.