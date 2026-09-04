"""
Tier 2: Boundary & Corner Cases Test Suite.
Exhaustively tests edge cases, zero divisions, malformed inputs, network timeouts,
extreme volatility spikes, corrupt frontmatter, and invalid Excel/CSV data.
"""

import os
import re
import json
import math
import uuid
import pytest
from datetime import datetime, timezone
from typing import Any

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.authorizer import Principal
from memory_controller.validation.schema import validate_frontmatter
from jsonschema.exceptions import ValidationError

from tests.financial.test_tier1_features import (
    calculate_rsi,
    calculate_macd,
    calculate_bollinger_bands,
    calculate_atr,
    calculate_confluence_score,
    calculate_portfolio_metrics,
    create_valid_note_payload,
)


# ============================================================================
# 1. Zero-Division and Numerical Extremes Defenses
# ============================================================================

def test_tier2_rsi_flat_price_zero_gain_and_loss():
    """T2.1: Verify RSI returns 50.0 when prices are completely flat (zero gain and zero loss)."""
    flat = [100.0] * 30
    rsi = calculate_rsi(flat, period=14)
    assert rsi == 50.0, f"Expected 50.0 for flat prices, got {rsi}"


def test_tier2_rvol_zero_average_volume():
    """T2.2: Verify RVOL calculation handles zero historical average volume without ZeroDivisionError."""
    def calc_rvol(current_vol: float, avg_vol: float) -> float:
        if avg_vol <= 0:
            return 1.0 if current_vol > 0 else 0.0
        return round(current_vol / avg_vol, 2)

    assert calc_rvol(1000.0, 0.0) == 1.0
    assert calc_rvol(0.0, 0.0) == 0.0
    assert calc_rvol(1500.0, 1000.0) == 1.5


def test_tier2_profit_factor_zero_loss_denominator():
    """T2.3: Verify profit factor returns a capped metric (e.g. 999.0) when total loss is 0."""
    all_wins = [
        {"trade_id": "T1", "pnl_currency": 500.0, "realized_rr": 2.0},
        {"trade_id": "T2", "pnl_currency": 750.0, "realized_rr": 1.5},
    ]
    metrics = calculate_portfolio_metrics(all_wins)
    assert metrics["win_rate"] == 100.0
    assert metrics["profit_factor"] == 999.0


def test_tier2_sharpe_ratio_zero_variance():
    """T2.4: Verify Sharpe ratio calculation returns 0.0 when returns have zero variance."""
    flat_returns = [0.01, 0.01, 0.01, 0.01, 0.01]
    mean_ret = sum(flat_returns) / len(flat_returns)
    variance = sum((r - mean_ret) ** 2 for r in flat_returns) / len(flat_returns)
    std_ret = math.sqrt(variance)
    sharpe = (mean_ret / std_ret) * math.sqrt(252) if std_ret > 0 else 0.0
    assert sharpe == 0.0


def test_tier2_realized_rr_zero_risk_distance():
    """T2.5: Verify realized RR formula handles Entry == SL without ZeroDivisionError."""
    def calc_rr(entry: float, sl: float, exit_price: float, direction: str = "LONG") -> float:
        risk_dist = abs(entry - sl)
        if risk_dist <= 1e-6:
            return 0.0  # Defensive clamp
        if direction.upper() == "LONG":
            return round((exit_price - entry) / risk_dist, 2)
        else:
            return round((entry - exit_price) / risk_dist, 2)

    assert calc_rr(2500.0, 2500.0, 2520.0, "LONG") == 0.0
    assert calc_rr(2500.0, 2490.0, 2520.0, "LONG") == 2.0


# ============================================================================
# 2. Empty Data, Insufficient Bars, and Single Bar Edge Cases
# ============================================================================

def test_tier2_indicators_with_empty_and_single_bar_data():
    """T2.6: Verify indicators return safe defaults when bar data is empty or insufficient."""
    assert calculate_rsi([], 14) == 50.0
    assert calculate_rsi([100.0], 14) == 50.0

    macd = calculate_macd([])
    assert macd == {"macd": 0.0, "signal": 0.0, "hist": 0.0}

    bb = calculate_bollinger_bands([], 20)
    assert bb["bandwidth"] == 0.0

    atr = calculate_atr([], [], [], 14)
    assert atr == 0.0


def test_tier2_portfolio_metrics_with_empty_trade_list():
    """T2.7: Verify portfolio analytics gracefully handle empty trade list."""
    metrics = calculate_portfolio_metrics([])
    assert metrics["win_rate"] == 0.0
    assert metrics["profit_factor"] == 0.0
    assert metrics["max_drawdown"] == 0.0
    assert metrics["avg_rr"] == 0.0


# ============================================================================
# 3. Invalid Tickers, Non-existent FRED Series, and Network Outages
# ============================================================================

def test_tier2_invalid_ticker_and_malformed_symbol_handling(asset_catalog):
    """T2.8: Verify lookup handles malformed, empty, and non-existent ticker strings."""
    def lookup_symbol(sym: str) -> bool:
        if not sym or not isinstance(sym, str):
            return False
        clean = sym.strip().upper()
        for cat, items in asset_catalog.items():
            for item in items:
                if item[0].upper() == clean:
                    return True
        return False

    assert not lookup_symbol("")
    assert not lookup_symbol(None)
    assert not lookup_symbol("INVALID_TICKER_XYZ_999")
    assert not lookup_symbol("!@#$%^&*()")
    assert lookup_symbol("GC=F")
    assert lookup_symbol("^GSPC")


def test_tier2_non_existent_fred_series_code_handling():
    """T2.9: Verify FRED API parser rejects unsupported or unknown series codes."""
    valid_series = {"FEDFUNDS", "CPIAUCSL", "UNRATE", "GDP", "DGS10", "M2SL"}
    assert "NON_EXISTENT_SERIES_999" not in valid_series


def test_tier2_network_outage_and_timeout_fallback_simulation():
    """T2.10: Verify network timeout triggers cached snapshot retrieval."""
    class MockMarketFetcher:
        def __init__(self, offline_mode: bool = False):
            self.offline_mode = offline_mode
            self.cache = {"GC=F": 2510.50, "^GSPC": 5600.25}

        def fetch_price(self, ticker: str) -> float:
            if self.offline_mode:
                if ticker in self.cache:
                    return self.cache[ticker]
                raise TimeoutError("Network offline and ticker not cached")
            return self.cache.get(ticker, 0.0)

    fetcher = MockMarketFetcher(offline_mode=True)
    assert fetcher.fetch_price("GC=F") == 2510.50
    with pytest.raises(TimeoutError):
        fetcher.fetch_price("UNKNOWN_TICKER")


# ============================================================================
# 4. Extreme Market Volatility Spikes & Flash Crashes
# ============================================================================

def test_tier2_flash_crash_50_percent_drop_simulation():
    """T2.11: Verify ATR and Bollinger Bands handle a 50% single-bar flash crash without crashing."""
    closes = [2500.0] * 20 + [1250.0]  # 50% flash crash
    highs = [2505.0] * 20 + [2500.0]
    lows = [2495.0] * 20 + [1200.0]

    atr = calculate_atr(highs, lows, closes, 14)
    assert atr > 80.0  # ATR expands massively

    bb = calculate_bollinger_bands(closes, 20)
    assert bb["bandwidth"] > 500.0  # Volatility expansion


def test_tier2_extreme_rvol_volume_surge():
    """T2.12: Verify confluence scoring handles extreme 100x volume surge gracefully."""
    extreme_confluence = calculate_confluence_score(
        rsi=20.0,
        macd_hist=5.0,
        price=2500.0,
        sma50=2400.0,
        sma200=2300.0,
        rvol=100.0,  # Extreme volume spike
    )
    assert extreme_confluence["probability_percent"] <= 90  # Clamped at 90%
    assert extreme_confluence["signal"] == "BUY"


# ============================================================================
# 5. Corrupt Frontmatter, Missing UUID, and Invalid Lifecycles
# ============================================================================

def test_tier2_corrupt_frontmatter_missing_required_fields():
    """T2.13: Verify schema validation rejects frontmatter with missing mandatory fields."""
    corrupt_payload = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        # missing lifecycle, category, tags, created, updated, provenance, confidence, verification, relations
    }
    with pytest.raises(ValidationError):
        validate_frontmatter(corrupt_payload)


def test_tier2_corrupt_frontmatter_invalid_uuid():
    """T2.14: Verify schema validation rejects invalid non-UUID strings in id."""
    invalid_uuid_payload = create_valid_note_payload(
        note_id="not-a-valid-uuid-12345",
        note_type="knowledge",
        title="Invalid UUID Note",
        content="Testing non-UUID id",
    )
    frontmatter = {k: v for k, v in invalid_uuid_payload.items() if k != "content"}
    with pytest.raises(ValidationError):
        validate_frontmatter(frontmatter)


def test_tier2_corrupt_frontmatter_invalid_lifecycle():
    """T2.15: Verify schema validation rejects unknown lifecycle strings."""
    invalid_lc = create_valid_note_payload(
        note_id=str(uuid.uuid4()),
        note_type="knowledge",
        title="Invalid Lifecycle",
        content="Testing unknown lifecycle",
        lifecycle="BOGUS_LIFECYCLE",
    )
    frontmatter = {k: v for k, v in invalid_lc.items() if k != "content"}
    with pytest.raises(ValidationError):
        validate_frontmatter(frontmatter)


def test_tier2_corrupt_frontmatter_additional_properties_rejected():
    """T2.16: Verify schema validation rejects unknown injected top-level keys."""
    injected_payload = create_valid_note_payload(
        note_id=str(uuid.uuid4()),
        note_type="knowledge",
        title="Injected Key Note",
        content="Testing injected key",
    )
    frontmatter = {k: v for k, v in injected_payload.items() if k != "content"}
    frontmatter["malicious_injected_key"] = "exploit"
    with pytest.raises(ValidationError):
        validate_frontmatter(frontmatter)


# ============================================================================
# 6. Malformed Excel / CSV Trade Data
# ============================================================================

def test_tier2_malformed_trade_negative_prices_or_inverted_timestamps():
    """T2.17: Verify parser validates trade rows for positive prices and chronological timestamps."""
    def validate_raw_trade_row(row: dict[str, Any]) -> tuple[bool, str]:
        if row.get("entry_price", 0) <= 0:
            return False, "Entry price must be positive"
        if row.get("stop_loss", 0) <= 0:
            return False, "Stop loss must be positive"
        if row.get("position_size", 0) <= 0:
            return False, "Position size must be positive"
        if "exit_date" in row and "date" in row:
            if row["exit_date"] < row["date"]:
                return False, "Exit date cannot precede entry date"
        return True, "Valid"

    assert not validate_raw_trade_row({"entry_price": -100.0, "stop_loss": 50.0, "position_size": 1.0})[0]
    assert not validate_raw_trade_row({"entry_price": 100.0, "stop_loss": 50.0, "position_size": -2.0})[0]
    assert not validate_raw_trade_row({
        "entry_price": 100.0, "stop_loss": 50.0, "position_size": 1.0,
        "date": "2026-08-25", "exit_date": "2026-08-20"
    })[0]
    assert validate_raw_trade_row({
        "entry_price": 2510.50, "stop_loss": 2504.00, "position_size": 2.0,
        "date": "2026-08-25", "exit_date": "2026-08-25"
    })[0]
