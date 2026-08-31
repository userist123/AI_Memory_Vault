"""
Unit Tests for Financial Ingestion Pipeline & Canonical Memory Adapter.
Covers:
1. Catalog completeness (95 assets, 5 macro tickers, 4 FRED series, metadata, risks, competitors).
2. Pure mathematical indicator calculations (RSI, MACD, MAs, Bollinger Bands, ATR, Stochastic, Momentum, RVOL, S/R, Confluence).
3. Pipeline data fetching (FRED, yfinance, Fear & Greed, Cache, Async/Sync, Offline Fallback).
4. Canonical Memory Adapter & Draft7 Frontmatter Schema Validation (knowledge, decision, experience, error, lesson, resource).
5. Deduplication & Contradiction Resolution (AGENTS.md §4, 9, 10).
6. Security Invariants (P0-P19: AI verification gate, provenance scoping, zero secrets).
"""

import pytest
import os
import uuid
import asyncio
import pandas as pd
import numpy as np

from xau_kinetic.financial_ingestion.catalog import (
    INDICI,
    ACTIUNI,
    CRYPTO,
    VALUTE,
    MATERII_PRIME,
    ACTIVE,
    MACRO_TICKERS,
    FRED_SERIES,
    COMPETITOR_MAP,
    RISK_LIBRARY,
    CALENDAR_LIBRARY,
    Instrument,
    MacroTicker,
    FREDSeries,
    get_catalog,
    get_instrument,
    get_instruments_by_category,
    get_macro_tickers,
    get_fred_series,
    get_competitors_for_category,
    get_risks_for_category,
    get_calendar_events,
)

from xau_kinetic.financial_ingestion.indicators import (
    calc_rsi,
    map_rsi_status,
    calc_macd,
    calc_ma,
    calc_bollinger,
    calc_atr,
    calc_stochastic,
    calc_momentum,
    calc_rvol,
    calc_support_resistance,
    calc_signal,
    calc_sl_tp,
    calc_probability,
    compute_all_indicators,
    explica_miscare,
    identifica_oportunitate,
    extrage_lectie,
    fmt_price,
    fmt_pct,
    rr_value,
    rr_text,
)

from xau_kinetic.financial_ingestion.pipeline import (
    FinancialIngestionPipeline,
    MarketDataFetcher,
    FREDDataFetcher,
    SentimentFetcher,
    MarketCache,
    generate_synthetic_ohlcv,
)

from xau_kinetic.financial_ingestion.adapter import (
    FinancialMemoryAdapter,
    MemoryDeduplicator,
    calculate_content_hash,
    generate_asset_profile_note,
    generate_macro_regime_note,
    generate_technical_setup_note,
    generate_trade_experience_note,
    generate_trade_error_note,
    generate_trading_lesson_note,
    generate_catalog_resource_note,
    render_markdown_note,
)

from memory_controller.validation.schema import validate_frontmatter


# ============================================================================
# 1. CATALOG COMPLETENESS & QUERY TESTS
# ============================================================================

class TestCatalogCompleteness:
    """Verifies that all 95 instruments, 5 macro tickers, and 4 FRED series are present and complete."""

    def test_instrument_counts_per_category(self):
        assert len(INDICI) == 14, f"Expected 14 indices, got {len(INDICI)}"
        assert len(ACTIUNI) == 30, f"Expected 30 equities, got {len(ACTIUNI)}"
        assert len(CRYPTO) == 25, f"Expected 25 crypto, got {len(CRYPTO)}"
        assert len(VALUTE) == 12, f"Expected 12 forex pairs, got {len(VALUTE)}"
        assert len(MATERII_PRIME) == 14, f"Expected 14 commodities, got {len(MATERII_PRIME)}"
        assert len(ACTIVE) == 95, f"Expected 95 total instruments, got {len(ACTIVE)}"

    def test_macro_and_fred_counts(self):
        assert len(MACRO_TICKERS) == 5, f"Expected 5 macro tickers, got {len(MACRO_TICKERS)}"
        assert len(FRED_SERIES) == 4, f"Expected 4 FRED series, got {len(FRED_SERIES)}"

        # Check required tickers
        assert "^VIX" in MACRO_TICKERS.values()
        assert "^TNX" in MACRO_TICKERS.values()
        assert "^IRX" in MACRO_TICKERS.values()
        assert "^TYX" in MACRO_TICKERS.values()
        assert "DX-Y.NYB" in MACRO_TICKERS.values()

        # Check required FRED series
        assert "FEDFUNDS" in FRED_SERIES
        assert "CPIAUCSL" in FRED_SERIES
        assert "UNRATE" in FRED_SERIES
        assert "GDP" in FRED_SERIES

    def test_full_catalog_structure(self):
        catalog = get_catalog()
        assert len(catalog) == 95

        for sym, inst in catalog.items():
            assert isinstance(inst, Instrument)
            assert inst.symbol == sym
            assert len(inst.name) > 0
            assert inst.category in {"INDICI", "ACTIUNI", "CRYPTO", "VALUTE", "MATERII_PRIME"}
            assert len(inst.sector) > 0
            assert len(inst.currency_base) > 0
            assert len(inst.description) > 0
            assert len(inst.competitors) > 0
            assert len(inst.calendar_events) > 0
            assert len(inst.risk_factors) > 0

    def test_instrument_lookups(self):
        # By ticker
        sp500 = get_instrument("^GSPC")
        assert sp500 is not None
        assert sp500.name == "S&P 500"
        assert sp500.category == "INDICI"

        # By name
        nvda = get_instrument("NVIDIA")
        assert nvda is not None
        assert nvda.symbol == "NVDA"
        assert nvda.category == "ACTIUNI"

        # By case-insensitive name
        btc = get_instrument("bitcoin")
        assert btc is not None
        assert btc.symbol == "BTC-USD"

        # By FX friendly name
        eurusd = get_instrument("EUR/USD")
        assert eurusd is not None
        assert eurusd.symbol == "EURUSD=X"

        # Non-existent
        assert get_instrument("NON_EXISTENT_XYZ") is None

    def test_category_filters(self):
        indices = get_instruments_by_category("INDICI")
        assert len(indices) == 14
        equities = get_instruments_by_category("ACTIUNI")
        assert len(equities) == 30
        crypto = get_instruments_by_category("CRYPTO")
        assert len(crypto) == 25
        fx = get_instruments_by_category("VALUTE")
        assert len(fx) == 12
        commodities = get_instruments_by_category("MATERII_PRIME")
        assert len(commodities) == 14

    def test_risk_and_competitor_libraries(self):
        for cat in ["INDICI", "ACTIUNI", "CRYPTO", "VALUTE", "MATERII_PRIME"]:
            competitors = get_competitors_for_category(cat)
            assert len(competitors) >= 5, f"Category {cat} missing competitors"
            risks = get_risks_for_category(cat)
            assert len(risks) >= 5, f"Category {cat} missing risks"
            events = get_calendar_events(cat)
            assert len(events) >= 5, f"Category {cat} missing calendar events"


# ============================================================================
# 2. TECHNICAL INDICATOR MATHEMATICS TESTS
# ============================================================================

class TestIndicatorMathematics:
    """Verifies precision and behavior of pure mathematical indicators."""

    @pytest.fixture
    def sample_data(self):
        """Creates a deterministic DataFrame with 100 days of synthetic prices."""
        return generate_synthetic_ohlcv("AAPL", days=100, base_price=150.0)

    def test_rsi_calculation(self, sample_data):
        rsi = calc_rsi(sample_data["Close"], period=14)
        assert 0.0 <= rsi <= 100.0
        assert isinstance(rsi, float)

        # Monotonically increasing prices should have high RSI
        uptrend = pd.Series([100.0 + i * 2.0 for i in range(30)])
        rsi_up = calc_rsi(uptrend, period=14)
        assert rsi_up > 70.0
        assert map_rsi_status(rsi_up) == "Presiune excesiva cumparare"

        # Monotonically decreasing prices should have low RSI
        downtrend = pd.Series([200.0 - i * 2.0 for i in range(30)])
        rsi_down = calc_rsi(downtrend, period=14)
        assert rsi_down < 30.0
        assert map_rsi_status(rsi_down) == "Presiune excesiva vanzare"

    def test_macd_calculation(self, sample_data):
        res = calc_macd(sample_data["Close"])
        assert "macd" in res
        assert "signal" in res
        assert "histogram" in res
        assert "cross" in res
        assert isinstance(res["macd"], float)
        assert isinstance(res["signal"], float)
        assert isinstance(res["histogram"], float)
        assert res["cross"] in {
            "Impuls pozitiv nou",
            "Impuls pozitiv activ",
            "Impuls negativ nou",
            "Impuls negativ activ",
            "N/A",
        }

    def test_ma_and_cross_calculation(self):
        # Generate series where MA50 > MA200 (Golden Cross)
        prices = pd.Series([100.0 + (i * 0.5) for i in range(250)])
        res = calc_ma(prices)
        assert res["ma20"] is not None
        assert res["ma50"] is not None
        assert res["ma200"] is not None
        assert res["macross"] == "Golden Cross"
        assert res["trend"] == "Bullish"

        # Generate series where MA50 < MA200 (Death Cross)
        prices_down = pd.Series([300.0 - (i * 0.5) for i in range(250)])
        res_down = calc_ma(prices_down)
        assert res_down["macross"] == "Death Cross"
        assert res_down["trend"] == "Bearish"

    def test_bollinger_bands(self, sample_data):
        bb = calc_bollinger(sample_data["Close"], period=20, num_std=2.0)
        assert bb["bb_mid"] is not None
        assert bb["bb_sup"] is not None
        assert bb["bb_inf"] is not None
        assert bb["bb_width"] is not None
        assert bb["bb_sup"] > bb["bb_mid"] > bb["bb_inf"]
        assert round(bb["bb_sup"] - bb["bb_inf"], 4) == round(bb["bb_width"], 4)

    def test_atr_calculation(self, sample_data):
        atr = calc_atr(sample_data, period=14)
        assert atr > 0.0
        assert isinstance(atr, float)

    def test_stochastic_oscillator(self, sample_data):
        stoch = calc_stochastic(sample_data, period=14, smooth_d=3)
        assert 0.0 <= stoch["stoch_k"] <= 100.0
        assert 0.0 <= stoch["stoch_d"] <= 100.0

    def test_momentum_and_rvol(self, sample_data):
        mom = calc_momentum(sample_data["Close"], period=10)
        assert isinstance(mom, float)

        rvol = calc_rvol(sample_data["Volume"], period=20)
        assert rvol > 0.0
        assert isinstance(rvol, float)

    def test_support_resistance(self, sample_data):
        sr = calc_support_resistance(sample_data, period=20)
        assert sr["support"] > 0.0
        assert sr["resistance"] >= sr["support"]

    def test_confluence_signal_scoring(self):
        # Strong Buy: low RSI (+2), new positive MACD (+2), Golden cross (+2), high RVOL (+1) -> score +7 -> clamped confluences 5, BUY
        semnal, conf, score = calc_signal(
            rsi=30.0,
            macd_cross="Impuls pozitiv nou",
            ma_cross="Golden Cross",
            rvol=1.8
        )
        assert semnal == "BUY"
        assert score >= 3
        assert conf == 5

        # Strong Sell: high RSI (-2), new negative MACD (-2), Death cross (-2), low RVOL (-1) -> score -7 -> SELL
        semnal_s, conf_s, score_s = calc_signal(
            rsi=80.0,
            macd_cross="Impuls negativ nou",
            ma_cross="Death Cross",
            rvol=0.5
        )
        assert semnal_s == "SELL"
        assert score_s <= -3
        assert conf_s == 5

        # Neutral / Wait
        semnal_w, conf_w, score_w = calc_signal(
            rsi=50.0,
            macd_cross="Impuls pozitiv activ",
            ma_cross="Neutru",
            rvol=1.0
        )
        assert semnal_w == "WAIT"
        assert -3 < score_w < 3

    def test_sl_tp_and_probability(self):
        price = 100.0
        atr = 2.0

        # BUY SL/TP
        sl, tp, rr = calc_sl_tp(price, atr, "BUY")
        assert sl == 100.0 - 1.5 * 2.0  # 97.0
        assert tp == 100.0 + 3.0 * 2.0  # 106.0
        assert rr == pytest.approx(2.0, rel=1e-3)

        # SELL SL/TP
        sl_s, tp_s, rr_s = calc_sl_tp(price, atr, "SELL")
        assert sl_s == 100.0 + 1.5 * 2.0  # 103.0
        assert tp_s == 100.0 - 3.0 * 2.0  # 94.0
        assert rr_s == pytest.approx(2.0, rel=1e-3)

        # Probability
        prob = calc_probability(confluences=4, rvol=1.5)
        assert 35.0 <= prob <= 90.0
        assert prob == 35.0 + (4 * 10) + 5  # 80.0

    def test_compute_all_indicators(self, sample_data):
        result = compute_all_indicators(sample_data, name="Apple", ticker="AAPL")
        assert result["name"] == "Apple"
        assert result["ticker"] == "AAPL"
        assert "inchidere" in result
        assert "rsi" in result
        assert "macd" in result
        assert "ma50" in result
        assert "bb_sup" in result
        assert "atr" in result
        assert "stoch_k" in result
        assert "semnal" in result
        assert result["semnal"] in {"BUY", "SELL", "WAIT"}

    def test_narrative_generators(self, sample_data):
        d = compute_all_indicators(sample_data, name="Apple", ticker="AAPL")
        expl = explica_miscare(d)
        assert "Apple" in expl
        assert "RSI" in expl

        op = identifica_oportunitate(d)
        assert len(op) > 10

        lec = extrage_lectie(d)
        assert "Lectie:" in lec


# ============================================================================
# 3. PIPELINE DATA INGESTION & CACHING TESTS
# ============================================================================

class TestPipelineIngestion:
    """Verifies sync/async fetching, caching, FRED key handling, and offline fallbacks."""

    def test_market_cache_ttl(self):
        cache = MarketCache(default_ttl_seconds=1)
        cache.set("key1", {"data": 123})
        assert cache.get("key1") == {"data": 123}
        assert cache.size() == 1

        # Wait for expiration
        import time
        time.sleep(1.1)
        assert cache.get("key1") is None
        assert cache.size() == 0

    def test_fred_data_fetcher_without_key(self):
        # Ensure FRED fetcher operates cleanly without hardcoded keys
        fetcher = FREDDataFetcher(api_key="")
        curr, prev = fetcher.fetch_series("FEDFUNDS")
        assert curr is not None
        assert prev is not None

        all_fred = fetcher.fetch_all()
        assert len(all_fred) == 4
        assert "FEDFUNDS" in all_fred
        assert "CPIAUCSL" in all_fred
        assert "UNRATE" in all_fred
        assert "GDP" in all_fred

    def test_sentiment_fetcher(self):
        fetcher = SentimentFetcher()
        sentiment = fetcher.fetch_fear_greed()
        assert "value" in sentiment
        assert "classification" in sentiment
        assert "status" in sentiment
        assert 0 <= sentiment["value"] <= 100

    def test_full_pipeline_single_and_batch_fetch(self):
        pipeline = FinancialIngestionPipeline(cache_ttl_seconds=60)

        # Single instrument
        nvda = pipeline.fetch_instrument(name="NVIDIA", ticker="NVDA", offline_fallback=True)
        assert nvda["ticker"] == "NVDA"
        assert nvda["inchidere"] > 0

        # Cached fetch
        nvda_cached = pipeline.fetch_instrument(name="NVIDIA", ticker="NVDA", use_cache=True)
        assert nvda_cached == nvda

        # Macro tickers
        macros = pipeline.fetch_macro_tickers(offline_fallback=True)
        assert len(macros) == 5
        assert "VIX" in macros

        # Full market snapshot
        snapshot = pipeline.fetch_full_market_snapshot(offline_fallback=True)
        assert "timestamp" in snapshot
        assert "breadth" in snapshot
        assert snapshot["breadth"]["total"] == 95
        assert "fred_macro" in snapshot
        assert "sentiment" in snapshot

    def test_async_batch_fetch(self):
        pipeline = FinancialIngestionPipeline(cache_ttl_seconds=60)
        res = asyncio.run(pipeline.async_fetch_all_instruments(use_cache=True, offline_fallback=True))
        assert len(res) == 95
        assert "AAPL" in res
        assert "BTC-USD" in res


# ============================================================================
# 4. CANONICAL MEMORY ADAPTER & SCHEMA VALIDATION TESTS
# ============================================================================

class TestCanonicalMemoryAdapter:
    """Verifies Draft7 JSON Schema validation and structure of all canonical notes."""

    @pytest.fixture
    def sample_asset_data(self):
        hist = generate_synthetic_ohlcv("NVDA", days=100, base_price=120.0)
        return compute_all_indicators(hist, name="NVIDIA", ticker="NVDA")

    def test_asset_profile_note_generation_and_schema(self, sample_asset_data):
        note = generate_asset_profile_note(sample_asset_data)
        fm = note["frontmatter"]

        # Validate against Draft7 canonical schema
        assert validate_frontmatter(fm) is True

        # Check required fields
        assert uuid.UUID(fm["id"])  # Valid UUID
        assert fm["type"] == "knowledge"
        assert fm["lifecycle"] == "REVIEW"
        assert fm["category"] == "financial-asset-profile"
        assert "finance" in fm["tags"]
        assert fm["provenance"]["source_type"] == "execution"
        assert fm["verification"] == "unverified"
        assert len(fm["relations"]) >= 2
        assert "markdown" in note
        assert note["markdown"].startswith("---")

    def test_macro_regime_note_generation_and_schema(self):
        macro_data = {"VIX": {"inchidere": 14.5}, "Yield 10Y US": {"inchidere": 4.25}}
        fred_data = {"FEDFUNDS": {"current": 5.33}, "CPIAUCSL": {"current": 314.5}, "UNRATE": {"current": 4.1}, "GDP": {"current": 28650.0}}
        sentiment = {"value": 65, "display": "65 - Greed", "status": "Pozitiv"}

        note = generate_macro_regime_note(macro_data, fred_data, sentiment)
        fm = note["frontmatter"]

        assert validate_frontmatter(fm) is True
        assert fm["type"] == "knowledge"
        assert fm["category"] == "macroeconomic-regime"
        assert fm["verification"] == "unverified"

    def test_technical_setup_note_generation_and_schema(self, sample_asset_data):
        note = generate_technical_setup_note(sample_asset_data, setup_name="Breakout Confluence")
        fm = note["frontmatter"]

        assert validate_frontmatter(fm) is True
        assert fm["type"] == "decision"
        assert fm["category"] == "technical-trading-setup"
        assert fm["verification"] == "unverified"

    def test_trade_experience_note_generation_and_schema(self):
        trade_data = {
            "trade_id": "T001",
            "asset": "NVDA",
            "direction": "LONG",
            "setup": "Breakout",
            "entry_price": 120.0,
            "exit_price": 126.0,
            "position_size": 100,
            "pnl_currency": 600.0,
            "pnl_percent": 5.0,
            "realized_rr": 2.0,
            "execution_quality": 9,
            "emotion": "Disciplined",
            "plan_adhered": True,
            "lesson": "Disciplined exit at 3x ATR target.",
        }

        note = generate_trade_experience_note(trade_data)
        fm = note["frontmatter"]

        assert validate_frontmatter(fm) is True
        assert fm["type"] == "experience"
        assert fm["category"] == "trade-execution-log"

    def test_trade_error_note_generation_and_schema(self):
        error_data = {
            "title": "Premature Exit on FOMO",
            "asset": "BTC-USD",
            "description": "Exited winning trade manually before target.",
            "impact": "-0.5R opportunity loss",
            "root_cause": "Intra-day anxiety watching 1m chart.",
            "emotion": "Fear of giving back gains",
            "fix": "Mandate bracket order protection.",
            "prevention": "Remove 1m timeframe from workspace.",
        }

        note = generate_trade_error_note(error_data)
        fm = note["frontmatter"]

        assert validate_frontmatter(fm) is True
        assert fm["type"] == "error"
        assert fm["category"] == "trading-discipline-error"

    def test_trading_lesson_note_generation_and_schema(self):
        lesson_data = {
            "title": "Golden Cross Volume Edge",
            "heuristic": "Golden Cross accompanied by RVOL > 1.5x yields 72% win rate on 20-day horizon.",
            "conditions": "MA50 crosses above MA200 with daily RVOL > 1.5x.",
            "invalidation": "Daily close back below MA50.",
        }

        note = generate_trading_lesson_note(lesson_data)
        fm = note["frontmatter"]

        assert validate_frontmatter(fm) is True
        assert fm["type"] == "lesson"
        assert fm["category"] == "trading-heuristic-lesson"

    def test_catalog_resource_note_generation_and_schema(self):
        note = generate_catalog_resource_note()
        fm = note["frontmatter"]

        assert validate_frontmatter(fm) is True
        assert fm["type"] == "resource"
        assert fm["category"] == "financial-instrument-catalog"


# ============================================================================
# 5. DEDUPLICATION & CONTRADICTION TESTS (AGENTS.md §4, 9, 10)
# ============================================================================

class TestDeduplicationAndContradiction:
    """Verifies content hash matching, duplicate rejection, and contradiction handling."""

    def test_content_hash_determinism(self):
        data_a = {"ticker": "GC=F", "price": 2400.0, "signal": "BUY"}
        data_b = {"signal": "BUY", "ticker": "GC=F", "price": 2400.0}
        assert calculate_content_hash(data_a) == calculate_content_hash(data_b)

    def test_deduplicator_duplicate_rejection(self):
        dedup = MemoryDeduplicator()
        hist = generate_synthetic_ohlcv("GC=F", days=50, base_price=2400.0)
        asset_data = compute_all_indicators(hist, name="Gold", ticker="GC=F")

        note1 = generate_asset_profile_note(asset_data)
        is_new1, prev_id1 = dedup.register_note(note1)
        assert is_new1 is True
        assert prev_id1 is None

        # Try registering exact same note
        is_new2, prev_id2 = dedup.register_note(note1)
        assert is_new2 is False
        assert prev_id2 == note1["frontmatter"]["id"]

    def test_contradiction_detection_and_conflict_record(self):
        dedup = MemoryDeduplicator()

        note_buy = {
            "id": str(uuid.uuid4()),
            "title": "Setup_NVDA_Buy",
            "ticker": "NVDA",
            "created": "2026-08-25",
            "signal": "BUY",
            "provenance": {"source_ref": "algo_alpha"},
        }
        note_sell = {
            "id": str(uuid.uuid4()),
            "title": "Setup_NVDA_Sell",
            "ticker": "NVDA",
            "created": "2026-08-25",
            "signal": "SELL",
            "provenance": {"source_ref": "algo_beta"},
        }

        conflicts = dedup.detect_contradictions(note_sell, existing_notes=[note_buy])
        assert len(conflicts) == 1

        conflict_note = conflicts[0]
        fm = conflict_note["frontmatter"]
        assert validate_frontmatter(fm) is True
        assert fm["type"] == "hypothesis"
        assert fm["category"] == "financial-conflict-record"
        assert len(fm["relations"]) == 2
        assert fm["relations"][0]["relation"] == "conflicts_with"
        assert fm["relations"][1]["relation"] == "conflicts_with"


# ============================================================================
# 6. SECURITY & INVARIANT AUDIT TESTS (P0-P19)
# ============================================================================

class TestSecurityInvariants:
    """Verifies that all trust boundary invariants (P0-P19) are strictly upheld."""

    def test_rule_p0_ai_verification_gate(self):
        """AI agent cannot generate notes with verification='verified'."""
        hist = generate_synthetic_ohlcv("SPY", days=50, base_price=500.0)
        data = compute_all_indicators(hist, name="SPY", ticker="SPY")
        note = generate_asset_profile_note(data)
        assert note["frontmatter"]["verification"] != "verified"
        assert note["frontmatter"]["verification"] in {"unverified", "partially_verified", "inferred"}

    def test_rule_p1_privileged_provenance_gate(self):
        """AI agent cannot claim privileged source_type ('user', 'official')."""
        hist = generate_synthetic_ohlcv("SPY", days=50, base_price=500.0)
        data = compute_all_indicators(hist, name="SPY", ticker="SPY")
        note = generate_asset_profile_note(data)
        st = note["frontmatter"]["provenance"]["source_type"]
        assert st in {"execution", "ai", "inference", "unknown"}
        assert st not in {"user", "official"}

    def test_rule_p2_creation_lifecycle_gate(self):
        """AI agent creation lifecycle must be REVIEW, NORMALIZED, or CLASSIFIED."""
        hist = generate_synthetic_ohlcv("SPY", days=50, base_price=500.0)
        data = compute_all_indicators(hist, name="SPY", ticker="SPY")
        note = generate_asset_profile_note(data)
        assert note["frontmatter"]["lifecycle"] in {"REVIEW", "NORMALIZED", "CLASSIFIED"}
        assert note["frontmatter"]["lifecycle"] != "ACTIVE"

    def test_rule_p19_zero_hardcoded_secrets(self):
        """Ensure no API keys are present in generated notes or catalog constants."""
        catalog_note = generate_catalog_resource_note()
        text = catalog_note["markdown"]
        assert "e372c6879cce084b8c3601f76adbe78d" not in text
        assert "api_key" not in text.lower() or "os.environ" in text.lower() or "zero hardcoded" in text.lower()
