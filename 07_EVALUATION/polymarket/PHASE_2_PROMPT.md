# Polymarket Research System — Phase 2 Prompt

## Historical replay and information-set reconstruction

Phase 1 is accepted only on executed runtime evidence. Build the smallest deterministic replay layer on top of the Phase 1 canonical snapshot model.

### Goal
For a cutoff timestamp `T`, reconstruct only the market state and price observations that were available by `T`.

### Required semantics

A record is eligible only when its observation/state timestamp, acquisition timestamp, and `known_as_of` timestamp are all at or before `T`.

Reuse `MarketSnapshot`, `SnapshotStore`, existing provenance requirements and existing timestamp parsing. Do not create a second temporal-memory subsystem.

### Provider contract

The current official Polymarket CLOB price-history contract exposes `market`, `startTs`, `endTs`, `interval`, and `fidelity`. The documented response contains historical points with timestamp `t` and price `p`. Preserve the requested fidelity as request metadata and preserve returned timestamps exactly. Do not assume uniform sampling or interpolate missing observations.

The current official documentation does not expose `as_of` or returned `resolution_seconds` for this endpoint. Do not invent either field. citeturn309990view0

### Scope

- deterministic replay engine for synthetic fixtures first
- explicit information-set and acquisition cutoff
- sparse history preserved as sparse history
- later resolution hidden before its availability boundary
- deterministic ordering and contradiction detection
- versioned replay contract
- automated runtime tests

### Out of scope

No prediction, calibration, market-vs-model edge, EV, risk, position sizing, backtesting, paper trading, execution, wallet credentials, or live order placement.

### Acceptance

1. Replay contract is versioned.
2. No state or price point first available after `T` can enter replay at `T`.
3. Acquisition-after-cutoff is rejected even when `known_as_of` is earlier.
4. Sparse points remain sparse; no interpolation.
5. Conflicting duplicate price points fail closed.
6. Replay is deterministic under input reordering.
7. Synthetic fixture is versioned and labelled synthetic.
8. Phase 1 and Phase 2 tests pass in executed CI.
9. Exact runtime evidence is recorded.

### Stop rule

After Phase 2 acceptance, stop. The next and only next slice is Phase 3: prediction ledger + provenance.
