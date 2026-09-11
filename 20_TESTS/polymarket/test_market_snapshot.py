"""Phase 1 tests for canonical Polymarket market snapshots."""

from __future__ import annotations

import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "03_IMPLEMENTATION" / "packages"))

import pytest

from polymarket.market_snapshot import (
    MarketLifecycle,
    PriceObservation,
    ResolutionMetadata,
    SnapshotStore,
    PolymarketMarket,
    build_snapshot,
    parse_gamma_market,
)


BASE_MARKET = PolymarketMarket(
    market_id="synthetic-001",
    condition_id="0xsynthetic001",
    question="Will event X happen?",
    description="synthetic",
    category="test",
    market_type="binary",
    outcomes=("Yes", "No"),
    outcome_ids=("yes-token", "no-token"),
    lifecycle=MarketLifecycle.OPEN,
    created_at="2025-01-01T09:00:00Z",
    start_at="2025-01-01T09:00:00Z",
    close_at=None,
    expected_resolution_at="2025-01-03T08:00:00Z",
    resolution_source="synthetic-source",
    resolution_rule_text="Yes if event X happens.",
    cancellation_state=None,
    slug="synthetic-001",
)


def make_snapshot(*, known_as_of="2025-01-01T10:00:00Z", resolution=None):
    return build_snapshot(
        BASE_MARKET,
        snapshot_at=known_as_of,
        acquired_at="2025-01-01T10:01:00Z",
        known_as_of=known_as_of,
        source_type="synthetic_fixture",
        source_ref="fixture://phase1/market-001",
        data_quality="synthetic",
        source_payload_hash="fixture-hash",
        price_observations=(
            PriceObservation("yes-token", "0.40", known_as_of, "synthetic-clob"),
            PriceObservation("no-token", "0.60", known_as_of, "synthetic-clob"),
        ),
        liquidity="1000",
        volume="200",
        resolution=resolution,
    )


def test_snapshot_is_deterministic_and_reloadable(tmp_path):
    first = make_snapshot()
    second = make_snapshot()
    assert first.snapshot_id == second.snapshot_id
    assert first.content_hash == second.content_hash

    store = SnapshotStore(tmp_path)
    path = store.put(first)
    loaded = store.get(first.snapshot_id)
    assert path.exists()
    assert loaded.to_dict() == first.to_dict()


def test_existing_snapshot_cannot_be_mutated(tmp_path):
    first = make_snapshot()
    store = SnapshotStore(tmp_path)
    store.put(first)
    target = tmp_path / f"{first.snapshot_id}.json"
    target.write_text('{"tampered":true}\n', encoding="utf-8")
    with pytest.raises(Exception):
        store.get(first.snapshot_id)


def test_resolution_cannot_cross_known_as_of_boundary():
    late_resolution = ResolutionMetadata(
        status="resolved",
        outcome_ids=("yes-token",),
        resolution_source="synthetic-source",
        resolution_rule_text="Yes if event X happens.",
        known_at="2025-01-03T09:00:00Z",
    )
    with pytest.raises(ValueError, match="known_as_of"):
        make_snapshot(resolution=late_resolution)


def test_resolution_is_allowed_when_known_before_boundary():
    resolution = ResolutionMetadata(
        status="resolved",
        outcome_ids=("yes-token",),
        resolution_source="synthetic-source",
        resolution_rule_text="Yes if event X happens.",
        known_at="2025-01-03T09:00:00Z",
    )
    snapshot = make_snapshot(
        known_as_of="2025-01-03T10:00:00Z", resolution=resolution
    )
    snapshot.verify()


def test_duplicate_snapshot_bytes_are_idempotent(tmp_path):
    snapshot = make_snapshot()
    store = SnapshotStore(tmp_path)
    store.put(snapshot)
    store.put(snapshot)
    assert len(list(tmp_path.glob("*.json"))) == 1


def test_schema_round_trip_is_deterministic():
    snapshot = make_snapshot()
    encoded = json.dumps(snapshot.to_dict(), sort_keys=True, separators=(",", ":"))
    restored = snapshot.from_dict(json.loads(encoded))
    assert restored.to_dict() == snapshot.to_dict()


def test_gamma_parser_handles_documented_json_string_arrays():
    parsed = parse_gamma_market(
        {
            "id": "42",
            "conditionId": "0xabc",
            "question": "Will X happen?",
            "description": "rules",
            "category": "test",
            "marketType": "binary",
            "outcomes": '["Yes", "No"]',
            "outcomePrices": '["0.55", "0.45"]',
            "clobTokenIds": '["yes-id", "no-id"]',
            "active": True,
            "closed": False,
            "startDate": "2025-01-01T00:00:00Z",
            "endDate": "2025-01-10T00:00:00Z",
            "resolutionSource": "official.example",
            "liquidity": "1000",
            "volume": "2000",
        }
    )
    assert parsed["market_id"] == "42"
    assert parsed["outcomes"] == ["Yes", "No"]
    assert parsed["outcome_prices"] == ["0.55", "0.45"]
    assert parsed["outcome_ids"] == ["yes-id", "no-id"]


def test_gamma_parser_does_not_promote_current_resolution_to_history():
    parsed = parse_gamma_market(
        {
            "id": "42",
            "question": "Will X happen?",
            "outcomes": '["Yes", "No"]',
            "outcomePrices": '["1", "0"]',
            "clobTokenIds": '["yes-id", "no-id"]',
            "closed": True,
            "umaResolutionStatus": "resolved",
            "resolvedBy": "0xresolver",
        }
    )
    assert "resolution" not in parsed
    assert parsed["closed"] is True


def test_missing_required_market_fields_fail():
    with pytest.raises(ValueError, match="id and question"):
        parse_gamma_market({"id": "42"})
