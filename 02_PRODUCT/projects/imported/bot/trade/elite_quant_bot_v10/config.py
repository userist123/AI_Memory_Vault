"""Central configuration for the Elite Quant Bot.

Tunables live here so behaviour can be adjusted without touching strategy /
execution code. Values can be overridden at runtime via
`config_overrides.json` (written by the UI config editor) and applied with
`apply_overrides()` / a live state-machine restart.
"""
from __future__ import annotations

import json
import os
from typing import Any, Dict, List

# --- Account / instrument ----------------------------------------------------
SYMBOL: str = "EURUSD"
MAGIC: int = 777777

# --- Timeframes (MT5 style) --------------------------------------------------
PRIMARY_TF: str = "H1"
FAST_TF: str = "M15"

# --- Risk --------------------------------------------------------------------
RISK_PCT: float = 0.01
ATR_SL_MULT: float = 0.8
ATR_TP_MULT: float = 1.0          # used only when TP_LEVELS == 1
MAX_SPREAD_PIPS: float = 2.0

# --- Multi-TP ladder ---------------------------------------------------------
# TP_LEVELS == 1 keeps legacy single-TP behaviour. >1 enables partial closes.
TP_LEVELS: int = 3
TP_RR_MULTIPLIERS: List[float] = [0.8, 1.5, 2.5]   # in R units of SL distance
TP_VOLUME_FRACTIONS: List[float] = [0.4, 0.35, 0.25]
SL_TRAILING_MODE: str = "TO_BREAK_EVEN_AT_TP1"     # NONE | TO_BREAK_EVEN_AT_TP1 | TO_BE_PLUS_AT_TP2
BE_PLUS_R: float = 0.5

# --- Circuit breakers --------------------------------------------------------
DAILY_LOSS_LIMIT_PCT: float = 0.03
MAX_CONSECUTIVE_LOSSES: int = 3
COOLDOWN_MINUTES: int = 30
MAX_ORDERS_PER_DAY: int = 50
MAX_CONCURRENT_POSITIONS: int = 5

# --- Session (UTC) -----------------------------------------------------------
SESSION_START_HOUR_UTC: int = 8
SESSION_END_HOUR_UTC: int = 17

# --- Loop timings ------------------------------------------------------------
EVALUATION_INTERVAL_SEC: float = 10.0
LADDER_MONITOR_INTERVAL_SEC: float = 1.0
UI_REFRESH_INTERVAL_MS: int = 500
POST_ORDER_CONFIRM_DELAY_SEC: float = 1.0

# --- Ensemble / ML -----------------------------------------------------------
ENSEMBLE_CONSENSUS_THRESHOLD: float = 0.55
ML_PROB_THRESHOLD: float = 0.55
ML_MIN_TRAINED_SAMPLES: int = 20
MIN_TRADES_FOR_DISABLE: int = 8
DISABLE_SCORE_THRESHOLD: float = 0.40
UNDERPERFORM_WINRATE: float = 0.45
# Auto-prune strategies from today's daily report (called by export_daily_report)
AUTO_PRUNE_FROM_REPORT: bool = True
AUTO_PRUNE_MIN_TRADES: int = 5
AUTO_PRUNE_WINRATE: float = 0.30
AUTO_PRUNE_MAX_PNL: float = 0.0   # disable if pnl <= this AND trades >= min

# --- Order execution ---------------------------------------------------------
DEVIATION_POINTS: int = 20
ORDER_RETRY_ATTEMPTS: int = 3

# --- Paper trading -----------------------------------------------------------
PAPER_TRADING: bool = False
PAPER_START_BALANCE: float = 10_000.0

# --- Persistence -------------------------------------------------------------
ML_STORE_DIR: str = "ml_store"
STRATEGY_STATS_FILE: str = "ml_store/strategy_stats.json"
ML_WEIGHTS_FILE: str = "ml_store/ml_weights.json"
JOURNAL_DB: str = "ml_store/journal.sqlite"

# --- Audit / logging / reports -----------------------------------------------
LOG_DIR: str = "logs"
REPORT_DIR: str = "reports"
HISTORY_MAX_POINTS: int = 500
STRATEGY_HISTORY_MAX: int = 50
SKIP_LOG_INTERVAL_SEC: float = 30.0

# --- Pre-start health checks -------------------------------------------------
PRESTART_LOOKBACK_DAYS: int = 5
PRESTART_MIN_WINRATE: float = 0.10
PRESTART_MAX_TICK_AGE_SEC: int = 60

# --- Live override file ------------------------------------------------------
OVERRIDES_FILE: str = "config_overrides.json"

EDITABLE_FIELDS: Dict[str, type] = {
    "SYMBOL": str,
    "PRIMARY_TF": str,
    "FAST_TF": str,
    "RISK_PCT": float,
    "ATR_SL_MULT": float,
    "ATR_TP_MULT": float,
    "MAX_SPREAD_PIPS": float,
    "TP_LEVELS": int,
    "SL_TRAILING_MODE": str,
    "BE_PLUS_R": float,
    "DAILY_LOSS_LIMIT_PCT": float,
    "MAX_CONSECUTIVE_LOSSES": int,
    "COOLDOWN_MINUTES": int,
    "MAX_ORDERS_PER_DAY": int,
    "MAX_CONCURRENT_POSITIONS": int,
    "SESSION_START_HOUR_UTC": int,
    "SESSION_END_HOUR_UTC": int,
    "EVALUATION_INTERVAL_SEC": float,
    "ENSEMBLE_CONSENSUS_THRESHOLD": float,
    "ML_PROB_THRESHOLD": float,
    "ML_MIN_TRAINED_SAMPLES": int,
    "MIN_TRADES_FOR_DISABLE": int,
    "DISABLE_SCORE_THRESHOLD": float,
    "PAPER_TRADING": bool,
    "PRESTART_MIN_WINRATE": float,
}

# Special fields with list values are edited as comma-separated strings.
LIST_FIELDS: Dict[str, type] = {
    "TP_RR_MULTIPLIERS": float,
    "TP_VOLUME_FRACTIONS": float,
}


def snapshot() -> Dict[str, Any]:
    """Return a dict of the current editable config values."""
    g = globals()
    out = {k: g[k] for k in EDITABLE_FIELDS}
    for k in LIST_FIELDS:
        out[k] = ",".join(str(x) for x in g[k])
    return out


def apply_overrides(updates: Dict[str, Any], persist: bool = True) -> Dict[str, Any]:
    """Apply (and optionally persist) overrides for whitelisted fields."""
    g = globals()
    applied: Dict[str, Any] = {}
    for k, v in updates.items():
        if k in EDITABLE_FIELDS:
            t = EDITABLE_FIELDS[k]
            try:
                if t is bool and isinstance(v, str):
                    cv = v.strip().lower() in ("1", "true", "yes", "on")
                else:
                    cv = t(v)
            except (TypeError, ValueError):
                continue
            g[k] = cv
            applied[k] = cv
        elif k in LIST_FIELDS:
            t = LIST_FIELDS[k]
            try:
                if isinstance(v, str):
                    parts = [p.strip() for p in v.split(",") if p.strip()]
                    cv = [t(p) for p in parts]
                else:
                    cv = [t(x) for x in v]
            except (TypeError, ValueError):
                continue
            g[k] = cv
            applied[k] = cv
    if persist and applied:
        _save_overrides_file()
    return applied


def validate_tp_config() -> List[str]:
    """Return list of human-readable issues with the TP ladder config."""
    issues: List[str] = []
    if TP_LEVELS < 1:
        issues.append("TP_LEVELS must be >= 1")
    if len(TP_RR_MULTIPLIERS) < TP_LEVELS:
        issues.append("TP_RR_MULTIPLIERS shorter than TP_LEVELS")
    if len(TP_VOLUME_FRACTIONS) < TP_LEVELS:
        issues.append("TP_VOLUME_FRACTIONS shorter than TP_LEVELS")
    if any(x <= 0 for x in TP_RR_MULTIPLIERS[:TP_LEVELS]):
        issues.append("TP_RR_MULTIPLIERS must all be > 0")
    if any(x <= 0 for x in TP_VOLUME_FRACTIONS[:TP_LEVELS]):
        issues.append("TP_VOLUME_FRACTIONS must all be > 0")
    if sum(TP_VOLUME_FRACTIONS[:TP_LEVELS]) > 1.000001:
        issues.append("TP_VOLUME_FRACTIONS must sum to <= 1.0")
    rr = TP_RR_MULTIPLIERS[:TP_LEVELS]
    if any(rr[i] >= rr[i + 1] for i in range(len(rr) - 1)):
        issues.append("TP_RR_MULTIPLIERS must be strictly increasing")
    return issues


def _save_overrides_file() -> None:
    try:
        with open(OVERRIDES_FILE, "w") as f:
            json.dump(snapshot(), f, indent=2)
    except OSError:
        pass


def _load_overrides_file() -> None:
    if not os.path.exists(OVERRIDES_FILE):
        return
    try:
        with open(OVERRIDES_FILE) as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError):
        return
    apply_overrides(data, persist=False)


_load_overrides_file()
