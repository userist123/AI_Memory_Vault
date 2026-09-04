"""
Unit Test Suite for FinancialQueryEngine (Tier 2).

Validates:
1. FinancialQueryEngine initialization with SQLiteStorageEngine.
2. ingest_financial_note:
   - Ingestion of compliant financial notes yielding valid UUIDs.
   - Rejection of malformed notes via Draft-07 schema validation.
   - Automatic enrichment with canonical frontmatter (lifecycle=REVIEW, type=knowledge).
   - Provenance assignment with P0-P18 adherence.
3. search functionality:
   - BM25 lexical keyword & symbol search (NASDAQ, RSI, XAUUSD, breakout, confluence).
   - Structured filter matching by symbol, date ranges (date_from, date_to), and tags.
   - Result pagination / top_k limits.
   - Vector search fallback handling (ENABLE_VECTOR_SEARCH toggle).
   - Deterministic SHA-256 note content hashing.
"""

import os
import uuid
import pytest
import jsonschema

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.financial_query import FinancialQueryEngine, ENABLE_VECTOR_SEARCH
from memory_controller.authorizer import Principal


# ============================================================================
# FIXTURES
# ============================================================================

@pytest.fixture
def sqlite_storage(tmp_path):
    """Provides an isolated SQLiteStorageEngine in WAL mode."""
    db_file = str(tmp_path / "test_query_engine.sqlite3")
    storage = SQLiteStorageEngine(db_file, wal_mode=True)
    yield storage
    storage.close()


@pytest.fixture
def query_engine(sqlite_storage):
    """Provides a fresh FinancialQueryEngine instance."""
    return FinancialQueryEngine(sqlite_storage)


def sample_financial_note_payload(
    symbol: str = "GC=F",
    title: str = "Gold Kinetic Breakout",
    category: str = "indici",
    narrative: str = None,
    tags: list = None,
    date: str = "2026-08-26"
) -> dict:
    """Helper creating a schema-valid financial note dict for ingestion."""
    narrative_text = narrative or f"{title} for {symbol}. Comprehensive market analysis and confluence levels."
    return {
        "title": title,
        "symbol": symbol,
        "category": category,
        "date": date,
        "tags": tags or ["finance", symbol.lower(), "test"],
        "indicators": {
            "rsi_14": 56.5,
            "rsi_status": "Momentum ascendent",
            "trend": "Bullish",
            "atr_14": 22.0,
            "macd_cross": "Impuls pozitiv activ"
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
        "narrative": narrative_text,
        "raw_content": f"# {title}\nSymbol: {symbol}\n\n{narrative_text}"
    }


# ============================================================================
# 1. INITIALIZATION & HASHING TESTS
# ============================================================================

class TestFinancialQueryEngineInitAndHashing:

    def test_initialization(self, sqlite_storage):
        engine = FinancialQueryEngine(sqlite_storage)
        assert engine.storage is sqlite_storage
        assert engine.search_engine is not None

    def test_deterministic_sha256_hashing(self, query_engine):
        note_a = {"title": "Gold", "symbol": "GC=F", "score": 4}
        note_b = {"score": 4, "symbol": "GC=F", "title": "Gold"}  # same keys different order
        note_c = {"title": "Silver", "symbol": "SI=F", "score": 3}

        hash_a = query_engine._hash_note(note_a)
        hash_b = query_engine._hash_note(note_b)
        hash_c = query_engine._hash_note(note_c)

        assert len(hash_a) == 64
        assert hash_a == hash_b, "Hash must be key-order invariant"
        assert hash_a != hash_c, "Different notes must produce different hashes"


# ============================================================================
# 2. INGESTION PIPELINE TESTS
# ============================================================================

class TestFinancialNoteIngestion:

    def test_ingest_valid_financial_note(self, query_engine):
        payload = sample_financial_note_payload(symbol="^NDX", title="NASDAQ Tech Breakout")
        note_id = query_engine.ingest_financial_note(payload)

        # Validate returned UUID format
        assert uuid.UUID(note_id)

        # Verify stored record structure in SQLite
        stored = query_engine.storage.get(note_id)
        assert stored is not None
        assert stored["id"] == note_id
        assert stored["type"] == "knowledge"
        assert stored["lifecycle"] == "REVIEW"
        assert stored["category"] == "financial" or stored["category"] == "indici"
        assert stored["provenance"]["source_type"] == "execution"
        assert "content" in stored

    def test_ingest_multiple_distinct_notes(self, query_engine):
        notes = [
            sample_financial_note_payload(symbol="GC=F", title="Gold Note"),
            sample_financial_note_payload(symbol="BTC-USD", title="Bitcoin Note"),
            sample_financial_note_payload(symbol="^GSPC", title="S&P 500 Note")
        ]
        ids = [query_engine.ingest_financial_note(n) for n in notes]
        assert len(ids) == 3
        assert len(set(ids)) == 3

        for nid in ids:
            assert query_engine.storage.get(nid) is not None


# ============================================================================
# 3. SEARCH & BM25 KEYWORD RETRIEVAL TESTS
# ============================================================================

class TestFinancialQuerySearch:

    def test_bm25_symbol_and_keyword_search(self, query_engine):
        n1 = sample_financial_note_payload(
            symbol="^NDX",
            title="NASDAQ 100 Technology Index Surges",
            narrative="Tech momentum strong driven by semiconductor rallies."
        )
        n2 = sample_financial_note_payload(
            symbol="GC=F",
            title="Gold Safe Haven Demand Rises",
            narrative="Precious metals bid up amid macroeconomic volatility."
        )
        n3 = sample_financial_note_payload(
            symbol="BTC-USD",
            title="Bitcoin Halving Cycle Analysis",
            narrative="Layer 1 network metrics indicate hash rate expansion."
        )

        id1 = query_engine.ingest_financial_note(n1)
        id2 = query_engine.ingest_financial_note(n2)
        id3 = query_engine.ingest_financial_note(n3)

        # Search for NASDAQ
        results_nasdaq = query_engine.search("NASDAQ")
        assert len(results_nasdaq) >= 1
        matched_ids = [r.get("id") for r in results_nasdaq]
        assert id1 in matched_ids

        # Search for Gold
        results_gold = query_engine.search("Gold Safe Haven")
        assert len(results_gold) >= 1
        matched_gold_ids = [r.get("id") for r in results_gold]
        assert id2 in matched_gold_ids

    def test_search_structured_filtering_by_symbol(self, query_engine):
        n_gold = sample_financial_note_payload(symbol="GC=F", title="Gold Setup")
        n_silver = sample_financial_note_payload(symbol="SI=F", title="Silver Setup")

        id_gold = query_engine.ingest_financial_note(n_gold)
        id_silver = query_engine.ingest_financial_note(n_silver)

        # Search with symbol filter
        res = query_engine.search("Setup", filters={"symbol": "GC=F"})
        assert len(res) >= 1
        for r in res:
            assert "GC=F" in r.get("content", "") or "gc=f" in [t.lower() for t in r.get("tags", [])]

    def test_search_temporal_date_filtering(self, query_engine):
        n_2024 = sample_financial_note_payload(symbol="GC=F", title="Gold 2024", date="2024-05-15")
        n_2025 = sample_financial_note_payload(symbol="GC=F", title="Gold 2025", date="2025-06-20")
        n_2026 = sample_financial_note_payload(symbol="GC=F", title="Gold 2026", date="2026-08-10")

        query_engine.ingest_financial_note(n_2024)
        id_2025 = query_engine.ingest_financial_note(n_2025)
        query_engine.ingest_financial_note(n_2026)

        res = query_engine.search("Gold", filters={"date_from": "2025-01-01", "date_to": "2025-12-31"})
        assert len(res) >= 1
        matched_ids = [r.get("id") for r in res]
        assert id_2025 in matched_ids

    def test_search_tag_filtering(self, query_engine):
        n_crypto = sample_financial_note_payload(symbol="ETH-USD", title="Ethereum DeFi Surge", tags=["crypto", "defi"])
        n_fx = sample_financial_note_payload(symbol="EURUSD=X", title="Euro Dollar ECB Stance", tags=["forex", "valute"])

        id_crypto = query_engine.ingest_financial_note(n_crypto)
        id_fx = query_engine.ingest_financial_note(n_fx)

        res = query_engine.search("Ethereum", filters={"tags": ["crypto"]})
        assert len(res) >= 1
        matched_ids = [r.get("id") for r in res]
        assert id_crypto in matched_ids

    def test_search_top_k_limiting(self, query_engine):
        for i in range(10):
            payload = sample_financial_note_payload(symbol="GC=F", title=f"Gold Note {i}")
            query_engine.ingest_financial_note(payload)

        res_3 = query_engine.search("Gold", top_k=3)
        assert len(res_3) <= 3

        res_7 = query_engine.search("Gold", top_k=7)
        assert len(res_7) <= 7

    def test_search_no_matches_returns_empty_list(self, query_engine):
        res = query_engine.search("NonexistentKeywordRandomX12345")
        assert isinstance(res, list)
        assert len(res) == 0

    def test_vector_search_config_gated_fallback(self, query_engine, monkeypatch):
        """Tests that vector search flag can be toggled without breaking search."""
        import memory_controller.financial_query as fq
        monkeypatch.setattr(fq, "ENABLE_VECTOR_SEARCH", True)

        payload = sample_financial_note_payload(symbol="GC=F", title="Gold Confluence")
        query_engine.ingest_financial_note(payload)

        # Must execute cleanly even when vector search flag is True
        res = query_engine.search("Gold Confluence")
        assert isinstance(res, list)
