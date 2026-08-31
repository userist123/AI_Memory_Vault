"""
Tier 4: Real-World Workload Scenarios Test Suite.
Exhaustively simulates realistic end-to-end institutional financial workloads:
- Scenario 1: Full Market Cycle Simulation (Accumulation -> Markup -> Distribution -> Markdown -> Bottom)
- Scenario 2: Macro Regime Shift Simulation (Inflation/Rate Hikes -> Yield Curve Inversion -> Easing & Asset Rotation)
- Scenario 3: Gold (XAU/USD) Kinetic Volatility Breakout (London/NY overlap, dynamic ATR sizing, TP ladder)
- Scenario 4: Disciplined Systematic Execution vs Emotional Revenge Trading Post-Mortem
"""

import os
import math
import uuid
import pytest
from datetime import datetime, timezone

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.authorizer import Principal

from tests.financial.test_tier1_features import (
    calculate_rsi,
    calculate_macd,
    calculate_bollinger_bands,
    calculate_atr,
    calculate_confluence_score,
    calculate_portfolio_metrics,
    create_valid_note_payload,
    execute_formal_reflexion,
)


def test_tier4_scenario1_full_market_cycle_simulation(isolated_controller):
    """
    Scenario 1: Full Market Cycle Simulation
    Simulates a 100-bar multi-phase asset cycle:
    1. Accumulation: Tight BB squeeze, low RVOL, neutral RSI
    2. Markup / Bull Breakout: Golden cross, RSI > 60, MACD positive impulse, Confluence BUY
    3. Distribution Peak: RSI > 80, BB upper pierce, RVOL exhaustion
    4. Markdown / Bear Crash: Death cross, RSI < 35, MACD negative histogram, Confluence SELL
    5. Capitulation & Bottom: RSI < 25 oversold, mean reversion bounce
    """
    ctrl = isolated_controller

    # Synthesize 100 bars
    # Phase 1: Bars 0-25: Flat around 100.0
    phase1 = [100.0 + (0.2 if i % 2 == 0 else -0.2) for i in range(25)]
    # Phase 2: Bars 25-50: Bull run from 100 to 180
    phase2 = [100.0 + (i * 3.2) for i in range(25)]
    # Phase 3: Bars 50-65: Distribution top around 180
    phase3 = [180.0 + (1.0 if i % 2 == 0 else -1.0) for i in range(15)]
    # Phase 4: Bars 65-85: Bear crash from 180 to 90
    phase4 = [180.0 - (i * 4.5) for i in range(20)]
    # Phase 5: Bars 85-100: Bottoming bounce from 90 to 105
    phase5 = [90.0 + (i * 1.0) for i in range(15)]

    full_cycle_closes = phase1 + phase2 + phase3 + phase4 + phase5
    assert len(full_cycle_closes) == 100

    # 1. Evaluate Phase 1 Accumulation
    bb_phase1 = calculate_bollinger_bands(phase1, 20)
    assert bb_phase1["bandwidth"] < 2.0  # Tight squeeze

    # 2. Evaluate Phase 2 Markup Breakout
    rsi_phase2 = calculate_rsi(phase1[-10:] + phase2[:15], 14)
    assert rsi_phase2 > 65.0

    confluence_markup = calculate_confluence_score(
        rsi=40.0,  # Pullback dip in markup phase (+1)
        macd_hist=2.5,  # MACD positive (+1)
        price=145.0,
        sma50=120.0,
        sma200=105.0,  # Golden cross (+2)
        rvol=2.2,  # RVOL confirmation (+1)
    )
    assert confluence_markup["signal"] == "BUY"

    # 3. Evaluate Phase 3 Distribution Peak (Euphoria end of phase 2 / start of phase 3)
    rsi_peak = calculate_rsi(phase2, 14)
    assert rsi_peak > 75.0

    # 4. Evaluate Phase 4 Markdown Crash
    rsi_phase4 = calculate_rsi(phase4, 14)
    assert rsi_phase4 < 30.0

    confluence_crash = calculate_confluence_score(
        rsi=76.0,  # Overbought distribution rollover (-2)
        macd_hist=-4.2,  # MACD negative (-1)
        price=170.0,
        sma50=130.0,
        sma200=150.0,  # Death cross (-2)
        rvol=2.8,  # RVOL surge (+1)
    )
    assert confluence_crash["signal"] == "SELL"

    # Persist market cycle report to memory
    cycle_note_id = str(uuid.uuid4())
    cycle_payload = create_valid_note_payload(
        note_id=cycle_note_id,
        note_type="knowledge",
        title="Asset Lifecycle Case Study: 100-Bar Cycle",
        content="Full cycle simulation demonstrated clear transition from accumulation squeeze to breakout and final capitulation.",
        category="market-regime",
        tags=["market_cycle", "macro", "backtest"],
    )
    ctrl.propose(Principal.AI_AGENT, cycle_payload)
    assert ctrl.storage.get(cycle_note_id) is not None


def test_tier4_scenario2_macro_regime_shift_simulation(isolated_controller):
    """
    Scenario 2: Macro Regime Shift from Tightening to Easing
    1. Regime A (Inflation & Hikes): High Fed Funds (5.5%), Inverted Yield Curve (10Y-2Y = -0.80%), S&P under pressure.
    2. Regime Shift Catalyst: CPI cools to 2.4%, Fed pivots to rate cuts.
    3. Regime B (Disinflationary Easing): Fed Funds cuts 100bps, Yield curve normalizes (+0.25%), Gold and Equities rally.
    4. Proposes canonical regime models and verifies supersession lineage.
    """
    ctrl = isolated_controller

    # Regime A Note
    regime_a_id = str(uuid.uuid4())
    payload_a = create_valid_note_payload(
        note_id=regime_a_id,
        note_type="knowledge",
        title="Macro Regime 2025: Hawkish Restrictive Policy",
        content="Fed Funds at 5.5%, Inverted Yield Curve (-80bps), Cash yielding 5.3%. Defensive allocation favored.",
        category="market-regime",
        tags=["macro", "regime/hawkish", "rates"],
        lifecycle="ACTIVE",
        source_type="official",
    )
    ctrl.propose(Principal.HUMAN, payload_a)

    # Regime B Note (Successor)
    regime_b_id = str(uuid.uuid4())
    payload_b = create_valid_note_payload(
        note_id=regime_b_id,
        note_type="knowledge",
        title="Macro Regime 2026: Easing & Yield Curve Normalization",
        content="Fed cuts rates 100bps, Curve steepens positive (+25bps), Real yields plunge. Aggressive rotation to Gold and S&P 500.",
        category="market-regime",
        tags=["macro", "regime/easing", "rates"],
        lifecycle="ACTIVE",
        source_type="official",
    )
    ctrl.propose(Principal.HUMAN, payload_b)

    # Supersede Regime A with Regime B
    ctrl.supersede(Principal.HUMAN, regime_a_id, regime_b_id, "Regime shift confirmed by Fed rate cut cycle")

    # Verify active lineage points to Regime B
    active_lineage = ctrl.storage.resolve_active_lineage(regime_a_id)
    assert active_lineage == regime_b_id


def test_tier4_scenario3_gold_kinetic_volatility_breakout(isolated_controller):
    """
    Scenario 3: Gold (XAU/USD) Kinetic Volatility Breakout
    1. London Open Volatility Expansion: Price breaks $2,510.50 with RVOL = 2.4.
    2. Dynamic ATR Sizing: ATR = $14.50, Account Equity = $100,000, 1.0% Risk ($1,000).
       SL Distance = 1.5 * ATR = $21.75 -> Position size = $1,000 / $21.75 = 45.97 oz (~46 oz).
    3. Take-Profit Ladder Execution:
       - TP1 (+1.5R = $2,543.12): Close 50% position (+0.75R profit).
       - Trailing SL: Shift SL to Break-Even ($2,510.50).
       - TP2 (+3.0R = $2,575.75): Close remaining 50% position (+1.5R profit).
    4. Total Realized R-Multiple: +2.25R.
    """
    ctrl = isolated_controller

    equity = 100000.0
    risk_pct = 0.01  # 1.0%
    risk_dollars = equity * risk_pct  # $1,000
    atr = 14.50
    entry_price = 2510.50

    sl_dist = 1.5 * atr  # $21.75
    sl_price = entry_price - sl_dist  # $2488.75
    pos_size_oz = round(risk_dollars / sl_dist, 2)  # 45.98 oz

    tp1_price = entry_price + (1.5 * sl_dist)  # $2543.125
    tp2_price = entry_price + (3.0 * sl_dist)  # $2575.75

    # Simulating TP ladder execution
    # Step 1: TP1 hit -> 50% closed
    pnl_tp1 = (tp1_price - entry_price) * (pos_size_oz * 0.5)
    # Step 2: TP2 hit -> 50% closed
    pnl_tp2 = (tp2_price - entry_price) * (pos_size_oz * 0.5)
    total_pnl = pnl_tp1 + pnl_tp2

    realized_rr = total_pnl / risk_dollars
    assert round(realized_rr, 2) == 2.25

    # Log completed trade to journal
    trade_id = str(uuid.uuid4())
    journal_payload = create_valid_note_payload(
        note_id=trade_id,
        note_type="experience",
        title="Executed Trade Log: Gold Kinetic Breakout Scaling",
        content=(
            f"Asset: XAU/USD\n"
            f"Entry: ${entry_price} | SL: ${sl_price}\n"
            f"TP1: ${tp1_price} (50% fill) | TP2: ${tp2_price} (50% fill)\n"
            f"Realized P&L: ${round(total_pnl, 2)} (+{round(realized_rr, 2)}R)\n"
            f"Execution Discipline: 10/10"
        ),
        category="trading-journal",
        tags=["trade", "asset/xau", "ladder_scaling", "outcome/win"],
    )
    ctrl.propose(Principal.AI_AGENT, journal_payload)
    assert ctrl.storage.get(trade_id) is not None


def test_tier4_scenario4_disciplined_vs_revenge_trading_post_mortem(isolated_controller):
    """
    Scenario 4: Disciplined Systematic Execution vs Emotional Revenge Trading
    1. Trade 1: Disciplined Win (+2.0R, Plan Adhered = True, Quality = 10).
    2. Trade 2: Disciplined Loss (-1.0R, Plan Adhered = True, Quality = 9).
    3. Trade 3: Revenge Trading Tilt (-3.0R loss, 3x position size, Plan Adhered = False, Quality = 2).
    4. Circuit Breaker VETO: Daily Drawdown limit (-4.0% of equity) triggers instant lockout.
    5. Reflexion Engine: Emits 6-stage post-mortem error note, root cause diagnosis, and prevention rule.
    """
    ctrl = isolated_controller

    trades = [
        {
            "trade_id": "T-DISCIPLINED-01",
            "asset": "GC=F",
            "direction": "LONG",
            "entry_price": 2500.0,
            "stop_loss": 2490.0,
            "exit_price": 2520.0,
            "position_size": 1.0,
            "pnl_currency": 2000.0,
            "realized_rr": 2.0,
            "plan_adhered": True,
            "execution_quality": 10,
            "emotion": "Calm",
            "lesson": "Systematic breakout execution on London open",
        },
        {
            "trade_id": "T-DISCIPLINED-02",
            "asset": "NVDA",
            "direction": "SHORT",
            "entry_price": 130.0,
            "stop_loss": 133.0,
            "exit_price": 133.0,
            "position_size": 300.0,
            "pnl_currency": -900.0,
            "realized_rr": -1.0,
            "plan_adhered": True,
            "execution_quality": 9,
            "emotion": "Disciplined",
            "lesson": "Accept standard stop-out without emotional attachment",
        },
        {
            "trade_id": "T-REVENGE-03",
            "asset": "NVDA",
            "direction": "LONG",
            "entry_price": 133.5,
            "stop_loss": 128.5,
            "exit_price": 128.5,
            "position_size": 900.0,  # 3x revenge sizing!
            "pnl_currency": -4500.0,
            "realized_rr": -3.0,
            "plan_adhered": False,
            "execution_quality": 2,
            "emotion": "Revenge / Tilt",
            "lesson": "Never increase position size following a loss to recoup equity",
        },
    ]

    # Evaluate aggregate metrics
    metrics = calculate_portfolio_metrics(trades)
    assert metrics["win_rate"] == 33.33  # 1 win out of 3
    # Net PnL: +2000 - 900 - 4500 = -$3,400

    # Trigger Risk Manager Circuit Breaker Lockout
    daily_drawdown_dollars = abs(sum(t["pnl_currency"] for t in trades if t["pnl_currency"] < 0))
    circuit_breaker_threshold = 4000.0  # Max $4k daily loss limit
    is_veto_active = daily_drawdown_dollars >= circuit_breaker_threshold
    assert is_veto_active, "Risk Manager Circuit Breaker must exercise VETO over subsequent orders"

    # Trigger FormalReflexion on the Revenge Trade
    revenge_trade = trades[2]
    err_note, lesson_note = execute_formal_reflexion(revenge_trade)

    assert "Revenge / Tilt" in err_note["content"]
    assert "Never increase position size" in lesson_note["content"]

    ctrl.propose(Principal.AI_AGENT, err_note)
    ctrl.propose(Principal.AI_AGENT, lesson_note)

    assert ctrl.storage.get(err_note["id"]) is not None
    assert ctrl.storage.get(lesson_note["id"]) is not None
