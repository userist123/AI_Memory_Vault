# Phase 7 — Risk + Abstention Report

## Status

**PENDING CI ACCEPTANCE**

Phase 7 is intentionally limited to deterministic abstention/decision gating. The previously drafted bounded sizing diagnostic was removed from the implementation so this phase does not encode position sizing or execution behavior.

## Branch

- `r053-pm-phase7-risk-abstention`
- Base: current `main` at Phase 6 completion

## Implemented

- `03_IMPLEMENTATION/packages/polymarket/risk_abstention.py`
- `20_TESTS/polymarket/test_risk_abstention.py`
- `.github/workflows/polymarket-phase7-tests.yml`
- `07_EVALUATION/polymarket/RISK_ABSTENTION_CONTRACT.md`

## Decision semantics

- missing historical price -> `ABSTAIN`;
- previously abstained prediction -> `ABSTAIN`;
- edge at or below the configured threshold -> `ABSTAIN`;
- positive edge above the configured threshold -> `BET`.

Default threshold: `0.05` raw probability edge.

## Safety boundary

The result payload contains no position size, risk fraction, order quantity, bankroll, execution command, paper order, live order, or credentials. `BET` means only that the analysis gate did not abstain.

## Acceptance

A dedicated CI run must execute the Phase 1-7 Polymarket test suite and record the exact test count/result here before this phase is marked accepted and merged.

## Known repository warning

The repository has a pre-existing malformed Obsidian submodule reference that can emit a Git cleanup warning during Actions teardown. This is outside Phase 7 scope unless it causes the actual test step to fail.

## Next phase after acceptance

Phase 8 — historical backtesting, with explicit out-of-sample evaluation and no live execution.
