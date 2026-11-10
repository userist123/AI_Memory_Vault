"""Canonical Polymarket market model and immutable historical snapshots."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field, replace
from datetime import datetime, timezone
from enum import Enum
import hashlib
import json
from pathlib import Path
from typing import Any, Mapping, Optional

CANONICAL_SCHEMA_VERSION = "polymarket-market-snapshot.v1"
DATA_QUALITY_VALUES = frozenset({"verified", "synthetic", "unverified", "contradictory"})


def _parse_datetime(value: str, *, field_name: str) -> datetime:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{field_name} must be a non-empty ISO-8601 string")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise ValueError(f"{field_name} must be ISO-8601: {value!r}") from exc
    if parsed.tzinfo is None:
        raise ValueError(f"{field_name} must include an explicit timezone")
    return parsed.astimezone(timezone.utc)


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_canonical(value: Any) -> str:
    return hashlib.sha256(canonical_json(value).encode("utf-8")).hexdigest()


class MarketLifecycle(str, Enum):
    OPEN = "open"
    CLOSED = "closed"
    RESOLVED = "resolved"
    CANCELLED = "cancelled"
    INVALIDATED = "invalidated"
    UNKNOWN = "unknown"


@dataclass(frozen=True)
class PriceObservation:
    outcome_id: str
    price: str
    observed_at: str
    source: str


@dataclass(frozen=True)
class ResolutionMetadata:
    status: str
    outcome_ids: tuple[str, ...]
    resolution_source: Optional[str]
    resolution_rule_text: Optional[str]
    known_at: str

    def validate(self, known_as_of: datetime) -> None:
        if _parse_datetime(self.known_at, field_name="resolution.known_at") > known_as_of:
            raise ValueError("resolution information is known after known_as_of")
        if not self.status:
            raise ValueError("resolution.status cannot be empty")


@dataclass(frozen=True)
class PolymarketMarket:
    market_id: str
    condition_id: Optional[str]
    question: str
    description: Optional[str]
    category: Optional[str]
    market_type: Optional[str]
    outcomes: tuple[str, ...]
    outcome_ids: tuple[str, ...]
    lifecycle: MarketLifecycle
    created_at: Optional[str]
    start_at: Optional[str]
    close_at: Optional[str]
    expected_resolution_at: Optional[str]
    resolution_source: Optional[str]
    resolution_rule_text: Optional[str]
    cancellation_state: Optional[str]
    slug: Optional[str]

    def validate(self) -> None:
        if not self.market_id.strip():
            raise ValueError("market_id is required")
        if not self.question.strip():
            raise ValueError("question is required")
        if not self.outcomes:
            raise ValueError("at least one outcome is required")
        if len(self.outcome_ids) != len(self.outcomes):
            raise ValueError("outcome_ids and outcomes must have equal length")
        for name, value in (("created_at", self.created_at), ("start_at", self.start_at),
                            ("close_at", self.close_at), ("expected_resolution_at", self.expected_resolution_at)):
            if value is not None:
                _parse_datetime(value, field_name=name)


@dataclass(frozen=True)
class MarketSnapshot:
    snapshot_id: str
    schema_version: str
    market: PolymarketMarket
    snapshot_at: str
    acquired_at: str
    known_as_of: str
    price_observations: tuple[PriceObservation, ...]
    liquidity: Optional[str]
    volume: Optional[str]
    source_type: str
    source_ref: str
    data_quality: str
    source_payload_hash: str
    resolution: Optional[ResolutionMetadata] = None
    extra_source_metadata: Mapping[str, Any] = field(default_factory=dict)
    content_hash: str = ""

    def canonical_payload(self) -> dict[str, Any]:
        return {
            "snapshot_id": self.snapshot_id,
            "schema_version": self.schema_version,
            "market": {
                **asdict(self.market),
                "lifecycle": self.market.lifecycle.value,
                "outcomes": list(self.market.outcomes),
                "outcome_ids": list(self.market.outcome_ids),
            },
            "snapshot_at": self.snapshot_at,
            "acquired_at": self.acquired_at,
            "known_as_of": self.known_as_of,
            "price_observations": [asdict(x) for x in self.price_observations],
            "liquidity": self.liquidity,
            "volume": self.volume,
            "source_type": self.source_type,
            "source_ref": self.source_ref,
            "data_quality": self.data_quality,
            "source_payload_hash": self.source_payload_hash,
            "resolution": None if self.resolution is None else {
                **asdict(self.resolution), "outcome_ids": list(self.resolution.outcome_ids)
            },
            "extra_source_metadata": dict(self.extra_source_metadata),
        }

    def recompute_hash(self) -> str:
        payload = self.canonical_payload()
        payload.pop("snapshot_id", None)
        return sha256_canonical(payload)

    def _validate_core(self) -> None:
        self.market.validate()
        snapshot_at = _parse_datetime(self.snapshot_at, field_name="snapshot_at")
        acquired_at = _parse_datetime(self.acquired_at, field_name="acquired_at")
        known_as_of = _parse_datetime(self.known_as_of, field_name="known_as_of")
        if acquired_at < snapshot_at:
            raise ValueError("acquired_at cannot precede snapshot_at")
        if self.schema_version != CANONICAL_SCHEMA_VERSION:
            raise ValueError(f"unsupported schema version: {self.schema_version}")
        if self.data_quality not in DATA_QUALITY_VALUES:
            raise ValueError(f"unknown data_quality: {self.data_quality}")
        if not self.source_type or not self.source_ref:
            raise ValueError("source_type and source_ref are required")
        for obs in self.price_observations:
            _parse_datetime(obs.observed_at, field_name="price.observed_at")
            if not obs.outcome_id or not obs.price or not obs.source:
                raise ValueError("price observation fields are required")
        if self.resolution is not None:
            self.resolution.validate(known_as_of)

    def verify(self) -> None:
        self._validate_core()
        if self.content_hash != self.recompute_hash():
            raise ValueError("snapshot content_hash does not match canonical payload")

    def to_dict(self) -> dict[str, Any]:
        self.verify()
        payload = self.canonical_payload()
        payload["content_hash"] = self.content_hash
        return payload

    @classmethod
    def from_dict(cls, payload: Mapping[str, Any]) -> "MarketSnapshot":
        try:
            market_raw = dict(payload["market"])
            market_raw["lifecycle"] = MarketLifecycle(market_raw["lifecycle"])
            market_raw["outcomes"] = tuple(market_raw["outcomes"])
            market_raw["outcome_ids"] = tuple(market_raw["outcome_ids"])
            resolution_raw = payload.get("resolution")
            resolution = None if resolution_raw is None else ResolutionMetadata(
                status=str(resolution_raw["status"]),
                outcome_ids=tuple(resolution_raw["outcome_ids"]),
                resolution_source=resolution_raw.get("resolution_source"),
                resolution_rule_text=resolution_raw.get("resolution_rule_text"),
                known_at=str(resolution_raw["known_at"]),
            )
            snapshot = cls(
                snapshot_id=str(payload["snapshot_id"]),
                schema_version=str(payload["schema_version"]),
                market=PolymarketMarket(**market_raw),
                snapshot_at=str(payload["snapshot_at"]),
                acquired_at=str(payload["acquired_at"]),
                known_as_of=str(payload["known_as_of"]),
                price_observations=tuple(PriceObservation(**x) for x in payload.get("price_observations", [])),
                liquidity=payload.get("liquidity"),
                volume=payload.get("volume"),
                source_type=str(payload["source_type"]),
                source_ref=str(payload["source_ref"]),
                data_quality=str(payload["data_quality"]),
                source_payload_hash=str(payload["source_payload_hash"]),
                resolution=resolution,
                extra_source_metadata=dict(payload.get("extra_source_metadata") or {}),
                content_hash=str(payload["content_hash"]),
            )
        except (KeyError, TypeError, ValueError) as exc:
            raise ValueError("malformed market snapshot payload") from exc
        snapshot.verify()
        return snapshot


_ALLOWED_TRANSITIONS = {
    MarketLifecycle.UNKNOWN: frozenset(MarketLifecycle),
    MarketLifecycle.OPEN: frozenset({MarketLifecycle.OPEN, MarketLifecycle.CLOSED, MarketLifecycle.CANCELLED, MarketLifecycle.INVALIDATED}),
    MarketLifecycle.CLOSED: frozenset({MarketLifecycle.CLOSED, MarketLifecycle.RESOLVED, MarketLifecycle.CANCELLED, MarketLifecycle.INVALIDATED}),
    MarketLifecycle.RESOLVED: frozenset({MarketLifecycle.RESOLVED}),
    MarketLifecycle.CANCELLED: frozenset({MarketLifecycle.CANCELLED}),
    MarketLifecycle.INVALIDATED: frozenset({MarketLifecycle.INVALIDATED}),
}


def validate_snapshot_transition(previous: MarketSnapshot, current: MarketSnapshot) -> None:
    """Validate monotonic identity/time/lifecycle progression between snapshots."""
    previous.verify()
    current.verify()
    if previous.market.market_id != current.market.market_id:
        raise ValueError("snapshot transition market_id mismatch")
    if previous.market.condition_id != current.market.condition_id:
        raise ValueError("snapshot transition condition_id mismatch")
    if _parse_datetime(current.snapshot_at, field_name="snapshot_at") <= _parse_datetime(previous.snapshot_at, field_name="snapshot_at"):
        raise ValueError("snapshot_at must move forward")
    if current.market.lifecycle not in _ALLOWED_TRANSITIONS[previous.market.lifecycle]:
        raise ValueError(
            f"invalid market lifecycle transition: {previous.market.lifecycle.value} -> {current.market.lifecycle.value}"
        )
    if previous.market.lifecycle in {MarketLifecycle.RESOLVED, MarketLifecycle.CANCELLED, MarketLifecycle.INVALIDATED}:
        if current.market.lifecycle != previous.market.lifecycle:
            raise ValueError("terminal market lifecycle cannot transition")


class SnapshotStore:
    """Content-addressed JSON store. Existing snapshot bytes are never overwritten."""

    def __init__(self, root: Path) -> None:
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)

    def put(self, snapshot: MarketSnapshot) -> Path:
        snapshot.verify()
        target = self.root / f"{snapshot.snapshot_id}.json"
        serialized = canonical_json(snapshot.to_dict()) + "\n"
        if target.exists():
            if target.read_text(encoding="utf-8") != serialized:
                raise ValueError(f"immutable snapshot collision: {snapshot.snapshot_id}")
            return target
        target.write_text(serialized, encoding="utf-8")
        return target

    def get(self, snapshot_id: str) -> MarketSnapshot:
        target = self.root / f"{snapshot_id}.json"
        if not target.exists():
            raise KeyError(snapshot_id)
        try:
            payload = json.loads(target.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError("malformed market snapshot storage") from exc
        if not isinstance(payload, dict):
            raise ValueError("malformed market snapshot storage")
        stored_hash = payload.get("content_hash")
        if not isinstance(stored_hash, str) or not stored_hash:
            raise ValueError("snapshot content_hash is missing")
        canonical_payload = dict(payload)
        canonical_payload.pop("snapshot_id", None)
        canonical_payload.pop("content_hash", None)
        if sha256_canonical(canonical_payload) != stored_hash:
            raise ValueError("snapshot content_hash does not match stored bytes")
        return MarketSnapshot.from_dict(payload)


def build_snapshot(
    market: PolymarketMarket,
    *,
    snapshot_at: str,
    acquired_at: str,
    known_as_of: str,
    source_type: str,
    source_ref: str,
    data_quality: str,
    source_payload_hash: str,
    price_observations: tuple[PriceObservation, ...] = (),
    liquidity: Optional[str] = None,
    volume: Optional[str] = None,
    resolution: Optional[ResolutionMetadata] = None,
    extra_source_metadata: Optional[Mapping[str, Any]] = None,
) -> MarketSnapshot:
    provisional = MarketSnapshot(
        snapshot_id="pending",
        schema_version=CANONICAL_SCHEMA_VERSION,
        market=market,
        snapshot_at=snapshot_at,
        acquired_at=acquired_at,
        known_as_of=known_as_of,
        price_observations=tuple(price_observations),
        liquidity=liquidity,
        volume=volume,
        source_type=source_type,
        source_ref=source_ref,
        data_quality=data_quality,
        source_payload_hash=source_payload_hash,
        resolution=resolution,
        extra_source_metadata=dict(extra_source_metadata or {}),
    )
    provisional._validate_core()
    content_hash = provisional.recompute_hash()
    snapshot = replace(provisional, snapshot_id=f"PMS-{content_hash[:24]}", content_hash=content_hash)
    snapshot.verify()
    return snapshot


def parse_gamma_market(payload: Mapping[str, Any]) -> dict[str, Any]:
    """Parse provider-verified fields without inferring historical resolution."""
    if not payload.get("id") or not payload.get("question"):
        raise ValueError("Gamma market requires id and question")

    def decode_array(value: Any, field_name: str) -> list[str]:
        if value is None:
            return []
        decoded = json.loads(value) if isinstance(value, str) else value
        if not isinstance(decoded, list):
            raise ValueError(f"{field_name} must be an array or JSON array string")
        return [str(item) for item in decoded]

    return {
        "market_id": str(payload["id"]),
        "condition_id": str(payload["conditionId"]) if payload.get("conditionId") else None,
        "question": str(payload["question"]),
        "description": payload.get("description"),
        "category": payload.get("category"),
        "market_type": payload.get("marketType"),
        "outcomes": decode_array(payload.get("outcomes"), "outcomes"),
        "outcome_prices": decode_array(payload.get("outcomePrices"), "outcomePrices"),
        "outcome_ids": decode_array(payload.get("clobTokenIds"), "clobTokenIds"),
        "active": payload.get("active"),
        "closed": payload.get("closed"),
        "created_at": payload.get("createdAt") or payload.get("creationDate"),
        "start_at": payload.get("startDate") or payload.get("startDateIso"),
        "close_at": payload.get("closedTime"),
        "expected_resolution_at": payload.get("endDate") or payload.get("endDateIso"),
        "resolution_source": payload.get("resolutionSource"),
        "liquidity": payload.get("liquidityClob", payload.get("liquidity")),
        "volume": payload.get("volumeClob", payload.get("volume")),
        "slug": payload.get("slug"),
        "source_payload": dict(payload),
    }
