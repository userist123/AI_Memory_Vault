# Phase 14 — Temporal provenance capture contract

## Purpose

Historical replay must distinguish three clocks:

- `observed_at`: when the market observation represents a state/event.
- `known_as_of`: when the observation was historically knowable to the evaluator.
- `acquired_at`: when this system actually obtained the evidence.

Required ordering:

`observed_at <= known_as_of <= acquired_at`

## Cutoff rule

A historical evaluator may use a point at prediction cutoff `C` only when all three timestamps are at or before `C` and the knowledge basis supports a historical claim.

## Knowledge basis

Accepted for cutoff-safe historical evaluation:

- `source_native`: the source explicitly supplies a timestamp that establishes historical knowledge.
- `event_derived`: historical knowledge time is deterministically derived from a versioned source event whose timing is independently evidenced.

Not cutoff-safe by itself:

- `acquisition_only`: the data was fetched retrospectively and only the current acquisition time is known.
- `unknown`: provenance is insufficient.

## Conservative materialization

When a retrospective API returns an old market timestamp but provides no historical knowledge timestamp, the system must not infer that the observation was knowable at `observed_at`. The safe representation is to use the acquisition timestamp as the knowledge bound and classify the evidence as `acquisition_only`.

Such evidence can be retained for research and diagnostics, but it must fail the historical evaluator cutoff check for earlier predictions.

## Required evidence fields

Every provenance-bearing observation should retain:

- `source_ref`
- `observed_at`
- `known_as_of`
- `acquired_at`
- `knowledge_basis`
- `source_timestamp_semantics`

## Non-goals

This phase does not modify the Phase 11 historical tape, does not claim historical publication timing where the source does not expose it, and does not enable live trading or execution.
