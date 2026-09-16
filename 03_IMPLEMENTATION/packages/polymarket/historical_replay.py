"""Deterministic historical replay over immutable Polymarket snapshots."""
from __future__ import annotations
from dataclasses import dataclass
from typing import Iterable, Optional, Sequence
from .market_snapshot import MarketSnapshot, _parse_datetime

REPLAY_SCHEMA_VERSION = "polymarket-historical-replay.v1"

@dataclass(frozen=True)
class HistoricalPricePoint:
    outcome_id: str
    observed_at: str
    price: str
    requested_fidelity_minutes: Optional[int]
    source_type: str
    source_ref: str
    acquired_at: str
    known_as_of: str

    def validate(self) -> None:
        observed_at = _parse_datetime(self.observed_at, field_name="price.observed_at")
        acquired_at = _parse_datetime(self.acquired_at, field_name="price.acquired_at")
        known_as_of = _parse_datetime(self.known_as_of, field_name="price.known_as_of")
        if not self.outcome_id or not self.price:
            raise ValueError("historical price outcome_id and price are required")
        if not self.source_type or not self.source_ref:
            raise ValueError("historical price source_type and source_ref are required")
        if self.requested_fidelity_minutes is not None and self.requested_fidelity_minutes <= 0:
            raise ValueError("requested_fidelity_minutes must be positive")
        if observed_at > known_as_of:
            raise ValueError("price observation cannot be observed after it is known")
        if acquired_at < observed_at:
            raise ValueError("price acquired_at cannot precede observed_at")

@dataclass(frozen=True)
class ReplayResult:
    replay_schema_version: str
    market_id: str
    as_of: str
    snapshot: Optional[MarketSnapshot]
    price_points: tuple[HistoricalPricePoint, ...]
    missing_state: bool

    @property
    def lifecycle(self) -> Optional[str]:
        return None if self.snapshot is None else self.snapshot.market.lifecycle.value

class HistoricalReplay:
    """Replay only information available by the requested cutoff."""
    def replay(self, *, market_id: str, as_of: str, snapshots: Sequence[MarketSnapshot], price_points: Iterable[HistoricalPricePoint] = ()) -> ReplayResult:
        cutoff = _parse_datetime(as_of, field_name="as_of")
        eligible = []
        for snapshot in snapshots:
            snapshot.verify()
            if snapshot.market.market_id == market_id and self._available(snapshot.snapshot_at, snapshot.acquired_at, snapshot.known_as_of, cutoff):
                eligible.append(snapshot)
        selected = max(eligible, key=lambda item: (_parse_datetime(item.known_as_of, field_name="known_as_of"), _parse_datetime(item.snapshot_at, field_name="snapshot_at"), _parse_datetime(item.acquired_at, field_name="acquired_at"), item.snapshot_id), default=None)

        seen = {}
        for point in price_points:
            point.validate()
            if not self._available(point.observed_at, point.acquired_at, point.known_as_of, cutoff):
                continue
            key = (point.outcome_id, point.observed_at)
            if key in seen and seen[key] != point:
                raise ValueError(f"contradictory historical price point: {point.outcome_id}@{point.observed_at}")
            seen[key] = point
        ordered = sorted(seen.values(), key=lambda item: (_parse_datetime(item.observed_at, field_name="price.observed_at"), item.outcome_id, item.price))
        return ReplayResult(REPLAY_SCHEMA_VERSION, market_id, as_of, selected, tuple(ordered), selected is None)

    @staticmethod
    def _available(observed_at: str, acquired_at: str, known_as_of: str, cutoff) -> bool:
        return all(_parse_datetime(value, field_name="replay timestamp") <= cutoff for value in (observed_at, acquired_at, known_as_of))

__all__ = ["HistoricalPricePoint", "HistoricalReplay", "ReplayResult", "REPLAY_SCHEMA_VERSION"]
