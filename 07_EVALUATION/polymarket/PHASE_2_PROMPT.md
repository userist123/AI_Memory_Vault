# Polymarket Research System — Phase 2 Prompt

## Historical replay and information-set reconstruction

Phase 2 may begin only after Phase 1 acceptance is proven by executed CI tests.

### Goal
Build the smallest deterministic historical replay layer that can reconstruct a Polymarket market state at a requested forecast timestamp using only information that was available by that timestamp.

### Scope
- Read `PHASE_0_ARCHITECTURE_AUDIT.md`, `PHASE_1_MARKET_SNAPSHOT_REPORT.md`, and `MARKET_SNAPSHOT_CONTRACT.md` before changes.
- Reuse the Phase 1 canonical market/snapshot model.
- Reuse existing temporal, provenance, hashing, and storage primitives where applicable.
- Add a replay input contract and deterministic replay engine for synthetic fixtures first.
- Define explicit `as_of` and `known_as_of` semantics.
- Support price-history observations with their provider-declared observation resolution; do not assume uniform sampling.
- Preserve missing/sparse history as missing rather than interpolating silently.
- Record source, acquisition time, and replay provenance.
- Add deterministic replay fixtures covering multiple timestamps and a later resolution that must remain invisible before its information boundary.

### Verified provider facts
Official Polymarket documentation currently exposes price history through `data-api.polymarket.com/v2/prices-history` and supports relative intervals, explicit `start`/`end`, and `as_of`. Price-history points carry timestamp, price, and `resolution_seconds`; explicit ranges are limited to at most 15 days, and finer-grained history has finite retention. The replay layer must preserve these semantics and never manufacture missing observations. See the official documentation cited in the phase report.

### Out of scope
Do not implement prediction, calibration, edge, EV, risk, backtesting, paper trading, execution, credentials, or live order placement.

### Acceptance
1. A replay contract is versioned.
2. Replay at `T` cannot expose fields first known after `T`.
3. Replaying the same snapshot set is deterministic.
4. Sparse/missing observations remain explicit.
5. Provider resolution metadata is preserved.
6. Synthetic replay fixtures pass automated tests.
7. No real historical observations are fabricated.
8. Relevant existing tests remain green.
9. Exact commands and CI outputs are recorded.

### Stop rule
After acceptance, stop and propose only the single smallest Phase 3 slice: prediction ledger + provenance. Never merge Phase 3 work in the same change set.
