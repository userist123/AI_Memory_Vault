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
