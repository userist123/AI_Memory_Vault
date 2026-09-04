# R001 C8 — Retrieval fusion runner forensics

Evidence classifications: `CODE_VERIFIED`, `TEST_VERIFIED`, and
`BROKEN` where noted.

## Baseline

```text
BASE_COMMIT=e43cc81e09789e284ef35a7e326297194f429a9
```

## Reproduced blocker

Running the runner from the repository root failed before evaluation because
its imports referenced a nonexistent top-level `evaluation` package:

```text
python 07_EVALUATION/retrieval_fusion/experiment_runner.py --help
ModuleNotFoundError: No module named 'evaluation'
```

The script has no `--help` parser and this invocation starts its experiment;
the attempt was stopped after confirming imports had progressed. No result
was counted from that interrupted run.

## Repair

`experiment_runner.py` now adds `07_EVALUATION` to `sys.path` and imports its
local modules. `retrieval_diagnostic_runner.py` likewise imports the local
`full_context_baseline` module. No corpus, scoring method, or experiment
configuration was changed.

## Test evidence

```text
python -m pytest -q 07_EVALUATION/tests/test_retrieval_fusion_lab.py 07_EVALUATION/tests/test_retrieval_diagnostic.py
......F....                                                              [100%]
1 failed, 10 passed in 2.93s
```

The failing test is `test_real_memory_controller_search_and_pack`. Its assertion
that the real search returns at least one result fails with `len(results) == 0`
for the SQLite PRAGMA query. This is a runtime retrieval/corpus blocker, not
an import failure, and remains unresolved.

## Limitations

No C8 metrics are reported from this branch: the test suite is not green and
the real provider experiment was not completed. The runner is importable past
the original package error, but evaluation readiness is `PARTIAL`, not proven.
