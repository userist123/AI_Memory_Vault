# Phase 4 — Prediction Council Contract

## Purpose

Combine independently generated Phase 3 prediction records into one deterministic research observation. The council is an aggregation boundary, not a trading decision. It does not calibrate probabilities, estimate market-vs-model edge, compute EV, size positions, backtest, paper trade, or execute orders.

## Inputs

Each member MUST be a valid `PredictionRecord` from `polymarket-prediction-ledger.v1` with:

- the same `market_id` and `outcome_id`;
- the same exact `known_as_of` information cutoff;
- immutable prediction provenance, including snapshot IDs and model identity/version;
- a distinct `(model_id, model_version)` pair from every other member.

At least two members are required. This prevents a single prediction from being relabeled as a council result.

## Aggregation

1. Validate every Phase 3 prediction before aggregation.
2. Require common market/outcome and exact information cutoff.
3. Require distinct model identity/version pairs.
4. Sort member prediction IDs for deterministic canonicalization.
5. Exclude explicitly abstained members from the arithmetic mean.
6. Compute an **unweighted arithmetic mean** of non-abstained probabilities. No calibration or confidence weighting is performed.
7. If every member abstains, produce an explicit council abstention with boundary probability `0.0`; do not invent a consensus probability.
8. Council `predicted_at` is the latest member prediction timestamp, while `known_as_of` remains the shared cutoff.

## Provenance

The council binds:

- every member `prediction_id`;
- the union of all immutable Phase 1 `snapshot_ids`;
- a deterministic evidence hash derived from member prediction IDs and their evidence bundle hashes;
- council model identity `prediction-council` and version `polymarket-prediction-council.v1`.

The council ID is SHA-256 content-derived and cannot be caller-selected.

## Determinism and integrity

- Member order MUST NOT change council content, ID, or provenance.
- Forged council IDs fail canonical-content validation.
- Duplicate model identities are rejected as non-independent.
- Mismatched information cutoffs are rejected rather than silently mixing information sets.
- Abstention is an explicit state and is not interpreted as confidence.

## Reuse decision

The repository search found no existing reusable council/consensus implementation exposed under the Vault implementation tree. Phase 4 therefore adds only the missing Polymarket aggregation contract and directly reuses the Phase 3 `PredictionRecord` and `PredictionProvenance` types rather than duplicating their validation/provenance machinery.

## Out of scope

Calibration, resolution scoring, Brier/log loss, market-vs-model edge, EV, fees, spread, slippage, risk, sizing, backtesting, paper trading, execution, credentials, live orders, and real-money behavior.
