# H1 Corpus Contract

## Purpose

This contract defines the immutable input format for the associative-retrieval benchmark. A case is evidence for an experiment only after the validator reports no errors.

## Case schema

Required fields:

- id: unique case identifier
- family: one of direct_lexical, paraphrase, indirect_cue, entity_context, multi_hop_associative, conflict, distractor
- query: exact frozen query presented to retrieval
- gold_relevant_notes: one or more note IDs
- required_facts: facts that must be present in the gold evidence
- abstain: boolean
- principal: benchmark principal, normally HUMAN for corpus diagnostics
- intended_boundary: candidate_generation, ranking, graph, context_pack, or end_to_end
- corpus_commit: frozen corpus revision
- corpus_hash: hash of the frozen corpus packet

Optional fields:

- rationale: why the case represents the selected family
- multi_gold_reason: mandatory when more than one gold ID is present
- graph_path: ordered source/target/relation objects for associative cases
- conflict_group: identifier for an explicitly conflicting evidence set
- distractor_ids: known plausible non-gold notes
- contamination_notes: documented unavoidable contamination
- gold_reuse_reason: mandatory when a final conflict/distractor case intentionally reuses a gold target
- expected_baseline: descriptive expectation, never a measured result

## Acceptance rules

1. Case IDs are unique.
2. Non-abstain cases have at least one gold ID; abstain cases have none.
3. Every gold ID exists in the frozen corpus.
4. Every required fact occurs in the gold evidence.
5. Gold notes are not RAW or ARCHIVED for the normal H1 retrieval contract.
6. More than one gold ID requires multi_gold_reason.
7. Multi-hop cases require graph_path and every declared edge must exist with the same direction and relation.
8. A graph path may not silently reverse an edge.
9. The corpus commit and hash must be identical across the frozen case set.
10. A final benchmark cannot contain duplicate gold-target reuse unless every case reusing that target is explicitly a conflict/distractor design and each case documents `gold_reuse_reason`.
11. Lexical/entity reachability is not assumed by this validator. It must be measured separately against the exact production path before a case is labelled associative.
12. The validator never changes or repairs a case.

## Failure semantics

- Any acceptance-rule violation is an error and blocks baseline measurement.
- Diagnostics are deterministic and sorted by case ID.
- Warnings are informational during drafting; they never turn an invalid case into a valid case.
