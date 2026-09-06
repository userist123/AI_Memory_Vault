"""Extra ML features that are only meaningful for XAUUSD.

Returned vector is APPENDED to the base feature vector when
`config.XAUUSD_PROFILE_ENABLED` is True. All values are robust to missing
data (return 0.0 instead of raising).

Slots (fixed = `XAU_EXTRA_DIM`):
    0  dist_to_asian_high_pct
    1  dist_to_asian_low_pct
    2  dist_to_pdh_pct
    3  dist_to_pdl_pct
    4  sess_london          (0/1)
    5  sess_overlap         (0/1)
    6  sess_asian           (0/1)
    7  sess_ny_late         (0/1)
    8  dxy_delta_pct        (last 20 bars on its own timeframe, optional)
    9  us10y_delta_pct      (last 20 bars on its own timeframe, optional)
"""
from __future__ import annotations

from datetime import datetime, time as dtime, timezone
from typing import List, Optional

import config

XAU_EXTRA_DIM = 10


def _safe_div(a: float, b: float) -> float:
    return (a / b) if b not in (0, 0.0, None) else 0.0


def _session_flags() -> List[float]:
    h = datetime.now(timezone.utc).hour + datetime.now(timezone.utc).minute / 60.0
    london = 1.0 if 7.5 <= h < 10.5 else 0.0
    overlap = 1.0 if 12.5 <= h < 16.5 else 0.0
    asian = 1.0 if (h < 7.0) else 0.0
    ny_late = 1.0 if 16.5 <= h < 21.0 else 0.0
    return [london, overlap, asian, ny_late]


def _delta_pct(rates) -> float:
    if rates is None or len(rates) < 21:
        return 0.0
    try:
        c_now = float(rates[-1]["close"])
        c_ref = float(rates[-21]["close"])
    except Exception:
        return 0.0
    return _safe_div(c_now - c_ref, c_ref)


def _asian_box(rates_m15) -> Optional[tuple]:
    """High/Low of the most recent Asian session window (00:00–07:00 UTC)."""
    if rates_m15 is None or len(rates_m15) == 0:
        return None
    today = datetime.now(timezone.utc).date()
    hi, lo = float("-inf"), float("inf")
    have = False
    for r in rates_m15:
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


def _prev_day_hl(rates_h1) -> Optional[tuple]:
    if rates_h1 is None or len(rates_h1) == 0:
        return None
    today = datetime.now(timezone.utc).date()
    hi, lo = float("-inf"), float("inf")
    have = False
    for r in rates_h1:
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


def build_xau_extras(rates_h1, rates_m15, tick, feed=None) -> List[float]:
    """Build the XAU-only extras. `feed` is optional; if given we try DXY
    and US10Y series via configured symbol names."""
    out = [0.0] * XAU_EXTRA_DIM
    last = None
    try:
        last = float(rates_m15[-1]["close"]) if rates_m15 else None
    except Exception:
        last = None
    if last:
        ab = _asian_box(rates_m15)
        if ab:
            hi, lo = ab
            out[0] = _safe_div(last - hi, last)
            out[1] = _safe_div(last - lo, last)
        pdh = _prev_day_hl(rates_h1)
        if pdh:
            hi, lo = pdh
            out[2] = _safe_div(last - hi, last)
            out[3] = _safe_div(last - lo, last)

    sf = _session_flags()
    out[4], out[5], out[6], out[7] = sf

    if feed is not None:
        dxy_sym = str(getattr(config, "XAU_DXY_SYMBOL", "") or "")
        us10_sym = str(getattr(config, "XAU_US10Y_SYMBOL", "") or "")
        if dxy_sym:
            try:
                r = feed.rates(dxy_sym, "H1", 30)
                out[8] = _delta_pct(r) * 100.0
            except Exception:
                out[8] = 0.0
        if us10_sym:
            try:
                r = feed.rates(us10_sym, "H1", 30)
                out[9] = _delta_pct(r) * 100.0
            except Exception:
                out[9] = 0.0
    return out
