# Phase 12 — Model historical replay contract

## Status
Design/specification only. No execution adapter, wallet, credentials, or live trading path is introduced.

## Goal
Define the evidence contract required before historical model predictions can be evaluated against real historical Polymarket observations and terminal outcomes.

## Required inputs

1. `PredictionRecord` from the existing prediction ledger.
2. An explicit prediction cutoff (`known_as_of`).
3. The market-bound historical bundle from Phase 11.
4. For every candidate historical price observation used for comparison, an explicit `known_as_of` and `acquired_at` provenance timestamp.
5. Terminal resolution metadata whose `resolution_known_at` is strictly after the prediction cutoff.

## Leakage rules

A replay case is invalid when any of the following is true:

- the prediction market ID differs from the historical bundle market ID;
- the selected historical observation is after the prediction cutoff;
- the observation's `known_as_of` is after the prediction cutoff;
- the observation's `acquired_at` is after the prediction cutoff;
- the terminal resolution was known at or before the prediction cutoff;
- a suitable historical observation cannot be bound to explicit knowledge provenance;
- contradictory observations exist for the same outcome and observation timestamp.

No interpolation, forward filling, or post-cutoff observation is permitted.

## Evaluation outputs

For an accepted research case, the evaluation record should contain:

- prediction ID and provenance;
- market and outcome IDs;
- prediction cutoff;
- selected historical market observation and provenance;
- raw model probability;
- historical market price;
- raw probability-minus-price edge;
- abstention state;
- deterministic paper-evaluation result, when a separately approved research simulator is used;
- terminal outcome and settlement result;
- provenance for every external fact.

## Evidence boundary

This phase may establish whether the prediction ledger can be joined to historical market observations without temporal leakage. It must not be described as evidence of profitable trading unless an independently validated research backtest demonstrates that result over a sufficiently broad held-out dataset.

Phase 11 already demonstrated real public-data ingestion and settlement plumbing for three resolved markets. That evidence is a prerequisite input, not a model-performance claim.

## Next acceptance gate

Before any model historical replay is accepted, the repository must show deterministic tests proving all leakage rejection rules above and at least one end-to-end research fixture where every timestamp is explicit and the expected rejection/acceptance decision is reproducible.
