# R001 C9 — Production graph integration

Evidence classifications: `CODE_VERIFIED`, `TEST_VERIFIED`; C8 rerun is `BROKEN` on this baseline and therefore has no after-metrics here.

## Baseline

```text
BASE_COMMIT=e43cc81e09789e284ef35a7e326297194f429a9e
```

## Reproduced defects

Before the change, `cognitive_core/ranked_search.py:build_multi_graph()` read `controller.storage.store` directly. `SQLiteStorageEngine` and `FileStorageEngine` expose `get`, `set`, `delete`, and `query`, but no `.store`; the production graph path therefore failed and the broad fallback returned base results without identifying the failure.

The same module rebuilt graph seed scores as `1 / (position + 1)`, discarding an available `relevance_score`/`score` from the base result.

Reproduction of the still-existing C8 runner blocker on this baseline:

```text
python 07_EVALUATION/retrieval_fusion/experiment_runner.py --help
Traceback (most recent call last):
  ...
ModuleNotFoundError: No module named 'evaluation'
```

This C9 branch does not redefine or claim C8 metrics.

## Repair

- Added `all_notes()` to in-memory, SQLite, and file storage engines.
- Graph construction now consumes that storage contract and excludes RAW notes consistently with normal read queries.
- `ranked_search(..., diagnostics=...)` reports `AVAILABLE`, `UNAVAILABLE`, or `FAILED` and an actionable reason while retaining deterministic base fallback.
- Base relevance scores are preserved as graph seed scores, with positional fallback only when no score exists.

No lifecycle, authorization, promotion, or canonical-memory behavior was changed.

## Targeted test evidence

```text
python -m pytest -q cognitive_core/tests/test_ranked_search.py memory_controller/tests/test_sqlite_storage.py memory_controller/tests/test_storage.py
..............................                                           [100%]
30 passed in 0.40s
```

The tests cover SQLite and File storage graph indexing, missing graph contract diagnostics, and preservation of `relevance_score`.

## Limitations

This proves the graph integration contract and failure observability at the reranking boundary. It does not prove end-to-end retrieval quality, causal memory benefit, or a real provider run. C8 held-out evaluation remains blocked by the import error shown above and must be rerun after its runner repair is available on the evaluated baseline.

```text
REMOTE_COMMIT=8b42ae4622396ea63bea292bbf9ac32446f9a91b
```


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
