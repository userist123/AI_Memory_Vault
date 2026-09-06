"""XAUUSD trading profile: session windows, dynamic spread defense,
news blackout, family-specific ATR multipliers, adaptive drawdown reduction.

This module is a *layer*: when `config.XAUUSD_PROFILE_ENABLED` is False
or `config.SYMBOL != "XAUUSD"`, every helper here is a no-op
(returns the same answer as the base RiskManager would).

Time conventions
----------------
All session windows are float UTC hours, e.g. 7.5 == 07:30 UTC.
Windows wrap correctly across midnight if start > end.
"""
from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Deque, Dict, List, Optional, Tuple

import config


# ----------------------------------------------------------------- helpers
def _now_utc() -> datetime:
    return datetime.now(timezone.utc)


def _hour_float(dt: datetime) -> float:
    return dt.hour + dt.minute / 60.0 + dt.second / 3600.0


def _in_window(now_h: float, start: float, end: float) -> bool:
    if start <= end:
        return start <= now_h < end
    # wraps midnight
    return now_h >= start or now_h < end


def profile_active(symbol: Optional[str] = None) -> bool:
    """True iff the XAUUSD profile should govern execution."""
    if not bool(getattr(config, "XAUUSD_PROFILE_ENABLED", False)):
        return False
    sym = (symbol or getattr(config, "SYMBOL", "")).upper()
    return sym.startswith("XAU")


# ----------------------------------------------------------------- sessions
def in_allowed_session() -> bool:
    """Return True if NOW is inside any allowed XAU window OR not in a
    forbidden window. Forbidden takes precedence."""
    now_h = _hour_float(_now_utc())
    forbidden = list(getattr(config, "XAU_FORBIDDEN_WINDOWS_UTC", []))
    for s, e in forbidden:
        if _in_window(now_h, float(s), float(e)):
            return False
    allowed = list(getattr(config, "XAU_ALLOWED_WINDOWS_UTC", []))
    if not allowed:
        return True
    for s, e in allowed:
        if _in_window(now_h, float(s), float(e)):
            return True
    return False


def current_session_tag() -> str:
    """Coarse session label for feature engineering / EOD reports."""
    now_h = _hour_float(_now_utc())
    if _in_window(now_h, 7.5, 10.5):
        return "LONDON_OPEN"
    if _in_window(now_h, 12.5, 16.5):
        return "OVERLAP"
    if _in_window(now_h, 16.5, 21.0):
        return "NY_LATE"
    if _in_window(now_h, 21.5, 23.5):
        return "ROLLOVER"
    if _in_window(now_h, 0.0, 7.0):
        return "ASIAN"
    return "OFF"


# ----------------------------------------------------------------- news blackout
def in_news_blackout() -> bool:
    """A blackout window is defined by a UTC ISO timestamp + minutes
    before/after. Config holds the list of upcoming high-impact events.
    """
    events: List[str] = list(getattr(config, "XAU_NEWS_EVENTS_UTC", []))
    if not events:
        return False
    before = int(getattr(config, "XAU_NEWS_BLACKOUT_BEFORE_MIN", 30))
    after = int(getattr(config, "XAU_NEWS_BLACKOUT_AFTER_MIN", 15))
    now = _now_utc()
    for ev in events:
        try:
            ts = datetime.fromisoformat(ev.replace("Z", "+00:00"))
        except (ValueError, TypeError):
            continue
        if ts.tzinfo is None:
            ts = ts.replace(tzinfo=timezone.utc)
        if (ts - timedelta(minutes=before)) <= now <= (ts + timedelta(minutes=after)):
            return True
    return False


# ----------------------------------------------------------------- spread MA
class SpreadMonitor:
    """Rolling average of spread (in pips). Used to flag abnormal widening."""

    def __init__(self, window: int = 240) -> None:
        self._buf: Deque[float] = deque(maxlen=int(window))

    def push(self, spread_pips: float) -> None:
        if spread_pips is None or spread_pips < 0:
            return
        self._buf.append(float(spread_pips))

    def avg(self) -> float:
        return (sum(self._buf) / len(self._buf)) if self._buf else 0.0

    def is_abnormal(self, current: float) -> bool:
        avg = self.avg()
        if avg <= 0 or len(self._buf) < 10:
            return False
        ratio = float(getattr(config, "XAU_SPREAD_MAX_RATIO", 2.0))
        return current > avg * ratio


# ------------------------------------------------------- family ATR mult
_DEFAULT_SL_MULTS: Dict[str, float] = {
    # Breakout-like families need extra room
    "donchian_breakout": 2.5,
    "fractal_breakout": 2.5,
    "squeeze_breakout": 2.5,
    "session_or": 2.5,
    "xau_asian_box_break": 2.8,
    "xau_body_close_breakout": 2.8,
    "xau_liquidity_sweep": 2.2,
    # Mean-reversion / range families: tight
    "bollinger_revert": 1.2,
    "squeeze_revert": 1.2,
    "rsi_meanrev": 1.3,
    "doji_extreme": 1.2,
    "vwap_dev": 1.3,
    # Trend / pullback / continuation: default
    "ema_cross": 2.0,
    "sma_cross": 2.0,
    "pullback_trend": 2.0,
    "macd_momentum": 2.0,
    "macd_divergence": 2.0,
    "rsi_divergence": 1.8,
    "adx_trend": 2.0,
    "mtf_align": 2.0,
    "engulfing": 1.8,
    "pinbar": 1.8,
    "sr_bounce": 1.8,
    "stoch_cross": 1.8,
    "vol_squeeze": 2.0,
    "xau_fvg_pullback": 2.0,
}


def sl_atr_mult_for(family: str) -> float:
    """SL multiplier (× ATR) for the given family under XAU profile."""
    overrides = dict(getattr(config, "XAU_FAMILY_ATR_MULTS", {}) or {})
    return float(overrides.get(family, _DEFAULT_SL_MULTS.get(family, 2.0)))


# --------------------------------------------------------- ML thresholds
_TREND_FAMILIES = {
    "ema_cross", "sma_cross", "macd_momentum", "donchian_breakout",
    "pullback_trend", "adx_trend", "mtf_align", "squeeze_breakout",
    "session_or", "fractal_breakout", "xau_asian_box_break",
    "xau_body_close_breakout", "xau_liquidity_sweep", "xau_fvg_pullback",
}
_MR_FAMILIES = {
    "rsi_meanrev", "bollinger_revert", "squeeze_revert", "doji_extreme",
    "vwap_dev", "sr_bounce", "rsi_divergence", "macd_divergence",
    "engulfing", "pinbar",
}


def family_kind(family: str) -> str:
    if family in _MR_FAMILIES:
        return "MR"
    return "TREND"


def ml_threshold_for(family: str) -> float:
    if family_kind(family) == "MR":
        return float(getattr(config, "XAU_ML_THRESHOLD_MR", 0.55))
    return float(getattr(config, "XAU_ML_THRESHOLD_TREND", 0.62))


# ----------------------------------------------------------- adaptive DD
@dataclass
class AdaptiveRiskState:
    reduced: bool = False
    wins_since_reduction: int = 0
    original_risk_pct: float = 0.0


_adaptive = AdaptiveRiskState()


def check_and_apply_adaptive_dd(day_start_balance: float,
                                realised_pnl_today: float) -> Optional[str]:
    """If today's DD breaches `XAU_DD_REDUCE_PCT`, halve RISK_PCT.
    Restore after `XAU_DD_RECOVERY_WINS` net wins.
    Returns a short status string when a transition fires, else None."""
    if day_start_balance <= 0:
        return None
    dd_pct = float(getattr(config, "XAU_DD_REDUCE_PCT", 0.03))
    factor = float(getattr(config, "XAU_DD_RISK_FACTOR", 0.5))
    if (not _adaptive.reduced
            and realised_pnl_today <= -abs(day_start_balance * dd_pct)):
        _adaptive.original_risk_pct = float(config.RISK_PCT)
        new_pct = max(0.001, _adaptive.original_risk_pct * factor)
        config.RISK_PCT = new_pct
        _adaptive.reduced = True
        _adaptive.wins_since_reduction = 0
        return f"DD {realised_pnl_today:.2f} <= -{dd_pct*100:.1f}% → RISK_PCT={new_pct:.4f}"
    return None


def on_trade_result(won: bool) -> Optional[str]:
    if not _adaptive.reduced:
        return None
    if won:
        _adaptive.wins_since_reduction += 1
    else:
        _adaptive.wins_since_reduction = max(
            0, _adaptive.wins_since_reduction - 1)
    need = int(getattr(config, "XAU_DD_RECOVERY_WINS", 10))
    if _adaptive.wins_since_reduction >= need:
        old = float(config.RISK_PCT)
        config.RISK_PCT = float(_adaptive.original_risk_pct or config.RISK_PCT)
        _adaptive.reduced = False
        _adaptive.wins_since_reduction = 0
        return f"Recovered: RISK_PCT restored {old:.4f} → {config.RISK_PCT:.4f}"
    return None


def reset_adaptive_state() -> None:
    _adaptive.reduced = False
    _adaptive.wins_since_reduction = 0
    _adaptive.original_risk_pct = 0.0
