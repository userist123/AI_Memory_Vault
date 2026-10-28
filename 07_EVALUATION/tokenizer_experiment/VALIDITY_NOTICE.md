# VALIDITY NOTICE - EXP-TOKEN-001

Status: **INVALID - NULL MEASUREMENT** (recorded 2026-10-22)

## Why

- The committed run (PR #195) did not exercise the tokenizer arms: the arm tokenizers never reached
  `MemoryController.search`, because the `memory_controller.*` shim aliases were not patched.
- All three arms therefore report identical results (21/130 hits, b = 0, c = 0, p = 1.000000).
  That identity is the symptom of a measurement that could not discriminate, not evidence that the
  tokenizers are equivalent.

## What this means

- The verdict (MENTINERE BASELINE, H-TOKEN-1 infirmata) MUST NOT be cited as evidence about tokenizers.
- `PREREGISTRATION.md`, `tokenizer_experiment_cases.json` and `TOKENIZER_EXPERIMENT_REPORT.md` are kept
  unchanged as a historical record (the accuracy test checks the report renders bit-for-bit from the JSON).

## Status of the fix

- The harness was fixed on 2026-10-22: `30_SCRIPTS/evaluation/eval_tokenizer_experiment.py`
  (`arm_tokenizer()` now patches every alias and restores the originals).
- A rerun has NOT been performed. Whether to rerun is an owner decision.

## References

- `07_EVALUATION/reports/overnight_reliability_2026-10-21/PHASE6_STABILIZATION_REPORT.md`
- Guard: `20_TESTS/test_tokenizer_experiment_accuracy.py` (fails if this notice is removed while arms are identical).
