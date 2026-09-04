# Retrieval Findings V1

Date: 2026-09-04
Evaluator: GPT-5.6 Luna
Baseline: remote `main`

## Purpose

Independent static/model-evaluated retrieval audit of the current memory artifacts and retrieval benchmark contract. This is not a runtime provider benchmark.

## Challenge set

The challenge set contains 20 positive queries mapped to the 10 synthesis atoms and 10 negative queries with no expected memory hit. The cases are stored in `retrieval_challenge_cases.jsonl`.

## Observed architectural condition

The new book-derived synthesis atoms are stored under `06_INBOX/DERIVED/BOOKS/2026-09-04/consolidated/` and remain `READY_FOR_HUMAN_REVIEW`. The Memory V6 architecture requires human approval before canonical promotion; therefore making them retrievable must not silently convert them to `ACTIVE`.

The repository's retrieval-quality metric implementation provides Precision@K, Recall@K and MRR, but metrics alone cannot prove that the production retrieval path actually indexes the book-derived review material.

## Result

**RETRIEVAL BLOCKER IDENTIFIED: review-gated book knowledge is not yet demonstrated to be addressable by the production retrieval path.**

Therefore the honest empirical result for this audit is:

- Runtime retrieval execution from this evaluator: NOT EXECUTED.
- Precision@K: NOT CLAIMED.
- Recall@K: NOT CLAIMED.
- MRR: NOT CLAIMED.
- Book-atom production retrieval: NOT PROVEN.
- Canonical promotion: NOT performed.

Earlier static inspection identified the relevant design tension: the cognitive read path can include `ACTIVE + REVIEW` while tagging review material as unverified, but the general production storage/retrieval path must explicitly expose that same review namespace if the new book knowledge is expected to participate in retrieval.

## Required implementation gate

Before claiming retrieval success, the implementation must prove all of the following:

1. `READY_FOR_HUMAN_REVIEW` book atoms are discoverable through an explicit review-safe retrieval path.
2. Returned review atoms retain an unverified marker.
3. Review retrieval cannot mutate or promote canonical memory.
4. Irrelevant queries can abstain instead of returning arbitrary top-k memories.
5. Positive challenge cases are scored with Precision@K, Recall@K and MRR.
6. Negative cases measure false-positive retrieval / abstention quality.
7. A regression test prevents accidental removal of review-safe retrieval.
8. CI executes the deterministic retrieval benchmark.

## Security boundary

Do **not** solve this by moving all book atoms into `ACTIVE`. The correct fix is retrieval visibility with explicit epistemic labeling, followed by the existing human-gated promotion process.

## Next implementation target

Implement a dedicated `REVIEW` retrieval namespace or equivalent read-only index over human-gated knowledge, integrate it with the cognitive retrieval path, add abstention/threshold behavior, and then execute the 30-case challenge set in a real local test environment.
