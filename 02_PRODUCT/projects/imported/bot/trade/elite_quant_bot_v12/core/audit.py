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
