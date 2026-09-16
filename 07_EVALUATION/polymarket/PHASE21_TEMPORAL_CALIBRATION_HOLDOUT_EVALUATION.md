## Phase 21 — temporal historical calibration holdout evaluation

Research-only Phase 21. Compares the original and fitted probabilities on the strictly later holdout segment only.

### Contract

- Consume only an accepted Phase 19 temporal calibration/holdout split and an accepted Phase 20 fit.
- Score raw and calibrated probabilities on the exact same holdout observations.
- Never use holdout targets to fit or alter the calibration mapping.
- Refuse holdout probabilities whose calibration bin was unobserved during fitting.
- Preserve deterministic Brier/log-loss scoring and explicit deltas.

### Non-goals

- No model retraining or parameter search.
- No profitability or predictive-alpha claim.
- No live trading, credentials, wallet, signing, or execution.
- No inference of historical knowledge timing.
