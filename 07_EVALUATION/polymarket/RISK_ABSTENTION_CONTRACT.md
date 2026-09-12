# Phase 7 — Risk, Abstention and Sizing Contract

## Purpose

Decide whether a prediction with a measured edge should be acted on, and if so
at what notional size. Two components, deliberately separate:

- `risk_abstention.py` answers `BET` or `ABSTAIN` from the edge alone.
- `risk_sizing.py` applies the limits and produces a size, or refuses.

Both are analysis. Neither constructs an order, contacts a venue, holds a
credential or opens a socket. Their output is a number in a report.

## Boundary

This phase MUST NOT:

- construct, submit, cancel or reconcile an order;
- reference a venue endpoint, wallet, signing key or API credential;
- import any module that does;
- read live market data — it consumes Phase 2 historical points and Phase 6
  resolved prices only.

A test greps `risk_sizing.py` for `requests`, `httpx`, `urllib`, `socket`,
`api_key`, `private_key`, `sign(`, `submit_order` and `place_order`. The
boundary is asserted, not merely documented.

## The abstention gate

Input is a `PredictionRecord` and a market price resolved through
`select_latest_eligible_price`. Output is `BET` or `ABSTAIN` with a reason.

`ABSTAIN` is returned when:

| reason | condition |
|---|---|
| `missing_historical_market_price` | no eligible price at or before the cutoff |
| `edge_below_abstention_threshold` | `edge < min_positive_edge` |
| `edge_at_abstention_threshold` | `edge` equals the threshold |

**An edge exactly at the threshold abstains.** This is the contract's sharpest
point and it was wrong in the first implementation: the comparison was
`edge <= min_positive_edge`, correct arithmetic and incorrect in binary
floating point, because `0.55 - 0.50` is `0.050000000000000044` — larger than
`0.05` by 4.2e-17. A prediction sitting precisely on a five-point threshold
returned `BET`. The gate failed open, in the one component whose purpose is to
decline, on the exact boundary a policy author would choose deliberately.

Comparison is now by `math.isclose` with both relative and absolute tolerance.
Absolute is required because the quantity is a probability difference and a
relative tolerance is meaningless as the threshold approaches zero — precisely
where a "bet on any positive edge" policy sits.

Contradictory historical points at one timestamp fail closed, raising rather
than choosing between them. The message comes from `market_model_edge`, which
resolves the price; `historical_replay` words the same refusal differently.
That inconsistency exists on `main` and is out of scope here.

## The risk engine

Eleven conditions refuse a trade. Every one is independent of expected value,
and that independence is the point — Section 15 of the brief: *the system must
be able to refuse a trade even when EV appears positive.*

| refusal | source |
|---|---|
| `kill_switch_engaged` | portfolio |
| `drawdown_limit_breached` | portfolio |
| `daily_loss_limit_breached` | portfolio |
| `portfolio_exposure_exhausted` | portfolio |
| `category_exposure_exhausted` | portfolio |
| `unresolved_exposure_exhausted` | portfolio |
| `insufficient_calibration_sample` | portfolio |
| `stale_quote` | market |
| `resolution_risk_above_maximum` | market |
| `net_edge_below_minimum` | market |
| `no_available_depth` | market |

**All applicable refusals are returned, not the first one found.** A report
naming a single cause sends someone to fix it, and they discover the next only
after they have.

## Sizing

Order of operations is load-bearing and MUST NOT be rearranged:

1. limits are checked — a refusal never depends on the arithmetic below it;
2. the method proposes a fraction;
3. caps bind, last and unconditionally;
4. a proposal below `min_position_fraction` is refused rather than taken.

**Kelly may never exceed a cap.** The criterion assumes a known probability,
independent repetition and no market impact. This system has none of the three:
its probabilities are produced by a council and then recalibrated. Kelly is
computed, multiplied by `kelly_fraction`, then clamped. `kelly_fraction` above
0.5 is rejected by the policy validator. The same clamp binds a misconfigured
fixed fraction, because it is applied after the method rather than inside it.

**Step 4 exists because the first implementation lacked it.** A book with a cap
nearly consumed allocated whatever slice remained — 0.001% of bankroll in one
measured case. That position pays fees and occupies an exposure slot to express
no view. The refusal still names the binding constraint, so thin liquidity
stays distinguishable from a full book.

## Defaults

| limit | default | why |
|---|---|---|
| `max_position_fraction` | 0.02 | |
| `min_position_fraction` | 0.002 | below this, decline |
| `max_portfolio_fraction` | 0.20 | |
| `max_category_fraction` | 0.08 | |
| `max_unresolved_fraction` | 0.15 | |
| `max_depth_fraction` | 0.10 | above it the edge is arithmetic, not money |
| `max_daily_loss_fraction` | 0.05 | |
| `max_drawdown_fraction` | 0.15 | |
| `min_net_edge` | 0.03 | below it, noise after fees and slippage |
| `max_resolution_risk` | 0.30 | |
| `max_quote_age_seconds` | 300 | older is a memory of a market, not a market |
| `min_calibration_samples` | 50 | fewer is not calibration |

These are timid on purpose. A research system with no realised track record has
no basis for anything else. **A limit loosened once evidence supports it is a
decision someone made; a limit that started loose is one nobody made.**

`min_position_fraction > max_position_fraction` and
`max_position_fraction > max_portfolio_fraction` are both rejected at
validation — the second because a single position would otherwise be permitted
to breach the book's own cap.

## Determinism

No clock, no randomness, no network, no I/O. The same inputs produce the same
decision, which is what makes the backtester in Phase 8 able to replay it.

## Reuse

`select_latest_eligible_price` from Phase 6 and `HistoricalPricePoint` from
Phase 2 are consumed unchanged. `PredictionRecord` from Phase 3 is the input
type. Nothing in this phase reimplements price resolution, cutoff handling or
ledger schemas.

## Explicit exclusions

Not in this phase, and each belongs to a later one: expected-value arithmetic
with explicit fees (Phase 8 backtest), correlated-exposure modelling across
markets, market impact beyond the depth ceiling, paper trading (Phase 10), any
execution adapter (Phase 11).
