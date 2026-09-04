# Repository hygiene

An exact scan found malformed generated citation markers in `README.md` (three broken external-reference artifacts). The three markers were removed without changing the surrounding claims. No such marker remains in the scanned source, knowledge, derived, evaluation, cognitive, controller, scripts, tests, or workflow paths.

The deterministic guard is `scripts/check_generated_artifact_hygiene.py`, covered by `tests/test_generated_artifact_hygiene.py` and invoked by `.github/workflows/memory-v6-tests.yml`.

Observed command output after cleanup:

```text
MALFORMED_GENERATED_ARTIFACTS=0
```

Evidence classification: initial corruption RUNTIME_VERIFIED; cleanup and guard TEST_VERIFIED after the targeted test run. The guard intentionally skips binary PDFs, bytecode, and notebooks.
