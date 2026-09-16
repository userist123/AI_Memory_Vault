# Phase 6 — Market-vs-Model Edge Report

## Status

**ACCEPTED / RUNTIME VERIFIED**

Phase 6 adds leakage-safe retrospective comparison between Phase 3 prediction probabilities and historical market prices. It does not create a calibrated per-prediction probability, expected value, trading recommendation, risk model, or execution path.

## Branch / PR

- Branch: `r052-pm-phase6-market-model-edge`
- Pull request: `#89` — `R052/polymarket Phase 6 market-vs-model edge`
- Base main at phase start: `fc9f740d19c66d496824a6d441dd49378a1ccc57`
- Final acceptance commit: `942882bddbf05a89a92078fb38f93fb012041d34`

## Scope verified

- selects the latest historical market price known at or before each prediction cutoff;
- rejects post-cutoff observations and post-cutoff knowledge;
- rejects contradictory same-timestamp market prices;
- computes raw probability edge as `model_probability - market_price`;
- preserves market price provenance and deterministic content-bound edge IDs;
- summarizes mean edge and sign counts without converting edge into a monetary return.

## Acceptance evidence

Dedicated workflow/check:

- Check: `Phase 6 market-model edge`
- Run: `34683758558`
- Job: `103526990546`
- Conclusion: `success`
- Exact command:

```text
python -m pytest -q \
  20_TESTS/polymarket/test_market_snapshot.py \
  20_TESTS/polymarket/test_historical_replay.py \
  20_TESTS/polymarket/test_prediction_ledger.py \
  20_TESTS/polymarket/test_prediction_council.py \
  20_TESTS/polymarket/test_calibration.py \
  20_TESTS/polymarket/test_market_model_edge.py
```

Executed result:

```text
...................................................... [100%]
54 passed in 0.20s
```

## Acceptance debugging record

The first Phase 6 gate executed 53 tests and found one invalid fixture: it attempted to represent `observed_at > prediction cutoff` while simultaneously setting `known_as_of <= prediction cutoff`, which violates the Phase 2 historical price invariant `observed_at <= known_as_of`. The fixture was corrected to exercise the actual information-boundary rejection instead.

The final gate then passed 54/54.

## Calibration distinction

Phase 5 provides retrospective calibration diagnostics for a set of predictions; it does not produce a transformed, per-prediction calibrated probability. Phase 6 therefore compares the recorded Phase 3 model probability directly and does not mislabel it as a newly calibrated probability.

## Known repository-level warning

GitHub Actions cleanup emits the pre-existing warning:

```text
fatal: No url found for submodule path '04_CONFIG/obsidian/AI_Memory_Vault_OBSIDIAN' in .gitmodules
```

The dedicated Phase 6 test job still completed successfully; this cleanup warning is outside Phase 6 scope and was not used to invalidate acceptance.

## Next phase

**Phase 7 — Risk + Abstention**: define analysis-only decision thresholds, true abstention behavior, and bounded sizing diagnostics without enabling execution or real-money orders.
