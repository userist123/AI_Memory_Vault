# Phase 3 — Prediction Ledger Contract

## Purpose

Record model predictions as immutable, reproducible research observations. This phase does not score predictions, calibrate probabilities, estimate edge/EV, size positions, execute orders, or use real money.

## Required fields

Each prediction MUST contain:

- `prediction_id`: deterministic content-derived identifier.
- `schema_version`: `polymarket-prediction-ledger.v1`.
- `market_id` and `outcome_id`.
- `probability`: finite numeric value in `[0,1]`.
- `predicted_at`: prediction timestamp with explicit timezone.
- `known_as_of`: information cutoff; MUST be `<= predicted_at`.
- `provenance.evidence_bundle_hash`.
- `provenance.snapshot_ids`: at least one immutable Phase 1 snapshot identifier.
- `provenance.source_refs` and aligned `source_types`.
- `provenance.model_id` and `model_version`.

Optional `prompt_hash`, note, and explicit abstention marker are allowed. Abstention is not a calibration or confidence claim.

## Integrity rules

1. The prediction ID is SHA-256 over the canonical prediction payload and is not caller-selected.
2. A ledger append is idempotent for an identical record.
3. A reused prediction ID with different content is a hard collision failure.
4. Provenance is mandatory; prediction content without an evidence binding is invalid.
5. `known_as_of > predicted_at` is invalid because it would use information learned after the prediction.
6. No outcome-resolution data is required or consumed in this phase.
7. The ledger is append-only from the caller perspective; accepted records are immutable dataclasses.

## Reuse decision

Existing Vault provenance validation requires only `source_type` and `source_ref`, which is insufficient for a prediction-specific audit trail. Existing `EvidenceItem`/`build_evidence_bundle` provides evidence hashes and source metadata but does not define a prediction record or bind a prediction to immutable market snapshot IDs and model version. Phase 3 therefore adds only the missing prediction-ledger contract and reuses the existing evidence/provenance concepts rather than replacing them.

## Out of scope

Calibration, resolution scoring, Brier/log loss, market-vs-model edge, EV, fees, spread, slippage, risk, sizing, backtesting, paper trading, execution, credentials, and live orders.
