"""Canonical Polymarket market model and immutable historical snapshots.

This module deliberately keeps the domain boundary separate from retrieval and memory.
It uses the Vault's existing provenance field contract and SHA-256 canonical hashing
convention, while refusing to infer historical resolution information from a current
provider response without an explicit information-availability timestamp.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
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
    normalized = value.replace("Z", "+00:00")
    try:
        parsed = datetime.fromisoformat(normalized)
    except ValueError as exc:
        raise ValueError(f"{field_name} must be ISO-8601: {value!r}") from exc
    if parsed.tzinfo is None:
        raise ValueError(f"{field_name} must include an explicit timezone")
    return parsed.astimezone(timezone.utc)


def _iso(value: datetime) -> str:
    if value.tzinfo is None:
        raise ValueError("datetime must include timezone")
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def canonical_json(value: Any) -> str:
    """Serialize data deterministically for content addressing."""
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
        known = _parse_datetime(self.known_at, field_name="resolution.known_at")
        if known > known_as_of:
            raise ValueError(
                "resolution information is known after the snapshot known_as_of boundary"
            )
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
        if len(self.outcomes) == 0:
            raise ValueError("at least one outcome is required")
        if len(self.outcome_ids) != len(self.outcomes):
            raise ValueError("outcome_ids and outcomes must have equal length")
        for field_name, value in (
            ("created_at", self.created_at),
            ("start_at", self.start_at),
            ("close_at", self.close_at),
            ("expected_resolution_at", self.expected_resolution_at),
        ):
            if value is not None:
                _parse_datetime(value, field_name=field_name)


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
    content_hash: str = field(default="", compare=True)

    def canonical_payload(self) -> dict[str, Any]:
        payload = asdict(self)
        payload.pop("content_hash", None)
        payload["market"]["lifecycle"] = self.market.lifecycle.value
        payload["price_observations"] = [
            asdict(obs) for obs in self.price_observations
        ]
        if self.resolution is not None:
            payload["resolution"]["outcome_ids"] = list(self.resolution.outcome_ids)
        payload["extra_source_metadata"] = dict(self.extra_source_metadata)
        return payload

    def recompute_hash(self) -> str:
        return sha256_canonical(self.canonical_payload())

    def verify(self) -> None:
        self.market.validate()
        known_as_of = _parse_datetime(self.known_as_of, field_name="known_as_of")
        snapshot_at = _parse_datetime(self.snapshot_at, field_name="snapshot_at")
        acquired_at = _parse_datetime(self.acquired_at, field_name="acquired_at")
        if acquired_at < snapshot_at:
            raise ValueError("acquired_at cannot precede snapshot_at")
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
        if self.content_hash != self.recompute_hash():
            raise ValueError("snapshot content_hash does not match canonical payload")

    def to_dict(self) -> dict[str, Any]:
        self.verify()
        payload = self.canonical_payload()
        payload["content_hash"] = self.content_hash
        payload["market"]["lifecycle"] = self.market.lifecycle.value
        payload["resolution"] = (
            None
            if self.resolution is None
            else {
                **asdict(self.resolution),
                "outcome_ids": list(self.resolution.outcome_ids),
            }
        )
        return payload

    @classmethod
    def from_dict(cls, payload: Mapping[str, Any]) -> "MarketSnapshot":
        market_raw = dict(payload["market"])
        market_raw["lifecycle"] = MarketLifecycle(market_raw["lifecycle"])
        market_raw["outcomes"] = tuple(market_raw["outcomes"])
        market_raw["outcome_ids"] = tuple(market_raw["outcome_ids"])
        market = PolymarketMarket(**market_raw)
        observations = tuple(
            PriceObservation(**dict(item)) for item in payload.get("price_observations", [])
        )
        resolution_raw = payload.get("resolution")
        resolution = None
        if resolution_raw is not None:
            resolution_data = dict(resolution_raw)
            resolution_data["outcome_ids"] = tuple(resolution_data["outcome_ids"])
            resolution = ResolutionMetadata(**resolution_data)
        snapshot = cls(
            snapshot_id=str(payload["snapshot_id"]),
            schema_version=str(payload["schema_version"]),
            market=market,
            snapshot_at=str(payload["snapshot_at"]),
            acquired_at=str(payload["acquired_at"]),
            known_as_of=str(payload["known_as_of"]),
            price_observations=observations,
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
        snapshot.verify()
        if not snapshot.schema_version == CANONICAL_SCHEMA_VERSION:
            raise ValueError(f"unsupported schema version: {snapshot.schema_version}")
        return snapshot


class SnapshotStore:
    """Content-addressed JSON store; existing snapshots are never overwritten."""

    def __init__(self, root: Path) -> None:
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)

    def put(self, snapshot: MarketSnapshot) -> Path:
        snapshot.verify()
        target = self.root / f"{snapshot.snapshot_id}.json"
        serialized = canonical_json(snapshot.to_dict()) + "\n"
        if target.exists():
            existing = target.read_text(encoding="utf-8")
            if existing != serialized:
                raise ValueError(f"immutable snapshot collision: {snapshot.snapshot_id}")
            return target
        target.write_text(serialized, encoding="utf-8")
        return target

    def get(self, snapshot_id: str) -> MarketSnapshot:
        target = self.root / f"{snapshot_id}.json"
        if not target.exists():
            raise KeyError(snapshot_id)
        return MarketSnapshot.from_dict(json.loads(target.read_text(encoding="utf-8")))


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
    market.validate()
    _parse_datetime(snapshot_at, field_name="snapshot_at")
    _parse_datetime(acquired_at, field_name="acquired_at")
    known = _parse_datetime(known_as_of, field_name="known_as_of")
    if not source_type or not source_ref:
        raise ValueError("source_type and source_ref are required")
    if data_quality not in DATA_QUALITY_VALUES:
        raise ValueError(f"unknown data_quality: {data_quality}")
    if resolution is not None:
        resolution.validate(known)

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
        content_hash="",
    )
    content_hash = provisional.recompute_hash()
    snapshot_id = f"PMS-{content_hash[:24]}"
    snapshot = MarketSnapshot(
        **{**asdict(provisional), "snapshot_id": snapshot_id, "content_hash": content_hash}
    )
    snapshot.verify()
    return snapshot


def parse_gamma_market(payload: Mapping[str, Any]) -> dict[str, Any]:
    """Parse only provider-verified market fields; preserve unknowns separately.

    The current official Gamma market contract exposes `id`, `question`, `conditionId`,
    `description`, `category`, `marketType`, `outcomes`, `outcomePrices`, lifecycle/date
    fields, `resolutionSource`, `liquidity`, `volume`, and CLOB token IDs. Provider
    fields that are resolution outcomes are intentionally *not* promoted to historical
    resolution metadata without an explicit `known_at` supplied by the caller.
    """
    required = ("id", "question")
    missing = [key for key in required if not payload.get(key)]
    if missing:
        raise ValueError(f"Gamma market missing required fields: {missing}")

    outcomes = payload.get("outcomes")
    outcome_prices = payload.get("outcomePrices")
    clob_token_ids = payload.get("clobTokenIds")

    def decode_array(value: Any, field_name: str) -> list[str]:
        if value is None:
            return []
        if isinstance(value, str):
            try:
                decoded = json.loads(value)
            except json.JSONDecodeError as exc:
                raise ValueError(f"{field_name} is not valid JSON") from exc
        else:
            decoded = value
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
        "outcomes": decode_array(outcomes, "outcomes"),
        "outcome_prices": decode_array(outcome_prices, "outcomePrices"),
        "outcome_ids": decode_array(clob_token_ids, "clobTokenIds"),
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
