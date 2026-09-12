from datetime import datetime, timezone

import pytest

from packages.polymarket.historical_evaluation_dataset import load_historical_evaluation_dataset


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


def test_valid_historical_row_loads_and_evaluates() -> None:
    dataset = load_historical_evaluation_dataset([_record()])
    assert len(dataset) == 1
    evaluation = dataset[0].evaluate()
    assert evaluation.observed_price == 0.42
    assert evaluation.raw_edge == pytest.approx(0.20)


def test_empty_dataset_is_rejected() -> None:
    with pytest.raises(ValueError, match="dataset is empty"):
        load_historical_evaluation_dataset([])


def test_missing_historical_knowledge_time_fails_closed() -> None:
    record = _record(knowledge_basis="source_native")
    record.pop("known_as_of")
    with pytest.raises(ValueError, match="known_as_of is required"):
        load_historical_evaluation_dataset([record])


def test_retrospective_acquisition_only_is_not_an_evaluation_dataset_row() -> None:
    with pytest.raises(ValueError, match="requires source_native or event_derived"):
        load_historical_evaluation_dataset([_record(knowledge_basis="acquisition_only")])


def test_resolution_known_before_prediction_cutoff_is_rejected() -> None:
    with pytest.raises(ValueError, match="resolution cannot be known"):
        load_historical_evaluation_dataset([_record(resolution_known_at=_ts(4))])


def test_point_acquired_after_prediction_cutoff_is_rejected() -> None:
    with pytest.raises(ValueError, match="temporally eligible"):
        load_historical_evaluation_dataset([_record(acquired_at=_ts(5))])


def test_observation_after_prediction_cutoff_is_rejected() -> None:
    with pytest.raises(ValueError, match="temporally eligible"):
        load_historical_evaluation_dataset([_record(observed_at=_ts(5))])


def test_invalid_temporal_order_is_rejected() -> None:
    with pytest.raises(ValueError, match="observed_at cannot be after known_as_of"):
        load_historical_evaluation_dataset([_record(known_as_of=_ts(1))])
