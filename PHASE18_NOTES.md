# Phase 18 — historical calibration projection

Research-only contract for projecting the accepted Phase 17 historical evaluation run into the existing deterministic calibration scorer.

## Safety boundary

The projection first validates the complete Phase 17 run. This preserves its cutoff-safe temporal provenance and duplicate-prediction rejection before any calibration score is computed.

## Scope

- project accepted historical evaluations into `CalibrationObservation`;
- preserve prediction cutoff and resolution knowledge timestamps;
- compute target from the resolved outcome after Phase 17 validation;
- reuse the existing deterministic Brier/log-loss/reliability-bin scorer.

## Explicit non-goals

- no model fitting;
- no calibration transform learned from the evaluation set;
- no benchmark or profitability claim;
- no live execution;
- no inference of historical knowledge timing.
