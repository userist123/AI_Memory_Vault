"""XAUUSD-only strategy families. Enabled by appending to FAMILIES when
`config.XAUUSD_PROFILE_ENABLED` is True.

All functions follow the standard `fn(ctx, params, timeframe)` signature
and rely only on indicators already available in `data.indicators`.
"""
from __future__ import annotations

from datetime import datetime, timezone
from typing import Tuple

from data.indicators import atr, ema
from strategies.base import SIGNAL_BUY, SIGNAL_NEUTRAL, SIGNAL_SELL


def _rates(ctx, tf, n=200):
    return ctx["feed"].rates(ctx["symbol"], tf, n)


def _asian_box(rates):
    today = datetime.now(timezone.utc).date()
    hi, lo = float("-inf"), float("inf")
    have = False
    for r in rates:
        try:
            ts = datetime.fromtimestamp(int(r["time"]), tz=timezone.utc)
        except Exception:
            continue
        if ts.date() != today:
            continue
        if 0 <= ts.hour < 7:
            hi = max(hi, float(r["high"]))
            lo = min(lo, float(r["low"]))
            have = True
    return (hi, lo) if have else None


def _prev_day_hl(rates):
    today = datetime.now(timezone.utc).date()
    hi, lo = float("-inf"), float("inf")
    have = False
    for r in rates:
        try:
            ts = datetime.fromtimestamp(int(r["time"]), tz=timezone.utc)
        except Exception:
            continue
        if ts.date() == today:
            continue
        hi = max(hi, float(r["high"]))
        lo = min(lo, float(r["low"]))
        have = True
    return (hi, lo) if have else None


# -------------------------------------------------- liquidity sweep
def fam_xau_liquidity_sweep(ctx, p, tf) -> Tuple[str, float]:
    """Sweep of PDH/PDL with body close back inside range = reversal."""
    r = _rates(ctx, tf, 120)
    if r is None or len(r) < 30:
        return SIGNAL_NEUTRAL, 0.0
    pdh = _prev_day_hl(r)
    if not pdh:
        return SIGNAL_NEUTRAL, 0.0
    hi, lo = pdh
    last = r[-1]
    body_high = max(float(last["open"]), float(last["close"]))
    body_low = min(float(last["open"]), float(last["close"]))
    wick_high = float(last["high"])
    wick_low = float(last["low"])
    if wick_high > hi and body_high <= hi:
        return SIGNAL_SELL, 0.65
    if wick_low < lo and body_low >= lo:
        return SIGNAL_BUY, 0.65
    return SIGNAL_NEUTRAL, 0.0


# -------------------------------------------------- asian box break
def fam_xau_asian_box_break(ctx, p, tf) -> Tuple[str, float]:
    """Body-close break of the Asian box during London/Overlap windows only."""
    now_h = datetime.now(timezone.utc).hour
    if not (7 <= now_h < 17):
        return SIGNAL_NEUTRAL, 0.0
    r = _rates(ctx, tf, 200)
    if r is None or len(r) < 10:
        return SIGNAL_NEUTRAL, 0.0
    ab = _asian_box(r)
    if not ab:
        return SIGNAL_NEUTRAL, 0.0
    hi, lo = ab
    last = r[-1]
    c = float(last["close"])
    o = float(last["open"])
    body_high = max(o, c)
    body_low = min(o, c)
    if body_low > hi and c > o:
        return SIGNAL_BUY, 0.6
    if body_high < lo and c < o:
        return SIGNAL_SELL, 0.6
    return SIGNAL_NEUTRAL, 0.0


# -------------------------------------------------- body close breakout
def fam_xau_body_close_breakout(ctx, p, tf) -> Tuple[str, float]:
    """Lookback swing-high/low broken AND closed beyond on full body."""
    look = int(p.get("lookback", 20))
    r = _rates(ctx, tf, look + 5)
    if r is None or len(r) < look + 2:
        return SIGNAL_NEUTRAL, 0.0
    window = r[-look - 1:-1]
    hi = max(float(x["high"]) for x in window)
    lo = min(float(x["low"]) for x in window)
    last = r[-1]
    o = float(last["open"]); c = float(last["close"])
    body_low = min(o, c); body_high = max(o, c)
    if body_low > hi:
        return SIGNAL_BUY, 0.65
    if body_high < lo:
        return SIGNAL_SELL, 0.65
    return SIGNAL_NEUTRAL, 0.0


# -------------------------------------------------- FVG pullback
def fam_xau_fvg_pullback(ctx, p, tf) -> Tuple[str, float]:
    """Three-candle Fair Value Gap detection + price returning to 50% of gap."""
    r = _rates(ctx, tf, 60)
    if r is None or len(r) < 5:
        return SIGNAL_NEUTRAL, 0.0
    # search the most recent FVG within last 15 bars
    found = None
    for i in range(len(r) - 3, max(1, len(r) - 15), -1):
        a = r[i - 1]; b = r[i]; c = r[i + 1]
        # bullish FVG: a.high < c.low (gap above a)
        if float(a["high"]) < float(c["low"]):
            found = ("BUY", float(a["high"]), float(c["low"]))
            break
        # bearish FVG: a.low > c.high
        if float(a["low"]) > float(c["high"]):
            found = ("SELL", float(c["high"]), float(a["low"]))
            break
    if not found:
        return SIGNAL_NEUTRAL, 0.0
    side, gap_lo, gap_hi = found
    mid = 0.5 * (gap_lo + gap_hi)
    last = float(r[-1]["close"])
    if side == "BUY" and gap_lo <= last <= mid:
        return SIGNAL_BUY, 0.6
    if side == "SELL" and mid <= last <= gap_hi:
        return SIGNAL_SELL, 0.6
    return SIGNAL_NEUTRAL, 0.0


XAU_FAMILIES = [
    ("xau_liquidity_sweep", fam_xau_liquidity_sweep, [{}]),
    ("xau_asian_box_break", fam_xau_asian_box_break, [{}]),
    ("xau_body_close_breakout", fam_xau_body_close_breakout,
     [{"lookback": lb} for lb in (15, 25, 40)]),
    ("xau_fvg_pullback", fam_xau_fvg_pullback, [{}]),
]
