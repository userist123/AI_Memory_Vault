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
