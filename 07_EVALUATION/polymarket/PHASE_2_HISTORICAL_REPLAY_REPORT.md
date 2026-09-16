# Phase 2 — Historical Replay Report

Status: **ACCEPTED / RUNTIME VERIFIED**
Branch: `r048-pm-phase2-replay`
Acceptance gate: GitHub Actions run `34655372029`
Acceptance commit: `3eb42e14606440905b2fc385654734d6a730ec7b`

## Scope

Implemented only the deterministic historical replay boundary. No prediction, calibration, market-vs-model edge, EV, risk, backtesting, paper trading, execution, credentials, or live-order behavior was added.

## Reuse

The replay layer reuses the Phase 1 `MarketSnapshot` contract and its timestamp/hash validation. It does not create a second temporal-memory subsystem.

Replay eligibility is deliberately stricter than a single `known_as_of` test: a record is eligible only when observation/state time, acquisition time, and known-as-of time are all at or before the requested cutoff `T`.

## Provider contract

The current official Polymarket CLOB price-history contract exposes `market`, `startTs`, `endTs`, `interval`, and `fidelity`, with returned history points containing timestamp `t` and price `p`. The Phase 2 contract preserves those timestamps and records requested fidelity; it does not assume uniform sampling or interpolate missing observations.

No undocumented `as_of` or `resolution_seconds` field was invented.

## Changed paths

- `03_IMPLEMENTATION/packages/polymarket/historical_replay.py`
- `03_IMPLEMENTATION/packages/polymarket/__init__.py`
- `20_TESTS/polymarket/test_historical_replay.py`
- `07_EVALUATION/polymarket/fixtures/phase2_historical_replay.json`
- `07_EVALUATION/polymarket/HISTORICAL_REPLAY_CONTRACT.md`
- `.github/workflows/polymarket-phase2-tests.yml`
- `07_EVALUATION/polymarket/PHASE_2_PROMPT.md`
- this report

## Acceptance evidence

Executed command from the dedicated gate:

`python -m pytest -q 20_TESTS/polymarket/test_market_snapshot.py 20_TESTS/polymarket/test_historical_replay.py`

GitHub Actions run `34655372029` completed with the Phase 2 test job successful, including the Phase 1 snapshot suite and Phase 2 replay suite.

## Acceptance matrix

| Criterion | Status | Evidence |
|---|---|---|
| Versioned replay contract | PASS | Evidence level 3 |
| No post-cutoff state leakage | PASS | Evidence level 1 + runtime tests |
| Acquisition-after-cutoff excluded | PASS | Evidence level 1 + runtime tests |
| Sparse history preserved without interpolation | PASS | Evidence level 1 + runtime tests |
| Contradictory same-timestamp price points fail closed | PASS | Evidence level 1 + runtime tests |
| Deterministic replay under input reordering | PASS | Evidence level 1 + runtime tests |
| Synthetic fixture present and labelled | PASS | Evidence level 3 |
| Phase 1 regression suite executes in dedicated gate | PASS | Evidence level 1 |
| No Phase 3+ functionality added | PASS | Scope review / Evidence level 3 |

## Unrelated repository CI note

A separate repository-wide workflow on PR #83 had one pre-existing Vault state accuracy failure: `VAULT_STATE.md` claimed 830 indexed notes while the runtime index contained 872. That failure is outside the Polymarket Phase 2 change set and is not used as Phase 2 acceptance evidence.

## Boundary decision

**PHASE 2 ACCEPTED.**

The historical replay boundary is now demonstrated by executed tests. Stop Phase 2 here. The only next slice is Phase 3: prediction ledger + provenance.
