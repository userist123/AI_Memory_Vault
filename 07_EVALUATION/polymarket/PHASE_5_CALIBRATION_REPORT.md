# Phase 5 — Calibration Report

## Status

**ACCEPTED / RUNTIME VERIFIED**

Phase 5 implements retrospective calibration scoring for immutable Phase 3 prediction records. It does not change prediction probabilities and does not introduce market-vs-model edge, EV, risk, sizing, backtesting, paper trading, execution, credentials, or live orders.

## Branch / PR

- Branch: `r051-pm-phase5-calibration`
- Pull request: `#88` — `R051/polymarket Phase 5 calibration`
- Base main at phase start: `5f7009a8e0dde7e51665c6adadff00390ef6d9d6`
- Latest acceptance-tested commit: `d847ba85a446109ff24471b2d63358e786cd0542`

## Acceptance evidence

Dedicated workflow:

- Workflow: `Polymarket Phase 5 Tests`
- Run: `34683519279`
- Conclusion: `success`
- Exact command:

```text
python -m pytest -q \
  20_TESTS/polymarket/test_market_snapshot.py \
  20_TESTS/polymarket/test_historical_replay.py \
  20_TESTS/polymarket/test_prediction_ledger.py \
  20_TESTS/polymarket/test_prediction_council.py \
  20_TESTS/polymarket/test_calibration.py
```

Executed result:

```text
............................................ [100%]
44 passed in 0.17s
```

## What was verified

1. Calibration observations are derived from valid Phase 3 `PredictionRecord` objects.
2. Evidence snapshots must exist, verify, belong to the same market, be `verified`, and have `known_as_of <= prediction.known_as_of`.
3. Resolution snapshots must be verified and resolved; target labels come only from validated resolved outcome IDs.
4. Prediction information cutoff must be strictly earlier than `resolution.known_at`; equal timestamps fail closed.
5. Brier score, finite clipped log-loss, and deterministic equal-width reliability bins are reproducible and independent of observation order.
6. Phase 1–4 regression tests remain green together with Phase 5.

## Acceptance debugging record

The first Phase 5 CI attempt failed before test execution because the repository test configuration imported `yaml` and the dedicated workflow installed only `pytest`. The workflow was corrected to install `pyyaml`.

The next gate executed all tests and found three test-fixture errors, not implementation failures: two fixtures keyed evidence by a human hint instead of the content-derived snapshot ID, and one fixture attempted `known_as_of > predicted_at`, which Phase 3 correctly rejects. The fixtures were corrected to use actual snapshot IDs and an equal prediction/resolution cutoff for the fail-closed boundary test.

The final dedicated gate then passed 44/44.

## Known repository-level warning

The Phase 5 job remains green, but GitHub Actions cleanup emits the pre-existing warning:

```text
fatal: No url found for submodule path '04_CONFIG/obsidian/AI_Memory_Vault_OBSIDIAN' in .gitmodules
```

This is outside Phase 5 scope and was not used to invalidate the dedicated acceptance gate.

## Next phase

**Phase 6 — Market-vs-model edge**: compare calibrated model probabilities with time-aligned historical market prices under the established information boundary. This phase must remain analysis-only and must not introduce trading execution.
