# Phase 7 — Risk, Abstention and Sizing Report

## Status

Complete on branch, **not merged**. Phase 6 remains the last phase in `main`.

The decision to merge is deliberately not taken here. Phase 7 was begun by
another agent (ChatGPT/Codex), which stopped before PR because its tooling
blocked writes to the risk/sizing component and it declined to rephrase around
that block. That was the correct call and the work resumed on a branch off its
own, without rewriting its commits.

## Branch

```
r054-phase7-threshold-fix   ← this work
  └── r053-pm-phase7-risk-abstention   ← the prior agent's, unmodified
        └── main @ 9d016683c38f
```

Eight commits ahead of `main`. Six are the prior agent's, two are this work.

## Scope

| file | lines | what |
|---|---|---|
| `03_IMPLEMENTATION/packages/polymarket/risk_abstention.py` | modified | BET/ABSTAIN gate |
| `03_IMPLEMENTATION/packages/polymarket/risk_sizing.py` | new | limits + sizing |
| `20_TESTS/polymarket/test_risk_abstention.py` | modified | 12 tests |
| `20_TESTS/polymarket/test_risk_sizing.py` | new | 28 tests |
| `07_EVALUATION/polymarket/RISK_ABSTENTION_CONTRACT.md` | new | the contract |

## Acceptance evidence

```
PYTHONPATH=03_IMPLEMENTATION python -m pytest 20_TESTS/polymarket/ -q
94 passed in 0.14s

PYTHONPATH=03_IMPLEMENTATION python -m pytest \
    20_TESTS/polymarket/test_risk_abstention.py \
    20_TESTS/polymarket/test_risk_sizing.py -q
40 passed in 0.07s

PYTHONPATH=03_IMPLEMENTATION python -m pytest 20_TESTS/ -q
1674 passed, 9 skipped in 64.93s
```

`PYTHONPATH=03_IMPLEMENTATION` is required; without it every polymarket test
module fails collection with `ModuleNotFoundError: No module named 'packages'`.
This affects the phases already in `main` too and is a repository-level
packaging gap, not a defect of this phase.

The execution boundary is asserted rather than documented:
`test_the_module_has_no_route_to_execution` greps `risk_sizing.py` for
`requests`, `httpx`, `urllib`, `socket`, `api_key`, `private_key`, `sign(`,
`submit_order`, `place_order`. Zero matches.

## Two defects found and fixed

**The abstention gate failed open at exactly its threshold.**
`test_edge_at_or_below_threshold_abstains` asserted a decline and got `BET`.
`edge <= min_positive_edge` is correct arithmetic and wrong in binary floating
point: `0.55 - 0.50` is `0.050000000000000044`, larger than `0.05` by 4.2e-17.

The direction matters more than the magnitude. This is the one component whose
purpose is to decline, and it failed open — on the exact boundary a policy
author would choose deliberately and would most expect to be honoured. Now
compared with `math.isclose`, absolute tolerance alongside relative because the
quantity is a probability difference and relative alone is meaningless as the
threshold approaches zero. Four nominally-at-threshold pairs are pinned, plus
one genuinely above, because a gate that abstains on everything is as useless
as one that bets on everything.

**A test asserted the wrong module's wording.**
`test_contradictory_same_timestamp_fails_closed` matched `"contradictory
historical price point"`, which `historical_replay` raises, while the path under
test resolves prices through `market_model_edge`, which words the same refusal
differently. Behaviour was already correct — it fails closed rather than
choosing a price. Only the assertion was wrong.

Both modules raising near-identical errors in different words is a real
inconsistency. It is left alone: fixing it changes a contract already in `main`.

## One gap the tests found in the implementation

Writing `test_a_book_with_no_room_left_refuses_rather_than_trading_small`
revealed that a book with a cap nearly consumed allocated whatever slice
remained — **0.001% of bankroll** in the measured case. That position pays fees
and occupies an exposure slot to express no view.

`min_position_fraction` was added, defaulting to 0.002. The refusal still names
the binding constraint, so thin liquidity stays distinguishable from a full
book. A second test then failed for the right reason — 1,000 of depth allows
100 on a 100,000 bankroll, now below the floor — and was rewritten rather than
the floor being loosened to accommodate it.

## What is demonstrated

**Kelly cannot exceed a cap.** Half Kelly — the most the policy validator
allows — at a 40-point edge on a 0.50 contract is 20% of bankroll against a 2%
cap. The test walks probabilities to 1.0 and the cap holds at every one. A
misconfigured fixed fraction of 0.90 clamps to 0.02 and the decision records
`uncapped_fraction: 0.90`, so a reader can see how much the cap actually bound.

**A positive EV is not permission.** Eleven limits each refuse a trade carrying
a 20-point edge, independently. All applicable reasons are returned together.

## What is not demonstrated

No realised performance, no expected-value arithmetic with explicit fees, no
correlated exposure across markets, no market impact beyond the depth ceiling.
The defaults are conservative guesses, not calibrated settings — there is no
track record to calibrate them against, which is itself the reason
`min_calibration_samples` exists.

## Trading readiness

**RESEARCH READY.** Not paper-ready: Phase 8 (backtesting) and Phase 9
(benchmark and ablations) are what would establish whether any of this has an
edge, and neither exists. Nothing here should be read as evidence that it does.

## Next phase

Phase 8 — backtesting. It is the first phase capable of producing evidence
rather than structure, and every limit above is currently an assumption waiting
for it.
