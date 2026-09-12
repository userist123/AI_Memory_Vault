from datetime import datetime, timezone

import pytest

from packages.polymarket.historical_evaluation_calibration import (
    project_calibration_observations,
    score_historical_evaluation_calibration,
)
from packages.polymarket.historical_evaluation_runner import evaluate_historical_dataset


def _ts(day: int) -> str:
    return datetime(2026, 1, day, 12, tzinfo=timezone.utc).isoformat().replace("+00:00", "Z")


def _record(**overrides):
    record = {
        "market_id": "m1",
        "outcome_id": "o1",
        "observed_at": _ts(1),
        "known_as_of": _ts(2),
        "acquired_at": _ts(3),
        "source_ref": "source://prices/o1",
        "knowledge_basis": "source_native",
        "source_timestamp_semantics": "source_published_at",
        "prediction_known_as_of": _ts(4),
        "probability": 0.8,
        "price": 0.42,
        "resolved_outcome_id": "o1",
        "resolution_known_at": _ts(10),
    }
    record.update(overrides)
    return record


def test_projection_preserves_prediction_and_resolution_times() -> None:
    run = evaluate_historical_dataset([_record()])
    observations = project_calibration_observations(run)
    assert len(observations) == 1
    assert observations[0].probability == pytest.approx(0.8)
    assert observations[0].target == 1
    assert observations[0].prediction_known_as_of == _ts(4)
    assert observations[0].resolution_known_at == _ts(10)


def test_projection_marks_non_winning_outcome_as_zero() -> None:
    run = evaluate_historical_dataset([_record(resolved_outcome_id="o2")])
    observations = project_calibration_observations(run)
    assert observations[0].target == 0


def test_calibration_score_uses_existing_deterministic_scorer() -> None:
    first = _record()
    second = _record(
        market_id="m2",
        outcome_id="o2",
        probability=0.2,
        resolved_outcome_id="o3",
    )
    run = evaluate_historical_dataset([first, second])
    report = score_historical_evaluation_calibration(run, bin_count=2)
    assert report.observation_count == 2
    assert report.brier_score == pytest.approx((0.2**2 + 0.2**2) / 2)


def test_projection_reuses_runner_validation_and_rejects_duplicate_rows() -> None:
    run = evaluate_historical_dataset([_record()])
    broken = type(run)(
        evaluations=run.evaluations,
        rows=run.rows + run.rows,
        source_knowledge_bases=run.source_knowledge_bases,
    )
    with pytest.raises(ValueError, match="duplicate historical evaluation prediction key"):
        project_calibration_observations(broken)


def test_post_cutoff_history_cannot_reach_calibration_projection() -> None:
    with pytest.raises(ValueError, match="temporally eligible"):
        evaluate_historical_dataset([_record(acquired_at=_ts(5))])
