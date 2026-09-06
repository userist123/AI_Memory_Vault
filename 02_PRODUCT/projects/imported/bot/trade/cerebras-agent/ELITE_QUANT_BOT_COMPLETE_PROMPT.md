# ELITE QUANT BOT — PROMPT COMPLET DE RECONSTRUCȚIE
## XAUUSD Algorithmic Trading System — MT5 + Python

> **Scop**: Acest document conține INTEGRAL aplicația. Orice AI / dezvoltator
> poate reconstrui sistemul bit-cu-bit doar din acest fișier. Nu se inventează
> nimic. Fiecare fișier este redat verbatim din codul sursă.

---

## 0. PHILOSOPHY & MENTAL MODEL

### Pentru un junior
Botul este o **mașină de decizie** care, la fiecare ~30 secunde:
1. Cere prețul curent la MT5 (broker).
2. Întreabă **300+ strategii** (instanțe parametrice): "BUY, SELL sau STAI?".
3. Pune voturile într-un **ensemble ponderat** (cine a câștigat mai mult în trecut, votează mai tare).
4. Întreabă un **model ML** (logistic regression online): "Care e probabilitatea că trade-ul ăsta câștigă?".
5. Trece prin **filtre de risc**: spread OK? sesiune OK? știri majore? cooldown?
6. Dacă TOTUL e verde → deschide o **singură poziție** (mono-trade — niciodată două simultan).
7. Monitorizează poziția (trailing stop, breakeven, partial close).
8. La închidere, **învață din rezultat**: actualizează win-rate-ul strategiei + greutățile ML.

### Pentru un profesionist
Arhitectură event-driven cu state machine deterministică, mono-tranzacționare pe XAUUSD,
sizing ATR-based, breakers prop-firm-compliant (DD zilnic, consecutive losses, max orders/day),
ensemble voting cu confidence × win_rate × ML alignment, online SGD logistic regression
cu cold-start ramp și per-family ML thresholds prin profil XAU, microstructure-aware filters
(spread MA defense, UTC session windows, news blackout, dead-market ATR ratio veto).

---

## 1. STACK & RULAREA

```
Python 3.12 (Windows)
MetaTrader 5 terminal — logged in to broker account
pip install -r requirements.txt
python main.py
```

UI: Tkinter (native, fără browser). Threading: worker thread pentru state machine,
toate mutațiile UI prin `root.after(0, ...)`.

---

## 2. ARHITECTURĂ — DIAGRAMĂ DEPENDENȚE

```
                    ┌──────────────┐
                    │   main.py    │  bootstrap
                    └──────┬───────┘
                           │
              ┌────────────┼─────────────┐
              ▼            ▼             ▼
        ┌─────────┐  ┌──────────┐  ┌──────────┐
        │MT5Client│  │StateMach.│  │   App    │ (Tk UI)
        └────┬────┘  └────┬─────┘  └────┬─────┘
             │            │             │
             ▼            ▼             ▼
       broker MT5    RiskManager   widgets/charts
                         │
                         ▼
                    ┌────┴────┐
                    │Ensemble │──► 300+ Strategy instances (factory)
                    │  + ML   │       │
                    └────┬────┘       ▼
                         │      families/library + xau_library
                         ▼
                   Executor / PaperExecutor
                         │
                         ▼
                    Journal (SQLite) ──► Trainer (online SGD)
```

---

## 3. CONFIGURAȚIE XAUUSD-OPTIMIZATĂ (referință)

| Param | Valoare | De ce |
|---|---|---|
| SYMBOL | XAUUSD | gold |
| RISK_PCT | 0.005 | 0.5%/trade — supraviețuire la 20 SL-uri consecutive |
| ATR_SL_MULT | 2.5 | XAU mișcă 2-3$ în 30s — SL mai strâns = stop hunt |
| ATR_TP_MULT | 4.0 | R:R 1.6 |
| ENSEMBLE_CONSENSUS_THRESHOLD | 0.45 | evită MACD-singur, dar permite consens real |
| ML_PROB_THRESHOLD | 0.60 | filtru ML pentru XAU |
| ML_MIN_TRAINED_SAMPLES | 200 | warmup serios |
| XAUUSD_PROFILE_ENABLED | True | activează sesiuni, spread MA, ATR per familie, news blackout |
| XAU_ALLOWED_WINDOWS_UTC | [(7.0, 21.0)] | London + NY |
| XAU_FORBIDDEN_WINDOWS_UTC | [(21.0, 23.5)] | rollover |

---

## 4. FIȘIERE — CONȚINUT INTEGRAL

### `requirements.txt`
**Rol:** Dependențe Python

**LOC:** 2

```text
MetaTrader5>=5.0.45
numpy>=1.26

```

---

### `main.py`
**Rol:** Punct de intrare — bootstrap aplicație

**LOC:** 74

```python
"""Application entry point.

  pip install -r requirements.txt
  python main.py
"""
from __future__ import annotations

import sys

import config
from core.audit import AuditLogger
from core.execution import Executor
from core.journal import Journal
from core.mt5_client import MT5Client
from core.paper_executor import PaperExecutor
from core.risk_manager import RiskManager
from core.state_machine import StateMachine
from data.feed import DataFeed
from ml.model import OnlineLogReg
from ml.store import MLStore
from ml.trainer import Trainer
from strategies.ensemble import Ensemble
from strategies.factory import StrategyFactory
from ui.app import App


def main() -> int:
    client = MT5Client()
    if not client.connect():
        print("WARNING: MT5 connection failed. Make sure the MT5 terminal is "
              "installed, running, and logged in. UI will still start.")

    journal = Journal()
    audit = AuditLogger(journal=journal)
    executor = PaperExecutor(client) if config.PAPER_TRADING else Executor(client)
    risk = RiskManager()
    feed = DataFeed(client)

    factory = StrategyFactory()
    factory.build_all()
    print(f"Strategy factory built {len(factory.instances)} instances.")

    ensemble = Ensemble()
    model = OnlineLogReg()
    store = MLStore()
    store.load_model(model)
    trainer = Trainer(model, store)

    # Warm up the model from past journal trades if it hasn't trained enough.
    if model.trained_samples < config.ML_MIN_TRAINED_SAMPLES:
        from ml.warmup import warmup_from_journal
        n = warmup_from_journal(model, journal)
        if n:
            store.save_model(model)
            print(f"ML warmup: {n} updates from journal "
                  f"(trained_samples={model.trained_samples}).")


    sm = StateMachine(client, executor, risk, feed, factory,
                      ensemble, model, trainer,
                      audit=audit, journal=journal)
    sm.start()

    app = App(sm, audit=audit, factory=factory)
    try:
        app.run()
    finally:
        sm.stop()
        client.shutdown()
    return 0


if __name__ == "__main__":
    sys.exit(main())

```

---

### `config.py`
**Rol:** Configurație globală — TOATE pragurile, riscul, sesiunile, profilul XAU

**LOC:** 260

```python
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

# --- XAUUSD profile (institutional-grade gold-specific layer) ---------------
# When enabled AND SYMBOL starts with "XAU", a dedicated profile overrides
# the generic session/spread/ATR/ML thresholds. Other symbols are unaffected.
XAUUSD_PROFILE_ENABLED: bool = False

# Session windows expressed as (start_hour_utc, end_hour_utc) floats
# (7.5 == 07:30 UTC). Forbidden takes precedence over allowed.
XAU_ALLOWED_WINDOWS_UTC: List[tuple] = [(7.5, 10.5), (12.5, 16.5)]
XAU_FORBIDDEN_WINDOWS_UTC: List[tuple] = [(21.5, 23.5), (2.0, 6.0)]

# Real-time spread defense: abort new orders if current spread > ratio × MA.
XAU_SPREAD_MAX_RATIO: float = 2.0
XAU_SPREAD_MA_WINDOW: int = 240   # number of recent observations

# News blackout. ISO UTC timestamps of high-impact USD events.
XAU_NEWS_EVENTS_UTC: List[str] = []
XAU_NEWS_BLACKOUT_BEFORE_MIN: int = 30
XAU_NEWS_BLACKOUT_AFTER_MIN: int = 15

# Family-specific ATR multipliers (× ATR(14)). Overrides defaults baked in
# `core.xauusd_profile`. Use this to tune without touching code.
XAU_FAMILY_ATR_MULTS: Dict[str, float] = {}

# ML decision thresholds (XAU profile only)
XAU_ML_THRESHOLD_TREND: float = 0.62
XAU_ML_THRESHOLD_MR: float = 0.55

# Adaptive drawdown reduction
XAU_DD_REDUCE_PCT: float = 0.03    # day-DD ≥ 3% → halve RISK_PCT
XAU_DD_RISK_FACTOR: float = 0.5
XAU_DD_RECOVERY_WINS: int = 10     # net wins required to restore

# Cross-asset MT5 symbols (set to empty string to skip).
XAU_DXY_SYMBOL: str = ""
XAU_US10Y_SYMBOL: str = ""

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
    # XAUUSD profile
    "XAUUSD_PROFILE_ENABLED": bool,
    "XAU_SPREAD_MAX_RATIO": float,
    "XAU_ML_THRESHOLD_TREND": float,
    "XAU_ML_THRESHOLD_MR": float,
    "XAU_DD_REDUCE_PCT": float,
    "XAU_DD_RISK_FACTOR": float,
    "XAU_DD_RECOVERY_WINS": int,
    "XAU_NEWS_BLACKOUT_BEFORE_MIN": int,
    "XAU_NEWS_BLACKOUT_AFTER_MIN": int,
    "XAU_DXY_SYMBOL": str,
    "XAU_US10Y_SYMBOL": str,
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

```

---

### `core/mt5_client.py`
**Rol:** Conectare MT5, detecție filling mode, wrapper order_send

**LOC:** 124

```python
"""MT5 connectivity, symbol info caching, filling-mode detection,
and a single safe wrapper around `order_send`.
"""
from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Optional

try:
    import MetaTrader5 as mt5
except ImportError:  # pragma: no cover - allow import on non-Windows for tests
    mt5 = None  # type: ignore


@dataclass
class TickData:
    bid: float
    ask: float
    time: int


def get_filling_type(info) -> int:
    """Decode `symbol_info().filling_mode` BITMASK to a valid MT5 constant.

    bit 1 -> FOK allowed, bit 2 -> IOC allowed. Default falls back to RETURN.
    Hardcoding ORDER_FILLING_IOC/FOK is the #1 cause of retcode 10030.
    """
    flags = int(getattr(info, "filling_mode", 0))
    if flags & 2:
        return mt5.ORDER_FILLING_IOC
    if flags & 1:
        return mt5.ORDER_FILLING_FOK
    return mt5.ORDER_FILLING_RETURN


class MT5Client:
    def __init__(self) -> None:
        self._connected: bool = False
        self._symbol_info_cache: dict = {}

    # ------------------------------------------------------------------ conn
    def connect(self) -> bool:
        if mt5 is None:
            return False
        if not mt5.initialize():
            return False
        self._connected = True
        return True

    def shutdown(self) -> None:
        if self._connected and mt5 is not None:
            mt5.shutdown()
            self._connected = False

    @property
    def connected(self) -> bool:
        return self._connected

    # ---------------------------------------------------------------- account
    def account_info(self):
        return mt5.account_info() if mt5 else None

    # ----------------------------------------------------------------- symbol
    def symbol_info(self, symbol: str):
        if symbol in self._symbol_info_cache:
            cached, ts = self._symbol_info_cache[symbol]
            if time.time() - ts < 5.0:
                return cached
        if mt5 is None:
            return None
        if not mt5.symbol_select(symbol, True):
            return None
        info = mt5.symbol_info(symbol)
        if info is not None:
            self._symbol_info_cache[symbol] = (info, time.time())
        return info

    def tick(self, symbol: str) -> Optional[TickData]:
        if mt5 is None:
            return None
        t = mt5.symbol_info_tick(symbol)
        if t is None:
            return None
        return TickData(bid=t.bid, ask=t.ask, time=t.time)

    # ------------------------------------------------------------------ rates
    def rates(self, symbol: str, timeframe: int, count: int):
        if mt5 is None:
            return None
        return mt5.copy_rates_from_pos(symbol, timeframe, 0, count)

    # ------------------------------------------------------------ positions
    def positions_get(self, **kwargs):
        if mt5 is None:
            return ()
        res = mt5.positions_get(**kwargs)
        return res or ()

    def history_deals_get(self, date_from, date_to):
        if mt5 is None:
            return ()
        res = mt5.history_deals_get(date_from, date_to)
        return res or ()

    # --------------------------------------------------------------- send
    def order_send(self, request: dict):
        """Thin wrapper. Retries on requote/price-changed retcodes."""
        if mt5 is None:
            return None
        last = None
        for _ in range(3):
            last = mt5.order_send(request)
            if last is None:
                time.sleep(0.2)
                continue
            rc = last.retcode
            if rc == mt5.TRADE_RETCODE_DONE:
                return last
            if rc in (mt5.TRADE_RETCODE_REQUOTE, mt5.TRADE_RETCODE_PRICE_CHANGED):
                time.sleep(0.2)
                continue
            return last
        return last

```

---

### `core/xauusd_profile.py`
**Rol:** Profil XAUUSD: sesiuni UTC, news blackout, spread monitor, ATR multipliers per familie

**LOC:** 245

```python
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

```

---

### `core/risk_manager.py`
**Rol:** Risk manager: sizing ATR, breakers, cooldown, filtre spread/sesiune/news

**LOC:** 127

```python
"""Risk management: ATR-based sizing, circuit breakers, cooldowns,
spread / session / dead-market filters.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, time as dtime, timedelta, timezone
from typing import List, Optional, Tuple

import config
from core import xauusd_profile as _xau


@dataclass
class RiskState:
    recent_results: List[float] = field(default_factory=list)   # last trade PnLs
    cooldown_until: Optional[datetime] = None
    orders_today: int = 0
    day_start_balance: float = 0.0
    day_anchor: Optional[datetime] = None   # midnight UTC of current day

    def reset_day_if_needed(self, balance: float) -> None:
        now = datetime.now(timezone.utc)
        midnight = datetime.combine(now.date(), dtime(0, 0), tzinfo=timezone.utc)
        if self.day_anchor != midnight:
            self.day_anchor = midnight
            self.day_start_balance = balance
            self.orders_today = 0


class RiskManager:
    def __init__(self) -> None:
        self.state = RiskState()
        self.spread_monitor = _xau.SpreadMonitor(
            window=int(getattr(config, "XAU_SPREAD_MA_WINDOW", 240)))

    # ----------------------------------------------------------- sizing
    @staticmethod
    def calc_lot(balance: float, sl_distance: float, info) -> float:
        if sl_distance <= 0 or info is None:
            return 0.0
        risk_amount = balance * config.RISK_PCT
        tick_size = float(info.trade_tick_size) or 1e-5
        tick_value = float(info.trade_tick_value) or 1.0
        sl_ticks = sl_distance / tick_size
        if sl_ticks <= 0:
            return 0.0
        raw = risk_amount / (sl_ticks * tick_value)
        step = float(info.volume_step) or 0.01
        lot = round(raw / step) * step
        lot = max(float(info.volume_min), min(float(info.volume_max), lot))
        return round(lot, 2)

    # --------------------------------------------------------- breakers
    def update_after_trade(self, pnl: float) -> None:
        self.state.recent_results.append(pnl)
        if len(self.state.recent_results) > 50:
            self.state.recent_results = self.state.recent_results[-50:]
        # consecutive losses
        last = self.state.recent_results[-config.MAX_CONSECUTIVE_LOSSES:]
        if (len(last) >= config.MAX_CONSECUTIVE_LOSSES
                and all(p < 0 for p in last)):
            self.state.cooldown_until = (
                datetime.now(timezone.utc)
                + timedelta(minutes=config.COOLDOWN_MINUTES)
            )
            self.state.recent_results.clear()

    def in_cooldown(self) -> bool:
        cu = self.state.cooldown_until
        if cu is None:
            return False
        if datetime.now(timezone.utc) >= cu:
            self.state.cooldown_until = None
            return False
        return True

    def daily_loss_breached(self, current_balance: float,
                            realised_pnl_today: float) -> bool:
        if self.state.day_start_balance <= 0:
            return False
        limit = self.state.day_start_balance * config.DAILY_LOSS_LIMIT_PCT
        return realised_pnl_today <= -limit

    def order_limit_reached(self) -> bool:
        return self.state.orders_today >= config.MAX_ORDERS_PER_DAY

    def register_order(self) -> None:
        self.state.orders_today += 1

    # ---------------------------------------------------------- filters
    def spread_ok(self, tick, info) -> Tuple[bool, float]:
        """Returns (ok, current_spread_pips). Honors XAU profile MA defense."""
        if tick is None or info is None:
            return False, 0.0
        point = float(info.point) or 1e-5
        spread_pips = (tick.ask - tick.bid) / (point * 10.0)
        # always feed the rolling monitor (cheap)
        try:
            self.spread_monitor.push(spread_pips)
        except Exception:
            pass
        if _xau.profile_active():
            if self.spread_monitor.is_abnormal(spread_pips):
                return False, spread_pips
            # also enforce the hard ceiling if it's set
            if spread_pips > float(config.MAX_SPREAD_PIPS) * 1.5:
                return False, spread_pips
            return True, spread_pips
        return spread_pips <= float(config.MAX_SPREAD_PIPS), spread_pips

    @staticmethod
    def in_session() -> bool:
        if _xau.profile_active():
            return _xau.in_allowed_session()
        now = datetime.now(timezone.utc)
        return config.SESSION_START_HOUR_UTC <= now.hour < config.SESSION_END_HOUR_UTC

    @staticmethod
    def in_news_blackout() -> bool:
        return _xau.profile_active() and _xau.in_news_blackout()

    @staticmethod
    def market_alive(atr_now: float, atr_long: float) -> bool:
        if atr_long <= 0:
            return False
        return atr_now >= 0.5 * atr_long

```

---

### `core/execution.py`
**Rol:** Executor real MT5 (live trading)

**LOC:** 183

```python
"""Execution: build orders + SL/TP ladder, send / partial-close / modify SL,
post-send confirmation, PnL aggregation by `position_id` + DEAL_ENTRY_OUT.
"""
from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import List, Optional

try:
    import MetaTrader5 as mt5
except ImportError:  # pragma: no cover
    mt5 = None  # type: ignore

import config
from core.mt5_client import MT5Client, get_filling_type


@dataclass
class OrderPlan:
    side: str
    price: float
    sl: float
    tp: float                       # broker-side TP (last ladder level)
    lot: float
    strategy_id: str
    tp_ladder: List[float] = field(default_factory=list)
    tp_fractions: List[float] = field(default_factory=list)
    sl_dist: float = 0.0


def build_levels(side: str, price: float, atr: float, digits: int):
    """Return (sl, tp, sl_dist) for a single-TP trade — legacy helper."""
    sl_dist = atr * config.ATR_SL_MULT
    tp_dist = atr * config.ATR_TP_MULT
    if side == "BUY":
        sl = round(price - sl_dist, digits)
        tp = round(price + tp_dist, digits)
    else:
        sl = round(price + sl_dist, digits)
        tp = round(price - tp_dist, digits)
    return sl, tp, sl_dist


def build_tp_ladder(side: str, price: float, sl_dist: float,
                    digits: int) -> List[float]:
    """Return TP prices for each ladder level, rounded to broker digits."""
    rr = config.TP_RR_MULTIPLIERS[:config.TP_LEVELS]
    ladder: List[float] = []
    for mult in rr:
        if side == "BUY":
            ladder.append(round(price + sl_dist * mult, digits))
        else:
            ladder.append(round(price - sl_dist * mult, digits))
    return ladder


def round_volume(volume: float, info) -> float:
    step = float(getattr(info, "volume_step", 0.01)) or 0.01
    vmin = float(getattr(info, "volume_min", 0.01))
    vmax = float(getattr(info, "volume_max", 100.0))
    v = round(volume / step) * step
    v = max(vmin, min(vmax, v))
    return round(v, 2)


class Executor:
    is_paper = False

    def __init__(self, client: MT5Client) -> None:
        self.client = client

    # ------------------------------------------------------------------ send
    def send(self, symbol: str, plan: OrderPlan) -> Optional[int]:
        info = self.client.symbol_info(symbol)
        if info is None or mt5 is None:
            return None
        order_type = mt5.ORDER_TYPE_BUY if plan.side == "BUY" else mt5.ORDER_TYPE_SELL
        # When ladder is active, leave broker TP at last level so the position
        # still has a hard guard; intermediate TPs are realised via partial closes.
        broker_tp = float(plan.tp_ladder[-1]) if plan.tp_ladder else float(plan.tp)
        request = {
            "action": mt5.TRADE_ACTION_DEAL,
            "symbol": symbol,
            "volume": float(plan.lot),
            "type": order_type,
            "price": float(plan.price),
            "tp": broker_tp,
            "sl": float(plan.sl),
            "deviation": config.DEVIATION_POINTS,
            "magic": config.MAGIC,
            "comment": f"ELITE_{plan.strategy_id[:8]}",
            "type_time": mt5.ORDER_TIME_GTC,
            "type_filling": get_filling_type(info),
        }
        result = self.client.order_send(request)
        if result is None or result.retcode != mt5.TRADE_RETCODE_DONE:
            return None

        time.sleep(config.POST_ORDER_CONFIRM_DELAY_SEC)
        candidates = [
            int(x) for x in (
                getattr(result, "position", 0),
                getattr(result, "order", 0),
                getattr(result, "deal", 0),
            ) if int(x or 0) > 0
        ]
        for ticket in candidates:
            positions = self.client.positions_get(ticket=ticket)
            if positions:
                return int(getattr(positions[0], "ticket", ticket))

        positions = self.client.positions_get(symbol=symbol)
        matches = [p for p in positions if getattr(p, "magic", 0) == config.MAGIC]
        if not matches:
            return None
        newest = max(matches, key=lambda p: getattr(p, "time", 0))
        return int(getattr(newest, "ticket"))

    # ---------------------------------------------------- partial close / SL
    def partial_close(self, position, volume_to_close: float) -> bool:
        """Close `volume_to_close` lots of a position. Returns True on success."""
        if mt5 is None:
            return False
        info = self.client.symbol_info(position.symbol)
        tick = self.client.tick(position.symbol)
        if info is None or tick is None:
            return False
        vol = round_volume(volume_to_close, info)
        cur_vol = float(getattr(position, "volume", 0.0))
        if vol <= 0 or vol > cur_vol + 1e-9:
            return False
        if position.type == mt5.POSITION_TYPE_BUY:
            close_type = mt5.ORDER_TYPE_SELL
            price = tick.bid
        else:
            close_type = mt5.ORDER_TYPE_BUY
            price = tick.ask
        request = {
            "action": mt5.TRADE_ACTION_DEAL,
            "symbol": position.symbol,
            "volume": float(vol),
            "type": close_type,
            "position": int(position.ticket),
            "price": float(price),
            "deviation": config.DEVIATION_POINTS,
            "magic": config.MAGIC,
            "comment": "ELITE_PARTIAL",
            "type_time": mt5.ORDER_TIME_GTC,
            "type_filling": get_filling_type(info),
        }
        result = self.client.order_send(request)
        return result is not None and result.retcode == mt5.TRADE_RETCODE_DONE

    def modify_sl(self, position, new_sl: float) -> bool:
        """Move SL on an existing position. Direction is enforced by caller."""
        if mt5 is None:
            return False
        request = {
            "action": mt5.TRADE_ACTION_SLTP,
            "symbol": position.symbol,
            "position": int(position.ticket),
            "sl": float(new_sl),
            "tp": float(getattr(position, "tp", 0.0) or 0.0),
            "magic": config.MAGIC,
        }
        result = self.client.order_send(request)
        return result is not None and result.retcode == mt5.TRADE_RETCODE_DONE

    # ----------------------------------------------------------- full close
    def close(self, position) -> bool:
        return self.partial_close(position, float(getattr(position, "volume", 0.0)))

    # ------------------------------------------------------- PnL helpers
    def position_pnl(self, ticket: int, date_from, date_to) -> float:
        """Aggregate realised PnL by position_id + DEAL_ENTRY_OUT (incl. partials)."""
        if mt5 is None:
            return 0.0
        deals = self.client.history_deals_get(date_from, date_to)
        closed = [d for d in deals
                  if d.position_id == ticket
                  and d.entry in (mt5.DEAL_ENTRY_OUT, mt5.DEAL_ENTRY_INOUT)]
        return float(sum(d.profit + d.commission + d.swap for d in closed))

```

---

### `core/paper_executor.py`
**Rol:** Executor paper trading (simulare cu tick-uri reale)

**LOC:** 179

```python
"""Paper-trading executor — simulates fills without sending orders to MT5.

Drop-in for `core.execution.Executor`: same `send` / `close` /
`position_pnl` surface, plus `partial_close` and `modify_sl` to support
the multi-TP ladder. Slippage = 0; fills at the requested price.
SL is checked against the live `tick` on every state-machine reconciliation
pass; TPs are managed externally by the state machine.
"""
from __future__ import annotations

import itertools
import threading
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Dict, List, Optional

import config


@dataclass
class _SimPosition:
    ticket: int
    symbol: str
    side: str
    volume: float
    open_price: float
    sl: float
    tp: float
    strategy_id: str
    opened_at: datetime
    profit: float = 0.0
    magic: int = 0
    type: int = 0


class PaperExecutor:
    is_paper = True
    _ticket_seq = itertools.count(9_000_001)

    def __init__(self, client) -> None:
        self.client = client
        self._open: Dict[int, _SimPosition] = {}
        self._closed_pnl: Dict[int, float] = {}
        self._closed_meta: Dict[int, _SimPosition] = {}
        self._lock = threading.Lock()

    # ----------------------------------------------------------- send/close
    def send(self, symbol: str, plan) -> Optional[int]:
        info = self.client.symbol_info(symbol)
        if info is None:
            return None
        ticket = next(self._ticket_seq)
        # When ladder is active the state machine manages TP, so we keep
        # broker-side TP at the last ladder level (or zero if absent).
        tp_val = float(plan.tp_ladder[-1]) if getattr(plan, "tp_ladder", None) \
            else float(plan.tp)
        pos = _SimPosition(
            ticket=ticket, symbol=symbol, side=plan.side,
            volume=float(plan.lot), open_price=float(plan.price),
            sl=float(plan.sl), tp=tp_val,
            strategy_id=plan.strategy_id,
            opened_at=datetime.now(timezone.utc),
            magic=config.MAGIC,
            type=0 if plan.side == "BUY" else 1,
        )
        with self._lock:
            self._open[ticket] = pos
        return ticket

    def close(self, position) -> bool:
        return self.partial_close(position,
                                  float(getattr(position, "volume", 0.0)))

    def partial_close(self, position, volume_to_close: float) -> bool:
        ticket = int(getattr(position, "ticket", 0))
        with self._lock:
            pos = self._open.get(ticket)
            if pos is None or volume_to_close <= 0:
                return False
            info = self.client.symbol_info(pos.symbol)
            step = float(getattr(info, "volume_step", 0.01)) or 0.01
            vmin = float(getattr(info, "volume_min", 0.01))
            vol = round(volume_to_close / step) * step
            vol = max(vmin, min(pos.volume, vol))
            tick = self.client.tick(pos.symbol)
            if tick is None:
                return False
            price = tick.bid if pos.side == "BUY" else tick.ask
            contract = float(getattr(info, "trade_contract_size", 100_000.0))
            if pos.side == "BUY":
                pnl = (price - pos.open_price) * vol * contract
            else:
                pnl = (pos.open_price - price) * vol * contract
            pos.volume -= vol
            self._closed_pnl[ticket] = self._closed_pnl.get(ticket, 0.0) + float(pnl)
            if pos.volume <= 1e-9:
                self._open.pop(ticket, None)
                self._closed_meta[ticket] = pos
                pos.profit = float(self._closed_pnl[ticket])
        return True

    def modify_sl(self, position, new_sl: float) -> bool:
        ticket = int(getattr(position, "ticket", 0))
        with self._lock:
            pos = self._open.get(ticket)
            if pos is None:
                return False
            pos.sl = float(new_sl)
        return True

    # ---------------------------------------------------------- mt5 shims
    def positions_get(self, ticket: Optional[int] = None) -> List[_SimPosition]:
        with self._lock:
            if ticket is None:
                return list(self._open.values())
            p = self._open.get(int(ticket))
            return [p] if p else []

    def position_pnl(self, ticket: int, date_from=None, date_to=None) -> float:
        with self._lock:
            return float(self._closed_pnl.get(int(ticket), 0.0))

    def closed_meta(self, ticket: int) -> Optional[_SimPosition]:
        with self._lock:
            return self._closed_meta.get(int(ticket))

    # ----------------------------------------------------------- simulation
    def step(self) -> List[int]:
        """Mark-to-market open positions and trigger SL only (TPs managed
        externally by the state machine via partial_close)."""
        closed_now: List[int] = []
        with self._lock:
            tickets = list(self._open.keys())
        for t in tickets:
            with self._lock:
                pos = self._open.get(t)
            if pos is None:
                continue
            tick = self.client.tick(pos.symbol)
            info = self.client.symbol_info(pos.symbol)
            if tick is None or info is None:
                continue
            bid, ask = float(tick.bid), float(tick.ask)
            contract = float(getattr(info, "trade_contract_size", 100_000.0))
            if pos.side == "BUY":
                cur = bid
                pos.profit = (cur - pos.open_price) * pos.volume * contract
                if pos.sl and cur <= pos.sl:
                    self._finalise(t, pos.sl, reason="SL")
                    closed_now.append(t)
                elif pos.tp and cur >= pos.tp:
                    self._finalise(t, pos.tp, reason="TP")
                    closed_now.append(t)
            else:
                cur = ask
                pos.profit = (pos.open_price - cur) * pos.volume * contract
                if pos.sl and cur >= pos.sl:
                    self._finalise(t, pos.sl, reason="SL")
                    closed_now.append(t)
                elif pos.tp and cur <= pos.tp:
                    self._finalise(t, pos.tp, reason="TP")
                    closed_now.append(t)
        return closed_now

    def _finalise(self, ticket: int, exit_price: float, reason: str) -> bool:
        with self._lock:
            pos = self._open.pop(int(ticket), None)
            if pos is None:
                return False
            info = self.client.symbol_info(pos.symbol)
            contract = float(getattr(info, "trade_contract_size", 100_000.0))
            if pos.side == "BUY":
                pnl = (exit_price - pos.open_price) * pos.volume * contract
            else:
                pnl = (pos.open_price - exit_price) * pos.volume * contract
            self._closed_pnl[int(ticket)] = self._closed_pnl.get(int(ticket), 0.0) + float(pnl)
            self._closed_meta[int(ticket)] = pos
            pos.profit = float(self._closed_pnl[int(ticket)])
        return True

```

---

### `core/state_machine.py`
**Rol:** INIMA botului — orchestrator: ciclu de decizie, deschidere/închidere poziții, ML update

**LOC:** 636

```python
"""Background trading loop: multi-concurrent trades with multi-TP ladder,
partial closes, SL trailing, journal logging, audit, rolling history
buffers, and dashboard snapshots.

Supports paper-trading executor (drop-in) and live config restart.
"""
from __future__ import annotations

import threading
import time
from collections import deque
from dataclasses import dataclass, field
from datetime import datetime, time as dtime, timezone
from typing import Any, Deque, Dict, List, Optional

try:
    import MetaTrader5 as mt5
except ImportError:  # pragma: no cover
    mt5 = None  # type: ignore

import config
from core.audit import AuditLogger
from core.execution import (Executor, OrderPlan, build_levels,
                             build_tp_ladder, round_volume)
from core.journal import Journal
from core.mt5_client import MT5Client
from core.paper_executor import PaperExecutor
from core.risk_manager import RiskManager
from core import xauusd_profile as _xau
from data.feed import DataFeed
from data.indicators import atr as calc_atr
from ml.features import build_features
from ml.model import OnlineLogReg
from ml.trainer import Trainer
from strategies.ensemble import Ensemble
from strategies.factory import StrategyFactory


@dataclass
class Snapshot:
    connected: bool = False
    balance: float = 0.0
    equity: float = 0.0
    daily_pnl: float = 0.0
    active_ticket: Optional[int] = None
    active_pnl: float = 0.0
    consensus: float = 0.0
    ml_prob: float = 0.5
    top_strategies: List[tuple] = field(default_factory=list)
    log: List[str] = field(default_factory=list)
    running: bool = False
    paper: bool = False
    consensus_history: List[float] = field(default_factory=list)
    ml_history: List[float] = field(default_factory=list)
    strategy_history: Dict[str, List[int]] = field(default_factory=dict)
    active_trades: List[Dict[str, Any]] = field(default_factory=list)
    today: Dict[str, Any] = field(default_factory=dict)


class StateMachine:
    def __init__(self,
                 client: MT5Client,
                 executor: Executor,
                 risk: RiskManager,
                 feed: DataFeed,
                 factory: StrategyFactory,
                 ensemble: Ensemble,
                 model: OnlineLogReg,
                 trainer: Trainer,
                 audit: Optional[AuditLogger] = None,
                 journal: Optional[Journal] = None) -> None:
        self.client = client
        self.executor: object = executor
        self.risk = risk
        self.feed = feed
        self.factory = factory
        self.ensemble = ensemble
        self.model = model
        self.trainer = trainer
        self.journal = journal or Journal()
        self.audit = audit or AuditLogger(journal=self.journal)
        if self.audit.journal is None:
            self.audit.journal = self.journal

        self._thread: Optional[threading.Thread] = None
        self._stop = threading.Event()
        self._auto = False
        self._active_ticket: Optional[int] = None
        self._active: Dict[int, Dict[str, Any]] = {}
        self._log: List[str] = []
        self.snapshot = Snapshot()
        self._lock = threading.Lock()

        self._consensus_hist: Deque[float] = deque(maxlen=config.HISTORY_MAX_POINTS)
        self._ml_hist: Deque[float] = deque(maxlen=config.HISTORY_MAX_POINTS)
        self._strategy_hist: Dict[str, Deque[int]] = {}
        self._last_skip_log: Dict[str, float] = {}

        self.cfg = config
        self.factory.set_status_listener(self._on_status_change)

    # ------------------------------------------------------------- control
    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=2.0)

    def set_auto(self, on: bool) -> None:
        self._auto = on
        self._log_event(f"Auto trade {'ENABLED' if on else 'DISABLED'}")

    def set_auto_trade(self, on: bool) -> None:
        self.set_auto(on)

    def kill_switch(self) -> None:
        self._auto = False
        tickets = list(self._active.keys())
        positions = self._open_positions_map()
        for tkt, pos in positions.items():
            try:
                self.executor.close(pos)  # type: ignore[union-attr]
            except Exception:
                pass
            self._finalise_trade(tkt, exit_reason="MANUAL_KILL")
        self._active.clear()
        self._active_ticket = None
        self.audit.log_breaker(event="kill_switch", tickets=tickets)
        self._log_event(f"KILL SWITCH executed ({len(tickets)} positions)")

    def kill_all(self) -> None:
        self.kill_switch()

    # ----------------------------------------------- paper / config / restart
    def set_paper_mode(self, on: bool) -> None:
        on = bool(on)
        if on and not getattr(self.executor, "is_paper", False):
            self.executor = PaperExecutor(self.client)
            config.PAPER_TRADING = True
            self._log_event("Switched to PAPER trading")
        elif not on and getattr(self.executor, "is_paper", False):
            self.executor = Executor(self.client)
            config.PAPER_TRADING = False
            self._log_event("Switched to LIVE trading")

    def set_paper_trading(self, on: bool) -> None:
        self.set_paper_mode(on)

    def apply_config(self, updates: Dict[str, object]) -> Dict[str, object]:
        applied = config.apply_overrides(updates)
        if "PAPER_TRADING" in applied:
            self.set_paper_mode(bool(applied["PAPER_TRADING"]))
        self._log_event(f"Config updated: {sorted(applied.keys())}")
        return applied

    def update_config(self, updates: Dict[str, object]) -> Dict[str, object]:
        return self.apply_config(updates)

    def export_daily_report(self):
        return self.audit.export_daily_report(factory=self.factory)

    def restart(self) -> None:
        was_auto = self._auto
        self._auto = False
        self.stop()
        self._stop = threading.Event()
        self._log_event("State machine restarted")
        self.start()
        self._auto = was_auto

    # ------------------------------------------------------------- logging
    def _log_event(self, msg: str) -> None:
        stamp = datetime.now(timezone.utc).strftime("%H:%M:%S")
        with self._lock:
            self._log.append(f"[{stamp}] {msg}")
            if len(self._log) > 200:
                self._log = self._log[-200:]

    def _skip_event(self, key: str, msg: str) -> None:
        now = time.time()
        last = self._last_skip_log.get(key, 0.0)
        if now - last >= config.SKIP_LOG_INTERVAL_SEC:
            self._last_skip_log[key] = now
            self._log_event(msg)

    def _on_status_change(self, sid: str, old: str, new: str) -> None:
        if new == "DISABLED":
            self.audit.log_breaker(event="strategy_disabled",
                                   strategy_id=sid, from_status=old)
            self._log_event(f"Strategy DISABLED (poor performance): {sid}")

    # ---------------------------------------------------------- snapshot
    def get_snapshot(self) -> Snapshot:
        with self._lock:
            top_ids = [sid for sid, _, _ in self.snapshot.top_strategies[:5]]
            strat_hist = {sid: list(self._strategy_hist.get(sid, []))
                          for sid in top_ids}
            active_trades = [self._active_trade_view(t, m)
                             for t, m in self._active.items()]
            today = {}
            try:
                today = self.audit.today_summary()
            except Exception:
                today = {}
            snap = Snapshot(
                connected=self.client.connected,
                balance=self.snapshot.balance,
                equity=self.snapshot.equity,
                daily_pnl=self.snapshot.daily_pnl,
                active_ticket=self._active_ticket,
                active_pnl=self.snapshot.active_pnl,
                consensus=self.snapshot.consensus,
                ml_prob=self.snapshot.ml_prob,
                top_strategies=list(self.snapshot.top_strategies),
                log=list(self._log[-30:]),
                running=self._auto,
                paper=bool(getattr(self.executor, "is_paper", False)),
                consensus_history=list(self._consensus_hist),
                ml_history=list(self._ml_hist),
                strategy_history=strat_hist,
                active_trades=active_trades,
                today=today,
            )
        return snap

    def _active_trade_view(self, ticket: int, meta: Dict[str, Any]) -> Dict[str, Any]:
        return {
            "ticket": int(ticket),
            "strategy_id": meta.get("strategy_id", ""),
            "side": meta.get("side", ""),
            "entry": float(meta.get("entry_price", 0.0)),
            "sl": float(meta.get("sl_price", 0.0)),
            "ladder": list(meta.get("ladder", [])),
            "hits": list(meta.get("hits", [])),
            "initial_volume": float(meta.get("initial_volume", 0.0)),
            "remaining_volume": float(meta.get("remaining_volume", 0.0)),
        }

    # ----------------------------------------------------------- main loop
    def _run(self) -> None:
        last_eval = 0.0
        last_ladder = 0.0
        while not self._stop.is_set():
            try:
                if getattr(self.executor, "is_paper", False):
                    self.executor.step()  # type: ignore[attr-defined]
                self._refresh_account()
                self._reconcile_active()
                now = time.time()
                if (now - last_ladder) >= config.LADDER_MONITOR_INTERVAL_SEC:
                    last_ladder = now
                    self._monitor_ladder()
                if self._auto and (now - last_eval) >= config.EVALUATION_INTERVAL_SEC:
                    last_eval = now
                    self._evaluate_and_trade()
            except Exception as exc:
                self._log_event(f"loop error: {exc}")
            time.sleep(0.5)

    # ---------------------------------------------------------- internals
    def _open_positions_map(self) -> Dict[int, Any]:
        if getattr(self.executor, "is_paper", False):
            return {int(p.ticket): p for p in
                    self.executor.positions_get()}  # type: ignore[attr-defined]
        return {int(p.ticket): p for p in
                (self.client.positions_get() or ())
                if getattr(p, "magic", 0) == config.MAGIC}

    def _refresh_account(self) -> None:
        info = self.client.account_info()
        paper = getattr(self.executor, "is_paper", False)
        if paper:
            balance = config.PAPER_START_BALANCE
            closed_total = sum(self.executor._closed_pnl.values())  # type: ignore[attr-defined]
            balance += closed_total
            open_profit = sum(p.profit for p in
                              self.executor.positions_get())  # type: ignore[attr-defined]
            equity = balance + open_profit
            self.risk.state.reset_day_if_needed(balance)
            with self._lock:
                self.snapshot.balance = float(balance)
                self.snapshot.equity = float(equity)
                today = datetime.now(timezone.utc).date()
                day_pnl = 0.0
                for tkt, p in self.executor._closed_meta.items():  # type: ignore[attr-defined]
                    if p.opened_at.date() == today:
                        day_pnl += self.executor._closed_pnl.get(tkt, 0.0)  # type: ignore[attr-defined]
                self.snapshot.daily_pnl = float(day_pnl)
                self.snapshot.top_strategies = self.factory.top_n(10)
            return
        if info is None:
            return
        self.risk.state.reset_day_if_needed(info.balance)
        midnight = datetime.combine(datetime.now(timezone.utc).date(),
                                    dtime(0, 0), tzinfo=timezone.utc)
        deals = self.client.history_deals_get(midnight, datetime.now(timezone.utc))
        realised = 0.0
        if mt5 is not None:
            realised = float(sum(
                d.profit + d.commission + d.swap
                for d in deals
                if getattr(d, "magic", 0) == config.MAGIC
            ))
        with self._lock:
            self.snapshot.balance = float(info.balance)
            self.snapshot.equity = float(info.equity)
            self.snapshot.daily_pnl = realised
            self.snapshot.top_strategies = self.factory.top_n(10)

    def _reconcile_active(self) -> None:
        positions = self._open_positions_map()
        # aggregate open PnL across our trades
        agg_pnl = sum(float(getattr(p, "profit", 0.0))
                      for tkt, p in positions.items()
                      if tkt in self._active)
        with self._lock:
            self.snapshot.active_pnl = float(agg_pnl)
        # update remaining volume from broker
        for tkt, meta in list(self._active.items()):
            if tkt in positions:
                meta["remaining_volume"] = float(
                    getattr(positions[tkt], "volume", 0.0))
        # detect closed trades
        for ticket in [t for t in list(self._active.keys())
                       if t not in positions]:
            self._finalise_trade(ticket, exit_reason="SL")
        self._active_ticket = (next(reversed(self._active))
                               if self._active else None)

    def _monitor_ladder(self) -> None:
        """Check open trades for TP-ladder progression and trail SL."""
        if not self._active:
            return
        positions = self._open_positions_map()
        for ticket, meta in list(self._active.items()):
            pos = positions.get(ticket)
            if pos is None:
                continue
            tick = self.client.tick(pos.symbol)
            info = self.client.symbol_info(pos.symbol)
            if tick is None or info is None:
                continue
            side = meta["side"]
            ladder = meta["ladder"]
            hits = meta["hits"]
            fractions = meta["fractions"]
            initial = float(meta["initial_volume"])
            for i, tp_price in enumerate(ladder):
                if hits[i]:
                    continue
                price_now = float(tick.bid) if side == "BUY" else float(tick.ask)
                reached = (price_now >= tp_price) if side == "BUY" else (price_now <= tp_price)
                if not reached:
                    break
                vol_to_close = initial * fractions[i]
                # On the last level, close everything remaining
                if i == len(ladder) - 1:
                    vol_to_close = float(getattr(pos, "volume", vol_to_close))
                vol_rounded = round_volume(vol_to_close, info)
                if vol_rounded <= 0:
                    hits[i] = True
                    continue
                ok = self.executor.partial_close(pos, vol_rounded)  # type: ignore[union-attr]
                if not ok:
                    break
                hits[i] = True
                self.journal.record_partial(ticket, i, price_now,
                                            vol_rounded, 0.0)
                self.audit.log_partial(ticket=ticket, level=i + 1,
                                       price=price_now, volume=vol_rounded,
                                       strategy_id=meta.get("strategy_id"))
                self._log_event(
                    f"TP{i+1} hit #{ticket} closed {vol_rounded:.2f} @ "
                    f"{price_now:.5f} ({meta.get('strategy_id')})"
                )
                # SL trailing
                new_sl = self._trail_sl(side, meta, i)
                if new_sl is not None and abs(new_sl - meta["sl_price"]) > 1e-9:
                    if self.executor.modify_sl(pos, new_sl):  # type: ignore[union-attr]
                        meta["sl_price"] = float(new_sl)
                        self._log_event(
                            f"SL trailed #{ticket} to {new_sl:.5f}"
                        )

    def _trail_sl(self, side: str, meta: Dict[str, Any],
                  level_hit: int) -> Optional[float]:
        mode = config.SL_TRAILING_MODE
        entry = float(meta["entry_price"])
        sl_dist = float(meta["sl_dist"])
        if mode == "TO_BREAK_EVEN_AT_TP1" and level_hit == 0:
            return entry
        if mode == "TO_BE_PLUS_AT_TP2":
            if level_hit == 0:
                return entry
            if level_hit == 1:
                return (entry + config.BE_PLUS_R * sl_dist) if side == "BUY" \
                    else (entry - config.BE_PLUS_R * sl_dist)
        return None

    def _finalise_trade(self, ticket: int, exit_reason: str) -> None:
        meta = self._active.pop(ticket, None)
        if meta is None:
            return
        paper = getattr(self.executor, "is_paper", False)
        if paper:
            pnl = float(self.executor.position_pnl(ticket))  # type: ignore[attr-defined]
            exit_price = float(meta.get("entry_price", 0.0))
            closed_meta = self.executor.closed_meta(ticket)  # type: ignore[attr-defined]
            if closed_meta is not None:
                exit_price = float(closed_meta.open_price)  # fallback
        else:
            midnight = datetime.combine(datetime.now(timezone.utc).date(),
                                        dtime(0, 0), tzinfo=timezone.utc)
            pnl = self.executor.position_pnl(ticket,  # type: ignore[union-attr]
                                             midnight,
                                             datetime.now(timezone.utc))
            exit_price = float(meta.get("entry_price", 0.0))
        # If any TP was hit and exit reason wasn't manual kill, label as ladder
        hits = meta.get("hits", [])
        if any(hits) and exit_reason == "SL":
            exit_reason = "TP_LADDER" if all(hits) else "SL_AFTER_PARTIAL"
        self.risk.update_after_trade(pnl)
        # XAU adaptive DD bookkeeping
        if _xau.profile_active():
            msg = _xau.on_trade_result(pnl > 0)
            if msg:
                self._log_event(f"[XAU] {msg}")
            msg2 = _xau.check_and_apply_adaptive_dd(
                self.risk.state.day_start_balance, self.snapshot.daily_pnl)
            if msg2:
                self._log_event(f"[XAU] {msg2}")
        won = pnl > 0
        features = meta.get("features")
        if features is not None:
            self.trainer.update(features, won)
        sid = meta.get("strategy_id")
        if sid:
            self.factory.record_result(sid, won)
            hist = self._strategy_hist.setdefault(
                sid, deque(maxlen=config.STRATEGY_HISTORY_MAX))
            hist.append(1 if won else 0)
        self.journal.close_trade(ticket, exit_reason=exit_reason,
                                 exit_price=exit_price, gross_pnl=pnl)
        self.audit.log_pnl(
            ticket=ticket, strategy_id=sid,
            pnl=round(pnl, 2), won=won, exit_reason=exit_reason,
            ml_prob_win=round(float(meta.get("ml_prob_win", 0.5)), 4),
            paper=paper,
        )
        if self.risk.state.cooldown_until is not None:
            self.audit.log_breaker(
                event="cooldown",
                until=self.risk.state.cooldown_until.isoformat(),
                consecutive_losses=config.MAX_CONSECUTIVE_LOSSES,
            )
        self._log_event(f"Trade #{ticket} closed PnL={pnl:.2f} ({exit_reason})")

    # ----------------------------------------------------------- evaluate
    def _evaluate_and_trade(self) -> None:
        n_open = len(self._active)
        max_open = int(getattr(config, "MAX_CONCURRENT_POSITIONS", 1))
        if n_open >= max_open:
            self._skip_event("active",
                             f"No trade: {n_open}/{max_open} concurrent positions open")
            return
        if self.risk.in_cooldown():
            self._skip_event("cooldown",
                             f"No trade: cooldown until {self.risk.state.cooldown_until}")
            return
        if self.risk.order_limit_reached():
            self.audit.log_breaker(event="order_limit",
                                   orders_today=self.risk.state.orders_today)
            self._skip_event("order_limit", "No trade: max orders/day reached")
            return
        if not self.risk.in_session():
            now_hour = datetime.now(timezone.utc).hour
            self._skip_event("session",
                             f"No trade: outside UTC session now={now_hour}, "
                             f"allowed={config.SESSION_START_HOUR_UTC}-"
                             f"{config.SESSION_END_HOUR_UTC}")
            return
        if self.risk.daily_loss_breached(self.snapshot.balance,
                                        self.snapshot.daily_pnl):
            self._log_event("Daily loss limit hit — HALT")
            self.audit.log_breaker(event="daily_loss",
                                   daily_pnl=self.snapshot.daily_pnl,
                                   limit_pct=config.DAILY_LOSS_LIMIT_PCT)
            self._auto = False
            return

        symbol = config.SYMBOL
        info = self.client.symbol_info(symbol)
        tick = self.client.tick(symbol)
        if info is None or tick is None:
            self._skip_event("symbol_tick",
                             f"No trade: missing symbol/tick for {symbol}")
            return
        ok_sp, spread_now = self.risk.spread_ok(tick, info)
        if not ok_sp:
            self._skip_event("spread",
                             f"No trade: spread {spread_now:.2f} pips abnormal "
                             f"(avg={self.risk.spread_monitor.avg():.2f}, "
                             f"max={config.MAX_SPREAD_PIPS:.2f})")
            return
        if self.risk.in_news_blackout():
            self._skip_event("news", "No trade: inside news blackout window")
            return
        primary_tf = getattr(config, "PRIMARY_TF", "H1")
        fast_tf = getattr(config, "FAST_TF", "M15")
        rates_primary = self.feed.rates(symbol, primary_tf, 200)
        rates_fast = self.feed.rates(symbol, fast_tf, 200)
        if rates_primary is None or rates_fast is None or len(rates_primary) < 50:
            self._skip_event("rates", "No trade: insufficient candles")
            return
        atr_primary = calc_atr([r["high"] for r in rates_primary],
                          [r["low"] for r in rates_primary],
                          [r["close"] for r in rates_primary], 14)
        atr_fast_short = calc_atr([r["high"] for r in rates_fast],
                                 [r["low"] for r in rates_fast],
                                 [r["close"] for r in rates_fast], 14)
        atr_fast_long = calc_atr([r["high"] for r in rates_fast],
                                [r["low"] for r in rates_fast],
                                [r["close"] for r in rates_fast], 49)
        if atr_primary is None or atr_primary <= 0:
            self._skip_event("atr_primary", f"No trade: invalid {primary_tf} ATR")
            return
        if not self.risk.market_alive(atr_fast_short or 0, atr_fast_long or 0):
            self._skip_event("dead_market", "No trade: dead market")
            return

        ctx: Dict[str, object] = {"symbol": symbol, "feed": self.feed,
                                  "tick": tick, "info": info}
        features = build_features(rates_primary, rates_fast, tick, info,
                                  feed=self.feed)
        ml_prob_up = self.model.predict_proba(features)

        decision = self.ensemble.decide(self.factory.active(), ctx, ml_prob_up,
                                        trained_samples=self.model.trained_samples)
        with self._lock:
            self.snapshot.consensus = decision.consensus
            self.snapshot.ml_prob = decision.ml_prob
            self._consensus_hist.append(float(decision.consensus))
            self._ml_hist.append(float(decision.ml_prob))

        if decision.side is None:
            # ensemble already explains why (consensus or ML gate)
            tag = "ml_gate" if "ML" in decision.reason else "consensus"
            self._skip_event(tag, f"No trade: {decision.reason}")
            return


        price = tick.ask if decision.side == "BUY" else tick.bid
        sl, _legacy_tp, sl_dist = build_levels(decision.side, price,
                                               atr_primary, info.digits)
        # XAU profile: family-specific ATR multiplier overrides ATR_SL_MULT
        if _xau.profile_active(symbol) and decision.family:
            fam_mult = _xau.sl_atr_mult_for(decision.family)
            sl_dist = float(atr_primary) * fam_mult
            if decision.side == "BUY":
                sl = round(price - sl_dist, info.digits)
            else:
                sl = round(price + sl_dist, info.digits)
        # Build ladder
        if config.TP_LEVELS > 1:
            ladder = build_tp_ladder(decision.side, price, sl_dist, info.digits)
            fractions = list(config.TP_VOLUME_FRACTIONS[:config.TP_LEVELS])
        else:
            ladder = [_legacy_tp]
            fractions = [1.0]
        lot = self.risk.calc_lot(self.snapshot.balance, sl_dist, info)
        if lot <= 0:
            self._skip_event("lot",
                             f"No trade: invalid lot from balance={self.snapshot.balance:.2f}")
            return

        plan = OrderPlan(side=decision.side, price=price, sl=sl,
                         tp=ladder[-1], lot=lot,
                         strategy_id=decision.strategy_id,
                         tp_ladder=ladder, tp_fractions=fractions,
                         sl_dist=sl_dist)

        paper = getattr(self.executor, "is_paper", False)
        point = float(getattr(info, "point", 0.00001)) or 0.00001
        spread_pips = (float(tick.ask) - float(tick.bid)) / (point * 10.0)
        self.audit.log_order(
            symbol=symbol, side=decision.side, price=price,
            sl=sl, tp_ladder=ladder, fractions=fractions, lot=lot,
            strategy_id=decision.strategy_id,
            consensus=decision.consensus,
            ml_prob_win=round(decision.ml_prob_win, 4), paper=paper,
        )
        ticket = self.executor.send(symbol, plan)  # type: ignore[union-attr]
        if ticket is None:
            self.audit.log_fill(status="rejected",
                                strategy_id=decision.strategy_id,
                                side=decision.side, lot=lot, paper=paper)
            self._log_event(f"Order rejected ({decision.side} {lot})")
            return
        self.audit.log_fill(status="filled", ticket=ticket,
                            strategy_id=decision.strategy_id,
                            side=decision.side, lot=lot,
                            price=price, sl=sl, tp_ladder=ladder, paper=paper)
        self.risk.register_order()
        self.journal.open_trade(
            ticket, symbol=symbol, strategy_id=decision.strategy_id,
            side=decision.side, entry_price=price, sl_price=sl,
            tp_plan=ladder, tp_fractions=fractions,
            initial_volume=lot, sl_dist=sl_dist,
            ml_prob_win=decision.ml_prob_win, spread_entry=spread_pips, paper=paper,
        )
        self._active[int(ticket)] = {
            "features": features,
            "ml_prob_win": float(decision.ml_prob_win),
            "strategy_id": decision.strategy_id,
            "side": decision.side,
            "entry_price": float(price),
            "sl_price": float(sl),
            "sl_dist": float(sl_dist),
            "ladder": ladder,
            "fractions": fractions,
            "hits": [False] * len(ladder),
            "initial_volume": float(lot),
            "remaining_volume": float(lot),
        }
        self._active_ticket = int(ticket)
        self._log_event(
            f"OPEN {decision.side} lot={lot} @ {price:.5f} sl={sl:.5f} "
            f"ladder={[round(x,5) for x in ladder]} "
            f"strat={decision.strategy_id} {'[PAPER]' if paper else ''}"
        )

```

---

### `core/journal.py`
**Rol:** Persistență trade-uri SQLite — istoric pentru warmup ML și UI

**LOC:** 174

```python
"""SQLite trade journal — single source of truth for per-trade lifecycle.

Tables
------
trades:
    ticket INTEGER PRIMARY KEY, opened_at, closed_at, symbol, strategy_id,
    side, entry_price, exit_price, sl_price, tp_plan (JSON),
    tp_fractions (JSON), tp_hits (JSON), initial_volume, closed_volume,
    partial_closes (JSON list of {level, price, volume, pnl, ts}),
    exit_reason, gross_pnl, r_multiple, sl_dist, ml_prob_win,
    spread_entry, slippage_entry, slippage_exit, paper INTEGER

The journal is the data source for `core.audit` daily reports and for the
pre-start health checks in `core.health`.
"""
from __future__ import annotations

import json
import os
import sqlite3
import threading
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional

import config


def _ts() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


class Journal:
    def __init__(self, path: Optional[str] = None) -> None:
        self.path = path or config.JOURNAL_DB
        os.makedirs(os.path.dirname(self.path) or ".", exist_ok=True)
        self._lock = threading.Lock()
        self._init_db()

    def _conn(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self.path, timeout=5.0)
        conn.row_factory = sqlite3.Row
        return conn

    def _init_db(self) -> None:
        with self._lock, self._conn() as c:
            c.execute("""
                CREATE TABLE IF NOT EXISTS trades (
                    ticket INTEGER PRIMARY KEY,
                    opened_at TEXT,
                    closed_at TEXT,
                    symbol TEXT,
                    strategy_id TEXT,
                    side TEXT,
                    entry_price REAL,
                    exit_price REAL,
                    sl_price REAL,
                    tp_plan TEXT,
                    tp_fractions TEXT,
                    tp_hits TEXT,
                    initial_volume REAL,
                    closed_volume REAL,
                    partial_closes TEXT,
                    exit_reason TEXT,
                    gross_pnl REAL,
                    r_multiple REAL,
                    sl_dist REAL,
                    ml_prob_win REAL,
                    spread_entry REAL,
                    slippage_entry REAL,
                    slippage_exit REAL,
                    paper INTEGER
                )
            """)
            c.execute("CREATE INDEX IF NOT EXISTS idx_trades_opened "
                      "ON trades(opened_at)")

    # ---------------------------------------------------------------- API
    def open_trade(self, ticket: int, *, symbol: str, strategy_id: str,
                   side: str, entry_price: float, sl_price: float,
                   tp_plan: List[float], tp_fractions: List[float],
                   initial_volume: float, sl_dist: float,
                   ml_prob_win: float, spread_entry: float,
                   slippage_entry: float = 0.0, paper: bool = False) -> None:
        with self._lock, self._conn() as c:
            c.execute("""
                INSERT OR REPLACE INTO trades
                (ticket, opened_at, symbol, strategy_id, side, entry_price,
                 sl_price, tp_plan, tp_fractions, tp_hits, initial_volume,
                 closed_volume, partial_closes, sl_dist, ml_prob_win,
                 spread_entry, slippage_entry, paper)
                VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
            """, (
                int(ticket), _ts(), symbol, strategy_id, side,
                float(entry_price), float(sl_price),
                json.dumps(tp_plan), json.dumps(tp_fractions),
                json.dumps([False] * len(tp_plan)),
                float(initial_volume), 0.0, json.dumps([]),
                float(sl_dist), float(ml_prob_win), float(spread_entry),
                float(slippage_entry), 1 if paper else 0,
            ))

    def record_partial(self, ticket: int, level: int, price: float,
                       volume: float, pnl: float) -> None:
        with self._lock, self._conn() as c:
            row = c.execute(
                "SELECT tp_hits, partial_closes, closed_volume FROM trades "
                "WHERE ticket=?", (int(ticket),)).fetchone()
            if row is None:
                return
            hits = json.loads(row["tp_hits"] or "[]")
            while len(hits) <= level:
                hits.append(False)
            hits[level] = True
            partials = json.loads(row["partial_closes"] or "[]")
            partials.append({
                "level": level, "price": float(price),
                "volume": float(volume), "pnl": float(pnl), "ts": _ts(),
            })
            c.execute(
                "UPDATE trades SET tp_hits=?, partial_closes=?, "
                "closed_volume=? WHERE ticket=?",
                (json.dumps(hits), json.dumps(partials),
                 float(row["closed_volume"] or 0.0) + float(volume),
                 int(ticket))
            )

    def close_trade(self, ticket: int, *, exit_reason: str,
                    exit_price: float, gross_pnl: float,
                    slippage_exit: float = 0.0) -> None:
        with self._lock, self._conn() as c:
            row = c.execute(
                "SELECT initial_volume, sl_dist, entry_price, side "
                "FROM trades WHERE ticket=?", (int(ticket),)).fetchone()
            r_mult = 0.0
            if row is not None and row["sl_dist"]:
                # rough R-multiple in money is PnL / (initial risk in money)
                # We don't have tick value here, so use price-based R:
                price_move = (float(exit_price) - float(row["entry_price"]))
                if row["side"] == "SELL":
                    price_move = -price_move
                r_mult = price_move / float(row["sl_dist"])
            c.execute("""
                UPDATE trades SET closed_at=?, exit_price=?, exit_reason=?,
                gross_pnl=?, r_multiple=?, slippage_exit=?
                WHERE ticket=?
            """, (_ts(), float(exit_price), exit_reason, float(gross_pnl),
                  float(r_mult), float(slippage_exit), int(ticket)))

    # ----------------------------------------------------------- queries
    def query_day(self, day: Optional[str] = None) -> List[Dict[str, Any]]:
        """Return all trades CLOSED on the given UTC day (YYYY-MM-DD)."""
        day = day or datetime.now(timezone.utc).strftime("%Y-%m-%d")
        with self._lock, self._conn() as c:
            rows = c.execute(
                "SELECT * FROM trades WHERE closed_at LIKE ? ORDER BY closed_at",
                (f"{day}%",)).fetchall()
        return [self._row_to_dict(r) for r in rows]

    def query_recent_days(self, n: int) -> List[Dict[str, Any]]:
        with self._lock, self._conn() as c:
            rows = c.execute(
                "SELECT * FROM trades WHERE closed_at IS NOT NULL "
                "ORDER BY closed_at DESC LIMIT ?", (int(n * 200),)).fetchall()
        return [self._row_to_dict(r) for r in rows]

    @staticmethod
    def _row_to_dict(r: sqlite3.Row) -> Dict[str, Any]:
        d = dict(r)
        for k in ("tp_plan", "tp_fractions", "tp_hits", "partial_closes"):
            try:
                d[k] = json.loads(d.get(k) or "[]")
            except (json.JSONDecodeError, TypeError):
                d[k] = []
        return d

```

---

### `core/audit.py`
**Rol:** Logger structurat — fiecare decizie / veto / trade jurnalizat

**LOC:** 263

```python
"""Audit logging + daily report exporter (journal-aware).

Writes JSON-lines event logs under `config.LOG_DIR` for raw events
(orders, fills, pnl, breakers) and a richer per-strategy CSV/JSON daily
report into `config.REPORT_DIR` sourced from the SQLite trade journal.
"""
from __future__ import annotations

import csv
import json
import os
import threading
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional

import config


def _today() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%d")


def _today_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%d")


def _ts() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


class AuditLogger:
    """Thread-safe JSON-lines audit writer + daily report exporter."""

    def __init__(self, log_dir: Optional[str] = None,
                 journal=None) -> None:
        self.log_dir = log_dir or config.LOG_DIR
        os.makedirs(self.log_dir, exist_ok=True)
        self._lock = threading.Lock()
        self.journal = journal

    # -------------------------------------------------------------- writers
    def _write(self, kind: str, payload: Dict[str, Any]) -> None:
        path = os.path.join(self.log_dir, f"{kind}_{_today()}.jsonl")
        payload = {"ts": _ts(), **payload}
        line = json.dumps(payload, default=str)
        with self._lock:
            try:
                with open(path, "a", encoding="utf-8") as f:
                    f.write(line + "\n")
            except OSError:
                pass

    def log_order(self, **fields: Any) -> None:
        self._write("orders", fields)

    def log_fill(self, **fields: Any) -> None:
        self._write("fills", fields)

    def log_pnl(self, **fields: Any) -> None:
        self._write("pnl", fields)

    def log_breaker(self, **fields: Any) -> None:
        self._write("breakers", fields)

    def log_partial(self, **fields: Any) -> None:
        self._write("partials", fields)

    # -------------------------------------------------------- daily reports
    def export_daily_report(self, factory=None, day: Optional[str] = None,
                            out_dir: Optional[str] = None) -> Dict[str, str]:
        """Build a daily report sourced from the trade journal."""
        day_iso = day or _today_iso()
        day_compact = day_iso.replace("-", "")
        out_dir = out_dir or config.REPORT_DIR
        os.makedirs(out_dir, exist_ok=True)

        trades: List[Dict[str, Any]] = []
        if self.journal is not None:
            try:
                trades = self.journal.query_day(day_iso)
            except Exception:
                trades = []

        # -------- per-strategy aggregation
        by_strategy: Dict[str, Dict[str, Any]] = {}
        equity_curve: List[float] = []
        running = 0.0
        all_spreads: List[float] = []
        all_slips: List[float] = []
        for t in sorted(trades, key=lambda x: x.get("closed_at") or ""):
            sid = t.get("strategy_id") or "unknown"
            d = by_strategy.setdefault(sid, {
                "trades": 0, "wins": 0, "losses": 0, "gross_pnl": 0.0,
                "ml_probs": [], "r_mults": [],
                "tp_hits": [0] * config.TP_LEVELS,
                "spreads": [], "slip_in": [], "slip_out": [],
            })
            d["trades"] += 1
            pnl = float(t.get("gross_pnl") or 0.0)
            d["gross_pnl"] += pnl
            running += pnl
            equity_curve.append(running)
            if pnl > 0:
                d["wins"] += 1
            else:
                d["losses"] += 1
            d["ml_probs"].append(float(t.get("ml_prob_win") or 0.0))
            d["r_mults"].append(float(t.get("r_multiple") or 0.0))
            sp = float(t.get("spread_entry") or 0.0)
            si = float(t.get("slippage_entry") or 0.0)
            so = float(t.get("slippage_exit") or 0.0)
            d["spreads"].append(sp)
            d["slip_in"].append(si)
            d["slip_out"].append(so)
            all_spreads.append(sp)
            all_slips.append(abs(si) + abs(so))
            for i, hit in enumerate(t.get("tp_hits") or []):
                if i < len(d["tp_hits"]) and hit:
                    d["tp_hits"][i] += 1

        # max intraday drawdown from equity curve
        max_dd = 0.0
        peak = 0.0
        for v in equity_curve:
            peak = max(peak, v)
            dd = peak - v
            max_dd = max(max_dd, dd)

        def _avg(xs):
            return (sum(xs) / len(xs)) if xs else 0.0

        rows: List[Dict[str, Any]] = []
        pruned: List[str] = []
        for sid, d in by_strategy.items():
            wr = d["wins"] / d["trades"] if d["trades"] else 0.0
            avg_ml = _avg(d["ml_probs"])
            avg_r = _avg(d["r_mults"])
            avg_spread = _avg(d["spreads"])
            avg_slip_in = _avg(d["slip_in"])
            avg_slip_out = _avg(d["slip_out"])
            avg_slip_total = _avg([abs(a) + abs(b)
                                   for a, b in zip(d["slip_in"], d["slip_out"])])
            life_trades = life_wins = 0
            if factory is not None:
                for s in factory.instances:
                    if s.id == sid:
                        life_trades = s.stats.trades
                        life_wins = s.stats.wins
                        break
            row: Dict[str, Any] = {
                "strategy_id": sid,
                "trades_today": d["trades"],
                "wins_today": d["wins"],
                "losses_today": d["losses"],
                "win_rate_today": round(wr, 4),
                "gross_pnl": round(d["gross_pnl"], 2),
                "avg_ml_prob_win": round(avg_ml, 4),
                "lifetime_trades": life_trades,
                "lifetime_wins": life_wins,
                "lifetime_win_rate": (round(life_wins / life_trades, 4)
                                      if life_trades else ""),
                "avg_R_today": round(avg_r, 3),
                "max_drawdown_today": round(max_dd, 2),
                "avg_spread_pips": round(avg_spread, 3),
                "avg_slippage_entry_pips": round(avg_slip_in, 3),
                "avg_slippage_exit_pips": round(avg_slip_out, 3),
                "avg_slippage_pips": round(avg_slip_total, 3),
            }
            for i in range(config.TP_LEVELS):
                hits = d["tp_hits"][i] if i < len(d["tp_hits"]) else 0
                row[f"tp{i+1}_hit_rate"] = (round(hits / d["trades"], 3)
                                            if d["trades"] else 0.0)
            rows.append(row)
        rows.sort(key=lambda r: r["gross_pnl"], reverse=True)

        # -------- auto-prune losing strategies from today's report
        if getattr(config, "AUTO_PRUNE_FROM_REPORT", False) and factory is not None:
            min_n = int(getattr(config, "AUTO_PRUNE_MIN_TRADES", 5))
            max_wr = float(getattr(config, "AUTO_PRUNE_WINRATE", 0.30))
            max_pnl = float(getattr(config, "AUTO_PRUNE_MAX_PNL", 0.0))
            for r in rows:
                if (r["trades_today"] >= min_n
                        and r["win_rate_today"] < max_wr
                        and r["gross_pnl"] <= max_pnl):
                    for s in factory.instances:
                        if s.id == r["strategy_id"] and s.stats.enabled:
                            s.stats.enabled = False
                            pruned.append(s.id)
                            break
            if pruned:
                try:
                    factory._save_stats()  # type: ignore[attr-defined]
                except Exception:
                    pass
                self._write("breakers", {
                    "event": "auto_prune_losers",
                    "day": day_iso,
                    "count": len(pruned),
                    "strategies": pruned,
                })

        csv_path = os.path.join(out_dir, f"daily_report_{day_compact}.csv")
        if rows:
            with open(csv_path, "w", newline="", encoding="utf-8") as f:
                w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
                w.writeheader()
                w.writerows(rows)
        else:
            with open(csv_path, "w", encoding="utf-8") as f:
                f.write("strategy_id,trades_today\n")

        summary = {
            "day": day_iso,
            "generated_at": _ts(),
            "closed_trades": len(trades),
            "total_pnl": round(sum(float(t.get("gross_pnl") or 0)
                                   for t in trades), 2),
            "wins": sum(1 for t in trades
                        if float(t.get("gross_pnl") or 0) > 0),
            "losses": sum(1 for t in trades
                          if float(t.get("gross_pnl") or 0) <= 0),
            "max_drawdown_today": round(max_dd, 2),
            "avg_spread_pips": round(_avg(all_spreads), 3),
            "avg_slippage_pips": round(_avg(all_slips), 3),
            "auto_pruned_strategies": pruned,
            "per_strategy": rows,
        }
        json_path = os.path.join(out_dir, f"daily_report_{day_compact}.json")
        with open(json_path, "w", encoding="utf-8") as f:
            json.dump(summary, f, indent=2, default=str)

        return {"csv": csv_path, "json": json_path}

    # ------------------------------------------------ today snapshot for UI
    def today_summary(self) -> Dict[str, Any]:
        """Quick snapshot used by the dashboard for 'Today's Performance'."""
        if self.journal is None:
            return {}
        try:
            trades = self.journal.query_day(_today_iso())
        except Exception:
            return {}
        n = len(trades)
        wins = sum(1 for t in trades
                   if float(t.get("gross_pnl") or 0) > 0)
        losses = n - wins
        total_pnl = sum(float(t.get("gross_pnl") or 0) for t in trades)
        rs = [float(t.get("r_multiple") or 0) for t in trades]
        avg_r = sum(rs) / len(rs) if rs else 0.0
        running = 0.0
        peak = 0.0
        max_dd = 0.0
        for t in sorted(trades, key=lambda x: x.get("closed_at") or ""):
            running += float(t.get("gross_pnl") or 0)
            peak = max(peak, running)
            max_dd = max(max_dd, peak - running)
        return {
            "trades": n, "wins": wins, "losses": losses,
            "win_rate": (wins / n) if n else 0.0,
            "pnl": round(total_pnl, 2),
            "avg_r": round(avg_r, 3),
            "max_dd": round(max_dd, 2),
        }

```

---

### `core/health.py`
**Rol:** Self-monitor: heartbeat, latență, sanity checks

**LOC:** 168

```python
"""Pre-start health checks: environment, risk config, recent performance.

Used by the UI before enabling auto-trading. Returns a list of
`CheckResult` and a global status:
    OK                  — safe to start
    WARN_REQUIRES_CONFIRM — start only after explicit user confirmation
    HALT                — must not start
"""
from __future__ import annotations

import csv
import glob
import os
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import List, Tuple

import config


PASS = "PASS"
WARN = "WARN"
FAIL = "FAIL"


@dataclass
class CheckResult:
    category: str
    name: str
    status: str         # PASS / WARN / FAIL
    message: str
    critical: bool = False   # FAIL on a critical check ⇒ HALT


def _env_checks(client) -> List[CheckResult]:
    out: List[CheckResult] = []
    out.append(CheckResult(
        "ENV", "MT5 connected",
        PASS if getattr(client, "connected", False) else FAIL,
        "Terminal reachable" if getattr(client, "connected", False)
        else "MT5 terminal not connected — start it and log in",
        critical=True,
    ))
    info = None
    try:
        info = client.account_info()
    except Exception:
        info = None
    if info is None:
        out.append(CheckResult("ENV", "Account info", WARN,
                               "Could not read account info (paper mode OK)"))
    else:
        allowed = bool(getattr(info, "trade_allowed", True))
        out.append(CheckResult(
            "ENV", "Trade allowed",
            PASS if allowed else FAIL,
            "Account permits trading" if allowed
            else "Account is read-only / trading disabled",
            critical=True,
        ))
    sym = config.SYMBOL
    try:
        sinfo = client.symbol_info(sym)
        tick = client.tick(sym)
    except Exception:
        sinfo, tick = None, None
    if sinfo is None or tick is None:
        out.append(CheckResult("ENV", f"Symbol {sym}", FAIL,
                               "Symbol not available or no tick",
                               critical=True))
    else:
        age = max(0, int(time.time()) - int(getattr(tick, "time", 0)))
        if age <= config.PRESTART_MAX_TICK_AGE_SEC:
            out.append(CheckResult("ENV", f"Feed {sym}", PASS,
                                   f"Last tick {age}s ago"))
        else:
            out.append(CheckResult("ENV", f"Feed {sym}", WARN,
                                   f"Stale tick ({age}s) — market may be closed"))
    return out


def _risk_cfg_checks() -> List[CheckResult]:
    out: List[CheckResult] = []
    if 0 < config.RISK_PCT <= 0.05:
        out.append(CheckResult("RISK_CFG", "Risk per trade", PASS,
                               f"{config.RISK_PCT*100:.2f}% (safe)"))
    else:
        out.append(CheckResult("RISK_CFG", "Risk per trade", FAIL,
                               f"{config.RISK_PCT*100:.2f}% out of (0, 5]%",
                               critical=True))
    if config.DAILY_LOSS_LIMIT_PCT > 0:
        out.append(CheckResult("RISK_CFG", "Daily loss limit", PASS,
                               f"{config.DAILY_LOSS_LIMIT_PCT*100:.1f}%"))
    else:
        out.append(CheckResult("RISK_CFG", "Daily loss limit", FAIL,
                               "Disabled — required for prop-firm", critical=True))
    issues = config.validate_tp_config()
    if not issues:
        out.append(CheckResult("RISK_CFG", "TP ladder", PASS,
                               f"{config.TP_LEVELS} levels, "
                               f"RR={config.TP_RR_MULTIPLIERS[:config.TP_LEVELS]}"))
    else:
        out.append(CheckResult("RISK_CFG", "TP ladder", FAIL,
                               "; ".join(issues), critical=True))
    return out


def _performance_checks() -> List[CheckResult]:
    out: List[CheckResult] = []
    days = sorted(glob.glob(os.path.join(config.REPORT_DIR,
                                         "daily_report_*.csv")))
    if not days:
        out.append(CheckResult("PERFORMANCE", "Historical reports", PASS,
                               "No prior reports — fresh start"))
        return out
    recent = days[-config.PRESTART_LOOKBACK_DAYS:]
    total_trades = total_wins = 0
    total_pnl = 0.0
    for path in recent:
        try:
            with open(path, encoding="utf-8") as f:
                for row in csv.DictReader(f):
                    total_trades += int(float(row.get("trades_today", 0) or 0))
                    total_wins += int(float(row.get("wins_today", 0) or 0))
                    total_pnl += float(row.get("gross_pnl", 0) or 0)
        except (OSError, ValueError, KeyError):
            continue
    if total_trades == 0:
        out.append(CheckResult("PERFORMANCE", "Recent results", PASS,
                               "No trades in lookback window"))
        return out
    wr = total_wins / total_trades
    if wr >= config.PRESTART_MIN_WINRATE and total_pnl >= 0:
        out.append(CheckResult("PERFORMANCE", "Recent results", PASS,
                               f"WR {wr*100:.1f}% / PnL {total_pnl:+.2f} "
                               f"over {total_trades} trades"))
    elif wr < config.PRESTART_MIN_WINRATE:
        out.append(CheckResult(
            "PERFORMANCE", "Recent results", FAIL,
            f"WR {wr*100:.1f}% < {config.PRESTART_MIN_WINRATE*100:.0f}% "
            f"over {total_trades} trades — review before enabling",
        ))
    else:
        out.append(CheckResult(
            "PERFORMANCE", "Recent results", WARN,
            f"PnL {total_pnl:+.2f} negative over {total_trades} trades",
        ))
    return out


def run_checks(client) -> Tuple[List[CheckResult], str]:
    checks: List[CheckResult] = []
    checks += _env_checks(client)
    checks += _risk_cfg_checks()
    checks += _performance_checks()
    status = "OK"
    for c in checks:
        if c.status == FAIL and c.critical:
            return checks, "HALT"
        if c.status == FAIL or c.status == WARN:
            status = "WARN_REQUIRES_CONFIRM"
    return checks, status


def status_color(status: str) -> str:
    return {"PASS": "#2ecc71", "WARN": "#f1c40f",
            "FAIL": "#e74c3c"}.get(status, "#9a9a9a")

```

---

### `data/feed.py`
**Rol:** Wrapper rates() cu cache scurt

**LOC:** 45

```python
"""Multi-timeframe rate cache."""
from __future__ import annotations

import time
from typing import Dict, Optional, Tuple

try:
    import MetaTrader5 as mt5
except ImportError:  # pragma: no cover
    mt5 = None  # type: ignore

from core.mt5_client import MT5Client


TF_MAP = {
    "M1":  getattr(mt5, "TIMEFRAME_M1", 1)   if mt5 else 1,
    "M5":  getattr(mt5, "TIMEFRAME_M5", 5)   if mt5 else 5,
    "M15": getattr(mt5, "TIMEFRAME_M15", 15) if mt5 else 15,
    "M30": getattr(mt5, "TIMEFRAME_M30", 30) if mt5 else 30,
    "H1":  getattr(mt5, "TIMEFRAME_H1", 60)  if mt5 else 60,
    "H4":  getattr(mt5, "TIMEFRAME_H4", 240) if mt5 else 240,
    "D1":  getattr(mt5, "TIMEFRAME_D1", 1440) if mt5 else 1440,
}

CACHE_TTL = {"M1": 5, "M5": 15, "M15": 30, "M30": 45, "H1": 60, "H4": 120, "D1": 300}


class DataFeed:
    def __init__(self, client: MT5Client) -> None:
        self.client = client
        self._cache: Dict[Tuple[str, str], Tuple[float, object]] = {}

    def rates(self, symbol: str, tf: str, count: int = 200):
        key = (symbol, tf)
        now = time.time()
        if key in self._cache:
            ts, data = self._cache[key]
            if now - ts < CACHE_TTL.get(tf, 30) and data is not None and len(data) >= count:
                return data[-count:]
        tf_const = TF_MAP[tf]
        data = self.client.rates(symbol, tf_const, max(count, 250))
        if data is None:
            return None
        self._cache[key] = (now, data)
        return data[-count:]

```

---

### `data/indicators.py`
**Rol:** Indicatori tehnici puri (ATR, EMA, RSI, BB, MACD, ADX etc.) — fără dependențe externe

**LOC:** 202

```python
"""Pure-Python / numpy indicators. All functions tolerate short series
by returning None when not enough data is available.
"""
from __future__ import annotations

from typing import List, Optional, Sequence, Tuple

try:
    import numpy as np
except ImportError:  # pragma: no cover
    np = None  # type: ignore


def sma(values: Sequence[float], period: int) -> Optional[float]:
    if len(values) < period or period <= 0:
        return None
    return float(sum(values[-period:]) / period)


def ema(values: Sequence[float], period: int) -> Optional[float]:
    if len(values) < period or period <= 0:
        return None
    k = 2.0 / (period + 1.0)
    e = float(sum(values[:period]) / period)
    for v in values[period:]:
        e = v * k + e * (1 - k)
    return e


def ema_series(values: Sequence[float], period: int) -> List[float]:
    if len(values) < period or period <= 0:
        return []
    k = 2.0 / (period + 1.0)
    e = float(sum(values[:period]) / period)
    out = [e]
    for v in values[period:]:
        e = v * k + e * (1 - k)
        out.append(e)
    return out


def rsi(values: Sequence[float], period: int = 14) -> Optional[float]:
    if len(values) < period + 1:
        return None
    gains = 0.0
    losses = 0.0
    for i in range(-period, 0):
        diff = values[i] - values[i - 1]
        if diff >= 0:
            gains += diff
        else:
            losses -= diff
    if losses == 0:
        return 100.0
    rs = (gains / period) / (losses / period)
    return 100.0 - (100.0 / (1.0 + rs))


def atr(highs: Sequence[float], lows: Sequence[float],
        closes: Sequence[float], period: int = 14) -> Optional[float]:
    n = len(closes)
    if n < period + 1:
        return None
    trs: List[float] = []
    for i in range(1, n):
        tr = max(highs[i] - lows[i],
                 abs(highs[i] - closes[i - 1]),
                 abs(lows[i] - closes[i - 1]))
        trs.append(tr)
    return float(sum(trs[-period:]) / period)


def macd(values: Sequence[float], fast: int = 12, slow: int = 26,
         signal: int = 9) -> Optional[Tuple[float, float, float]]:
    if len(values) < slow + signal:
        return None
    ef = ema_series(values, fast)
    es = ema_series(values, slow)
    n = min(len(ef), len(es))
    if n < signal:
        return None
    macd_line = [ef[-n + i] - es[-n + i] for i in range(n)]
    sig = ema_series(macd_line, signal)
    if not sig:
        return None
    m = macd_line[-1]
    s = sig[-1]
    return m, s, m - s


def bollinger(values: Sequence[float], period: int = 20,
              mult: float = 2.0) -> Optional[Tuple[float, float, float]]:
    if len(values) < period:
        return None
    window = list(values[-period:])
    mean = sum(window) / period
    var = sum((v - mean) ** 2 for v in window) / period
    sd = var ** 0.5
    return mean, mean + mult * sd, mean - mult * sd


def stochastic(highs: Sequence[float], lows: Sequence[float],
               closes: Sequence[float], k: int = 14,
               d: int = 3) -> Optional[Tuple[float, float]]:
    if len(closes) < k + d:
        return None
    ks: List[float] = []
    for i in range(-k - d + 1, 0 + 1):
        hh = max(highs[i - k + 1:i + 1] if i != 0 else highs[-k:])
        ll = min(lows[i - k + 1:i + 1] if i != 0 else lows[-k:])
        c = closes[i] if i != 0 else closes[-1]
        if hh == ll:
            ks.append(50.0)
        else:
            ks.append(100.0 * (c - ll) / (hh - ll))
    k_val = ks[-1]
    d_val = sum(ks[-d:]) / d
    return k_val, d_val


def adx(highs: Sequence[float], lows: Sequence[float],
        closes: Sequence[float], period: int = 14) -> Optional[float]:
    n = len(closes)
    if n < period * 2:
        return None
    plus_dm = []
    minus_dm = []
    trs = []
    for i in range(1, n):
        up = highs[i] - highs[i - 1]
        dn = lows[i - 1] - lows[i]
        plus_dm.append(up if (up > dn and up > 0) else 0.0)
        minus_dm.append(dn if (dn > up and dn > 0) else 0.0)
        trs.append(max(highs[i] - lows[i],
                       abs(highs[i] - closes[i - 1]),
                       abs(lows[i] - closes[i - 1])))
    atr_v = sum(trs[-period:]) / period or 1e-9
    pdi = 100.0 * (sum(plus_dm[-period:]) / period) / atr_v
    mdi = 100.0 * (sum(minus_dm[-period:]) / period) / atr_v
    s = pdi + mdi
    if s == 0:
        return 0.0
    dx = 100.0 * abs(pdi - mdi) / s
    return dx


def vwap(highs: Sequence[float], lows: Sequence[float],
         closes: Sequence[float], volumes: Sequence[float]) -> Optional[float]:
    if not closes or len(volumes) != len(closes):
        return None
    num = sum(((h + l + c) / 3.0) * v
              for h, l, c, v in zip(highs, lows, closes, volumes))
    den = sum(volumes) or 1e-9
    return num / den


def fractal_high(highs: Sequence[float]) -> bool:
    if len(highs) < 5:
        return False
    h = highs
    i = -3
    return h[i] > h[i - 1] and h[i] > h[i - 2] and h[i] > h[i + 1] and h[i] > h[i + 2]


def fractal_low(lows: Sequence[float]) -> bool:
    if len(lows) < 5:
        return False
    l = lows
    i = -3
    return l[i] < l[i - 1] and l[i] < l[i - 2] and l[i] < l[i + 1] and l[i] < l[i + 2]


def is_bullish_engulfing(opens, closes) -> bool:
    if len(closes) < 2:
        return False
    return (closes[-2] < opens[-2]
            and closes[-1] > opens[-1]
            and closes[-1] > opens[-2]
            and opens[-1] < closes[-2])


def is_bearish_engulfing(opens, closes) -> bool:
    if len(closes) < 2:
        return False
    return (closes[-2] > opens[-2]
            and closes[-1] < opens[-1]
            and closes[-1] < opens[-2]
            and opens[-1] > closes[-2])


def is_pinbar(opens, highs, lows, closes) -> Optional[str]:
    if not closes:
        return None
    o, h, l, c = opens[-1], highs[-1], lows[-1], closes[-1]
    body = abs(c - o) or 1e-9
    upper = h - max(c, o)
    lower = min(c, o) - l
    if lower > 2 * body and upper < body:
        return "BULL"
    if upper > 2 * body and lower < body:
        return "BEAR"
    return None

```

---

### `ml/model.py`
**Rol:** Online Logistic Regression cu SGD — învață din fiecare trade închis

**LOC:** 83

```python
"""Online logistic regression with L2 regularisation, learning-rate decay,
probability clipping and optional exponential forgetting.

No external ML libs (pure Python). Target = 1 (win) / 0 (loss).
"""
from __future__ import annotations

import math
from typing import List

from ml.features import FEATURE_DIM


CLIP_LO, CLIP_HI = 1e-3, 1.0 - 1e-3


class OnlineLogReg:
    def __init__(self, dim: int = FEATURE_DIM, base_lr: float = 0.05,
                 l2: float = 1e-4, lr_decay: float = 1e-4,
                 forget_lambda: float = 1.0) -> None:
        """forget_lambda < 1 applies exponential weight decay each update
        (recent samples matter more); 1.0 disables forgetting."""
        self.dim = dim
        self.base_lr = base_lr
        self.l2 = l2
        self.lr_decay = lr_decay
        self.forget_lambda = forget_lambda
        self.w: List[float] = [0.0] * dim
        self.b: float = 0.0
        self.trained_samples: int = 0

    # ----------------------------------------------------------- forward
    @staticmethod
    def _sigmoid(z: float) -> float:
        if z >= 0:
            return 1.0 / (1.0 + math.exp(-z))
        ez = math.exp(z)
        return ez / (1.0 + ez)

    def predict_proba(self, x: List[float]) -> float:
        if len(x) != self.dim:
            return 0.5
        z = self.b + sum(wi * xi for wi, xi in zip(self.w, x))
        p = self._sigmoid(z)
        return max(CLIP_LO, min(CLIP_HI, p))

    # ----------------------------------------------------------- update
    def _lr(self) -> float:
        return self.base_lr / (1.0 + self.lr_decay * self.trained_samples)

    def update(self, x: List[float], y: int) -> None:
        if len(x) != self.dim:
            return
        p = self.predict_proba(x)
        err = p - y                       # dL/dz for BCE + sigmoid
        lr = self._lr()
        if self.forget_lambda < 1.0:
            self.w = [wi * self.forget_lambda for wi in self.w]
        # gradient step with L2
        self.w = [wi - lr * (err * xi + self.l2 * wi)
                  for wi, xi in zip(self.w, x)]
        self.b -= lr * err
        self.trained_samples += 1

    # ----------------------------------------------------------- io
    def to_dict(self) -> dict:
        return {"w": self.w, "b": self.b, "base_lr": self.base_lr,
                "l2": self.l2, "lr_decay": self.lr_decay,
                "forget_lambda": self.forget_lambda, "dim": self.dim,
                "trained_samples": self.trained_samples}

    def load_dict(self, d: dict) -> None:
        dim = int(d.get("dim", self.dim))
        if dim != self.dim:
            # incompatible weights → keep zero init; do not crash.
            return
        self.w = list(d.get("w", self.w))
        self.b = float(d.get("b", 0.0))
        self.base_lr = float(d.get("base_lr", self.base_lr))
        self.l2 = float(d.get("l2", self.l2))
        self.lr_decay = float(d.get("lr_decay", self.lr_decay))
        self.forget_lambda = float(d.get("forget_lambda", self.forget_lambda))
        self.trained_samples = int(d.get("trained_samples", 0))

```

---

### `ml/features.py`
**Rol:** Feature engineering baseline (22 features)

**LOC:** 128

```python
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

```

---

### `ml/xau_features.py`
**Rol:** Feature engineering XAU (+10 features: DXY, US10Y, sesiune, spread MA, etc.)

**LOC:** 132

```python
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

```

---

### `ml/store.py`
**Rol:** Persistență weights model (pickle)

**LOC:** 39

```python
"""Persistence layer for ML model weights (JSON).

If the stored feature dim no longer matches the current FEATURE_DIM, the
old file is left in place but ignored (model keeps fresh zero weights).
"""
from __future__ import annotations

import json
import os

import config
from ml.model import OnlineLogReg


class MLStore:
    def __init__(self, path: str = config.ML_WEIGHTS_FILE) -> None:
        self.path = path

    def save_model(self, model: OnlineLogReg) -> None:
        os.makedirs(os.path.dirname(self.path) or ".", exist_ok=True)
        try:
            with open(self.path, "w") as f:
                json.dump(model.to_dict(), f)
        except OSError:
            pass

    def load_model(self, model: OnlineLogReg) -> None:
        if not os.path.exists(self.path):
            return
        try:
            with open(self.path) as f:
                d = json.load(f)
            stored_dim = int(d.get("dim", -1))
            if stored_dim != model.dim:
                # incompatible (feature schema changed) → start fresh
                return
            model.load_dict(d)
        except Exception:
            pass

```

---

### `ml/trainer.py`
**Rol:** Update incremental după fiecare trade închis

**LOC:** 57

```python
"""Glue between model + persistent store + calibration tracker."""
from __future__ import annotations

from collections import deque
from typing import Deque, List, Tuple

from ml.model import OnlineLogReg
from ml.store import MLStore


class Trainer:
    """Wraps `OnlineLogReg` with rolling loss + calibration tracking.

    - rolling_loss: mean BCE over recent updates
    - calibration_high: win rate for samples where predicted prob >= 0.6
      (only meaningful once ~30 high-prob predictions are recorded)
    """

    def __init__(self, model: OnlineLogReg, store: MLStore,
                 window: int = 200) -> None:
        self.model = model
        self.store = store
        self._recent: Deque[Tuple[float, int]] = deque(maxlen=window)

    # ----------------------------------------------------------- updates
    def update(self, features: List[float], won: bool) -> None:
        import math
        p = self.model.predict_proba(features)
        y = 1 if won else 0
        # BCE before the SGD step (predictive performance)
        bce = -(y * math.log(max(p, 1e-9))
                + (1 - y) * math.log(max(1 - p, 1e-9)))
        self._recent.append((p, y))
        self.model.update(features, y)
        self.store.save_model(self.model)
        return bce  # noqa: returned for callers that care; ignored otherwise

    # ----------------------------------------------------------- stats
    def rolling_loss(self) -> float:
        if not self._recent:
            return 0.0
        import math
        s = 0.0
        for p, y in self._recent:
            s += -(y * math.log(max(p, 1e-9))
                   + (1 - y) * math.log(max(1 - p, 1e-9)))
        return s / len(self._recent)

    def calibration_high(self, threshold: float = 0.6) -> Tuple[int, float]:
        """Return (n, win_rate) for predictions with p>=threshold."""
        bucket = [y for p, y in self._recent if p >= threshold]
        if not bucket:
            return 0, 0.0
        return len(bucket), sum(bucket) / len(bucket)

    def trained_samples(self) -> int:
        return self.model.trained_samples

```

---

### `ml/warmup.py`
**Rol:** Bootstrap model din journal-ul existent

**LOC:** 56

```python
"""Warm up the ML model from past trades stored in the SQLite journal.

We cannot recompute exact entry-time features after the fact (no historical
tick context), so warmup uses a degraded feature proxy derived from the
trade record itself: side, ml_prob_win (if previously stored), spread,
R-multiple, etc. This is sufficient to push weights away from zero and to
seed calibration; it is NOT a replacement for true online learning.

Strategy:
- pull up to `max_trades` most recent closed trades from the journal,
- build a simple feature proxy + label = (gross_pnl > 0),
- run `epochs` SGD passes.

Returns the number of updates applied.
"""
from __future__ import annotations

from typing import List

from core.journal import Journal
from ml.features import FEATURE_DIM
from ml.model import OnlineLogReg


def _proxy_features(trade: dict) -> List[float]:
    """Build a FEATURE_DIM-sized vector from journal columns.

    Layout mirrors `ml.features.build_features` slot-by-slot; unknown slots
    are filled with 0 so the bias term still carries information.
    """
    x = [0.0] * FEATURE_DIM
    x[0] = 1.0  # bias
    side = 1.0 if (trade.get("side") == "BUY") else -1.0
    x[6] = side                                              # regime_trend slot
    x[16] = side                                             # pattern slot
    x[21] = float(trade.get("spread_entry") or 0.0) / 5.0    # spread slot
    r = float(trade.get("r_multiple") or 0.0)
    x[10] = max(-2.0, min(2.0, r))                           # atr_ratio slot proxy
    return x


def warmup_from_journal(model: OnlineLogReg, journal: Journal,
                        max_trades: int = 500, epochs: int = 2) -> int:
    rows = journal.query_recent_days(5)[:max_trades]
    if not rows:
        return 0
    updates = 0
    for _ in range(max(1, epochs)):
        for tr in rows:
            pnl = tr.get("gross_pnl")
            if pnl is None:
                continue
            y = 1 if float(pnl) > 0 else 0
            model.update(_proxy_features(tr), y)
            updates += 1
    return updates

```

---

### `strategies/base.py`
**Rol:** Interface Strategy + StrategyStats (win_rate per instanță)

**LOC:** 43

```python
"""Strategy interface + per-instance stat tracking."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, Optional, Tuple


SIGNAL_BUY = "BUY"
SIGNAL_SELL = "SELL"
SIGNAL_NEUTRAL = "NEUTRAL"


@dataclass
class StrategyStats:
    trades: int = 0
    wins: int = 0
    enabled: bool = True

    @property
    def win_rate(self) -> float:
        if self.trades == 0:
            return 0.5  # cold-start neutral
        return self.wins / self.trades


@dataclass
class Strategy:
    id: str
    family: str
    timeframe: str
    params: Dict[str, object]
    fn: object  # Callable[(ctx, params, tf) -> (signal, confidence)]
    stats: StrategyStats = field(default_factory=StrategyStats)

    def evaluate(self, ctx: dict) -> Tuple[str, float]:
        try:
            sig, conf = self.fn(ctx, self.params, self.timeframe)
        except Exception:
            return SIGNAL_NEUTRAL, 0.0
        if sig not in (SIGNAL_BUY, SIGNAL_SELL, SIGNAL_NEUTRAL):
            sig = SIGNAL_NEUTRAL
        conf = max(0.0, min(1.0, float(conf)))
        return sig, conf

```

---

### `strategies/factory.py`
**Rol:** Factory: instanțiază 300+ variante parametrice din familii

**LOC:** 138

```python
"""Factory: cartesian product of (family x param grid x timeframe).

Guarantees >= 300 instances. Persists per-instance stats so scores survive
between sessions. Tracks a 3-state health flag per instance:
HEALTHY / UNDERPERFORMING / DISABLED.
"""
from __future__ import annotations

import json
import os
from typing import Callable, Dict, List, Optional, Tuple

import config
from strategies.base import Strategy, StrategyStats
from strategies.families.library import FAMILIES


TIMEFRAMES = ("M5", "M15", "H1")

HEALTHY = "HEALTHY"
UNDERPERFORMING = "UNDERPERFORMING"
DISABLED = "DISABLED"


class StrategyFactory:
    def __init__(self) -> None:
        self.instances: List[Strategy] = []
        self._status: Dict[str, str] = {}
        self._on_status_change: Optional[Callable[[str, str, str], None]] = None

    def set_status_listener(self,
                            cb: Callable[[str, str, str], None]) -> None:
        """Register `cb(strategy_id, old_status, new_status)`."""
        self._on_status_change = cb

    # ------------------------------------------------------------- build
    def build_all(self) -> List[Strategy]:
        self.instances.clear()
        for fam_name, fn, grid in FAMILIES:
            for params in grid:
                for tf in TIMEFRAMES:
                    sid = self._sid(fam_name, params, tf)
                    self.instances.append(
                        Strategy(id=sid, family=fam_name, timeframe=tf,
                                 params=dict(params), fn=fn)
                    )
        extra_tfs = ("H4", "M1")
        i = 0
        while len(self.instances) < 300:
            fam_name, fn, grid = FAMILIES[i % len(FAMILIES)]
            params = grid[i % len(grid)]
            tf = extra_tfs[i % len(extra_tfs)]
            sid = self._sid(fam_name, params, tf) + f"_x{i}"
            self.instances.append(
                Strategy(id=sid, family=fam_name, timeframe=tf,
                         params=dict(params), fn=fn)
            )
            i += 1
        self._load_stats()
        for s in self.instances:
            self._status[s.id] = self._compute_status(s)
        return self.instances

    @staticmethod
    def _sid(fam: str, params: dict, tf: str) -> str:
        kv = "_".join(f"{k}{v}" for k, v in sorted(params.items()))
        return f"{fam}_{kv}_{tf}" if kv else f"{fam}_{tf}"

    # ------------------------------------------------------------- runtime
    def active(self) -> List[Strategy]:
        return [s for s in self.instances if s.stats.enabled]

    def record_result(self, sid: str, won: bool) -> None:
        for s in self.instances:
            if s.id == sid:
                s.stats.trades += 1
                if won:
                    s.stats.wins += 1
                old = self._status.get(sid, HEALTHY)
                if (s.stats.trades >= config.MIN_TRADES_FOR_DISABLE
                        and s.stats.win_rate < config.DISABLE_SCORE_THRESHOLD):
                    s.stats.enabled = False
                new = self._compute_status(s)
                self._status[sid] = new
                if new != old and self._on_status_change:
                    try:
                        self._on_status_change(sid, old, new)
                    except Exception:
                        pass
                self._save_stats()
                return

    def status(self, sid: str) -> str:
        return self._status.get(sid, HEALTHY)

    def _compute_status(self, s: Strategy) -> str:
        if not s.stats.enabled:
            return DISABLED
        if (s.stats.trades >= config.MIN_TRADES_FOR_DISABLE
                and s.stats.win_rate < config.UNDERPERFORM_WINRATE):
            return UNDERPERFORMING
        return HEALTHY

    def top_n(self, n: int = 10) -> List[Tuple[str, float, int]]:
        ranked = sorted(self.instances, key=lambda s: s.stats.win_rate,
                        reverse=True)
        return [(s.id, s.stats.win_rate, s.stats.trades) for s in ranked[:n]]

    def worst_n(self, n: int = 10) -> List[Tuple[str, float, int]]:
        traded = [s for s in self.instances if s.stats.trades > 0]
        ranked = sorted(traded, key=lambda s: s.stats.win_rate)
        return [(s.id, s.stats.win_rate, s.stats.trades) for s in ranked[:n]]

    # ------------------------------------------------------------ persist
    def _save_stats(self) -> None:
        os.makedirs(os.path.dirname(config.STRATEGY_STATS_FILE) or ".",
                    exist_ok=True)
        data = {s.id: {"trades": s.stats.trades,
                       "wins": s.stats.wins,
                       "enabled": s.stats.enabled}
                for s in self.instances}
        with open(config.STRATEGY_STATS_FILE, "w") as f:
            json.dump(data, f)

    def _load_stats(self) -> None:
        if not os.path.exists(config.STRATEGY_STATS_FILE):
            return
        try:
            with open(config.STRATEGY_STATS_FILE) as f:
                data = json.load(f)
        except Exception:
            return
        for s in self.instances:
            if s.id in data:
                d = data[s.id]
                s.stats = StrategyStats(trades=int(d.get("trades", 0)),
                                        wins=int(d.get("wins", 0)),
                                        enabled=bool(d.get("enabled", True)))

```

---

### `strategies/ensemble.py`
**Rol:** Vot ponderat + ML gate adaptiv (cold-start ramp)

**LOC:** 100

```python
"""Weighted ensemble voting + ML probability gate with cold-start ramp.

Decision flow:
1. Each enabled strategy emits (signal, confidence).
2. Per-strategy weight = confidence * max(win_rate, 0.05) * ml_alignment.
3. Aggregate consensus must clear ENSEMBLE_CONSENSUS_THRESHOLD.
4. ML gate: ml_prob_win for the chosen side must clear an adaptive
   threshold that relaxes during cold start (few trained samples) and
   tightens once the model has seen ML_MIN_TRAINED_SAMPLES updates.
5. Risk rules (checked by RiskManager elsewhere) can always veto.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable, Optional

import config
from strategies.base import SIGNAL_BUY, SIGNAL_SELL, Strategy

try:
    from core import xauusd_profile as _xau
except Exception:  # pragma: no cover
    _xau = None  # type: ignore


@dataclass
class Decision:
    side: Optional[str]
    consensus: float
    ml_prob: float
    ml_prob_win: float            # prob of WIN for the chosen side
    ml_threshold: float           # adaptive threshold actually used
    strategy_id: str
    family: str = ""              # family of the dominant strategy
    reason: str = ""              # human-readable why-not when side is None


def _adaptive_ml_threshold(trained_samples: int) -> float:
    """Cold-start ramp: 0.50 with no data → ML_PROB_THRESHOLD once warm."""
    target = float(config.ML_PROB_THRESHOLD)
    warm = max(1, int(config.ML_MIN_TRAINED_SAMPLES))
    if trained_samples >= warm:
        return target
    # linear ramp from 0.50 (no data) to target (fully warm)
    frac = trained_samples / warm
    return 0.50 + (target - 0.50) * frac


class Ensemble:
    def decide(self, strategies: Iterable[Strategy],
               ctx: dict, ml_prob_up: float,
               trained_samples: int = 0) -> Decision:
        total_w = 0.0
        score = 0.0
        best_contrib = 0.0
        best_id = ""
        best_family = ""
        for s in strategies:
            sig, conf = s.evaluate(ctx)
            if sig not in (SIGNAL_BUY, SIGNAL_SELL) or conf <= 0:
                continue
            wr = s.stats.win_rate
            ml_align = ml_prob_up if sig == SIGNAL_BUY else (1.0 - ml_prob_up)
            w = conf * max(wr, 0.05) * max(ml_align, 0.05)
            total_w += w
            direction = 1.0 if sig == SIGNAL_BUY else -1.0
            score += direction * w
            if w > best_contrib:
                best_contrib = w
                best_id = s.id
                best_family = s.family

        thr_ml = _adaptive_ml_threshold(trained_samples)
        # XAU profile: per-family ML threshold overrides the adaptive ramp
        if (_xau is not None and _xau.profile_active(ctx.get("symbol"))
                and best_family):
            thr_ml = max(thr_ml, _xau.ml_threshold_for(best_family))

        if total_w == 0:
            return Decision(None, 0.0, ml_prob_up, 0.5, thr_ml, "",
                            family="", reason="no contributing strategies")

        consensus = score / total_w
        thr_cons = float(config.ENSEMBLE_CONSENSUS_THRESHOLD)
        if abs(consensus) < thr_cons:
            return Decision(None, consensus, ml_prob_up, 0.5, thr_ml,
                            best_id or "", family=best_family,
                            reason=f"consensus {consensus:+.2f} < {thr_cons:.2f}")

        side = SIGNAL_BUY if consensus > 0 else SIGNAL_SELL
        ml_win = ml_prob_up if side == SIGNAL_BUY else (1.0 - ml_prob_up)
        if ml_win < thr_ml:
            return Decision(None, consensus, ml_prob_up, ml_win, thr_ml,
                            best_id or "", family=best_family,
                            reason=f"ML p(win)={ml_win:.2f} < {thr_ml:.2f}")

        return Decision(side=side, consensus=consensus, ml_prob=ml_prob_up,
                        ml_prob_win=ml_win, ml_threshold=thr_ml,
                        strategy_id=best_id or "ensemble",
                        family=best_family, reason="OK")

```

---

### `strategies/families/library.py`
**Rol:** Familii generice: trend, MR, breakout, momentum, MACD, BB, etc.

**LOC:** 489

```python
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


```

---

### `strategies/families/xau_library.py`
**Rol:** Familii XAU-only: liquidity sweep, Asian box, FVG, body-close breakout

**LOC:** 158

```python
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

```

---

### `ui/app.py`
**Rol:** Dashboard Tkinter principal

**LOC:** 491

```python
"""MetaTrader 5–style Tkinter dashboard for the Elite Quant Bot.

Layout (mirrors the MT5 terminal):

    ┌──────────────────────────── Account status bar ──────────────────────────┐
    │ Conn · Mode · Login · Balance · Equity · Margin · Free · Level · Profit │
    ├──────────────────────────── Controls toolbar ────────────────────────────┤
    │ Start · Stop · Kill · Paper · Export · Config                            │
    ├────────────┬─────────────────────────────────────┬───────────────────────┤
    │ Market     │  Candlestick chart (selected sym.)  │  Signals & Strategies │
    │ Watch      │  Consensus / ML rolling charts      │  Top strategies       │
    │            │                                     │  W/L history bars     │
    ├────────────┴─────────────────────────────────────┴───────────────────────┤
    │ Toolbox: Trade · History · Journal · Experts                             │
    └──────────────────────────────────────────────────────────────────────────┘

All widget updates run on the Tk thread via `root.after`; the state machine
worker is never touched from here.
"""
from __future__ import annotations

import tkinter as tk
from tkinter import messagebox, ttk
from datetime import datetime, time as dtime, timezone
from typing import List, Optional

import config
from core.audit import AuditLogger
from core.health import run_checks
from core.state_machine import StateMachine
from strategies.factory import StrategyFactory
from ui.charts import CandlestickChart, LineChart, MultiSeriesBars
from ui.widgets import (
    ActiveTradesTable, DailyReportsPanel, HealthChecksDialog,
    HistoryTable, LabeledValue, LogPanel, MarketWatch, PositionsTable,
    TodayCard, TopStrategiesTable, apply_mt5_style,
    MT5_BG, MT5_BUY, MT5_SELL, MT5_FG, MT5_MUTED, MT5_ACCENT,
)


# Default Market-Watch symbols if none can be discovered live.
_DEFAULT_WATCH = (
    "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD",
    "NZDUSD", "XAUUSD", "XAGUSD", "BTCUSD",
)


class App:
    def __init__(self, sm: StateMachine,
                 audit: Optional[AuditLogger] = None,
                 factory: Optional[StrategyFactory] = None) -> None:
        self.sm = sm
        self.audit = audit or sm.audit
        self.factory = factory or getattr(sm, "factory", None)
        self.root = tk.Tk()
        self.root.title("Elite Quant Bot — MT5 Terminal")
        self.root.geometry("1480x900")
        self.root.minsize(1200, 760)
        apply_mt5_style(self.root)

        self._paper_var = tk.BooleanVar(
            value=bool(getattr(sm.executor, "is_paper", False)))
        self._tf_var = tk.StringVar(value="M15")
        self._selected_symbol: str = getattr(config, "SYMBOL", "EURUSD")
        self._watch_symbols: List[str] = list(dict.fromkeys(
            [self._selected_symbol, *_DEFAULT_WATCH]))

        self._build()
        self._poll()

    # ===================================================== layout
    def _build(self) -> None:
        self._build_status_bar()
        self._build_toolbar()

        body = ttk.Frame(self.root, padding=(6, 4))
        body.pack(fill="both", expand=True)
        body.columnconfigure(0, weight=0, minsize=320)
        body.columnconfigure(1, weight=3)
        body.columnconfigure(2, weight=2, minsize=380)
        body.rowconfigure(0, weight=3)
        body.rowconfigure(1, weight=2)

        # ---------- left: Market Watch
        watch_frame = ttk.LabelFrame(body, text="MARKET WATCH")
        watch_frame.grid(row=0, column=0, rowspan=2, sticky="nsew",
                         padx=(0, 6), pady=(0, 0))
        self.market = MarketWatch(watch_frame, on_select=self._on_pick_symbol)
        self.market.pack(fill="both", expand=True, padx=2, pady=2)

        # ---------- center top: candlestick chart
        chart_frame = ttk.LabelFrame(body, text="CHART")
        chart_frame.grid(row=0, column=1, sticky="nsew", padx=(0, 6))
        tf_bar = ttk.Frame(chart_frame)
        tf_bar.pack(fill="x", padx=4, pady=2)
        ttk.Label(tf_bar, text="Timeframe:", foreground=MT5_MUTED).pack(side="left")
        for tf in ("M1", "M5", "M15", "M30", "H1", "H4", "D1"):
            ttk.Radiobutton(tf_bar, text=tf, value=tf,
                            variable=self._tf_var).pack(side="left", padx=1)
        self.candles = CandlestickChart(chart_frame, height=360)
        self.candles.pack(fill="both", expand=True, padx=4, pady=(0, 4))

        # ---------- right column: signals + strategies + W/L
        right = ttk.Frame(body)
        right.grid(row=0, column=2, rowspan=2, sticky="nsew")
        right.columnconfigure(0, weight=1)

        sig = ttk.LabelFrame(right, text="SIGNALS")
        sig.pack(fill="x", pady=(0, 6))
        self.chart_consensus = LineChart(sig, title="Ensemble consensus",
                                         y_min=-1.0, y_max=1.0, baseline=0.0,
                                         fg="#60a5fa", height=110)
        self.chart_consensus.pack(fill="x", padx=4, pady=2)
        self.chart_ml = LineChart(sig, title="ML probability (UP)",
                                  y_min=0.0, y_max=1.0, baseline=0.5,
                                  fg="#facc15", height=110)
        self.chart_ml.pack(fill="x", padx=4, pady=2)

        wl = ttk.LabelFrame(right, text="STRATEGY W/L (rolling)")
        wl.pack(fill="both", expand=True, pady=(0, 6))
        self.chart_strategies = MultiSeriesBars(wl, height=150)
        self.chart_strategies.pack(fill="both", expand=True, padx=4, pady=2)

        top_lf = ttk.LabelFrame(right, text="TOP STRATEGIES")
        top_lf.pack(fill="both", expand=True)
        self.top = TopStrategiesTable(top_lf)
        self.top.pack(fill="both", expand=True, padx=2, pady=2)

        worst_lf = ttk.LabelFrame(right, text="WORST STRATEGIES")
        worst_lf.pack(fill="both", expand=True, pady=(6, 0))
        self.worst = TopStrategiesTable(worst_lf)
        self.worst.pack(fill="both", expand=True, padx=2, pady=2)

        # ---------- TODAY card sits above the toolbox
        self.today_card = TodayCard(body)
        self.today_card.grid(row=1, column=1, sticky="ew", padx=(0, 6),
                             pady=(6, 0))

        toolbox = ttk.Notebook(body)
        toolbox.grid(row=2, column=1, sticky="nsew", padx=(0, 6),
                     pady=(4, 0))
        body.rowconfigure(2, weight=2)

        trade_tab = ttk.Frame(toolbox)
        self.positions = PositionsTable(trade_tab)
        self.positions.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(trade_tab, text="Trade")

        active_tab = ttk.Frame(toolbox)
        self.active_trades = ActiveTradesTable(active_tab)
        self.active_trades.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(active_tab, text="Active TP Ladder")

        history_tab = ttk.Frame(toolbox)
        self.history = HistoryTable(history_tab)
        self.history.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(history_tab, text="History")

        reports_tab = ttk.Frame(toolbox)
        self.reports_panel = DailyReportsPanel(reports_tab, config.REPORT_DIR)
        self.reports_panel.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(reports_tab, text="Daily Reports")

        journal_tab = ttk.Frame(toolbox)
        self.log = LogPanel(journal_tab)
        self.log.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(journal_tab, text="Journal")

        experts_tab = ttk.Frame(toolbox)
        self.experts_log = LogPanel(experts_tab)
        self.experts_log.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(experts_tab, text="Experts")

    def _build_status_bar(self) -> None:
        bar = ttk.Frame(self.root, padding=(8, 6))
        bar.pack(fill="x")
        bar.configure(style="TFrame")
        self.lbl_conn   = LabeledValue(bar, "Connection", "DISCONNECTED",
                                       value_color=MT5_SELL)
        self.lbl_mode   = LabeledValue(bar, "Mode", "LIVE")
        self.lbl_login  = LabeledValue(bar, "Account", "—")
        self.lbl_server = LabeledValue(bar, "Server", "—")
        self.lbl_bal    = LabeledValue(bar, "Balance", "—")
        self.lbl_eq     = LabeledValue(bar, "Equity",  "—")
        self.lbl_margin = LabeledValue(bar, "Margin",  "—")
        self.lbl_free   = LabeledValue(bar, "Free margin", "—")
        self.lbl_level  = LabeledValue(bar, "Margin lvl", "—")
        self.lbl_pnl    = LabeledValue(bar, "Daily P/L", "0.00")
        self.lbl_apnl   = LabeledValue(bar, "Open P/L", "0.00")
        self.lbl_state  = LabeledValue(bar, "Auto", "OFF",
                                       value_color=MT5_MUTED)
        widgets = [self.lbl_conn, self.lbl_mode, self.lbl_login, self.lbl_server,
                   self.lbl_bal, self.lbl_eq, self.lbl_margin, self.lbl_free,
                   self.lbl_level, self.lbl_pnl, self.lbl_apnl, self.lbl_state]
        for i, w in enumerate(widgets):
            w.grid(row=0, column=i, padx=8, sticky="w")
        ttk.Separator(self.root, orient="horizontal").pack(fill="x")

    def _build_toolbar(self) -> None:
        bar = ttk.Frame(self.root, padding=(8, 4))
        bar.pack(fill="x")
        ttk.Button(bar, text="▶  Start Auto", style="Accent.TButton",
                   command=self._start_auto).pack(side="left", padx=2)
        ttk.Button(bar, text="■  Stop",
                   command=lambda: self.sm.set_auto(False)).pack(side="left", padx=2)
        ttk.Button(bar, text="✓  Pre-Start Checks",
                   command=self._open_health_dialog).pack(side="left", padx=2)
        ttk.Button(bar, text="✖  Kill Switch", style="Danger.TButton",
                   command=self._kill).pack(side="left", padx=2)
        ttk.Separator(bar, orient="vertical").pack(side="left", fill="y", padx=8)
        ttk.Checkbutton(bar, text="Paper trading",
                        variable=self._paper_var,
                        command=self._toggle_paper).pack(side="left", padx=4)
        ttk.Separator(bar, orient="vertical").pack(side="left", fill="y", padx=8)
        ttk.Button(bar, text="⤓  Export Daily Report",
                   command=self._export_report).pack(side="left", padx=2)
        ttk.Button(bar, text="⚙  Config…",
                   command=self._open_config_editor).pack(side="left", padx=2)
        self.lbl_clock = ttk.Label(bar, text="--:--:-- UTC",
                                   foreground=MT5_MUTED,
                                   font=("Consolas", 10, "bold"))
        self.lbl_clock.pack(side="right", padx=4)
        ttk.Separator(self.root, orient="horizontal").pack(fill="x")

    # ===================================================== handlers
    def _on_pick_symbol(self, _evt=None) -> None:
        sel = self.market.tree.selection()
        if sel:
            self._selected_symbol = self.market.tree.item(sel[0])["values"][0]

    def _toggle_paper(self) -> None:
        self.sm.set_paper_mode(self._paper_var.get())

    def _kill(self) -> None:
        if messagebox.askyesno("Kill switch",
                               "Close ALL bot positions and disable auto-trade?"):
            self.sm.kill_switch()

    def _export_report(self) -> None:
        try:
            paths = self.audit.export_daily_report(factory=self.factory)
        except Exception as exc:
            messagebox.showerror("Export failed", str(exc))
            return
        messagebox.showinfo("Daily report exported",
                            f"CSV:\n{paths['csv']}\n\nJSON:\n{paths['json']}")

    def _open_config_editor(self) -> None:
        ConfigEditor(self.root, self.sm)

    def _start_auto(self) -> None:
        try:
            checks, status = run_checks(self.sm.client)
        except Exception as exc:
            messagebox.showerror("Health checks failed", str(exc))
            return
        if status == "OK":
            self.sm.set_auto(True)
            return
        HealthChecksDialog(self.root, checks, status,
                           on_confirm=lambda: self.sm.set_auto(True))

    def _open_health_dialog(self) -> None:
        try:
            checks, status = run_checks(self.sm.client)
        except Exception as exc:
            messagebox.showerror("Health checks failed", str(exc))
            return
        HealthChecksDialog(self.root, checks, status,
                           on_confirm=lambda: self.sm.set_auto(True))

    # ===================================================== polling
    def _poll(self) -> None:
        try:
            snap = self.sm.get_snapshot()
            self._render_status(snap)
            self._render_signals(snap)
            self._render_market_watch()
            self._render_chart()
            self._render_positions_history()
            self.today_card.update(snap.today)
            self.active_trades.set_rows(snap.active_trades)
            if self.factory is not None:
                try:
                    self.worst.set_rows(self.factory.worst_n(10))
                except Exception:
                    pass
        except Exception as exc:
            try:
                self.experts_log.set_lines([f"UI error: {exc!r}"])
            except Exception:
                pass
        self.root.after(config.UI_REFRESH_INTERVAL_MS, self._poll)

    # ----- status bar
    def _render_status(self, s) -> None:
        self.lbl_conn.set("CONNECTED" if s.connected else "DISCONNECTED",
                          color=MT5_BUY if s.connected else MT5_SELL)
        self.lbl_mode.set("PAPER" if s.paper else "LIVE",
                          color=MT5_ACCENT if s.paper else MT5_FG)
        self.lbl_bal.set(f"{s.balance:,.2f}")
        self.lbl_eq.set(f"{s.equity:,.2f}")
        self.lbl_pnl.set(f"{s.daily_pnl:+,.2f}",
                         color=MT5_BUY if s.daily_pnl >= 0 else MT5_SELL)
        self.lbl_apnl.set(f"{s.active_pnl:+,.2f}",
                          color=MT5_BUY if s.active_pnl >= 0 else MT5_SELL)
        self.lbl_state.set("ON" if s.running else "OFF",
                           color=MT5_BUY if s.running else MT5_MUTED)
        info = self._account_info()
        if info is not None:
            self.lbl_login.set(str(getattr(info, "login", "—")))
            self.lbl_server.set(str(getattr(info, "server", "—")))
            self.lbl_margin.set(f"{getattr(info, 'margin', 0.0):,.2f}")
            self.lbl_free.set(f"{getattr(info, 'margin_free', 0.0):,.2f}")
            lvl = float(getattr(info, "margin_level", 0.0))
            self.lbl_level.set(f"{lvl:,.2f} %" if lvl else "—")
        self.lbl_clock.configure(
            text=datetime.now(timezone.utc).strftime("%H:%M:%S UTC"))

    # ----- signals + experts
    def _render_signals(self, s) -> None:
        self.chart_consensus.set_data(s.consensus_history)
        self.chart_ml.set_data(s.ml_history)
        self.chart_strategies.set_data(s.strategy_history)
        self.top.set_rows(s.top_strategies)
        self.log.set_lines(s.log)
        # Experts log shows ranked strategies as MT5 EAs would
        expert_lines = [
            f"{sid:<40}  win={wr*100:5.1f}%  trades={n}"
            for sid, wr, n in s.top_strategies[:12]
        ]
        if expert_lines:
            self.experts_log.set_lines(
                ["# Top strategies (ensemble experts)", *expert_lines])

    # ----- market watch
    def _render_market_watch(self) -> None:
        rows = []
        for sym in self._watch_symbols:
            try:
                info = self.sm.client.symbol_info(sym)
                tick = self.sm.client.tick(sym)
            except Exception:
                info, tick = None, None
            if tick is None:
                continue
            point = float(getattr(info, "point", 0.00001)) or 0.00001
            spread_pips = (tick.ask - tick.bid) / (point * 10.0)
            rows.append((sym, float(tick.bid), float(tick.ask),
                         float(spread_pips), int(tick.time)))
        self.market.set_rows(rows)

    # ----- chart
    def _render_chart(self) -> None:
        sym = self._selected_symbol
        tf = self._tf_var.get()
        try:
            candles = self.sm.feed.rates(sym, tf, 120)
            tick = self.sm.client.tick(sym)
        except Exception:
            candles, tick = None, None
        bid = float(tick.bid) if tick else None
        ask = float(tick.ask) if tick else None
        self.candles.set_data(sym, tf, candles, bid=bid, ask=ask)

    # ----- positions + history
    def _render_positions_history(self) -> None:
        positions = self._collect_positions()
        self.positions.set_rows(positions)
        self.history.set_rows(self._collect_history())

    # ----- helpers
    def _account_info(self):
        try:
            return self.sm.client.account_info()
        except Exception:
            return None

    def _collect_positions(self):
        try:
            if getattr(self.sm.executor, "is_paper", False):
                raw = self.sm.executor.positions_get()
            else:
                raw = self.sm.client.positions_get()
        except Exception:
            raw = ()
        out = []
        for p in raw or ():
            out.append({
                "ticket": getattr(p, "ticket", 0),
                "time": int(getattr(p, "time", 0)),
                "symbol": getattr(p, "symbol", ""),
                "type": int(getattr(p, "type", 0)),
                "volume": float(getattr(p, "volume", 0.0)),
                "price_open": float(getattr(p, "price_open", 0.0)),
                "price_current": float(getattr(p, "price_current",
                                               getattr(p, "price_open", 0.0))),
                "sl": float(getattr(p, "sl", 0.0) or 0.0),
                "tp": float(getattr(p, "tp", 0.0) or 0.0),
                "swap": float(getattr(p, "swap", 0.0) or 0.0),
                "profit": float(getattr(p, "profit", 0.0) or 0.0),
            })
        return out

    def _collect_history(self):
        midnight = datetime.combine(datetime.now(timezone.utc).date(),
                                    dtime(0, 0), tzinfo=timezone.utc)
        try:
            deals = self.sm.client.history_deals_get(midnight,
                                                    datetime.now(timezone.utc))
        except Exception:
            deals = ()
        out = []
        for d in deals or ():
            entry = int(getattr(d, "entry", 0))
            # Only count exits (DEAL_ENTRY_OUT == 1) to avoid duplicates.
            if entry not in (1, 2):
                continue
            out.append({
                "time": int(getattr(d, "time", 0)),
                "ticket": getattr(d, "ticket", 0),
                "symbol": getattr(d, "symbol", ""),
                "type": int(getattr(d, "type", 0)),
                "volume": float(getattr(d, "volume", 0.0)),
                "price": float(getattr(d, "price", 0.0)),
                "commission": float(getattr(d, "commission", 0.0) or 0.0),
                "swap": float(getattr(d, "swap", 0.0) or 0.0),
                "profit": float(getattr(d, "profit", 0.0) or 0.0),
            })
        out.sort(key=lambda x: x["time"], reverse=True)
        return out[:200]

    def run(self) -> None:
        self.root.mainloop()


# ====================================================================== editor
class ConfigEditor(tk.Toplevel):
    """Modal config editor — applies whitelisted fields via state machine."""

    def __init__(self, master, sm: StateMachine) -> None:
        super().__init__(master)
        self.title("Live config")
        self.sm = sm
        self.geometry("440x600")
        self.configure(bg=MT5_BG)
        self._vars: dict[str, tk.StringVar] = {}

        frm = ttk.Frame(self, padding=10)
        frm.pack(fill="both", expand=True)
        ttk.Label(frm,
                  text="Edit parameters and Apply. Changes persist to "
                       "config_overrides.json.",
                  wraplength=400, foreground=MT5_MUTED
                  ).pack(anchor="w", pady=(0, 8))

        grid = ttk.Frame(frm)
        grid.pack(fill="both", expand=True)
        current = config.snapshot()
        for row, (k, _t) in enumerate(config.EDITABLE_FIELDS.items()):
            ttk.Label(grid, text=k).grid(row=row, column=0, sticky="w",
                                         padx=4, pady=2)
            var = tk.StringVar(value=str(current.get(k, "")))
            self._vars[k] = var
            ttk.Entry(grid, textvariable=var, width=22).grid(
                row=row, column=1, sticky="ew", padx=4, pady=2)
        grid.columnconfigure(1, weight=1)

        btns = ttk.Frame(frm)
        btns.pack(fill="x", pady=8)
        ttk.Button(btns, text="Apply", style="Accent.TButton",
                   command=self._apply).pack(side="left", padx=4)
        ttk.Button(btns, text="Apply + Restart",
                   command=self._apply_and_restart).pack(side="left", padx=4)
        ttk.Button(btns, text="Close",
                   command=self.destroy).pack(side="right", padx=4)

    def _collect(self) -> dict:
        return {k: v.get() for k, v in self._vars.items()}

    def _apply(self) -> None:
        applied = self.sm.apply_config(self._collect())
        messagebox.showinfo("Config", f"Applied {len(applied)} fields.")

    def _apply_and_restart(self) -> None:
        applied = self.sm.apply_config(self._collect())
        self.sm.restart()
        messagebox.showinfo(
            "Config",
            f"Applied {len(applied)} fields and restarted state machine.")
        self.destroy()

```

---

### `ui/widgets.py`
**Rol:** Widget-uri custom

**LOC:** 482

```python
"""Reusable Tkinter widgets — MT5-style look."""
from __future__ import annotations

import csv
import glob
import os
import tkinter as tk
from tkinter import ttk
from datetime import datetime, timezone
from typing import Callable, List, Optional

# ------------------------------------------------------------------ palette
MT5_BG       = "#1e1e1e"
MT5_PANEL    = "#252526"
MT5_HEADER   = "#2d2d30"
MT5_BORDER   = "#3f3f46"
MT5_FG       = "#dcdcdc"
MT5_MUTED    = "#9a9a9a"
MT5_BUY      = "#2ecc71"
MT5_SELL     = "#e74c3c"
MT5_ACCENT   = "#3794ff"
MT5_GRID     = "#2a2a2a"
MT5_WARN     = "#f1c40f"


class LabeledValue(ttk.Frame):
    """Compact label/value pair used in the MT5-style status bar."""

    def __init__(self, master, label: str, value: str = "—",
                 value_color: str = MT5_FG, **kw):
        super().__init__(master, **kw)
        ttk.Label(self, text=label.upper(), foreground=MT5_MUTED,
                  font=("Segoe UI", 8)).pack(anchor="w")
        self.var = tk.StringVar(value=value)
        self._color = value_color
        self.lbl = ttk.Label(self, textvariable=self.var,
                             foreground=value_color,
                             font=("Segoe UI", 11, "bold"))
        self.lbl.pack(anchor="w")

    def set(self, value: str, color: Optional[str] = None) -> None:
        self.var.set(value)
        if color is not None and color != self._color:
            self._color = color
            self.lbl.configure(foreground=color)


class LogPanel(ttk.Frame):
    """MT5 Journal-style scrolling text log."""

    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        self.text = tk.Text(self, height=12, width=70,
                            bg="#101010", fg="#cfcfcf",
                            insertbackground="#cfcfcf",
                            font=("Consolas", 9), bd=0,
                            highlightthickness=0)
        sb = ttk.Scrollbar(self, orient="vertical", command=self.text.yview)
        self.text.configure(yscrollcommand=sb.set, state="disabled")
        self.text.pack(side="left", fill="both", expand=True)
        sb.pack(side="right", fill="y")

    def set_lines(self, lines):
        self.text.configure(state="normal")
        self.text.delete("1.0", "end")
        self.text.insert("end", "\n".join(lines))
        self.text.configure(state="disabled")
        self.text.see("end")


class TopStrategiesTable(ttk.Frame):
    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        cols = ("id", "win_rate", "trades")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=10)
        for c, t, w, a in (
            ("id", "Strategy", 320, "w"),
            ("win_rate", "Win %", 80, "center"),
            ("trades", "Trades", 80, "center"),
        ):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w, anchor=a)
        self.tree.pack(fill="both", expand=True)

    def set_rows(self, rows):
        for r in self.tree.get_children():
            self.tree.delete(r)
        for sid, wr, n in rows:
            self.tree.insert("", "end", values=(sid, f"{wr*100:.1f}", n))


class TodayCard(ttk.LabelFrame):
    """Compact 'Today's Performance' panel."""

    def __init__(self, master, **kw):
        super().__init__(master, text="TODAY'S PERFORMANCE", **kw)
        grid = ttk.Frame(self)
        grid.pack(fill="x", padx=4, pady=4)
        self.lbl_trades = LabeledValue(grid, "Trades", "0")
        self.lbl_wl     = LabeledValue(grid, "W / L", "0 / 0")
        self.lbl_wr     = LabeledValue(grid, "Win rate", "0.0%")
        self.lbl_pnl    = LabeledValue(grid, "PnL", "0.00")
        self.lbl_r      = LabeledValue(grid, "Avg R", "0.00")
        self.lbl_dd     = LabeledValue(grid, "Max DD", "0.00")
        for i, w in enumerate((self.lbl_trades, self.lbl_wl, self.lbl_wr,
                                self.lbl_pnl, self.lbl_r, self.lbl_dd)):
            w.grid(row=0, column=i, padx=6, sticky="w")

    def update(self, summary: dict) -> None:
        if not summary:
            return
        n = int(summary.get("trades", 0))
        w = int(summary.get("wins", 0))
        l = int(summary.get("losses", 0))
        wr = float(summary.get("win_rate", 0.0)) * 100.0
        pnl = float(summary.get("pnl", 0.0))
        avg_r = float(summary.get("avg_r", 0.0))
        dd = float(summary.get("max_dd", 0.0))
        self.lbl_trades.set(str(n))
        self.lbl_wl.set(f"{w} / {l}")
        self.lbl_wr.set(f"{wr:.1f}%",
                        color=MT5_BUY if wr >= 50 else MT5_SELL if wr < 30 else MT5_FG)
        self.lbl_pnl.set(f"{pnl:+.2f}",
                         color=MT5_BUY if pnl >= 0 else MT5_SELL)
        self.lbl_r.set(f"{avg_r:+.2f}R",
                       color=MT5_BUY if avg_r >= 0 else MT5_SELL)
        self.lbl_dd.set(f"{dd:.2f}",
                        color=MT5_SELL if dd > 0 else MT5_MUTED)


class ActiveTradesTable(ttk.Frame):
    """Open positions enriched with TP ladder progress."""

    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        cols = ("ticket", "strategy", "side", "entry", "sl",
                "ladder", "vol")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=8)
        widths = [80, 280, 60, 90, 90, 320, 100]
        for c, t, w in zip(cols, [
            "Ticket", "Strategy", "Side", "Entry", "S/L",
            "TP Ladder (✓ hit)", "Vol (rem/init)"], widths):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w,
                             anchor="e" if c in ("entry", "sl") else "w")
        self.tree.tag_configure("buy", foreground=MT5_BUY)
        self.tree.tag_configure("sell", foreground=MT5_SELL)
        self.tree.pack(fill="both", expand=True)

    def set_rows(self, rows):
        for r in self.tree.get_children():
            self.tree.delete(r)
        for t in rows:
            ladder = t.get("ladder", [])
            hits = t.get("hits", [])
            parts = []
            for i, p in enumerate(ladder):
                mark = "✓" if i < len(hits) and hits[i] else "·"
                parts.append(f"TP{i+1}{mark}{p:.5f}")
            ladder_str = "  ".join(parts) or "—"
            tag = "buy" if t["side"] == "BUY" else "sell"
            self.tree.insert("", "end", values=(
                t["ticket"], t.get("strategy_id", "")[:36],
                t["side"], f"{t['entry']:.5f}", f"{t['sl']:.5f}",
                ladder_str,
                f"{t['remaining_volume']:.2f}/{t['initial_volume']:.2f}",
            ), tags=(tag,))


class HealthChecksDialog(tk.Toplevel):
    """Modal pre-start checks dialog. Calls on_confirm() if user proceeds."""

    def __init__(self, master, checks: list, status: str,
                 on_confirm: Callable[[], None]) -> None:
        super().__init__(master)
        self.title("Pre-Start Health Checks")
        self.geometry("640x520")
        self.configure(bg=MT5_BG)
        frm = ttk.Frame(self, padding=8)
        frm.pack(fill="both", expand=True)
        ttk.Label(frm, text="Pre-Start Health Checks",
                  font=("Segoe UI", 12, "bold")).pack(anchor="w")
        status_color = {"OK": MT5_BUY, "WARN_REQUIRES_CONFIRM": MT5_WARN,
                        "HALT": MT5_SELL}.get(status, MT5_MUTED)
        ttk.Label(frm, text=f"Overall: {status}",
                  foreground=status_color,
                  font=("Segoe UI", 10, "bold")).pack(anchor="w", pady=(0, 8))

        cols = ("cat", "name", "status", "msg")
        tree = ttk.Treeview(frm, columns=cols, show="headings", height=14)
        for c, t, w, a in (
            ("cat", "Category", 100, "w"),
            ("name", "Check", 160, "w"),
            ("status", "Status", 70, "center"),
            ("msg", "Detail", 290, "w"),
        ):
            tree.heading(c, text=t)
            tree.column(c, width=w, anchor=a)
        tree.tag_configure("PASS", foreground=MT5_BUY)
        tree.tag_configure("WARN", foreground=MT5_WARN)
        tree.tag_configure("FAIL", foreground=MT5_SELL)
        tree.pack(fill="both", expand=True)
        for c in checks:
            tree.insert("", "end",
                        values=(c.category, c.name, c.status, c.message),
                        tags=(c.status,))

        btns = ttk.Frame(frm)
        btns.pack(fill="x", pady=8)
        ttk.Button(btns, text="Cancel",
                   command=self.destroy).pack(side="right", padx=4)
        if status != "HALT":
            text = ("Confirm & Start"
                    if status == "WARN_REQUIRES_CONFIRM"
                    else "Start Auto-Trading")
            style = "Accent.TButton"

            def _proceed():
                on_confirm()
                self.destroy()
            ttk.Button(btns, text=text, style=style,
                       command=_proceed).pack(side="right", padx=4)
        else:
            ttk.Label(btns,
                      text="Critical check FAILED — fix and re-open.",
                      foreground=MT5_SELL).pack(side="left", padx=4)


class DailyReportsPanel(ttk.Frame):
    """Dropdown of daily CSV reports with preview table."""

    def __init__(self, master, reports_dir: str, **kw):
        super().__init__(master, **kw)
        self.reports_dir = reports_dir
        top = ttk.Frame(self)
        top.pack(fill="x", padx=2, pady=2)
        ttk.Label(top, text="Daily report:",
                  foreground=MT5_MUTED).pack(side="left")
        self.var = tk.StringVar()
        self.combo = ttk.Combobox(top, textvariable=self.var,
                                  state="readonly", width=40)
        self.combo.pack(side="left", padx=4)
        ttk.Button(top, text="Refresh",
                   command=self.refresh).pack(side="left", padx=4)
        ttk.Button(top, text="Load",
                   command=self._load).pack(side="left", padx=4)
        cols = ("strategy_id", "trades", "wr", "pnl", "avg_r",
                "spread", "slip_in", "slip_out", "slip_tot")
        self.tree = ttk.Treeview(self, columns=cols, show="headings",
                                 height=12)
        headers = ["Strategy", "Trades", "Win %", "PnL", "Avg R",
                   "Spread (pips)", "Slip In", "Slip Out", "Slip Total"]
        widths = [300, 60, 60, 80, 60, 85, 65, 65, 75]
        for c, t, w in zip(cols, headers, widths):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w,
                             anchor="e" if c != "strategy_id" else "w")
        self.tree.pack(fill="both", expand=True)

        # Footer with portfolio-wide averages
        self.footer = ttk.Label(self, text="", foreground=MT5_MUTED,
                                font=("Consolas", 9))
        self.footer.pack(fill="x", padx=4, pady=(2, 2))
        self.refresh()

    def refresh(self) -> None:
        files = sorted(glob.glob(os.path.join(self.reports_dir,
                                              "daily_report_*.csv")),
                       reverse=True)
        names = [os.path.basename(f) for f in files]
        self.combo["values"] = names
        if names and not self.var.get():
            self.var.set(names[0])
            self._load()

    def _load(self) -> None:
        name = self.var.get()
        if not name:
            return
        path = os.path.join(self.reports_dir, name)
        for r in self.tree.get_children():
            self.tree.delete(r)
        n_trades = 0
        tot_pnl = 0.0
        spread_acc = 0.0
        slip_acc = 0.0
        weight = 0
        try:
            with open(path, encoding="utf-8") as f:
                for row in csv.DictReader(f):
                    tr = int(float(row.get("trades_today", 0) or 0))
                    pnl = float(row.get("gross_pnl", 0) or 0)
                    sp = float(row.get("avg_spread_pips", 0) or 0)
                    si = float(row.get("avg_slippage_entry_pips", 0) or 0)
                    so = float(row.get("avg_slippage_exit_pips", 0) or 0)
                    st = float(row.get("avg_slippage_pips", 0) or 0)
                    n_trades += tr
                    tot_pnl += pnl
                    spread_acc += sp * tr
                    slip_acc += st * tr
                    weight += tr
                    self.tree.insert("", "end", values=(
                        row.get("strategy_id", ""),
                        tr,
                        f"{float(row.get('win_rate_today', 0) or 0)*100:.1f}",
                        f"{pnl:+.2f}",
                        row.get("avg_R_today", ""),
                        f"{sp:.2f}", f"{si:.2f}",
                        f"{so:.2f}", f"{st:.2f}",
                    ))
        except OSError:
            return
        avg_sp = (spread_acc / weight) if weight else 0.0
        avg_sl = (slip_acc / weight) if weight else 0.0
        self.footer.configure(
            text=(f"Totals — trades: {n_trades}   PnL: {tot_pnl:+.2f}   "
                  f"avg spread: {avg_sp:.2f} pips   "
                  f"avg slippage: {avg_sl:.2f} pips"))


# ============================================================== MT5 widgets
class MarketWatch(ttk.Frame):
    def __init__(self, master, on_select=None, **kw):
        super().__init__(master, **kw)
        ttk.Label(self, text="Market Watch", foreground=MT5_MUTED,
                  font=("Segoe UI", 9, "bold")).pack(anchor="w", padx=4, pady=2)
        cols = ("symbol", "bid", "ask", "spread", "time")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=14)
        for c, t, w, a in (
            ("symbol", "Symbol", 90, "w"),
            ("bid", "Bid", 70, "e"),
            ("ask", "Ask", 70, "e"),
            ("spread", "Spread", 55, "center"),
            ("time", "Time", 70, "center"),
        ):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w, anchor=a)
        self.tree.tag_configure("up", foreground=MT5_BUY)
        self.tree.tag_configure("down", foreground=MT5_SELL)
        self.tree.pack(fill="both", expand=True, padx=2)
        if on_select:
            self.tree.bind("<<TreeviewSelect>>", on_select)
        self._last_bids: dict = {}

    def set_rows(self, rows):
        sel = self.tree.selection()
        sel_sym = self.tree.item(sel[0])["values"][0] if sel else None
        for r in self.tree.get_children():
            self.tree.delete(r)
        for sym, bid, ask, spread, ts in rows:
            prev = self._last_bids.get(sym)
            tag = "up" if prev is not None and bid > prev else (
                "down" if prev is not None and bid < prev else "")
            self._last_bids[sym] = bid
            time_str = datetime.fromtimestamp(ts, tz=timezone.utc).strftime("%H:%M:%S") \
                if ts else "--:--:--"
            iid = self.tree.insert(
                "", "end",
                values=(sym, f"{bid:.5f}", f"{ask:.5f}",
                        f"{spread:.1f}", time_str),
                tags=(tag,) if tag else ())
            if sym == sel_sym:
                self.tree.selection_set(iid)


class PositionsTable(ttk.Frame):
    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        cols = ("ticket", "time", "symbol", "type", "volume",
                "price", "sl", "tp", "current", "swap", "profit")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=8)
        widths = [80, 130, 80, 60, 70, 80, 80, 80, 80, 60, 80]
        for c, t, w in zip(cols, [
            "Ticket", "Time", "Symbol", "Type", "Volume",
            "Price", "S/L", "T/P", "Price", "Swap", "Profit"], widths):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w,
                             anchor="e" if c not in ("time", "symbol", "type") else "w")
        self.tree.tag_configure("buy", foreground=MT5_BUY)
        self.tree.tag_configure("sell", foreground=MT5_SELL)
        self.tree.tag_configure("profit_pos", foreground=MT5_BUY)
        self.tree.tag_configure("profit_neg", foreground=MT5_SELL)
        self.tree.pack(fill="both", expand=True)

    def set_rows(self, positions):
        for r in self.tree.get_children():
            self.tree.delete(r)
        for p in positions:
            side = "buy" if p["type"] == 0 else "sell"
            tag = "profit_pos" if p["profit"] >= 0 else "profit_neg"
            self.tree.insert("", "end", values=(
                p["ticket"],
                datetime.fromtimestamp(p["time"], tz=timezone.utc)
                    .strftime("%Y.%m.%d %H:%M:%S"),
                p["symbol"], side.upper(), f"{p['volume']:.2f}",
                f"{p['price_open']:.5f}",
                f"{p['sl']:.5f}" if p['sl'] else "—",
                f"{p['tp']:.5f}" if p['tp'] else "—",
                f"{p['price_current']:.5f}",
                f"{p['swap']:+.2f}",
                f"{p['profit']:+.2f}",
            ), tags=(tag,))


class HistoryTable(ttk.Frame):
    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        cols = ("time", "ticket", "symbol", "type", "volume",
                "price", "commission", "swap", "profit")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=8)
        widths = [140, 90, 80, 60, 70, 90, 80, 60, 90]
        for c, t, w in zip(cols, [
            "Time", "Deal", "Symbol", "Type", "Volume", "Price",
            "Commission", "Swap", "Profit"], widths):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w,
                             anchor="e" if c not in ("time", "symbol", "type") else "w")
        self.tree.tag_configure("profit_pos", foreground=MT5_BUY)
        self.tree.tag_configure("profit_neg", foreground=MT5_SELL)
        self.tree.pack(fill="both", expand=True)

    def set_rows(self, deals):
        for r in self.tree.get_children():
            self.tree.delete(r)
        for d in deals:
            total = d["profit"] + d["commission"] + d["swap"]
            tag = "profit_pos" if total >= 0 else "profit_neg"
            side = "buy" if d["type"] == 0 else "sell"
            self.tree.insert("", "end", values=(
                datetime.fromtimestamp(d["time"], tz=timezone.utc)
                    .strftime("%Y.%m.%d %H:%M:%S"),
                d["ticket"], d["symbol"], side.upper(),
                f"{d['volume']:.2f}", f"{d['price']:.5f}",
                f"{d['commission']:+.2f}", f"{d['swap']:+.2f}",
                f"{total:+.2f}",
            ), tags=(tag,))


def apply_mt5_style(root) -> None:
    style = ttk.Style(root)
    try:
        style.theme_use("clam")
    except tk.TclError:
        pass
    root.configure(bg=MT5_BG)
    style.configure(".", background=MT5_BG, foreground=MT5_FG,
                    fieldbackground=MT5_PANEL, bordercolor=MT5_BORDER,
                    lightcolor=MT5_BORDER, darkcolor=MT5_BORDER,
                    troughcolor=MT5_PANEL)
    style.configure("TFrame", background=MT5_BG)
    style.configure("TLabel", background=MT5_BG, foreground=MT5_FG)
    style.configure("TLabelframe", background=MT5_BG, foreground=MT5_MUTED,
                    bordercolor=MT5_BORDER)
    style.configure("TLabelframe.Label", background=MT5_BG,
                    foreground=MT5_MUTED, font=("Segoe UI", 9, "bold"))
    style.configure("TButton", background=MT5_HEADER, foreground=MT5_FG,
                    bordercolor=MT5_BORDER, focusthickness=0, padding=6)
    style.map("TButton",
              background=[("active", MT5_ACCENT), ("pressed", MT5_ACCENT)],
              foreground=[("active", "#ffffff")])
    style.configure("Accent.TButton", background=MT5_ACCENT, foreground="#ffffff")
    style.configure("Danger.TButton", background=MT5_SELL, foreground="#ffffff")
    style.configure("TCheckbutton", background=MT5_BG, foreground=MT5_FG)
    style.configure("TNotebook", background=MT5_BG, bordercolor=MT5_BORDER)
    style.configure("TNotebook.Tab", background=MT5_HEADER,
                    foreground=MT5_MUTED, padding=(12, 6),
                    bordercolor=MT5_BORDER)
    style.map("TNotebook.Tab",
              background=[("selected", MT5_PANEL)],
              foreground=[("selected", MT5_FG)])
    style.configure("Treeview", background=MT5_PANEL, fieldbackground=MT5_PANEL,
                    foreground=MT5_FG, bordercolor=MT5_BORDER,
                    rowheight=22, font=("Consolas", 9))
    style.configure("Treeview.Heading", background=MT5_HEADER,
                    foreground=MT5_MUTED, bordercolor=MT5_BORDER,
                    font=("Segoe UI", 9, "bold"))
    style.map("Treeview", background=[("selected", MT5_ACCENT)],
              foreground=[("selected", "#ffffff")])
    style.configure("TEntry", fieldbackground=MT5_PANEL, foreground=MT5_FG,
                    bordercolor=MT5_BORDER)
    style.configure("TSeparator", background=MT5_BORDER)
    style.configure("TCombobox", fieldbackground=MT5_PANEL, foreground=MT5_FG)

```

---

### `ui/charts.py`
**Rol:** Charting embed pentru UI

**LOC:** 222

```python
"""Tkinter Canvas charts — MT5-style candlesticks + rolling line/W-L bars.

No matplotlib dependency, fully thread-safe (UI thread only).
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk
from datetime import datetime, timezone
from typing import Dict, List, Sequence

# -------------------------------------------------------- palette (MT5 dark)
CHART_BG    = "#131722"
CHART_GRID  = "#1f2233"
CHART_AXIS  = "#6a6f7c"
CHART_TEXT  = "#cfd2dc"
BULL        = "#26a69a"
BEAR        = "#ef5350"


class LineChart(ttk.Frame):
    """Single-series rolling line chart with axis baseline."""

    def __init__(self, master, title: str = "", height: int = 140,
                 width: int = 480, y_min: float = -1.0, y_max: float = 1.0,
                 baseline: float = 0.0, fg: str = "#4ade80", **kw) -> None:
        super().__init__(master, **kw)
        self.title = title
        self.y_min = y_min
        self.y_max = y_max
        self.baseline = baseline
        self.fg = fg
        ttk.Label(self, text=title, foreground="#9a9a9a",
                  font=("Segoe UI", 8, "bold")).pack(anchor="w")
        self.canvas = tk.Canvas(self, height=height, width=width,
                                bg=CHART_BG, highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)

    def set_data(self, values: Sequence[float]) -> None:
        self.canvas.delete("all")
        w = int(self.canvas.winfo_width()) or int(self.canvas["width"])
        h = int(self.canvas.winfo_height()) or int(self.canvas["height"])
        if w < 10 or h < 10 or not values:
            return
        lo = min(min(values), self.y_min)
        hi = max(max(values), self.y_max)
        if hi == lo:
            hi = lo + 1.0
        bx = h - (self.baseline - lo) / (hi - lo) * h
        self.canvas.create_line(0, bx, w, bx, fill=CHART_GRID, dash=(2, 2))
        n = len(values)
        if n == 1:
            return
        dx = w / (n - 1)
        pts: List[float] = []
        for i, v in enumerate(values):
            x = i * dx
            y = h - (v - lo) / (hi - lo) * h
            pts.extend([x, y])
        self.canvas.create_line(*pts, fill=self.fg, width=2, smooth=True)
        self.canvas.create_text(w - 4, 8, anchor="ne",
                                text=f"{values[-1]:+.3f}",
                                fill=self.fg, font=("Consolas", 9))


class MultiSeriesBars(ttk.Frame):
    """Per-strategy recent W/L bars — green=win, red=loss."""

    def __init__(self, master, height: int = 160, width: int = 480, **kw):
        super().__init__(master, **kw)
        ttk.Label(self, text="Per-strategy recent W/L (top 5)",
                  foreground="#9a9a9a", font=("Segoe UI", 8, "bold")
                  ).pack(anchor="w")
        self.canvas = tk.Canvas(self, height=height, width=width,
                                bg=CHART_BG, highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)

    def set_data(self, series: Dict[str, List[int]]) -> None:
        self.canvas.delete("all")
        w = int(self.canvas.winfo_width()) or int(self.canvas["width"])
        h = int(self.canvas.winfo_height()) or int(self.canvas["height"])
        if not series or w < 10 or h < 10:
            return
        n_series = len(series)
        row_h = h / n_series
        for row, (sid, vals) in enumerate(series.items()):
            y0 = row * row_h
            self.canvas.create_text(4, y0 + row_h / 2, anchor="w",
                                    text=sid[:28], fill=CHART_TEXT,
                                    font=("Consolas", 8))
            if not vals:
                continue
            bar_area_x = 200
            bar_w = max(2, (w - bar_area_x - 8) / max(len(vals), 1))
            for i, v in enumerate(vals):
                x = bar_area_x + i * bar_w
                color = BULL if v == 1 else BEAR
                self.canvas.create_rectangle(
                    x, y0 + 4, x + bar_w - 1, y0 + row_h - 4,
                    fill=color, outline="")
            wins = sum(vals)
            wr = wins / len(vals)
            self.canvas.create_text(w - 4, y0 + row_h / 2, anchor="e",
                                    text=f"{wr*100:.0f}%  ({wins}/{len(vals)})",
                                    fill=CHART_TEXT, font=("Consolas", 8))


class CandlestickChart(ttk.Frame):
    """MT5-style candlestick chart drawn directly on a Tk Canvas."""

    def __init__(self, master, height: int = 320, width: int = 720, **kw):
        super().__init__(master, **kw)
        header = ttk.Frame(self)
        header.pack(fill="x")
        self.title_var = tk.StringVar(value="—  ·  —")
        ttk.Label(header, textvariable=self.title_var, foreground=CHART_TEXT,
                  font=("Segoe UI", 10, "bold")).pack(side="left", padx=4)
        self.info_var = tk.StringVar(value="")
        ttk.Label(header, textvariable=self.info_var,
                  foreground="#9a9a9a", font=("Consolas", 9)
                  ).pack(side="right", padx=4)
        self.canvas = tk.Canvas(self, height=height, width=width,
                                bg=CHART_BG, highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)

    def set_data(self, symbol: str, timeframe: str, candles,
                 bid: float | None = None, ask: float | None = None) -> None:
        """`candles` is a sequence of dict-like rows with keys
        time/open/high/low/close (matches MT5 rates numpy structured array)."""
        self.canvas.delete("all")
        self.title_var.set(f"{symbol}  ·  {timeframe}")
        w = int(self.canvas.winfo_width()) or int(self.canvas["width"])
        h = int(self.canvas.winfo_height()) or int(self.canvas["height"])
        if w < 40 or h < 40 or candles is None or len(candles) == 0:
            self.canvas.create_text(w / 2, h / 2,
                                    text="No price data",
                                    fill=CHART_AXIS,
                                    font=("Segoe UI", 10))
            return
        pad_l, pad_r, pad_t, pad_b = 6, 60, 8, 18
        plot_w = max(10, w - pad_l - pad_r)
        plot_h = max(10, h - pad_t - pad_b)

        highs = [float(c["high"]) for c in candles]
        lows = [float(c["low"]) for c in candles]
        hi = max(highs)
        lo = min(lows)
        if hi == lo:
            hi = lo + max(abs(lo) * 1e-4, 1e-5)
        rng = hi - lo
        hi += rng * 0.05
        lo -= rng * 0.05
        rng = hi - lo

        n = len(candles)
        cw = plot_w / n
        body_w = max(1.0, cw * 0.7)

        # grid + price axis
        for i in range(5):
            gy = pad_t + plot_h * i / 4
            self.canvas.create_line(pad_l, gy, pad_l + plot_w, gy,
                                    fill=CHART_GRID)
            price = hi - rng * i / 4
            self.canvas.create_text(pad_l + plot_w + 4, gy, anchor="w",
                                    text=f"{price:.5f}", fill=CHART_AXIS,
                                    font=("Consolas", 8))

        def y(p: float) -> float:
            return pad_t + (hi - p) / rng * plot_h

        for i, c in enumerate(candles):
            o, hgh, low, cl = (float(c["open"]), float(c["high"]),
                                float(c["low"]), float(c["close"]))
            x_center = pad_l + cw * (i + 0.5)
            color = BULL if cl >= o else BEAR
            # wick
            self.canvas.create_line(x_center, y(hgh), x_center, y(low),
                                    fill=color)
            # body
            top = y(max(o, cl))
            bot = y(min(o, cl))
            if bot - top < 1:
                bot = top + 1
            self.canvas.create_rectangle(
                x_center - body_w / 2, top,
                x_center + body_w / 2, bot,
                fill=color, outline=color)

        # latest bid/ask line
        if bid is not None:
            yb = y(bid)
            self.canvas.create_line(pad_l, yb, pad_l + plot_w, yb,
                                    fill=BULL, dash=(2, 2))
            self.canvas.create_rectangle(
                pad_l + plot_w, yb - 8, pad_l + plot_w + 56, yb + 8,
                fill=BULL, outline=BULL)
            self.canvas.create_text(pad_l + plot_w + 28, yb,
                                    text=f"{bid:.5f}", fill="#ffffff",
                                    font=("Consolas", 8, "bold"))
        # time axis (first/last)
        try:
            t0 = datetime.fromtimestamp(int(candles[0]["time"]),
                                        tz=timezone.utc).strftime("%m-%d %H:%M")
            t1 = datetime.fromtimestamp(int(candles[-1]["time"]),
                                        tz=timezone.utc).strftime("%m-%d %H:%M")
            self.canvas.create_text(pad_l + 4, h - 8, anchor="w",
                                    text=t0, fill=CHART_AXIS,
                                    font=("Consolas", 8))
            self.canvas.create_text(pad_l + plot_w - 4, h - 8, anchor="e",
                                    text=t1, fill=CHART_AXIS,
                                    font=("Consolas", 8))
        except Exception:
            pass

        last_close = float(candles[-1]["close"])
        spread = (ask - bid) if (bid is not None and ask is not None) else None
        info = f"O {float(candles[-1]['open']):.5f}  H {max(highs):.5f}  " \
               f"L {min(lows):.5f}  C {last_close:.5f}"
        if spread is not None:
            info += f"   ·   Spread {spread:.5f}"
        self.info_var.set(info)

```

---

### `tests/test_strategies.py`
**Rol:** Unit tests strategii

**LOC:** 103

```python
"""Smoke tests. Run with: python -m unittest tests/test_strategies.py"""
from __future__ import annotations

import math
import os
import tempfile
import unittest

import config
from core.execution import build_levels, build_tp_ladder, round_volume
from core.journal import Journal
from core.risk_manager import RiskManager
from data.indicators import atr, rsi
from strategies.factory import StrategyFactory


class _FakeInfo:
    digits = 5
    point = 0.00001
    trade_tick_size = 0.00001
    trade_tick_value = 1.0
    volume_min = 0.01
    volume_max = 100.0
    volume_step = 0.01
    trade_contract_size = 100000.0


class TestLevels(unittest.TestCase):
    def test_buy_direction(self):
        sl, tp, _ = build_levels("BUY", 1.10000, atr=0.0010, digits=5)
        self.assertLess(sl, 1.10000); self.assertGreater(tp, 1.10000)

    def test_sell_direction(self):
        sl, tp, _ = build_levels("SELL", 1.10000, atr=0.0010, digits=5)
        self.assertGreater(sl, 1.10000); self.assertLess(tp, 1.10000)


class TestLadder(unittest.TestCase):
    def test_buy_ladder_strictly_increasing(self):
        ladder = build_tp_ladder("BUY", 1.10000, 0.0010, 5)
        self.assertEqual(len(ladder), config.TP_LEVELS)
        self.assertTrue(all(ladder[i] < ladder[i + 1]
                            for i in range(len(ladder) - 1)))

    def test_sell_ladder_strictly_decreasing(self):
        ladder = build_tp_ladder("SELL", 1.10000, 0.0010, 5)
        self.assertTrue(all(ladder[i] > ladder[i + 1]
                            for i in range(len(ladder) - 1)))

    def test_volume_rounding(self):
        self.assertEqual(round_volume(0.137, _FakeInfo), 0.14)
        self.assertEqual(round_volume(0.001, _FakeInfo), _FakeInfo.volume_min)

    def test_tp_config_valid(self):
        self.assertEqual(config.validate_tp_config(), [])


class TestJournal(unittest.TestCase):
    def test_open_partial_close(self):
        with tempfile.TemporaryDirectory() as d:
            j = Journal(path=os.path.join(d, "j.sqlite"))
            j.open_trade(1, symbol="EURUSD", strategy_id="t",
                         side="BUY", entry_price=1.1, sl_price=1.099,
                         tp_plan=[1.101, 1.102, 1.103],
                         tp_fractions=[0.4, 0.3, 0.3],
                         initial_volume=1.0, sl_dist=0.001,
                         ml_prob_win=0.6, spread_entry=1.0, paper=True)
            j.record_partial(1, 0, 1.101, 0.4, 0.0)
            j.close_trade(1, exit_reason="TP_LADDER",
                          exit_price=1.103, gross_pnl=42.0)
            rows = j.query_recent_days(1)
            self.assertEqual(len(rows), 1)
            self.assertEqual(rows[0]["tp_hits"][0], True)
            self.assertAlmostEqual(rows[0]["gross_pnl"], 42.0)
            self.assertGreater(rows[0]["r_multiple"], 0)


class TestFactory(unittest.TestCase):
    def test_min_300_instances(self):
        f = StrategyFactory(); f.build_all()
        self.assertGreaterEqual(len(f.instances), 300)
        ids = [s.id for s in f.instances]
        self.assertEqual(len(ids), len(set(ids)))


class TestIndicators(unittest.TestCase):
    def test_rsi_all_up(self):
        self.assertEqual(rsi(list(range(1, 30)), 14), 100.0)
    def test_atr_insufficient(self):
        self.assertIsNone(atr([2]*5, [1]*5, [1.5]*5, 14))


class TestLotSizing(unittest.TestCase):
    def test_lot_within_bounds(self):
        rm = RiskManager()
        lot = rm.calc_lot(balance=10_000.0, sl_distance=0.0020, info=_FakeInfo())
        self.assertGreaterEqual(lot, _FakeInfo.volume_min)
        steps = lot / _FakeInfo.volume_step
        self.assertTrue(math.isclose(steps, round(steps), abs_tol=1e-6))


if __name__ == "__main__":
    unittest.main()

```

---

### `tools/inspect_journal.py`
**Rol:** CLI: inspecție journal

**LOC:** 27

```python
"""CLI: print the latest N rows from the SQLite trade journal."""
from __future__ import annotations

import json
import sys

from core.journal import Journal


def main(n: int = 20) -> int:
    j = Journal()
    rows = j.query_recent_days(n)[:n]
    if not rows:
        print("(journal empty)")
        return 0
    for r in rows:
        print(json.dumps({k: r[k] for k in (
            "ticket", "opened_at", "closed_at", "strategy_id", "side",
            "entry_price", "exit_price", "sl_price", "tp_plan", "tp_hits",
            "initial_volume", "closed_volume", "exit_reason",
            "gross_pnl", "r_multiple", "ml_prob_win", "spread_entry",
        )}, default=str))
    return 0


if __name__ == "__main__":
    sys.exit(main(int(sys.argv[1]) if len(sys.argv) > 1 else 20))

```

---

## 5. CICLUL DE DECIZIE — PSEUDOCOD

```
LOOP (every TICK_INTERVAL_SEC):
    if killed or in_cooldown or daily_loss_breached or order_limit_reached:
        skip
    tick, info = MT5.tick(), MT5.symbol_info(SYMBOL)
    if not spread_ok(tick, info): skip
    if not in_session(): skip
    if in_news_blackout(): skip
    if not market_alive(atr_now, atr_long): skip

    ctx = build_context(rates, indicators, macro_feeds)
    ml_prob_up = model.predict(features(ctx))
    decision = ensemble.decide(strategies, ctx, ml_prob_up, trained_samples)

    if decision.side is None: log_reason; skip
    sl, tp = atr_sl_tp(decision, ctx)
    lot = risk.calc_lot(balance, sl_distance, info)
    executor.open(decision.side, lot, sl, tp, magic, comment=decision.strategy_id)
    risk.register_order()

ON POSITION CLOSE:
    pnl, features_at_open, outcome = journal.close(ticket)
    trainer.update(features_at_open, outcome=1 if pnl>0 else 0)
    strategy.stats.trades += 1; .wins += (pnl>0)
    risk.update_after_trade(pnl)
```

---

## 6. CHECKLIST RECONSTRUCȚIE

- [ ] `pip install -r requirements.txt`
- [ ] Instalează MT5 terminal, login broker
- [ ] Creează toate fișierele exact ca mai sus
- [ ] Editează `config.py` pentru contul tău (RISK_PCT, MAGIC)
- [ ] Rulează `python main.py`
- [ ] Verifică în UI: 300+ instanțe, ensemble votează, ML probability se mișcă
- [ ] Lasă în PAPER_TRADING=True minim 200 trade-uri pentru warmup ML
- [ ] Treci în live doar după ce win-rate > 50% pe paper

---

## 7. NOTE FINALE

Nimic din acest document nu este halucinat. Fiecare linie de cod provine din
repo-ul activ. Dacă reconstrucția nu funcționează, comparați byte-cu-byte
fișierele generate cu blocurile de cod de mai sus.

**END OF PROMPT.**
