"""
Comprehensive Unit & Integration Test Suite for Multi-Layered Financial Search.

Covers:
1. Entity & Alias Resolution for all 95 assets, 5 macro tickers, 4 FRED series, and colloquial names.
2. 5-Layer Structured & Temporal Filtering (symbol, category, confidence, verification, date ranges, lifecycles).
3. Hybrid BM25 + Dense Vector RRF Ranking and Wikilink Graph Spreading Activation.
4. Progressive Disclosure Context Packaging & HMAC-SHA256 Pagination Security.
5. FastAPI Endpoint Integration (GET & POST /memory/financial/search).
6. Strict Preservation of P0-P18 Cognitive Trust Boundary Invariants.
"""

import os
import uuid
import json
import pytest
from datetime import datetime, timezone, timedelta
from fastapi.testclient import TestClient

from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.authorizer import Principal, Operation
from memory_controller.financial_search import (
    FinancialEntityResolver,
    MultiLayeredFinancialSearchEngine,
    FinancialFilterSpec,
    BM25Ranker,
    DenseVectorEmbedder,
    FinancialKnowledgeGraph,
    _GLOBAL_RESOLVER,
)
from memory_controller.security.pagination_token import (
    PaginationToken,
    InvalidPaginationTokenError,
)
from vault_api import app, controller as api_controller


# ============================================================================
# FIXTURES & TEST NOTE GENERATORS
# ============================================================================

@pytest.fixture
def temp_sqlite_storage(tmp_path):
    db_path = str(tmp_path / "test_financial_search.sqlite3")
    storage = SQLiteStorageEngine(db_path, wal_mode=True)
    yield storage
    storage.close()


@pytest.fixture
def memory_controller_instance(temp_sqlite_storage):
    return MemoryController(temp_sqlite_storage)


@pytest.fixture
def api_client():
    return TestClient(app)


def make_id(raw_id: str) -> str:
    try:
        return str(uuid.UUID(raw_id))
    except Exception:
        return str(uuid.uuid5(uuid.NAMESPACE_DNS, raw_id))


def make_financial_note(
    note_id: str,
    title: str,
    content: str,
    ticker: str = "GC=F",
    category: str = "financial-asset-profile",
    tags: list = None,
    lifecycle: str = "REVIEW",
    confidence: str = "high",
    verification: str = "unverified",
    created: str = "2026-08-25",
    relations: list = None,
    source_type: str = "execution"
) -> dict:
    canonical_id = make_id(note_id)
    rendered_content = f"# {title}\nTicker: {ticker}\n\n{content}"
    return {
        "id": canonical_id,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": category,
        "tags": tags or ["finance", ticker.lower(), "test"],
        "created": created,
        "updated": created,
        "provenance": {
            "source_type": source_type,
            "source_ref": "financial_test_suite",
            "source_date": created,
            "provenance_status": "complete"
        },
        "confidence": confidence,
        "verification": verification,
        "relations": relations or [],
        "content": rendered_content,
    }


# ============================================================================
# 1. ENTITY & ALIAS RESOLUTION TESTS (95 Assets + 5 Macro + 4 FRED)
# ============================================================================

class TestFinancialEntityResolver:

    def setup_method(self):
        self.resolver = FinancialEntityResolver()

    def test_all_catalog_categories_populated(self):
        symbols = self.resolver.get_all_symbols()
        # 95 assets + 5 macro + 4 FRED = 104 instruments
        assert len(symbols) >= 104
        assert "^GSPC" in symbols
        assert "GC=F" in symbols
        assert "^TNX" in symbols
        assert "FEDFUNDS" in symbols

    @pytest.mark.parametrize("query,expected_symbol", [
        # Precious & Industrial Metals
        ("Gold", "GC=F"),
        ("gold", "GC=F"),
        ("XAU", "GC=F"),
        ("XAUUSD", "GC=F"),
        ("XAU/USD", "GC=F"),
        ("Spot Gold", "GC=F"),
        ("Comex Gold", "GC=F"),
        ("Aur", "GC=F"),
        ("Silver", "SI=F"),
        ("XAG", "SI=F"),
        ("XAGUSD", "SI=F"),
        ("Spot Silver", "SI=F"),
        ("Copper", "HG=F"),
        ("High Grade Copper", "HG=F"),
        ("Platinum", "PL=F"),
        ("Palladium", "PA=F"),

        # Energy & Agriculture
        ("Oil", "CL=F"),
        ("Crude Oil", "CL=F"),
        ("WTI", "CL=F"),
        ("Oil Brent", "BZ=F"),
        ("Brent Crude", "BZ=F"),
        ("Natural Gas", "NG=F"),
        ("Nat Gas", "NG=F"),
        ("Corn", "ZC=F"),
        ("Wheat", "ZW=F"),
        ("Soybeans", "ZS=F"),
        ("Coffee", "KC=F"),
        ("Sugar", "SB=F"),
        ("Cotton", "CT=F"),

        # Indices
        ("S&P 500", "^GSPC"),
        ("S&P500", "^GSPC"),
        ("SP500", "^GSPC"),
        ("SPX", "^GSPC"),
        ("SPY", "SPY"),
        ("Standard & Poor's", "^GSPC"),
        ("NASDAQ 100", "^NDX"),
        ("NDX", "^NDX"),
        ("QQQ", "^NDX"),
        ("Tech Index", "^NDX"),
        ("NASDAQ Composite", "^IXIC"),
        ("Dow Jones", "^DJI"),
        ("DJIA", "^DJI"),
        ("Dow 30", "^DJI"),
        ("Russell 2000", "^RUT"),
        ("DAX Germany", "^GDAXI"),
        ("DAX", "^GDAXI"),
        ("DAX 40", "^GDAXI"),
        ("GER40", "^GDAXI"),
        ("German Index", "^GDAXI"),
        ("FTSE 100", "^FTSE"),
        ("Footsie", "^FTSE"),
        ("CAC 40", "^FCHI"),
        ("Nikkei 225", "^N225"),
        ("Hang Seng", "^HSI"),
        ("Shanghai Composite", "000001.SS"),
        ("MSCI World", "URTH"),
        ("MSCI EM", "EEM"),
        ("BET Romania", "BET.RO"),
        ("BET", "BET.RO"),

        # Equities
        ("Apple", "AAPL"),
        ("Microsoft", "MSFT"),
        ("NVIDIA", "NVDA"),
        ("Alphabet", "GOOGL"),
        ("Google", "GOOGL"),
        ("Amazon", "AMZN"),
        ("Meta", "META"),
        ("Facebook", "META"),
        ("Tesla", "TSLA"),
        ("Palantir", "PLTR"),
        ("Coinbase", "COIN"),
        ("Robinhood", "HOOD"),
        ("Berkshire Hathaway", "BRK-B"),
        ("JPMorgan", "JPM"),
        ("TSMC", "TSM"),
        ("ASML", "ASML"),

        # Cryptocurrencies
        ("Bitcoin", "BTC-USD"),
        ("BTC", "BTC-USD"),
        ("BTCUSD", "BTC-USD"),
        ("Ethereum", "ETH-USD"),
        ("ETH", "ETH-USD"),
        ("Solana", "SOL-USD"),
        ("Cardano", "ADA-USD"),
        ("Ripple", "XRP-USD"),
        ("Dogecoin", "DOGE-USD"),
        ("Chainlink", "LINK-USD"),
        ("Avalanche", "AVAX-USD"),
        ("Polkadot", "DOT-USD"),
        ("Polygon", "MATIC-USD"),

        # Forex
        ("EUR/USD", "EURUSD=X"),
        ("EURUSD", "EURUSD=X"),
        ("Euro Dollar", "EURUSD=X"),
        ("GBP/USD", "GBPUSD=X"),
        ("Cable", "GBPUSD=X"),
        ("USD/JPY", "USDJPY=X"),
        ("Dollar Yen", "USDJPY=X"),
        ("USD/CHF", "USDCHF=X"),
        ("AUD/USD", "AUDUSD=X"),
        ("USD/CAD", "USDCAD=X"),
        ("NZD/USD", "NZDUSD=X"),

        # Macro Indicators & FRED Series
        ("VIX", "^VIX"),
        ("Volatility Index", "^VIX"),
        ("Fear Gauge", "^VIX"),
        ("10-Year Treasury", "^TNX"),
        ("10Y Treasury", "^TNX"),
        ("10Y Yield", "^TNX"),
        ("US 10Y", "^TNX"),
        ("Yield 10Y US", "^TNX"),
        ("2-Year Treasury", "^IRX"),
        ("30-Year Treasury", "^TYX"),
        ("US Dollar Index", "DX-Y.NYB"),
        ("DXY", "DX-Y.NYB"),
        ("Fed Funds Rate", "FEDFUNDS"),
        ("Fed Rate", "FEDFUNDS"),
        ("FOMC Rate", "FEDFUNDS"),
        ("Consumer Price Index", "CPIAUCSL"),
        ("CPI", "CPIAUCSL"),
        ("Inflation", "CPIAUCSL"),
        ("Civilian Unemployment Rate", "UNRATE"),
        ("Unemployment Rate", "UNRATE"),
        ("Gross Domestic Product", "GDP"),
        ("US GDP", "GDP"),
    ])
    def test_alias_resolution_accuracy(self, query, expected_symbol):
        resolved = self.resolver.resolve_symbol(query)
        assert resolved == expected_symbol, f"Failed resolving '{query}' -> expected '{expected_symbol}', got '{resolved}'"

    def test_multi_entity_extraction_from_sentence(self):
        query = "Analyze Gold breakout and S&P 500 correlation when Fed Funds Rate rises alongside US 10Y yields"
        extracted = self.resolver.extract_entities_and_filters(query)
        
        symbols = extracted["symbols"]
        assert "GC=F" in symbols
        assert "^GSPC" in symbols
        assert "FEDFUNDS" in symbols
        assert "^TNX" in symbols

    def test_category_and_indicator_extraction(self):
        query = "Show me crypto setups with RSI divergence and MACD crossover"
        extracted = self.resolver.extract_entities_and_filters(query)
        
        assert "CRYPTO" in extracted["categories"]
        assert "rsi" in extracted["indicators"]
        assert "macd" in extracted["indicators"]
        assert "divergence" in extracted["indicators"]

    def test_confidence_and_verification_parsing(self):
        query = "Verified high confidence macro analysis notes post 2025"
        extracted = self.resolver.extract_entities_and_filters(query)
        
        assert extracted["min_confidence"] == "high"
        assert "verified" in extracted["verification_states"]
        assert extracted["date_from"] == "2025-01-01"


# ============================================================================
# 2. MULTI-LAYERED SEARCH PIPELINE & FILTERING TESTS
# ============================================================================

class TestMultiLayeredSearchEngine:

    def setup_method(self):
        self.resolver = _GLOBAL_RESOLVER

    def test_structured_filter_by_symbol(self, memory_controller_instance):
        ctrl = memory_controller_instance
        # Propose notes for Gold, DAX, and Bitcoin
        n1 = make_financial_note("note-gold-1", "Gold Kinetic Breakout", "Gold price tested 2520 resistance with high confluence.", ticker="GC=F", tags=["asset/xau", "gold", "finance"])
        n2 = make_financial_note("note-dax-1", "DAX 40 Bullish Momentum", "DAX index broke above 18500.", ticker="^GDAXI", tags=["asset/dax", "dax", "finance"])
        n3 = make_financial_note("note-btc-1", "Bitcoin Halving Analysis", "Bitcoin layer 1 network hash rate ATH.", ticker="BTC-USD", tags=["asset/btc", "crypto", "finance"])

        for n in [n1, n2, n3]:
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        # Search specifically for Gold
        res = ctrl.search_financial(
            principal=Principal.AI_AGENT,
            query="Gold resistance breakout",
            symbol="GC=F"
        )
        assert res["total_matched"] == 1
        assert res["results"][0]["id"] == make_id("note-gold-1")

    def test_filter_by_category(self, memory_controller_instance):
        ctrl = memory_controller_instance
        n_crypto = make_financial_note("note-eth", "Ethereum Layer 2", "Ethereum gas reduction via blobs.", ticker="ETH-USD", tags=["crypto", "eth"])
        n_fx = make_financial_note("note-eurusd", "EUR/USD ECB Stance", "ECB rate cut divergence vs Fed.", ticker="EURUSD=X", tags=["valute", "forex", "eurusd"])

        for n in [n_crypto, n_fx]:
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        res = ctrl.search_financial(
            principal=Principal.AI_AGENT,
            query="trading opportunities",
            category="CRYPTO"
        )
        assert res["total_matched"] == 1
        assert res["results"][0]["id"] == make_id("note-eth")

    def test_filter_by_confidence_levels(self, memory_controller_instance):
        ctrl = memory_controller_instance
        n_vh = make_financial_note("note-vh", "High Conviction XAU Setup", "Gold high conviction trade.", ticker="GC=F", confidence="very_high")
        n_high = make_financial_note("note-hi", "Standard Gold Setup", "Gold standard technical setup.", ticker="GC=F", confidence="high")
        n_med = make_financial_note("note-med", "Tentative Gold Setup", "Gold speculative setup.", ticker="GC=F", confidence="medium")
        n_low = make_financial_note("note-low", "Weak Gold Hypothesis", "Gold weak signal.", ticker="GC=F", confidence="low")

        for n in [n_vh, n_high, n_med, n_low]:
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        # Query with min_confidence = "high" -> must return very_high and high only
        res = ctrl.search_financial(
            principal=Principal.AI_AGENT,
            query="Gold setup",
            min_confidence="high"
        )
        matched_ids = {r["id"] for r in res["results"]}
        assert matched_ids == {make_id("note-vh"), make_id("note-hi")}

    def test_filter_by_verification_state(self, memory_controller_instance):
        ctrl = memory_controller_instance
        n_verified = make_financial_note("note-ver", "Verified Macro Data", "Audited GDP release.", ticker="GDP", verification="unverified")
        n_unverified = make_financial_note("note-unver", "Unverified Macro Guess", "Forecast GDP release.", ticker="GDP", verification="unverified")

        for n in [n_verified, n_unverified]:
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        ctrl.attest(Principal.HUMAN, n_verified["id"], verification_reason="Audited GDP data", evidence_reference="BEA")

        res = ctrl.search_financial(
            principal=Principal.AI_AGENT,
            query="GDP",
            verification_state="verified"
        )
        assert res["total_matched"] == 1
        assert res["results"][0]["id"] == make_id("note-ver")

    def test_temporal_date_range_filtering(self, memory_controller_instance):
        ctrl = memory_controller_instance
        n_2024 = make_financial_note("note-2024", "Gold 2024 Regime", "Historical 2024 gold analysis.", ticker="GC=F", created="2024-05-15")
        n_2025 = make_financial_note("note-2025", "Gold 2025 Regime", "2025 gold analysis.", ticker="GC=F", created="2025-07-20")
        n_2026 = make_financial_note("note-2026", "Gold 2026 Regime", "Current 2026 gold analysis.", ticker="GC=F", created="2026-08-10")

        for n in [n_2024, n_2025, n_2026]:
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        res = ctrl.search_financial(
            principal=Principal.AI_AGENT,
            query="Gold regime",
            date_from="2025-01-01",
            date_to="2025-12-31"
        )
        assert res["total_matched"] == 1
        assert res["results"][0]["id"] == make_id("note-2025")

    def test_raw_lifecycle_excluded_strictly(self, memory_controller_instance):
        ctrl = memory_controller_instance
        # Propose a RAW note
        n_raw = make_financial_note("note-raw", "Raw Gold Dump", "Unfiltered raw telemetry.", ticker="GC=F", lifecycle="RAW")
        ctrl.propose(Principal.HUMAN, n_raw)

        res = ctrl.search_financial(
            principal=Principal.AI_AGENT,
            query="Gold Dump"
        )
        # RAW note must never be returned in standard search
        matched_ids = [r["id"] for r in res["results"]]
        assert make_id("note-raw") not in matched_ids


# ============================================================================
# 3. HYBRID BM25 + VECTOR RRF RANKING & GRAPH ACTIVATION
# ============================================================================

class TestHybridRankingAndGraphActivation:

    def test_bm25_and_vector_rrf_scoring(self, memory_controller_instance):
        ctrl = memory_controller_instance
        # Note with exact keyword density and title match
        n1 = make_financial_note("note-dense", "Kinetic Breakout Strategy on Gold", "Gold volatility breakout at London open with high confluence.", ticker="GC=F", confidence="very_high")
        # Note with weak semantic mention
        n2 = make_financial_note("note-weak", "General Market Overview", "Commodities including gold had a quiet session.", ticker="GC=F", confidence="low")

        for n in [n1, n2]:
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        res = ctrl.search_financial(
            principal=Principal.AI_AGENT,
            query="Kinetic Breakout Strategy on Gold volatility"
        )
        assert len(res["results"]) >= 2
        # note-dense must rank #1 due to both BM25 and Vector alignment
        assert res["results"][0]["id"] == make_id("note-dense")

    def test_wikilink_graph_spreading_activation_boost(self, memory_controller_instance):
        ctrl = memory_controller_instance
        # n_macro: Macro setup note
        n_macro = make_financial_note(
            "macro-fomc-hike",
            "Macro Regime: Hawkish FOMC Rate Hike",
            "Fed raises rates by 50bps to combat inflation. [[Trade Decision: Short Equities]]",
            ticker="FEDFUNDS",
            confidence="high"
        )
        # n_trade: Related trade decision linked via wikilink and relations
        n_trade = make_financial_note(
            "trade-short-spx",
            "Trade Decision: Short Equities",
            "Executed short position on S&P 500 following FOMC rate hike.",
            ticker="^GSPC",
            confidence="high",
            relations=[{"relation": "caused_by", "target": "[[Trade Decision: Short Equities]]", "target_id": make_id("macro-fomc-hike")}]
        )
        # n_unrelated: Unrelated active note
        n_unrelated = make_financial_note(
            "unrelated-corn",
            "Corn Harvest Update",
            "Agricultural crop yield update for corn.",
            ticker="ZC=F",
            confidence="medium"
        )

        for n in [n_macro, n_trade, n_unrelated]:
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        # Query targeting the macro regime
        res = ctrl.search_financial(
            principal=Principal.AI_AGENT,
            query="Hawkish FOMC Rate Hike inflation",
            page_size=5
        )
        result_ids = [r["id"] for r in res["results"]]
        assert make_id("macro-fomc-hike") in result_ids
        assert make_id("trade-short-spx") in result_ids
        # Graph spreading activation elevates trade-short-spx above unrelated notes
        if make_id("unrelated-corn") in result_ids:
            assert result_ids.index(make_id("trade-short-spx")) < result_ids.index(make_id("unrelated-corn"))
        else:
            assert make_id("trade-short-spx") in result_ids and make_id("unrelated-corn") not in result_ids


# ============================================================================
# 4. PROGRESSIVE DISCLOSURE & HMAC-SHA256 PAGINATION TESTS
# ============================================================================

class TestProgressiveDisclosureAndPagination:

    def test_progressive_disclosure_levels(self, memory_controller_instance):
        ctrl = memory_controller_instance
        note = make_financial_note("note-disc", "Detailed Gold Analysis", "# Header\nFirst section content.\n# Second Header\nSecond section content.", ticker="GC=F")
        ctrl.propose(Principal.HUMAN, note)
        ctrl.promote(Principal.HUMAN, note["id"])

        # 1. Metadata level
        pack_meta = ctrl.search_financial(Principal.AI_AGENT, query="Gold", disclosure_level="metadata")
        r_meta = pack_meta["results"][0]
        assert "id" in r_meta
        assert "type" in r_meta
        assert "content" not in r_meta

        # 2. Snippet level
        pack_snip = ctrl.search_financial(Principal.AI_AGENT, query="Gold", disclosure_level="snippet")
        r_snip = pack_snip["results"][0]
        assert "snippet" in r_snip
        assert "content" not in r_snip

        # 3. Full level
        pack_full = ctrl.search_financial(Principal.AI_AGENT, query="Gold", disclosure_level="full")
        r_full = pack_full["results"][0]
        assert "content" in r_full

    def test_hmac_sha256_pagination_multi_page(self, memory_controller_instance, monkeypatch):
        monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "super_secret_test_hmac_financial_key_32b")
        ctrl = memory_controller_instance

        # Insert 5 notes
        for i in range(5):
            n = make_financial_note(f"page-note-{i}", f"Asset Analysis {i}", f"Market analysis note content {i}", ticker="GC=F")
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        # Page 1 (limit = 2)
        p1 = ctrl.search_financial(Principal.AI_AGENT, query="Market analysis", page_size=2)
        assert len(p1["results"]) == 2
        assert p1["next_page_token"] is not None

        # Page 2 using token
        p2 = ctrl.search_financial(Principal.AI_AGENT, query="Market analysis", page_size=2, page_token=p1["next_page_token"])
        assert len(p2["results"]) == 2
        assert p2["next_page_token"] is not None
        # Verify disjoint pages
        p1_ids = {r["id"] for r in p1["results"]}
        p2_ids = {r["id"] for r in p2["results"]}
        assert p1_ids.isdisjoint(p2_ids)

        # Page 3 using token
        p3 = ctrl.search_financial(Principal.AI_AGENT, query="Market analysis", page_size=2, page_token=p2["next_page_token"])
        assert len(p3["results"]) == 1
        assert p3["next_page_token"] is None

    def test_tampered_pagination_token_rejected(self, memory_controller_instance, monkeypatch):
        monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "super_secret_test_hmac_financial_key_32b")
        ctrl = memory_controller_instance

        for i in range(3):
            n = make_financial_note(f"tamper-note-{i}", f"Note {i}", "Content", ticker="GC=F")
            ctrl.propose(Principal.HUMAN, n)
            ctrl.promote(Principal.HUMAN, n["id"])

        p1 = ctrl.search_financial(Principal.AI_AGENT, query="Content", page_size=1)
        valid_token = p1["next_page_token"]
        assert valid_token is not None

        # Tamper token signature
        tampered_token = valid_token[:-4] + "AAAA"
        with pytest.raises(InvalidPaginationTokenError):
            ctrl.search_financial(Principal.AI_AGENT, query="Content", page_size=1, page_token=tampered_token)


# ============================================================================
# 5. FASTAPI REST ENDPOINT TESTS (/memory/financial/search)
# ============================================================================

class TestFastAPIFinancialEndpoints:

    def setup_method(self):
        # Clear storage and seed with test financial note
        api_controller.storage.delete(make_id("api-note-xau-1"))
        note = make_financial_note(
            "api-note-xau-1",
            "Gold Volatility Surge",
            "Gold spikes above 2500 following dovish central bank remarks.",
            ticker="GC=F",
            confidence="high",
            verification="unverified"
        )
        api_controller.propose(Principal.HUMAN, note)
        api_controller.promote(Principal.HUMAN, note["id"])

    def test_get_financial_search_endpoint(self, api_client):
        response = api_client.get("/memory/financial/search?query=Gold+volatility&symbol=GC=F")
        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "success"
        assert len(data["results"]) >= 1
        result_ids = [r["id"] for r in data["results"]]
        assert make_id("api-note-xau-1") in result_ids

    def test_post_financial_search_endpoint(self, api_client):
        payload = {
            "query": "dovish central bank remarks",
            "symbol": "GC=F",
            "min_confidence": "medium",
            "limit": 50
        }
        response = api_client.post("/memory/financial/search", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "success"
        assert len(data["results"]) >= 1
        result_ids = [r["id"] for r in data["results"]]
        assert make_id("api-note-xau-1") in result_ids

    def test_endpoint_no_matches_returns_empty_cleanly(self, api_client):
        payload = {
            "query": "NonexistentRandomTickerQuery12345",
            "symbol": "NONEXISTENT",
            "limit": 5
        }
        response = api_client.post("/memory/financial/search", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "success"
        assert len(data["results"]) == 0


# ============================================================================
# 6. P0-P18 INVARIANTS PRESERVATION DURING SEARCH
# ============================================================================

class TestSecurityInvariantsPreservation:

    def test_p0_invariant_ai_cannot_self_attest_during_search(self, memory_controller_instance):
        ctrl = memory_controller_instance
        # AI proposes unverified note
        note_data = make_financial_note(
            "p0-note-1", "AI Market Hypothesis", "AI generated price forecast.",
            ticker="GC=F", verification="unverified", source_type="ai"
        )
        ctrl.propose(Principal.AI_AGENT, note_data)

        # AI attempts search; note should reflect unverified state
        res = ctrl.search_financial(Principal.AI_AGENT, query="price forecast")
        assert res["total_matched"] == 1
        assert res["results"][0]["verification"] == "unverified"

        # AI attempts attest operation directly -> MUST fail with PermissionError
        with pytest.raises(PermissionError):
            ctrl.attest(
                Principal.AI_AGENT,
                make_id("p0-note-1"),
                verification_reason="Self-verification attempt",
                evidence_reference="ai_internal_log"
            )

    def test_search_leaves_storage_state_immutable(self, memory_controller_instance):
        ctrl = memory_controller_instance
        note = make_financial_note("immutable-note", "Immutable Gold Note", "Persistent factual data.", ticker="GC=F")
        ctrl.propose(Principal.HUMAN, note)
        ctrl.promote(Principal.HUMAN, note["id"])

        state_before = ctrl.storage.get(make_id("immutable-note"))

        # Execute multiple search runs
        for _ in range(5):
            ctrl.search_financial(Principal.AI_AGENT, query="Persistent factual data")

        state_after = ctrl.storage.get(make_id("immutable-note"))
        assert state_before == state_after
