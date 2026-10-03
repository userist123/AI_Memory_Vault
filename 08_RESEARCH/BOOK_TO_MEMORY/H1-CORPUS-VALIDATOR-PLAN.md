# H1 Corpus Validator Implementation Plan

**Goal:** Add a research-only validator that rejects malformed, ineligible, contaminated, or non-reproducible H1 retrieval cases before baseline measurement.

**Architecture:** The validator consumes a frozen corpus packet plus an H1 case JSON file and emits deterministic per-case findings. It does not modify production retrieval, storage, lifecycle, ranking, graph behavior, or active memory.

**Tech Stack:** Python 3, pytest, existing VaultIndex, SynapseStore, production candidate-generation primitives.

## Global Constraints
- Research-only: changes stay under 08_RESEARCH/BOOK_TO_MEMORY and research tests.
- No production retrieval changes.
- RAW and ARCHIVED targets are rejected for normal H1 retrieval cases.
- Gold IDs must resolve in the frozen corpus.
- Required facts must exist in gold evidence.
- Query classification, lexical/entity reachability, graph direction/path, and baseline rank must be recorded in the runtime evidence packet; the structural validator checks only the parts it can prove from the frozen packet.
- Association cases cannot already be trivial lexical/entity hits.
- Duplicate target leakage must be detectable; intentional final reuse is allowed only for conflict/distractor designs with an explicit `gold_reuse_reason`.
- Corpus commit/hash must be frozen before measurement.
- No accepted case proves a mechanism by itself.

## Task 1: Define the frozen H1 case contract
Files:
- Create: 08_RESEARCH/BOOK_TO_MEMORY/H1-CORPUS-CONTRACT.md
- Create: 08_RESEARCH/BOOK_TO_MEMORY/h1_cases.example.json
Interfaces:
- Consumes: H1 case JSON.
- Produces: documented fields for case identity, family, query, gold IDs, required facts, principal, intended failure boundary, and contamination controls.

## Task 2: Implement the deterministic validator
Files:
- Create: 08_RESEARCH/BOOK_TO_MEMORY/validate_h1_corpus.py
- Test: 20_TESTS/research/test_h1_corpus_validator.py

The validator remains dependency-light and does not import production retrieval components; this prevents the structural gate from changing behavior when production retrieval changes.
Interfaces:
- Consumes: case JSON and a frozen corpus packet compatible with the labeling-corpus shape.
- Produces: deterministic JSON findings with errors, warnings, and per-case structural diagnostics. Runtime reachability and baseline-rank evidence are external prerequisites, not validator outputs.

Focused tests:
- missing gold IDs
- absent required facts
- RAW/ARCHIVED targets
- invalid abstention/gold combinations
- duplicate case IDs
- graph paths with missing edges
- abstention/gold consistency
- duplicate target leakage

## Task 3: Integrate documentation and freeze semantics
Files:
- Modify: 08_RESEARCH/BOOK_TO_MEMORY/H1-RETRIEVAL-ASSOCIATIVE-RECALL-BENCHMARK.md
- Modify: 08_RESEARCH/BOOK_TO_MEMORY/H1-BENCHMARK-CORPUS-AUDIT.md
- Create: 08_RESEARCH/BOOK_TO_MEMORY/H1-RUNTIME-EVIDENCE-CONTRACT.md

Unresolved externally observable decisions:
- Multiple gold IDs are allowed only with an explicit reason.
- Duplicate target reuse is a warning during drafting and a hard error for the final frozen benchmark.
