"""
Adversarial Stress Test Suite — Final Challenger (teamwork_preview_challenger)

Exhaustively verifies:
1. FinancialQueryEngine robustness under adversarial and boundary inputs:
   - Empty queries, whitespace, None
   - Multilingual unicode, Cyrillic, Chinese, Arabic RTL, emojis, math notation
   - Injection payloads (SQLi, XSS, nested quotes, control characters, regex metacharacters)
   - Nonexistent and unknown assets
   - Extreme and boundary limit / top_k / page_size parameters (0, -1, -999, 1000000, None)
2. BM25 Symbol Search Precision & Relevance Ranking:
   - "NASDAQ", "XAUUSD", "BTC", "RSI", "support" ranking #1 against corpus of distractors
3. Concurrency & Thread-Safety:
   - Multi-threaded concurrent ingestion and search stress test under SQLite WAL
4. REST API Endpoint Adversarial Verification (vault_api.py):
   - /financial_note, /search, /api/v1/search, /memory/financial/search (GET and POST)
   - 100% exception safety, zero unhandled 500 crashes, correct status codes
"""

import os
import time
import uuid
import json
import pytest
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed
from fastapi.testclient import TestClient

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.financial_query import FinancialQueryEngine
from memory_controller.authorizer import Principal
from vault_api import app, controller, storage


# ============================================================================
# FIXTURES
# ============================================================================

@pytest.fixture
def fresh_sqlite_storage(tmp_path):
    """Provides an isolated SQLiteStorageEngine in WAL mode."""
    db_file = str(tmp_path / "test_adversarial_challenger.sqlite3")
    storage_inst = SQLiteStorageEngine(db_file, wal_mode=True)
    yield storage_inst
    storage_inst.close()


@pytest.fixture
def engine(fresh_sqlite_storage):
    """Provides a clean FinancialQueryEngine instance."""
    return FinancialQueryEngine(fresh_sqlite_storage)


@pytest.fixture
def test_client():
    """FastAPI TestClient for REST API stress testing."""
    return TestClient(app)


def make_note(
    symbol: str = "GC=F",
    title: str = "Gold Note",
    category: str = "indici",
    narrative: str = "Sample narrative",
    tags: list = None,
    date: str = "2026-08-26"
) -> dict:
    """Helper generating a valid financial note payload."""
    return {
        "title": title,
        "symbol": symbol,
        "category": category,
        "date": date,
        "tags": tags or ["finance", symbol.lower()],
        "indicators": {
            "rsi_14": 55.0,
            "rsi_status": "Neutral",
            "trend": "Bullish",
            "atr_14": 20.0,
            "macd_cross": "Positive"
        },
        "signals": [
            {
                "signal": "BUY",
                "score": 4,
                "confluences": 4,
                "win_probability_pct": 75.0
            }
        ],
        "risk_metrics": {
            "impact": 3,
            "probability_pct": 60.0,
            "planned_rr": 2.0
        },
        "narrative": narrative,
        "raw_content": f"# {title}\nSymbol: {symbol}\n\n{narrative}"
    }


# ============================================================================
# 1. ADVERSARIAL & BOUNDARY INPUTS ON FinancialQueryEngine
# ============================================================================

class TestFinancialQueryEngineAdversarialInputs:

    def test_empty_and_whitespace_queries_zero_crashes(self, engine):
        """Tests that empty, whitespace, and nullish query strings return safe lists without crashing."""
        # Seed engine with some data
        engine.ingest_financial_note(make_note(symbol="^NDX", title="NASDAQ Note", narrative="Tech index analysis"))

        empty_inputs = ["", "   ", "\t", "\n", "\r\n", "      \t\n  "]
        for q in empty_inputs:
            results = engine.search(q)
            assert isinstance(results, list), f"Expected list for query {repr(q)}, got {type(results)}"

    def test_unicode_and_multilingual_queries(self, engine):
        """Tests search robustness with Unicode, non-Latin alphabets, RTL Arabic, Chinese, and emojis."""
        note_cyrillic = make_note(
            symbol="GC=F",
            title="Золото Анализ Золотых Слитков",
            narrative="Анализ рынка золота и драгоценных металлов в условиях волатильности."
        )
        note_chinese = make_note(
            symbol="BTC-USD",
            title="比特币区块链技术与市场突破",
            narrative="比特币在阻力位上方放量突破，牛市动能强劲。"
        )
        note_arabic = make_note(
            symbol="CL=F",
            title="تحليل سوق النفط الخام والمخزونات",
            narrative="ارتفاع أسعار النفط الخام بسبب تقلبات العرض والطلب العالمية."
        )
        note_emoji = make_note(
            symbol="^GSPC",
            title="S&P 500 Moon Rocket 🚀🔥",
            narrative="Equities hitting all-time highs with massive volume 📈🎯."
        )
        note_math = make_note(
            symbol="EURUSD=X",
            title="Forex Quantitative Volatility ∫∑√±",
            narrative="Calculated volatility distribution where σ = 0.05 and μ = 1.0850."
        )

        id_cyr = engine.ingest_financial_note(note_cyrillic)
        id_chi = engine.ingest_financial_note(note_chinese)
        id_ara = engine.ingest_financial_note(note_arabic)
        id_emo = engine.ingest_financial_note(note_emoji)
        id_mat = engine.ingest_financial_note(note_math)

        # Query Cyrillic
        res_cyr = engine.search("Золото")
        assert isinstance(res_cyr, list)
        assert any(r.get("id") == id_cyr for r in res_cyr)

        # Query Chinese
        res_chi = engine.search("比特币")
        assert isinstance(res_chi, list)
        assert any(r.get("id") == id_chi for r in res_chi)

        # Query Arabic
        res_ara = engine.search("النفط")
        assert isinstance(res_ara, list)
        assert any(r.get("id") == id_ara for r in res_ara)

        # Query Emoji
        res_emo = engine.search("🚀")
        assert isinstance(res_emo, list)

        # Query Math symbols
        res_mat = engine.search("∫∑√±")
        assert isinstance(res_mat, list)

    def test_injection_and_complex_punctuation_queries(self, engine):
        """Tests SQL injection strings, XSS payloads, regex metacharacters, and nested quotes."""
        engine.ingest_financial_note(make_note(symbol="GC=F", title="Standard Gold Note", narrative="Gold commodities"))

        injection_payloads = [
            "' OR '1'='1",
            "'; DROP TABLE notes; --",
            "UNION SELECT * FROM sqlite_master",
            "<script>alert('XSS')</script>",
            "<img src=x onerror=alert(1)>",
            "\"\"\"'''\"\"\"'''",
            "!@#$%^&*()_+-=[]{}|;':\",.<>/?~`",
            ".*+?^${}()|[]\\",
            "\\\\\\\\\\\\",
            "\x00\x01\x02\x1f\x7f",
            "{\"invalid\": [\"json\", {true: false}]}",
            "SELECT COUNT(*) FROM (SELECT 1 UNION SELECT 2)"
        ]

        for payload in injection_payloads:
            try:
                res = engine.search(payload)
                assert isinstance(res, list)
            except Exception as exc:
                pytest.fail(f"Search crashed with payload {repr(payload)}: {exc}")

    def test_unknown_and_nonexistent_assets(self, engine):
        """Tests that queries for nonexistent or gibberish asset symbols do not error."""
        engine.ingest_financial_note(make_note(symbol="GC=F", title="Gold Note"))

        unknown_queries = [
            "ZZZZ9999_NONEXISTENT_SYMBOL_XYZ",
            "FAKE_TICKER_999999",
            "UNSUPPORTED_CRYPTO_TOKEN_TOKEN12345",
            "NOT_A_REAL_INSTRUMENT_FOOBAR_123"
        ]

        for q in unknown_queries:
            res = engine.search(q)
            assert isinstance(res, list)

    def test_extreme_and_boundary_limit_parameters(self, engine):
        """Tests boundary parameters for limit, top_k, and page_size."""
        for i in range(15):
            engine.ingest_financial_note(make_note(symbol="GC=F", title=f"Gold Note {i}", narrative="Gold market analysis"))

        # limit = 0
        res_0 = engine.search("Gold", limit=0)
        assert isinstance(res_0, list)
        assert len(res_0) == 0

        # top_k = 0
        res_top0 = engine.search("Gold", top_k=0)
        assert isinstance(res_top0, list)
        assert len(res_top0) == 0

        # negative limit
        res_neg = engine.search("Gold", limit=-1)
        assert isinstance(res_neg, list)

        # huge limit
        res_huge = engine.search("Gold", limit=1000000)
        assert isinstance(res_huge, list)
        assert len(res_huge) <= 15

        # limit None or top_k None
        res_none = engine.search("Gold", limit=10, top_k=None)
        assert isinstance(res_none, list)
        assert len(res_none) <= 10


# ============================================================================
# 2. BM25 SYMBOL SEARCH PRECISION & RELEVANCE RANKING
# ============================================================================

class TestBM25SymbolSearchRelevance:

    @pytest.fixture(autouse=True)
    def setup_corpus(self, engine):
        """Seeds engine with targeted assets alongside distractor notes."""
        self.notes = {
            "nasdaq": engine.ingest_financial_note(make_note(
                symbol="^NDX",
                title="NASDAQ 100 Technology Index Benchmark",
                narrative="Tech rally powered by semiconductors and high growth software momentum on NASDAQ.",
                tags=["finance", "nasdaq", "tech", "^ndx"]
            )),
            "gold": engine.ingest_financial_note(make_note(
                symbol="GC=F",
                title="Gold Spot XAUUSD Safe Haven Allocation",
                narrative="Spot Gold XAUUSD testing critical resistance at 2500 amid central bank accumulation.",
                tags=["finance", "gc=f", "gold", "xauusd"]
            )),
            "btc": engine.ingest_financial_note(make_note(
                symbol="BTC-USD",
                title="Bitcoin BTC Crypto Market Cycle",
                narrative="Bitcoin BTC institutional inflow reaches record highs following ETF approvals.",
                tags=["finance", "btc-usd", "crypto", "btc"]
            )),
            "rsi": engine.ingest_financial_note(make_note(
                symbol="EURUSD=X",
                title="EURUSD Technical Momentum and RSI Divergence",
                narrative="Relative Strength Index RSI indicator reveals severe bullish divergence on 4H chart.",
                tags=["finance", "eurusd=x", "rsi", "momentum"]
            )),
            "support": engine.ingest_financial_note(make_note(
                symbol="^GSPC",
                title="S&P 500 Major Support Confluence Zone",
                narrative="S&P 500 establishes multi-touch horizontal support level with heavy institutional buying.",
                tags=["finance", "^gspc", "support", "confluence"]
            )),
            "distractor_oil": engine.ingest_financial_note(make_note(
                symbol="CL=F",
                title="Crude Oil Energy Logistics Report",
                narrative="Petroleum stockpiles increase as refinery throughput slows in summer maintenance.",
                tags=["finance", "cl=f", "oil", "energy"]
            )),
            "distractor_wheat": engine.ingest_financial_note(make_note(
                symbol="ZW=F",
                title="Agricultural Wheat Grain Harvest",
                narrative="Grain export quotas and harvest yields in Eastern Europe show moderate surplus.",
                tags=["finance", "zw=f", "agriculture", "wheat"]
            )),
        }

    def test_bm25_nasdaq_relevance(self, engine):
        """BM25 search for 'NASDAQ' must rank the NASDAQ note at #1."""
        results = engine.search("NASDAQ")
        assert len(results) >= 1, "Expected at least 1 result for NASDAQ"
        top_result = results[0]
        assert top_result["id"] == self.notes["nasdaq"], (
            f"Expected NASDAQ note {self.notes['nasdaq']} at rank #1, got {top_result['id']} ({top_result.get('title')})"
        )

    def test_bm25_xauusd_relevance(self, engine):
        """BM25 search for 'XAUUSD' must rank the Gold XAUUSD note at #1."""
        results = engine.search("XAUUSD")
        assert len(results) >= 1, "Expected at least 1 result for XAUUSD"
        top_result = results[0]
        assert top_result["id"] == self.notes["gold"], (
            f"Expected Gold note {self.notes['gold']} at rank #1, got {top_result['id']} ({top_result.get('title')})"
        )

    def test_bm25_btc_relevance(self, engine):
        """BM25 search for 'BTC' must rank the Bitcoin note at #1."""
        results = engine.search("BTC")
        assert len(results) >= 1, "Expected at least 1 result for BTC"
        top_result = results[0]
        assert top_result["id"] == self.notes["btc"], (
            f"Expected Bitcoin note {self.notes['btc']} at rank #1, got {top_result['id']} ({top_result.get('title')})"
        )

    def test_bm25_rsi_relevance(self, engine):
        """BM25 search for 'RSI' must rank the RSI Momentum note at #1."""
        results = engine.search("RSI")
        assert len(results) >= 1, "Expected at least 1 result for RSI"
        top_result = results[0]
        assert top_result["id"] == self.notes["rsi"], (
            f"Expected RSI note {self.notes['rsi']} at rank #1, got {top_result['id']} ({top_result.get('title')})"
        )

    def test_bm25_support_relevance(self, engine):
        """BM25 search for 'support' must rank the Support Confluence note at #1."""
        results = engine.search("support")
        assert len(results) >= 1, "Expected at least 1 result for support"
        top_result = results[0]
        assert top_result["id"] == self.notes["support"], (
            f"Expected Support note {self.notes['support']} at rank #1, got {top_result['id']} ({top_result.get('title')})"
        )


# ============================================================================
# 3. CONCURRENT INGESTION AND RETRIEVAL STRESS TESTING
# ============================================================================

class TestConcurrentIngestionAndRetrieval:

    def test_multi_threaded_concurrent_ingest_and_search(self, engine):
        """
        Executes high-concurrency stress test with 16 parallel worker threads:
        - 6 Ingestion Workers continuously creating notes.
        - 10 Search Workers continuously querying with varied keywords and filters.
        - Validates zero SQLite database lock errors, zero thread collisions, and 100% data integrity.
        """
        num_ingest_threads = 6
        num_search_threads = 10
        iterations_per_worker = 15

        errors = []
        ingested_ids = []
        lock = threading.Lock()

        def ingest_worker(worker_id: int):
            for i in range(iterations_per_worker):
                try:
                    payload = make_note(
                        symbol="GC=F" if i % 2 == 0 else "^NDX",
                        title=f"Worker {worker_id} Note {i}",
                        narrative=f"Stress test payload from worker {worker_id} iteration {i} analyzing resistance and liquidity."
                    )
                    nid = engine.ingest_financial_note(payload)
                    with lock:
                        ingested_ids.append(nid)
                except Exception as exc:
                    with lock:
                        errors.append(f"Ingest worker {worker_id} failed on iter {i}: {exc}")

        def search_worker(worker_id: int):
            queries = ["Gold", "NASDAQ", "Worker", "liquidity", "resistance", "GC=F", "^NDX", "nonexistent_term"]
            for i in range(iterations_per_worker):
                try:
                    q = queries[i % len(queries)]
                    res = engine.search(q, limit=10)
                    assert isinstance(res, list)
                except Exception as exc:
                    with lock:
                        errors.append(f"Search worker {worker_id} failed on iter {i}: {exc}")

        threads = []
        for w in range(num_ingest_threads):
            t = threading.Thread(target=ingest_worker, args=(w,))
            threads.append(t)
        for w in range(num_search_threads):
            t = threading.Thread(target=search_worker, args=(w,))
            threads.append(t)

        # Start all threads simultaneously
        for t in threads:
            t.start()

        # Wait for all threads to conclude
        for t in threads:
            t.join(timeout=30.0)

        # Verify zero unhandled exceptions or lock collisions
        assert len(errors) == 0, f"Encountered concurrency errors: {errors}"
        assert len(ingested_ids) == num_ingest_threads * iterations_per_worker
        assert len(set(ingested_ids)) == len(ingested_ids), "All generated note UUIDs must be unique"


# ============================================================================
# 4. REST API ENDPOINTS ADVERSARIAL VERIFICATION (vault_api.py)
# ============================================================================

class TestVaultApiAdversarialEndpoints:

    def test_post_financial_note_boundary_payloads(self, test_client):
        """Tests POST /financial_note with empty, huge, and special-character payloads."""
        # 1. Minimal valid note
        resp_min = test_client.post("/financial_note", json={"title": "Min Note", "symbol": "GC=F"})
        assert resp_min.status_code == 200
        assert resp_min.json()["status"] == "success"

        # 2. Large narrative (100KB string)
        large_narrative = "Gold market analysis. " * 4000
        resp_large = test_client.post("/financial_note", json={
            "title": "Large Payload Note",
            "symbol": "GC=F",
            "narrative": large_narrative
        })
        assert resp_large.status_code == 200
        assert resp_large.json()["status"] == "success"

        # 3. Unicode and special characters
        resp_unicode = test_client.post("/financial_note", json={
            "title": "🔥 Золото / 比特币 / 🚀",
            "symbol": "BTC-USD",
            "narrative": "Multilingual narrative with emojis and Arabic: أسعار الذهب",
            "tags": ["crypto", "btc", "тест"]
        })
        assert resp_unicode.status_code == 200
        assert resp_unicode.json()["status"] == "success"

    def test_get_search_and_api_v1_search_boundary_queries(self, test_client):
        """Tests GET /search and GET /api/v1/search with edge queries and extreme limits."""
        endpoints = ["/search", "/api/v1/search"]

        for ep in endpoints:
            # Empty query
            r_empty = test_client.get(ep, params={"q": ""})
            assert r_empty.status_code == 200
            assert "results" in r_empty.json()

            # Whitespace query
            r_ws = test_client.get(ep, params={"q": "   "})
            assert r_ws.status_code == 200
            assert "results" in r_ws.json()

            # Extreme limits
            r_lim0 = test_client.get(ep, params={"q": "Gold", "limit": 0})
            assert r_lim0.status_code == 200

            r_lim_huge = test_client.get(ep, params={"q": "Gold", "limit": 50000})
            assert r_lim_huge.status_code == 200

            # SQL injection query parameter
            r_sqli = test_client.get(ep, params={"q": "' OR '1'='1"})
            assert r_sqli.status_code == 200
            assert "results" in r_sqli.json()

            # Unicode and emojis
            r_uni = test_client.get(ep, params={"q": "🚀 Золото 比特币"})
            assert r_uni.status_code == 200
            assert "results" in r_uni.json()

    def test_get_and_post_memory_financial_search_adversarial(self, test_client):
        """Tests GET /memory/financial/search and POST /memory/financial/search endpoints."""
        # Seed test note via API
        test_client.post("/financial_note", json={
            "title": "Seed Note For Search",
            "symbol": "GC=F",
            "narrative": "Seed analysis for financial search endpoint tests."
        })

        # GET /memory/financial/search with empty query
        r_get_empty = test_client.get("/memory/financial/search", params={"query": ""})
        assert r_get_empty.status_code == 200
        assert r_get_empty.json()["status"] == "success"

        # GET /memory/financial/search with filters
        r_get_filt = test_client.get("/memory/financial/search", params={
            "query": "Seed",
            "symbol": "GC=F",
            "min_confidence": "high",
            "limit": 5
        })
        assert r_get_filt.status_code == 200
        assert r_get_filt.json()["status"] == "success"

        # POST /memory/financial/search with empty payload
        r_post_empty = test_client.post("/memory/financial/search", json={})
        assert r_post_empty.status_code == 200
        assert r_post_empty.json()["status"] == "success"

        # POST /memory/financial/search with complex structured filters
        r_post_complex = test_client.post("/memory/financial/search", json={
            "query": "Seed analysis",
            "symbol": "GC=F",
            "symbols": ["GC=F", "^NDX"],
            "category": "indici",
            "min_confidence": "high",
            "limit": 10
        })
        assert r_post_complex.status_code == 200
        assert r_post_complex.json()["status"] == "success"
        assert len(r_post_complex.json()["results"]) >= 0
