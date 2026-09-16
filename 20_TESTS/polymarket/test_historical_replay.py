"""Phase 2 deterministic historical replay tests."""
from pathlib import Path
import sys

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "03_IMPLEMENTATION" / "packages"))

from polymarket.historical_replay import HistoricalPricePoint, HistoricalReplay
from polymarket.market_snapshot import MarketLifecycle, PolymarketMarket, ResolutionMetadata, PriceObservation, build_snapshot


BASE = PolymarketMarket(
    market_id="replay-001", condition_id="0xreplay", question="Will X happen?", description="synthetic",
    category="test", market_type="binary", outcomes=("Yes", "No"), outcome_ids=("yes", "no"),
    lifecycle=MarketLifecycle.OPEN, created_at="2025-01-01T00:00:00Z", start_at="2025-01-01T00:00:00Z",
    close_at=None, expected_resolution_at="2025-01-05T00:00:00Z", resolution_source="fixture",
    resolution_rule_text="Yes if X.", cancellation_state=None, slug="replay-001",
)


def snap(at, lifecycle=MarketLifecycle.OPEN, resolution=None, acquired=None):
    market = PolymarketMarket(**{**BASE.__dict__, "lifecycle": lifecycle})
    return build_snapshot(
        market, snapshot_at=at, acquired_at=acquired or at, known_as_of=at,
        source_type="synthetic_fixture", source_ref="fixture://phase2/replay-001", data_quality="synthetic",
        source_payload_hash="fixture", price_observations=(PriceObservation("yes", "0.40", at, "fixture"),), resolution=resolution,
    )


def point(at, price, acquired=None, known=None):
    return HistoricalPricePoint("yes", at, price, 60, "synthetic_fixture", "fixture://phase2/replay-001", acquired or at, known or at)


def test_replay_selects_latest_state_known_by_cutoff():
    result = HistoricalReplay().replay(market_id="replay-001", as_of="2025-01-02T12:00:00Z", snapshots=[snap("2025-01-01T00:00:00Z"), snap("2025-01-02T10:00:00Z")])
    assert result.lifecycle == "open"
    assert result.snapshot is not None
    assert result.snapshot.snapshot_at == "2025-01-02T10:00:00Z"


def test_later_resolution_cannot_leak_into_earlier_replay():
    resolution = ResolutionMetadata("resolved", ("yes",), "fixture", "Yes if X.", "2025-01-04T09:00:00Z")
    late = snap("2025-01-04T10:00:00Z", MarketLifecycle.RESOLVED, resolution)
    early = snap("2025-01-02T10:00:00Z")
    result = HistoricalReplay().replay(market_id="replay-001", as_of="2025-01-03T00:00:00Z", snapshots=[early, late])
    assert result.lifecycle == "open"
    assert result.snapshot.resolution is None


def test_snapshot_acquired_after_cutoff_is_not_available():
    delayed = snap("2025-01-02T00:00:00Z", acquired="2025-01-04T00:00:00Z")
    result = HistoricalReplay().replay(market_id="replay-001", as_of="2025-01-03T00:00:00Z", snapshots=[delayed])
    assert result.missing_state is True


def test_sparse_price_history_is_preserved_without_interpolation():
    result = HistoricalReplay().replay(market_id="replay-001", as_of="2025-01-04T00:00:00Z", snapshots=[snap("2025-01-01T00:00:00Z")], price_points=[point("2025-01-01T01:00:00Z", "0.40"), point("2025-01-03T01:00:00Z", "0.65"), point("2025-01-05T01:00:00Z", "0.90")])
    assert [p.price for p in result.price_points] == ["0.40", "0.65"]
    assert len(result.price_points) == 2


def test_contradictory_price_points_at_same_timestamp_fail():
    with pytest.raises(ValueError, match="contradictory historical price point"):
        HistoricalReplay().replay(market_id="replay-001", as_of="2025-01-02T00:00:00Z", snapshots=[snap("2025-01-01T00:00:00Z")], price_points=[point("2025-01-01T01:00:00Z", "0.40"), point("2025-01-01T01:00:00Z", "0.41")])


def test_replay_is_deterministic():
    snapshots = [snap("2025-01-01T00:00:00Z"), snap("2025-01-02T00:00:00Z")]
    prices = [point("2025-01-01T01:00:00Z", "0.40"), point("2025-01-01T03:00:00Z", "0.45")]
    a = HistoricalReplay().replay(market_id="replay-001", as_of="2025-01-02T12:00:00Z", snapshots=snapshots, price_points=prices)
    b = HistoricalReplay().replay(market_id="replay-001", as_of="2025-01-02T12:00:00Z", snapshots=list(reversed(snapshots)), price_points=list(reversed(prices)))
    assert a == b


def test_price_observation_cannot_be_known_before_observed():
    with pytest.raises(ValueError, match="cannot be observed after"):
        point("2025-01-02T00:00:00Z", "0.5", known="2025-01-01T00:00:00Z").validate()
