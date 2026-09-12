# Phase 7 — Abstention Contract

## Scope

Phase 7 defines a deterministic, analysis-only `BET` / `ABSTAIN` gate over recorded Phase 3 predictions and historically eligible Phase 6 market prices.

## Information boundary

For a prediction at cutoff `T`, the gate may use only a historical market price selected by the existing Phase 6 eligibility rules. Post-cutoff observation or post-cutoff knowledge is excluded. Contradictory same-timestamp historical prices fail closed.

## Decision rule

Let `edge = model_probability - market_price`.

- Missing market price -> `ABSTAIN`.
- Prediction already recorded as abstained -> `ABSTAIN`.
- `edge <= min_positive_edge` -> `ABSTAIN`.
- `edge > min_positive_edge` -> `BET`.

The default threshold is `min_positive_edge = 0.05`.

## Explicit non-scope

This phase does not calculate expected monetary value, bankroll use, position size, risk fraction, order quantity, fees, spread, slippage, paper orders, live orders, credentials, or execution instructions.

`BET` here means only that the analysis gate did not abstain. It is not an instruction to trade.

## Acceptance properties

- deterministic decision output;
- explicit abstention reasons;
- finite probabilities/prices in `[0,1]`;
- no future-information leakage through historical price selection;
- no execution or sizing fields in the decision payload.
