from datetime import datetime, timezone

import pytest

from packages.polymarket.historical_paper_replay import HistoricalTapePoint
from packages.polymarket.temporal_provenance_adapter import attach_provenance


def _ts(day: int) -> str:
    return datetime(2026, 1, day, 12, tzinfo=timezone.utc).isoformat().replace("+00:00", "Z")


def _point(observed_day: int = 1) -> HistoricalTapePoint:
    return HistoricalTapePoint(
        market_id="m1",
        outcome_id="o1",
        observed_at=_ts(observed_day),
        price=0.42,
        source_ref="source://prices/o1",
    )


def test_retrospective_capture_is_acquisition_only_and_not_cutoff_safe() -> None:
    captured = attach_provenance(
        _point(1),
        acquired_at=_ts(10),
        knowledge_basis="acquisition_only",
    )

    assert captured.provenance.known_as_of == _ts(10)
    assert captured.provenance.acquired_at == _ts(10)
    assert captured.provenance.is_cutoff_safe(_ts(9)) is False


def test_historical_source_requires_explicit_knowledge_time() -> None:
    with pytest.raises(ValueError, match="historical_knowledge_at is required"):
        attach_provenance(
            _point(1),
            acquired_at=_ts(10),
            knowledge_basis="source_native",
        )


def test_historically_provenanced_capture_converts_to_evaluator_point() -> None:
    captured = attach_provenance(
        _point(1),
        acquired_at=_ts(4),
        knowledge_basis="source_native",
        historical_knowledge_at=_ts(3),
        source_timestamp_semantics="source_published_at",
    )

    evaluator_point = captured.as_temporal_tape_point()
    evaluator_point.validate()
    assert evaluator_point.known_as_of == _ts(3)
    assert evaluator_point.acquired_at == _ts(4)


def test_provenance_source_and_observation_must_match_tape_point() -> None:
    captured = attach_provenance(
        _point(1),
        acquired_at=_ts(2),
        knowledge_basis="acquisition_only",
    )
    assert captured.provenance.source_ref == captured.tape_point.source_ref
    assert captured.provenance.observed_at == captured.tape_point.observed_at
