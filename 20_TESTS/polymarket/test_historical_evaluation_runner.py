from datetime import datetime, timezone

import pytest

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
        "probability": 0.62,
        "price": 0.42,
        "resolved_outcome_id": "o1",
        "resolution_known_at": _ts(10),
    }
    record.update(overrides)
    return record


def test_batch_runner_evaluates_rows_and_preserves_source_summary() -> None:
    run = evaluate_historical_dataset([_record()])
    assert run.count == 1
    assert run.source_knowledge_bases == ("source_native",)
    assert run.evaluations[0].raw_edge == pytest.approx(0.20)


def test_batch_runner_accepts_mixed_safe_knowledge_bases() -> None:
    second = _record(market_id="m2", outcome_id="o2", knowledge_basis="event_derived")
    run = evaluate_historical_dataset([_record(), second])
    assert run.count == 2
    assert run.source_knowledge_bases == ("event_derived", "source_native")


def test_duplicate_prediction_key_is_rejected() -> None:
    with pytest.raises(ValueError, match="duplicate historical evaluation prediction key"):
        evaluate_historical_dataset([_record(), _record()])


def test_retrospective_row_is_rejected_by_batch_boundary() -> None:
    with pytest.raises(ValueError, match="requires source_native or event_derived"):
        evaluate_historical_dataset([_record(knowledge_basis="acquisition_only")])


def test_post_cutoff_row_is_rejected_by_batch_boundary() -> None:
    with pytest.raises(ValueError, match="temporally eligible"):
        evaluate_historical_dataset([_record(acquired_at=_ts(5))])
