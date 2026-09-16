# Phase 13 — Model historical evaluator

## Scope

Phase 13 joins an existing immutable `PredictionRecord` to a real Phase 11 historical market bundle and evaluates raw probability-versus-price edge plus terminal settlement.

This is research-only. It does not place orders, sign transactions, connect wallets, or claim profitability.

## Leakage contract

The evaluator requires matching `market_id`, selects only observations at or before `prediction.known_as_of`, and rejects bundles whose `resolution_known_at` is not strictly after the prediction cutoff.

No interpolation, forward filling, post-cutoff observation, or terminal-outcome look-ahead is allowed.

## Output

Each accepted evaluation contains prediction ID, market/outcome IDs, cutoff, selected historical observation, raw model probability, historical market price, raw edge, terminal outcome, win/loss state, and binary settlement units per unit notional.

Settlement is an analytical outcome calculation only. The evaluator does not create a `PaperInstruction` or invoke a live/execution adapter.

## Acceptance gate

The dedicated Phase 13 CI workflow must pass its evaluator tests plus the Phase 12 temporal contract, Phase 11 historical replay, Phase 6 edge, and prediction-ledger regression tests.

Passing Phase 13 establishes deterministic research evaluation plumbing. It is not evidence of profitable trading and is not a substitute for a broad held-out backtest.
