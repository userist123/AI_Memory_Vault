# R001 C8 — Retrieval fusion runner forensics

Evidence classifications: `CODE_VERIFIED`, `TEST_VERIFIED`, and
`BROKEN` where noted.

## Baseline

```text
BASE_COMMIT=e43cc81e09789e284ef35a7e326297194f429a9
```

## Reproduced blocker

Running the runner from the repository root initially failed before evaluation
because its imports referenced a nonexistent top-level `evaluation` package:

```text
python 07_EVALUATION/retrieval_fusion/experiment_runner.py --help
ModuleNotFoundError: No module named 'evaluation'
```

The script has no `--help` parser and this invocation starts its experiment;
the attempt was stopped after confirming imports had progressed. No result
was counted from that interrupted run.

## Repair

`experiment_runner.py` was repaired to add `07_EVALUATION` to `sys.path` and
import its local modules. `retrieval_diagnostic_runner.py` likewise imports
the local `full_context_baseline` module. No corpus, scoring method, or
experiment configuration was changed.

## Test evidence

The first repaired branch run reported:

```text
python -m pytest -q 07_EVALUATION/tests/test_retrieval_fusion_lab.py 07_EVALUATION/tests/test_retrieval_diagnostic.py
......F....                                                              [100%]
1 failed, 10 passed in 2.93s
```

The failing test was `test_real_memory_controller_search_and_pack`, whose assertion
that the real search returns at least one result failed with `len(results) == 0`
for the SQLite PRAGMA query. This was a runtime retrieval/corpus blocker, not
an import failure.

A follow-up repair on the branch identified a context-budget serialization
bug: large notes were compressed to `bytes`, then converted to an oversized
`str` by JSON serialization. The context boundary was changed to decode
compressed UTF-8 payloads (or use base64 for non-text bytes), and full
disclosure skips an oversized candidate rather than discarding later
candidates.

After that repair:

```text
python -m pytest -q 07_EVALUATION/tests/test_retrieval_fusion_lab.py 07_EVALUATION/tests/test_retrieval_diagnostic.py
...........                                                              [100%]
11 passed in 2.71s
```

Additional budget regression tests passed in the targeted run (`16 passed in
1.31s`). This is `TEST_VERIFIED` for the local diagnostic suite, not evidence
of real-provider effectiveness.

## C8 runtime evidence on main

`R001_C8_REAL_RUN.md` records a completed Ollama local-provider run at commit
`e43cc81e09789e284ef35a7e326297194f429a9`, using the repository's 15-case corpus,
R1–R4 retrieval strategies, and a full-context baseline for two local models.
That document explicitly limits the interpretation to execution and aggregate
measurement; it does not claim causal proof of memory usefulness.

## Limitations

The forensic evidence does not independently reproduce or validate the local
provider metrics. The corpus is repository-authored, the selective strategies
differ in more than one signal, and no randomized paired treatment/control
design is established by this record.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
