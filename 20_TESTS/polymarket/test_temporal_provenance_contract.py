import pytest

from packages.polymarket.temporal_provenance_contract import CaptureInput, ProvenanceRecord


def test_accepts_historically_provenanced_point_before_cutoff():
    record = ProvenanceRecord(
        observed_at="2023-11-14T20:30:00Z",
        known_as_of="2023-11-14T20:35:00Z",
        acquired_at="2023-11-14T20:40:00Z",
        source_ref="fixture://historical",
        knowledge_basis="source_native",
    )
    assert record.is_cutoff_safe("2023-11-14T21:00:00Z") is True


def test_retrospective_acquisition_is_not_cutoff_safe():
    record = ProvenanceRecord(
        observed_at="2023-11-14T20:30:00Z",
        known_as_of="2026-09-12T16:00:00Z",
        acquired_at="2026-09-12T16:00:00Z",
        source_ref="fixture://retrospective",
        knowledge_basis="acquisition_only",
    )
    assert record.is_cutoff_safe("2023-11-14T21:00:00Z") is False


def test_rejects_invalid_timestamp_ordering():
    record = ProvenanceRecord(
        observed_at="2023-11-14T21:00:00Z",
        known_as_of="2023-11-14T20:00:00Z",
        acquired_at="2023-11-14T20:05:00Z",
        source_ref="fixture://invalid",
        knowledge_basis="source_native",
    )
    with pytest.raises(ValueError, match="observed_at cannot be after"):
        record.validate()


def test_source_native_requires_historical_knowledge_timestamp():
    capture = CaptureInput(
        observed_at="2023-11-14T20:30:00Z",
        acquired_at="2026-09-12T16:00:00Z",
        source_ref="fixture://native",
        knowledge_basis="source_native",
    )
    with pytest.raises(ValueError, match="historical_knowledge_at is required"):
        capture.materialize()


def test_acquisition_only_materializes_conservatively():
    capture = CaptureInput(
        observed_at="2023-11-14T20:30:00Z",
        acquired_at="2026-09-12T16:00:00Z",
        source_ref="fixture://retrospective",
        knowledge_basis="acquisition_only",
    )
    record = capture.materialize()
    assert record.known_as_of == record.acquired_at
    assert record.is_cutoff_safe("2023-11-14T21:00:00Z") is False
