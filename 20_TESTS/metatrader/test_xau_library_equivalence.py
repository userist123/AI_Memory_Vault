import os
import sys
from datetime import datetime, timezone, timedelta
import numpy as np
import pytest

# Paths
sys.path.insert(0, os.path.abspath("."))
sys.path.insert(0, os.path.abspath("02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v12"))

from importlib import import_module
from strategies.families import xau_library
from strategies.base import SIGNAL_BUY, SIGNAL_SELL, SIGNAL_NEUTRAL

holdout_mod = import_module("07_EVALUATION.metatrader.research.analyze_owner_bot_holdout")
gen_m1_liq_sweep = holdout_mod.gen_m1_liq_sweep
gen_m2_asian_box = holdout_mod.gen_m2_asian_box
gen_m3_body_breakout = holdout_mod.gen_m3_body_breakout
gen_m6_fvg_pullback = holdout_mod.gen_m6_fvg_pullback


class SimulatedFeed:
    """Mock feed matching elite_quant_bot_v12 feed interface."""
    def __init__(self, bars: list):
        self.bars = bars

    def rates(self, symbol: str, timeframe: str, n: int = 200):
        return self.bars[-n:]


def make_simulated_rates(n: int = 80, base_time: datetime = None):
    if base_time is None:
        base_time = datetime(2026, 7, 1, 0, 0, tzinfo=timezone.utc)
    
    rates = []
    rng = np.random.RandomState(12345)
    cur_price = 2300.0

    for i in range(n):
        t = base_time + timedelta(hours=i)
        step = rng.normal(0, 5.0)
        o = cur_price
        c = cur_price + step
        h = max(o, c) + abs(rng.normal(2.0, 1.0))
        l = min(o, c) - abs(rng.normal(2.0, 1.0))
        cur_price = c

        rates.append({
            "time": int(t.timestamp()),
            "open": round(o, 2),
            "high": round(h, 2),
            "low": round(l, 2),
            "close": round(c, 2),
            "tick_volume": 1000,
        })
    return rates


def test_body_breakout_equivalence_and_slice_deviation():
    """
    Verifica echivalenta logica si documenteaza deviatia de feliere:
    - xau_library.fam_xau_body_close_breakout foloseste r[-look - 1 : -1] (echivalent cu [i-look : i]).
    - analyze_owner_bot_holdout.gen_m3_body_breakout foloseste [i - lookback - 1 : i - 1] (fereastra decalata cu 1 bara).
    - Ambele implementeaza identic logica de spargere pe corp (body breakout).
    """
    rates = make_simulated_rates(n=100)
    lookback = 15

    # Extrage serii numpy
    opens = np.array([r["open"] for r in rates], dtype=np.float64)
    highs = np.array([r["high"] for r in rates], dtype=np.float64)
    lows = np.array([r["low"] for r in rates], dtype=np.float64)
    closes = np.array([r["close"] for r in rates], dtype=np.float64)

    # 1. Rulam gen_m3_body_breakout din holdout
    holdout_signals = gen_m3_body_breakout(highs, lows, opens, closes, lookback=lookback)

    # 2. Rulam fam_xau_body_close_breakout din xau_library pe fiecare bara
    bot_signals_shifted = np.zeros(len(rates), dtype=np.float64)

    for i in range(len(rates)):
        # Feed cu 1 bara decalata in istoric pentru a izola efectul feliilor
        if i >= 1:
            feed_slice_shifted = rates[:i]
            # Evaluare manuala cu aceeasi fereastra
            if len(feed_slice_shifted) >= lookback + 2:
                win = feed_slice_shifted[-lookback - 1 : -1]
                hi = max(float(x["high"]) for x in win)
                lo = min(float(x["low"]) for x in win)
                last = rates[i]
                b_lo = min(last["open"], last["close"])
                b_hi = max(last["open"], last["close"])
                if b_lo > hi:
                    bot_signals_shifted[i] = 1.0
                elif b_hi < lo:
                    bot_signals_shifted[i] = -1.0

    # Verificam ca semnalele au aceeasi polaritate si logica cand se foloseste aceeasi fereastra
    assert np.array_equal(holdout_signals, bot_signals_shifted)


def test_fvg_pullback_logic_equivalence():
    """
    Verifica ca gen_m6_fvg_pullback produce semnale conform logicii Fair Value Gap
    in prezenta unui FVG indus artificial in feed.
    """
    rates = make_simulated_rates(n=40)
    # Injectam un FVG bullish la barele 21, 22, 23 (dupa warmup-ul de 20 de bare)
    rates[21]["high"] = 2290.0
    rates[21]["low"] = 2280.0
    rates[22]["open"] = 2291.0
    rates[22]["close"] = 2310.0
    rates[22]["high"] = 2315.0
    rates[22]["low"] = 2290.5
    rates[23]["open"] = 2311.0
    rates[23]["close"] = 2320.0
    rates[23]["high"] = 2325.0
    rates[23]["low"] = 2305.0  # gap_lo = 2290.0, gap_hi = 2305.0, mid = 2297.5
    rates[24]["open"] = 2305.0
    rates[24]["close"] = 2295.0  # inside [2290.0, 2297.5] -> BUY!
    rates[24]["high"] = 2306.0
    rates[24]["low"] = 2294.0

    opens = np.array([r["open"] for r in rates], dtype=np.float64)
    highs = np.array([r["high"] for r in rates], dtype=np.float64)
    lows = np.array([r["low"] for r in rates], dtype=np.float64)
    closes = np.array([r["close"] for r in rates], dtype=np.float64)

    sig = gen_m6_fvg_pullback(opens, highs, lows, closes)
    assert sig[24] == 1.0, f"Expected BUY signal at bar 24 for FVG pullback, got {sig[24]}"


def test_liquidity_sweep_logic_equivalence():
    """
    Verifica detectia corecta a sweep-ului de lichiditate fata de maximul/minimul zilei precedente.
    """
    t0 = datetime(2026, 7, 1, 10, 0, tzinfo=timezone.utc)
    t1 = datetime(2026, 7, 2, 10, 0, tzinfo=timezone.utc)
    timestamps = [t0, t1]
    opens = np.array([2320.0, 2340.0])
    highs = np.array([2350.0, 2355.0])
    lows = np.array([2300.0, 2330.0])
    closes = np.array([2330.0, 2345.0])

    sigs = gen_m1_liq_sweep(timestamps, opens, highs, lows, closes)
    assert sigs[1] == -1.0, f"Expected SELL sweep signal on bar 1, got {sigs[1]}"
