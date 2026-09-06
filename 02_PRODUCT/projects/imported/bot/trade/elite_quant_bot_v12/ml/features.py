"""Feature engineering for the online ML model.

Compact, robust vector covering: technical state, volatility/regime, time/session.
All inputs come from data/indicators + the local feed/tick (no external sources).

When `config.XAUUSD_PROFILE_ENABLED` is True, an extra XAU-specific block
is APPENDED via `ml.xau_features.build_xau_extras`. FEATURE_DIM grows
accordingly. ml.store.MLStore resets weights automatically when the
stored dim no longer matches FEATURE_DIM.
"""
from __future__ import annotations

import math
from datetime import datetime, timezone
from typing import List, Optional

import config

from data.indicators import (adx, atr, bollinger, ema, macd, rsi,
                              is_bearish_engulfing, is_bullish_engulfing,
                              is_pinbar)
from ml.xau_features import XAU_EXTRA_DIM, build_xau_extras


def build_features(rates_h1, rates_m15, tick, info, feed=None) -> List[float]:
    closes_h1 = [r["close"] for r in rates_h1]
    closes_m15 = [r["close"] for r in rates_m15]
    highs_m15 = [r["high"] for r in rates_m15]
    lows_m15 = [r["low"] for r in rates_m15]
    opens_m15 = [r["open"] for r in rates_m15]

    last = closes_m15[-1] or 1.0

    # ---- oscillators
    rsi_h1 = (rsi(closes_h1, 14) or 50.0)
    rsi_m15 = (rsi(closes_m15, 14) or 50.0)

    # ---- trend (EMA stack on H1)
    e20 = ema(closes_h1, 20) or last
    e50 = ema(closes_h1, 50) or last
    e200 = ema(closes_h1, 200) or last
    slope_20_50 = (e20 - e50) / (e50 or 1)
    px_vs_e20 = (last - e20) / (e20 or 1)
    px_vs_e200 = (last - e200) / (e200 or 1)
    regime_trend = 1.0 if e20 > e50 > e200 else (-1.0 if e20 < e50 < e200 else 0.0)

    # ---- MACD on M15
    m = macd(closes_m15, 12, 26, 9)
    if m is None:
        macd_line = macd_sig = macd_hist = 0.0
    else:
        macd_line, macd_sig, macd_hist = m
    macd_hist_n = macd_hist / (last * 1e-3 + 1e-9)   # normalised

    # ---- volatility
    atr_s = atr(highs_m15, lows_m15, closes_m15, 14) or 0.0
    atr_l = atr(highs_m15, lows_m15, closes_m15, 49) or 0.0
    atr_ratio = atr_s / atr_l if atr_l > 0 else 1.0

    bb = bollinger(closes_m15, 20, 2.0)
    if bb is None:
        bb_width = 0.0
    else:
        mid, up, lo = bb
        bb_width = (up - lo) / (mid or 1)

    # ---- regime: ADX
    adx_v = adx(highs_m15, lows_m15, closes_m15, 14) or 0.0
    trending = 1.0 if adx_v > 25 else 0.0

    # ---- price action
    o, c = opens_m15[-1], closes_m15[-1]
    h, l = highs_m15[-1], lows_m15[-1]
    rng = (h - l) or 1e-9
    body_ratio = abs(c - o) / rng
    upper_wick = (h - max(o, c)) / rng
    lower_wick = (min(o, c) - l) / rng
    pat_bull = 1.0 if (is_bullish_engulfing(opens_m15, closes_m15)
                       or is_pinbar(opens_m15, highs_m15, lows_m15, closes_m15)
                       == "BULL") else 0.0
    pat_bear = 1.0 if (is_bearish_engulfing(opens_m15, closes_m15)
                       or is_pinbar(opens_m15, highs_m15, lows_m15, closes_m15)
                       == "BEAR") else 0.0

    # ---- time / session (sin/cos for cyclicity)
    hour = datetime.now(timezone.utc).hour
    h_sin = math.sin(2 * math.pi * hour / 24.0)
    h_cos = math.cos(2 * math.pi * hour / 24.0)
    sess_london = 1.0 if 7 <= hour < 16 else 0.0
    sess_ny = 1.0 if 12 <= hour < 21 else 0.0

    # ---- microstructure
    point = float(getattr(info, "point", 0.0)) or 1e-5
    spread_pips = (tick.ask - tick.bid) / (point * 10.0)

    base = [
        1.0,                       # 0 bias
        (rsi_h1 - 50) / 50.0,      # 1
        (rsi_m15 - 50) / 50.0,     # 2
        slope_20_50 * 100.0,       # 3
        px_vs_e20 * 100.0,         # 4
        px_vs_e200 * 100.0,        # 5
        regime_trend,              # 6
        macd_line * 1000.0,        # 7
        macd_sig * 1000.0,         # 8
        max(-5.0, min(5.0, macd_hist_n)),  # 9 clipped
        atr_ratio - 1.0,           # 10
        bb_width * 100.0,          # 11
        adx_v / 50.0,              # 12
        trending,                  # 13
        body_ratio,                # 14
        upper_wick - lower_wick,   # 15
        pat_bull - pat_bear,       # 16
        h_sin,                     # 17
        h_cos,                     # 18
        sess_london,               # 19
        sess_ny,                   # 20
        spread_pips / 5.0,         # 21
    ]
    if bool(getattr(config, "XAUUSD_PROFILE_ENABLED", False)):
        base.extend(build_xau_extras(rates_h1, rates_m15, tick, feed=feed))
    return base


_BASE_DIM = 22
FEATURE_DIM = _BASE_DIM + (XAU_EXTRA_DIM
                           if bool(getattr(config, "XAUUSD_PROFILE_ENABLED",
                                           False)) else 0)
