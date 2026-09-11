from __future__ import annotations

import math
import pytest

from packages.polymarket import (
    PredictionLedger,
    PredictionProvenance,
    build_prediction,
)


BASE = {
    "market_id": "mkt-1",
    "outcome_id": "YES",
    "probability": 0.62,
    "predicted_at": "2026-01-01T12:00:00Z",
    "known_as_of": "2026-01-01T11:59:00Z",
}


def provenance(**overrides):
    values = {
        "evidence_bundle_hash": "sha256:bundle-1",
        "snapshot_ids": ("PMS-s1",),
        "source_refs": ("fixture://market-1",),
        "source_types": ("fixture",),
        "model_id": "research-model",
        "model_version": "1",
        "prompt_hash": "sha256:prompt-1",
    }
    values.update(overrides)
    return PredictionProvenance(**values)


def test_prediction_id_is_deterministic_and_content_bound():
    a = build_prediction(provenance=provenance(), **BASE)
    b = build_prediction(provenance=provenance(), **BASE)
    assert a == b
    assert a.prediction_id.startswith("PRED-")

    changed = build_prediction(provenance=provenance(), **{**BASE, "probability": 0.63})
    assert changed.prediction_id != a.prediction_id


def test_prediction_requires_known_information_not_after_prediction():
    with pytest.raises(ValueError, match="known_as_of"):
        build_prediction(
            provenance=provenance(),
            **{**BASE, "known_as_of": "2026-01-01T12:00:01Z"},
        )


def test_probability_is_finite_and_bounded():
    for value in (-0.01, 1.01, math.inf, math.nan):
        with pytest.raises(ValueError, match="probability"):
            build_prediction(provenance=provenance(), **{**BASE, "probability": value})


def test_provenance_requires_evidence_snapshot_and_model_identity():
    with pytest.raises(ValueError, match="evidence_bundle_hash"):
        build_prediction(provenance=provenance(evidence_bundle_hash=""), **BASE)
    with pytest.raises(ValueError, match="snapshot_id"):
        build_prediction(provenance=provenance(snapshot_ids=()), **BASE)
    with pytest.raises(ValueError, match="model_id"):
        build_prediction(provenance=provenance(model_id=""), **BASE)


def test_ledger_is_append_only_and_idempotent_for_identical_record():
    record = build_prediction(provenance=provenance(), **BASE)
    ledger = PredictionLedger()
    assert ledger.append(record) == record.prediction_id
    assert ledger.append(record) == record.prediction_id
    assert ledger.records() == (record,)


def test_ledger_rejects_forged_prediction_id():
    record = build_prediction(provenance=provenance(), **BASE)
    conflicting = build_prediction(provenance=provenance(), **{**BASE, "probability": 0.63})
    forged = type(record)(record.prediction_id, conflicting.schema_version, conflicting.market_id, conflicting.outcome_id, conflicting.probability, conflicting.predicted_at, conflicting.known_as_of, conflicting.provenance, conflicting.abstained, conflicting.note)
    ledger = PredictionLedger([record])
    with pytest.raises(ValueError, match="prediction_id"):
        ledger.append(forged)


def test_export_is_stable_and_contains_provenance():
    record = build_prediction(provenance=provenance(), **BASE)
    exported = PredictionLedger([record]).export()
    assert exported[0]["prediction_id"] == record.prediction_id
    assert exported[0]["provenance"]["evidence_bundle_hash"] == "sha256:bundle-1"
    assert exported[0]["provenance"]["snapshot_ids"] == ["PMS-s1"]


def test_abstention_does_not_become_implicit_confidence():
    with pytest.raises(ValueError, match="boundary probability"):
        build_prediction(provenance=provenance(), abstained=True, **BASE)
    record = build_prediction(provenance=provenance(), abstained=True, probability=0.0, **{k: v for k, v in BASE.items() if k != "probability"})
    assert record.abstained is True
