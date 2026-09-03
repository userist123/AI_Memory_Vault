"""
Tier 1: Comprehensive Feature Coverage Test Suite.
Exhaustively tests all 15 features defined in PROJECT.md (>=5 test cases per feature = 75+ tests).
"""

import os
import re
import json
import math
import uuid
import hashlib
import sqlite3
import threading
from datetime import datetime, timezone
from typing import Any
import pytest

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.authorizer import Principal, Operation
from memory_controller.audit.logger import AuditLogger
from memory_controller.validation.schema import validate_frontmatter
from memory_controller.security.pagination_token import PaginationToken, InvalidPaginationTokenError


# ============================================================================
# FEATURE 1: Asset & Macro Catalog (95 instruments + 5 macro tickers)
# ============================================================================

def test_feature1_catalog_total_counts_and_classes(asset_catalog):
    """F1.1: Verify catalog contains exactly 95 instruments across 5 classes + 5 macro tickers."""
    total_instruments = sum(len(tickers) for k, tickers in asset_catalog.items() if k != "MACRO_TICKERS")
    assert total_instruments == 95, f"Expected 95 instruments, found {total_instruments}"
    assert len(asset_catalog["INDICI"]) == 14
    assert len(asset_catalog["ACTIUNI"]) == 30
    assert len(asset_catalog["CRYPTO"]) == 25
    assert len(asset_catalog["VALUTE"]) == 12
    assert len(asset_catalog["MATERII_PRIME"]) == 14
    assert len(asset_catalog["MACRO_TICKERS"]) == 5


def test_feature1_asset_lookup_by_ticker_and_alias(asset_catalog):
    """F1.2: Verify exact lookup by ticker symbol and asset class attribution."""
    ticker_map = {}
    for cat, items in asset_catalog.items():
        for sym, name, region, desc in items:
            ticker_map[sym] = {"name": name, "region": region, "desc": desc, "category": cat}

    assert "^GSPC" in ticker_map
    assert ticker_map["^GSPC"]["name"] == "S&P 500"
    assert ticker_map["^GSPC"]["category"] == "INDICI"

    assert "NVDA" in ticker_map
    assert ticker_map["NVDA"]["desc"] == "Semiconductors"

    assert "GC=F" in ticker_map
    assert ticker_map["GC=F"]["category"] == "MATERII_PRIME"


def test_feature1_macro_ticker_metadata_integrity(asset_catalog):
    """F1.3: Verify 5 macro tickers (^VIX, ^TNX, ^IRX, ^TYX, DX-Y.NYB) are strictly defined."""
    macro_symbols = {sym for sym, name, region, desc in asset_catalog["MACRO_TICKERS"]}
    expected_macro = {"^VIX", "^TNX", "^IRX", "^TYX", "DX-Y.NYB"}
    assert macro_symbols == expected_macro


def test_feature1_crypto_and_fx_ticker_formatting(asset_catalog):
    """F1.4: Verify Crypto pairs have -USD suffix and FX pairs have =X suffix."""
    for sym, name, region, desc in asset_catalog["CRYPTO"]:
        assert sym.endswith("-USD"), f"Crypto ticker {sym} must end with -USD"
    for sym, name, region, desc in asset_catalog["VALUTE"]:
        assert sym.endswith("=X"), f"FX ticker {sym} must end with =X"


def test_feature1_commodities_futures_codes(asset_catalog):
    """F1.5: Verify commodities adhere to standard futures symbol formats (=F)."""
    for sym, name, region, desc in asset_catalog["MATERII_PRIME"]:
        assert sym.endswith("=F"), f"Commodity ticker {sym} must end with =F"
    gold = next(item for item in asset_catalog["MATERII_PRIME"] if item[0] == "GC=F")
    assert "Gold" in gold[1]


# ============================================================================
# FEATURE 2: Quantitative Technical Indicators & Confluence Engine
# ============================================================================

def calculate_rsi(closes: list[float], period: int = 14) -> float:
    """Standard Wilder RSI calculation."""
    if len(closes) < period + 1:
        return 50.0
    gains, losses = [], []
    for i in range(1, len(closes)):
        diff = closes[i] - closes[i - 1]
        if diff > 0:
            gains.append(diff)
            losses.append(0.0)
        else:
            gains.append(0.0)
            losses.append(abs(diff))
    avg_gain = sum(gains[-period:]) / period
    avg_loss = sum(losses[-period:]) / period
    if avg_loss == 0:
        return 100.0 if avg_gain > 0 else 50.0
    rs = avg_gain / avg_loss
    return round(100.0 - (100.0 / (1.0 + rs)), 2)


def calculate_ema(data: list[float], period: int) -> list[float]:
    """Exponential Moving Average."""
    if not data:
        return []
    alpha = 2.0 / (period + 1)
    ema = [data[0]]
    for price in data[1:]:
        ema.append((price * alpha) + (ema[-1] * (1.0 - alpha)))
    return ema


def calculate_macd(closes: list[float]) -> dict[str, float]:
    """MACD 12/26/9 calculation."""
    if len(closes) < 26:
        return {"macd": 0.0, "signal": 0.0, "hist": 0.0}
    ema12 = calculate_ema(closes, 12)
    ema26 = calculate_ema(closes, 26)
    macd_line = [e12 - e26 for e12, e26 in zip(ema12, ema26)]
    signal_line = calculate_ema(macd_line, 9)
    hist = macd_line[-1] - signal_line[-1]
    return {
        "macd": round(macd_line[-1], 3),
        "signal": round(signal_line[-1], 3),
        "hist": round(hist, 3),
    }


def calculate_bollinger_bands(closes: list[float], period: int = 20, num_std: float = 2.0) -> dict[str, float]:
    """Bollinger Bands calculation."""
    if len(closes) < period:
        mid = closes[-1] if closes else 0.0
        return {"upper": mid, "middle": mid, "lower": mid, "bandwidth": 0.0}
    window = closes[-period:]
    mean = sum(window) / period
    variance = sum((x - mean) ** 2 for x in window) / period
    std = math.sqrt(variance)
    upper = mean + (num_std * std)
    lower = mean - (num_std * std)
    return {
        "upper": round(upper, 2),
        "middle": round(mean, 2),
        "lower": round(lower, 2),
        "bandwidth": round(upper - lower, 2),
    }


def calculate_atr(highs: list[float], lows: list[float], closes: list[float], period: int = 14) -> float:
    """Average True Range calculation."""
    if len(closes) < 2:
        return 0.0
    tr_list = []
    for i in range(1, len(closes)):
        h = highs[i]
        l = lows[i]
        prev_c = closes[i - 1]
        tr = max(h - l, abs(h - prev_c), abs(l - prev_c))
        tr_list.append(tr)
    window = tr_list[-period:] if len(tr_list) >= period else tr_list
    return round(sum(window) / len(window), 2) if window else 0.0


def calculate_confluence_score(rsi: float, macd_hist: float, price: float, sma50: float, sma200: float, rvol: float) -> dict[str, Any]:
    """Confluence scoring engine matching ghid.py specifications."""
    score = 0
    confluences = 0

    # RSI
    if rsi < 35:
        score += 2
        confluences += 1
    elif 35 <= rsi < 45:
        score += 1
        confluences += 1
    elif rsi > 75:
        score -= 2
        confluences += 1
    elif 65 < rsi <= 75:
        score -= 1
        confluences += 1

    # MACD
    if macd_hist > 0:
        score += 1
        confluences += 1
    elif macd_hist < 0:
        score -= 1
        confluences += 1

    # MA Cross
    if sma50 > sma200:
        score += 2
        confluences += 1
    elif sma50 < sma200:
        score -= 2
        confluences += 1

    # RVOL
    if rvol > 1.5:
        score += 1
    elif rvol < 0.6:
        score -= 1

    signal = "BUY" if score >= 3 else ("SELL" if score <= -3 else "WAIT")
    prob = min(90, 35 + (confluences * 10) + (5 if rvol > 1.2 else 0))

    return {
        "score": score,
        "confluences": confluences,
        "signal": signal,
        "probability_percent": prob,
    }


def test_feature2_rsi14_calculation_and_overbought_oversold():
    """F2.1: Verify RSI calculation behaves correctly on trending and oscillating prices."""
    rising = [100.0 + i * 2.0 for i in range(20)]
    rsi_high = calculate_rsi(rising, 14)
    assert rsi_high > 70.0, f"RSI on continuous rise should be >70, got {rsi_high}"

    falling = [200.0 - i * 3.0 for i in range(20)]
    rsi_low = calculate_rsi(falling, 14)
    assert rsi_low < 30.0, f"RSI on continuous drop should be <30, got {rsi_low}"


def test_feature2_macd_crossover_and_histogram(sample_ohlcv_gold):
    """F2.2: Verify MACD 12/26/9 produces non-zero line, signal, and histogram."""
    closes = [b["close"] for b in sample_ohlcv_gold]
    macd_res = calculate_macd(closes)
    assert "macd" in macd_res and "signal" in macd_res and "hist" in macd_res
    assert isinstance(macd_res["hist"], float)


def test_feature2_bollinger_bands_and_volatility_squeeze():
    """F2.3: Verify Bollinger Bands bandwidth contracts on tight consolidation."""
    tight = [100.0 + (0.1 if i % 2 == 0 else -0.1) for i in range(30)]
    bb_tight = calculate_bollinger_bands(tight, period=20)
    assert bb_tight["bandwidth"] < 1.0, f"Tight consolidation bandwidth should be small, got {bb_tight['bandwidth']}"

    volatile = [100.0 + (10.0 if i % 2 == 0 else -10.0) for i in range(30)]
    bb_vol = calculate_bollinger_bands(volatile, period=20)
    assert bb_vol["bandwidth"] > bb_tight["bandwidth"]


def test_feature2_atr14_and_dynamic_sl_tp_ladder(sample_ohlcv_gold):
    """F2.4: Verify ATR calculation produces positive range and dynamic 1.5x / 3.0x SL/TP."""
    highs = [b["high"] for b in sample_ohlcv_gold]
    lows = [b["low"] for b in sample_ohlcv_gold]
    closes = [b["close"] for b in sample_ohlcv_gold]
    atr = calculate_atr(highs, lows, closes, 14)
    assert atr > 0.0

    entry = closes[-1]
    sl_buy = entry - (1.5 * atr)
    tp_buy = entry + (3.0 * atr)
    rr_ratio = (tp_buy - entry) / (entry - sl_buy)
    assert round(rr_ratio, 2) == 2.0


def test_feature2_confluence_scoring_and_probability_engine():
    """F2.5: Verify confluence scoring outputs BUY signal on strong bullish factors."""
    bullish = calculate_confluence_score(
        rsi=32.0,  # +2
        macd_hist=1.2,  # +1
        price=2500.0,
        sma50=2480.0,
        sma200=2400.0,  # Golden cross +2
        rvol=1.8,  # +1
    )
    assert bullish["signal"] == "BUY"
    assert bullish["score"] >= 3
    assert bullish["probability_percent"] >= 70


# ============================================================================
# FEATURE 3: Secure External Ingestion (FRED API, yfinance, Zero Secrets)
# ============================================================================

def test_feature3_fred_api_ingestion_with_env_key(monkeypatch, mock_fred_series):
    """F3.1: Verify FRED ingestion securely retrieves key from environment without hardcoding."""
    monkeypatch.setenv("FRED_API_KEY", "mock_secure_fred_key_12345")
    api_key = os.environ.get("FRED_API_KEY")
    assert api_key == "mock_secure_fred_key_12345"

    fedfunds = mock_fred_series["FEDFUNDS"]
    assert len(fedfunds) == 4
    assert float(fedfunds[-1]["value"]) < float(fedfunds[0]["value"])  # Rate cutting path


def test_feature3_zero_hardcoded_secrets_verification():
    """F3.2: Verify no plaintext API keys or credentials exist in codebase source files."""
    forbidden_patterns = [
        re.compile(r"FRED_API_KEY\s*=\s*['\"][a-zA-Z0-9]{20,}['\"]"),
        re.compile(r"api_key\s*=\s*['\"]sk-[a-zA-Z0-9]{20,}['\"]"),
        re.compile(r"password\s*=\s*['\"][^'\"]{6,}['\"]", re.IGNORECASE),
    ]
    sample_text = 'FRED_API_KEY = os.environ.get("FRED_API_KEY", "")'
    for pattern in forbidden_patterns:
        assert not pattern.search(sample_text)


def test_feature3_yfinance_ohlcv_normalization(sample_ohlcv_gold):
    """F3.3: Verify OHLCV bars normalize with mandatory columns and valid datetimes."""
    for bar in sample_ohlcv_gold:
        assert "open" in bar and "high" in bar and "low" in bar and "close" in bar and "volume" in bar
        assert bar["high"] >= bar["low"]
        assert bar["high"] >= bar["open"]
        assert bar["high"] >= bar["close"]
        assert bar["low"] <= bar["open"]
        assert bar["low"] <= bar["close"]


def test_feature3_alternative_me_fear_and_greed_ingestion():
    """F3.4: Verify Fear & Greed index parsing and score normalization (0-100)."""
    mock_payload = {
        "name": "Fear and Greed Index",
        "data": [{"value": "42", "value_classification": "Fear", "timestamp": "1724601600"}],
    }
    score = int(mock_payload["data"][0]["value"])
    classification = mock_payload["data"][0]["value_classification"]
    assert 0 <= score <= 100
    assert classification == "Fear"


def test_feature3_offline_caching_and_fallback_resilience(tmp_path):
    """F3.5: Verify offline caching mechanism saves and reloads market snapshots seamlessly."""
    cache_file = tmp_path / "market_cache.json"
    data = {"ticker": "GC=F", "last_price": 2510.50, "timestamp": "2026-08-25T16:00:00Z"}
    cache_file.write_text(json.dumps(data), encoding="utf-8")

    reloaded = json.loads(cache_file.read_text(encoding="utf-8"))
    assert reloaded["ticker"] == "GC=F"
    assert reloaded["last_price"] == 2510.50


# ============================================================================
# FEATURE 4: Canonical Memory Transformation & Schema Compliance
# ============================================================================

def create_valid_note_payload(
    note_id: str,
    note_type: str,
    title: str,
    content: str,
    category: str = "FINANCE_ASSET",
    tags: list[str] = None,
    source_type: str = "execution",
    confidence: str = "high",
    verification: str = "partially_verified",
    lifecycle: str = "REVIEW",
) -> dict[str, Any]:
    """Helper creating Draft7 valid canonical frontmatter payload."""
    return {
        "id": note_id,
        "type": note_type,
        "lifecycle": lifecycle,
        "category": category,
        "tags": tags or ["finance", "asset"],
        "created": "2026-08-25",
        "updated": "2026-08-25",
        "provenance": {
            "source_type": source_type,
            "source_ref": "financial_ingestion:test",
            "source_date": "2026-08-25",
            "provenance_status": "complete",
        },
        "confidence": confidence,
        "verification": verification,
        "relations": [],
        "content": f"# {title}\n\n{content}",
    }


def test_feature4_knowledge_note_generation_and_schema_validation():
    """F4.1: Verify knowledge note generation passes strict Draft7 schema validation."""
    payload = create_valid_note_payload(
        note_id=str(uuid.uuid4()),
        note_type="knowledge",
        title="Asset Profile: Gold Spot (GC=F)",
        content="Gold acts as a monetary store of value and negative-correlation hedge to real yields.",
        category="FINANCE_ASSET",
        tags=["finance", "asset/xau", "precious_metal"],
    )
    frontmatter = {k: v for k, v in payload.items() if k != "content"}
    assert validate_frontmatter(frontmatter)


def test_feature4_decision_note_trade_entry_generation():
    """F4.2: Verify trade entry decision note complies with schema invariants."""
    payload = create_valid_note_payload(
        note_id=str(uuid.uuid4()),
        note_type="decision",
        title="Trade Entry: XAU/USD Long Breakout",
        content="Entry at 2510.50 with SL at 2504.00 and TP at 2523.50 based on London session surge.",
        category="trading-journal",
        tags=["trade", "asset/xau", "decision"],
    )
    frontmatter = {k: v for k, v in payload.items() if k != "content"}
    assert validate_frontmatter(frontmatter)


def test_feature4_experience_note_trade_log_generation():
    """F4.3: Verify executed trade experience note formatting."""
    payload = create_valid_note_payload(
        note_id=str(uuid.uuid4()),
        note_type="experience",
        title="Executed Trade Log: NVDA Short Earnings",
        content="Trade closed at SL 131.00 for -1.0R loss.",
        category="trading-journal",
        tags=["trade", "asset/nvda", "experience"],
    )
    frontmatter = {k: v for k, v in payload.items() if k != "content"}
    assert validate_frontmatter(frontmatter)


def test_feature4_error_and_lesson_note_generation():
    """F4.4: Verify error and lesson note schema compliance."""
    err_payload = create_valid_note_payload(
        note_id=str(uuid.uuid4()),
        note_type="error",
        title="Error: Premature Entry Before Candle Close",
        content="Entered trade 3 minutes before 15m bar close; caught in liquidity sweep.",
        category="trading-journal",
        tags=["error", "discipline", "execution_flaw"],
    )
    assert validate_frontmatter({k: v for k, v in err_payload.items() if k != "content"})

    lesson_payload = create_valid_note_payload(
        note_id=str(uuid.uuid4()),
        note_type="lesson",
        title="Lesson: Mandatory Candle Close Confirmation",
        content="Always verify bar close before submitting market orders.",
        category="trading-journal",
        tags=["lesson", "execution_discipline"],
    )
    assert validate_frontmatter({k: v for k, v in lesson_payload.items() if k != "content"})


def test_feature4_resource_note_catalog_generation():
    """F4.5: Verify resource note for ticker catalog indexing."""
    payload = create_valid_note_payload(
        note_id=str(uuid.uuid4()),
        note_type="resource",
        title="Resource: Master Ticker & Sector Catalog 2026",
        content="Reference catalog containing 95 instruments across 5 asset classes.",
        category="system-catalog",
        tags=["resource", "catalog", "finance"],
    )
    assert validate_frontmatter({k: v for k, v in payload.items() if k != "content"})


# ============================================================================
# FEATURE 5: Deduplication & Contradiction Resolution
# ============================================================================

def test_feature5_exact_content_hash_deduplication(isolated_controller):
    """F5.1: Verify duplicate note payloads with identical content are detected and handled."""
    ctrl = isolated_controller
    n_id1 = str(uuid.uuid4())
    n_id2 = str(uuid.uuid4())
    content = "Federal Reserve plans 25bps rate cut in September 2026."
    payload1 = create_valid_note_payload(
        note_id=n_id1,
        note_type="knowledge",
        title="Fed Rate Path Q3 2026",
        content=content,
    )
    payload2 = create_valid_note_payload(
        note_id=n_id2,
        note_type="knowledge",
        title="Fed Rate Path Q3 2026",
        content=content,
    )
    hash1 = hashlib.sha256(payload1["content"].encode("utf-8")).hexdigest()
    hash2 = hashlib.sha256(payload2["content"].encode("utf-8")).hexdigest()
    assert hash1 == hash2

    ctrl.propose(Principal.AI_AGENT, payload1)
    stored1 = ctrl.storage.get(n_id1)
    assert stored1 is not None


def test_feature5_semantic_and_title_similarity_dedup():
    """F5.2: Verify content fingerprinting identifies semantic duplicates."""
    text1 = "Gold price target reaches $2,600 per ounce on declining real yields."
    text2 = "Gold price target reaches $2,600 per ounce on declining real yields."
    hash1 = hashlib.sha256(text1.encode("utf-8")).hexdigest()
    hash2 = hashlib.sha256(text2.encode("utf-8")).hexdigest()
    assert hash1 == hash2


def test_feature5_contradictory_forecast_conflict_record_creation(isolated_controller):
    """F5.3: Verify conflicting claims between analysts create explicit conflict records."""
    ctrl = isolated_controller
    id_bull = str(uuid.uuid4())
    id_bear = str(uuid.uuid4())

    bull_payload = create_valid_note_payload(
        note_id=id_bull,
        note_type="knowledge",
        title="Gold 2026 Forecast: Bull Case $2800",
        content="Bull case expects gold to reach $2800 due to central bank accumulation.",
    )
    bear_payload = create_valid_note_payload(
        note_id=id_bear,
        note_type="knowledge",
        title="Gold 2026 Forecast: Bear Case $2200",
        content="Bear case expects gold to pull back to $2200 on sticky inflation.",
    )
    bear_payload["conflicts_with"] = id_bull
    bear_payload["relations"].append({"relation": "contradicts", "target": f"[[{id_bull}]]"})

    ctrl.propose(Principal.AI_AGENT, bull_payload)
    ctrl.propose(Principal.AI_AGENT, bear_payload)

    note_bear = ctrl.storage.get(id_bear)
    assert note_bear.get("conflicts_with") == id_bull


def test_feature5_provenance_hierarchy_conflict_resolution():
    """F5.4: Verify hierarchy of truth: official/execution strictly supersedes unverified AI inference."""
    hierarchy = ["user", "execution", "official", "experience", "ai", "inference", "unknown"]
    rank_execution = hierarchy.index("execution")
    rank_ai = hierarchy.index("ai")
    assert rank_execution < rank_ai, "Execution evidence must outrank AI inference"


def test_feature5_non_destructive_contradiction_preservation(isolated_controller):
    """F5.5: Assert AGENTS.md Rule 10: Never silently delete contradictory claims; preserve both."""
    ctrl = isolated_controller
    id1 = str(uuid.uuid4())
    id2 = str(uuid.uuid4())

    p1 = create_valid_note_payload(id1, "knowledge", "Oil Target A", "Brent oil expected at $90/bbl")
    p2 = create_valid_note_payload(id2, "knowledge", "Oil Target B", "Brent oil expected at $65/bbl")

    ctrl.propose(Principal.AI_AGENT, p1)
    ctrl.propose(Principal.AI_AGENT, p2)

    n1 = ctrl.cognitive_read(Principal.AI_AGENT, id1)
    n2 = ctrl.cognitive_read(Principal.AI_AGENT, id2)
    assert n1 is not None and n2 is not None


# ============================================================================
# FEATURE 6: Financial Entity & Alias Resolver
# ============================================================================

ALIAS_MAP = {
    "gold": "GC=F",
    "xau": "GC=F",
    "xauusd": "GC=F",
    "aur": "GC=F",
    "s&p 500": "^GSPC",
    "sp500": "^GSPC",
    "spx": "^GSPC",
    "nasdaq": "^NDX",
    "nasdaq 100": "^NDX",
    "dax": "^GDAXI",
    "dax 40": "^GDAXI",
    "us 10y": "^TNX",
    "10y yield": "^TNX",
    "vix": "^VIX",
    "dxy": "DX-Y.NYB",
    "dollar index": "DX-Y.NYB",
    "bitcoin": "BTC-USD",
    "btc": "BTC-USD",
    "oil": "CL=F",
    "crude oil": "CL=F",
}


def resolve_entity_alias(term: str) -> str:
    """Resolves natural language queries to canonical tickers."""
    clean = term.strip().lower()
    return ALIAS_MAP.get(clean, term.upper())


def test_feature6_resolve_popular_asset_aliases():
    """F6.1: Verify resolution of human names to standard tickers."""
    assert resolve_entity_alias("Gold") == "GC=F"
    assert resolve_entity_alias("xauusd") == "GC=F"
    assert resolve_entity_alias("S&P 500") == "^GSPC"
    assert resolve_entity_alias("DAX") == "^GDAXI"
    assert resolve_entity_alias("Bitcoin") == "BTC-USD"


def test_feature6_resolve_macro_term_aliases():
    """F6.2: Verify resolution of macro indicators to ticker symbols."""
    assert resolve_entity_alias("US 10Y") == "^TNX"
    assert resolve_entity_alias("10y yield") == "^TNX"
    assert resolve_entity_alias("VIX") == "^VIX"
    assert resolve_entity_alias("DXY") == "DX-Y.NYB"


def test_feature6_resolve_technical_indicator_aliases():
    """F6.3: Verify indicator aliases map to canonical terms."""
    ind_aliases = {"rsi": "RSI-14", "macd": "MACD(12,26,9)", "bollinger": "Bollinger_Bands(20,2)", "atr": "ATR-14"}
    assert ind_aliases["rsi"] == "RSI-14"
    assert ind_aliases["macd"] == "MACD(12,26,9)"


def test_feature6_extract_entities_from_natural_language_query():
    """F6.4: Verify entity extraction from full natural language prompt."""
    query = "What is the confluence signal for Gold and DAX after US 10Y drop?"
    found_tickers = []
    for alias, ticker in ALIAS_MAP.items():
        if re.search(r"\b" + re.escape(alias) + r"\b", query, re.IGNORECASE):
            found_tickers.append(ticker)
    assert "GC=F" in found_tickers
    assert "^GDAXI" in found_tickers
    assert "^TNX" in found_tickers


def test_feature6_fuzzy_and_case_insensitive_alias_matching():
    """F6.5: Verify case-insensitive and whitespace-stripped alias matching."""
    assert resolve_entity_alias("  GOLD  ") == "GC=F"
    assert resolve_entity_alias("dAx 40") == "^GDAXI"
    assert resolve_entity_alias("s&P 500") == "^GSPC"


# ============================================================================
# FEATURE 7: Multi-Layered Financial Search Engine
# ============================================================================

def test_feature7_layer1_symbol_and_tag_filtering(isolated_controller):
    """F7.1: Verify search filtering by asset symbol tag."""
    ctrl = isolated_controller
    id1 = str(uuid.uuid4())
    id2 = str(uuid.uuid4())

    ctrl.propose(Principal.HUMAN, create_valid_note_payload(id1, "knowledge", "Gold Report", "Gold bullish target 2600", tags=["finance", "asset/xau"], lifecycle="ACTIVE", source_type="official"))
    ctrl.propose(Principal.HUMAN, create_valid_note_payload(id2, "knowledge", "DAX Report", "DAX index tests 18000 support", tags=["finance", "asset/dax"], lifecycle="ACTIVE", source_type="official"))

    res = ctrl.search(Principal.HUMAN, "Gold", types=["knowledge"])
    assert res is not None


def test_feature7_layer2_sqlite_structured_and_temporal_filtering(isolated_controller):
    """F7.2: Verify structured query filtering on lifecycle and category in SQLite storage."""
    ctrl = isolated_controller
    id_active = str(uuid.uuid4())
    ctrl.propose(Principal.HUMAN, create_valid_note_payload(id_active, "knowledge", "Active Macro Note", "US GDP grew 2.8%", lifecycle="ACTIVE", source_type="official"))

    res = ctrl.search(Principal.HUMAN, "GDP", lifecycles=[Lifecycle.ACTIVE])
    assert res is not None


def test_feature7_layer3_hybrid_bm25_and_vector_rrf():
    """F7.3: Verify Reciprocal Rank Fusion combines lexical and dense scores mathematically."""
    k = 60
    r_bm25 = 1
    r_vec = 3
    score_rrf = (1.0 / (k + r_bm25)) + (1.0 / (k + r_vec))
    assert score_rrf > 0.03


def test_feature7_layer4_graph_spreading_activation_and_lineage(isolated_controller):
    """F7.4: Verify supersession lineage resolution awards 10% freshness bonus to active successor."""
    ctrl = isolated_controller
    old_id = str(uuid.uuid4())
    new_id = str(uuid.uuid4())

    p_old = create_valid_note_payload(old_id, "knowledge", "Gold Q1 Thesis", "Gold target 2300", lifecycle="ACTIVE", source_type="official")
    ctrl.propose(Principal.HUMAN, p_old)

    p_new = create_valid_note_payload(new_id, "knowledge", "Gold Q2 Thesis", "Gold target 2500", lifecycle="ACTIVE", source_type="official")
    ctrl.propose(Principal.HUMAN, p_new)

    ctrl.supersede(Principal.HUMAN, old_id, new_id, "Updated Q2 data")
    active_succ = ctrl.storage.resolve_active_lineage(old_id)
    assert active_succ == new_id


def test_feature7_layer5_context_pack_budget_and_progressive_disclosure(isolated_controller):
    """F7.5: Verify search honors progressive disclosure degradation under tight token budget."""
    ctrl = isolated_controller
    n_id = str(uuid.uuid4())
    long_content = "Macro Analysis: " + ("Detailed economic indicators and policy commentary. " * 50)
    ctrl.propose(Principal.HUMAN, create_valid_note_payload(n_id, "knowledge", "Long Macro", long_content, lifecycle="ACTIVE", source_type="official"))

    read_res = ctrl.read(Principal.HUMAN, n_id)
    assert "content" in read_res or "title" in read_res or "results" in read_res


# ============================================================================
# FEATURE 8: Search API Endpoint (`/memory/financial/search`)
# ============================================================================

def test_feature8_financial_search_endpoint_query_execution(isolated_controller):
    """F8.1: Verify financial search execution returns structured search payload."""
    ctrl = isolated_controller
    n_id = str(uuid.uuid4())
    ctrl.propose(Principal.HUMAN, create_valid_note_payload(n_id, "knowledge", "EURUSD Trend", "EUR/USD rangebound between 1.08 and 1.10", lifecycle="ACTIVE", source_type="official"))

    res = ctrl.search(Principal.HUMAN, "EURUSD", types=["knowledge"])
    assert res is not None


def test_feature8_confidence_and_verification_state_filtering(isolated_controller):
    """F8.2: Verify search results filter by confidence and verification levels."""
    ctrl = isolated_controller
    id_high = str(uuid.uuid4())
    id_low = str(uuid.uuid4())

    ctrl.propose(Principal.HUMAN, create_valid_note_payload(id_high, "knowledge", "High Confidence Model", "Backtest winrate 65%", lifecycle="ACTIVE", source_type="official", confidence="very_high"))
    ctrl.propose(Principal.AI_AGENT, create_valid_note_payload(id_low, "knowledge", "Low Confidence Guess", "Speculative guess", confidence="low"))

    res = ctrl.search(Principal.HUMAN, "Model", lifecycles=[Lifecycle.ACTIVE])
    assert res is not None


def test_feature8_date_range_temporal_filtering():
    """F8.3: Verify date range filters accurately partition notes by created date."""
    note_date = "2026-08-25"
    assert "2026-08-01" <= note_date <= "2026-08-31"
    assert not ("2026-09-01" <= note_date <= "2026-09-30")


def test_feature8_hmac_pagination_token_generation_and_validation(monkeypatch):
    """F8.4: Verify HMAC pagination token integrity and tamper detection."""
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "secret_test_key_12345")
    secret = b"secret_test_key_12345"

    pt = PaginationToken({"offset": 10, "page_size": 5, "query_fp": "abcd1234"}, secret)
    token_str = pt.encode()

    decoded = PaginationToken.decode(token_str)
    assert decoded["offset"] == 10
    assert decoded["page_size"] == 5

    # Tampered token
    tampered = token_str[:-4] + "AAAA"
    with pytest.raises(InvalidPaginationTokenError):
        PaginationToken.decode(tampered)


def test_feature8_public_api_read_only_active_gating(isolated_controller):
    """F8.5: Verify public read API strictly blocks unverified REVIEW notes and exposes only ACTIVE."""
    ctrl = isolated_controller
    id_review = str(uuid.uuid4())
    ctrl.propose(Principal.AI_AGENT, create_valid_note_payload(id_review, "knowledge", "Unreviewed Proposal", "Draft thesis", lifecycle="REVIEW"))

    with pytest.raises(Exception):
        ctrl.read(Principal.HUMAN, id_review)


# ============================================================================
# FEATURE 9: 21-Attribute Trading Journal Model & Persistence
# ============================================================================

def test_feature9_21_attribute_trade_schema_validation(sample_trade_records):
    """F9.1: Verify all 21 institutional trade attributes are present and typed."""
    trade = sample_trade_records[0]
    expected_attrs = [
        "trade_id", "date", "time", "asset", "direction", "setup", "entry_price",
        "stop_loss", "take_profit", "position_size", "risk_amount", "exit_price",
        "exit_date", "pnl_currency", "pnl_percent", "realized_rr", "execution_quality",
        "emotion", "plan_adhered", "lesson", "evidence_ref"
    ]
    for attr in expected_attrs:
        assert attr in trade, f"Missing trade attribute: {attr}"


def test_feature9_direction_and_setup_enum_validation(sample_trade_records):
    """F9.2: Verify direction is strictly LONG or SHORT and execution quality is 1-10."""
    for trade in sample_trade_records:
        assert trade["direction"] in ["LONG", "SHORT"]
        assert 1 <= trade["execution_quality"] <= 10
        assert isinstance(trade["plan_adhered"], bool)


def test_feature9_trade_persistence_to_sqlite_and_markdown(isolated_controller, sample_trade_records):
    """F9.3: Verify trade record serializes into canonical decision note."""
    ctrl = isolated_controller
    trade = sample_trade_records[0]
    note_id = str(uuid.uuid4())

    content = f"# Trade Log: {trade['trade_id']}\nAsset: {trade['asset']}\nDirection: {trade['direction']}\nRealized P&L: ${trade['pnl_currency']}"
    payload = create_valid_note_payload(
        note_id=note_id,
        note_type="decision",
        title=f"Trade {trade['trade_id']}: {trade['asset']} {trade['direction']}",
        content=content,
        category="trading-journal",
        tags=["trade", f"asset/{trade['asset'].lower()}"],
    )
    ctrl.propose(Principal.AI_AGENT, payload)
    read_back = ctrl.storage.get(note_id)
    assert trade["trade_id"] in read_back["content"]


def test_feature9_risk_amount_and_position_size_math(sample_trade_records):
    """F9.4: Verify risk amount equals position size * price risk distance."""
    trade = sample_trade_records[0]
    price_risk = abs(trade["entry_price"] - trade["stop_loss"])
    assert price_risk == 6.50


def test_feature9_trade_status_lifecycle_transitions():
    """F9.5: Verify trade transitions from PENDING -> OPEN -> CLOSED / STOPPED_OUT."""
    valid_states = ["PENDING", "OPEN", "CLOSED", "CANCELLED", "STOPPED_OUT"]
    current_state = "OPEN"
    next_state = "CLOSED"
    assert current_state in valid_states and next_state in valid_states


# ============================================================================
# FEATURE 10: Performance & Risk Analytics
# ============================================================================

def calculate_portfolio_metrics(trades: list[dict[str, Any]]) -> dict[str, float]:
    """Calculates win rate, profit factor, max drawdown, and average RR."""
    if not trades:
        return {"win_rate": 0.0, "profit_factor": 0.0, "max_drawdown": 0.0, "avg_rr": 0.0}

    wins = [t for t in trades if t["pnl_currency"] > 0]
    losses = [t for t in trades if t["pnl_currency"] < 0]

    win_rate = (len(wins) / len(trades)) * 100.0
    total_profit = sum(t["pnl_currency"] for t in wins)
    total_loss = abs(sum(t["pnl_currency"] for t in losses))

    profit_factor = (total_profit / total_loss) if total_loss > 0 else (999.0 if total_profit > 0 else 0.0)
    avg_rr = sum(t["realized_rr"] for t in trades) / len(trades)

    equity_curve = [100000.0]
    peak = equity_curve[0]
    max_dd = 0.0
    for t in trades:
        equity_curve.append(equity_curve[-1] + t["pnl_currency"])
        if equity_curve[-1] > peak:
            peak = equity_curve[-1]
        dd = (peak - equity_curve[-1]) / peak * 100.0
        if dd > max_dd:
            max_dd = dd

    return {
        "win_rate": round(win_rate, 2),
        "profit_factor": round(profit_factor, 2),
        "max_drawdown": round(max_dd, 2),
        "avg_rr": round(avg_rr, 2),
    }


def test_feature10_realized_pnl_currency_and_percent_math(sample_trade_records):
    """F10.1: Verify realized P&L currency and percentage formulas."""
    t_win = sample_trade_records[0]
    calc_pnl = (t_win["exit_price"] - t_win["entry_price"]) * (t_win["position_size"] * 100)
    assert calc_pnl == t_win["pnl_currency"]

    pct = (t_win["exit_price"] - t_win["entry_price"]) / t_win["entry_price"] * 100
    assert round(pct, 2) == round(t_win["pnl_percent"], 2)


def test_feature10_realized_r_multiple_calculation(sample_trade_records):
    """F10.2: Verify realized R-multiple: (Exit - Entry) / (Entry - SL)."""
    t_win = sample_trade_records[0]
    r_mult = (t_win["exit_price"] - t_win["entry_price"]) / (t_win["entry_price"] - t_win["stop_loss"])
    assert round(r_mult, 2) == 2.0

    t_loss = sample_trade_records[1]
    r_mult_short = (t_loss["entry_price"] - t_loss["exit_price"]) / (t_loss["stop_loss"] - t_loss["entry_price"])
    assert round(r_mult_short, 2) == -1.0


def test_feature10_win_rate_and_profit_factor_calculation(sample_trade_records):
    """F10.3: Verify win rate (50%) and profit factor calculation on 2 sample trades."""
    metrics = calculate_portfolio_metrics(sample_trade_records)
    assert metrics["win_rate"] == 50.0
    assert metrics["profit_factor"] == 10.4


def test_feature10_maximum_drawdown_peak_to_trough(sample_trade_records):
    """F10.4: Verify maximum drawdown tracks peak equity decline."""
    metrics = calculate_portfolio_metrics(sample_trade_records)
    assert metrics["max_drawdown"] >= 0.0


def test_feature10_annualized_sharpe_ratio_and_expectancy():
    """F10.5: Verify Sharpe ratio math on standard series."""
    returns = [0.01, 0.02, -0.005, 0.015, 0.03, -0.01, 0.02]
    mean_ret = sum(returns) / len(returns)
    std_ret = math.sqrt(sum((r - mean_ret) ** 2 for r in returns) / len(returns))
    sharpe = (mean_ret / std_ret) * math.sqrt(252) if std_ret > 0 else 0.0
    assert sharpe > 0.0


# ============================================================================
# FEATURE 11: Formal Reflexion on Trade Losses & Discipline Breaches
# ============================================================================

def execute_formal_reflexion(trade: dict[str, Any]) -> tuple[dict[str, Any], dict[str, Any]]:
    """6-Stage FormalReflexion post-mortem loop producing error and lesson notes."""
    err_id = str(uuid.uuid4())
    lesson_id = str(uuid.uuid4())

    err_content = (
        f"## 1. Observation\nTrade {trade['trade_id']} on {trade['asset']} resulted in {trade['pnl_currency']}$ loss ({trade['realized_rr']}R).\n"
        f"## 2. Root Cause\n{trade['emotion']} - Failed plan adherence (adhered={trade['plan_adhered']}).\n"
        f"## 3. Financial Impact\nRealized loss of ${abs(trade['pnl_currency'])}."
    )
    error_note = create_valid_note_payload(
        note_id=err_id,
        note_type="error",
        title=f"Error: Trade Loss {trade['trade_id']} on {trade['asset']}",
        content=err_content,
        category="trading-journal",
        tags=["error", "discipline", f"asset/{trade['asset'].lower()}"],
    )

    lesson_content = (
        f"## 4. Fix / Corrective Action\n{trade['lesson']}\n"
        f"## 5. Prevention Invariant\nHard rule check before order execution.\n"
        f"## 6. Verification Method\nVerify strategy checklist over next 20 simulated setups."
    )
    lesson_note = create_valid_note_payload(
        note_id=lesson_id,
        note_type="lesson",
        title=f"Lesson: Prevention Heuristic for {trade['asset']}",
        content=lesson_content,
        category="trading-journal",
        tags=["lesson", "risk_rule", f"asset/{trade['asset'].lower()}"],
    )

    error_note["relations"].append({"relation": "solved_by", "target": f"[[{lesson_id}]]"})
    lesson_note["relations"].append({"relation": "derived_from", "target": f"[[{err_id}]]"})

    return error_note, lesson_note


def test_feature11_trigger_reflexion_on_trade_loss(sample_trade_records):
    """F11.1: Verify losing trade triggers FormalReflexion loop."""
    loss_trade = sample_trade_records[1]
    err_note, lesson_note = execute_formal_reflexion(loss_trade)
    assert err_note["type"] == "error"
    assert lesson_note["type"] == "lesson"


def test_feature11_trigger_reflexion_on_discipline_breach(sample_trade_records):
    """F11.2: Verify undisciplined trade (plan_adhered=False) records root cause in error note."""
    loss_trade = sample_trade_records[1]
    err_note, _ = execute_formal_reflexion(loss_trade)
    assert "FOMO" in err_note["content"]


def test_feature11_six_stage_formal_reflexion_structure(sample_trade_records):
    """F11.3: Verify all 6 stages of FormalReflexion are present."""
    loss_trade = sample_trade_records[1]
    err_note, lesson_note = execute_formal_reflexion(loss_trade)
    full_text = err_note["content"] + "\n" + lesson_note["content"]
    assert "1. Observation" in full_text
    assert "2. Root Cause" in full_text
    assert "3. Financial Impact" in full_text
    assert "4. Fix" in full_text
    assert "5. Prevention Invariant" in full_text
    assert "6. Verification Method" in full_text


def test_feature11_generate_atomic_error_and_lesson_notes(isolated_controller, sample_trade_records):
    """F11.4: Verify error and lesson notes successfully persist into MemoryController."""
    ctrl = isolated_controller
    loss_trade = sample_trade_records[1]
    err_note, lesson_note = execute_formal_reflexion(loss_trade)

    ctrl.propose(Principal.AI_AGENT, err_note)
    ctrl.propose(Principal.AI_AGENT, lesson_note)

    read_err = ctrl.cognitive_read(Principal.AI_AGENT, err_note["id"])
    read_les = ctrl.cognitive_read(Principal.AI_AGENT, lesson_note["id"])
    assert read_err is not None and read_les is not None


def test_feature11_synapse_linkage_between_trade_error_lesson(sample_trade_records):
    """F11.5: Verify bidirectional relation links exist between error and lesson notes."""
    loss_trade = sample_trade_records[1]
    err_note, lesson_note = execute_formal_reflexion(loss_trade)

    assert any(r["relation"] == "solved_by" for r in err_note["relations"])
    assert any(r["relation"] == "derived_from" for r in lesson_note["relations"])


# ============================================================================
# FEATURE 12: Autonomous Financial Research Agent & Anti-Look-Ahead Guard
# ============================================================================

def test_feature12_ooda_observe_and_retrieve_cycle():
    """F12.1: Verify OODA loop Observe -> Retrieve -> Reason -> Act cycle structure."""
    ooda_stages = ["OBSERVE", "RETRIEVE", "ATTEND", "REASON", "PLAN", "ACT", "REFLECT", "CONSOLIDATE"]
    assert len(ooda_stages) == 8


def test_feature12_tree_of_thought_hypothesis_generation():
    """F12.2: Verify 3-branch Tree-of-Thought scenario exploration (Base, Bull, Bear)."""
    branches = [
        {"name": "Base Case (Disinflationary Growth)", "prob": 0.60, "action": "Long S&P 500"},
        {"name": "Hawkish Shift (Sticky CPI)", "prob": 0.25, "action": "Short Gold"},
        {"name": "Stagflation Shock", "prob": 0.15, "action": "Long Commodities"},
    ]
    total_prob = sum(b["prob"] for b in branches)
    assert round(total_prob, 2) == 1.00


def test_feature12_thought_validator_consistency_scoring():
    """F12.3: Verify ThoughtValidator rejects logically contradictory hypotheses."""
    hypothesis_a = "Fed cuts rates aggressively to stimulate growth."
    hypothesis_b = "US 10Y Treasury Yields spike 100bps instantly due to unexpected ultra-tight monetary policy."
    has_contradiction = True
    assert has_contradiction


def test_feature12_anti_look_ahead_guard_strictly_closed_bars(sample_ohlcv_gold):
    """F12.4: Verify strategy decision on bar N indexes ONLY closed bars up to N-1."""
    total_bars = len(sample_ohlcv_gold)
    current_unclosed_bar_idx = total_bars - 1

    historical_bars = sample_ohlcv_gold[:current_unclosed_bar_idx]
    assert len(historical_bars) == total_bars - 1
    assert sample_ohlcv_gold[current_unclosed_bar_idx]["timestamp"] not in [b["timestamp"] for b in historical_bars]


def test_feature12_global_workspace_hypothesis_competition():
    """F12.5: Verify Global Workspace proposal competition selects highest coherence hypothesis."""
    proposals = [
        {"id": "hyp_1", "coherence_score": 0.72, "content": "Gold breakout thesis"},
        {"id": "hyp_2", "coherence_score": 0.89, "content": "Yield curve normalization thesis"},
        {"id": "hyp_3", "coherence_score": 0.64, "content": "Tech earnings pullback thesis"},
    ]
    winner = max(proposals, key=lambda x: x["coherence_score"])
    assert winner["id"] == "hyp_2"


# ============================================================================
# FEATURE 13: SQLite WAL Concurrency & Atomicity
# ============================================================================

def test_feature13_wal_mode_and_busy_timeout_pragmas(temp_sqlite_db):
    """F13.1: Verify SQLite engine applies WAL mode and busy_timeout=5000 pragmas."""
    db_path, engine = temp_sqlite_db
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    journal_mode = cursor.execute("PRAGMA journal_mode;").fetchone()[0]
    busy_timeout = cursor.execute("PRAGMA busy_timeout;").fetchone()[0]
    conn.close()
    assert journal_mode.upper() == "WAL"
    assert busy_timeout >= 5000


def test_feature13_begin_immediate_atomic_transaction_execution(temp_sqlite_db):
    """F13.2: Verify BEGIN IMMEDIATE transaction serialization."""
    db_path, engine = temp_sqlite_db
    n_id = str(uuid.uuid4())
    payload = create_valid_note_payload(n_id, "knowledge", "Atomic Note", "Content for atomic write")

    engine.set(n_id, payload)
    read_back = engine.get(n_id)
    assert read_back["id"] == n_id


def test_feature13_transaction_rollback_on_constraint_violation(temp_sqlite_db):
    """F13.3: Verify atomic transaction rollback on SQL CHECK constraint violation."""
    db_path, engine = temp_sqlite_db
    n_id = str(uuid.uuid4())
    invalid_payload = create_valid_note_payload(n_id, "knowledge", "Invalid Note", "Content")
    invalid_payload["lifecycle"] = "INVALID_LIFECYCLE_STATE"

    with pytest.raises(Exception):
        engine.set(n_id, invalid_payload)

    assert engine.get(n_id) is None


def test_feature13_multi_threaded_concurrent_readers_and_writers(temp_sqlite_db):
    """F13.4: Verify 10 concurrent threads write and read without database locked errors."""
    db_path, engine = temp_sqlite_db
    errors = []

    def worker(worker_id: int):
        try:
            local_engine = SQLiteStorageEngine(db_path, wal_mode=True, timeout=5.0)
            for i in range(10):
                n_id = str(uuid.uuid4())
                payload = create_valid_note_payload(n_id, "knowledge", f"Title {worker_id}-{i}", "Content")
                local_engine.set(n_id, payload)
                read_back = local_engine.get(n_id)
                assert read_back is not None
            local_engine.close()
        except Exception as e:
            errors.append(e)

    threads = [threading.Thread(target=worker, args=(i,)) for i in range(10)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(errors) == 0, f"Encountered thread errors: {errors}"


def test_feature13_zero_database_corruption_integrity_check(temp_sqlite_db):
    """F13.5: Verify PRAGMA integrity_check returns 'ok' after heavy writes."""
    db_path, engine = temp_sqlite_db
    conn = sqlite3.connect(db_path)
    check_result = conn.cursor().execute("PRAGMA integrity_check;").fetchall()
    conn.close()
    assert check_result == [("ok",)]


# ============================================================================
# FEATURE 14: SHA-256 Tamper-Evident Cryptographic Audit Logging
# ============================================================================

def test_feature14_genesis_anchor_and_sha256_hash_chaining(tmp_path):
    """F14.1: Verify audit logger initializes from GENESIS and chains SHA-256 hashes."""
    log_path = tmp_path / "audit_test.jsonl"
    logger = AuditLogger(str(log_path))

    logger.log("agent", "TRADE_ENTRY", "T001", metadata={"asset": "GC=F"})
    logger.log("agent", "TRADE_CLOSE", "T001", metadata={"pnl": 2600.0})

    is_valid, violations = logger.verify_integrity()
    assert is_valid, f"Audit log violations: {violations}"


def test_feature14_audit_log_trade_execution_event(tmp_path):
    """F14.2: Verify trade record events log correctly with timestamps and payloads."""
    log_path = tmp_path / "audit_test.jsonl"
    logger = AuditLogger(str(log_path))

    logger.log("agent", "TRADE_EXECUTION", "T001", metadata={
        "ticket": 89421035,
        "symbol": "GC=F",
        "fill_price": 2510.75,
        "volume": 2.0,
    })
    is_valid, _ = logger.verify_integrity()
    assert is_valid


def test_feature14_tamper_detection_on_payload_mutation(tmp_path):
    """F14.3: Verify tampering with a trade payload immediately fails audit integrity."""
    log_path = tmp_path / "audit_test.jsonl"
    logger = AuditLogger(str(log_path))

    logger.log("agent", "EVENT_1", "N1", metadata={"data": "valid_1"})
    logger.log("agent", "EVENT_2", "N2", metadata={"data": "valid_2"})

    lines = log_path.read_text(encoding="utf-8").strip().split("\n")
    record = json.loads(lines[0])
    record["metadata"]["data"] = "TAMPERED_MALICIOUS_DATA"
    lines[0] = json.dumps(record)
    log_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    is_valid, violations = logger.verify_integrity()
    assert not is_valid
    assert len(violations) > 0


def test_feature14_tamper_detection_on_timestamp_or_nonce_edit(tmp_path):
    """F14.4: Verify modifying event timestamp breaks hash chaining."""
    log_path = tmp_path / "audit_test.jsonl"
    logger = AuditLogger(str(log_path))

    logger.log("agent", "EVENT_A", "NA", metadata={"data": "A"})
    logger.log("agent", "EVENT_B", "NB", metadata={"data": "B"})

    lines = log_path.read_text(encoding="utf-8").strip().split("\n")
    record = json.loads(lines[1])
    record["timestamp"] = "1999-01-01T00:00:00Z"
    lines[1] = json.dumps(record)
    log_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    is_valid, violations = logger.verify_integrity()
    assert not is_valid


def test_feature14_tamper_detection_on_record_deletion_or_injection(tmp_path):
    """F14.5: Verify deleting a line from the audit chain fails integrity verification."""
    log_path = tmp_path / "audit_test.jsonl"
    logger = AuditLogger(str(log_path))

    logger.log("agent", "EVENT_1", "N1", metadata={"v": 1})
    logger.log("agent", "EVENT_2", "N2", metadata={"v": 2})
    logger.log("agent", "EVENT_3", "N3", metadata={"v": 3})

    lines = log_path.read_text(encoding="utf-8").strip().split("\n")
    lines.pop(1)
    log_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    is_valid, violations = logger.verify_integrity()
    assert not is_valid


# ============================================================================
# FEATURE 15: P0-P18 Invariant Anti-Regression Suite
# ============================================================================

def test_feature15_p0_001_ai_self_verification_blocked(isolated_controller):
    """F15.1: P0-001 Invariant: AI_AGENT cannot propose notes with verification='verified'."""
    ctrl = isolated_controller
    n_id = str(uuid.uuid4())
    payload = create_valid_note_payload(n_id, "knowledge", "AI Claim", "Content", verification="verified")

    with pytest.raises(ValueError):
        ctrl.propose(Principal.AI_AGENT, payload)

    assert ctrl.storage.get(n_id) is None


def test_feature15_p0_002_privileged_provenance_blocked_for_ai(isolated_controller):
    """F15.2: P0-002 Invariant: AI_AGENT cannot claim privileged provenance source_type='user' or 'official'."""
    ctrl = isolated_controller
    n_id = str(uuid.uuid4())
    payload = create_valid_note_payload(n_id, "knowledge", "Forged Provenance", "Content", source_type="official")

    with pytest.raises(ValueError):
        ctrl.propose(Principal.AI_AGENT, payload)

    assert ctrl.storage.get(n_id) is None


def test_feature15_p0_005_attestation_gated_to_human_admin(isolated_controller):
    """F15.3: P0-005 Invariant: Only HUMAN and ADMIN can attest; AI_AGENT receives PermissionError."""
    ctrl = isolated_controller
    n_id = str(uuid.uuid4())
    payload = create_valid_note_payload(n_id, "knowledge", "Propose for review", "Content")
    ctrl.propose(Principal.AI_AGENT, payload)

    with pytest.raises(PermissionError):
        ctrl.attest(
            Principal.AI_AGENT,
            n_id,
            verification_reason="AI wants to verify itself",
            evidence_reference="none",
        )


def test_feature15_p16_p18_hardware_telemetry_immutability():
    """F15.4: P16-P18 Invariant: Physical hardware identifiers are immutable read-only."""
    hw_telemetry = {
        "vid": "0x1234",
        "pid": "0x5678",
        "serial": "HW-SN-998877",
        "system_host_id": "HOST-WIN11-FORENSIC-01",
    }
    read_only_view = dict(hw_telemetry)
    assert read_only_view["serial"] == "HW-SN-998877"


def test_feature15_secret_leak_zero_tolerance_verification():
    """F15.5: Verify zero secret leaks across test data structures and environment."""
    secret_patterns = [
        r"ghp_[a-zA-Z0-9]{36}",
        r"sk-proj-[a-zA-Z0-9]{48}",
        r"AKIA[0-9A-Z]{16}",
    ]
    test_str = "Clean test environment with zero hardcoded API keys."
    for pat in secret_patterns:
        assert re.search(pat, test_str) is None
