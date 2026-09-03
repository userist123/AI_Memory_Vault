# Book knowledge consolidation V1

- Input: six provisional book derivates from batch `BOOK_INGEST_2026-09-04_01`.
- Initial candidates: **54**.
- Clusters: **12**.
- Candidate mapping: all 54 candidates map to exactly one cluster and at least one atom.
- Atoms: **31** (10 cross-source synthesis, 21 single-source).
- Status policy: every atom is `READY_FOR_HUMAN_REVIEW`; `verification_required` is true; `promotion_allowed` is false.
- Composite prioritisation: `utility_score * (1 - misleading_risk)`; utility, reliability, reuse probability, stability, misleading risk, and retention cost remain separate fields. This is not an automatic promotion criterion.

## Relation counts

The matrix contains all 1,431 candidate pairs. Counts below include only explicitly meaningful classifications; `UNRELATED` pairs are retained in the matrix but omitted from this summary.

- DUPLICATE: **0**
- NEAR_DUPLICATE: **3**
- COMPLEMENTARY: **6**
- CONFLICT: **0**
- NEEDS evidence verification: **31**
- READY_FOR_HUMAN_REVIEW: **31**

## Concrete examples

- `book-agents-2026-c003` ↔ `book-llm-apps-c007`: `NEAR_DUPLICATE`; both describe RAG/external-context retrieval, so they are represented in one synthesis family rather than two fundamental memories.
- `book-ddia-2017-c006` ↔ `book-ddia-2017-c009`: `COMPLEMENTARY`; replication/conflict resolution and distributed timing/partial failure are different mechanisms.
- `book-agents-2026-c003` ↔ `book-agents-2026-c005`: `UNRELATED`; retrieval grounding and task-specific evaluation are retained separately.

## Ten highest-value atoms

See `promotion_candidates.md`. They are prioritisation output only. Each atom links to `supporting_candidates`, then to book, source SHA-256, and locator through `sources`; `evidence_bundles.jsonl` repeats this lossless mapping.

## Deliberately not consolidated

Search algorithms, constraint satisfaction, low-level transformer attention, convolutional/recurrent/LSTM mechanisms, encoding/partitioning details, and specific social/ethical claims remain single-source atoms when no sufficiently supported cross-book abstraction was justified. We also emit no `SUPERSEDES` relation: the corpus provides no evidence that one candidate replaces another. Raw PDFs and canonical memory were not modified.
