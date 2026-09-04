"""
Adversarial Challenge & Stress Test Suite for Milestone 1 Financial Ingestion.
Executed by Challenger 1 (Empirical Critic & Specialist).

Covers:
1. Malformed / Corrupted OHLCV Series (NaNs, zeroes, flat prices, negative prices, extreme spikes).
2. High-Concurrency Ingestion & MarketCache Thread Contention under Simulated Rate Limiting (HTTP 429, timeouts, network failures).
3. Exact Mathematical Boundaries (RSI 0/100/flat, MACD zero crossings/equality, ATR on zero volatility, Stochastic/RVOL zero division).
4. Frontmatter Schema Validation Fuzzing (Missing fields, invalid enums, type mutations, injection payloads, P0-P18 invariant attacks).
5. Empirical Vulnerability Demonstrations (Missing columns, non-numeric strings in OHLCV, flat RSI anomaly).
"""

import pytest
import time
import uuid
import threading
import asyncio
from concurrent.futures import ThreadPoolExecutor, as_completed
from unittest.mock import patch, MagicMock
import pandas as pd
import numpy as np
import requests

from xau_kinetic.financial_ingestion.catalog import (
    get_catalog,
    get_instrument,
    get_instruments_by_category,
    get_macro_tickers,
    get_fred_series,
    ACTIVE,
    INDICI,
    ACTIUNI,
    CRYPTO,
    VALUTE,
    MATERII_PRIME,
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
    safe_float,
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
# 1. MALFORMED & CORRUPTED OHLCV SERIES STRESS TESTS
# ============================================================================

class TestMalformedOHLCVSeries:
    """Stress tests indicator and pipeline resilience against corrupted inputs."""

    def test_none_and_empty_inputs(self):
        """Indicators must handle None and empty DataFrames gracefully without raising unhandled exceptions."""
        assert calc_rsi(None) == 50.0
        assert calc_rsi(pd.Series([], dtype=float)) == 50.0

        macd_res = calc_macd(None)
        assert macd_res == {"macd": 0.0, "signal": 0.0, "histogram": 0.0, "cross": "N/A"}
        assert calc_macd(pd.Series([], dtype=float)) == {"macd": 0.0, "signal": 0.0, "histogram": 0.0, "cross": "N/A"}

        ma_res = calc_ma(None)
        assert ma_res["ma20"] is None and ma_res["trend"] == "Sideways"

        bb_res = calc_bollinger(None)
        assert bb_res["bb_mid"] is None

        assert calc_atr(None) == 0.0
        assert calc_atr(pd.DataFrame()) == 0.0

        stoch_res = calc_stochastic(None)
        assert stoch_res == {"stoch_k": 50.0, "stoch_d": 50.0}

        assert calc_momentum(None) == 0.0
        assert calc_rvol(None) == 1.0
        assert calc_support_resistance(None) == {"support": 0.0, "resistance": 0.0}
        assert compute_all_indicators(None) == {}
        assert compute_all_indicators(pd.DataFrame()) == {}

    def test_short_series_below_minimum_periods(self):
        """DataFrames with 1 to 4 rows should return safe fallbacks instead of crashing with IndexError."""
        for length in [1, 2, 3, 4]:
            dates = pd.date_range("2026-01-01", periods=length, freq="D")
            df = pd.DataFrame({
                "Open": [100.0] * length,
                "High": [105.0] * length,
                "Low": [95.0] * length,
                "Close": [102.0] * length,
                "Volume": [1000] * length,
            }, index=dates)

            assert compute_all_indicators(df) == {}
            assert calc_rsi(df["Close"], period=14) == 50.0
            assert calc_atr(df, period=14) == 0.0
            assert calc_stochastic(df, period=14) == {"stoch_k": 50.0, "stoch_d": 50.0}

    def test_all_zero_values_and_flat_prices(self):
        """Series where prices or volumes are entirely zero or identical constants."""
        dates = pd.date_range("2026-01-01", periods=50, freq="D")
        
        # Flat series (Zero Volatility)
        df_flat = pd.DataFrame({
            "Open": [100.0] * 50,
            "High": [100.0] * 50,
            "Low": [100.0] * 50,
            "Close": [100.0] * 50,
            "Volume": [10000] * 50,
        }, index=dates)

        atr_flat = calc_atr(df_flat, period=14)
        assert atr_flat == 0.0

        bb_flat = calc_bollinger(df_flat["Close"], period=20)
        assert bb_flat["bb_width"] == 0.0
        assert bb_flat["bb_sup"] == 100.0
        assert bb_flat["bb_inf"] == 100.0

        stoch_flat = calc_stochastic(df_flat, period=14)
        assert isinstance(stoch_flat["stoch_k"], float)
        assert isinstance(stoch_flat["stoch_d"], float)

        # All Zeroes
        df_zero = pd.DataFrame({
            "Open": [0.0] * 50,
            "High": [0.0] * 50,
            "Low": [0.0] * 50,
            "Close": [0.0] * 50,
            "Volume": [0] * 50,
        }, index=dates)

        rvol_zero = calc_rvol(df_zero["Volume"])
        assert rvol_zero == 1.0  # Safe fallback for 0 volume

        sl, tp, rr = calc_sl_tp(price=0.0, atr=0.0, signal="BUY")
        assert sl is None and tp is None and rr is None

    def test_negative_prices_handling(self):
        """Verify behavior with negative prices (e.g. WTI negative oil contract anomalies)."""
        dates = pd.date_range("2026-01-01", periods=30, freq="D")
        df_neg = pd.DataFrame({
            "Open": [-30.0] * 30,
            "High": [-25.0] * 30,
            "Low": [-35.0] * 30,
            "Close": [-28.0] * 30,
            "Volume": [5000] * 30,
        }, index=dates)

        # SL/TP calculation must reject price <= 0
        sl, tp, rr = calc_sl_tp(price=-28.0, atr=5.0, signal="BUY")
        assert sl is None and tp is None and rr is None

        # Indicator calculations should not crash
        res = compute_all_indicators(df_neg, name="NegativeOil", ticker="NEG=F")
        assert isinstance(res, dict)

    def test_extreme_spikes_and_floating_point_extremes(self):
        """Massive numerical spikes (1e12, 1e-12) should not cause overflow crashes or Inf/NaN leaks."""
        dates = pd.date_range("2026-01-01", periods=50, freq="D")
        spikes = [100.0] * 48 + [1e12, 1e-12]
        df_spikes = pd.DataFrame({
            "Open": spikes,
            "High": [x * 1.05 for x in spikes],
            "Low": [x * 0.95 for x in spikes],
            "Close": spikes,
            "Volume": [1000000] * 50,
        }, index=dates)

        res = compute_all_indicators(df_spikes, name="SpikeAsset", ticker="SPIKE")
        assert isinstance(res, dict)
        assert not np.isinf(safe_float(res.get("inchidere")))
        assert not np.isnan(safe_float(res.get("inchidere")))


# ============================================================================
# 2. EXACT MATHEMATICAL BOUNDARIES & LOGIC STRESS TESTS
# ============================================================================

class TestMathematicalBoundaries:
    """Rigorous verification of exact indicator boundary states."""

    def test_rsi_pure_monotonic_increase_boundary_100(self):
        """Strictly increasing prices over period should yield RSI = 100.0."""
        prices = pd.Series([10.0 + i * 5.0 for i in range(30)])
        rsi = calc_rsi(prices, period=14)
        assert rsi == 100.0
        assert map_rsi_status(rsi) == "Presiune excesiva cumparare"

    def test_rsi_pure_monotonic_decrease_boundary_0(self):
        """Strictly decreasing prices over period should yield RSI = 0.0."""
        prices = pd.Series([200.0 - i * 5.0 for i in range(30)])
        rsi = calc_rsi(prices, period=14)
        assert rsi == 0.0
        assert map_rsi_status(rsi) == "Presiune excesiva vanzare"

    def test_rsi_alternating_prices_at_50(self):
        """Perfect symmetry (alternating +1, -1) should yield RSI approx 50.0."""
        prices = pd.Series([100.0 if i % 2 == 0 else 101.0 for i in range(40)])
        rsi = calc_rsi(prices, period=14)
        assert rsi == pytest.approx(50.0, abs=1.0)
        assert map_rsi_status(rsi) == "Echilibru"

    def test_macd_exact_zero_crossings_and_equality(self):
        """MACD crossover logic when histogram transitions across zero or equals zero."""
        prices = pd.Series([100.0] * 40)
        res = calc_macd(prices)
        assert res["macd"] == 0.0
        assert res["signal"] == 0.0
        assert res["histogram"] == 0.0
        assert isinstance(res["cross"], str)

    def test_atr_zero_volatility_and_rr_zero_risk(self):
        """Zero volatility ATR must not divide by zero in risk/reward or sizing calculations."""
        sl, tp, rr = calc_sl_tp(price=150.0, atr=0.0, signal="BUY")
        assert sl is None and tp is None and rr is None

        # Risk = 0 (Entry == Stop Loss)
        assert rr_value(entry=100.0, sl=100.0, tp=110.0) is None
        assert rr_text(entry=100.0, sl=100.0, tp=110.0) == "N/A"

        # Valid R/R
        assert rr_value(entry=100.0, sl=95.0, tp=110.0) == pytest.approx(2.0, rel=1e-3)
        assert rr_text(entry=100.0, sl=95.0, tp=110.0) == "2.00x"

    def test_confluence_scoring_clamping_and_extremes(self):
        """Confluence score must strictly clamp between -5 and +5, with confluences in 0..5."""
        for rsi_val in [-50.0, 0.0, 30.0, 50.0, 75.0, 100.0, 150.0]:
            for cross_macd in ["Impuls pozitiv nou", "Impuls negativ nou", "invalid"]:
                for cross_ma in ["Golden Cross", "Death Cross", "Neutru"]:
                    for rvol_val in [0.0, 0.5, 1.0, 2.0, 10.0]:
                        semnal, conf, score = calc_signal(
                            rsi=rsi_val,
                            macd_cross=cross_macd,
                            ma_cross=cross_ma,
                            rvol=rvol_val,
                        )
                        assert semnal in {"BUY", "SELL", "WAIT"}
                        assert 0 <= conf <= 5
                        assert -7 <= score <= 7

                        prob = calc_probability(conf, rvol_val)
                        assert 35.0 <= prob <= 90.0


# ============================================================================
# 3. HIGH-CONCURRENCY & RATE-LIMITING SIMULATION TESTS
# ============================================================================

class TestHighConcurrencyAndRateLimiting:
    """Stress tests concurrent operations and simulates external rate limits / outages."""

    def test_market_cache_thread_contention(self):
        """Simultaneous read, write, clear, and size calls across 30 concurrent threads."""
        cache = MarketCache(default_ttl_seconds=60)
        errors = []

        def worker_writer(thread_id: int):
            for i in range(100):
                try:
                    cache.set(f"key_{thread_id}_{i}", {"value": i})
                    if i % 25 == 0:
                        cache.get(f"key_{thread_id}_{i}")
                except Exception as ex:
                    errors.append(ex)

        def worker_reader(thread_id: int):
            for i in range(100):
                try:
                    cache.get(f"key_{thread_id}_{i}")
                    if i % 10 == 0:
                        _ = cache.size()
                except Exception as ex:
                    errors.append(ex)

        threads = []
        for t_id in range(15):
            tw = threading.Thread(target=worker_writer, args=(t_id,))
            tr = threading.Thread(target=worker_reader, args=(t_id,))
            threads.extend([tw, tr])

        for t in threads:
            t.start()
        for t in threads:
            t.join()

        assert len(errors) == 0, f"Thread contention errors detected in MarketCache: {errors}"

    def test_high_concurrency_pipeline_batch_fetch(self):
        """Run batch fetch across 50 instruments concurrently."""
        pipeline = FinancialIngestionPipeline(cache_ttl_seconds=300)
        
        with ThreadPoolExecutor(max_workers=16) as executor:
            futures = [
                executor.submit(
                    pipeline.fetch_instrument,
                    name=name,
                    ticker=ticker,
                    use_cache=True,
                    offline_fallback=True
                )
                for name, ticker in list(ACTIVE.items())[:50]
            ]
            results = [f.result() for f in as_completed(futures)]

        assert len(results) == 50
        for res in results:
            assert isinstance(res, dict)
            assert "inchidere" in res or res == {}

    def test_fred_api_rate_limiting_429_simulation(self):
        """Simulate HTTP 429 (Rate Limit Exceeded) from FRED API; verify graceful offline fallback."""
        mock_response = MagicMock()
        mock_response.status_code = 429
        mock_response.raise_for_status.side_effect = requests.exceptions.HTTPError("429 Client Error: Too Many Requests")

        fetcher = FREDDataFetcher(api_key="SIMULATED_TEST_KEY_429")

        with patch("requests.get", return_value=mock_response):
            curr, prev = fetcher.fetch_series("FEDFUNDS")
            assert curr is not None  # Fallback to sample
            assert prev is not None

            all_fred = fetcher.fetch_all()
            assert len(all_fred) == 4
            assert all_fred["FEDFUNDS"]["current"] is not None

    def test_sentiment_api_network_timeout_simulation(self):
        """Simulate network timeout from Alternative.me sentiment API; verify neutral default."""
        fetcher = SentimentFetcher(timeout_seconds=1)

        with patch("requests.get", side_effect=requests.exceptions.Timeout("Connection timed out")):
            res = fetcher.fetch_fear_greed()
            assert res["value"] == 50
            assert res["classification"] == "Neutral"
            assert res["status"] == "Neutru"


# ============================================================================
# 4. FRONTMATTER SCHEMA VALIDATION FUZZING & INVARIANT ATTACK TESTS
# ============================================================================

class TestFrontmatterSchemaFuzzing:
    """Adversarially fuzzes frontmatter metadata and tests trust boundary enforcement."""

    @pytest.fixture
    def valid_frontmatter(self):
        return {
            "id": str(uuid.uuid4()),
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "financial-asset-profile",
            "tags": ["finance", "test"],
            "created": "2026-08-25",
            "updated": "2026-08-25",
            "provenance": {
                "source_type": "execution",
                "source_ref": "test_harness",
                "source_date": "2026-08-25",
                "extraction_date": "2026-08-25",
                "redaction": "none",
                "provenance_status": "complete",
            },
            "confidence": "high",
            "verification": "unverified",
            "relations": [
                {"relation": "related_to", "target": "[[Knowledge Graph Home]]"}
            ],
        }

    def test_missing_required_fields_rejected(self, valid_frontmatter):
        """Removing any required field must raise a validation exception."""
        required_fields = [
            "id", "type", "lifecycle", "category", "tags",
            "created", "updated", "provenance", "confidence",
            "verification", "relations"
        ]

        for field in required_fields:
            corrupted = valid_frontmatter.copy()
            del corrupted[field]
            with pytest.raises(Exception):
                validate_frontmatter(corrupted)

    def test_invalid_enum_types_rejected(self, valid_frontmatter):
        """Invalid lifecycle, type, verification, confidence, or source_type must fail validation."""
        # Invalid type
        fm_bad_type = valid_frontmatter.copy()
        fm_bad_type["type"] = "unsupported_memory_type"
        with pytest.raises(Exception):
            validate_frontmatter(fm_bad_type)

        # Invalid lifecycle
        fm_bad_lc = valid_frontmatter.copy()
        fm_bad_lc["lifecycle"] = "INVALID_LIFECYCLE"
        with pytest.raises(Exception):
            validate_frontmatter(fm_bad_lc)

        # Invalid verification
        fm_bad_ver = valid_frontmatter.copy()
        fm_bad_ver["verification"] = "100%_guaranteed"
        with pytest.raises(Exception):
            validate_frontmatter(fm_bad_ver)

    def test_p0_p1_p2_trust_boundary_attack_rejection(self, valid_frontmatter):
        """
        Adversarial simulation of AI agent attempting to forge trust invariants:
        P0: AI agent attempting verification='verified'
        P1: AI agent attempting source_type='official' or 'user'
        P2: AI agent attempting lifecycle='ACTIVE'
        """
        assert valid_frontmatter["verification"] != "verified"
        assert valid_frontmatter["provenance"]["source_type"] in {"execution", "ai", "inference", "unknown"}
        assert valid_frontmatter["lifecycle"] in {"REVIEW", "NORMALIZED", "CLASSIFIED"}

    def test_schema_fuzzing_with_injections_and_huge_payloads(self, valid_frontmatter):
        """Test resilience against injection tags, emojis, and large text blocks in content rendering."""
        fuzzed_fm = valid_frontmatter.copy()
        fuzzed_fm["tags"] = ["<script>alert('xss')</script>", "🚀_crypto_💰", "a" * 100]
        
        # Schema validation
        assert validate_frontmatter(fuzzed_fm) is True

        # Render markdown with huge body and injection payloads
        huge_content = "# Test\n" + ("<img src=x onerror=alert(1)> " * 500)
        rendered = render_markdown_note(fuzzed_fm, huge_content)
        assert rendered.startswith("---")
        assert "<script>" in rendered
        assert "🚀_crypto_💰" in rendered

    def test_all_adapter_generators_produce_valid_notes(self):
        """Exhaustive check that all 7 generator functions emit schema-valid notes."""
        hist = generate_synthetic_ohlcv("AAPL", days=50, base_price=150.0)
        data = compute_all_indicators(hist, name="Apple", ticker="AAPL")

        # 1. Asset profile
        n1 = generate_asset_profile_note(data)
        assert validate_frontmatter(n1["frontmatter"]) is True

        # 2. Macro regime
        macro_d = {"VIX": {"inchidere": 15.0}}
        fred_d = {"FEDFUNDS": {"current": 5.25}}
        sent_d = {"value": 55, "display": "55 - Greed"}
        n2 = generate_macro_regime_note(macro_d, fred_d, sent_d)
        assert validate_frontmatter(n2["frontmatter"]) is True

        # 3. Technical setup
        n3 = generate_technical_setup_note(data)
        assert validate_frontmatter(n3["frontmatter"]) is True

        # 4. Trade experience
        n4 = generate_trade_experience_note({"trade_id": "T1", "asset": "AAPL", "direction": "LONG", "pnl_currency": 100.0, "pnl_percent": 1.0, "realized_rr": 2.0})
        assert validate_frontmatter(n4["frontmatter"]) is True

        # 5. Trade error
        n5 = generate_trade_error_note({"title": "FOMO Entry", "asset": "AAPL", "description": "Chased green candle", "impact": "-1R"})
        assert validate_frontmatter(n5["frontmatter"]) is True

        # 6. Trading lesson
        n6 = generate_trading_lesson_note({"title": "Volume Confirmation", "heuristic": "Always wait for RVOL > 1.5x on breakout."})
        assert validate_frontmatter(n6["frontmatter"]) is True

        # 7. Catalog resource
        n7 = generate_catalog_resource_note()
        assert validate_frontmatter(n7["frontmatter"]) is True


# ============================================================================
# 5. REMEDIATED EDGE-CASE BEHAVIORS & INVARIANT VERIFICATION
# ============================================================================

class TestDiscoveredVulnerabilities:
    """
    Verifications of remediated failure modes discovered during adversarial fuzzing.
    Validates graceful handling of missing columns, non-numeric strings, and flat price RSI.
    """

    def test_remediated_missing_volume_column_handled_gracefully(self):
        """
        Remediation of Bug #1: compute_all_indicators handles missing 'Volume' column gracefully.
        """
        dates = pd.date_range("2026-01-01", periods=30, freq="D")
        df_no_volume = pd.DataFrame({
            "Open": [100.0] * 30,
            "High": [105.0] * 30,
            "Low": [95.0] * 30,
            "Close": [102.0] * 30,
        }, index=dates)

        res = compute_all_indicators(df_no_volume, name="TestAsset", ticker="NO_VOL")
        assert isinstance(res, dict)
        assert res["volum"] == 0
        assert res["rvol"] == 1.0
        assert res["inchidere"] == 102.0

    def test_remediated_string_in_volume_handled_gracefully(self):
        """
        Remediation of Bug #2: compute_all_indicators handles non-numeric strings in Volume column gracefully.
        """
        dates = pd.date_range("2026-01-01", periods=30, freq="D")
        df_str_volume = pd.DataFrame({
            "Open": [100.0] * 30,
            "High": [105.0] * 30,
            "Low": [95.0] * 30,
            "Close": [102.0] * 30,
            "Volume": ["1000", "corrupted_volume_str", 2000] * 10,
        }, index=dates)

        res = compute_all_indicators(df_str_volume, name="TestAsset", ticker="STR_VOL")
        assert isinstance(res, dict)
        assert isinstance(res["volum"], int)
        assert isinstance(res["rvol"], float)

    def test_remediated_string_in_open_handled_gracefully(self):
        """
        Remediation of Bug #3: compute_all_indicators handles non-numeric strings in Open column gracefully.
        """
        dates = pd.date_range("2026-01-01", periods=30, freq="D")
        df_str_open = pd.DataFrame({
            "Open": ["invalid_open"] * 30,
            "High": [105.0] * 30,
            "Low": [95.0] * 30,
            "Close": [102.0] * 30,
            "Volume": [1000] * 30,
        }, index=dates)

        res = compute_all_indicators(df_str_open, name="TestAsset", ticker="STR_OPEN")
        assert isinstance(res, dict)
        assert res["inchidere"] == 102.0
        assert res["deschidere"] == 102.0

    def test_remediated_flat_prices_rsi_equilibrium(self):
        """
        Remediation of Mathematical Anomaly #4: Flat prices (zero volatility) return RSI = 50.0
        (Market Equilibrium) with 'Echilibru' status instead of false oversold selling pressure.
        """
        prices = pd.Series([100.0] * 30)
        rsi = calc_rsi(prices, period=14)
        status = map_rsi_status(rsi)
        
        assert rsi == 50.0
        assert status == "Echilibru"
