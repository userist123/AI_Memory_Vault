"""Parametrized strategy families. Each function has signature:
    fn(ctx, params, timeframe) -> (signal, confidence in [0,1])

`ctx` provides: symbol, feed, tick, info.
"""
from __future__ import annotations

from typing import Tuple

from data.indicators import (adx, atr, bollinger, ema, ema_series,
                              is_bearish_engulfing, is_bullish_engulfing,
                              is_pinbar, macd, rsi, sma, stochastic, vwap)
from strategies.base import SIGNAL_BUY, SIGNAL_NEUTRAL, SIGNAL_SELL


def _closes(rates):
    return [r["close"] for r in rates]


def _ohlc(rates):
    return ([r["open"] for r in rates], [r["high"] for r in rates],
            [r["low"] for r in rates], [r["close"] for r in rates])


def _rates(ctx, tf, n=200):
    return ctx["feed"].rates(ctx["symbol"], tf, n)


# ------------------------------------------------------------------ FAMILIES
def fam_ema_cross(ctx, p, tf) -> Tuple[str, float]:
    r = _rates(ctx, tf, 120)
    if r is None or len(r) < p["slow"] + 2:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    f = ema(c, p["fast"])
    s = ema(c, p["slow"])
    f2 = ema(c[:-1], p["fast"])
    s2 = ema(c[:-1], p["slow"])
    if None in (f, s, f2, s2):
        return SIGNAL_NEUTRAL, 0.0
    if f2 <= s2 and f > s:
        return SIGNAL_BUY, 0.6
    if f2 >= s2 and f < s:
        return SIGNAL_SELL, 0.6
    return SIGNAL_NEUTRAL, 0.0


def fam_sma_cross(ctx, p, tf):
    r = _rates(ctx, tf, 120)
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    f = sma(c, p["fast"]); s = sma(c, p["slow"])
    f2 = sma(c[:-1], p["fast"]); s2 = sma(c[:-1], p["slow"])
    if None in (f, s, f2, s2):
        return SIGNAL_NEUTRAL, 0.0
    if f2 <= s2 and f > s:
        return SIGNAL_BUY, 0.55
    if f2 >= s2 and f < s:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_rsi_meanrev(ctx, p, tf):
    r = _rates(ctx, tf, max(60, p["period"] * 4))
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    v = rsi(c, p["period"])
    if v is None:
        return SIGNAL_NEUTRAL, 0.0
    lo, hi = p["lo"], p["hi"]
    if v < lo:
        return SIGNAL_BUY, min(1.0, (lo - v) / lo + 0.3)
    if v > hi:
        return SIGNAL_SELL, min(1.0, (v - hi) / (100 - hi) + 0.3)
    return SIGNAL_NEUTRAL, 0.0


def fam_bollinger_revert(ctx, p, tf):
    r = _rates(ctx, tf, max(60, p["period"] * 3))
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    b = bollinger(c, p["period"], p["mult"])
    if b is None:
        return SIGNAL_NEUTRAL, 0.0
    mid, up, lo = b
    last = c[-1]
    if last < lo:
        return SIGNAL_BUY, 0.6
    if last > up:
        return SIGNAL_SELL, 0.6
    return SIGNAL_NEUTRAL, 0.0


def fam_donchian_breakout(ctx, p, tf):
    r = _rates(ctx, tf, p["period"] + 5)
    if r is None or len(r) < p["period"] + 2:
        return SIGNAL_NEUTRAL, 0.0
    highs = [x["high"] for x in r[:-1]]
    lows = [x["low"] for x in r[:-1]]
    hh = max(highs[-p["period"]:])
    ll = min(lows[-p["period"]:])
    last = r[-1]["close"]
    if last > hh:
        return SIGNAL_BUY, 0.65
    if last < ll:
        return SIGNAL_SELL, 0.65
    return SIGNAL_NEUTRAL, 0.0


def fam_macd_momentum(ctx, p, tf):
    r = _rates(ctx, tf, 200)
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    m = macd(c, p["fast"], p["slow"], p["signal"])
    if m is None:
        return SIGNAL_NEUTRAL, 0.0
    line, sig, hist = m
    if line > sig and hist > 0:
        return SIGNAL_BUY, min(1.0, abs(hist) * 100 + 0.4)
    if line < sig and hist < 0:
        return SIGNAL_SELL, min(1.0, abs(hist) * 100 + 0.4)
    return SIGNAL_NEUTRAL, 0.0


def fam_volatility_squeeze(ctx, p, tf):
    r = _rates(ctx, tf, 100)
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    o, h, l, c = _ohlc(r)
    a_short = atr(h, l, c, p["short"])
    a_long = atr(h, l, c, p["long"])
    if a_short is None or a_long is None or a_long == 0:
        return SIGNAL_NEUTRAL, 0.0
    ratio = a_short / a_long
    if ratio < 0.7:
        # squeeze; direction from last close vs ema
        e = ema(c, 20) or c[-1]
        if c[-1] > e:
            return SIGNAL_BUY, 0.5
        if c[-1] < e:
            return SIGNAL_SELL, 0.5
    return SIGNAL_NEUTRAL, 0.0


def fam_sr_bounce(ctx, p, tf):
    r = _rates(ctx, tf, p["period"] + 5)
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    highs = [x["high"] for x in r]
    lows = [x["low"] for x in r]
    res = max(highs[-p["period"]:-1])
    sup = min(lows[-p["period"]:-1])
    last = r[-1]["close"]
    tol = (res - sup) * 0.02
    if abs(last - sup) < tol:
        return SIGNAL_BUY, 0.55
    if abs(last - res) < tol:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_engulfing(ctx, p, tf):
    r = _rates(ctx, tf, 30)
    if r is None or len(r) < 2:
        return SIGNAL_NEUTRAL, 0.0
    o = [x["open"] for x in r]; c = [x["close"] for x in r]
    if is_bullish_engulfing(o, c):
        return SIGNAL_BUY, 0.55
    if is_bearish_engulfing(o, c):
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_pinbar(ctx, p, tf):
    r = _rates(ctx, tf, 30)
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    o, h, l, c = _ohlc(r)
    pb = is_pinbar(o, h, l, c)
    if pb == "BULL":
        return SIGNAL_BUY, 0.5
    if pb == "BEAR":
        return SIGNAL_SELL, 0.5
    return SIGNAL_NEUTRAL, 0.0


def fam_rsi_divergence(ctx, p, tf):
    r = _rates(ctx, tf, max(80, p["period"] * 4))
    if r is None or len(r) < 20:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    r1 = rsi(c[:-5], p["period"])
    r2 = rsi(c, p["period"])
    if r1 is None or r2 is None:
        return SIGNAL_NEUTRAL, 0.0
    p1, p2 = c[-6], c[-1]
    if p2 < p1 and r2 > r1:
        return SIGNAL_BUY, 0.55
    if p2 > p1 and r2 < r1:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_session_open_range(ctx, p, tf):
    r = _rates(ctx, tf, p["period"] + 2)
    if r is None or len(r) < p["period"] + 1:
        return SIGNAL_NEUTRAL, 0.0
    rng = r[-p["period"] - 1:-1]
    hh = max(x["high"] for x in rng)
    ll = min(x["low"] for x in rng)
    last = r[-1]["close"]
    if last > hh:
        return SIGNAL_BUY, 0.55
    if last < ll:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_pullback_in_trend(ctx, p, tf):
    r = _rates(ctx, tf, 120)
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    e_fast = ema(c, p["fast"]) or 0
    e_slow = ema(c, p["slow"]) or 0
    last = c[-1]
    if e_fast > e_slow and last < e_fast and last > e_slow:
        return SIGNAL_BUY, 0.55
    if e_fast < e_slow and last > e_fast and last < e_slow:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_stoch_cross(ctx, p, tf):
    r = _rates(ctx, tf, 80)
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    o, h, l, c = _ohlc(r)
    st = stochastic(h, l, c, p["k"], p["d"])
    if st is None:
        return SIGNAL_NEUTRAL, 0.0
    k, d = st
    if k < 20 and k > d:
        return SIGNAL_BUY, 0.55
    if k > 80 and k < d:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_adx_trend(ctx, p, tf):
    r = _rates(ctx, tf, max(60, p["period"] * 3))
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    o, h, l, c = _ohlc(r)
    a = adx(h, l, c, p["period"])
    if a is None or a < p["min"]:
        return SIGNAL_NEUTRAL, 0.0
    e = ema(c, 20) or c[-1]
    if c[-1] > e:
        return SIGNAL_BUY, min(1.0, a / 50.0)
    if c[-1] < e:
        return SIGNAL_SELL, min(1.0, a / 50.0)
    return SIGNAL_NEUTRAL, 0.0


def fam_vwap_deviation(ctx, p, tf):
    r = _rates(ctx, tf, p["period"])
    if r is None or len(r) < p["period"]:
        return SIGNAL_NEUTRAL, 0.0
    o, h, l, c = _ohlc(r)
    vols = [float(x.get("tick_volume", 1)) for x in r]
    v = vwap(h, l, c, vols)
    if v is None:
        return SIGNAL_NEUTRAL, 0.0
    last = c[-1]
    dev = (last - v) / v if v else 0
    if dev < -p["dev"]:
        return SIGNAL_BUY, 0.55
    if dev > p["dev"]:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_squeeze_breakout(ctx, p, tf):
    """Bollinger squeeze + breakout direction with MACD confirmation."""
    r = _rates(ctx, tf, max(80, p["period"] + 30))
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    o, h, l, c = _ohlc(r)
    b = bollinger(c, p["period"], p["mult"])
    if b is None:
        return SIGNAL_NEUTRAL, 0.0
    mid, up, lo = b
    width = (up - lo) / (mid or 1)
    # historical band widths
    widths = []
    for i in range(p["period"], len(c) - 1):
        bb = bollinger(c[: i + 1], p["period"], p["mult"])
        if bb:
            m, u, ll = bb
            widths.append((u - ll) / (m or 1))
    if not widths:
        return SIGNAL_NEUTRAL, 0.0
    # squeeze: current width below 25th percentile
    sw = sorted(widths)
    pct25 = sw[max(0, int(len(sw) * 0.25) - 1)]
    if width > pct25 * p.get("squeeze_mult", 1.1):
        return SIGNAL_NEUTRAL, 0.0
    m = macd(c, 12, 26, 9)
    if m is None:
        return SIGNAL_NEUTRAL, 0.0
    line, sig, hist = m
    last = c[-1]
    if last > up and hist > 0:
        return SIGNAL_BUY, min(1.0, 0.55 + abs(hist) * 50)
    if last < lo and hist < 0:
        return SIGNAL_SELL, min(1.0, 0.55 + abs(hist) * 50)
    return SIGNAL_NEUTRAL, 0.0


def fam_squeeze_revert(ctx, p, tf):
    """Inside a tight band: fade brief excursions back to the mid."""
    r = _rates(ctx, tf, max(60, p["period"] * 3))
    if r is None:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    b = bollinger(c, p["period"], p["mult"])
    if b is None:
        return SIGNAL_NEUTRAL, 0.0
    mid, up, lo = b
    width = (up - lo) / (mid or 1)
    if width > p.get("max_width", 0.012):
        return SIGNAL_NEUTRAL, 0.0
    last = c[-1]
    if last < lo:
        return SIGNAL_BUY, 0.55
    if last > up:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_fractal_breakout(ctx, p, tf):
    """Break of the most recent fractal swing high/low."""
    from data.indicators import fractal_high, fractal_low
    r = _rates(ctx, tf, max(50, p.get("lookback", 30)))
    if r is None or len(r) < 10:
        return SIGNAL_NEUTRAL, 0.0
    highs = [x["high"] for x in r]
    lows = [x["low"] for x in r]
    lookback = p.get("lookback", 30)
    # last confirmed fractal in window
    last_fh = None
    last_fl = None
    for i in range(len(r) - 3, max(2, len(r) - lookback), -1):
        h_slice = highs[: i + 3]
        l_slice = lows[: i + 3]
        if last_fh is None and fractal_high(h_slice):
            last_fh = highs[i]
        if last_fl is None and fractal_low(l_slice):
            last_fl = lows[i]
        if last_fh and last_fl:
            break
    last = r[-1]["close"]
    if last_fh and last > last_fh:
        return SIGNAL_BUY, 0.6
    if last_fl and last < last_fl:
        return SIGNAL_SELL, 0.6
    return SIGNAL_NEUTRAL, 0.0


def fam_macd_divergence(ctx, p, tf):
    """Classic bullish/bearish MACD-histogram vs price divergence."""
    r = _rates(ctx, tf, 80)
    if r is None or len(r) < 30:
        return SIGNAL_NEUTRAL, 0.0
    c = _closes(r)
    m1 = macd(c[:-5], p["fast"], p["slow"], p["signal"])
    m2 = macd(c, p["fast"], p["slow"], p["signal"])
    if m1 is None or m2 is None:
        return SIGNAL_NEUTRAL, 0.0
    _, _, h1 = m1
    _, _, h2 = m2
    p1, p2 = c[-6], c[-1]
    if p2 < p1 and h2 > h1:
        return SIGNAL_BUY, 0.55
    if p2 > p1 and h2 < h1:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_doji_extreme(ctx, p, tf):
    """Doji-like indecision at Bollinger extreme = mean-reversion entry."""
    r = _rates(ctx, tf, 60)
    if r is None or len(r) < 25:
        return SIGNAL_NEUTRAL, 0.0
    o, h, l, c = _ohlc(r)
    rng = (h[-1] - l[-1]) or 1e-9
    body = abs(c[-1] - o[-1]) / rng
    if body > p.get("max_body", 0.2):
        return SIGNAL_NEUTRAL, 0.0
    b = bollinger(c, 20, 2.0)
    if b is None:
        return SIGNAL_NEUTRAL, 0.0
    _, up, lo = b
    if c[-1] <= lo:
        return SIGNAL_BUY, 0.55
    if c[-1] >= up:
        return SIGNAL_SELL, 0.55
    return SIGNAL_NEUTRAL, 0.0


def fam_mtf_alignment(ctx, p, tf):
    """Confirm direction across the current tf and H1."""
    r_lo = _rates(ctx, tf, 60)
    r_hi = _rates(ctx, "H1", 60)
    if r_lo is None or r_hi is None:
        return SIGNAL_NEUTRAL, 0.0
    c_lo = _closes(r_lo); c_hi = _closes(r_hi)
    e_lo = ema(c_lo, p["ema"]) or c_lo[-1]
    e_hi = ema(c_hi, p["ema"]) or c_hi[-1]
    if c_lo[-1] > e_lo and c_hi[-1] > e_hi:
        return SIGNAL_BUY, 0.6
    if c_lo[-1] < e_lo and c_hi[-1] < e_hi:
        return SIGNAL_SELL, 0.6
    return SIGNAL_NEUTRAL, 0.0


# Registry of (family_name, fn, param_grid_list)
import config as _config
try:
    from strategies.families.xau_library import XAU_FAMILIES
except Exception:  # pragma: no cover
    XAU_FAMILIES = []

FAMILIES = [
    ("ema_cross", fam_ema_cross,
     [{"fast": f, "slow": s} for f, s in [(5, 20), (9, 21), (12, 26), (20, 50)]]),
    ("sma_cross", fam_sma_cross,
     [{"fast": f, "slow": s} for f, s in [(10, 30), (20, 50), (50, 200)]]),
    ("rsi_meanrev", fam_rsi_meanrev,
     [{"period": p, "lo": lo, "hi": hi}
      for p in (7, 14, 21) for lo, hi in [(20, 80), (25, 75), (30, 70)]]),
    ("bollinger_revert", fam_bollinger_revert,
     [{"period": p, "mult": m} for p in (10, 20, 30) for m in (1.5, 2.0, 2.5)]),
    ("donchian_breakout", fam_donchian_breakout,
     [{"period": p} for p in (10, 20, 30, 55)]),
    ("macd_momentum", fam_macd_momentum,
     [{"fast": f, "slow": s, "signal": sg}
      for f, s, sg in [(12, 26, 9), (8, 17, 9), (5, 35, 5)]]),
    ("vol_squeeze", fam_volatility_squeeze,
     [{"short": a, "long": b} for a, b in [(14, 49), (10, 30), (7, 28)]]),
    ("squeeze_breakout", fam_squeeze_breakout,
     [{"period": p, "mult": m} for p in (20,) for m in (1.5, 2.0)]),
    ("squeeze_revert", fam_squeeze_revert,
     [{"period": p, "mult": m, "max_width": w}
      for p in (20,) for m in (2.0,) for w in (0.008, 0.012)]),
    ("sr_bounce", fam_sr_bounce,
     [{"period": p} for p in (20, 50, 100)]),
    ("engulfing", fam_engulfing, [{}]),
    ("pinbar", fam_pinbar, [{}]),
    ("doji_extreme", fam_doji_extreme,
     [{"max_body": b} for b in (0.15, 0.2, 0.25)]),
    ("rsi_divergence", fam_rsi_divergence, [{"period": p} for p in (7, 14, 21)]),
    ("macd_divergence", fam_macd_divergence,
     [{"fast": f, "slow": s, "signal": sg}
      for f, s, sg in [(12, 26, 9), (8, 17, 9)]]),
    ("fractal_breakout", fam_fractal_breakout,
     [{"lookback": lb} for lb in (20, 40, 60)]),
    ("session_or", fam_session_open_range,
     [{"period": p} for p in (5, 10, 20)]),
    ("pullback_trend", fam_pullback_in_trend,
     [{"fast": f, "slow": s} for f, s in [(20, 50), (10, 30), (9, 21)]]),
    ("stoch_cross", fam_stoch_cross,
     [{"k": k, "d": d} for k in (5, 14, 21) for d in (3, 5)]),
    ("adx_trend", fam_adx_trend,
     [{"period": p, "min": m} for p in (14, 21) for m in (20, 25, 30)]),
    ("vwap_dev", fam_vwap_deviation,
     [{"period": p, "dev": d} for p in (20, 50) for d in (0.001, 0.002, 0.003)]),
    ("mtf_align", fam_mtf_alignment, [{"ema": e} for e in (20, 50, 100)]),
]

# Append XAU-only families when the profile is enabled.
if bool(getattr(_config, "XAUUSD_PROFILE_ENABLED", False)) and XAU_FAMILIES:
    FAMILIES = FAMILIES + list(XAU_FAMILIES)

