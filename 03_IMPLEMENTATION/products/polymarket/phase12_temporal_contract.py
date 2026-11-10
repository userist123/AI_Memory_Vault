"""Deterministic temporal-leakage contract for model historical replay."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime

from .market_snapshot import _parse_datetime

TEMPORAL_CONTRACT_SCHEMA_VERSION = "polymarket-phase12-temporal-contract.v1"


def _ts(value: str, field_name: str) -> datetime:
    return _parse_datetime(value, field_name=field_name)


@dataclass(frozen=True)
class TemporalReplayCase:
    prediction_market_id: str
    bundle_market_id: str
    prediction_cutoff: str
    observation_at: str
    observation_known_as_of: str
    observation_acquired_at: str
    resolution_known_at: str


def validate_temporal_replay_case(case: TemporalReplayCase) -> None:
    """Reject any case that cannot prove information availability before cutoff."""
    if not case.prediction_market_id or not case.bundle_market_id:
        raise ValueError("market IDs are required")
    if case.prediction_market_id != case.bundle_market_id:
        raise ValueError("prediction market_id does not match historical bundle market_id")

    cutoff = _ts(case.prediction_cutoff, "prediction_cutoff")
    observed = _ts(case.observation_at, "observation_at")
    known = _ts(case.observation_known_as_of, "observation_known_as_of")
    acquired = _ts(case.observation_acquired_at, "observation_acquired_at")
    resolution_known = _ts(case.resolution_known_at, "resolution_known_at")

    if observed > cutoff:
        raise ValueError("historical observation is after prediction cutoff")
    if known > cutoff:
        raise ValueError("historical observation was known after prediction cutoff")
    if acquired > cutoff:
        raise ValueError("historical observation was acquired after prediction cutoff")
    if resolution_known <= cutoff:
        raise ValueError("terminal resolution was known at or before prediction cutoff")
    if observed > known:
        raise ValueError("historical observation cannot be after its known_as_of")
    if acquired < observed:
        raise ValueError("historical observation cannot be acquired before observation time")


__all__ = ["TEMPORAL_CONTRACT_SCHEMA_VERSION", "TemporalReplayCase", "validate_temporal_replay_case"]
