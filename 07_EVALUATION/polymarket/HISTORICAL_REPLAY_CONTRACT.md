# Polymarket Historical Replay Contract v1

## Purpose

Reconstruct the market state and price observations that were actually available at a requested cutoff `T` without exposing later information.

## Replay boundary

A snapshot is eligible at `T` only when all three timestamps are at or before `T`:

- `snapshot_at <= T`: the state existed at that time.
- `known_as_of <= T`: the information was known by that time.
- `acquired_at <= T`: the system had physically acquired the source information by that time.

A price-history point follows the same rule using `observed_at`, `known_as_of`, and `acquired_at`.

The rule is intentionally stricter than using `known_as_of` alone: information recorded as historically true but acquired later must not leak into an earlier replay.

## State selection

For one `market_id`, the deterministic replay engine selects the eligible snapshot with the latest `known_as_of`, then `snapshot_at`, then `acquired_at`, then `snapshot_id` as a stable tie-breaker.

Snapshots from other markets are ignored. No snapshot is synthesized when none is eligible; `missing_state=true` records that absence.

## Price-history semantics

The official CLOB price-history contract accepts `market`, `startTs`, `endTs`, `interval`, and `fidelity`. The documented response provides timestamp `t` and price `p`. The replay layer stores the requested fidelity as request metadata and preserves the observed timestamps exactly; it never assumes uniform sampling or silently interpolates gaps.

The provider contract does not expose an `as_of` field or a returned `resolution_seconds` field in the current official documentation, so neither is invented in the internal contract.

## Contradictions

Two different price values for the same `outcome_id` and `observed_at` are a contradiction and fail closed. Exact duplicate observations are idempotent.

## Resolution isolation

A resolution carried by a later snapshot is unavailable before that snapshot and its information/acquisition boundary. Replay tests must prove that resolved state cannot appear in pre-resolution replay.

## Provenance

Every replay price point carries source type, source reference, acquisition time, and known-as-of boundary. Market-state provenance remains attached to the Phase 1 `MarketSnapshot`.

## Data quality

Synthetic fixtures are labelled `synthetic`. Real provider history must retain the provider timestamps and requested fidelity and must not be presented as historically complete when the provider returned sparse data.

## Determinism

The same snapshots and price points must produce byte-for-byte equivalent replay results regardless of input ordering.

## Scope exclusions

No prediction, calibration, edge, EV, risk, backtesting, paper trading, execution, credentials, or live orders are part of this contract.
