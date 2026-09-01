# P2 Temporal Memory Laboratory

This laboratory investigates temporal memory representation, validity interval filtering, supersession lineage traversal, and bi-temporal query resolution without altering production pipelines.

## Problem Context
In benchmarks P0 and P1:
- Retrieval Fusion (R4) and Section-Aware Extractive Packing (P2) achieved 76.7% fact recall and 71.1% 7B accuracy overall.
- However, `TEMPORAL` queries remained at only **`37.5%`** fact recall.
- Without explicit temporal awareness, standard lexical/semantic retrieval collapses past and present truths, failing to distinguish active policy from historical or superseded guidelines.

## Temporal Conditions Under Test
- **T0 (Control Baseline)**: Current R4 Candidate Generation + P2 Extractive Packing without temporal filtering.
- **T1 (Valid-Time Filtering)**: Filters and scores candidate notes using `valid_from` / `valid_until` validity intervals relative to query temporal context or current date.
- **T2 (Supersession Traversal)**: Traverses reciprocal `supersedes` / `superseded_by` and `replaces` / `replaced_by` graph edges to resolve the current canonical active node vs historical ancestor.
- **T3 (Valid-Time + Supersession)**: Combines interval validity filtering with recursive supersession lineage traversal.
- **T4 (Bi-Temporal Traversal)**: Distinguishes **Valid Time** (when the fact was true in domain reality) from **Transaction / Observation Time** (when the event was ingested into vault audit/outcome logs).

## Invariants & Rules
- **No Production Code Modifications**: Production `RecallEngine`, `MultiGraphMemory`, and `ContextPackBuilder` remain frozen.
- **Real Vault Data & Supersession Chains**: Operates on real vault notes and actual metadata.
- **Explicit Abstention**: Queries with missing timestamps or unknown validity intervals return explicit `UNKNOWN` / `UNRESOLVED` signals rather than hallucinating temporal ordering.
