# Phase 6 — Market-vs-Model Edge Contract

## Purpose

Compare an immutable Phase 3 `PredictionRecord` probability with a historically recorded Polymarket price that was observable within the prediction information boundary.

This phase is analysis-only. It does not place orders, size positions, calculate fees/slippage, or declare profitable trading opportunities.

## Boundary

For a prediction with `known_as_of = T`, an eligible market price MUST satisfy:

- `observed_at <= T`
- `known_as_of <= T`
- `observed_at <= known_as_of`
- the price outcome ID matches the prediction outcome ID
- the price is finite and in `[0, 1]`
- source type and source reference are present

The selector chooses the latest eligible observation timestamp. No interpolation, forward-filling from future data, or post-cutoff evidence is allowed.

Contradictory full records for the same outcome and observation timestamp fail closed.

## Edge definition

Raw probability edge is:

`edge = prediction.probability - market_price`

Positive edge means the recorded model probability is above the historical market price. Negative edge means it is below. This is not an expected-value calculation and does not account for fees, spread, slippage, liquidity, or execution.

## Calibration distinction

Phase 5 supplies retrospective calibration diagnostics for a set of predictions; it does not create a transformed, per-prediction calibrated probability. Therefore Phase 6 compares the recorded Phase 3 model probability directly and must not label it as a newly calibrated probability.

## Determinism

Edge IDs are content-bound to the prediction identity, market-price provenance, timestamps, and numeric values. Summary observations are sorted by prediction ID and are independent of input order.

## Reuse

- Phase 1 `MarketSnapshot` / canonical timestamp handling
- Phase 2 `HistoricalPricePoint` and `HistoricalReplay` information-boundary rules
- Phase 3 `PredictionRecord` and provenance validation
- Phase 5 remains a separate retrospective calibration evaluator

## Explicit exclusions

No EV, fees, spread, slippage, risk, sizing, backtesting, paper trading, credentials, execution adapter, live orders, or real-money behavior is included in Phase 6.
