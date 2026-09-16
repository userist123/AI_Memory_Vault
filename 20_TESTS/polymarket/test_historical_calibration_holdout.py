from __future__ import annotations

import pytest

from polymarket.historical_calibration_holdout import split_historical_calibration_holdout
from polymarket.historical_evaluation_runner import evaluate_historical_dataset


def _record(day: int, probability: float, resolved: str = "YES") -> dict[str, object]:
    stamp = f"2026-01-{day:02d}T10:00:00Z"
    return {
        "market_id": f"m{day}",
        "outcome_id": "YES",
        "probability": probability,
        "prediction_known_as_of": stamp,
        "observed_at": f"2026-01-{day:02d}T09:00:00Z",
        "known_as_of": f"2026-01-{day:02d}T09:30:00Z",
        "acquired_at": stamp,
        "source_ref": f"fixture-{day}",
        "knowledge_basis": "source_native",
        "source_timestamp_semantics": "source publication time",
        "resolved_outcome_id": resolved,
        "resolution_known_at": "2026-02-01T10:00:00Z",
        "price": 0.5,
    }


def test_holdout_is_temporally_disjoint_and_deterministic() -> None:
    run = evaluate_historical_dataset(
        [_record(1, 0.2), _record(2, 0.4), _record(3, 0.8)]
    )
    split = split_historical_calibration_holdout(
        run, boundary_exclusive="2026-01-03T00:00:00Z"
    )

    assert split.calibration_count == 2
    assert split.holdout_count == 1
    assert split.calibration.rows[-1].prediction.known_as_of == "2026-01-02T10:00:00Z"
    assert split.holdout.rows[0].prediction.known_as_of == "2026-01-03T10:00:00Z"
    assert split.calibration.source_knowledge_bases == ("source_native",)
    assert split.holdout.source_knowledge_bases == ("source_native",)

    second = split_historical_calibration_holdout(
        run, boundary_exclusive="2026-01-03T00:00:00Z"
    )
    assert split == second


def test_duplicate_prediction_key_is_rejected_before_holdout() -> None:
    run = evaluate_historical_dataset([_record(1, 0.2), _record(2, 0.4)])
    duplicated = run.__class__(
        evaluations=(run.evaluations[0], run.evaluations[0]),
        rows=(run.rows[0], run.rows[0]),
        source_knowledge_bases=run.source_knowledge_bases,
    )

    with pytest.raises(ValueError, match="duplicate historical evaluation prediction key"):
        split_historical_calibration_holdout(
            duplicated, boundary_exclusive="2026-01-02T00:00:00Z"
        )


def test_boundary_must_create_two_non_empty_segments() -> None:
    run = evaluate_historical_dataset([_record(1, 0.2), _record(2, 0.4)])
    with pytest.raises(ValueError, match="non-empty calibration and holdout"):
        split_historical_calibration_holdout(
            run, boundary_exclusive="2026-01-05T00:00:00Z"
        )


def test_timezone_is_required() -> None:
    run = evaluate_historical_dataset([_record(1, 0.2), _record(2, 0.4)])
    with pytest.raises(ValueError, match="timestamp must include timezone"):
        split_historical_calibration_holdout(run, boundary_exclusive="2026-01-02T00:00:00")
