# Phase 5 — Calibration Contract

## Purpose

Score already-recorded Phase 3 prediction records against later verified market resolutions and report deterministic calibration diagnostics. Calibration is retrospective evaluation only; it must not alter prediction generation or introduce trading decisions.

## Inputs

Each prediction MUST be a valid `PredictionRecord` from `polymarket-prediction-ledger.v1`.

Evidence snapshots referenced by the prediction provenance MUST be supplied and MUST:

- exist by immutable snapshot ID;
- be verified-quality snapshots;
- belong to the prediction market;
- have `known_as_of <= prediction.known_as_of`.

The resolution snapshot MUST be a verified-quality `MarketSnapshot` containing `resolution.status == "resolved"` and valid winning `outcome_ids`.

The resolution's `known_at` MUST be strictly later than the prediction information cutoff. Equal timestamps are rejected fail-closed because the prediction cannot be assumed to precede information becoming known at the same instant.

## Scoring

For each binary event defined by a prediction's `outcome_id`:

- target is `1` when that outcome is in the verified resolved outcome set, otherwise `0`;
- Brier score is `(p - target)^2`;
- log loss uses the finite floor `LOG_EPSILON = 1e-15` before the logarithm;
- equal-width reliability bins summarize count, mean predicted probability, empirical frequency, and absolute calibration gap.

No confidence weighting, market price comparison, fees, slippage, EV, risk, sizing, backtesting, paper trading, execution, or live behavior is introduced.

## Leakage boundary

The evaluator does not rebuild or modify prediction probabilities. It only joins immutable prediction provenance to verified resolution data after establishing that all prediction evidence was known no later than the prediction cutoff and that the resolution became known strictly afterward.

## Determinism

Scoring is invariant to observation input order. Empty datasets and invalid bin counts fail closed. The report schema is versioned.

## Reuse decision

Phase 5 directly reuses the Phase 1 `MarketSnapshot`/`ResolutionMetadata` validation and the Phase 3 `PredictionRecord` provenance contract. No parallel snapshot or prediction schema is introduced.

## Out of scope

Calibration fitting that changes future probabilities, market-vs-model edge, EV, trading thresholds, fees/spread/slippage, risk, position sizing, backtesting, paper trading, execution, credentials, and real-money orders.
