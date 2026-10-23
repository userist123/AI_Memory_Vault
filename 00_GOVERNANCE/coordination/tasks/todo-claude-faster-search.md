# todo-claude-faster-search

- **Task:** the three slowest suite tests (`20_TESTS/test_retrieval_loss_funnel.py`, 514 s of a 924 s CI suite) — find why and fix without weakening them.
- **Branch:** `claude/faster-loss-funnel-tests`
- **Cause (profiled 2026-10-23):** not the tests. Every `controller.search` re-folded and re-tokenized the full text of every gated note; `tokenize`/`_fold` were ~90% of a 3.3 s search. The tests just run ~650 searches.
- **Done:** `retrieval/context/candidate_generation.py` memoizes `tokenize`/`entities` of note texts by `(function, text)`, bounded LRU, immutable values, exceptions not cached; module-level tokenizer patches (tokenizer experiment) are honoured. Search 3.26 s → 0.43 s locally. Tests unchanged. New `20_TESTS/regression/test_candidate_generation_doc_cache.py` (10 tests: identical results cached/uncached, edited note retokenized, patched tokenizer honoured, immutability, exceptions propagate).
- **Next:** read the suite time and `--durations` in this PR's CI; remaining search time is spread (BM25 Counter build, storage stat/store), no single hotspot.
- **Blockers:** none.
- **Key files:** `03_IMPLEMENTATION/packages/retrieval/context/candidate_generation.py`, `20_TESTS/regression/test_candidate_generation_doc_cache.py`.
