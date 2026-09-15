from __future__ import annotations

import pytest

from packages.polymarket.phase12_temporal_contract import TemporalReplayCase, validate_temporal_replay_case


def _case(**overrides: str) -> TemporalReplayCase:
    values = {
        "prediction_market_id": "market-1",
        "bundle_market_id": "market-1",
        "prediction_cutoff": "2026-01-01T11:00:00Z",
        "observation_at": "2026-01-01T10:00:00Z",
        "observation_known_as_of": "2026-01-01T10:05:00Z",
        "observation_acquired_at": "2026-01-01T10:06:00Z",
        "resolution_known_at": "2026-01-02T12:00:00Z",
    }
    values.update(overrides)
    return TemporalReplayCase(**values)


def test_valid_case_is_accepted() -> None:
    validate_temporal_replay_case(_case())


def test_market_identity_mismatch_is_rejected() -> None:
    with pytest.raises(ValueError, match="market_id"):
        validate_temporal_replay_case(_case(bundle_market_id="market-2"))


def test_observation_after_cutoff_is_rejected() -> None:
    with pytest.raises(ValueError, match="after prediction cutoff"):
        validate_temporal_replay_case(_case(observation_at="2026-01-01T11:00:01Z"))


def test_post_cutoff_known_as_of_is_rejected() -> None:
    with pytest.raises(ValueError, match="known after prediction cutoff"):
        validate_temporal_replay_case(_case(observation_known_as_of="2026-01-01T11:00:01Z"))


def test_post_cutoff_acquisition_is_rejected() -> None:
    with pytest.raises(ValueError, match="acquired after prediction cutoff"):
        validate_temporal_replay_case(_case(observation_acquired_at="2026-01-01T11:00:01Z"))


def test_resolution_known_at_cutoff_is_rejected() -> None:
    with pytest.raises(ValueError, match="resolution was known"):
        validate_temporal_replay_case(_case(resolution_known_at="2026-01-01T11:00:00Z"))


def test_observation_cannot_be_after_known_as_of() -> None:
    with pytest.raises(ValueError, match="cannot be after its known_as_of"):
        validate_temporal_replay_case(_case(observation_at="2026-01-01T10:10:00Z", observation_known_as_of="2026-01-01T10:05:00Z"))


def test_acquisition_cannot_precede_observation() -> None:
    with pytest.raises(ValueError, match="acquired before observation"):
        validate_temporal_replay_case(_case(observation_acquired_at="2026-01-01T09:59:00Z"))
