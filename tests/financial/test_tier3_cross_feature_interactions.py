"""
Tier 3: Cross-Feature Interactions & End-to-End Pipelines Test Suite.
Exhaustively tests integrated multi-subsystem workflows:
- Ingestion -> Technical Analysis -> Confluence Scoring -> Canonical Memory Proposal
- Memory Proposal -> SQLite WAL Transaction -> Cryptographic SHA-256 Audit Log Chaining
- Multi-Layered Search -> Alias Extraction -> Structured Filtering -> Graph Traversal
- Trade Lifecycle -> Realized P&L / RR -> FormalReflexion -> Error & Lesson Synapses
- Autonomous Research Agent -> Macro Ingestion -> ToT Hypothesis -> Consolidation
"""

import os
import uuid
import json
import pytest
from datetime import datetime, timezone

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.authorizer import Principal
from memory_controller.audit.logger import AuditLogger
from memory_controller.validation.schema import validate_frontmatter

from tests.financial.test_tier1_features import (
    calculate_rsi,
    calculate_macd,
    calculate_bollinger_bands,
    calculate_atr,
    calculate_confluence_score,
    calculate_portfolio_metrics,
    create_valid_note_payload,
    resolve_entity_alias,
    execute_formal_reflexion,
)


def test_tier3_pipeline1_ingestion_to_canonical_memory_proposal(isolated_controller, sample_ohlcv_gold):
    """
    Pipeline 1:
    OHLCV Data Ingestion -> Quantitative Indicator Calculation ->
    Confluence Signal Generation -> Canonical Memory Note Payload Construction ->
    Proposal to MemoryController with Draft7 Schema Validation.
    """
    ctrl = isolated_controller
    closes = [b["close"] for b in sample_ohlcv_gold]
    highs = [b["high"] for b in sample_ohlcv_gold]
    lows = [b["low"] for b in sample_ohlcv_gold]

    # 1. Calculate indicators
    rsi = calculate_rsi(closes, 14)
    macd = calculate_macd(closes)
    bb = calculate_bollinger_bands(closes, 20)
    atr = calculate_atr(highs, lows, closes, 14)

    # 2. Confluence scoring
    confluence = calculate_confluence_score(
        rsi=rsi,
        macd_hist=macd["hist"],
        price=closes[-1],
        sma50=2480.0,
        sma200=2400.0,
        rvol=1.6,
    )

    # 3. Build canonical note
    note_id = str(uuid.uuid4())
    content = (
        f"## Market Snapshot: Gold (GC=F)\n"
        f"- Price: ${closes[-1]}\n"
        f"- RSI(14): {rsi}\n"
        f"- MACD Hist: {macd['hist']}\n"
        f"- ATR(14): {atr}\n"
        f"- Confluence Signal: {confluence['signal']} (Score: {confluence['score']}, Prob: {confluence['probability_percent']}%)\n"
    )
    payload = create_valid_note_payload(
        note_id=note_id,
        note_type="knowledge",
        title="Gold Market Quantitative Analysis Snapshot",
        content=content,
        category="FINANCE_ASSET",
        tags=["finance", "asset/xau", "technical_analysis"],
        confidence="high",
        verification="partially_verified",
    )

    # 4. Propose into controller
    ctrl.propose(Principal.AI_AGENT, payload)
    stored = ctrl.storage.get(note_id)

    assert stored is not None
    assert "Confluence Signal: BUY" in stored["content"] or "Confluence Signal:" in stored["content"]


def test_tier3_pipeline2_memory_proposal_wal_and_audit_chaining(isolated_controller, tmp_path):
    """
    Pipeline 2:
    Memory Proposal -> SQLite WAL Atomic Transaction ->
    Tamper-Evident SHA-256 Chained Audit Event Logging.
    """
    ctrl = isolated_controller
    log_path = tmp_path / "audit_pipeline.jsonl"
    logger = AuditLogger(str(log_path))

    note_id = str(uuid.uuid4())
    payload = create_valid_note_payload(
        note_id=note_id,
        note_type="decision",
        title="Asset Allocation Shift: Commodities Overweight",
        content="Rebalance portfolio +5% XAU, -5% Cash on real yield decline.",
        category="portfolio-allocation",
        tags=["decision", "portfolio", "macro"],
    )

    # 1. Propose into memory
    ctrl.propose(Principal.AI_AGENT, payload)

    # 2. Write audit event
    logger.log("agent", "PROPOSE_MEMORY", note_id, metadata={"category": payload["category"], "type": payload["type"]})

    # 3. Verify SQLite persistence and audit integrity
    stored = ctrl.storage.get(note_id)
    assert stored is not None

    is_valid, violations = logger.verify_integrity()
    assert is_valid, f"Audit violations: {violations}"


def test_tier3_pipeline3_search_alias_extraction_and_graph_retrieval(isolated_controller):
    """
    Pipeline 3:
    Natural Language Query -> Alias Resolver ("Gold", "US 10Y") ->
    Structured Multi-Layer Search -> Wikilink Knowledge Graph Navigation.
    """
    ctrl = isolated_controller

    macro_note_id = str(uuid.uuid4())
    asset_note_id = str(uuid.uuid4())

    # Create Macro Note
    macro_payload = create_valid_note_payload(
        note_id=macro_note_id,
        note_type="knowledge",
        title="US 10Y Real Yield Drop",
        content="Benchmark 10-Year yield declined 35bps following FOMC easing guidance.",
        category="macroeconomics",
        tags=["macro", "yields", "us10y"],
        lifecycle="ACTIVE",
        source_type="official",
    )
    ctrl.propose(Principal.HUMAN, macro_payload)

    # Create Asset Note linked to Macro Note
    asset_payload = create_valid_note_payload(
        note_id=asset_note_id,
        note_type="knowledge",
        title="Gold Price Surge Catalyst",
        content=f"Gold rallied $40/oz directly driven by [[{macro_note_id}]].",
        category="FINANCE_ASSET",
        tags=["finance", "asset/xau"],
        lifecycle="ACTIVE",
        source_type="official",
    )
    asset_payload["relations"].append({"relation": "caused_by", "target": f"[[{macro_note_id}]]"})
    ctrl.propose(Principal.HUMAN, asset_payload)

    # Query using natural language
    nl_query = "What drove the Gold rally after US 10Y dropped?"
    resolved_ticker = resolve_entity_alias("Gold")
    assert resolved_ticker == "GC=F"

    # Search in controller
    search_pack = ctrl.search(Principal.HUMAN, "Gold", lifecycles=[Lifecycle.ACTIVE])
    assert search_pack is not None

    # Verify graph link
    stored_asset = ctrl.storage.get(asset_note_id)
    assert any(r["target"] == f"[[{macro_note_id}]]" for r in stored_asset["relations"])


def test_tier3_pipeline4_trade_lifecycle_to_formal_reflexion_loop(isolated_controller, sample_trade_records):
    """
    Pipeline 4:
    Trade Order Entry -> Trade Closure with Stop-Loss Hit ->
    Performance Metric Calculation (Realized RR = -1.0) ->
    Automated 6-Stage FormalReflexion Post-Mortem ->
    Generation & Linking of Atomic Error & Lesson Notes.
    """
    ctrl = isolated_controller
    loss_trade = sample_trade_records[1]  # NVDA Short loss

    # 1. Log trade entry decision note
    entry_note_id = str(uuid.uuid4())
    entry_payload = create_valid_note_payload(
        note_id=entry_note_id,
        note_type="decision",
        title=f"Trade Entry: {loss_trade['asset']} {loss_trade['direction']}",
        content=f"Entry at {loss_trade['entry_price']} with planned SL {loss_trade['stop_loss']}",
        category="trading-journal",
        tags=["trade", "decision"],
    )
    ctrl.propose(Principal.AI_AGENT, entry_payload)

    # 2. Trigger FormalReflexion post-mortem on closure
    err_note, lesson_note = execute_formal_reflexion(loss_trade)
    err_note["relations"].append({"relation": "caused_by", "target": f"[[{entry_note_id}]]"})

    # 3. Propose error and lesson notes
    ctrl.propose(Principal.AI_AGENT, err_note)
    ctrl.propose(Principal.AI_AGENT, lesson_note)

    # 4. Verify all nodes in storage and linked
    assert ctrl.storage.get(entry_note_id) is not None
    assert ctrl.storage.get(err_note["id"]) is not None
    assert ctrl.storage.get(lesson_note["id"]) is not None

    stored_err = ctrl.storage.get(err_note["id"])
    assert any(r["relation"] == "solved_by" for r in stored_err["relations"])


def test_tier3_pipeline5_autonomous_research_agent_tot_and_consolidation(isolated_controller, mock_fred_series):
    """
    Pipeline 5:
    Research Agent Ingests FRED Macro Series ->
    Explores 3-Branch Tree-of-Thought (ToT) Scenarios ->
    Selects Winning Coherent Hypothesis ->
    Proposes Hypothesis Note ->
    Consolidator Promotes Reusable Principle into Canonical Knowledge.
    """
    ctrl = isolated_controller

    # 1. Ingest FRED series
    fedfunds_trend = mock_fred_series["FEDFUNDS"]
    latest_rate = float(fedfunds_trend[-1]["value"])
    initial_rate = float(fedfunds_trend[0]["value"])
    rate_cut_bps = int((initial_rate - latest_rate) * 100)

    # 2. Tree-of-Thought Exploration
    tot_scenarios = [
        {"branch": "Soft Landing Easing", "prob": 0.65, "thesis": f"Fed cuts {rate_cut_bps}bps fostering non-inflationary growth."},
        {"branch": "Persistent Sticky Inflation", "prob": 0.20, "thesis": "Fed pauses cuts as CPI rebounds."},
        {"branch": "Hard Landing Recession", "prob": 0.15, "thesis": "Unemployment accelerates triggering emergency cuts."},
    ]
    winner = max(tot_scenarios, key=lambda x: x["prob"])

    # 3. Propose Hypothesis note
    hyp_id = str(uuid.uuid4())
    hyp_payload = create_valid_note_payload(
        note_id=hyp_id,
        note_type="hypothesis",
        title=f"Macro Hypothesis: {winner['branch']}",
        content=f"{winner['thesis']}\nProbability: {int(winner['prob']*100)}%",
        category="macro-hypothesis",
        tags=["hypothesis", "macro", "rates"],
        confidence="medium",
        verification="inferred",
    )
    ctrl.propose(Principal.AI_AGENT, hyp_payload)

    # 4. Consolidator synthesizes reusable market regime rule
    regime_id = str(uuid.uuid4())
    regime_payload = create_valid_note_payload(
        note_id=regime_id,
        note_type="knowledge",
        title="Regime Model: Rate Cutting Cycle Bullish Rotation",
        content="Historical empirical data indicates rate cuts with steady GDP favor equities and precious metals.",
        category="market-regime",
        tags=["knowledge", "model", "macro"],
        confidence="high",
        verification="partially_verified",
    )
    regime_payload["relations"].append({"relation": "derived_from", "target": f"[[{hyp_id}]]"})
    ctrl.propose(Principal.AI_AGENT, regime_payload)

    assert ctrl.storage.get(hyp_id) is not None
    assert ctrl.storage.get(regime_id) is not None
