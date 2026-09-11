# Phase 4 — Prediction Council Report

Status: **ACCEPTED / RUNTIME VERIFIED**
Branch: `r050-pm-phase4-prediction-council`
Base: `main` at `96e39a1ab57c9187e0ce7acc48fe3554c4dc8e25`

## Scope

Implemented only the deterministic prediction council over Phase 3 prediction records. The phase does not calibrate probabilities, score resolutions, estimate market-vs-model edge, compute EV, model fees/spread/slippage, size positions, backtest, paper trade, execute orders, use credentials, or enable real-money behavior.

## Reuse decision

A repository search for council/consensus/agent-council implementation returned no reusable council component in the inspected repository search surface. The implementation therefore reuses the Phase 3 `PredictionRecord` and `PredictionProvenance` contracts directly and adds only the missing council aggregation layer.

## Implemented contract

- at least two member predictions required;
- all members must target the same market and outcome;
- all members must share the exact `known_as_of` cutoff;
- `(model_id, model_version)` pairs must be distinct;
- deterministic unweighted arithmetic mean over non-abstained members;
- all-abstained council is an explicit abstention, not a confidence score;
- member IDs are sorted for deterministic canonicalization;
- provenance binds member prediction IDs, snapshot ID union, and a deterministic evidence hash;
- council identity/version are explicit;
- council ID is SHA-256 content-derived and validated;
- member order cannot alter the result.

## Exact acceptance command

```text
python -m pytest -q 20_TESTS/polymarket/test_market_snapshot.py 20_TESTS/polymarket/test_historical_replay.py 20_TESTS/polymarket/test_prediction_ledger.py 20_TESTS/polymarket/test_prediction_council.py
```

Dedicated GitHub Actions run: `34656643838`
Job: `Phase 4 prediction council`
Result: **success**
Runtime output:

```text
....................................                                     [100%]
36 passed in 0.13s
```

The gate checked the exact final branch revision before merge and exercised Phase 1 snapshot, Phase 2 replay, Phase 3 ledger, and Phase 4 council tests together.

## Acceptance matrix

| Criterion | Status | Evidence |
|---|---|---|
| Phase 3 predictions consumed directly | PASS | Runtime tests + implementation |
| Independent model identities required | PASS | Runtime test |
| Common information cutoff required | PASS | Runtime test |
| Deterministic aggregation | PASS | Runtime test |
| Member-order invariance | PASS | Runtime test |
| Abstention semantics explicit | PASS | Runtime tests |
| Provenance binds member predictions/snapshots/evidence | PASS | Runtime test + implementation |
| Content-derived council ID | PASS | Runtime test |
| Phase 1-3 regressions execute | PASS | Dedicated gate |
| Calibration/edge/EV/risk/execution added | NO | Scope review |
| Real-money execution enabled | NO | Explicitly out of scope |

## CI note

The dedicated Phase 4 job completed successfully. The repository-wide hygiene workflow still emits the pre-existing cleanup warning for the malformed Obsidian submodule reference (`04_CONFIG/obsidian/AI_Memory_Vault_OBSIDIAN` has no URL in `.gitmodules`); this unrelated repository condition does not invalidate the dedicated Polymarket gate.

## Boundary decision

**PHASE 4 ACCEPTED.**

Stop here. The only next slice is **Phase 5 — Calibration**. It must consume immutable Phase 3 prediction records (and council outputs where appropriate), score against verified resolutions without leakage, and demonstrate calibration behavior before any market-vs-model edge or trading logic is introduced.
