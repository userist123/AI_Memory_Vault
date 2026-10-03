# H1 Corpus Generator Audit

Date: 2026-10-03
Branch: research/book-to-memory

## Scope

Audited:
- 30_SCRIPTS/evaluation/build_labeling_corpus.py
- 08_RESEARCH/BOOK_TO_MEMORY/H1-CORPUS-CONTRACT.md
- 08_RESEARCH/BOOK_TO_MEMORY/validate_h1_corpus.py
- production Principal/authorizer definitions
- SynapseStore graph construction contract

## Findings

### G1 — Generator is a labeling packet, not yet an H1 benchmark packet

The existing generator emits title, type, lifecycle, path and a 700-character excerpt. It is intentionally designed for independent labeling and is not sufficient as the final H1 evidence packet.

Disposition: KEEP AS LABELING TOOL. Do not overload it with benchmark semantics.

### G2 — Exact evidence is truncated

Required-fact validation currently operates on the fields present in the H1 packet. The labeling generator exposes only a shortened excerpt.

Disposition: FINAL H1 corpus needs deterministic evidence anchors or a separately generated evidence packet. A case must not be accepted merely because a 700-character excerpt happened to contain a phrase.

### G3 — Graph construction is broader than declared relations

SynapseStore.from_index() can include declared relations, inferred weak mirrors and wikilinks, subject to hub filtering. Therefore a graph_path must identify the exact relation and direction present in the runtime graph.

Disposition: validator rule 7/8 is correct. No inferred reverse edge may be invented by the labeler.

### G4 — Principal eligibility is explicit in runtime

Production search permits HUMAN, AI_AGENT and ADMIN for SEARCH. The benchmark contract currently defaults to HUMAN.

Disposition: validate principal enum explicitly; do not infer it from free text.

### G5 — Lifecycle policy is benchmark-specific

The benchmark excludes RAW and ARCHIVED, while runtime has additional lifecycle behavior and principal-dependent floors.

Disposition: retain the benchmark rule but document it as an experimental population constraint, not as a claim about all production lifecycle semantics.

### G6 — Corpus identity must be frozen before labels

The generator accepts an externally supplied COMMIT. That is useful, but the final corpus must also carry a canonical content hash calculated from the exact packet.

Disposition: final H1 packet must be immutable after labeling. Re-running against another snapshot requires a new corpus revision.

### G7 — Lexical/entity reachability remains a runtime measurement

The structural validator must not guess whether a case is associative. This depends on exact candidate generation and query classification.

Disposition: correct separation. Runtime baseline measurement is mandatory before accepting a case as an associative failure.

## Required final H1 packet

Before baseline:
1. exact corpus commit;
2. canonical corpus hash;
3. deterministic note evidence;
4. exact directed graph edges;
5. principal;
6. query classification;
7. lifecycle/type gate;
8. BM25 reachability;
9. entity reachability;
10. baseline candidate rank;
11. final context position;
12. contamination notes;
13. frozen query and gold.

### G8 — Generator ordering was not previously explicit

The labeling generator iterated the index notes and graph store in their native iteration order. Because the corpus hash is computed over arrays whose order is semantically part of the JSON payload, equivalent corpus content could otherwise produce different hashes across environments or implementation changes.

Disposition: FIXED. Notes are sorted by `(id, path)` and links by `(source, target, relation, origin)` before serialization. This makes the generated packet byte-order deterministic for the same loaded corpus content.

The canonical validator hash still sorts JSON object keys and intentionally retains array order, so generator determinism remains part of the reproducibility contract.

## Decision

No associative retrieval implementation is justified by the current corpus state alone.

Next gate is construction of a frozen current corpus packet, followed by runtime reachability audit. Only cases surviving that gate may enter H1 baseline measurement.


### G9 — Selection bias must be blocked at the runtime-analysis layer

The corpus contract correctly requires runtime reachability, but reachability alone does not prevent post-hoc deletion of inconvenient cases. The benchmark now treats reachability as a reported stratum and requires the complete frozen parent denominator to remain visible. Any associative subgroup must be declared before baseline/variant results are inspected.

Disposition: FIXED in the H1 runtime evidence and benchmark contracts.